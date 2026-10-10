using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EliteBioRadar
{
    // The Radar tab's status chip: shows each ShipEvent for a few seconds at the top centre of the radar, one after another. Bursts
    // (boarding then retracting the gear) queue and shorten their holds so the chip keeps up. The chip is only a subscriber of
    // ShipEventDetector.Raised, so an animation screen can later listen to the same events.
    public partial class MainWindow
    {
        private readonly Queue<ShipEvent> _chipQueue = new();
        private bool _chipBusy;

        private void InitShipEventChip(EliteWatcherService svc)
        {
            // Raised on the status/journal threads.
            svc.ShipEvents.Raised += ev => Dispatcher.InvokeAsync(() => EnqueueShipEvent(ev));
            svc.ShipEvents.HazardsChanged += hz => Dispatcher.InvokeAsync(() => SetHazards(hz));
        }

        private void EnqueueShipEvent(ShipEvent ev)
        {
            while (_chipQueue.Count >= 4) _chipQueue.Dequeue();   // never fall far behind
            _chipQueue.Enqueue(ev);
            if (!_chipBusy) ShowNextShipEvent();
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        // glyph, border, text, background: brightness separates on/off, the hue is kept for things that need attention.
        private static (string glyph, SolidColorBrush border, SolidColorBrush text, SolidColorBrush bg) ChipStyle(ShipEventTone tone) => tone switch
        {
            ShipEventTone.On      => ("●", Frozen("#00e5ff"), Frozen("#e6fbff"), Frozen("#e0062a30")),
            ShipEventTone.Off     => ("○", Frozen("#4f7a85"), Frozen("#8fb0b8"), Frozen("#e00c1a1c")),
            ShipEventTone.Caution => ("▲", Frozen("#ffb347"), Frozen("#ffd48a"), Frozen("#e02b1d08")),
            ShipEventTone.Danger  => ("!", Frozen("#ff5c5c"), Frozen("#ffb0b0"), Frozen("#e02c0c0c")),
            // Cold runs through blues: a light ice blue for "getting cold", a deeper saturated blue for the dangerous level.
            ShipEventTone.ColdMild   => ("❄", Frozen("#8fd6ff"), Frozen("#d9f2ff"), Frozen("#e00a2230")),
            ShipEventTone.ColdSevere => ("❄", Frozen("#4f7dff"), Frozen("#a9bfff"), Frozen("#e0101a46")),
            // Heat runs from a warm yellow-amber for "getting hot" to a deep fiery red for the dangerous level.
            ShipEventTone.HotMild    => ("▲", Frozen("#ffd166"), Frozen("#fff0c4"), Frozen("#e02a2208")),
            ShipEventTone.HotSevere  => ("▲", Frozen("#ff3b2a"), Frozen("#ffb3a6"), Frozen("#e0360a06")),
            _                     => ("▸", Frozen("#cfe8e8"), Frozen("#ffffff"), Frozen("#e0152426")),
        };

        // ---- Persistent hazards (on-foot overheating, freezing, low oxygen/health, with live readings) ----
        // Shown whenever no event is playing, and they stay until the hazard is gone. With more than one, they take turns on the same
        // timer as the events; a new event plays its usual short show and the chip then returns to the hazard.
        private IReadOnlyList<ShipHazard> _hazards = Array.Empty<ShipHazard>();
        private int _hazardIndex;
        private bool _hazardShowing;
        private System.Windows.Threading.DispatcherTimer? _hazardTimer;

        private void SetHazards(IReadOnlyList<ShipHazard> hazards)
        {
            _hazards = hazards;
            if (!_chipBusy) RefreshHazardChip();   // while an event plays, its end brings the hazard back
        }

        private void RefreshHazardChip()
        {
            if (_chipBusy) return;
            bool layerVisible = shipEventLayer.Visibility == Visibility.Visible;
            if (_hazards.Count == 0 || !layerVisible)
            {
                _hazardTimer?.Stop();
                if (_hazardShowing)
                {
                    _hazardShowing = false;
                    shipEventChip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.35)));
                }
                return;
            }

            if (_hazardIndex >= _hazards.Count) _hazardIndex = 0;
            var h = _hazards[_hazardIndex];
            var (glyph, border, text, bg) = ChipStyle(h.Tone);
            shipEventChip.BorderBrush = border;
            shipEventChip.Background = bg;
            shipEventChipGlyph.Text = glyph;
            shipEventChipGlyph.Foreground = border;
            shipEventChipText.Text = h.Text.ToUpperInvariant();   // live: updated every time the reading changes
            shipEventChipText.Foreground = text;
            if (!_hazardShowing)
            {
                _hazardShowing = true;
                shipEventChip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.15)));
            }

            if (_hazards.Count > 1)
            {
                if (_hazardTimer == null)
                {
                    _hazardTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
                    _hazardTimer.Tick += (_, _) => { _hazardIndex++; RefreshHazardChip(); };
                }
                if (!_hazardTimer.IsEnabled) _hazardTimer.Start();
            }
            else _hazardTimer?.Stop();
        }

        private void ShowNextShipEvent()
        {
            // Drop anything stale, and anything that fires while another tab is showing (nobody would see it).
            while (_chipQueue.Count > 0 && (DateTime.UtcNow - _chipQueue.Peek().At).TotalSeconds > 8)
                _chipQueue.Dequeue();
            if (_chipQueue.Count == 0 || shipEventLayer.Visibility != Visibility.Visible)
            {
                _chipQueue.Clear();
                _chipBusy = false;
                RefreshHazardChip();
                return;
            }

            _chipBusy = true;
            double startOpacity = _hazardShowing ? 1 : 0;
            _hazardShowing = false;
            _hazardTimer?.Stop();
            var ev = _chipQueue.Dequeue();
            var (glyph, border, text, bg) = ChipStyle(ev.Tone);
            shipEventChip.BorderBrush = border;
            shipEventChip.Background = bg;
            shipEventChipGlyph.Text = glyph;
            shipEventChipGlyph.Foreground = border;
            shipEventChipText.Text = ev.Text.ToUpperInvariant();
            shipEventChipText.Foreground = text;

            double hold = _chipQueue.Count > 0 ? 1.1 : 2.6;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(startOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15 + hold))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15 + hold + 0.35))));
            anim.Completed += (_, _) => ShowNextShipEvent();
            shipEventChip.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
