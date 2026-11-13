using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The self destruct (the journal's SelfDestruct, which carries no fields): the screen pulses red, a huge two-digit countdown runs from 15 (white for the last five seconds, the pulse quickening) between two bands of
    // scrolling yellow and black hazard stripes, and a white flash lands at zero. The 15 seconds is the game's countdown as best known; the journal does not report one, so this runs on its own clock.
    public sealed class SelfDestructVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, Countdown = 15, CountStart = 0.5, CountLen = 15, FadeOutAt = 16.0;
        private const double Base = 70, CapTop = Base - 35, Gap = 7.5, BandH = 15;
        private static readonly Brush[] RedWash = MakeLevels(Color.FromRgb(255, 40, 30));
        private static readonly Brush RedBrush = Frozen(Color.FromRgb(255, 74, 58)), WhiteBrush2 = Frozen(Colors.White), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)),
            PlateBrush = Frozen(Color.FromArgb(224, 20, 5, 5)), DarkBand = Frozen(Color.FromRgb(0x1a, 0x14, 0x10)), Yellow = Frozen(Color.FromRgb(0xe8, 0xb0, 0x20));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(179, 255, 74, 58), 1);
        private static readonly Geometry Stripe = MakeStripe();

        private static Brush[] MakeLevels(Color c)
        {
            var a = new Brush[33];
            for (int i = 0; i < a.Length; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round(255.0 * i / 32), c.R, c.G, c.B));
            return a;
        }
        private static Geometry MakeStripe() => Poly(new[] { (0.0, BandH), (12.0, BandH), (12.0 + BandH, 0.0), (BandH, 0.0) });

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail) { }

        private static int Seconds(double t) => (int)Math.Max(0, Math.Ceiling(Countdown * (1 - Phase(t, CountStart, CountLen))));
        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        // One band of hazard stripes across the whole window, scrolling left or right. Called inside the scene's scale, with x running a little past the window on both sides.
        private void DrawBand(DrawingContext dc, double left, double right, double y, double t, int dir)
        {
            dc.PushClip(new RectangleGeometry(new Rect(left, y, right - left, BandH)));
            dc.DrawRectangle(DarkBand, null, new Rect(left, y, right - left, BandH));
            double off = ((t * 25 * dir) % 24 + 24) % 24;
            for (double sx = left - BandH - 24; sx < right + 24; sx += 24)
            {
                dc.PushTransform(new TranslateTransform(sx + off, y));
                dc.DrawGeometry(Yellow, null, Stripe);
                dc.Pop();
            }
            dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            int secs = Seconds(t);
            double hz = secs <= 5 ? 2.5 : 1.2, pulse = 0.5 + 0.5 * Math.Sin(t * Math.PI * 2 * hz);

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.DrawRectangle(RedWash[(int)Math.Round((0.05 + 0.18 * pulse) * 32)], null, new Rect(-40, -40, W + 80, H + 80));

            // The stripes run the full width of the window, so they are drawn in the scene's scale but not its horizontal offset.
            dc.PushTransform(new TranslateTransform(0, py));
            dc.PushTransform(new ScaleTransform(k, k));
            DrawBand(dc, -20, W / k + 20, CapTop - Gap - BandH, t, 1);
            DrawBand(dc, -20, W / k + 20, Base + Gap, t, -1);
            dc.Pop(); dc.Pop();

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            DrawSpaced(dc, secs.ToString("00", CultureInfo.InvariantCulture), MonoFace, 48, secs <= 5 ? WhiteBrush2 : RedBrush, SceneW / 2, Base - 46.5, 0.02, Phase(t, 0.1, 0.3));

            string l1 = "SELF DESTRUCT INITIATED", l2 = secs > 0 ? "DETONATION IN " + secs.ToString(CultureInfo.InvariantCulture) + (secs == 1 ? " SECOND" : " SECONDS") : "DETONATION";
            double pa = Phase(t, 0.3, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(30, 110, 180, 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 10, 170), RedBrush, SceneW / 2, 113, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 8, 170), NameBrush, SceneW / 2, 127, 0.08, pa);
            }
            dc.Pop(); dc.Pop();

            // The flash at zero.
            double end = CountStart + CountLen, f = Phase(t, end, 0.3) * (1 - Phase(t, end + 0.3, 0.4));
            if (f > 0) dc.DrawRectangle(Level(Whites, f), null, new Rect(-40, -40, W + 80, H + 80));

            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
