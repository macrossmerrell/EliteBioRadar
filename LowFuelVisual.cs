using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Low fuel warning: a tank bar draining to a blinking red marker. LOW (below 25%): amber bar that turns red as it crosses the marker, title LOW FUEL.
    // CRITICAL (below 10%): red throughout, faster blink, the bar shakes as it lands, the screen edges flash red, title FUEL CRITICAL on a red-tinted plate.
    // About 3.6 seconds. Detail = "LOW" or "CRITICAL".
    public sealed class LowFuelVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, BarX = 40, BarY = 34, BarW = 160, BarH = 26;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Amber = Color.FromRgb(0xf2, 0xb8, 0x4b), Red = Color.FromRgb(0xff, 0x5a, 0x46);
        private static readonly Brush AmberBrush = Frozen(Amber), RedBrush = Frozen(Red), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush AmberFill = Frozen(Color.FromArgb(217, Amber.R, Amber.G, Amber.B)), RedFill = Frozen(Color.FromArgb(217, Red.R, Red.G, Red.B));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)); internal static readonly Brush CritPlateBrush = Frozen(Color.FromArgb(204, 40, 6, 4));
        private static readonly Pen AmberPlateEdge = FrozenPen(Color.FromArgb(115, Amber.R, Amber.G, Amber.B), 1), RedPlateEdge = FrozenPen(Color.FromArgb(115, Red.R, Red.G, Red.B), 1); internal static readonly Pen CritPlateEdge = FrozenPen(Color.FromArgb(204, Red.R, Red.G, Red.B), 1.5);
        private static readonly Pen AmberOutline = FrozenPen(Color.FromArgb(217, Amber.R, Amber.G, Amber.B), 2), RedOutline = FrozenPen(Color.FromArgb(217, Red.R, Red.G, Red.B), 2), TickPen = FrozenPen(Color.FromArgb(77, 255, 255, 255), 1);
        private static readonly Pen[] Marker = MakeMarkers();
        internal static readonly Brush[] Edge = MakeEdges();

        private static Pen[] MakeMarkers()
        {
            var a = new Pen[9];
            for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), Red.R, Red.G, Red.B), 2);
            return a;
        }

        // Red vignette at several strengths, clear in the middle and strongest at the corners.
        private static Brush[] MakeEdges()
        {
            var a = new Brush[9];
            for (int i = 0; i < 9; i++)
            {
                var b = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.75 };
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, Red.R, Red.G, Red.B), 0.5));
                b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(72.0 * i / 8), Red.R, Red.G, Red.B), 1));
                b.Freeze();
                a[i] = b;
            }
            return a;
        }

        internal static double Blink(double t, double start, double freq) => t < start ? 0 : (Math.Sin((t - start) * freq) > 0 ? 1 : 0.3);

        private bool _crit;

        protected override void OnStart(string subject, string detail) => _crit = detail.Contains("CRIT", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double from = _crit ? 0.25 : 1, to = _crit ? 0.08 : 0.25, mark = _crit ? 0.10 : 0.25;
            double lv = from + (to - from) * EaseOut(Phase(t, 0.2, 1.4));
            bool red = _crit || lv <= mark;
            double blinkFreq = _crit ? 22 : 14;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);

            if (_crit)
            {
                double ed = Blink(t, 1.3, 18) * Phase(t, 1.3, 0.2);
                if (ed > 0.01) dc.DrawRectangle(Edge[(int)Math.Round(ed * 8)], null, new Rect(0, 0, W, H));
            }

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            double shake = _crit && t > 1.4 ? Math.Sin(t * 50) * 1.2 * Math.Max(0, 1 - (t - 1.4) / 1.2) : 0;
            dc.PushTransform(new TranslateTransform(shake, 0));
            dc.DrawRoundedRectangle(null, _crit && t > 1.4 ? RedOutline : AmberOutline, new Rect(BarX, BarY, BarW, BarH), 5, 5);
            double fw = Math.Max(0, (BarW - 6) * lv);
            if (fw > 0.5) dc.DrawRoundedRectangle(red ? RedFill : AmberFill, null, new Rect(BarX + 3, BarY + 3, fw, BarH - 6), 3, 3);
            for (int i = 1; i < 4; i++) dc.DrawLine(TickPen, new Point(BarX + BarW * i / 4, BarY + BarH + 2), new Point(BarX + BarW * i / 4, BarY + BarH + 7));
            double mx = BarX + BarW * mark;
            dc.DrawLine(Marker[(int)Math.Round((0.5 + 0.5 * Blink(t, 1.4, blinkFreq)) * 8)], new Point(mx, BarY - 6), new Point(mx, BarY + BarH + 9));
            double px2 = BarX + BarW * Math.Max(0.1, Math.Min(0.9, lv <= mark ? mark : lv));
            DrawSpaced(dc, Math.Round(lv * 100) + "%", MonoFace, 11, red ? RedBrush : AmberBrush, px2, BarY + BarH + 11, 0, 1);
            DrawSpaced(dc, "E", MonoFace, 11, MutedBrush, BarX + 2, BarY - 20, 0, 1);
            DrawSpaced(dc, "F", MonoFace, 11, MutedBrush, BarX + BarW - 2, BarY - 20, 0, 1);
            dc.Pop();

            // The plate: red-tinted when critical, otherwise the usual dark backing with an amber edge that goes red below the marker.
            dc.DrawRoundedRectangle(_crit ? CritPlateBrush : PlateBrush, _crit ? CritPlateEdge : red ? RedPlateEdge : AmberPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _crit ? "FUEL CRITICAL" : "LOW FUEL", MonoFace, 13, red ? RedBrush : AmberBrush, SceneW / 2, 112, 0.15, 0.55 + 0.45 * Blink(t, 0.9, blinkFreq));
            DrawSpaced(dc, _crit ? "BELOW 10% - REFUEL NOW" : "BELOW 25%", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.2, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
