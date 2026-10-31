using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Shared by the two HUD-mode screens: a short banner moment (about 3 seconds) played when the commander switches the ship's HUD between Combat and Analysis mode.
    public abstract class HudModeVisual : AnimationVisual
    {
        private const double FadeOutAt = 2.3;
        protected override double Duration => FadeOutAt + 0.7;
        protected abstract void Draw(DrawingContext dc, double W, double H, double t, double env, double fadeOut);

        protected override void OnStart(string subject, string detail) { }

        protected static Pen[] MakeOutlines(Color c) { var a = new Pen[9]; for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)(i * 32 - (i == 8 ? 1 : 0)), c.R, c.G, c.B), 1.5); return a; }
        protected static Pen Fade(Pen[] set, double alpha) => set[(int)Math.Round(Clamp01(alpha) * 8)];

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            Draw(dc, W, H, t, env, fadeOut);
            dc.Pop();
        }
    }

    // Combat mode: an orange sweep crosses the screen, the edges pulse red, and a hazard-striped banner drops in with COMBAT MODE.
    public sealed class CombatModeVisual : HudModeVisual
    {
        private static readonly Brush Plate = Frozen(Color.FromArgb(0xe6, 8, 13, 13));
        private static readonly Brush TitleOrange = Frozen(Color.FromRgb(255, 150, 90));
        private static readonly Pen[] Outlines = MakeOutlines(Color.FromRgb(255, 120, 60));
        private static readonly Pen Stripe = FrozenPen(Color.FromArgb(90, 255, 120, 60), 7);
        private static readonly Brush SweepLine = Frozen(Color.FromArgb(130, 255, 150, 90));
        private static readonly Brush SweepTrail = MakeTrail(Color.FromRgb(255, 90, 40), 72);
        private static readonly Brush[] Vignettes = MakeVignettes();

        private static Brush MakeTrail(Color c, byte a)
        {
            var b = new LinearGradientBrush(Color.FromArgb(0, c.R, c.G, c.B), Color.FromArgb(a, c.R, c.G, c.B), 0);
            b.Freeze(); return b;
        }

        private static Brush[] MakeVignettes()
        {
            var a = new Brush[9];
            for (int i = 0; i < a.Length; i++)
            {
                var b = new RadialGradientBrush();
                b.RadiusX = 0.9; b.RadiusY = 0.9;
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 40, 20), 0.3));
                b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(i * 10), 255, 40, 20), 1));
                b.Freeze(); a[i] = b;
            }
            return a;
        }

        protected override void Draw(DrawingContext dc, double W, double H, double t, double env, double fadeOut)
        {
            double cx = W / 2, cy = H / 2, a = Clamp01(t / 0.3) * fadeOut;

            double pulse = 0.6 + 0.4 * Math.Sin(t * 7);
            dc.DrawRectangle(Vignettes[(int)Math.Round(Clamp01(a * pulse) * 8)], null, new Rect(0, 0, W, H));

            if (t < 1.1)
            {
                double sx = -30 + t / 1.1 * (W + 60);
                dc.DrawRectangle(SweepTrail, null, new Rect(sx - 70, 0, 70, H));
                dc.DrawRectangle(SweepLine, null, new Rect(sx - 2, 0, 4, H));
            }

            // The hazard band runs the full width of the screen; the title sits on a lighter plate in the middle so the stripes show through around it.
            double halfW = Math.Min(W * 0.34, 105), halfH = 18;
            var band = new Rect(-40, cy - halfH, W + 80, halfH * 2);
            dc.DrawRectangle(Level(Strips, 0.8 * a), null, band);
            dc.PushClip(new RectangleGeometry(band));
            for (double x = -60 + (t * 30) % 28; x < W + 40; x += 28) dc.DrawLine(Stripe, new Point(x, cy - halfH), new Point(x + 17, cy + halfH));
            dc.Pop();
            dc.DrawRectangle(Level(Backdrops, a), null, new Rect(cx - halfW, cy - halfH + 2, halfW * 2, halfH * 2 - 4));
            var edge = Fade(Outlines, a);
            dc.DrawRectangle(null, edge, new Rect(cx - halfW, cy - halfH + 2, halfW * 2, halfH * 2 - 4));
            dc.DrawLine(edge, new Point(-40, cy - halfH), new Point(W + 40, cy - halfH));
            dc.DrawLine(edge, new Point(-40, cy + halfH), new Point(W + 40, cy + halfH));
            DrawSpaced(dc, "COMBAT MODE", MonoFace, 16, TitleOrange, cx, cy - 10, 0.4, a);
        }
    }

    // Analysis mode: a cyan scan line crosses a faint grid and reveals contact markers as it passes, then the title settles in.
    public sealed class AnalysisModeVisual : HudModeVisual
    {
        private static readonly Brush TitleCyan = Frozen(Color.FromRgb(150, 235, 255));
        private static readonly Pen GridPen = FrozenPen(Color.FromArgb(34, 90, 220, 255), 1);
        private static readonly Pen[] Outlines = MakeOutlines(Color.FromRgb(90, 220, 255));
        private static readonly Brush ScanLine = Frozen(Color.FromArgb(230, 180, 245, 255));
        private static readonly Brush ScanTrail = MakeTrail();
        private static readonly Pen[] MarkerPens = MakePens();
        private static readonly Brush[] MarkerDots = MakeDots();
        private static readonly double[] HashA = MakeHash(12.9898), HashB = MakeHash(3.7);
        private static double[] MakeHash(double k) { var a = new double[12]; for (int i = 0; i < a.Length; i++) { double v = Math.Sin(i * k) * 43758.5453; a[i] = v - Math.Floor(v); } return a; }

        private static Brush MakeTrail()
        {
            var b = new LinearGradientBrush(Color.FromArgb(0, 90, 220, 255), Color.FromArgb(90, 90, 220, 255), 0);
            b.Freeze(); return b;
        }
        private static Pen[] MakePens() { var a = new Pen[5]; for (int i = 0; i < 5; i++) a[i] = FrozenPen(Color.FromArgb((byte)(i * 51), 90, 220, 255), 1); return a; }
        private static Brush[] MakeDots() { var a = new Brush[5]; for (int i = 0; i < 5; i++) a[i] = Frozen(Color.FromArgb((byte)(i * 51), 140, 235, 255)); return a; }

        protected override void Draw(DrawingContext dc, double W, double H, double t, double env, double fadeOut)
        {
            double cx = W / 2, cy = H / 2, a = Clamp01(t / 0.3) * fadeOut;
            const double SweepSeconds = 1.3;

            for (double x = 0; x <= W; x += 24) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, H));
            for (double y = 0; y <= H; y += 24) dc.DrawLine(GridPen, new Point(0, y), new Point(W, y));

            double sx = t / SweepSeconds * (W + 40) - 20;
            if (t < SweepSeconds)
            {
                dc.DrawRectangle(ScanTrail, null, new Rect(sx - 110, 0, 110, H));
                dc.DrawRectangle(ScanLine, null, new Rect(sx - 1, 0, 2, H));
            }

            // Contact markers appear as the scan line reaches them and fade with the screen.
            for (int i = 0; i < 12; i++)
            {
                double x = 14 + HashA[i] * (W - 28), y = 12 + HashB[i] * (H - 24);
                double k = Clamp01((t - x / (W + 40) * SweepSeconds) / 0.4) * fadeOut;
                if (k <= 0) continue;
                int lvl = (int)Math.Round(k * 4);
                dc.DrawRectangle(null, MarkerPens[lvl], new Rect(x - 5, y - 5, 10, 10));
                dc.DrawRectangle(MarkerDots[lvl], null, new Rect(x - 1.3, y - 1.3, 2.6, 2.6));
            }

            double halfW = Math.Min(W * 0.4, 130), halfH = 17;
            dc.DrawRectangle(Level(Strips, 0.85 * a), Fade(Outlines, a), new Rect(cx - halfW, cy - halfH, halfW * 2, halfH * 2));
            DrawSpaced(dc, "ANALYSIS MODE", MonoFace, 16, TitleCyan, cx, cy - 10, 0.4, a);
        }
    }
}
