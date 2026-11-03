using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Overheating warning: a red warning triangle with a flame pops in and pulses, rings spread out from it one after another, and the screen edges flash red.
    // Title OVERHEATING on a red-tinted plate. About 3.6 seconds. No detail.
    public sealed class OverheatVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 54;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Red = Color.FromRgb(0xff, 0x46, 0x32), Flame = Color.FromRgb(0xff, 0x8c, 0x32);
        private static readonly Brush RedBrush = Frozen(Red), FlameBrush = Frozen(Flame), TintBrush = Frozen(Color.FromArgb(38, Red.R, Red.G, Red.B));
        private static readonly Pen TrianglePen = RoundPen(Red, 3);
        private static readonly Pen[] Ring = MakeRings();
        private static readonly Geometry Triangle = MakeTriangle(), FlameShape = MakeFlame();

        private static Pen RoundPen(Color c, double w) { var p = new Pen(Frozen(c), w) { LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

        private static Pen[] MakeRings()
        {
            var a = new Pen[9];
            for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), Red.R, Red.G, Red.B), 2);
            return a;
        }

        private static Geometry MakeTriangle()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -30), true, true);
                c.LineTo(new Point(32, 24), true, false);
                c.LineTo(new Point(-32, 24), true, false);
            }
            g.Freeze();
            return g;
        }

        private static Geometry MakeFlame()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -12), true, true);
                c.BezierTo(new Point(10, -2), new Point(12, 6), new Point(6, 14), true, false);
                c.BezierTo(new Point(4, 10), new Point(2, 10), new Point(0, 6), true, false);
                c.BezierTo(new Point(-2, 10), new Point(-4, 10), new Point(-6, 14), true, false);
                c.BezierTo(new Point(-12, 6), new Point(-10, -2), new Point(0, -12), true, false);
            }
            g.Freeze();
            return g;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double pop = EaseOut(Phase(t, 0.15, 0.5)), pulse = 1 + 0.06 * Math.Sin(t * 12) * Phase(t, 0.8, 0.2), blink = LowFuelVisual.Blink(t, 0.9, 16);

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);

            double ed = LowFuelVisual.Blink(t, 1.2, 14) * Phase(t, 1.2, 0.3);
            if (ed > 0.01) dc.DrawRectangle(LowFuelVisual.Edge[(int)Math.Round(ed * 8)], null, new Rect(0, 0, W, H));

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // Rings leave the triangle one after another, each fading as it widens.
            for (int i = 0; i < 3; i++)
            {
                if (t <= 0.4 + i * 0.5) continue;
                double q = ((t - 0.4 - i * 0.5) % 1.5) / 1.5, r = 24 + q * 34;
                dc.DrawEllipse(null, Ring[(int)Math.Round((1 - q) * 0.6 * 8)], new Point(CentreX, CentreY), r, r);
            }

            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.PushTransform(new ScaleTransform(Math.Max(0.001, pop * pulse), Math.Max(0.001, pop * pulse)));
            dc.DrawGeometry(TintBrush, TrianglePen, Triangle);
            dc.DrawGeometry(FlameBrush, null, FlameShape);
            dc.Pop(); dc.Pop();

            // The plate: red-tinted, one word.
            dc.DrawRoundedRectangle(LowFuelVisual.CritPlateBrush, LowFuelVisual.CritPlateEdge, new Rect(46, 108, 148, 28), 6, 6);
            DrawSpaced(dc, "OVERHEATING", MonoFace, 13, RedBrush, SceneW / 2, 114, 0.15, 0.55 + 0.45 * blink);

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
