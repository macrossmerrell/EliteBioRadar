using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace EliteBioRadar
{
    // Procedural asteroid-field scene for the Planet tab's Belt Cluster view (and the System Scan
    // thumbnails). The belt is a lane of lumpy, cauliflower-textured rocks (real in-cockpit
    // references: pale grey-white icy rocks on a dark navy sky, warm tan-brown pitted rocks on a
    // dark violet sky, thousands of fine pebbles in a dense river) with a soft depth of field:
    // far rocks are small, dim and blurred, mid rocks are crisp, a few near ones are bigger.
    //
    // The Planet tab version is ANIMATED: every rock is a pre-baked sprite that drifts along the
    // lane at its own depth-dependent speed (parallax), wrapping around off-screen, while it
    // slowly tumbles a few degrees back and forth (a full spin would rotate the baked lighting
    // with it, so the tumble is kept small). The fine dust is a seamless tile that scrolls.
    // Everything is plain WPF transform animation - no per-frame code.
    //
    // Colours follow the belt's real ring class (see EliteWatcherService.GetBeltRingClass).
    public static class AsteroidFieldRenderer
    {
        private static readonly Dictionary<string, BitmapSource> _frameCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BitmapSource[]> _spriteCache =
            new Dictionary<string, BitmapSource[]>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() { _frameCache.Clear(); _spriteCache.Clear(); }

        private static double Seeded(double i)
        {
            var x = Math.Sin(i * 999.7) * 43758.5453;
            return x - Math.Floor(x);
        }

        // FNV-1a in a uint: every character of a long belt name ("... A Belt Cluster 1" vs "... 2")
        // affects the seed (the old double-accumulating hash silently dropped the last characters).
        private static double BodySeed(string bodyName)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in bodyName ?? "")
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (hash % 100000) + 1;
            }
        }

        private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private static Color Scale(Color c, double f) => Color.FromRgb(
            (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

        private sealed class Pal
        {
            public string Key = "";
            public Color Light, Mid, Dark, Haze, BgIn, BgOut;
            public Color? DustLight, DustMid;      // fine pebbles when they should differ from the rock palette (backlit dark rocks)
            public double FarOpacity = 0.80, HazeAlpha = 120;
            public double Glint, Crater;
            public int Dust;
            public bool Ice;
        }

        private static Pal GetPal(string? rawRingClass)
        {
            Color c(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
            switch (rawRingClass)
            {
                case "eRingClass_Icy":      // pale grey-white rocks, blue-grey shadows, navy sky
                    return new Pal { Key = "Icy", Ice = true, Light = c(238, 240, 243), Mid = c(172, 182, 196), Dark = c(66, 76, 96),
                        Haze = c(74, 92, 128), BgIn = c(30, 36, 50), BgOut = c(9, 11, 18), Glint = 0.0, Crater = 0.0, Dust = 280 };
                case "eRingClass_Rocky":    // warm tan-brown, deeply pitted, violet-black sky
                    return new Pal { Key = "Rocky", Light = c(224, 178, 134), Mid = c(158, 112, 86), Dark = c(48, 34, 32),
                        Haze = c(66, 46, 72), BgIn = c(26, 21, 36), BgOut = c(8, 7, 16), Glint = 0.10, Crater = 0.85, Dust = 820 };
                case "eRingClass_MetalRich":
                    return new Pal { Key = "MetalRich", Light = c(104, 88, 76), Mid = c(48, 41, 37), Dark = c(14, 12, 12),
                        Haze = c(176, 118, 68), BgIn = c(132, 88, 54), BgOut = c(38, 25, 18), Glint = 0.10, Crater = 0.70, Dust = 700,
                        DustLight = c(204, 158, 110), DustMid = c(152, 108, 72), FarOpacity = 0.42, HazeAlpha = 190 };
                case "eRingClass_Metalic":
                    return new Pal { Key = "Metalic", Light = c(216, 222, 230), Mid = c(128, 136, 148), Dark = c(34, 38, 46),
                        Haze = c(60, 72, 94), BgIn = c(25, 29, 37), BgOut = c(8, 10, 14), Glint = 0.35, Crater = 0.50, Dust = 420 };
                default:
                    return new Pal { Key = "Unknown", Light = c(212, 206, 196), Mid = c(142, 136, 126), Dark = c(44, 40, 36),
                        Haze = c(72, 72, 78), BgIn = c(25, 27, 31), BgOut = c(8, 9, 11), Glint = 0.05, Crater = 0.40, Dust = 400 };
            }
        }

        // ---------------------------------------------------------------- sprites

        // One lumpy rock: a cluster of overlapping soft-lit lobes (shadow-side lobes first, so the
        // lit ones overlap them and the seams read as creases), then terminator shading, speckle,
        // pits (rocky/metal) or cracks (ice) and the occasional bright ore glint.
        private static BitmapSource BakeSprite(Pal pal, double seed, int size, double blur, Vector toLight)
        {
            double c = size / 2.0, R = size * 0.38;
            // One smooth, lumpy silhouette: a rounded polygon whose radius varies by a few low-frequency
            // waves (so it is never a circle) and is stretched along a random axis.
            double layoutRot = Seeded(seed + 2) * Math.PI * 2;
            double elong = 1.0 + Seeded(seed + 3) * 0.35;
            double p1 = Seeded(seed + 4) * 6.28, p2 = Seeded(seed + 5) * 6.28, p3 = Seeded(seed + 6) * 6.28;
            double a1 = 0.10 + Seeded(seed + 7) * 0.10, a2 = 0.06 + Seeded(seed + 8) * 0.09, a3 = 0.03 + Seeded(seed + 9) * 0.06;
            const int nv = 20;
            var pts = new Point[nv];
            for (int i = 0; i < nv; i++)
            {
                double th = i * Math.PI * 2 / nv;
                double rr = R * (0.88 + a1 * Math.Sin(2 * th + p1) + a2 * Math.Sin(3 * th + p2) + a3 * Math.Sin(5 * th + p3)
                                 + (Seeded(seed + 20 + i * 1.7) - 0.5) * 0.07);
                double lx = Math.Cos(th) * rr * elong, ly = Math.Sin(th) * rr / elong;
                pts[i] = new Point(c + lx * Math.Cos(layoutRot) - ly * Math.Sin(layoutRot),
                                   c + lx * Math.Sin(layoutRot) + ly * Math.Cos(layoutRot));
            }
            Point Mid(Point a, Point b) => new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var union = new StreamGeometry();
            using (var gc = union.Open())
            {
                gc.BeginFigure(Mid(pts[nv - 1], pts[0]), true, true);
                for (int i = 0; i < nv; i++)
                    gc.QuadraticBezierTo(pts[i], Mid(pts[i], pts[(i + 1) % nv]), true, false);
            }
            union.Freeze();

            var vis = new DrawingVisual();
            using (var dc = vis.RenderOpen())
            {
                // Body: one volume lit from the sun side (matte - no hot highlight).
                var body = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5 + toLight.X * 0.22, 0.5 + toLight.Y * 0.22),
                    RadiusX = 0.58, RadiusY = 0.58,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Lerp(pal.Mid, pal.Light, 0.80), 0.0),
                        new GradientStop(pal.Mid, 0.50),
                        new GradientStop(Lerp(pal.Mid, pal.Dark, 0.55), 1.0),
                    },
                };
                dc.DrawGeometry(body, null, union);

                dc.PushClip(union);

                // Cauliflower bumps: many small soft lobes, each lit on its sun side and shadowed opposite.
                int bumps = 16 + (int)(Seeded(seed + 60) * 12);
                for (int i = 0; i < bumps; i++)
                {
                    double s = seed + 300 + i * 3.3;
                    double ang = Seeded(s) * Math.PI * 2, d = Math.Sqrt(Seeded(s + 1)) * R * 1.05;
                    var bp = new Point(c + Math.Cos(ang) * d, c + Math.Sin(ang) * d);
                    double br = R * (0.12 + Seeded(s + 2) * 0.17);
                    var bb = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.5),
                        GradientOrigin = new Point(0.5 + toLight.X * 0.30, 0.5 + toLight.Y * 0.30),
                        RadiusX = 0.55, RadiusY = 0.55,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(90, pal.Light.R, pal.Light.G, pal.Light.B), 0.0),
                            new GradientStop(Color.FromArgb(28, pal.Mid.R, pal.Mid.G, pal.Mid.B), 0.6),
                            new GradientStop(Color.FromArgb(105, pal.Dark.R, pal.Dark.G, pal.Dark.B), 1.0),
                        },
                    };
                    dc.DrawEllipse(bb, null, bp, br, br);
                }

                // Terminator: the side facing away from the sun falls into shadow.
                var shade = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5 + toLight.X * 0.5, 0.5 + toLight.Y * 0.5),
                    EndPoint = new Point(0.5 - toLight.X * 0.5, 0.5 - toLight.Y * 0.5),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.22),
                        new GradientStop(Color.FromArgb(165, pal.Dark.R, pal.Dark.G, pal.Dark.B), 1.0),
                    },
                };
                dc.DrawRectangle(shade, null, new Rect(0, 0, size, size));

                // Fine speckle (grain / small bumps).
                double dotScale = size / 96.0;
                for (int i = 0; i < 90; i++)
                {
                    double s = seed + 200 + i * 2.3;
                    double ang = Seeded(s) * Math.PI * 2, d = Math.Sqrt(Seeded(s + 1)) * R * 1.15;
                    var p = new Point(c + Math.Cos(ang) * d, c + Math.Sin(ang) * d);
                    bool light = Seeded(s + 2) > 0.55;
                    var col = light ? pal.Light : pal.Dark;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(40 + Seeded(s + 3) * 70), col.R, col.G, col.B)), null,
                        p, (0.4 + Seeded(s + 4) * 1.0) * dotScale, (0.4 + Seeded(s + 4) * 1.0) * dotScale);
                }

                // Pits on rocky / metal-rich rocks: shadow on the sun-facing wall, lighter floor.
                if (size >= 90 && Seeded(seed + 80) < pal.Crater)
                {
                    int pits = 2 + (int)(Seeded(seed + 81) * 3);
                    for (int i = 0; i < pits; i++)
                    {
                        double ang = Seeded(seed + 82 + i * 7.3) * Math.PI * 2, d = Seeded(seed + 83 + i * 7.3) * R * 0.65;
                        var p = new Point(c + Math.Cos(ang) * d, c + Math.Sin(ang) * d);
                        double pr = R * (0.10 + Seeded(seed + 84 + i * 7.3) * 0.14);
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(125, pal.Dark.R, pal.Dark.G, pal.Dark.B)), null, p, pr, pr * 0.9);
                        var floor = new Point(p.X - toLight.X * pr * 0.42, p.Y - toLight.Y * pr * 0.42);
                        var fb = new RadialGradientBrush
                        {
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(200, pal.Mid.R, pal.Mid.G, pal.Mid.B), 0.0),
                                new GradientStop(Color.FromArgb(0, pal.Mid.R, pal.Mid.G, pal.Mid.B), 1.0),
                            },
                        };
                        dc.DrawEllipse(fb, null, floor, pr * 0.85, pr * 0.78);
                    }
                }
                else if (pal.Ice && size >= 90)
                {
                    // Thin dark fracture veins.
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, pal.Dark.R, pal.Dark.G, pal.Dark.B)), Math.Max(0.6, size / 140.0));
                    for (int k = 0; k < 2; k++)
                    {
                        double ang = Seeded(seed + 90 + k * 5.1) * Math.PI * 2;
                        var cur = new Point(c + Math.Cos(ang) * R * 0.2, c + Math.Sin(ang) * R * 0.2);
                        var geo = new StreamGeometry();
                        using (var gc = geo.Open())
                        {
                            gc.BeginFigure(cur, false, false);
                            for (int st = 0; st < 4; st++)
                            {
                                ang += (Seeded(seed + 91 + k * 5.1 + st * 2.2) - 0.5) * 1.2;
                                double len = R * (0.18 + Seeded(seed + 92 + k * 5.1 + st * 2.2) * 0.14);
                                cur = new Point(cur.X + Math.Cos(ang) * len, cur.Y + Math.Sin(ang) * len);
                                gc.LineTo(cur, true, false);
                            }
                        }
                        dc.DrawGeometry(null, pen, geo);
                    }
                }

                // Ore glint on the sun side.
                if (pal.Glint > 0 && Seeded(seed + 95) < pal.Glint * 2.2)
                {
                    var gp = new Point(c + toLight.X * R * 0.35 + (Seeded(seed + 96) - 0.5) * R * 0.5,
                                       c + toLight.Y * R * 0.35 + (Seeded(seed + 97) - 0.5) * R * 0.5);
                    double gr = Math.Max(1.0, R * 0.11);
                    var gb = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(235, 255, 248, 224), 0),
                            new GradientStop(Color.FromArgb(0, 255, 248, 224), 1),
                        },
                    };
                    dc.DrawEllipse(gb, null, gp, gr, gr);
                }
                dc.Pop();
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(vis);
            bmp.Freeze();
            return blur > 0 ? ToBlurred(bmp, size, size, blur) : bmp;
        }

        // layer 0 far (small, dim, soft), 1 mid (crisp), 2 near (bigger).
        private static BitmapSource[] GetSprites(Pal pal, double seedBase, int layer, Vector toLight)
        {
            string key = pal.Key + "|" + seedBase + "|" + layer;
            if (_spriteCache.TryGetValue(key, out var set)) return set;
            int count = layer == 0 ? 6 : layer == 1 ? 8 : 5;
            int size = layer == 0 ? 48 : layer == 1 ? 96 : 128;
            double blur = layer == 0 ? 4.0 : layer == 1 ? 1.0 : 0.0;
            set = new BitmapSource[count];
            for (int i = 0; i < count; i++)
                set[i] = BakeSprite(pal, seedBase + layer * 1000 + i * 71.3, size, blur, toLight);
            _spriteCache[key] = set;
            return set;
        }

        // ---------------------------------------------------------------- scene model
        //
        // The whole field flows along one direction (angle A, random per body): rocks enter at one
        // side of the view and leave at another. Everything is laid out in the lane's own frame:
        // u runs along the flow, v across it (perpendicular), both measured from the view centre.

        private sealed class Rock
        {
            public BitmapSource Sprite = null!;
            public double D, V, Speed, Phase, Rot0, RotAmp, RotPeriod, RotPhase, Opacity;
        }

        private sealed class Scene
        {
            public int W, H;
            public Pal Pal = null!;
            public BitmapSource Background = null!, Dust = null!;
            public List<Rock> Rocks = new List<Rock>();
            public double Angle, Diag, DustSpeed;      // flow direction (radians), view diagonal, dust scroll px/s
            public Vector Dir => new Vector(Math.Cos(Angle), Math.Sin(Angle));
            public Vector Perp => new Vector(-Math.Sin(Angle), Math.Cos(Angle));
        }

        private static Scene BuildScene(string bodyName, string? rawRingClass, int w, int h)
        {
            double seedBase = BodySeed(bodyName);
            var pal = GetPal(rawRingClass);
            double k = w / 370.0;                               // sizes/speeds are designed for the 370px panel
            double lightAngle = 0.6 + Seeded(seedBase + 6500) * 0.6;
            var toLight = new Vector(-Math.Cos(lightAngle), -Math.Sin(lightAngle));

            double diag = Math.Sqrt((double)w * w + (double)h * h);
            var sc = new Scene { W = w, H = h, Pal = pal, Diag = diag, DustSpeed = 7 * k };
            sc.Angle = Seeded(seedBase + 7001) * Math.PI * 2;                     // any side to any side
            double laneOffset = (Seeded(seedBase + 7000) - 0.5) * 0.24 * Math.Min(w, h);
            double thickness = (0.30 + Seeded(seedBase + 7002) * 0.16) * Math.Min(w, h) * 1.1;
            double cx = w / 2.0, cy = h / 2.0;

            double Lane(double s, int i) =>
                (Seeded(s + i * 1.7) + Seeded(s + i * 2.9 + 40)) / 2 - 0.5;

            // ----- background: sky, stars, haze along the lane -----
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
            {
                var sky = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.45), Center = new Point(0.5, 0.45), RadiusX = 0.95, RadiusY = 0.8,
                    GradientStops = new GradientStopCollection { new GradientStop(pal.BgIn, 0.0), new GradientStop(pal.BgOut, 1.0) },
                };
                dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
                var haze = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0.0), EndPoint = new Point(0.5, 1.0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, pal.Haze.R, pal.Haze.G, pal.Haze.B), 0.0),
                        new GradientStop(Color.FromArgb((byte)pal.HazeAlpha, pal.Haze.R, pal.Haze.G, pal.Haze.B), 0.5),
                        new GradientStop(Color.FromArgb(0, pal.Haze.R, pal.Haze.G, pal.Haze.B), 1.0),
                    },
                };
                // Haze band in the lane frame: rotated to the flow direction, shifted by the lane offset.
                dc.PushTransform(new RotateTransform(sc.Angle * 180 / Math.PI, cx, cy));
                dc.PushTransform(new TranslateTransform(0, laneOffset));
                dc.DrawRectangle(haze, null, new Rect(cx - diag, cy - Math.Min(w, h) * 0.34, diag * 2, Math.Min(w, h) * 0.68));
                dc.Pop(); dc.Pop();
                for (int i = 0; i < 46; i++)
                {
                    double s = seedBase + i * 3.7 + 5000;
                    double sr = (0.35 + Seeded(s + 2) * 0.85) * Math.Max(k, 0.6);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(80 + Seeded(s + 3) * 130), 255, 255, 255)), null,
                        new Point(Seeded(s) * w, Seeded(s + 1) * h), sr, sr);
                }
            }
            var bgBmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bgBmp.Render(bg);
            bgBmp.Freeze();
            sc.Background = bgBmp;

            // ----- fine dust: a square tile in the lane frame, seamless along u (specks near an edge are also drawn one tile over) -----
            int tile = (int)Math.Ceiling(diag);
            var dust = new DrawingVisual();
            using (var dc = dust.RenderOpen())
            {
                int dustN = (int)(pal.Dust * Math.Max(0.35, k * k) * (diag * diag) / ((double)w * h) * 0.55);
                for (int i = 0; i < dustN; i++)
                {
                    double s = seedBase + i * 7.13 + 8000;
                    double u = Seeded(s) * tile;
                    double v = tile / 2.0 + laneOffset + Lane(s, 3) * thickness * 1.5;
                    double dr = (0.35 + Seeded(s + 2) * 0.85) * Math.Max(k, 0.6);
                    byte da = (byte)(55 + Seeded(s + 3) * 110);
                    var col = Seeded(s + 4) > 0.5 ? (pal.DustLight ?? pal.Light) : (pal.DustMid ?? pal.Mid);
                    var br = new SolidColorBrush(Color.FromArgb(da, col.R, col.G, col.B));
                    dc.DrawEllipse(br, null, new Point(u, v), dr, dr);
                    if (u < dr * 2) dc.DrawEllipse(br, null, new Point(u + tile, v), dr, dr);
                    if (u > tile - dr * 2) dc.DrawEllipse(br, null, new Point(u - tile, v), dr, dr);
                }
            }
            var dustBmp = new RenderTargetBitmap(tile, tile, 96, 96, PixelFormats.Pbgra32);
            dustBmp.Render(dust);
            dustBmp.Freeze();
            sc.Dust = dustBmp;

            // ----- rocks -----
            var layers = new[]
            {
                (layer: 0, count: 90, dMin: 10.0, dMax: 22.0, vMin: 4.0,  vMax: 7.0,  op: pal.FarOpacity),
                (layer: 1, count: 44, dMin: 22.0, dMax: 48.0, vMin: 8.0,  vMax: 13.0, op: 1.0),
                (layer: 2, count: 7,  dMin: 54.0, dMax: 76.0, vMin: 16.0, vMax: 22.0, op: 1.0),
            };
            foreach (var L in layers)
            {
                var sprites = GetSprites(pal, seedBase, L.layer, toLight);
                int cnt = Math.Max(4, (int)(L.count * Math.Max(0.4, k)));
                for (int i = 0; i < cnt; i++)
                {
                    double s = seedBase + L.layer * 3000 + i * 13.7;
                    double d = (L.dMin + Seeded(s + 2) * (L.dMax - L.dMin)) * k;
                    // Most rocks follow the lane; ~22% stray further out (the scattered stragglers).
                    double v = Seeded(s + 5) < 0.22
                        ? (Seeded(s + 1) - 0.5) * Math.Min(w, h) * 1.1
                        : laneOffset + Lane(s, 1) * thickness * (L.layer == 0 ? 1.5 : 1.15);
                    sc.Rocks.Add(new Rock
                    {
                        Sprite = sprites[(int)(Seeded(s + 6) * sprites.Length) % sprites.Length],
                        D = d, V = v,
                        Speed = (L.vMin + Seeded(s + 7) * (L.vMax - L.vMin)) * k,
                        Phase = Seeded(s),
                        Rot0 = Seeded(s + 8) * 360, RotAmp = 4 + Seeded(s + 9) * 9,
                        RotPeriod = 18 + Seeded(s + 10) * 30, RotPhase = Seeded(s + 11),
                        Opacity = L.op * (0.88 + Seeded(s + 12) * 0.12),
                    });
                }
            }
            return sc;
        }

        // Top-left of a rock's sprite when it is at flow position u (measured from the view centre).
        private static Point RockTopLeft(Scene sc, Rock r, double u)
        {
            var p = new Point(sc.W / 2.0, sc.H / 2.0) + sc.Dir * u + sc.Perp * r.V;
            return new Point(p.X - r.D / 2, p.Y - r.D / 2);
        }

        private static double TravelHalf(Scene sc, Rock r) => (sc.Diag + r.D) / 2;

        // ---------------------------------------------------------------- static frame (thumbnails)

        public static BitmapSource GetAsteroidFieldFrame(string bodyName, string? rawRingClass, int width = 370, int height = 420)
        {
            var key = bodyName + "|belt5|" + (rawRingClass ?? "?") + "|" + width + "x" + height;
            if (!_frameCache.TryGetValue(key, out var bmp)) { bmp = RenderStatic(bodyName, rawRingClass, width, height); _frameCache[key] = bmp; }
            return bmp;
        }

        private static BitmapSource RenderStatic(string bodyName, string? rawRingClass, int w, int h)
        {
            var sc = BuildScene(bodyName, rawRingClass, w, h);
            var vis = new DrawingVisual();
            using (var dc = vis.RenderOpen())
            {
                dc.DrawImage(sc.Background, new Rect(0, 0, w, h));
                double cx = w / 2.0, cy = h / 2.0;
                dc.PushTransform(new RotateTransform(sc.Angle * 180 / Math.PI, cx, cy));
                dc.DrawImage(sc.Dust, new Rect(cx - sc.Dust.PixelWidth / 2.0, cy - sc.Dust.PixelHeight / 2.0, sc.Dust.PixelWidth, sc.Dust.PixelHeight));
                dc.Pop();
                foreach (var r in sc.Rocks)
                {
                    double half = TravelHalf(sc, r);
                    var tl = RockTopLeft(sc, r, -half + r.Phase * 2 * half);
                    dc.PushOpacity(r.Opacity);
                    dc.PushTransform(new RotateTransform(r.Rot0, tl.X + r.D / 2, tl.Y + r.D / 2));
                    dc.DrawImage(r.Sprite, new Rect(tl.X, tl.Y, r.D, r.D));
                    dc.Pop(); dc.Pop();
                }
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(vis);
            bmp.Freeze();
            return bmp;
        }

        // ---------------------------------------------------------------- animated scene (Planet tab)

        private static void Throttle(Timeline t) => Timeline.SetDesiredFrameRate(t, 30);

        public static FrameworkElement CreateAnimatedField(string bodyName, string? rawRingClass, int w = 370, int h = 420)
        {
            var sc = BuildScene(bodyName, rawRingClass, w, h);
            var root = new Canvas { Width = w, Height = h, ClipToBounds = true, IsHitTestVisible = false };
            root.Children.Add(new Image { Source = sc.Background, Width = w, Height = h });
            double cx = w / 2.0, cy = h / 2.0;

            // Dust: three tiles in a row along the flow, rotated to the flow direction, scrolled by exactly one tile and repeated.
            double tile = sc.Dust.PixelWidth;
            var dustHost = new Canvas { Width = tile * 3, Height = tile };
            for (int i = 0; i < 3; i++)
            {
                var di = new Image { Source = sc.Dust, Width = tile, Height = tile };
                Canvas.SetLeft(di, i * tile);
                dustHost.Children.Add(di);
            }
            Canvas.SetLeft(dustHost, cx - tile * 1.5);
            Canvas.SetTop(dustHost, cy - tile / 2);
            var dustT = new TranslateTransform();
            dustHost.RenderTransform = new TransformGroup
            {
                Children = { dustT, new RotateTransform(sc.Angle * 180 / Math.PI, tile * 1.5, tile / 2) },
            };
            var dustAnim = new DoubleAnimation(-tile, 0, TimeSpan.FromSeconds(tile / sc.DustSpeed)) { RepeatBehavior = RepeatBehavior.Forever };
            Throttle(dustAnim);
            dustT.BeginAnimation(TranslateTransform.XProperty, dustAnim);
            root.Children.Add(dustHost);

            foreach (var r in sc.Rocks)
            {
                var img = new Image { Source = r.Sprite, Width = r.D, Height = r.D, Opacity = r.Opacity };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var rot = new RotateTransform(r.Rot0, r.D / 2, r.D / 2);
                var tr = new TranslateTransform();
                img.RenderTransform = new TransformGroup { Children = { rot, tr } };

                double half = TravelHalf(sc, r), T = 2 * half / r.Speed;
                var a = RockTopLeft(sc, r, -half);
                var b = RockTopLeft(sc, r, half);
                var begin = TimeSpan.FromSeconds(-r.Phase * T);
                var ax = new DoubleAnimation(a.X, b.X, TimeSpan.FromSeconds(T)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                var ay = new DoubleAnimation(a.Y, b.Y, TimeSpan.FromSeconds(T)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                var ar = new DoubleAnimation(r.Rot0 - r.RotAmp, r.Rot0 + r.RotAmp, TimeSpan.FromSeconds(r.RotPeriod))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromSeconds(-r.RotPhase * r.RotPeriod * 2),
                };
                Throttle(ax); Throttle(ay); Throttle(ar);
                tr.BeginAnimation(TranslateTransform.XProperty, ax);
                tr.BeginAnimation(TranslateTransform.YProperty, ay);
                rot.BeginAnimation(RotateTransform.AngleProperty, ar);
                root.Children.Add(img);
            }

            // Soft fade toward the edges so rocks drift in and out of the dark rather than popping at a hard rectangle.
            root.OpacityMask = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.62,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.70),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
                },
            };
            return root;
        }

        // Real Gaussian blur, baked into the sprite (no live effects) - the depth-of-field softness.
        private static RenderTargetBitmap ToBlurred(BitmapSource sharp, int width, int height, double radius)
        {
            var img = new Image { Source = sharp, Width = width, Height = height, Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } };
            img.Measure(new Size(width, height));
            img.Arrange(new Rect(0, 0, width, height));
            var blurred = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            blurred.Render(img);
            blurred.Freeze();
            return blurred;
        }
    }
}
