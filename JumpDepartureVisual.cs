using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // What the departure screen needs to know about the jump in progress, shared by every window showing it. Set from the journal watcher's events (see MainWindow.InitAnimations).
    public static class JumpDepartureState
    {
        public static DateTime ChargeStart = DateTime.MinValue;   // the hyperspace charge began
        public static DateTime DriveFiredAt = DateTime.MinValue;  // the journal's StartJump: the countdown begins (set after ChargeStart)
        public static DateTime LastEnd = DateTime.MinValue;       // when EndRequested last fired (a departure that starts late, after its jump has already ended, must not run)
        public static event Action? EndRequested;                 // the jump arrived or the charge was cancelled
        public static void RequestEnd() { LastEnd = DateTime.UtcNow; EndRequested?.Invoke(); }
    }

    // The departure half of a hyperspace jump, for a jump made in space: the charge ring closes while the drive spools up (about 15 seconds), then "HYPERSPACE JUMP" and a
    // 3-2-1-0 countdown once the drive fires, a flash, and a streaking tunnel that runs for as long as the jump takes, however long. It ends when the arrival (or a cancelled
    // charge) calls EndRequested: a quick flash to white that the Arrival screen then fades from. Subject = the next system's name, or "".
    public sealed class JumpDepartureVisual : AnimationVisual
    {
        private const double ChargeSeconds = 15.0, CountdownSeconds = 5.0, FlashPhaseSeconds = 1.6, EndSeconds = 0.35, GiveUpChargeSeconds = 90, GiveUpJumpSeconds = 150;

        private static readonly Brush Backdrop = Frozen(Color.FromRgb(0x03, 0x06, 0x0b));
        private static readonly Brush White = Frozen(Color.FromRgb(235, 245, 255));
        private static readonly Brush StarDot = Frozen(Color.FromArgb(150, 200, 220, 255));
        private static readonly Brush TunnelGlow = MakeGlow();
        private static readonly Pen RingPen = FrozenPen(Color.FromArgb(64, 85, 242, 255), 1);
        private static readonly Pen ArcPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 3.4);   // the charge ring, the same green as the atmosphere jump's
        private static readonly Pen LockPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 1.8);
        private static readonly Pen AlignPen = FrozenPen(Color.FromRgb(255, 111, 210), 1.8);   // the lock circle until the target is locked
        private static readonly Brush CyanBrush = Frozen(Color.FromRgb(0x55, 0xf2, 0xff));
        private static readonly Brush PaleBrush = Frozen(Color.FromRgb(0xff, 0xd9, 0xa0));
        private static readonly (double a, double d, double v)[] Stars = MakeStars();
        // The hyperspace streaks are the atmosphere jump's: pink, cyan and white, laid out from the same hash of the streak number.
        private static readonly Pen[] StreakPens = { FrozenPen(Color.FromArgb(204, 255, 111, 210), 1.0), FrozenPen(Color.FromArgb(204, 95, 255, 240), 1.5), FrozenPen(Color.FromArgb(204, 255, 255, 255), 2.0) };
        private static readonly double[] HashA = MakeHash(12.9898, 43758.5453), HashB = MakeHash(3.7, 9999), HashC = MakeHash(7.1, 9999);
        private static readonly Brush JumpText = Frozen(Color.FromRgb(2, 6, 11));
        private static double[] MakeHash(double k, double m) { var a = new double[150]; for (int i = 0; i < a.Length; i++) { double v = Math.Sin(i * k) * m; a[i] = v - Math.Floor(v); } return a; }

        private static Brush MakeGlow()
        {
            var b = new RadialGradientBrush(Color.FromArgb(97, 150, 190, 255), Color.FromArgb(0, 150, 190, 255));
            b.Freeze(); return b;
        }

        private static (double, double, double)[] MakeStars()
        {
            var r = new Random(3);
            var a = new (double, double, double)[110];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble() * Math.PI * 2, 0.3 + r.NextDouble() * 0.7, r.NextDouble());
            return a;
        }


        private string _subject = "";
        private double _endAt = -1;
        private bool _preview, _dead;   // preview: the settings window's Play button runs a scripted jump (charge, countdown, tunnel, end); dead: the jump had already ended when this started
        private readonly Dictionary<int, Geometry> _arcs = new();

        public JumpDepartureVisual()
        {
            Loaded += (_, _) => JumpDepartureState.EndRequested += OnEndRequested;
            Unloaded += (_, _) => JumpDepartureState.EndRequested -= OnEndRequested;
        }

        private void OnEndRequested() { if (_endAt < 0) _endAt = Elapsed; }

        // Open-ended: runs until EndRequested (the flash to white takes EndSeconds), or gives up if the game never says how the jump went.
        protected override double Duration
        {
            get
            {
                if (_dead) return 0;
                if (_endAt >= 0) return _endAt + EndSeconds;
                bool fired = JumpDepartureState.DriveFiredAt > JumpDepartureState.ChargeStart;
                if (fired ? (DateTime.UtcNow - JumpDepartureState.DriveFiredAt).TotalSeconds > GiveUpJumpSeconds : Elapsed > GiveUpChargeSeconds) return 0;
                return double.MaxValue;
            }
        }

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _endAt = -1;
            _preview = detail == "PREVIEW";
            _dead = !_preview && JumpDepartureState.LastEnd >= JumpDepartureState.ChargeStart;
        }

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
            double W = w / u, H = h / u, cx = W / 2, cy = H / 2 - 8;
            double env = Math.Min(1, t / 0.35);

            bool fired; double tj, chargeFrac;
            if (_preview)
            {
                const double PreviewCharge = 4.5;
                fired = t >= PreviewCharge; tj = fired ? t - PreviewCharge : -1; chargeFrac = Math.Min(0.99, t / PreviewCharge);
                if (t >= PreviewCharge + CountdownSeconds + FlashPhaseSeconds + 3.5 && _endAt < 0) _endAt = t;
            }
            else
            {
                fired = JumpDepartureState.DriveFiredAt > JumpDepartureState.ChargeStart;
                tj = fired ? (DateTime.UtcNow - JumpDepartureState.DriveFiredAt).TotalSeconds : -1;
                chargeFrac = Math.Min(0.99, t / ChargeSeconds);
            }
            // After the countdown: the drive's flash (the streaks lengthen out of the lock circle under a white flash and the word JUMP), then the tunnel.
            bool jumping = fired && tj >= CountdownSeconds;
            double tt = jumping ? tj - CountdownSeconds : 0;
            bool tunnel = jumping && tt >= FlashPhaseSeconds, flashing = jumping && !tunnel;
            double f = flashing ? tt / FlashPhaseSeconds : 0, cy2 = H * 0.42;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            if (!tunnel)
            {
                for (int i = 0; i < 110; i++) dc.DrawRectangle(StarDot, null, new Rect(HashB[i] * W, HashC[(i * 7) % 150] * H, 1.2, 1.2));   // across the whole width, not just a 4:3 patch
                dc.PushTransform(new TranslateTransform(cx, cy));
                foreach (double r in new[] { 30.0, 58, 86 }) dc.DrawEllipse(null, RingPen, new Point(0, 0), r, r);
                double ch = fired ? 1 : chargeFrac;
                dc.DrawGeometry(null, ArcPen, Arc((int)Math.Round(ch * 100)));
                dc.DrawEllipse(null, fired || t >= 2.5 ? LockPen : AlignPen, new Point(0, 0), 18, 18);
                dc.Pop();
                if (flashing)
                {
                    var sgs = new StreamGeometry[3]; var ctx = new StreamGeometryContext[3];
                    for (int k = 0; k < 3; k++) { sgs[k] = new StreamGeometry(); ctx[k] = sgs[k].Open(); }
                    for (int i = 0; i < 120; i++)
                    {
                        double a = HashA[i] * Math.PI * 2, d0 = 11 + HashB[i] * 33, d1 = d0 + f * f * W * 0.5 * (0.4 + HashC[i]);
                        ctx[i % 3].BeginFigure(new Point(cx + Math.Cos(a) * d0, cy + Math.Sin(a) * d0), false, false);
                        ctx[i % 3].LineTo(new Point(cx + Math.Cos(a) * d1, cy + Math.Sin(a) * d1), true, false);
                    }
                    for (int k = 0; k < 3; k++) { ctx[k].Close(); sgs[k].Freeze(); dc.DrawGeometry(null, StreakPens[k], sgs[k]); }
                }
            }
            else
            {
                // The tunnel: a bluish core and 150 streaks in pink, cyan and white pouring out of the centre, as in the atmosphere jump.
                dc.DrawEllipse(TunnelGlow, null, new Point(cx, cy2), 140, 140);
                var sgs = new StreamGeometry[3]; var ctx = new StreamGeometryContext[3];
                for (int k = 0; k < 3; k++) { sgs[k] = new StreamGeometry(); ctx[k] = sgs[k].Open(); }
                for (int i = 0; i < 150; i++)
                {
                    double a = HashA[i] * Math.PI * 2, z = (tt * 0.55 + HashC[i]) % 1.0, z2 = z * z;
                    double d0 = 8 + z2 * W * 0.60, d1 = d0 + 4 + z2 * W * 0.38;
                    ctx[i % 3].BeginFigure(new Point(cx + Math.Cos(a) * d0, cy2 + Math.Sin(a) * d0), false, false);
                    ctx[i % 3].LineTo(new Point(cx + Math.Cos(a) * d1, cy2 + Math.Sin(a) * d1), true, false);
                }
                for (int k = 0; k < 3; k++) { ctx[k].Close(); sgs[k].Freeze(); dc.DrawGeometry(null, StreakPens[k], sgs[k]); }
            }

            // The white flash as the drive fires (a sharp burst, then it builds to white), and the flash to white when the jump ends.
            double flash = flashing ? Math.Clamp(0.9 * Math.Exp(-Math.Pow((f - 0.12) / 0.12, 2)) + 0.85 * Smooth(0.75, 1, f), 0, 1) : 0;
            if (_endAt >= 0) flash = Math.Max(flash, Clamp01((t - _endAt) / EndSeconds));
            if (flash > 0.01) dc.DrawRectangle(Level(Whites, flash), null, new Rect(-40, -40, W + 80, H + 80));

            // Text.
            if (_endAt < 0)
            {
                if (!fired)
                    DrawText(dc, $"FSD CHARGING {(int)Math.Round(chargeFrac * 100)} %", MonoFace, 12, GreenBrush, cx, cy + 52, env);
                else if (!jumping)
                {
                    DrawSpaced(dc, "HYPERSPACE JUMP", MonoFace, 12, GreenBrush, cx, cy + 52, 0.1, env);
                    DrawText(dc, (tj < 1 ? 3 : Math.Max(0, 3 - (int)Math.Floor(tj - 1))).ToString(), MonoFace, 21, WhiteBrush, cx, cy - 12, env);   // sized to sit inside the lock circle
                }
                if (_subject.Length > 0 && !jumping) DrawText(dc, "TO " + _subject, MonoFace, 11, PaleBrush, cx, H - 20, env);
                if (flashing && f < 0.8) DrawText(dc, "JUMP", MonoFace, 26, JumpText, cx, H * 0.5 - 16, env);
                if (tunnel)
                {
                    DrawText(dc, "IN HYPERSPACE", MonoFace, 14, WhiteBrush, cx, H - 46, env);
                    if (_subject.Length > 0) DrawText(dc, _subject, MonoFace, 11, SubjectBrush, cx, H - 26, 0.8 * env);
                }
            }
            dc.Pop();   // scale
        }
    }
}
