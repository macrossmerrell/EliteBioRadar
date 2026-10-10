using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // How a ship event should read on screen. The colours are chosen in the UI (ShipEventChip); the tone is only the meaning.
    public enum ShipEventTone { On, Off, Move, Caution, Danger, ColdMild, ColdSevere, HotMild, HotSevere }

    // One thing that happened to the ship, SRV or commander: a short line for the Radar chip today, and the event an animation
    // screen can subscribe to later (Key is a stable id such as "gear.on" to pick an animation by).
    public sealed record ShipEvent(string Key, string Text, ShipEventTone Tone, DateTime At);

    // A condition that stays on screen for as long as it lasts (on-foot overheating, freezing, low oxygen or health), with its live reading in
    // the text. Unlike a ShipEvent it is not a moment but a state; the Radar chip shows it whenever no event is playing.
    public sealed record ShipHazard(string Key, string Text, ShipEventTone Tone);

    // Turns the Status.json flag bits and a few journal events into ShipEvents. Everything is edge-triggered: the first status seen
    // (or the first one after switching between ship, SRV and on foot, where the bits change meaning) only sets the baseline, so
    // starting the app or boarding the SRV never announces the current state of the switches. On-foot hazards are the exception:
    // they are states, recomputed from every status, so one already in force when you step out shows at once.
    public sealed class ShipEventDetector
    {
        public event Action<ShipEvent>? Raised;

        // Raised (on the status thread) whenever the set of active hazards, or the live reading in one of them, changes.
        public event Action<IReadOnlyList<ShipHazard>>? HazardsChanged;

        private enum Ctx { None, Ship, Srv, Foot, Other }

        [Flags]
        private enum Where { Ship = 1, Srv = 2, Both = 3, Any = 7 }

        // flags word, bit, contexts it applies to, key, text when the bit turns on, tone, text when it turns off (null = silent), tone
        private readonly record struct Watch(int Bit, Where Where, string Key, string OnText, ShipEventTone OnTone, string? OffText, ShipEventTone OffTone);

        private static readonly Watch[] Watches =
        {
            new(2,  Where.Ship, "gear",         "Landing Gear Deployed",  ShipEventTone.Caution, "Landing Gear Retracted",   ShipEventTone.Off),
            new(8,  Where.Both, "lights",       "Headlights On",          ShipEventTone.On,      "Headlights Off",           ShipEventTone.Off),
            new(31, Where.Srv,  "highbeam",     "High Beam On",           ShipEventTone.On,      "High Beam Off",            ShipEventTone.Off),
            new(9,  Where.Ship, "scoop",        "Cargo Scoop Activated",  ShipEventTone.Caution, "Cargo Scoop Retracted",    ShipEventTone.Off),
            new(28, Where.Any,  "nightvision",  "Night Vision Activated", ShipEventTone.On,      "Night Vision Deactivated", ShipEventTone.Off),
            new(15, Where.Srv,  "driveassist",  "Drive Assist On",        ShipEventTone.On,      "Drive Assist Off",         ShipEventTone.Off),
            new(12, Where.Srv,  "brake",        "Brake On",               ShipEventTone.On,      "Brake Off",                ShipEventTone.Off),
            new(6,  Where.Ship, "hardpoints",   "Hardpoints Deployed",    ShipEventTone.Caution, "Hardpoints Retracted",     ShipEventTone.Off),
            new(5,  Where.Ship, "flightassist", "Flight Assist Off",      ShipEventTone.Caution, "Flight Assist On",         ShipEventTone.On),
            new(10, Where.Ship, "silent",       "Silent Running On",      ShipEventTone.On,      "Silent Running Off",       ShipEventTone.Off),
            new(11, Where.Ship, "fuelscoop",    "Fuel Scoop Active",      ShipEventTone.On,      "Fuel Scoop Finished",      ShipEventTone.Off),
            new(3,  Where.Ship, "shields",      "Shields Up",             ShipEventTone.On,      "Shields Down",             ShipEventTone.Danger),
            new(0,  Where.Ship, "docked",       "Docked",                 ShipEventTone.Move,    "Undocked",                 ShipEventTone.Move),
            new(19, Where.Ship, "lowfuel",      "Low Fuel",               ShipEventTone.Danger,  null,                       ShipEventTone.Off),
            new(20, Where.Ship, "overheat",     "Overheating",            ShipEventTone.Danger,  null,                       ShipEventTone.Off),
            new(23, Where.Ship, "interdicted",  "Being Interdicted",      ShipEventTone.Danger,  null,                       ShipEventTone.Off),
        };

        // Smallest drop in Health (0..1) that counts as an injury; and how long to wait before announcing another, so a steady burn cycles
        // with the hazard chip instead of burying it.
        private const double InjuryDrop = 0.005;
        private const double InjuryGapSeconds = 5.0;

        private bool _haveBaseline;
        private Ctx _ctx = Ctx.None;
        private uint _flags;
        private DateTime _quietUntil = DateTime.MinValue;
        private double _lastHealth;
        private string _hazardSignature = "";
        private bool _hadTemperatureHazard, _hadOxygenHazard;
        private readonly Dictionary<string, DateTime> _lastRaised = new();

        private static Ctx ContextOf(EliteStatus s) =>
            s.OnFoot ? Ctx.Foot
            : s.InSRV ? Ctx.Srv
            : (s.Flags & (1u << 24)) != 0 ? Ctx.Ship
            : Ctx.Other;   // fighter, taxi, multicrew seat: no announcements

        public void ObserveStatus(EliteStatus s)
        {
            var ctx = ContextOf(s);
            bool newContext = !_haveBaseline || ctx != _ctx;
            uint prev = _flags;
            if (newContext)
            {
                // The flags change meaning between ship, SRV and on foot: re-baseline silently and stay quiet for a moment.
                _haveBaseline = true;
                _ctx = ctx;
                _quietUntil = DateTime.UtcNow.AddSeconds(1.5);
                _lastHealth = ctx == Ctx.Foot ? s.Health : 0;
            }
            _flags = s.Flags;

            UpdateHazards(s, ctx);

            if (newContext || DateTime.UtcNow < _quietUntil) return;
            switch (ctx)
            {
                case Ctx.Ship: Diff(Watches, prev, s.Flags, Where.Ship); break;
                case Ctx.Srv: Diff(Watches, prev, s.Flags, Where.Srv); break;
                case Ctx.Foot:
                    Diff(Watches, prev, s.Flags, Where.Any, nightVisionOnly: true);   // night vision works on foot too
                    ObserveHealth(s);
                    break;
            }
        }

        // Injury: the suit's health dropped since the last status. Throttled well above the chip's hold time so it alternates with the hazard.
        private void ObserveHealth(EliteStatus s)
        {
            if (s.Health > 0 && _lastHealth > 0 && s.Health < _lastHealth - InjuryDrop)
                Raise("injury", $"Injury Detected ({s.Health:P0})", ShipEventTone.Danger, InjuryGapSeconds);
            if (s.Health > 0) _lastHealth = s.Health;
        }

        // The on-foot hazards (Flags2 bits 6-11), recomputed from every status so the text carries the live reading. Only the worse of
        // hot/very hot and cold/very cold is listed. Cleared hazards raise a short "back to normal" event.
        private void UpdateHazards(EliteStatus s, Ctx ctx)
        {
            var list = new List<ShipHazard>();
            if (ctx == Ctx.Foot)
            {
                bool veryHot = (s.Flags2 & (1u << 11)) != 0, hot = (s.Flags2 & (1u << 9)) != 0;
                bool veryCold = (s.Flags2 & (1u << 10)) != 0, cold = (s.Flags2 & (1u << 8)) != 0;
                string temp = s.SuitTemperatureK > 0 ? $" ({s.SuitTemperatureK:N0} K)" : "";
                if (veryHot)       list.Add(new ShipHazard("veryhot", "OVERHEATING - Get Out" + temp, ShipEventTone.HotSevere));
                else if (hot)      list.Add(new ShipHazard("hot", "Getting Hot" + temp, ShipEventTone.HotMild));
                if (veryCold)      list.Add(new ShipHazard("verycold", "FREEZING - Suit Failing" + temp, ShipEventTone.ColdSevere));
                else if (cold)     list.Add(new ShipHazard("cold", "Getting Cold" + temp, ShipEventTone.ColdMild));
                if ((s.Flags2 & (1u << 6)) != 0) list.Add(new ShipHazard("lowoxygen", $"LOW OXYGEN ({s.Oxygen:P0})", ShipEventTone.Danger));
                if ((s.Flags2 & (1u << 7)) != 0) list.Add(new ShipHazard("lowhealth", $"LOW HEALTH ({s.Health:P0})", ShipEventTone.Danger));
            }

            bool hasTemp = list.Any(h => h.Key is "hot" or "veryhot" or "cold" or "verycold");
            bool hasOxygen = list.Any(h => h.Key == "lowoxygen");
            bool quiet = DateTime.UtcNow < _quietUntil;
            if (_hadTemperatureHazard && !hasTemp && ctx == Ctx.Foot && !quiet) Raise("temperature.normal", "Temperature Normal", ShipEventTone.Off);
            if (_hadOxygenHazard && !hasOxygen && ctx == Ctx.Foot && !quiet) Raise("oxygen.normal", "Oxygen Normal", ShipEventTone.Off);
            _hadTemperatureHazard = hasTemp; _hadOxygenHazard = hasOxygen;

            string sig = string.Join("|", list.Select(h => h.Key + ":" + h.Text));
            if (sig == _hazardSignature) return;
            _hazardSignature = sig;
            HazardsChanged?.Invoke(list);
        }

        private void Diff(Watch[] table, uint before, uint after, Where here, bool nightVisionOnly = false)
        {
            foreach (var w in table)
            {
                if (nightVisionOnly ? w.Key != "nightvision" : (w.Where & here) == 0) continue;
                bool was = (before & (1u << w.Bit)) != 0, now = (after & (1u << w.Bit)) != 0;
                if (was == now) continue;
                if (now) Raise($"{w.Key}.on", w.OnText, w.OnTone);
                else if (w.OffText != null) Raise($"{w.Key}.off", w.OffText, w.OffTone);
            }
        }

        // Live journal lines only (the caller skips the startup replay).
        public void ObserveJournal(string evt, JObject obj)
        {
            switch (evt)
            {
                case "Liftoff":   Raise("liftoff", "Liftoff", ShipEventTone.Move); break;
                case "Touchdown": Raise("touchdown", "Ship Landed", ShipEventTone.Move); break;
                case "LaunchSRV": Raise("srv.launch", "SRV Launched", ShipEventTone.Move); break;
                case "DockSRV":   Raise("srv.dock", "SRV Docked", ShipEventTone.Move); break;
                case "Embark":
                    if (obj.Value<bool?>("SRV") == true) Raise("board.srv", "Boarding SRV", ShipEventTone.Move);
                    else Raise("board.ship", "Boarding Ship", ShipEventTone.Move);
                    break;
                case "Disembark":
                    if (obj.Value<bool?>("SRV") == true) Raise("leave.srv", "Leaving SRV", ShipEventTone.Move);
                    else Raise("disembark", "Disembarking", ShipEventTone.Move);
                    break;
            }
        }

        private void Raise(string key, string text, ShipEventTone tone, double minGapSeconds = 1.0)
        {
            var now = DateTime.UtcNow;
            // Status polling and the journal reader run on different threads, hence the lock.
            lock (_lastRaised)
            {
                // The same event twice within a moment (a flag poll and its journal twin, a flickering bit) is one event.
                if (_lastRaised.TryGetValue(key, out var last) && (now - last).TotalSeconds < minGapSeconds) return;
                _lastRaised[key] = now;
            }
            Raised?.Invoke(new ShipEvent(key, text, tone, now));   // raised on the calling thread; subscribers marshal to the UI themselves
        }
    }
}
