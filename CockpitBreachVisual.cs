using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The cockpit breached (the journal's CockpitBreached, no fields): an impact flash, cyan and white cracks spread across the canopy, air and debris blow out of them, and a red plate names it.
    public sealed class CockpitBreachVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private static readonly Color Red = Color.FromRgb(0xff, 0x6b, 0x5e), Cyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Brush RedBrush = Frozen(Red), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CanopyFill = Frozen(Color.FromArgb(13, 120, 190, 255));
        private static readonly Brush[] RedAlpha = AlphaBrushes(Red);
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1), CanopyEdge = FrozenPen(Color.FromArgb(128, 120, 190, 255), 1), CrackCyan = FrozenPen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B), 1.2), CrackWhite = FrozenPen(Color.FromArgb(89, 255, 255, 255), 1.2);
        private static readonly Geometry Canopy = MakeCanopy();

        // Each crack is five points (the origin and four segments) as offsets for a crack of length 1; scaled by the crack's length and progress.
        private static readonly (double x, double y)[][] CracksA = MakeCracks(1), CracksB = MakeCracks(9);

        private static double Hs(double n) { double x = Math.Sin(n * 127.1 + 311.7) * 43758.5453; return x - Math.Floor(x); }

        private static Geometry MakeCanopy()
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(40, 92), true, true); c.QuadraticBezierTo(new Point(120, -10), new Point(200, 92), true, false); }
            g.Freeze(); return g;
        }

        private static (double x, double y)[][] MakeCracks(int seed)
        {
            var all = new (double x, double y)[9][];
            for (int i = 0; i < 9; i++)
            {
                double a = i / 9.0 * Math.PI * 2 + Hs(i + seed) * 0.5, len = 30 + Hs(i + seed + 5) * 60;
                var pts = new (double x, double y)[5];
                for (int s = 1; s <= 4; s++)
                {
                    double d = len * s / 4, j = (Hs(i * 7 + s + seed) - 0.5) * 0.5;
                    pts[s] = (Math.Cos(a + j) * d, Math.Sin(a + j) * d);
                }
                all[i] = pts;
            }
            return all;
        }

        protected override double Duration => FadeOutAt + 0.6;
        protected override void OnStart(string subject, string detail) { }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        private static void DrawCracks(DrawingContext dc, (double x, double y)[][] cracks, double cx, double cy, double p, Pen pen)
        {
            if (p <= 0) return;
            foreach (var c in cracks)
                for (int s = 1; s <= 4; s++)
                    dc.DrawLine(pen, new Point(cx + c[s - 1].x * p, cy + c[s - 1].y * p), new Point(cx + c[s].x * p, cy + c[s].y * p));
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.DrawGeometry(CanopyFill, CanopyEdge, Canopy);

            if (t > 0.3)
            {
                double fl = Math.Max(0, 1 - Phase(t, 0.3, 0.5)) * 0.35;
                if (fl > 0.02) dc.DrawRectangle(RedAlpha[(int)Math.Round(fl * 8)], null, new Rect(-40, -40, W + 80, H + 80));
                DrawCracks(dc, CracksA, 120, 48, EaseOut(Phase(t, 0.3, 0.8)), CrackCyan);
                DrawCracks(dc, CracksB, 120, 48, EaseOut(Phase(t, 0.5, 0.8)), CrackWhite);
            }
            if (t > 0.5)
                for (int i = 0; i < 14; i++)
                {
                    double q = (t * 1.6 + Hs(i)) % 1;
                    dc.DrawRectangle(Level(Whites, (1 - q) * 0.7), null, new Rect(120 + (Hs(i + 2) - 0.5) * 30 + q * (Hs(i + 4) - 0.5) * 200, 48 - q * 10, 1.5, 1.5));
                }

            string l1 = "COCKPIT BREACHED", l2 = "CANOPY INTEGRITY LOST", l3 = "RAPID DEPRESSURIZATION";
            double pa = Phase(t, 1.4, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 180), RedBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, 1.6, 0.4));
                DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), RedBrush, SceneW / 2, 134, 0.08, Phase(t, 1.8, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
