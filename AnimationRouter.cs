using System;
using System.Collections.Generic;

namespace EliteBioRadar
{
    // Where an animation may be shown: the Elite Bio Radar window, the System Notifications window, both, or neither (off).
    [Flags]
    public enum AnimationSurfaces { None = 0, Main = 1, Notifications = 2, Both = Main | Notifications }

    // One entry per animation the app can play. Group is the heading in the settings list; BackdropOpacity is how much of whatever lies
    // underneath is hidden while it plays (1 = fully covered).
    // PreviewSubject/PreviewDetail are the sample text the Notification Settings window's Play button uses.
    // PreviewCycle = the details Play steps through, one per press, for animations with several modes (gear down / up); null = always PreviewDetail.
    // Live = a screen driven continuously by the ship's state (the atmosphere scenes) rather than a one-shot played through the router:
    // MainWindow shows it in the windows ticked here for as long as the phase lasts, so it has no Play preview.
    public sealed record AnimationDef(string Id, string Name, string Group, AnimationSurfaces DefaultSurfaces, double BackdropOpacity,
                                      string PreviewSubject, string PreviewDetail, bool Live = false, string[]? PreviewCycle = null);

    public static class AnimationCatalog
    {
        public static readonly AnimationDef FirstDiscovery =
            new("first-discovery", "First discovery", "Discovery", AnimationSurfaces.Main, 1.0, "Sample System", "M-Type");

