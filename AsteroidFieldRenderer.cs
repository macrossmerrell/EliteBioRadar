using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace EliteBioRadar
{
    // Procedural asteroid-field backdrop for the Planet tab's Belt Cluster view — replaces the
    // old flat "belt_N.png" icon (one of five fixed pieces of art, unrelated to the real body)
    // with a scene built from real in-cockpit reference screenshots and whatever real ring-class
    // data the parent ring's own Scan event carries (see EliteWatcherService.GetBeltRingClass —
    // a belt cluster's OWN Scan event has almost no data at all; the real composition lives on
    // the ring it belongs to).
    //
    // Second pass, after direct feedback against a real reference photo: real asteroids are
    // rounded/lumpy (cauliflower-like clusters of soft bumps), NOT faceted low-poly gems — the
    // first pass's flat-shaded triangle facets were exactly backwards. Also real belts show a
    // dense LANE/band structure (a river of rock and fine dust trailing off into sparse
    // scattered chunks above/below), not a uniform scatter, and real depth-of-field: distant
    // chunks read soft/hazy, only the close ones are crisp.
    public static class AsteroidFieldRenderer
    {
        private static readonly Dictionary<string, RenderTargetBitmap> _cache =
            new Dictionary<string, RenderTargetBitmap>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() => _cache.Clear();

        private static double Seeded(double i)
        {
            var x = Math.Sin(i * 999.7) * 43758.5453;
            return x - Math.Floor(x);
        }

        // Real bug found from direct feedback ("all asteroid belts show the same image"): the
        // old version accumulated the hash in a `double` (`h = h*31 + c`). For a body name this
        // long (a real belt cluster name like "...A Belt Cluster 1" runs 30+ characters), 31^n
        // blows past a double's ~15-17 significant digits of exact-integer precision well
        // before the string ends — the LAST few characters (exactly where "Cluster 1" vs
        // "Cluster 2" differ) were being silently swallowed by floating-point rounding, so
        // every cluster belonging to the same ring hashed to the same seed. FNV-1a accumulated
        // in a `uint` (proper mod-2^32 wraparound, no precision loss regardless of string
        // length) fixes it — and has a real avalanche property, so even a one-character
        // difference anywhere in the name scrambles the whole hash.
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

        // rockLight/rockMid/rockDark: the chunk's own lit-face/mid-tone/shadow-face palette.
        // hazeColor: the soft colored dust glow behind everything. glintChance: how often a
        // chunk gets a small bright ore-glint fleck (rocky/metallic fields show these; the icy
        // reference didn't). craterChance: rocky/metal-rich fields show visible impact craters
        // on the bigger chunks — the real ice reference instead shows thin fracture veins.
        private static (Color rockLight, Color rockMid, Color rockDark, Color haze, double glintChance, double craterChance) GetPalette(string? rawRingClass)
        {
            var mapped = rawRingClass switch
            {
                "eRingClass_Icy" => "Icy",
                "eRingClass_MetalRich" => "MetalRich",
                "eRingClass_Metalic" => "Metalic",
                "eRingClass_Rocky" => "Rocky",
                _ => "Unknown",
            };
            return mapped switch
            {
                // Haze brightened — confirmed this IS already tied to ring class (the rocky
                // haze you liked came from this same table), but the old blue-grey read closer
                // to "dark navy" than the pale, almost-white icy mist the reference photos show.
                "Icy" => (Color.FromRgb(0xe8, 0xee, 0xf2), Color.FromRgb(0xa8, 0xba, 0xc8), Color.FromRgb(0x3e, 0x4c, 0x5c),
                          Color.FromRgb(0xc4, 0xd6, 0xe6), 0.0, 0.0),
                "Rocky" => (Color.FromRgb(0xd8, 0xc4, 0x9c), Color.FromRgb(0xa8, 0x8e, 0x66), Color.FromRgb(0x3a, 0x2e, 0x1e),
                            Color.FromRgb(0x8a, 0x5a, 0x2e), 0.12, 0.7),
                "MetalRich" => (Color.FromRgb(0xc8, 0xb4, 0x9c), Color.FromRgb(0x8a, 0x72, 0x5a), Color.FromRgb(0x2e, 0x24, 0x1a),
                                 Color.FromRgb(0x6a, 0x46, 0x2a), 0.22, 0.75),
                "Metalic" => (Color.FromRgb(0xd0, 0xd4, 0xda), Color.FromRgb(0x94, 0x9a, 0xa2), Color.FromRgb(0x2c, 0x2e, 0x32),
                              Color.FromRgb(0x50, 0x58, 0x66), 0.3, 0.6),
                _ => (Color.FromRgb(0xc8, 0xc2, 0xb6), Color.FromRgb(0x96, 0x90, 0x84), Color.FromRgb(0x36, 0x32, 0x2c),
                      Color.FromRgb(0x50, 0x50, 0x50), 0.05, 0.4),
            };
        }

        public static BitmapSource GetAsteroidFieldFrame(string bodyName, string? rawRingClass, int width = 370, int height = 420)
        {
            var key = bodyName + "|belt2|" + (rawRingClass ?? "?") + "|" + width + "x" + height;
            if (!_cache.TryGetValue(key, out var bmp)) { bmp = RenderField(bodyName, rawRingClass, width, height); _cache[key] = bmp; }
            return bmp;
        }

        private static RenderTargetBitmap RenderField(string bodyName, string? rawRingClass, int width, int height)
        {
            double seedBase = BodySeed(bodyName);
            var (rockLight, rockMid, rockDark, haze, glintChance, craterChance) = GetPalette(rawRingClass);
            var lightAngle = 0.6 + Seeded(seedBase + 6500) * 0.6;
            var lightDir = new Vector(Math.Cos(lightAngle), Math.Sin(lightAngle));

            // The real "lane" structure — every real belt photo shows a dense band of rock and
            // fine dust with sparse stragglers above/below it, not a uniform scatter. Band
            // center/tilt/thickness are all seeded per body so different belts read as genuinely
            // different structures, not just reshuffled dots in the same generic cloud.
            double bandCenter = 0.42 + Seeded(seedBase + 7000) * 0.16;
            double bandTilt = -0.22 + Seeded(seedBase + 7001) * 0.44;
            double bandThickness = 0.16 + Seeded(seedBase + 7002) * 0.1;

            double BandY(double xFrac, double spread)
            {
                double centerAtX = bandCenter + (xFrac - 0.5) * bandTilt;
                // Two averaged uniforms approximate a soft (Gaussian-ish) falloff away from the
                // band center instead of a hard-edged stripe.
                double n = (Seeded(seedBase + xFrac * 977 + spread * 311) + Seeded(seedBase + xFrac * 613 + spread * 197 + 40)) / 2 - 0.5;
                return Math.Clamp(centerAtX + n * bandThickness, 0.02, 0.98);
            }

            // Background (space + stars + haze) — static, sharp, shared by every layer.
            var bgVisual = new DrawingVisual();
            using (var dc = bgVisual.RenderOpen())
            {
                var spaceBrush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.42), Center = new Point(0.5, 0.42), RadiusX = 0.9, RadiusY = 0.75,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0x14, 0x16, 0x1a), 0.0),
                        new GradientStop(Color.FromRgb(0x06, 0x07, 0x09), 1.0),
                    },
                };
                dc.DrawRectangle(spaceBrush, null, new Rect(0, 0, width, height));
                for (int i = 0; i < 40; i++)
                {
                    double s = seedBase + i * 3.7 + 5000;
                    double sx = Seeded(s) * width, sy = Seeded(s + 1) * height;
                    double sr = 0.4 + Seeded(s + 2) * 0.9;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(90 + Seeded(s + 3) * 120), 255, 255, 255)), null, new Point(sx, sy), sr, sr);
                }
                // Haze concentrated along the same lane the rocks follow, not a separate
                // independent diagonal band.
                var hazeBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, bandCenter - bandTilt * 0.5 - 0.22), EndPoint = new Point(1, bandCenter + bandTilt * 0.5 + 0.22),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, haze.R, haze.G, haze.B), 0.0),
                        new GradientStop(Color.FromArgb(130, haze.R, haze.G, haze.B), 0.42),
                        new GradientStop(Color.FromArgb(90, haze.R, haze.G, haze.B), 0.58),
                        new GradientStop(Color.FromArgb(0, haze.R, haze.G, haze.B), 1.0),
                    },
                };
                dc.DrawRectangle(hazeBrush, null, new Rect(0, 0, width, height));

                // Dense fine dust trailing along the lane — hundreds of tiny specks, the sandy
                // "river" texture real belt photos show threading between the bigger chunks.
                for (int i = 0; i < 420; i++)
                {
                    double s = seedBase + i * 7.13 + 8000;
                    double xFrac = Seeded(s);
                    double yFrac = BandY(xFrac, 0.6);
                    double dr = 0.4 + Seeded(s + 2) * 0.9;
                    byte da = (byte)(60 + Seeded(s + 3) * 110);
                    var c = Seeded(s + 4) > 0.5 ? rockLight : rockMid;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(da, c.R, c.G, c.B)), null, new Point(xFrac * width, yFrac * height), dr, dr);
                }
            }
            var bgBmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bgBmp.Render(bgVisual);
            bgBmp.Freeze();

            // Two depth layers now — the old third "front" layer (big, crisp, edge-crowding
            // chunks meant to read as passing close by camera) is gone entirely per direct
            // feedback: keep everything at middle-to-far distance, nothing looming up close.
            // Mid's own max size nudged up a little (was 0.062, now 0.075) so the field still
            // has some visual weight/variety without that removed layer.
            var back = BakeLayer(seedBase, 1000, width, height, count: 34, minR: 0.014, maxR: 0.03, alpha: 165,
                rockLight, rockMid, rockDark, lightDir, glintChance, craterChance, BandY, spreadTag: 1.1, blurRadius: 3.2);
            var mid = BakeLayer(seedBase, 2000, width, height, count: 18, minR: 0.032, maxR: 0.075, alpha: 220,
                rockLight, rockMid, rockDark, lightDir, glintChance, craterChance, BandY, spreadTag: 2.1, blurRadius: 1.1);

            var composite = new DrawingVisual();
            using (var dc = composite.RenderOpen())
            {
                dc.DrawImage(bgBmp, new Rect(0, 0, width, height));
                dc.DrawImage(back, new Rect(0, 0, width, height));
                dc.DrawImage(mid, new Rect(0, 0, width, height));
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(composite);
            bmp.Freeze();
            return bmp;
        }

        // edgeBias pulls the front layer's chunks toward the frame's edges/corners (real
        // reference: the biggest, closest rocks crowd the top/bottom/side edges of the canopy
        // view rather than sitting in the open middle).
        private static RenderTargetBitmap BakeLayer(double seedBase, double layerSeed, int width, int height,
            int count, double minR, double maxR, byte alpha,
            Color rockLight, Color rockMid, Color rockDark, Vector lightDir, double glintChance, double craterChance,
            Func<double, double, double> bandY, double spreadTag, double blurRadius, bool edgeBias = false)
        {
            double diag = Math.Sqrt(width * width + height * height);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (int i = 0; i < count; i++)
                {
                    double s = seedBase + layerSeed + i * 13.7;
                    double cxFrac = Seeded(s);
                    // Most chunks follow the lane; a minority (~15%) stray further from it for
                    // organic variety, same as scattered outliers in the real photo. edgeBias
                    // layer chunks skip the lane entirely — they're meant to loom at the frame
                    // edges regardless of where the lane itself falls.
                    double cyFrac;
                    if (edgeBias)
                        cyFrac = Seeded(s + 1) < 0.5 ? Seeded(s + 1) * 0.32 : 1 - Seeded(s + 1) * 0.32;
                    else if (Seeded(s + 5) < 0.15)
                        cyFrac = Seeded(s + 1);
                    else
                        cyFrac = bandY(cxFrac, spreadTag + i * 0.01);
                    double cx = cxFrac * width, cy = cyFrac * height;
                    double r = (minR + Seeded(s + 2) * (maxR - minR)) * diag;

                    DrawSoftChunk(dc, cx, cy, r, s, lightDir, alpha, rockLight, rockMid, rockDark, craterChance);

                    if (glintChance > 0 && Seeded(s + 9) < glintChance)
                    {
                        double ga = Seeded(s + 10) * Math.PI * 2, gd = Seeded(s + 11) * r * 0.5;
                        var gp = new Point(cx + Math.Cos(ga) * gd, cy + Math.Sin(ga) * gd);
                        double gr = Math.Max(0.8, r * 0.12);
                        var glint = new RadialGradientBrush
                        {
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(230, 255, 250, 220), 0),
                                new GradientStop(Color.FromArgb(0, 255, 250, 220), 1),
                            },
                        };
                        dc.DrawEllipse(glint, null, gp, gr, gr);
                    }
                }
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return blurRadius > 0 ? ToBlurred(bmp, width, height, blurRadius) : bmp;
        }

        // Irregular, asymmetric outer silhouette — varied vertex count, per-vertex radius
        // jitter, and elongation/rotation, so no two chunks (even at the same size) share the
        // same shape. Vertices are joined with a slight quadratic curve instead of a dead-
        // straight line so the outline itself is never a hard geometric edge either.
        private static Geometry BuildIrregularSilhouette(double cx, double cy, double r, double seed)
        {
            int vertCount = 7 + (int)(Seeded(seed + 20) * 5); // 7-11
            double jitterMin = 0.55 + Seeded(seed + 21) * 0.2;
            double jitterSpread = 0.35 + Seeded(seed + 22) * 0.4;
            double elongX = 0.8 + Seeded(seed + 23) * 0.5;
            double elongY = 0.8 + Seeded(seed + 24) * 0.5;
            double rotation = Seeded(seed + 25) * Math.PI * 2;

            var pts = new Point[vertCount];
            for (int k = 0; k < vertCount; k++)
            {
                double ang = k * Math.PI * 2 / vertCount + (Seeded(seed + 30 + k * 4.1) - 0.5) * 0.3;
                double rr = r * (jitterMin + Seeded(seed + 31 + k * 3.3) * jitterSpread);
                double lx = Math.Cos(ang) * rr * elongX, ly = Math.Sin(ang) * rr * elongY;
                pts[k] = new Point(
                    cx + lx * Math.Cos(rotation) - ly * Math.Sin(rotation),
                    cy + lx * Math.Sin(rotation) + ly * Math.Cos(rotation));
            }

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(pts[0], true, true);
                for (int k = 0; k < vertCount; k++)
                {
                    var next = pts[(k + 1) % vertCount];
                    // Bow the segment's own midpoint slightly off the straight line between the
                    // two vertices — a small quadratic curve instead of a perfectly flat edge.
                    var mid = new Point((pts[k].X + next.X) / 2, (pts[k].Y + next.Y) / 2);
                    var toNext = next - pts[k];
                    var normal = new Vector(-toNext.Y, toNext.X);
                    if (normal.Length > 0.0001) normal.Normalize();
                    double bow = (Seeded(seed + 35 + k * 2.9) - 0.5) * r * 0.18;
                    var control = mid + normal * bow;
                    gc.QuadraticBezierTo(control, next, true, false);
                }
            }
            geo.Freeze();
            return geo;
        }

        // Third pass. Round soft lobes (previous version) over-corrected — real feedback was
        // "way too round, they look like bowling balls." A real asteroid IS irregular and
        // asymmetric (different chunks read as genuinely different shapes/sizes, with visible
        // facet-like tonal variation across the surface) — what was actually wrong in the
        // FIRST version wasn't the irregular silhouette, it was drawing each facet as a flat
        // hard-edged triangle with its own stroked border. This keeps the irregular jittered
        // silhouette (asymmetric, no two chunks the same shape) but paints the facet variation
        // as soft blended tonal patches within it — no stroke between them, no straight
        // boundary anywhere, each patch's color pulled from the same rock palette so it reads
        // as one rock's own surface variation, not a separate outlined piece.
        private static void DrawSoftChunk(DrawingContext dc, double cx, double cy, double r, double seed,
            Vector lightDir, byte alpha, Color rockLight, Color rockMid, Color rockDark, double craterChance)
        {
            var silhouette = BuildIrregularSilhouette(cx, cy, r, seed);
            dc.PushClip(silhouette);

            // Base directional wash — the overall lit-side/shadow-side tone before any facet
            // texture on top of it.
            var baseBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 - lightDir.X * 0.5, 0.5 - lightDir.Y * 0.5),
                EndPoint = new Point(0.5 + lightDir.X * 0.5, 0.5 + lightDir.Y * 0.5),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(alpha, rockLight.R, rockLight.G, rockLight.B), 0.0),
                    new GradientStop(Color.FromArgb(alpha, rockMid.R, rockMid.G, rockMid.B), 0.55),
                    new GradientStop(Color.FromArgb(alpha, rockDark.R, rockDark.G, rockDark.B), 1.0),
                },
            };
            dc.DrawRectangle(baseBrush, null, new Rect(cx - r * 1.3, cy - r * 1.3, r * 2.6, r * 2.6));

            // Soft "facet" patches — a handful of irregular soft-edged tonal patches, each
            // either a bit lighter or a bit darker than the base wash at that point, giving the
            // surface visible plane-to-plane variation without ever drawing a border between
            // planes. Every patch's color still comes from the same three-tone palette, so it
            // never introduces a color outside the rock's own.
            int patchCount = 4 + (int)(Seeded(seed + 40) * 4); // 4-7
            for (int i = 0; i < patchCount; i++)
            {
                double pa = Seeded(seed + 41 + i * 3.7) * Math.PI * 2;
                double pd = Seeded(seed + 42 + i * 3.7) * r * 0.7;
                double pr = r * (0.35 + Seeded(seed + 43 + i * 3.7) * 0.45);
                var pp = new Point(cx + Math.Cos(pa) * pd, cy + Math.Sin(pa) * pd);
                bool lighter = Seeded(seed + 44 + i * 3.7) > 0.5;
                var patchColor = lighter ? Lerp(rockMid, rockLight, 0.6) : Lerp(rockMid, rockDark, 0.6);
                byte patchAlpha = (byte)(50 + Seeded(seed + 45 + i * 3.7) * 60);
                var patchBrush = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(patchAlpha, patchColor.R, patchColor.G, patchColor.B), 0.0),
                        new GradientStop(Color.FromArgb(0, patchColor.R, patchColor.G, patchColor.B), 1.0),
                    },
                };
                dc.DrawEllipse(patchBrush, null, pp, pr, pr * (0.7 + Seeded(seed + 46 + i * 3.7) * 0.5));
            }
            dc.Pop();

            // A thin, low-alpha outer edge in the rock's own dark tone (never a stark black
            // outline) — just enough definition to read against the star field behind it.
            var edgePen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.4), rockDark.R, rockDark.G, rockDark.B)), Math.Max(0.6, r * 0.025));
            dc.DrawGeometry(null, edgePen, silhouette);

            // Fine surface detail — real ice asteroids show thin fracture veins, rocky/metal
            // ones show impact craters; which one (if either) depends on the real ring class.
            if (r > 12 && Seeded(seed + 80) < craterChance)
            {
                int craterCount = 1 + (int)(Seeded(seed + 81) * 2);
                for (int p = 0; p < craterCount; p++)
                {
                    double pa = Seeded(seed + 82 + p * 7.3) * Math.PI * 2;
                    double pd = Seeded(seed + 83 + p * 7.3) * r * 0.5;
                    var pp = new Point(cx + Math.Cos(pa) * pd, cy + Math.Sin(pa) * pd);
                    double pr = r * (0.08 + Seeded(seed + 84 + p * 7.3) * 0.1);
                    var pitBrush = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.35, 0.35), RadiusX = 1, RadiusY = 1,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(alpha * 0.7), rockDark.R, rockDark.G, rockDark.B), 0.0),
                            new GradientStop(Color.FromArgb(0, rockDark.R, rockDark.G, rockDark.B), 1.0),
                        },
                    };
                    dc.DrawEllipse(pitBrush, null, pp, pr, pr * 0.85);
                }
            }
            else if (r > 12 && craterChance < 0.5)
            {
                // Thin fracture veins (ice) — a couple of soft jagged cracks following the
                // surface curvature, faint enough to read as texture, not damage.
                int crackCount = 1 + (int)(Seeded(seed + 85) * 2);
                for (int cIdx = 0; cIdx < crackCount; cIdx++)
                {
                    double startAng = Seeded(seed + 86 + cIdx * 5.1) * Math.PI * 2;
                    var p0 = new Point(cx + Math.Cos(startAng) * r * 0.15, cy + Math.Sin(startAng) * r * 0.15);
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.3), rockDark.R, rockDark.G, rockDark.B)), Math.Max(0.5, r * 0.02));
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open())
                    {
                        gc.BeginFigure(p0, false, false);
                        var cur = p0;
                        double ang = startAng + (Seeded(seed + 87 + cIdx * 5.1) - 0.5) * 1.2;
                        for (int step = 0; step < 4; step++)
                        {
                            ang += (Seeded(seed + 88 + cIdx * 5.1 + step * 2.2) - 0.5) * 1.1;
                            double len = r * (0.16 + Seeded(seed + 89 + cIdx * 5.1 + step * 2.2) * 0.12);
                            cur = new Point(cur.X + Math.Cos(ang) * len, cur.Y + Math.Sin(ang) * len);
                            gc.LineTo(cur, true, false);
                        }
                    }
                    dc.DrawGeometry(null, pen, geo);
                }
            }
        }

        // Real Gaussian blur (same technique used throughout the app's other procedural
        // renderers) — the actual mechanism behind the back/mid layers' depth-of-field softness.
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
