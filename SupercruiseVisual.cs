using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // What the supercruise screen needs to know about the charge in progress, shared by every window showing it. Set from the journal watcher's events (see MainWindow.InitAnimations).
    public static class SupercruiseState
    {
        public static DateTime ChargeStart = DateTime.MinValue;   // the FSD began charging for supercruise
        public static DateTime EnteredAt = DateTime.MinValue;     // the journal's SupercruiseEntry: the drive engaged (after ChargeStart for the charge in progress)
        public static DateTime LastEnd = DateTime.MinValue;       // when the charge was last cancelled
        public static DateTime DriveFiredAt = DateTime.MinValue;  // the journal's StartJump: the spool-up is done and the game's countdown to the entry begins (after ChargeStart)
        public static double ChargeSeconds = 4.5;                 // how long the spool-up (charge start to StartJump) took last time: paces the ring until the next one is measured
        public static event Action? EndRequested;                 // the charge was cancelled
        public static void RequestEnd() { LastEnd = DateTime.UtcNow; EndRequested?.Invoke(); }
    }

    // Entering supercruise, for a charge made in space. It starts when the FSD starts charging: the ship from behind with its engines building, a ring closing round it, the stars
    // beginning to drift. When the drive engages (the journal's SupercruiseEntry) the ring pulses out and the stars streak away from the ship faster and longer as it accelerates, then
    // it holds at cruise speed. A charge that is cancelled just fades out. Subject = unused; Detail "PREVIEW" runs a scripted charge, countdown and entry, "ENTRY" skips the charge.
    public sealed class SupercruiseVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50, FadeSeconds = 0.6, HoldSeconds = 3.0, GiveUpSeconds = 90, PreviewChargeSeconds = 3.5, PreviewCountdownSeconds = 3.0;
        private const int Streaks = 54;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Brush CyanBrush = Frozen(Cyan), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(204, 5, 10, 20)), HullBrush = Frozen(Color.FromRgb(0x2a, 0x3a, 0x46)), CoreBrush = Frozen(Color.FromRgb(255, 236, 200));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(128, Cyan.R, Cyan.G, Cyan.B), 1), HullEdge = FrozenPen(Cyan, 0.87), NozzleRing = FrozenPen(Cyan, 0.87), ArcPen = FrozenPen(Cyan, 3);
        private static readonly Pen[] CyanStreak = AlphaPens(Cyan, 1.2), WhiteStreak = AlphaPens(Color.FromRgb(235, 250, 255), 1.6), Ring = AlphaPens(Cyan, 1.6);
        // The ship seen from behind: RearHull, two swept wings, a RearFin and three engine nozzles. Glows come in nine levels (the RearHull glow swells with the drive, the nozzles flare).
        private static readonly Brush[] HullGlow = GlowLevels(Cyan, 77, 166), NozzleGlow = GlowLevels(Color.FromRgb(255, 217, 160), 128, 242);
        // Where each streak sits: its angle, its distance from the centre when it starts, its length factor and its starting phase (all fixed, so it is the same every time).
        private static readonly double[] Angle = StarValues(Streaks, 0, 2 * Math.PI), Start = StarValues(Streaks, 1, 1), Length = StarValues(Streaks, 2, 1), Offset = StarValues(Streaks, 3, 1);

        private readonly Dictionary<int, Geometry> _arcs = new();
        private bool _preview, _entryOnly;
        private double _endAt = -1, _flow, _lastT;

        public SupercruiseVisual()
        {
            Loaded += (_, _) => SupercruiseState.EndRequested += OnEndRequested;
            Unloaded += (_, _) => SupercruiseState.EndRequested -= OnEndRequested;
        }

        private void OnEndRequested() { if (_endAt < 0) _endAt = Elapsed; }

        // Open-ended while charging (the charge takes as long as it takes); after the drive engages it holds a few seconds, then fades. Gives up if the game never says what became of the charge.
        protected override double Duration => _endAt >= 0 ? _endAt + FadeSeconds : !_preview && !_entryOnly && Elapsed > GiveUpSeconds ? 0 : double.MaxValue;

        protected override void OnStart(string subject, string detail)
        {
            _preview = detail == "PREVIEW";
            _entryOnly = detail == "ENTRY";
            _endAt = -1; _flow = 0; _lastT = 0;
            // A charge that was cancelled before this window started it must not run.
            if (!_preview && !_entryOnly && SupercruiseState.LastEnd >= SupercruiseState.ChargeStart) _endAt = 0;
        }

        // The charge ring: an arc of 0..100 % round the ship, cached per percent.
        private Geometry Arc(int pct)
        {
            if (_arcs.TryGetValue(pct, out var g)) return g;
            double f = pct / 100.0, r = 31, a0 = -Math.PI / 2, a1 = a0 + Math.PI * 2 * f;
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(new Point(Math.Cos(a0) * r, Math.Sin(a0) * r), false, false);
                if (pct >= 100) { c.ArcTo(new Point(Math.Cos(a0 + Math.PI) * r, Math.Sin(a0 + Math.PI) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false); c.ArcTo(new Point(Math.Cos(a0) * r, Math.Sin(a0) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false); }
                else if (pct > 0) c.ArcTo(new Point(Math.Cos(a1) * r, Math.Sin(a1) * r), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true, false);
            }
            sg.Freeze(); _arcs[pct] = sg; return sg;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            double W = w / u, H = h / u;
            double k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            // Where the charge is: `entered` once the drive has engaged, `te` the seconds since then, `frac` how far through the charge (paced to how long the last one took).
            // The ring fills over the spool-up (charge start to the journal's StartJump) and is full from then on; after that comes the game's own countdown to the entry (`tf` seconds since StartJump).
            bool entered, fired; double te, tf, frac;
            if (_preview)
            {
                fired = t >= PreviewChargeSeconds; tf = fired ? t - PreviewChargeSeconds : -1;
                entered = t >= PreviewChargeSeconds + PreviewCountdownSeconds; te = entered ? t - PreviewChargeSeconds - PreviewCountdownSeconds : -1;
                frac = fired ? 1 : Math.Min(0.99, t / PreviewChargeSeconds);
            }
            else if (_entryOnly) { entered = true; fired = true; te = t; tf = t; frac = 1; }
            else
            {
                entered = SupercruiseState.EnteredAt > SupercruiseState.ChargeStart;
                te = entered ? (DateTime.UtcNow - SupercruiseState.EnteredAt).TotalSeconds : -1;
                fired = entered || SupercruiseState.DriveFiredAt > SupercruiseState.ChargeStart;
                tf = fired && SupercruiseState.DriveFiredAt > SupercruiseState.ChargeStart ? (DateTime.UtcNow - SupercruiseState.DriveFiredAt).TotalSeconds : 0;
                frac = fired ? 1 : Math.Min(0.99, t / Math.Max(1.5, SupercruiseState.ChargeSeconds));
            }
            if (entered && _endAt < 0) _endAt = t - te + HoldSeconds;

            double fadeOut = _endAt >= 0 && t > _endAt ? Clamp01(1 - (t - _endAt) / FadeSeconds) : 1;
            double env = Math.Min(1, t / 0.35) * fadeOut;
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut;

            // How fast the ship is going, 0..1: a slow build while the drive spools, then the acceleration after it engages. The stars' flow is accumulated frame by frame so a change of speed never makes them jump.
            double accel = entered ? 0.4 + 0.6 * EaseOut(Phase(te, 0, 1.4)) : fired ? 0.15 + 0.25 * Phase(tf, 0, 4) : 0.15 * frac;
            _flow += Math.Clamp(t - _lastT, 0, 0.1) * (0.25 + 1.5 * accel);
            _lastT = t;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The stars pouring out of the centre: each one starts near it and races outwards, quicker and longer the faster the ship goes.
            for (int i = 0; i < Streaks; i++)
            {
                double z = (_flow + Offset[i]) % 1.0, z2 = z * z;
                double d0 = 8 + Start[i] * 14 + z2 * 92, d1 = d0 + 2 + accel * (0.35 + Length[i]) * (6 + z2 * 70);
                double cs = Math.Cos(Angle[i]), sn = Math.Sin(Angle[i]);
                int lvl = (int)Math.Round(Math.Min(1, z * 4) * Math.Min(1, 0.35 + accel) * 8);
                dc.DrawLine((i % 4 == 0 ? WhiteStreak : CyanStreak)[lvl], new Point(CentreX + cs * d0, CentreY + sn * d0), new Point(CentreX + cs * d1, CentreY + sn * d1));
            }

            // The charge ring closes while the drive spools; when it engages a pulse leaves the ship.
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            if (!entered) dc.DrawGeometry(null, ArcPen, Arc(fired ? 100 : (int)Math.Round(frac * 100)));
            double rp = entered ? Phase(te, 0, 0.9) : 0;
            if (rp > 0 && rp < 1) dc.DrawEllipse(null, Ring[(int)Math.Round((1 - rp) * 8)], new Point(0, 0), 14 + rp * 80, 14 + rp * 80);
            dc.Pop();

            // The ship from behind: a glow that swells with the drive, the RearHull, wings and RearFin, and three engine nozzles that flare as it speeds up.
            int lv = (int)Math.Round(accel * 8);
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.PushTransform(new ScaleTransform(1.15, 1.15));
            dc.DrawEllipse(HullGlow[lv], null, new Point(0, 2), 22 + accel * 10, 22 + accel * 10);
            dc.DrawGeometry(HullBrush, HullEdge, RearHull);
            dc.DrawGeometry(HullBrush, HullEdge, RearWingL);
            dc.DrawGeometry(HullBrush, HullEdge, RearWingR);
            dc.DrawGeometry(HullBrush, HullEdge, RearFin);
            // All the glows first, then the cores and rings, so no engine's glow washes over another.
            double gr = 5 + accel * 7, core = 1.8 + accel * 0.9;
            for (int e = -1; e <= 1; e++) dc.DrawEllipse(NozzleGlow[lv], null, new Point(e * 5.5, 3), gr, gr);
            for (int e = -1; e <= 1; e++)
            {
                dc.DrawEllipse(CoreBrush, null, new Point(e * 5.5, 3), core, core);
                dc.DrawEllipse(null, NozzleRing, new Point(e * 5.5, 3), 3.2, 3.2);
            }
            dc.Pop(); dc.Pop();

            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, "SUPERCRUISE", MonoFace, 13, CyanBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            if (entered) DrawSpaced(dc, "DRIVE ENGAGED", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(te, 0, 0.4));
            else if (fired) DrawSpaced(dc, "DRIVE CHARGED", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, 1);
            else DrawSpaced(dc, $"FSD CHARGING {(int)Math.Round(frac * 100)} %", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 0.2, 0.3));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
