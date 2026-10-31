using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The First Footfall moment: the commander steps onto a world nobody has walked on. A close top-down view of the ground at the star's low light: a boot print
    // presses into the dust (its rim lit from the star's side), dust puffs roll out, amber brackets lock on, then the title, the body's name and a line of text.
    // About 5.6 seconds. The ground, dust and light take the planet's own colour and its star's colour, as the Landed screen does.
    // Subject = body name, detail = the hidden "~STAR / ~FLUX / ~GROUND" entries (see AnimationVisual.ReadSurfaceTint).
    public sealed class FirstFootfallVisual : AnimationVisual
    {
        private const double FadeOutAt = 5.0;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Geometry Print = MakePrint();
        private static readonly Pen TreadPen = FrozenPen(Color.FromArgb(140, 8, 5, 4), 1.2);
        private static readonly Pen BracketPen = FrozenPen(Color.FromRgb(0xf2, 0xb8, 0x4b), 2);
        private static readonly Brush RockBrush = Frozen(Color.FromArgb(128, 20, 14, 12));
        private static readonly Brush TagBrush = Frozen(Color.FromRgb(0x9f, 0xd8, 0xd4));

        // Specks and rocks are laid out once, as fractions of the area.
        private static readonly (double x, double y, double w, double h, int level)[] Specks = MakeSpecks();
        private static readonly (double x, double y, double rx, double ry)[] Rocks = MakeRocks();

        private static Geometry MakePrint()
        {
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            g.Children.Add(new EllipseGeometry(new Point(0, 20), 8.5, 7.5));      // heel
            g.Children.Add(new EllipseGeometry(new Point(0, -12), 12, 19));       // ball and toes
            g.Freeze(); return g;
        }

        private static (double, double, double, double, int)[] MakeSpecks()
        {
            var r = new Random(5);
            var a = new (double, double, double, double, int)[140];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble(), 1 + r.NextDouble() * 2, 1 + r.NextDouble(), r.Next(4));
            return a;
        }

        private static (double, double, double, double)[] MakeRocks()
        {
            var r = new Random(5);
            var a = new (double, double, double, double)[9];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), 0.35 + r.NextDouble() * 0.6, 4 + r.NextDouble() * 7, 2 + r.NextDouble() * 3);
            return a;
        }

        private string _subject = "";
        private Brush _ground = null!, _light = null!, _printFill = null!, _printRim = null!;
        private readonly Brush[] _speck = new Brush[4];
        private readonly Brush[] _dustLevels = new Brush[17];   // dust at 0/16 .. 16/16 of its strength (opacity layers are costly, so the fade steps between ready-made brushes)
        private Pen _ring = null!;

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            var tint = ReadSurfaceTint(ParseExtras(detail));

            // Default look: the warm brown ground of the mock-up; a known ground colour takes over 70% of it, darkened for a low-light view.
            Color top = Color.FromRgb(0x3a, 0x2c, 0x26), bottom = Color.FromRgb(0x1e, 0x16, 0x14);
            Color dust = Scale(Color.FromRgb(196, 150, 118), tint.Star);
            if (tint.Ground is Color pc)
            {
                var dark = Color.FromRgb((byte)(pc.R * 0.34), (byte)(pc.G * 0.34), (byte)(pc.B * 0.34));
                top = Mix(top, dark, 0.7); bottom = Mix(bottom, Scale(dark, (0.55, 0.55, 0.55)), 0.7);
                dust = Mix(dust, Color.FromRgb((byte)Math.Min(255, pc.R * 1.25), (byte)Math.Min(255, pc.G * 1.15), (byte)Math.Min(255, pc.B * 1.05)), 0.6);
            }
            var g = new LinearGradientBrush(top, bottom, 90);
            g.Freeze(); _ground = g;

            var lightColour = Scale(Color.FromRgb(255, 190, 120), tint.Star);
            var l = new LinearGradientBrush(Color.FromArgb(0, lightColour.R, lightColour.G, lightColour.B), Color.FromArgb((byte)(33 * Math.Clamp(tint.Lit, 0.6, 1.0)), lightColour.R, lightColour.G, lightColour.B), 0);
            l.Freeze(); _light = l;

            var speckBase = Mix(Color.FromRgb(190, 150, 120), dust, 0.4);
            for (int i = 0; i < _speck.Length; i++) _speck[i] = Frozen(Color.FromArgb((byte)(255 * (0.05 + i * 0.045)), speckBase.R, speckBase.G, speckBase.B));

            // The print: darker than the ground around it, its rim lit on the side facing the star.
            _printFill = Frozen(Color.FromArgb(209, (byte)(top.R * 0.28), (byte)(top.G * 0.28), (byte)(top.B * 0.28)));
            var rim = Scale(Color.FromRgb(255, 205, 150), tint.Star, Math.Clamp(tint.Lit, 0.7, 1.1));
            _printRim = Frozen(Color.FromArgb(107, rim.R, rim.G, rim.B));

            for (int i = 0; i < _dustLevels.Length; i++)
            {
                var b = new RadialGradientBrush(Color.FromArgb((byte)(255 * i / 16), dust.R, dust.G, dust.B), Color.FromArgb(0, dust.R, dust.G, dust.B));
                b.Freeze(); _dustLevels[i] = b;
            }
            _ring = FrozenPen(Color.FromRgb(0xf2, 0xb8, 0x4b), 1.6);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double cx = W * 0.47, cy = H * 0.59;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);
            var area = new Rect(-40, -40, W + 80, H + 80);
            dc.DrawRectangle(_ground, null, area);
            foreach (var s in Specks) dc.DrawRectangle(_speck[s.level], null, new Rect(s.x * W, s.y * H, s.w, s.h));
            foreach (var r in Rocks) dc.DrawEllipse(RockBrush, null, new Point(r.x * W, r.y * H), r.rx, r.ry);
            dc.DrawRectangle(_light, null, area);

            // The press: the print settles in over about half a second, shrinking slightly as it sinks.
            double p = EaseOut((t - 0.5) / 0.45);
            if (p > 0)
            {
                dc.PushTransform(new TranslateTransform(cx, cy));
                dc.PushTransform(new RotateTransform(-0.3 * 180 / Math.PI));
                double k = 1.65 - 0.6 * p;
                dc.PushTransform(new ScaleTransform(k, k));
                if (p < 1) dc.PushOpacity(p);
                dc.PushTransform(new TranslateTransform(1.4, 1.4));
                dc.DrawGeometry(_printRim, null, Print);
                dc.Pop();
                dc.DrawGeometry(_printFill, null, Print);
                dc.PushClip(Print);
                for (int y = -30; y < 30; y += 5) dc.DrawLine(TreadPen, new Point(-14, y), new Point(14, y));
                dc.Pop();
                if (p < 1) dc.Pop();
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            // Dust puffs thrown out from the print, flattened to the ground plane, rising a little as they thin.
            for (int i = 0; i < 16; i++)
            {
                double a = i * 0.39 + 0.3, age = t - 0.8 - (i % 3) * 0.05;
                if (age <= 0) continue;
                double reach = 14 + 62 * EaseOut(age / 2.3) * (0.7 + 0.3 * ((i * 7) % 5) / 4.0);
                double alpha = 0.5 * (1 - Clamp01(age / 2.7)), rad = 7 + 10 * age;
                dc.DrawEllipse(_dustLevels[(int)Math.Round(alpha * 16)], null, new Point(cx + Math.Cos(a) * reach, cy + Math.Sin(a) * reach * 0.55 - 4 * age), rad, rad);
            }

            // The lock: a ring pulses out and amber brackets close on the print.
            double q = Phase(t, 2.5, 0.7);
            if (q > 0)
            {
                double d = 64 - 30 * EaseOut(q), L = 13;
                dc.PushOpacity(EaseOut(q));
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    double bx = cx + sx * d, by = cy + sy * d;
                    dc.DrawLine(BracketPen, new Point(bx, by - sy * L), new Point(bx, by));
                    dc.DrawLine(BracketPen, new Point(bx, by), new Point(bx - sx * L, by));
                }
                dc.Pop();
                double rr = Phase(t, 2.5, 1.4);
                if (rr < 1)
                {
                    dc.PushOpacity(0.8 * (1 - rr));
                    double rad = 14 + 60 * EaseOut(rr);
                    dc.DrawEllipse(null, _ring, new Point(cx, cy), rad, rad);
                    dc.Pop();
                }
            }
            dc.Pop();   // backdrop opacity

            double pt = Phase(t, 3.0, 0.8);
            if (pt > 0)
            {
                double ease = EaseOut(pt);
                dc.PushOpacity(ease * 0.88 * fadeOut);
                dc.DrawRectangle(StripBrush, null, new Rect(-40, 8, W + 80, 56));
                dc.Pop();
                DrawSpaced(dc, "FIRST FOOTFALL", MonoFace, 17, TitleBrush, W / 2, 16, 1.0 - 0.6 * ease, ease * fadeOut);
                DrawText(dc, _subject, MonoFace, 11, SubjectBrush, W / 2, 43, Phase(t, 3.5, 0.5) * fadeOut);
            }
            DrawText(dc, "FIRST COMMANDER TO WALK THIS WORLD", MonoFace, 10, TagBrush, W / 2, H - 18, Phase(t, 3.7, 0.6) * fadeOut);
            dc.Pop();   // scale
        }
    }
}