        // Detail = "LABEL=value|LABEL=value": the stats line each of these shows under its title.
        public static readonly AnimationDef Landed =
            new("landed", "Landed on a planet", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "GRAVITY=0.89 G|SURFACE=214 K|ATMOSPHERE=None|~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef Liftoff =
            new("liftoff", "Lift-off from a planet", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "GRAVITY=0.89 G|ATMOSPHERE=None|~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef FirstFootfall =
            new("first-footfall", "First footfall on a world", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef HyperspaceJump =
            new("hyperspace-jump", "Hyperspace jump (departure)", "Jump", AnimationSurfaces.Main, 0.92, "Sample System", "PREVIEW");
        public static readonly AnimationDef Supercruise =
            new("supercruise", "Entering supercruise", "Jump", AnimationSurfaces.Both, 0.85, "", "PREVIEW");
        public static readonly AnimationDef SupercruiseOverdrive =
            new("supercruise-overdrive", "Supercruise overdrive engaged / disengaged", "Jump", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef SupercruiseAssist =
            new("supercruise-assist", "Supercruise assist engaged / disengaged", "Jump", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef SupercruiseExit =
            new("supercruise-exit", "Exiting supercruise", "Jump", AnimationSurfaces.Both, 0.85, "", "PREVIEW");
        public static readonly AnimationDef Supercharge =
            new("supercharge", "FSD supercharge (neutron star / white dwarf)", "Jump", AnimationSurfaces.Both, 0.85, "", "NEUTRON|4", PreviewCycle: new[] { "NEUTRON|4", "WHITE DWARF|1.5" });
        public static readonly AnimationDef Route =
            new("route", "Route plotted / cancelled / complete", "Jump", AnimationSurfaces.Both, 0.85, "", "PLOTTED|12|184|KMFKMAFK", PreviewCycle: new[] { "PLOTTED|12|184|KMFKMAFK", "CANCELLED|12|184|KMFKMAFK", "COMPLETE|12|184|KMFKMAFK" });
        public static readonly AnimationDef FssComplete =
            new("fss-complete", "FSS complete (all bodies found)", "Exploration", AnimationSurfaces.Both, 0.85, "Sample System", "14");
        public static readonly AnimationDef CodexEntry =
            new("codex-entry", "New codex entry", "Exploration", AnimationSurfaces.Both, 0.85, "Fonticulua Digitos", "BIOLOGICAL AND GEOLOGICAL|FLORA");
        public static readonly AnimationDef OrganismScan =
            new("organism-scan", "Organism sample 1 / 2 / 3 / genus complete", "Biology", AnimationSurfaces.Both, 0.85, "Bacterium Aurasus", "1", PreviewCycle: new[] { "1", "2", "3", "4" });
        public static readonly AnimationDef FuelScoop =
            new("fuel-scoop", "Fuel scooping", "Jump", AnimationSurfaces.Main, 0.92, "Sample Star", "PREVIEW|TYPE=K-Star|~STAR=4500");
        public static readonly AnimationDef CombatMode =
            new("combat-mode", "Combat mode", "HUD", AnimationSurfaces.Main, 0.85, "", "");
        public static readonly AnimationDef LandingGear =
            new("landing-gear", "Landing gear deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "DOWN", PreviewCycle: new[] { "DOWN", "UP" });
        public static readonly AnimationDef Hardpoints =
            new("hardpoints", "Hardpoints deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "OUT", PreviewCycle: new[] { "OUT", "IN" });
        public static readonly AnimationDef CargoScoop =
            new("cargo-scoop", "Cargo scoop deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "OUT", PreviewCycle: new[] { "OUT", "IN" });
        public static readonly AnimationDef Headlights =
            new("headlights", "Headlights on / high beam / off", "HUD", AnimationSurfaces.Both, 0.85, "", "HIGH", PreviewCycle: new[] { "ON", "HIGH", "OFF" });
        public static readonly AnimationDef NightVision =
            new("night-vision", "Night vision activated / deactivated", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Shields =
            new("shields", "Shields up / down", "HUD", AnimationSurfaces.Both, 0.85, "", "UP", PreviewCycle: new[] { "UP", "DOWN" });
        public static readonly AnimationDef LowFuel =
            new("low-fuel", "Low fuel / fuel critical", "HUD", AnimationSurfaces.Both, 0.85, "", "LOW", PreviewCycle: new[] { "LOW", "CRITICAL" });
        public static readonly AnimationDef Overheat =
            new("overheat", "Overheating", "HUD", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef HullDamage =
            new("hull-damage", "Hull damaged / critical", "HUD", AnimationSurfaces.Both, 0.85, "", "DAMAGED", PreviewCycle: new[] { "DAMAGED", "CRITICAL" });
        public static readonly AnimationDef ShipLost =
            new("ship-lost", "Ship destroyed / respawn", "HUD", AnimationSurfaces.Both, 0.9, "", "DESTROYED|Cobra Mk III|KILLED BY  Thargoid Interceptor", PreviewCycle: new[] { "DESTROYED|Cobra Mk III|KILLED BY  Thargoid Interceptor", "RESTORED|Cobra Mk III|REBUY PAID  3,420,000 CR" });
        public static readonly AnimationDef Fighter =
            new("fighter", "Fighter launched / recovered", "HUD", AnimationSurfaces.Both, 0.85, "", "LAUNCH|YOU", PreviewCycle: new[] { "LAUNCH|YOU", "RECOVER" });
        public static readonly AnimationDef MassLock =
            new("mass-lock", "Mass locked / mass lock cleared", "HUD", AnimationSurfaces.Both, 0.85, "", "LOCKED", PreviewCycle: new[] { "LOCKED", "CLEARED" });
        public static readonly AnimationDef FsdCooldown =
            new("fsd-cooldown", "FSD cooling down / ready", "HUD", AnimationSurfaces.Both, 0.85, "", "COOLING", PreviewCycle: new[] { "COOLING", "READY" });
        public static readonly AnimationDef Interdiction =
            new("interdiction", "Being interdicted", "HUD", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef SilentRunning =
            new("silent-running", "Silent running on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef DriveAssist =
            new("drive-assist", "SRV drive assist on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Handbrake =
            new("handbrake", "SRV handbrake on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Turret =
            new("turret", "SRV turret deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef FlightAssist =
            new("flight-assist", "Flight assist on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "OFF", PreviewCycle: new[] { "OFF", "ON" });
        public static readonly AnimationDef Disembark =
            new("disembark", "Disembark from a vehicle", "Surface", AnimationSurfaces.Both, 0.85, "", "SHIP");
        public static readonly AnimationDef Srv =
            new("srv", "SRV deployed / recovered", "Surface", AnimationSurfaces.Both, 0.85, "SRV Scarab", "DEPLOYED", PreviewCycle: new[] { "DEPLOYED", "RECOVERED" });
        public static readonly AnimationDef Embark =
            new("embark", "Board a vehicle", "Surface", AnimationSurfaces.Both, 0.85, "", "SHIP");
        public static readonly AnimationDef AnalysisMode =
            new("analysis-mode", "Analysis mode", "HUD", AnimationSurfaces.Main, 0.85, "", "");
        public static readonly AnimationDef Arrival =
            new("arrival", "Arrival in a system", "Jump", AnimationSurfaces.Main, 1.0, "Sample System", "TYPE=K-Star|~STAR=4500");
        public static readonly AnimationDef DockingRequest =
            new("docking-request", "Docking requested / granted / denied / undocked", "Docking", AnimationSurfaces.Both, 0.85, "Jameson Memorial", "REQUESTED", PreviewCycle: new[] { "REQUESTED", "GRANTED|17", "DENIED|NO SPACE", "UNDOCKED" });
        public static readonly AnimationDef Docked =
            new("docked", "Docked at a station or carrier", "Docking", AnimationSurfaces.Main, 1.0, "Sample Station", "TYPE=Coriolis|ECONOMY=Refinery|DISTANCE=432 ls");

        public static readonly AnimationDef Descent =
            new("descent", "Descent to a planet", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);
        public static readonly AnimationDef Climb =
            new("climb", "Climb out (launch / ascent)", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);
        public static readonly AnimationDef AtmosphericJump =
            new("atmospheric-jump", "Hyperspace jump from an atmosphere", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);

        public static readonly IReadOnlyList<AnimationDef> All = new[] { FirstDiscovery, HyperspaceJump, Arrival, Supercruise, SupercruiseOverdrive, SupercruiseAssist, SupercruiseExit, Supercharge, Route, FuelScoop, CombatMode, AnalysisMode, LandingGear, Hardpoints, CargoScoop, Headlights, NightVision, Shields, LowFuel, Overheat, HullDamage, ShipLost, Fighter, MassLock, FsdCooldown, Interdiction, SilentRunning, DriveAssist, Handbrake, Turret, FlightAssist, Disembark, Embark, Srv, Landed, Liftoff, FirstFootfall, DockingRequest, Docked, OrganismScan, FssComplete, CodexEntry, Descent, Climb, AtmosphericJump };

        // The user's choice from the settings file, else the animation's own default.
        public static AnimationSurfaces SurfacesFor(AnimationDef def, AppSettingsData settings) =>
            settings.AnimationTargets.TryGetValue(def.Id, out var v) ? (AnimationSurfaces)v : def.DefaultSurfaces;
    }

    // What to play and what to say. Subject/Detail are the animation's own text (First discovery: system name, star class).
    public sealed record AnimationRequest(AnimationDef Def, string Subject, string Detail)
    {
        public DateTime At { get; } = DateTime.UtcNow;
    }

    // A place that can play animations. Play must call done exactly once when the animation has finished and the surface is clear again.
    public interface IAnimationSurface
    {
        AnimationSurfaces Kind { get; }
        bool IsAvailable { get; }
        void Play(AnimationRequest request, Action done);
    }

    // Events are detected once, then handed to Raise. The router looks up where that animation is switched on and gives it to each of those
    // surfaces. Every surface plays one animation at a time and queues the rest; a request still waiting after MaxWait is dropped, since
    // a stale notification (a system the commander has already left) is worse than none. All calls are on the UI thread.
    public static class AnimationRouter
    {
        private const double MaxWaitSeconds = 10;

        private sealed class Lane
        {
            public IAnimationSurface Surface = null!;
            public readonly Queue<AnimationRequest> Queue = new();
            public bool Busy;
        }

        private static readonly Dictionary<AnimationSurfaces, Lane> _lanes = new();

        public static void Register(IAnimationSurface surface) => _lanes[surface.Kind] = new Lane { Surface = surface };

        public static void Unregister(IAnimationSurface surface)
        {
            if (_lanes.TryGetValue(surface.Kind, out var lane) && ReferenceEquals(lane.Surface, surface)) _lanes.Remove(surface.Kind);
        }

        public static void Raise(AnimationRequest request)
        {
            var targets = AnimationCatalog.SurfacesFor(request.Def, AppSettings.Load());
            foreach (var kv in _lanes)
            {
                if ((targets & kv.Key) == 0) continue;
                kv.Value.Queue.Enqueue(request);
                Pump(kv.Value);
            }
        }

        private static void Pump(Lane lane)
        {
            if (lane.Busy) return;
            while (lane.Queue.Count > 0)
            {
                var req = lane.Queue.Dequeue();
                if ((DateTime.UtcNow - req.At).TotalSeconds > MaxWaitSeconds)
                {
                    Log.Write($"Animation '{req.Def.Id}' dropped on {lane.Surface.Kind}: waited too long");
                    continue;
                }
                if (!lane.Surface.IsAvailable)
                {
                    Log.Write($"Animation '{req.Def.Id}' skipped on {lane.Surface.Kind}: surface not available");
                    continue;
                }
                lane.Busy = true;
                lane.Surface.Play(req, () => { lane.Busy = false; Pump(lane); });
                return;
            }
        }
    }
}
