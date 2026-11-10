using System;
using System.Collections.Generic;
using System.Globalization;
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

    // The departure half of a hyperspace jump, for a jump made in space, laid out like the atmosphere jump: the next system's star drifts in along a dashed line while ALIGN counts down to LOCKED,
    // TARGET LOCKED blinks, and a destination card (name, class with the fuel drop, distance, jump number, scoopable) slides in. Then the charge ring closes while the drive spools up (about 15
    // seconds), "HYPERSPACE JUMP" and a 3-2-1-0 countdown once the drive fires, a flash, and a streaking tunnel that runs for as long as the jump takes, however long. It ends when the arrival (or a
    // cancelled charge) calls EndRequested: a quick flash to white that the Arrival screen then fades from.
    // Subject = the next system's name, or "". Detail = "LIVE|class|distance ly|jump number|jumps in route|scoopable 1/0" ("PREVIEW|..." for the settings window's scripted run); the fields after the
    // first may be empty when the route is not known.
    public sealed class JumpDepartureVisual : AnimationVisual
    {
        private const double ChargeSeconds = 15.0, CountdownSeconds = 5.0, FlashPhaseSeconds = 1.6, EndSeconds = 0.35, GiveUpChargeSeconds = 90, GiveUpJumpSeconds = 150;
        private const double AlignSeconds = 2.3, LockSeconds = 2.4, ChargeStartsAt = 2.8, PreviewCharge = 7.0;

        private static readonly Brush Backdrop = Frozen(Color.FromRgb(0x03, 0x06, 0x0b));
        private static readonly Brush White = Frozen(Color.FromRgb(235, 245, 255));
        private static readonly Brush StarDot = Frozen(Color.FromArgb(150, 200, 220, 255));
        private static readonly Brush TunnelGlow = MakeGlow();
        private static readonly Brush TargetGlow = MakeTargetGlow();
        private static readonly Pen RingPen = FrozenPen(Color.FromArgb(64, 85, 242, 255), 1);
        private static readonly Pen ArcPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 3.4);   // the charge ring, the same green as the atmosphere jump's
        private static readonly Pen LockPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 1.8);
        private static readonly Pen AlignPen = FrozenPen(Color.FromRgb(255, 111, 210), 1.8);   // the lock circle until the target is locked
        private static readonly Pen AlignLine = MakeAlignLine();
        private static readonly Pen CrossPen = FrozenPen(Color.FromArgb(77, 85, 242, 255), 1);
        private static readonly Pen TargetCross = FrozenPen(Color.FromRgb(255, 255, 255), 1);
        private static readonly Brush CyanBrush = Frozen(Color.FromRgb(0x55, 0xf2, 0xff));
        private static readonly Brush PaleBrush = Frozen(Color.FromRgb(0xff, 0xd9, 0xa0));
        private static readonly Brush OrangeBrush = Frozen(Color.FromRgb(0xff, 0xaa, 0x00)), OrangeBrush2 = Frozen(Color.FromRgb(0xff, 0xaa, 0x00));
        private static readonly Brush AlignText = Frozen(Color.FromRgb(0xff, 0x9a, 0xd8)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 10, 16, 18)), CardBrush = Frozen(Color.FromArgb(199, 10, 16, 18));
        private static readonly Pen CardEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1);
        private static readonly Typeface MonoBold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Geometry Drop = MakeDrop();
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

        private static Brush MakeTargetGlow()
        {
            var b = new RadialGradientBrush(Color.FromArgb(140, 255, 240, 200), Color.FromArgb(0, 255, 240, 200));
            b.Freeze(); return b;
        }

        private static Pen MakeAlignLine()
        {
            var p = new Pen(Frozen(Color.FromArgb(153, 255, 111, 210)), 1) { DashStyle = new DashStyle(new[] { 5.0, 6.0 }, 0) };
            p.Freeze(); return p;
        }

        // The fuel drop, the same shape as the Destination route list's (10 x 15 units, here scaled to the text beside it).
        private static Geometry MakeDrop() { var g = Geometry.Parse("M5,0 C5,0 0,6.4 0,9 A5,5 0 1,0 10,9 C10,6.4 5,0 5,0 Z"); g.Freeze(); return g; }

        private static (double, double, double)[] MakeStars()
        {
            var r = new Random(3);
            var a = new (double, double, double)[110];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble() * Math.PI * 2, 0.3 + r.NextDouble() * 0.7, r.NextDouble());
            return a;
        }


        private string _subject = "", _class = "";
        private double _distance = -1;
        private int _jumpNo, _jumpTotal;
        private bool _scoop;
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
            var p = (detail ?? "").Split('|');
            _preview = p[0] == "PREVIEW";
            _dead = !_preview && JumpDepartureState.LastEnd >= JumpDepartureState.ChargeStart;
            _class = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "";
            _distance = p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : -1;
            _jumpNo = p.Length > 3 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            _jumpTotal = p.Length > 4 && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 0;
            _scoop = p.Length > 5 && p[5].Trim() == "1";
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

        // The fuel drop with its top-left corner at (x, y), scaled so it is about `height` tall. A new transform each time: the drawing keeps a reference to it.
        private void DrawDrop(DrawingContext dc, double x, double y, double height, Brush brush, double opacity)
        {
            double s = height / 15.0;
            dc.PushOpacity(opacity);
            dc.PushTransform(new MatrixTransform(s, 0, 0, s, x, y));
            dc.DrawGeometry(brush, null, Drop);
            dc.Pop(); dc.Pop();
        }

        private static double TextWidth(string text, double size) => text.Length * size * 0.55;   // Consolas advance
        private string ClassText => _class.Length > 0 ? "CLASS " + _class : "";

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            double W = w / u, H = h / u, cx = W / 2, cy = H / 2 - 8;
            double env = Math.Min(1, t / 0.35);

            bool fired; double tj, chargeFrac;
            double chargeTotal = _preview ? PreviewCharge : ChargeSeconds;
            if (_preview)
            {
                fired = t >= PreviewCharge; tj = fired ? t - PreviewCharge : -1;
                if (t >= PreviewCharge + CountdownSeconds + FlashPhaseSeconds + 3.5 && _endAt < 0) _endAt = t;
            }
            else
            {
                fired = JumpDepartureState.DriveFiredAt > JumpDepartureState.ChargeStart;
                tj = fired ? (DateTime.UtcNow - JumpDepartureState.DriveFiredAt).TotalSeconds : -1;
            }
            // The ring fills from the lock to the moment the drive fires (about 15 s in a real jump).
            chargeFrac = Math.Min(0.99, Clamp01((t - ChargeStartsAt) / Math.Max(1, chargeTotal - ChargeStartsAt)));
            // After the countdown: the drive's flash (the streaks lengthen out of the lock circle under a white flash and the word JUMP), then the tunnel.
            bool jumping = fired && tj >= CountdownSeconds;
            double tt = jumping ? tj - CountdownSeconds : 0;
            bool tunnel = jumping && tt >= FlashPhaseSeconds, flashing = jumping && !tunnel;
            double f = flashing ? tt / FlashPhaseSeconds : 0, cy2 = H * 0.42;

            // The target: a small off-centre star at first, drifting onto the crosshair as the alignment closes.
            bool locked = fired || t >= LockSeconds;
            double al = fired ? 0 : 1 - Smooth(0.2, AlignSeconds, t);
            double tx = cx + Math.Min(95, W * 0.3) * al, ty = cy - 52 * al;
            double tsz = t < 0.5 ? 0.4 + 0.6 * Smooth(0, 0.5, t) : 1.0, ta = Clamp01(t / 0.3) * env;
            double flare = 1 + 1.6 * Smooth(ChargeStartsAt, chargeTotal, t);

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            if (!tunnel)
            {
                for (int i = 0; i < 110; i++) dc.DrawRectangle(StarDot, null, new Rect(HashB[i] * W, HashC[(i * 7) % 150] * H, 1.2, 1.2));   // across the whole width, not just a 4:3 patch
                dc.PushTransform(new TranslateTransform(cx, cy));
                foreach (double r in new[] { 30.0, 58, 86 }) dc.DrawEllipse(null, RingPen, new Point(0, 0), r, r);
                dc.DrawLine(CrossPen, new Point(-100, 0), new Point(100, 0));
                dc.DrawLine(CrossPen, new Point(0, -100), new Point(0, 100));
                if (t >= ChargeStartsAt || fired) dc.DrawGeometry(null, ArcPen, Arc((int)Math.Round((fired ? 1 : chargeFrac) * 100)));
                dc.DrawEllipse(null, locked ? LockPen : AlignPen, new Point(0, 0), 18, 18);
                dc.Pop();

                if (!jumping)
                {
                    // The dashed line from the crosshair to the target while it is still off-centre, then the star itself: a glow that swells as the drive charges, a cross and a dot.
                    dc.PushOpacity(ta);
                    if (al > 0.02) { dc.PushOpacity(Math.Min(1, t / 0.4)); dc.DrawLine(AlignLine, new Point(cx, cy), new Point(tx, ty)); dc.Pop(); }
                    double gr = 30 * tsz * flare, arm = 9 * tsz * flare;
                    dc.DrawEllipse(TargetGlow, null, new Point(tx, ty), gr, gr);
                    dc.DrawLine(TargetCross, new Point(tx - arm, ty), new Point(tx + arm, ty));
                    dc.DrawLine(TargetCross, new Point(tx, ty - arm), new Point(tx, ty + arm));
                    dc.DrawEllipse(White, null, new Point(tx, ty), 2.2, 2.2);
                    dc.Pop();

                    // While the target is off-centre its name and class sit beside it, as in the atmosphere jump.
                    if (al > 0.35 && ta > 0.5)
                    {
                        DrawText(dc, _subject.Length > 0 ? _subject : "NEXT JUMP", MonoFace, 9, WhiteBrush, tx + 13, ty - 13, ta, TextAlignment.Left);
                        if (_subject.Length > 0 && ClassText.Length > 0)
                        {
                            DrawText(dc, ClassText, MonoFace, 9, OrangeBrush, tx + 13, ty - 3, ta, TextAlignment.Left);
                            if (_scoop) DrawDrop(dc, tx + 13 + TextWidth(ClassText, 9) + 4, ty - 2, 9, CyanBrush, ta);
                        }
                    }
                }

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
                {
                    if (t < LockSeconds) DrawText(dc, $"ALIGN {al * 34:F1}°", MonoFace, 12, AlignText, cx, cy + 42, env);
                    else if (t < ChargeStartsAt) DrawText(dc, "LOCKED", MonoFace, 12, GreenBrush, cx, cy + 42, env);
                    else DrawText(dc, $"FSD CHARGING {(int)Math.Round(chargeFrac * 100)} %", MonoFace, 12, GreenBrush, cx, cy + 42, env);
                }
                else if (!jumping)
                {
                    DrawSpaced(dc, "HYPERSPACE JUMP", MonoFace, 12, GreenBrush, cx, cy + 42, 0.1, env);
                    DrawText(dc, (tj < 1 ? 3 : Math.Max(0, 3 - (int)Math.Floor(tj - 1))).ToString(), MonoFace, 21, WhiteBrush, cx, cy - 12, env);   // sized to sit inside the lock circle
                }
                // TARGET LOCKED blinks above the ring from the lock until the charge has been going a moment.
                if (!jumping && locked && t < ChargeStartsAt + 0.8 && (t < ChargeStartsAt || (int)(t * 4) % 2 == 0))
                    DrawText(dc, "TARGET LOCKED", MonoBold, 11, GreenBrush, cx, cy - 36, env);

                // Once the target is centred: its name and class on a dark plate under the status text, and the destination card on the right.
                if (!jumping && _subject.Length > 0)
                {
                    double pa = Smooth(1.6, LockSeconds, t) * env;
                    if (pa > 0.01)
                    {
                        string cls = ClassText;
                        double nameW = TextWidth(_subject, 12), classW = cls.Length > 0 ? TextWidth(cls, 12) + (_scoop ? 12 : 0) : 0, plateW = Math.Max(Math.Max(nameW, classW) + 16, 80), plateH = cls.Length > 0 ? 29 : 17, py = cy + 54;
                        dc.PushOpacity(pa);
                        dc.DrawRoundedRectangle(PlateBrush, null, new Rect(cx - plateW / 2, py, plateW, plateH), 2, 2);
                        dc.Pop();
                        DrawText(dc, _subject, MonoBold, 12, WhiteBrush, cx, py + 1, pa);
                        if (cls.Length > 0)
                        {
                            double left = cx - classW / 2;
                            DrawText(dc, cls, MonoBold, 12, OrangeBrush, left, py + 15, pa, TextAlignment.Left);
                            if (_scoop) DrawDrop(dc, left + TextWidth(cls, 12) + 4, py + 17, 11, CyanBrush, pa);
                        }
                    }

                    double ca = Smooth(LockSeconds, LockSeconds + 0.7, t) * env;
                    if (ca > 0.01)
                    {
                        var rows = new List<(string label, string value)>();
                        if (_distance > 0) rows.Add(("DISTANCE", _distance.ToString("0.0", CultureInfo.InvariantCulture) + " LY"));
                        if (_jumpNo > 0 && _jumpTotal > 0) rows.Add(("JUMP", _jumpNo.ToString(CultureInfo.InvariantCulture) + " OF " + _jumpTotal.ToString(CultureInfo.InvariantCulture)));
                        if (_class.Length > 0) rows.Add(("SCOOPABLE", _scoop ? "YES" : "NO"));
                        double cw = 96, ch = 40 + rows.Count * 10 + (rows.Count > 0 ? 2 : 0), x = Math.Min(cx + 96, W - cw - 8) + (1 - ca) * 22, y = cy - ch / 2 - 4;
                        dc.PushOpacity(ca);
                        dc.DrawRectangle(CardBrush, CardEdge, new Rect(x, y, cw, ch));
                        dc.Pop();
                        DrawText(dc, "DESTINATION", MonoFace, 7, MutedBrush, x + 6, y + 4, ca, TextAlignment.Left);
                        double nameSize = Math.Min(11, (cw - 12) / (Math.Max(1, _subject.Length) * 0.58));
                        DrawText(dc, _subject, MonoBold, nameSize, WhiteBrush, x + 6, y + 13, ca, TextAlignment.Left);
                        if (_class.Length > 0)
                        {
                            DrawText(dc, ClassText, MonoFace, 9, OrangeBrush, x + 6, y + 27, ca, TextAlignment.Left);
                            if (_scoop) DrawDrop(dc, x + 6 + TextWidth(ClassText, 9) + 4, y + 28, 9, CyanBrush, ca);
                        }
                        for (int i = 0; i < rows.Count; i++)
                        {
                            double ra = Smooth(LockSeconds + 0.3 + i * 0.2, LockSeconds + 0.7 + i * 0.2, t) * env;
                            DrawText(dc, rows[i].label, MonoFace, 7, MutedBrush, x + 6, y + 41 + i * 10, ra, TextAlignment.Left);
                            DrawText(dc, rows[i].value, MonoBold, 8, WhiteBrush, x + cw - 6, y + 40 + i * 10, ra, TextAlignment.Right);
                        }
                    }
                }
                if (flashing && f < 0.8) DrawText(dc, "JUMP", MonoFace, 26, JumpText, cx, H * 0.5 - 16, env);
                if (tunnel)
                {
                    DrawText(dc, "IN HYPERSPACE", MonoFace, 14, WhiteBrush, cx, H - 52, env);
                    if (_subject.Length > 0) DrawText(dc, _subject, MonoFace, 11, SubjectBrush, cx, H - 33, 0.8 * env);
                    string info = (_distance > 0 ? _distance.ToString("0.0", CultureInfo.InvariantCulture) + " LY" : "") + (_distance > 0 && _jumpNo > 0 && _jumpTotal > 0 ? "  -  " : "") + (_jumpNo > 0 && _jumpTotal > 0 ? "JUMP " + _jumpNo.ToString(CultureInfo.InvariantCulture) + " OF " + _jumpTotal.ToString(CultureInfo.InvariantCulture) : "");
                    if (info.Length > 0) DrawText(dc, info, MonoFace, 9.5, OrangeBrush, cx, H - 16, 0.9 * env);
                }
            }
            dc.Pop();   // scale
        }
    }
}
