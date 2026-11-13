using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A screenshot was taken: a white flash, then an instant photo drops out of a slot, the picture developing from dark and the frame number written on its border.
    // Detail = "frame|body or system|second line|surface (1 = on a planet)".
    public sealed class ScreenshotVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Brush Solid = Frozen(Cyan), Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a));
        private static readonly Brush Photo = Frozen(Color.FromRgb(0x0a, 0x14, 0x20)), Ground = Frozen(Color.FromRgb(0x3a, 0x32, 0x26)), Planet = Frozen(Color.FromRgb(0x2a, 0x4a, 0x6a)), Cap = Frozen(Color.FromRgb(0x4a, 0x7a, 0xa0));
        private static readonly Brush Star = Frozen(Color.FromRgb(230, 240, 255)), SlotBrush = Frozen(Color.FromRgb(0x2a, 0x34, 0x38)), Flash = Frozen(Colors.White);
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1), SlotPen = FrozenPen(Color.FromRgb(0x7f, 0xa0, 0xaa), 1);

        // The picture's ground line: the photo area never changes, so the shape is built once.
        private static readonly Geometry GroundShape = MakeGround();
        private static Geometry MakeGround()
        {
            var area = new Rect(-39, -86 / 2.0 + 7, 78, 58);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(area.Left, area.Top + area.Height * 0.6), true, true);
                c.QuadraticBezierTo(new Point(area.Left + area.Width / 2, area.Top + area.Height * 0.35), new Point(area.Right, area.Top + area.Height * 0.62), true, false);
                c.LineTo(new Point(area.Right, area.Bottom), true, false);
                c.LineTo(new Point(area.Left, area.Bottom), true, false);
            }
            g.Freeze();
            return g;
        }

        private string _frame = "", _name = "", _sub = "";
        private bool _surface;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _frame = P(0); _name = P(1).ToUpperInvariant(); _sub = P(2).ToUpperInvariant(); _surface = P(3) == "1";
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

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

            // the slot
            double slotA = EaseOut(Phase(t, 0.05, 0.3));
            if (slotA > 0)
            {
                dc.PushOpacity(slotA);
                dc.DrawRoundedRectangle(SlotBrush, SlotPen, new Rect(70, 6, 100, 10), 2, 2);
                dc.Pop();
            }

            // the print drops out of the slot
            double drop = EaseOut(Phase(t, 0.55, 0.8));
            if (drop > 0)
            {
                const double pw = 92, ph = 86;
                double top = 12 + drop * 6 - (1 - drop) * ph * 0.9;
                dc.PushClip(ClipOf(new Rect(60, 11, 120, 100)));
                dc.PushTransform(new TranslateTransform(120, top + ph / 2));
                dc.PushTransform(new RotateTransform(0.04 * 57.3 * 0.5));
                dc.DrawRectangle(Paper, null, new Rect(-pw / 2, -ph / 2, pw, ph));
                // the picture, developing from dark
                var area = new Rect(-39, -ph / 2 + 7, 78, 58);
                dc.PushClip(ClipOf(area));
                dc.DrawRectangle(Photo, null, area);
                for (int i = 0; i < 18; i++)
                {
                    double sx = area.X + ((i * 53) % 100) / 100.0 * area.Width, sy = area.Y + ((i * 29) % 100) / 100.0 * area.Height * 0.7;
                    dc.DrawRectangle(Star, null, new Rect(sx, sy, 1.2, 1.2));
                }
                if (_surface)
                {
                    dc.DrawGeometry(Ground, null, GroundShape);
                }
                else
                {
                    dc.DrawEllipse(Planet, null, new Point(area.Left + area.Width * 0.7, area.Top + area.Height * 0.95), 40, 40);
                    dc.DrawEllipse(Cap, null, new Point(area.Left + area.Width * 0.7 - 6, area.Top + area.Height * 0.95 - 8), 22, 18);
                }
                // undeveloped haze that clears
                double dev = Phase(t, 1.2, 1.0);
                if (dev < 1) dc.DrawRectangle(Level(Backdrops, 1 - dev), null, area);
                dc.Pop();
                DrawText(dc, "No. " + _frame, MonoFace, 7, Ink, 0, ph / 2 - 15, Phase(t, 1.6, 0.5), TextAlignment.Center);
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            // the flash
            double fl = Phase(t, 0.5, 0.35);
            if (fl > 0 && fl < 1)
            {
                dc.PushOpacity(0.85 * (1 - fl));
                dc.DrawRectangle(Flash, null, new Rect(-40, -40, W + 80, H + 80));
                dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "SCREENSHOT TAKEN", MonoFace, 12, Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
