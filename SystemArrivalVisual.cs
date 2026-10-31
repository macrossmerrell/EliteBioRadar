using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The arrival moment of a hyperspace jump: a white flash fades to the new system's star blooming in its class colour, with corona rays and a ripple, then the
    // title ARRIVED, the system's name and the star type. About 5 seconds. Subject = system name, detail = "TYPE=<star type>|~STAR=<kelvin>" (the kelvin only picks the colour).
    public sealed class SystemArrivalVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.6;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Brush Backdrop = Frozen(Color.FromRgb(0x03, 0x06, 0x0b));
        private static readonly Brush FlashBrush = Frozen(Color.FromRgb(235, 245, 255));
        private static readonly Brush StarDot = Frozen(Color.FromArgb(150, 200, 220, 255));
        private static readonly Brush TypeBrush = Frozen(Color.FromRgb(0x9f, 0xd8, 0xd4));
        private static readonly Pen RayThin = FrozenPen(Color.FromArgb(150, 255, 200, 130), 1);
        private static readonly Pen RayThick = FrozenPen(Color.FromArgb(150, 255, 200, 130), 2);
        private static readonly (double x, double y)[] Dots = MakeDots();

        private static (double, double)[] MakeDots()
        {
            var r = new Random(4);
            var a = new (double, double)[60];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble());
            return a;
        }

        private string _subject = "", _type = "";
        private Brush _outer = null!, _inner = null!, _core = null!;
        private Pen _ring = null!, _rayThin = RayThin, _rayThick = RayThick;

        // "K-Star" for the main sequence, "L-Dwarf" for the brown dwarfs, and the class's own name (Neutron Star, Black Hole, Wolf-Rayet Star ...) for the rest.
        public static string StarTypeLabel(string starClass)
        {
            string c = (starClass ?? "").Trim();
            if (c.Length == 0) return "";
            if (c.Length == 1 && "OBAFGKM".Contains(char.ToUpperInvariant(c[0]))) return char.ToUpperInvariant(c[0]) + "-Star";
            if ((c.Length == 1 || char.IsDigit(c[1])) && "LTY".Contains(char.ToUpperInvariant(c[0]))) return char.ToUpperInvariant(c[0]) + "-Dwarf";
            return StarClassNames.GetDisplayName(c);
        }

        // A representative temperature for a star class letter, for the colour only (the journal's FSDJump names the class, not the temperature).
        public static double ClassKelvin(string starClass)
        {
            string c = (starClass ?? "").TrimStart();
            if (c.Length == 0) return 0;
            return char.ToUpperInvariant(c[0]) switch
            {
                'O' => 35000, 'B' => 20000, 'A' => 8500, 'F' => 6700, 'G' => 5700, 'K' => 4500, 'M' => 3200,
                'L' => 1800, 'T' => 1000, 'Y' => 700, 'N' => 20000, 'W' => 30000, _ => 0
            };
        }

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            var stats = ParseStats(detail);
            _type = stats.Length > 0 ? stats[0].value : "";
            var tint = ReadSurfaceTint(ParseExtras(detail));   // the colour of this star against the default (a K star)

            RadialGradientBrush Glow(Color c0)
            {
                var b = new RadialGradientBrush(Color.FromArgb(255, c0.R, c0.G, c0.B), Color.FromArgb(0, c0.R, c0.G, c0.B));
                b.Freeze(); return b;
            }
            _outer = Glow(Scale(Color.FromRgb(255, 150, 70), tint.Star));
            _inner = Glow(Scale(Color.FromRgb(255, 214, 150), tint.Star));
            _core = Frozen(Scale(Color.FromRgb(255, 246, 228), tint.Star, 0.95));
            var ray = Scale(Color.FromRgb(255, 200, 130), tint.Star);
            _rayThin = FrozenPen(Color.FromArgb(150, ray.R, ray.G, ray.B), 1);
            _rayThick = FrozenPen(Color.FromArgb(150, ray.R, ray.G, ray.B), 2);
            _ring = FrozenPen(ray, 1.5);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double cx = W / 2, cy = H / 2 - 14;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);
            dc.DrawRectangle(Backdrop, null, new Rect(-40, -40, W + 80, H + 80));

            double e = EaseOut((t - 0.5) / 1.6);
            if (e > 0)
            {
                dc.PushOpacity(e);
                foreach (var d in Dots) dc.DrawRectangle(StarDot, null, new Rect(d.x * W, d.y * H, 1.2, 1.2));
                dc.Pop();
                dc.PushOpacity(0.55 * e);
                double r1 = 30 + 80 * e; dc.DrawEllipse(_outer, null, new Point(cx, cy), r1, r1);
                dc.Pop();
                dc.PushOpacity(0.95 * e);
                double r2 = 14 + 26 * e; dc.DrawEllipse(_inner, null, new Point(cx, cy), r2, r2);
                dc.Pop();
                dc.PushOpacity(0.5 * e);
                for (int i = 0; i < 16; i++)
                {
                    double a = i * 0.3927 + 0.2, len = (20 + ((i * 37) % 9) * 7) * e * (0.8 + 0.2 * Math.Sin(t * 2 + i));
                    dc.DrawLine(i % 2 == 1 ? _rayThin : _rayThick, new Point(cx + Math.Cos(a) * 9, cy + Math.Sin(a) * 9), new Point(cx + Math.Cos(a) * (9 + len), cy + Math.Sin(a) * (9 + len)));
                }
                dc.Pop();
                dc.DrawEllipse(_core, null, new Point(cx, cy), 6 * e + 1, 6 * e + 1);
            }

            double rr = Phase(t, 0.6, 1.8);
            if (rr < 1 && rr > 0)
            {
                dc.PushOpacity(0.6 * (1 - rr));
                double rad = 10 + 110 * EaseOut(rr);
                dc.DrawEllipse(null, _ring, new Point(cx, cy), rad, rad);
                dc.Pop();
            }

            double flash = 1 - Smooth(0, 1.1, t);
            if (flash > 0.01) { dc.PushOpacity(flash); dc.DrawRectangle(FlashBrush, null, new Rect(-40, -40, W + 80, H + 80)); dc.Pop(); }
            dc.Pop();   // backdrop opacity

            double p = Phase(t, 1.8, 0.8);
            if (p > 0)
            {
                double ease = EaseOut(p);
                dc.PushOpacity(ease * 0.86 * fadeOut);
                dc.DrawRectangle(StripBrush, null, new Rect(-40, H - 74, W + 80, 58));
                dc.Pop();
                DrawSpaced(dc, "ARRIVED", MonoFace, 15, TitleBrush, W / 2, H - 70, 1.0 - 0.6 * ease, ease * fadeOut);
                DrawText(dc, _subject, MonoFace, 11, SubjectBrush, W / 2, H - 48, Phase(t, 2.2, 0.5) * fadeOut);
                DrawText(dc, _type, MonoFace, 10, TypeBrush, W / 2, H - 33, Phase(t, 2.6, 0.5) * fadeOut);
            }
            dc.Pop();   // scale
        }
    }
}
