using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace EliteBioRadar
{
    // Night vision switching on or off: a small green-phosphor screen where static grain resolves into a soft dark landscape (one dim jagged range, a plain, rocks) after a green
    // flash, scan lines and corner brackets locking on; switching off reverses it. About 3.8 seconds. Detail = "ON" or "OFF".
    public sealed class NightVisionVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.2, SceneW = 240, SceneH = 150, BakeScale = 4;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Green = Color.FromRgb(0x55, 0xff, 0x7a), Pale = Color.FromRgb(0xa6, 0xff, 0xbd), Panel = Color.FromRgb(0x05, 0x0b, 0x07);
        private static readonly Brush PanelBrush = Frozen(Panel), GreenBrush2 = Frozen(Green), OffBrush = Frozen(Color.FromRgb(0x44, 0x55, 0x66));
        private static readonly Brush[] Fade = MakeLevels(Panel, 255, 32), Flash = MakeLevels(Green, 128, 16), Grain = MakeLevels(Pale, 204, 8);
        private static readonly Brush[] Lines = MakeLines();
        private static readonly Pen PanelEdge = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1.5), BracketPen = FrozenPen(Green, 2);
        private static readonly Geometry Brackets = Geometry.Parse("M12,30 V12 H30 M228,30 V12 H210 M12,120 V138 H30 M228,120 V138 H210");
        private static ImageSource? _land;

        private static Brush[] MakeLevels(Color c, int max, int steps)
        {
            var a = new Brush[steps + 1];
            for (int i = 0; i <= steps; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round((double)max * i / steps), c.R, c.G, c.B));
            return a;
        }

        private static Brush[] MakeLines()
        {
            var a = new Brush[4];
            for (int i = 0; i < a.Length; i++)
            {
                var d = new GeometryDrawing(Frozen(Color.FromArgb((byte)(72 * i / 3), 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 2)));
                d.Freeze();
                var b = new DrawingBrush(d) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 4, 4), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4) };
                b.Freeze();
                a[i] = b;
            }
            return a;
        }

        // The landscape is drawn once, softened with a blur, and kept as a bitmap.
        private static ImageSource BakeLand()
        {
            var dv = new DrawingVisual { Effect = new BlurEffect { Radius = 3.2 } };
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(BakeScale, BakeScale));
                Fill(dc, "M0,98 L30,84 L52,94 L84,76 L120,96 L158,82 L196,96 L240,86 L240,150 L0,150Z", Color.FromRgb(0x1d, 0x6b, 0x35), 0.4);
                Fill(dc, "M0,112 Q60,106 120,114 T240,110 L240,150 L0,150Z", Color.FromRgb(0x2f, 0xae, 0x55), 0.5);
                Fill(dc, "M0,134 Q70,126 140,136 T240,132 L240,150 L0,150Z", Green, 0.35);
                foreach (var (x, y, rx, ry) in new[] { (40.0, 126.0, 9.0, 5.0), (78, 134, 5, 3), (130, 122, 6, 3.5), (176, 132, 10, 5.5), (215, 124, 4, 2.5) })
                {
                    dc.DrawEllipse(Frozen(Color.FromArgb(140, 0x7f, 0xf2, 0x9b)), null, new Point(x, y), rx, ry);
                    dc.DrawEllipse(Frozen(Color.FromArgb(128, 0x0f, 0x4a, 0x24)), null, new Point(x, y + ry * 0.7), rx, ry * 0.45);
                }
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap((int)(SceneW * BakeScale), (int)(SceneH * BakeScale), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static void Fill(DrawingContext dc, string path, Color c, double opacity)
        {
            var g = Geometry.Parse(path);
            g.Freeze();
            dc.DrawGeometry(Frozen(Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B)), null, g);
        }

        private readonly Random _rng = new();
        private bool _on = true;

        protected override void OnStart(string subject, string detail) => _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            _land ??= BakeLand();
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double run = Phase(t, 0.1, 1.4), p = _on ? run : 1 - run;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            var screen = new Rect(0, 0, SceneW, SceneH);
            dc.DrawRoundedRectangle(PanelBrush, PanelEdge, screen, 8, 8);
            dc.PushClip(new RectangleGeometry(screen, 8, 8));

            double landOp = Clamp01((p - 0.35) / 0.65);
            dc.DrawImage(_land, screen);
            dc.DrawRectangle(Fade[(int)Math.Round((1 - landOp) * 32)], null, screen);

            double grain = 1 - p;
            if (grain > 0.002)
                for (int i = 0; i < 70; i++)
                    dc.DrawRectangle(Grain[(int)Math.Round(grain * _rng.NextDouble() * 0.8 * 8)], null, new Rect(_rng.NextDouble() * 238, _rng.NextDouble() * 148, 2, 2));

            double flash = Math.Max(0, 0.5 * Math.Sin(Math.Min(1, p * 1.6) * Math.PI));
            if (flash > 0.01) dc.DrawRectangle(Flash[(int)Math.Round(flash / 0.5 * 16)], null, screen);
            if (p > 0.7) dc.DrawRectangle(Lines[(int)Math.Round((p - 0.7) / 0.3 * 3)], null, screen);
            dc.Pop();   // clip
            if (p > 0.75) dc.DrawGeometry(null, BracketPen, Brackets);

            DrawSpaced(dc, _on ? "NIGHT VISION ACTIVE" : "NIGHT VISION OFF", MonoFace, 11, _on ? GreenBrush2 : OffBrush, SceneW / 2, 8, 0.15, 1);
            dc.Pop();   // scale
            dc.Pop();   // translate
            if (a < 0.999) dc.Pop();   // opacity
            dc.Pop();   // u
        }
    }
}
