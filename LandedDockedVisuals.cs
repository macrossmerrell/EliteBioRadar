using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Both visuals below are laid out in "local units" like the mock-ups they came from: a scene about 340 x 210 that is scaled uniformly to fit the
    // area (taking the width and height of the area into account), so local W and H vary a little with the window's shape.

    // The Landed moment (a planet touchdown): a cockpit view along the ground at the star's low light. The bump of contact, a dust haze that rolls past and clears, the
    // title "LANDED", the body's name and a short stats line (gravity, temperature, atmosphere). About 6 seconds. Starts at the touchdown itself.
    // Subject = body name, detail = "LABEL=value|LABEL=value".
    public sealed class LandedVisual : AnimationVisual
    {
        private const double FadeOutAt = 5.8;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Typeface Mono = new(AppFonts.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush Amber = Frozen(Color.FromRgb(0xf2, 0xb8, 0x4b));
        private static readonly Brush Pale = Frozen(Color.FromRgb(0xff, 0xd9, 0xa0));
        private static readonly Brush Dim = Frozen(Color.FromRgb(0x5a, 0xa8, 0xb0));
        private static readonly Brush White = Frozen(Color.FromRgb(0xff, 0xff, 0xff));
        private static readonly Brush Green = Frozen(Color.FromRgb(0x7d, 0xff, 0xb0));
        private static readonly Brush Strip = Frozen(Color.FromArgb(0xd9, 0x08, 0x0d, 0x0d));
        private static readonly Brush Ground = Frozen(Color.FromRgb(0x1d, 0x12, 0x10));
        private static readonly Brush Frame = Frozen(Color.FromRgb(0x07, 0x0b, 0x0e));
        private static readonly Pen Horizon = FrozenPen(Color.FromRgb(0x3a, 0x24, 0x20), 1.5);
        private static readonly Pen Lines = FrozenPen(Color.FromArgb(64, 120, 80, 64), 0.8);
        private static readonly Pen FrameEdge = FrozenPen(Color.FromArgb(128, 85, 242, 255), 1);
        private static readonly Brush Disc = Frozen(Color.FromRgb(0xff, 0xe0, 0xb0));

        // Rocks and dust clouds are laid out once: fractions of the width and of the depth of the ground.
        private static readonly (double x, double d, double w, double h)[] Rocks = MakeRocks();
        private static readonly (double x, double d, double rx, double ry, double speed, double alpha)[] Clouds = MakeClouds();

        private string _subject = "";
        private (string label, string value)[] _stats = Array.Empty<(string, string)>();

        private static (double, double, double, double)[] MakeRocks()
        {
            var r = new Random(11);
            var a = new (double, double, double, double)[16];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble(), 2 + r.NextDouble() * 6, 1 + r.NextDouble() * 3);
            return a;
        }

        private static (double, double, double, double, double, double)[] MakeClouds()
        {
            var r = new Random(23);
            var a = new (double, double, double, double, double, double)[9];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble(), 50 + r.NextDouble() * 70, 12 + r.NextDouble() * 18, 6 + r.NextDouble() * 14, 0.5 + r.NextDouble() * 0.5);
            return a;
        }

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _stats = ParseStats(detail);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            double fadeOut = t > FadeOutAt ? Clamp01(1 - (t - FadeOutAt) / (Duration - FadeOutAt)) : 1;
            double env = Math.Min(1, t / 0.35) * fadeOut;
            if (env <= 0) return;
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            double W = w / u, H = h / u, hz = H * 0.585;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);

            // The bump of contact, then the engines' vibration dying away.
            double shake = 5 * Math.Exp(-t * 5) * Math.Sin(t * 30) + 0.7 * Math.Exp(-t * 1.1) * Math.Sin(t * 53);
            dc.PushTransform(new TranslateTransform(0, shake));

            var sky = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, hz) };
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(4, 7, 12), 0));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(26, 20, 32), 0.6));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(90, 58, 50), 1));
            dc.DrawRectangle(sky, null, new Rect(-40, -40, W + 80, hz + 40));

            // The star, low on the horizon, climbing a little.
            double sx = W * 0.69, sy = hz + 2 - 22 * Smooth(0, 5, t);
            dc.PushClip(new RectangleGeometry(new Rect(-40, -40, W + 80, hz + 40)));
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(230, 255, 200, 130), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(102, 255, 150, 90), 0.25));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 120, 80), 1));
            dc.DrawEllipse(glow, null, new Point(sx, sy), 86, 86);
            dc.DrawEllipse(Disc, null, new Point(sx, sy), 9, 9);
            dc.Pop();

            dc.DrawRectangle(Ground, null, new Rect(-40, hz, W + 80, H - hz + 80));
            dc.DrawLine(Horizon, new Point(-40, hz), new Point(W + 40, hz));
            double reach = 300, slope = 56.0 / (210 * 0.415);
            for (int i = -8; i <= 8; i++)
                dc.DrawLine(Lines, new Point(W / 2 + i * 4, hz), new Point(W / 2 + i * 4 + i * slope * reach, hz + reach));
            foreach (var r in Rocks)
            {
                double y = hz + 8 + r.d * Math.Max(20, H - hz - 40), s = 0.6 + (y - hz) / 60;
                dc.DrawEllipse(Frame, null, new Point(r.x * W, y), r.w * s, r.h * s);
            }

            // Dust: thick at contact, drifting past and thinning out.
            double da = 0.62 * Smooth(0, 0.3, t) * (1 - Smooth(1.0, 4.8, t));
            if (da > 0.01)
            {
                double span = W + 160;
                foreach (var q in Clouds)
                {
                    double x = (((q.x * W + t * q.speed * 1.6) % span) + span) % span - 80;
                    double y = hz + 10 + q.d * Math.Max(20, H - hz - 50) - 14 * Smooth(0.5, 4.8, t);
                    byte a = (byte)Math.Clamp(da * q.alpha * 255, 0, 255);
                    var b = new RadialGradientBrush(Color.FromArgb(a, 176, 112, 88), Color.FromArgb(0, 176, 112, 88));
                    dc.DrawEllipse(b, null, new Point(x, y), q.rx, q.ry);
                }
            }
            dc.Pop();   // shake

            // Cockpit frame: side pillars and the dash, with a ground readout.
            var left = new StreamGeometry();
            using (var g = left.Open()) { g.BeginFigure(new Point(0, 0), true, true); g.LineTo(new Point(34, 0), true, false); g.LineTo(new Point(8, H), true, false); g.LineTo(new Point(0, H), true, false); }
            var right = new StreamGeometry();
            using (var g = right.Open()) { g.BeginFigure(new Point(W, 0), true, true); g.LineTo(new Point(W - 34, 0), true, false); g.LineTo(new Point(W - 8, H), true, false); g.LineTo(new Point(W, H), true, false); }
            dc.DrawGeometry(Frame, null, left); dc.DrawGeometry(Frame, null, right);
            dc.DrawLine(FrameEdge, new Point(34, 0), new Point(8, H)); dc.DrawLine(FrameEdge, new Point(W - 34, 0), new Point(W - 8, H));
            dc.DrawRectangle(Frame, null, new Rect(0, H - 30, W, 30));
            dc.DrawRectangle(Frame, null, new Rect(8, H - 30, W - 16, 1));
            dc.DrawLine(FrameEdge, new Point(8, H - 30), new Point(W - 8, H - 30));
            dc.Pop();   // backdrop opacity

            // Text, drawn at full strength over the scene.
            DrawText(dc, "ALT", Mono, 10, Dim, 30, H - 22, env * 0.9, TextAlignment.Left);
            DrawText(dc, "0 m", Mono, 13, White, 52, H - 24, env, TextAlignment.Left);
            DrawText(dc, "ON GROUND", Mono, 11, Green, W - 30, H - 22, env, TextAlignment.Right);

            double pt = Phase(t, 1.5, 0.8);
            if (pt > 0)
            {
                double ease = EaseOut(pt), titleY = H * 0.28;
                dc.PushOpacity(ease * fadeOut);
                dc.DrawRectangle(Strip, null, new Rect(-40, titleY - 14, W + 80, 86));
                dc.Pop();
                DrawSpaced(dc, "LANDED", Mono, 22, Amber, W / 2, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
                double ps = Phase(t, 2.1, 0.5);
                DrawText(dc, _subject, Mono, 12, Pale, W / 2, titleY + 34, ps * fadeOut);
                for (int i = 0; i < _stats.Length && i < 3; i++)
                {
                    double a = Phase(t, 2.5 + i * 0.3, 0.5) * fadeOut;
                    double x = W / 2 + (i - (Math.Min(_stats.Length, 3) - 1) / 2.0) * 100;
                    DrawText(dc, _stats[i].label, Mono, 9, Dim, x, titleY + 56, a * 0.85);
                    DrawText(dc, _stats[i].value, Mono, 12, White, x, titleY + 68, a);
                }
            }
            dc.Pop();   // scale
        }
    }

    // The Docked moment (a station, outpost, settlement or fleet carrier): a top-down pad lock. Four brackets close on the ship, a pink ring turns, ripples
    // spread at contact and the brackets go green, then the title "DOCKED", the station's name and its stats appear. About 5.5 seconds.
    // Subject = station name, detail = "LABEL=value|LABEL=value".
    public sealed class DockedVisual : AnimationVisual
    {
        private const double ContactAt = 1.1, FadeOutAt = 5.0;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Typeface Mono = new(AppFonts.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush Backdrop = Frozen(Color.FromRgb(0x05, 0x09, 0x0b));
        private static readonly Brush Amber = Frozen(Color.FromRgb(0xf2, 0xb8, 0x4b));
        private static readonly Brush Pale = Frozen(Color.FromRgb(0xff, 0xd9, 0xa0));
        private static readonly Brush Dim = Frozen(Color.FromRgb(0x5a, 0xa8, 0xb0));
        private static readonly Brush White = Frozen(Color.FromRgb(0xff, 0xff, 0xff));
        private static readonly Brush Cyan = Frozen(Color.FromRgb(0x55, 0xf2, 0xff));
        private static readonly Brush Green = Frozen(Color.FromRgb(0x7d, 0xff, 0xb0));
        private static readonly Brush Strip = Frozen(Color.FromArgb(0xd9, 0x08, 0x0d, 0x0d));
        private static readonly Brush ShipFill = Frozen(Color.FromRgb(0x1b, 0x2a, 0x33));
        private static readonly Brush Shadow = Frozen(Color.FromArgb(90, 0, 0, 0));
        private static readonly Brush Canopy = Frozen(Color.FromArgb(153, 85, 242, 255));
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x0e, 0x2a, 0x30), 0.7);
        private static readonly Pen ShipPen = FrozenPen(Color.FromRgb(0x55, 0xf2, 0xff), 1.2);
        private static readonly Pen CyanPen = FrozenPen(Color.FromRgb(0x55, 0xf2, 0xff), 2.2);
        private static readonly Pen GreenPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 2.2);

        private string _subject = "";
        private (string label, string value)[] _stats = Array.Empty<(string, string)>();

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _stats = ParseStats(detail);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            double fadeOut = t > FadeOutAt ? Clamp01(1 - (t - FadeOutAt) / (Duration - FadeOutAt)) : 1;
            double env = Math.Min(1, t / 0.35) * fadeOut;
            if (env <= 0) return;
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            double W = w / u, H = h / u, cx = W / 2, cy = H * 0.46;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);
            dc.DrawRectangle(Backdrop, null, new Rect(-40, -40, W + 80, H + 80));
            dc.Pop();

            dc.PushOpacity(env);
            for (double x = 10; x < W; x += 20) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, H));
            for (double y = 5; y < H; y += 20) dc.DrawLine(GridPen, new Point(0, y), new Point(W, y));

            double e = Smooth(0, ContactAt, t);
            bool contact = t >= ContactAt;
            const double s = 38;
            double d = s + 78 * Math.Pow(1 - e, 2);
            double sc = Lerp(1.7, 1, Math.Pow(e, 1.4));

            dc.PushTransform(new TranslateTransform(cx, cy + (1 - e) * 6));
            dc.PushTransform(new ScaleTransform(sc, sc));
            dc.DrawEllipse(Shadow, null, new Point(5 * (1 - e) + 2, 6), 20, 24);
            var hull = new StreamGeometry();
            using (var g = hull.Open())
            {
                g.BeginFigure(new Point(0, -24), true, true);
                foreach (var p in new[] { new Point(17, 15), new Point(6, 11), new Point(0, 18), new Point(-6, 11), new Point(-17, 15) }) g.LineTo(p, true, false);
            }
            dc.DrawGeometry(ShipFill, ShipPen, hull);
            dc.DrawRectangle(Canopy, null, new Rect(-2, -12, 4, 8));
            dc.Pop(); dc.Pop();

            var ring = new Pen(Frozen(Color.FromArgb(191, 255, 111, 210)), 1.3) { DashStyle = new DashStyle(new[] { 4.0, 3.2 }, -t * 15) };
            dc.DrawEllipse(null, ring, new Point(cx, cy), s + 22 + (1 - e) * 30, s + 22 + (1 - e) * 30);

            var pen = contact ? GreenPen : CyanPen;
            const double L = 13;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                double x = cx + sx * d, y = cy + sy * d;
                dc.DrawLine(pen, new Point(x, y - sy * L), new Point(x, y));
                dc.DrawLine(pen, new Point(x, y), new Point(x - sx * L, y));
            }

            if (!contact) DrawText(dc, "ALIGNING", Mono, 11, Cyan, cx, cy + 66, 1);
            else
            {
                double k = t - ContactAt;
                foreach (double o in new[] { 0.0, 0.25, 0.5 })
                {
                    double v = k - o;
                    if (v < 0) continue;
                    var rp = new Pen(Frozen(Color.FromArgb((byte)(180 * Math.Exp(-v * 1.5)), 125, 255, 176)), 2);
                    dc.DrawEllipse(null, rp, new Point(cx, cy), 10 + v * 120, 10 + v * 120);
                }
                if (k < 0.35) dc.DrawEllipse(Frozen(Color.FromArgb((byte)(128 * (1 - k / 0.35)), 255, 255, 255)), null, new Point(cx, cy), 60, 60);
                if (((int)(t * 4)) % 2 == 0 || t > ContactAt + 1.2) DrawText(dc, "SECURE", Mono, 11, Green, cx, cy + 66, 1);
            }
            dc.Pop();   // env

            double pt = Phase(t, 1.4, 0.7);
            if (pt > 0)
            {
                double ease = EaseOut(pt), titleY = H * 0.06;
                dc.PushOpacity(ease * fadeOut);
                dc.DrawRectangle(Strip, null, new Rect(-40, titleY - 12, W + 80, 66));
                dc.Pop();
                DrawSpaced(dc, "DOCKED", Mono, 20, Amber, cx, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
                DrawText(dc, _subject, Mono, 12, Pale, cx, titleY + 28, Phase(t, 1.9, 0.5) * fadeOut);
            }
            for (int i = 0; i < _stats.Length && i < 3; i++)
            {
                double a = Phase(t, 2.0 + i * 0.3, 0.5) * fadeOut;
                double x = cx + (i - (Math.Min(_stats.Length, 3) - 1) / 2.0) * 100, y = cy + 88;
                DrawText(dc, _stats[i].label, Mono, 9, Dim, x, y, a * 0.85);
                DrawText(dc, _stats[i].value, Mono, 12, White, x, y + 12, a);
            }
            dc.Pop();   // scale
        }
    }
}
