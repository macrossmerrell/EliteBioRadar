using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace EliteBioRadar
{
    // Procedural star art — same architecture as PlanetRenderer (baked RenderTargetBitmap
    // layers, cross-faded for motion, cached per body). First pass covers the real-fusion
    // main sequence (O/B/A/F/G/K/M, Wolf-Rayet W*) and the brown dwarfs (L/T/Y) with an
    // actual surface: seeded granulation "bubbling" texture, looping prominence/flare arcs
    // at the limb, and a soft corona. Explicitly NOT sunspots — confirmed with the user that
    // Elite doesn't normally show them. White dwarfs / neutron stars / black holes stay on
    // the old flat icon for now (no real screenshot reference yet for those); they're routed
    // around IsProceduralStarFamily below rather than guessed at.
    public static class StarRenderer
    {
        private static readonly Dictionary<string, RenderTargetBitmap> _cache =
            new Dictionary<string, RenderTargetBitmap>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() => _cache.Clear();

        // Real fusion stars only — degenerate/exotic remnants (white dwarf "D*", neutron "N",
        // black hole "H"/supermassive) have a completely different visual language (accretion
        // disk, lensing, pulsar beams, not a granulated photosphere) and no reference screenshot
        // yet, so they're left on the legacy flat icon rather than forced through this pipeline.
        public static bool IsProceduralStarFamily(string starType)
        {
            if (string.IsNullOrEmpty(starType)) return false;
            switch (starType)
            {
                case "O": case "B": case "A": case "F": case "G": case "K": case "M":
                case "W": case "WC": case "WN": case "WNC": case "WO":
                case "L": case "T": case "Y":
                    return true;
                default:
                    return false;
            }
        }

        private static double Seeded(double i)
        {
            var x = Math.Sin(i * 999.7) * 43758.5453;
            return x - Math.Floor(x);
        }

        // A filled, hand-tapered ribbon along a path instead of a constant-width Pen stroke —
        // real prominence filaments (studied against user-supplied reference photos) fade to
        // near-nothing at both feet and vary in width along their own length; a Pen's uniform
        // thickness plus round/flat end caps is exactly what read as thick, blobby-ended "golden
        // arches" tubes instead. centerFn/halfWidthFn are sampled at `steps` points; the ribbon
        // is built by offsetting each sample perpendicular to the path's own local tangent.
        private static StreamGeometry BuildTaperedRibbon(Func<double, Point> centerFn, Func<double, double> halfWidthFn, int steps)
        {
            var top = new Point[steps + 1];
            var bottom = new Point[steps + 1];
            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                var c = centerFn(t);
                var cNext = centerFn(Math.Min(1.0, t + 0.01));
                var dir = cNext - c;
                if (dir.Length < 1e-6) dir = new Vector(1, 0); else dir.Normalize();
                var normal = new Vector(-dir.Y, dir.X);
                double hw = halfWidthFn(t);
                top[k] = c + normal * hw;
                bottom[k] = c - normal * hw;
            }
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(top[0], true, true);
                for (int k = 1; k <= steps; k++) gc.LineTo(top[k], true, false);
                for (int k = steps; k >= 0; k--) gc.LineTo(bottom[k], true, false);
            }
            geo.Freeze();
            return geo;
        }

        public static (double cx, double cy, double R) GetStarGeometry(int width, int height)
        {
            double cx = width / 2.0, cy = height / 2.0;
            double R = Math.Min(width, height) * 0.30;
            return (cx, cy, R);
        }

        // A real ring (not an asteroid-belt entry — see PlanetRenderer.GetRingColor's own
        // comment on how a dim brown dwarf can genuinely carry one) needs the disc/flares drawn
        // a touch smaller (see the 0.85 shrink wherever this is checked below) so there's real
        // clearance between the visible disc and the ring — same "shrink the sphere for rings"
        // convention PlanetRenderer's own gas-giant sphereR already uses. Ring geometry itself
        // (GetStarRingLayers/GetStarThumbFrame) stays keyed to the NOMINAL (unshrunk) R from
        // GetStarGeometry — only the drawn disc/flares shrink, mirroring exactly how a gas
        // giant's own ComputeGeometry keeps one nominal R for ring math while separately
        // shrinking just the drawn sphereR.
        private static bool HasRealRing(BodyScanDetail detail) =>
            detail.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0 && !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));

        // Real blackbody-ish star color from SurfaceTemperature — continuous, not a per-class
        // palette pick, so two G stars at slightly different real temperatures render slightly
        // different shades of yellow instead of being visually identical. Anchor points follow
        // the standard O/B/A/F/G/K/M color convention (blue-white at the hot end, deep
        // red-orange at the cool end); StarType itself is only used as a fallback when
        // SurfaceTemperature wasn't parsed (0).
        // internal (not private) so SystemScanWindow can reuse the exact same real color logic
        // for its small star swatch dot, instead of a second, looser approximation drifting out
        // of sync with the actual Star tab render over time.
        internal static (Color core, Color mid, Color edge) GetStarColors(double tempK, string starType)
        {
            if (tempK <= 0)
            {
                // Fallback anchors per class (roughly the class's real mean temperature).
                tempK = starType switch
                {
                    "O" => 35000, "B" => 20000, "A" => 8500, "F" => 6750,
                    "G" => 5700, "K" => 4500, "M" => 3200,
                    "W" or "WC" or "WN" or "WNC" or "WO" => 45000,
                    "L" => 1700, "T" => 900, "Y" => 400,
                    _ => 5700,
                };
            }

            // Real bug found from a screenshot: a real M dwarf at 2,664K (StarType "M", per the
            // app's own CLASS readout) was rendering nearly as dark as a brown dwarf. The old
            // single continuous table used one shared 1000-2600K "Y/late-T" / "L/T" bracket for
            // ANY star that cold, regardless of StarType — but real M dwarfs run as cool as
            // ~2300K and are properly bright, fusing stars, while L/T/Y brown dwarfs are a
            // genuinely different sub-stellar population that happens to overlap the same
            // temperature range. Splitting the anchor table by StarType (not just
            // SurfaceTemperature) keeps a cool real M dwarf on its own bright branch instead of
            // ever blending toward the brown-dwarf colors.
            //
            // A second real screenshot (confirmed StarType "L") then showed those brown-dwarf
            // colors were wrong on their own terms too — a real L dwarf renders as a vivid,
            // saturated magenta-pink/crimson with a bright near-white hotspot, not a dim muddy
            // orange-brown. A THIRD real screenshot (confirmed StarType "T") showed the same
            // thing is true one class down — still a vivid saturated magenta/fuchsia banded
            // disk, just a touch deeper/less pink-white than L rather than the dim muddy
            // orange-brown this row used to be. Only Y is still unconfirmed pending a real
            // reference — left as the one genuinely dim/dark anchor for now, though given L and
            // T both turned out vivid it may well need the same treatment eventually.
            bool isBrownDwarf = starType is "L" or "T" or "Y";
            var stops = isBrownDwarf
                ? new (double k, Color core, Color mid, Color edge)[]
                {
                    (300,  Color.FromRgb(0x8a,0x28,0x18), Color.FromRgb(0x5c,0x14,0x10), Color.FromRgb(0x2c,0x08,0x08)), // Y
                    (1300, Color.FromRgb(0xff,0xb0,0xd0), Color.FromRgb(0xd6,0x1f,0x78), Color.FromRgb(0x6e,0x10,0x48)), // T
                    (2200, Color.FromRgb(0xff,0xd6,0xe6), Color.FromRgb(0xff,0x20,0x64), Color.FromRgb(0xa8,0x08,0x30)), // L
                }
                : new (double k, Color core, Color mid, Color edge)[]
                {
                    // Real M dwarfs run cool (down to ~2300K) but bright — same vivid tone the
                    // 3700K anchor below already uses, just extended down to a real floor
                    // instead of ever touching the brown-dwarf branch above.
                    (2300,  Color.FromRgb(0xff,0xae,0x5a), Color.FromRgb(0xff,0x8a,0x3a), Color.FromRgb(0xc8,0x5a,0x24)), // M (cool)
                    (3700,  Color.FromRgb(0xff,0xae,0x5a), Color.FromRgb(0xff,0x8a,0x3a), Color.FromRgb(0xc8,0x5a,0x24)), // M (warm)
                    (5200,  Color.FromRgb(0xff,0xd8,0x92), Color.FromRgb(0xff,0xb6,0x5c), Color.FromRgb(0xd8,0x82,0x38)), // K
                    (6000,  Color.FromRgb(0xff,0xf3,0xd6), Color.FromRgb(0xf5,0xd2,0x8a), Color.FromRgb(0xc8,0x96,0x4a)), // G
                    (7500,  Color.FromRgb(0xff,0xfb,0xf2), Color.FromRgb(0xf0,0xec,0xd8), Color.FromRgb(0xc8,0xc0,0xa0)), // F
                    (10000, Color.FromRgb(0xff,0xff,0xff), Color.FromRgb(0xe4,0xea,0xff), Color.FromRgb(0xb0,0xbe,0xe0)), // A
                    (30000, Color.FromRgb(0xe8,0xf0,0xff), Color.FromRgb(0xb8,0xd0,0xff), Color.FromRgb(0x6c,0x8c,0xd8)), // B
                    (60000, Color.FromRgb(0xd8,0xe8,0xff), Color.FromRgb(0x9c,0xc4,0xff), Color.FromRgb(0x4c,0x74,0xd0)), // O/W
                };
            // Below the table's own floor (a main-sequence star reporting an oddly low temp, or
            // vice versa) clamp to the coolest anchor instead of extrapolating past it.
            tempK = Math.Max(tempK, stops[0].k);

            Color core, mid, edge;
            (core, mid, edge) = (stops[0].core, stops[0].mid, stops[0].edge);
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (tempK <= stops[i + 1].k || i == stops.Length - 2)
                {
                    double t = Math.Clamp((tempK - stops[i].k) / (stops[i + 1].k - stops[i].k), 0, 1);
                    Color Lerp(Color a, Color b) => Color.FromRgb(
                        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
                    core = Lerp(stops[i].core, stops[i + 1].core);
                    mid = Lerp(stops[i].mid, stops[i + 1].mid);
                    edge = Lerp(stops[i].edge, stops[i + 1].edge);
                    break;
                }
            }

            return (core, mid, edge);
        }

        // Real-data-driven activity level (0=quiet, 1=violently active) — drives flare count/
        // size and granulation contrast. Grounded in actual stellar astrophysics rather than
        // invented: low-mass M/K dwarfs are the most flare-prone stars known (strong convective
        // dynamos), hot O/B/A stars flare far less often but drive a much stronger stellar
        // wind/corona instead, and Wolf-Rayet stars are violently active via mass-loss rather
        // than magnetic flares. AgeMY nudges the baseline the same direction real gyrochronology
        // does — younger stars spin faster and stay magnetically active longer before the dynamo
        // winds down with age.
        private static double GetActivity(BodyScanDetail detail)
        {
            double baseActivity = detail.StarType switch
            {
                "M" => 0.85, "K" => 0.65,
                "G" => 0.45, "F" => 0.35,
                "A" => 0.25, "B" => 0.22, "O" => 0.2,
                "W" or "WC" or "WN" or "WNC" or "WO" => 0.75,
                "L" => 0.3, "T" => 0.18, "Y" => 0.12,
                _ => 0.4,
            };
            if (detail.AgeMY > 0)
            {
                if (detail.AgeMY < 500) baseActivity += 0.15;
                else if (detail.AgeMY > 8000) baseActivity -= 0.15;
            }
            return Math.Clamp(baseActivity, 0.1, 1.0);
        }

        public static (BitmapSource baseLayer, BitmapSource surfaceA, BitmapSource surfaceB, BitmapSource topLayer) GetStarLayers(
            BodyScanDetail detail, int width = 370, int height = 420)
        {
            var baseKey = detail.BodyName + "|star|" + width + "x" + height;
            if (!_cache.TryGetValue(baseKey + "|base", out var baseLayer)) { baseLayer = RenderStarBase(detail, width, height); _cache[baseKey + "|base"] = baseLayer; }
            if (!_cache.TryGetValue(baseKey + "|surfA", out var surfaceA)) { surfaceA = RenderStarSurface(detail, width, height, 0); _cache[baseKey + "|surfA"] = surfaceA; }
            if (!_cache.TryGetValue(baseKey + "|surfB", out var surfaceB)) { surfaceB = RenderStarSurface(detail, width, height, Math.PI); _cache[baseKey + "|surfB"] = surfaceB; }
            if (!_cache.TryGetValue(baseKey + "|top", out var topLayer)) { topLayer = RenderStarTop(detail, width, height); _cache[baseKey + "|top"] = topLayer; }
            return (baseLayer, surfaceA, surfaceB, topLayer);
        }

        // Real rings for a dim star (see PlanetRenderer.GetRingColor's own comment — a brown
        // dwarf, e.g. a Y-dwarf, CAN carry genuine Saturn-style rings, unlike an ordinary star
        // whose Rings entry is always an asteroid belt). Same back-pass/front-sliver technique a
        // gas giant's own rings use (PlanetRenderer.ComputeRingBands/DrawRingBandTextured,
        // reused directly rather than duplicated), composited as two EXTRA static layers around
        // the existing granulation/flare stack — MainWindow adds the back layer behind imgBase
        // and the front layer on top of everything else, rather than this touching the
        // granulation pipeline's own sphere geometry at all. That keeps the (far more common)
        // ringless case completely unaffected and avoids re-tuning flare/granulation math that
        // has nothing to do with rings. The front sliver clips to a circle a hair inside the
        // sphere's own real feathered radius (0.95R vs the disc's 1.05R clip) so it reads as
        // passing in front of the star without a hard edge poking past the soft limb.
        public static (BitmapSource back, BitmapSource front) GetStarRingLayers(BodyScanDetail detail, int width = 370, int height = 420)
        {
            var baseKey = detail.BodyName + "|starring|" + width + "x" + height;
            if (_cache.TryGetValue(baseKey + "|ringBack", out var cachedBack) && _cache.TryGetValue(baseKey + "|ringFront", out var cachedFront))
                return (cachedBack, cachedFront);

            var (cx, cy, R) = GetStarGeometry(width, height);
            // Ring geometry itself stays keyed to the nominal R (matches GetStarThumbFrame and
            // the gas-giant convention) — only the front sliver's own clip circle needs to know
            // how far the ACTUAL drawn disc reaches. RenderStarSurface shrinks its own R by the
            // same 0.85 when a real ring is present.
            double discR = HasRealRing(detail) ? R * 0.85 : R;
            // Matches the back layer's own backing-disc radius (RenderStarSurface's real clipR)
            // exactly — real report + screenshot: the earlier, deliberately conservative 0.7x
            // clip left a visible gap between where the front sliver stopped and where the ring
            // becomes visible again beside the star, so the front band read as a short isolated
            // stripe plunging into the disc instead of smoothly meeting the ring's own outer,
            // already-visible portion. Safe to extend all the way to the real edge now that the
            // back layer's own opaque backing disc (not the star's soft feathered edge) is what
            // actually guarantees full occlusion underneath — this sliver is the topmost layer
            // either way, so there's no bleed risk from widening it.
            double sliverClipR = discR * 1.05;
            // Reverted the real-diameter experiment (ComputeRingBandsRealScale) for this body —
            // real report + data check: "Scheau Phoe HW-T d4-13 1"'s own B ring reaches 12.37x
            // its own Radius (Inner 90.3M/Outer 620.4M vs Radius 50.1M), while this canvas only
            // has room for about 1.8x before the safety cap — true-to-scale rendering collapsed
            // BOTH of this star's real rings down to the same clamped pixel radius, near-zero
            // width, effectively invisible. ComputeRingBands' own "stretch whatever the real
            // ratio range is to fill a fixed window" is what actually produces a legible result
            // for a body whose real ring is this disproportionate to itself.
            double minPx = R * 1.4, maxPx = Math.Min(width, height) * 0.46;
            var rings = PlanetRenderer.ComputeRingBands(detail, minPx, maxPx);
            double ringSeed = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;
            const double TILT = PlanetRenderer.RingTilt, SQUASH = PlanetRenderer.RingSquash;

            var backVisual = new DrawingVisual();
            using (var dc = backVisual.RenderOpen())
            {
                for (int i = 0; i < rings.Count; i++)
                    PlanetRenderer.DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);

                // Real report + screenshot: the ring was still visibly showing through near the
                // star's edge, on both the near AND far side — the disc's own soft/feathered
                // edge (by design, for the star's fuzzy-limb look) is only PARTIALLY opaque
                // through its whole fade band, so wherever the ring's squashed geometry crossed
                // that band, it showed through blended with the disc instead of being cleanly
                // hidden. A gas giant's own hard-edged disc never has this problem — it's either
                // 100% opaque or 100% not, nothing in between. Rather than chase the exact
                // crossing angle, an opaque backing disc (same colors, hard edge at the disc's
                // own real clip radius — see RenderStarSurface's identical clipR) is baked into
                // this SAME back-pass image, painted over the ring after it: everything under
                // the disc's footprint is now guaranteed fully hidden regardless of where the
                // ring's ellipse happens to cross the feather band, and the real feathered star
                // (drawn on top of this whole layer in MainWindow) still supplies the actual
                // soft-edge look — this backing is never visible on its own, only as an opaque
                // stand-in wherever the real star's own edge is too translucent to fully occlude
                // by itself. The separate front-sliver layer below is unaffected — it's composited
                // on top of everything, including this.
                double backingR = discR * 1.05;
                var (bCore, bMid, bEdge) = GetStarColors(detail.SurfaceTemperature > 0 ? detail.SurfaceTemperature : 5700, detail.StarType);
                dc.PushClip(new EllipseGeometry(new Point(cx, cy), backingR, backingR));
                var backingBrush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.5, 0.5),
                    RadiusX = 0.75, RadiusY = 0.75,
                };
                backingBrush.GradientStops.Add(new GradientStop(bCore, 0.0));
                backingBrush.GradientStops.Add(new GradientStop(bMid, 0.55));
                backingBrush.GradientStops.Add(new GradientStop(bEdge, 1.0));
                dc.DrawRectangle(backingBrush, null, new Rect(cx - backingR, cy - backingR, backingR * 2, backingR * 2));
                dc.Pop();
            }
            var back = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            back.Render(backVisual);
            back.Freeze();

            var frontVisual = new DrawingVisual();
            using (var dc = frontVisual.RenderOpen())
            {
                if (rings.Count > 0)
                {
                    var discClip = new EllipseGeometry(new Point(cx, cy), sliverClipR, sliverClipR);
                    var halfPlane = new RectangleGeometry(new Rect(cx - R * 4, cy, R * 8, R * 4));
                    halfPlane.Transform = new RotateTransform(TILT * 180 / Math.PI, cx, cy);
                    var sliverClip = new CombinedGeometry(GeometryCombineMode.Intersect, discClip, halfPlane);
                    dc.PushClip(sliverClip);
                    for (int i = 0; i < rings.Count; i++)
                        PlanetRenderer.DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);
                    dc.Pop();
                }
            }
            var front = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            front.Render(frontVisual);
            front.Freeze();

            _cache[baseKey + "|ringBack"] = back;
            _cache[baseKey + "|ringFront"] = front;
            return (back, front);
        }

        // Compact static render for a small card thumbnail (System Scan's attached-star card,
        // BuildAttachedStarCard) — sphere + rings baked into one bitmap, no granulation/flare
        // animation. Same back-pass/sphere/front-sliver layering as GetStarRingLayers and the
        // same gas-giant-style "shrink the sphere a touch when rings are present" convention
        // (ComputeGeometry) so there's real clearance between the sphere and the ring's inner
        // edge, matching how a ringed gas giant's own thumbnail looks.
        public static BitmapSource GetStarThumbFrame(BodyScanDetail detail, int width, int height)
        {
            var key = detail.BodyName + "|starthumb|" + width + "x" + height;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var (cx, cy, R) = GetStarGeometry(width, height);
            var (core, mid, edge) = GetStarColors(detail.SurfaceTemperature > 0 ? detail.SurfaceTemperature : 5700, detail.StarType);
            // Reverted the real-diameter experiment (ComputeRingBandsRealScale) — see
            // GetStarRingLayers' own comment for why (a real ring can be over 12x the star's own
            // radius, which collapses to invisible once clamped to this canvas's safety cap).
            double minPx = R * 1.4, maxPx = Math.Min(width, height) * 0.46;
            var rings = PlanetRenderer.ComputeRingBands(detail, minPx, maxPx);
            double sphereR = rings.Count > 0 ? R * 0.85 : R;
            double ringSeed = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;
            const double TILT = PlanetRenderer.RingTilt, SQUASH = PlanetRenderer.RingSquash;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (int i = 0; i < rings.Count; i++)
                    PlanetRenderer.DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);

                dc.PushClip(new EllipseGeometry(new Point(cx, cy), sphereR, sphereR));
                var sphereBrush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.5, 0.5),
                    RadiusX = 0.75, RadiusY = 0.75,
                };
                sphereBrush.GradientStops.Add(new GradientStop(core, 0.0));
                sphereBrush.GradientStops.Add(new GradientStop(mid, 0.55));
                sphereBrush.GradientStops.Add(new GradientStop(edge, 1.0));
                dc.DrawRectangle(sphereBrush, null, new Rect(cx - sphereR, cy - sphereR, sphereR * 2, sphereR * 2));
                dc.Pop();

                if (rings.Count > 0)
                {
                    var discClip = new EllipseGeometry(new Point(cx, cy), sphereR, sphereR);
                    var halfPlane = new RectangleGeometry(new Rect(cx - R * 4, cy, R * 8, R * 4));
                    halfPlane.Transform = new RotateTransform(TILT * 180 / Math.PI, cx, cy);
                    var sliverClip = new CombinedGeometry(GeometryCombineMode.Intersect, discClip, halfPlane);
                    dc.PushClip(sliverClip);
                    for (int i = 0; i < rings.Count; i++)
                        PlanetRenderer.DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);
                    dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            _cache[key] = rtb;
            return rtb;
        }

        // Used to carry its own static corona (a second, unchanging soft-glow hump sitting
        // behind the dynamic one added in RenderStarSurface). Two overlapping glows — one
        // static, one breathing — summed their brightness where their falloffs crossed, and
        // that crossing read as the hard "bright ring" a real screenshot called out, not as one
        // smooth haze. Removed rather than tuned — the single animated glow in RenderStarSurface
        // is now the only halo, so there's nothing left for a second glow to clash with. Kept
        // (blank) purely so GetStarLayers' 4-image composite doesn't need restructuring.
        private static RenderTargetBitmap RenderStarBase(BodyScanDetail detail, int width, int height)
        {
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(new DrawingVisual());
            bmp.Freeze();
            return bmp;
        }

        // The ONLY cross-faded layer: granulation "bubbling" (many small seeded mottled
        // patches — the boiling-photosphere look, explicitly not discrete sunspots) plus the
        // looping prominence/flare arcs at the limb. phase shifts both the granulation seed
        // and which flares are "up" between the two frames, same cross-fade technique as the
        // gas giant cloud layer, so the surface reads as constantly churning/flickering rather
        // than a single static image.
        private static RenderTargetBitmap RenderStarSurface(BodyScanDetail detail, int width, int height, double phase)
        {
            var (cx, cy, R) = GetStarGeometry(width, height);
            if (HasRealRing(detail)) R *= 0.85;
            double seedBase = BodySeed(detail.BodyName);
            var (core, mid, edge) = GetStarColors(detail.SurfaceTemperature, detail.StarType);
            double activity = GetActivity(detail);

            // Disc pass (sphere gradient + granulation) — kept perfectly round; the earlier
            // attempt at an irregular polygon edge (little bumps merged into the limb) just
            // read as a lumpy potato rather than "fuzzy". A real star's limb isn't jagged, it's
            // SOFT — the actual fix is a feathered alpha falloff (below), not a distorted
            // outline. Rendered as its own bitmap, separate from the flares, since the flares
            // must NOT be clipped/feathered the same way (they're supposed to extend crisply
            // past the limb, not fade out at it).
            var discVisual = new DrawingVisual();
            using (var dc = discVisual.RenderOpen())
            {
                double clipR = R * 1.05;
                dc.PushClip(new EllipseGeometry(new Point(cx, cy), clipR, clipR));

                // The REAL cause of the flat top/bottom/left/right edges: this clip circle's
                // radius (1.05R) is bigger than a 2R×2R fill *square*'s reach along its own
                // cardinal axes (only R), even though the square's diagonal reaches out to
                // R√2. That left an unpainted transparent sliver between R and 1.05R exactly at
                // the 12/3/6/9-o'clock points — the fill stopped short of the circular clip
                // there while the diagonals, where the square reaches further than the clip,
                // filled and got rounded off normally. Widening the fill rect so its own
                // half-extent (1.2R) exceeds the clip radius on every axis, cardinal included,
                // removes that gap — the clip alone now determines the shape everywhere.
                double fillHalf = R * 1.2;
                // MappingMode explicitly Absolute here (and on every other radial brush below
                // meant to look perfectly round) — the default RelativeToBoundingBox maps its
                // 0-1 Center/Radius independently against the fill geometry's own width and
                // height, and this canvas isn't square (370x420), so it's worth keeping this
                // explicit even though the real bug turned out to be the fill/clip mismatch
                // above, not this mapping mode.
                var sphereOrigin = new Point(cx + R * (0.38 * 2 - 1), cy + R * (0.35 * 2 - 1));
                var sphere = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    GradientOrigin = sphereOrigin, Center = sphereOrigin, RadiusX = R * 1.7, RadiusY = R * 1.7,
                    // mid pushed from 0.55 to 0.7 — real screenshot feedback was that this
                    // whole render came out darker/muddier than the actual in-game star, which
                    // reads as vividly bright across almost the whole visible disc, not shaded
                    // like a strongly-lit 3D sphere. Holding the brighter core/mid blend across
                    // more of the gradient's own range keeps more of the visible disc close to
                    // that brightness instead of sliding toward the darker edge tone so early.
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(core, 0.0),
                        new GradientStop(mid, 0.7),
                        new GradientStop(edge, 1.0),
                    },
                };
                dc.DrawRectangle(sphere, null, new Rect(cx - fillHalf, cy - fillHalf, fillHalf * 2, fillHalf * 2));

                // Limb darkening was tried three times (a separate hard-clipped layer, then
                // baked into this same pass and softened twice more) and every version still
                // read as a visible dark ring/crescent partway in from the edge — real feedback
                // was blunt: stars don't visibly dim like that. Dropped entirely rather than
                // tuned a fourth time; the sphere gradient's own edge tone plus the feathered
                // limb already carry the disc's shading.

                // Granulation — small soft mottled patches, brighter and darker than the base,
                // scaled up in count/contrast by real activity. Deliberately irregular blob
                // shapes (not a grid) and no two the same size, per the "bubbling", not
                // "sunspots" brief — sunspots would be a few large, sharply-bounded dark ovals;
                // this is many small, soft, roughly-equal bright/dark patches.
                int granuleCount = (int)(26 + activity * 34);
                for (int i = 0; i < granuleCount; i++)
                {
                    double s = seedBase + i * 13.7 + phase * 37.1;
                    double ang = Seeded(s) * Math.PI * 2;
                    double dist = Math.Sqrt(Seeded(s + 1)) * 0.92;
                    double gx = cx + Math.Cos(ang) * dist * R;
                    double gy = cy + Math.Sin(ang) * dist * R;
                    double gr = R * (0.045 + Seeded(s + 2) * 0.08);
                    bool bright = Seeded(s + 3) > 0.45;
                    Color c = bright ? core : edge;
                    byte alpha = (byte)(40 + Seeded(s + 4) * 55);
                    var dab = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0),
                            new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
                        },
                    };
                    dc.DrawEllipse(dab, null, new Point(gx, gy), gr, gr * 0.85);
                }
                dc.Pop();
            }
            var discBmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            discBmp.Render(discVisual);
            discBmp.Freeze();
            var discFeathered = FeatherDiscEdge(discBmp, width, height, cx, cy, R);

            // Animated soft glow — the first attempt at making the edge "move" scattered a ring
            // of separate haze puffs unevenly around the limb, which broke the star's own round
            // silhouette (it read as a lumpy blob, not a circle with a soft edge). This is a
            // single concentric radial gradient instead — perfectly circular at every angle,
            // same as the corona in RenderStarBase — whose radius and strength shift a little
            // between the two cross-fade frames (via `phase`), so it breathes in and out the
            // same way the gas giant cloud layer drifts, without ever distorting the disc shape.
            var glowVisual = new DrawingVisual();
            using (var dc = glowVisual.RenderOpen())
            {
                // Widened from the first pass (1.3, was paired with the now-removed separate
                // corona layer) — with that second glow gone, this alone has to cover the same
                // overall halo size the two together used to, or the star reads as having
                // shrunk a glow ring rather than having one ring removed.
                double wobble = Math.Sin(phase + seedBase * 0.017);
                double glowR = R * (1.65 + wobble * 0.15);
                byte glowAlpha = (byte)Math.Clamp(65 + wobble * 30 + activity * 20, 25, 140);
                // Stops pulled inward (was 0.42/0.62, i.e. the glow didn't reach peak brightness
                // until ~1.0R) — that left a real brightness DIP between where the disc's own
                // feathered edge (FeatherDiscEdge's mask starts fading around 0.76R) had already
                // faded out and where this glow had ramped up, which is exactly what read as a
                // dark ring: not a shading bug on the disc, a gap between two falloffs that
                // didn't overlap. Peaking around 0.7R now means the glow is already near-bright
                // right where the disc itself is fading, so there's no gap for a dark band to
                // sit in.
                var glow = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = glowR, RadiusY = glowR,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, mid.R, mid.G, mid.B), 0.2),
                        new GradientStop(Color.FromArgb(glowAlpha, mid.R, mid.G, mid.B), 0.42),
                        new GradientStop(Color.FromArgb(0, mid.R, mid.G, mid.B), 1.0),
                    },
                };
                dc.DrawEllipse(glow, null, new Point(cx, cy), glowR, glowR);
            }
            var glowBmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            glowBmp.Render(glowVisual);
            glowBmp.Freeze();
            var glowBlurred = ToBlurred(glowBmp, width, height, R * 0.03);

            // Flares used to be baked into this same 2-frame cross-fade — but with only two
            // possible states (phase 0 / phase π), any flare slot that happened to roll "lit" in
            // BOTH frames just sat there looking frozen, while only the slots that differed
            // between the two frames ever visibly flickered. Real feedback: "you can watch three
            // of them disappear and come back, the others never change." Flares are now their
            // own independent layers (GetFlareFrame/GetFlareTiming below), each with its own
            // async opacity animation in MainWindow — not baked in here at all.
            var composite = new DrawingVisual();
            using (var dc = composite.RenderOpen())
            {
                // Glow drawn BEHIND the disc — it's meant to extend past the limb as ambient
                // haze, not tint the disc's own face. discFeathered's own semi-transparent rim
                // blends into it naturally at the boundary.
                dc.DrawImage(glowBlurred, new Rect(0, 0, width, height));
                dc.DrawImage(discFeathered, new Rect(0, 0, width, height));
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(composite);
            bmp.Freeze();
            return bmp;
        }

        // How many flare/prominence slots this star gets — a pure function of its real
        // activity, so MainWindow knows how many independent flare layers to create.
        public static int GetFlareCount(BodyScanDetail detail)
        {
            double activity = GetActivity(detail);
            return 2 + (int)Math.Round(activity * 6);
        }

        // Per-flare fade timing, seeded per body+index — stable for a given star, but
        // different flare-to-flare and star-to-star, so the cluster fades in and out
        // asynchronously instead of all together. periodSeconds is the ONE-WAY fade duration
        // — real feedback said the first slowdown (3.5-6.5s) was still "very fast", so this is
        // roughly triple that (8-14s one-way, a 16-28s full in/out cycle). beginOffsetSeconds
        // staggers each flare's start point within its own cycle so they don't all begin in
        // sync even though several may share a similar period.
        public static (double periodSeconds, double beginOffsetSeconds) GetFlareTiming(BodyScanDetail detail, int index)
        {
            double seedBase = BodySeed(detail.BodyName);
            double s = seedBase + index * 41.7 + 12000;
            double period = 8.0 + Seeded(s) * 6.0;
            double offset = Seeded(s + 1) * period * 2;
            return (period, offset);
        }

        public static BitmapSource GetFlareFrame(BodyScanDetail detail, int index, int width = 370, int height = 420)
        {
            var key = detail.BodyName + "|flare|" + index + "|" + width + "x" + height;
            if (!_cache.TryGetValue(key, out var bmp)) { bmp = RenderFlareFrame(detail, width, height, index); _cache[key] = bmp; }
            return bmp;
        }

        // One flare's own cluster of tapered filament ribbons (see BuildTaperedRibbon), baked
        // to its own transparent layer so MainWindow can fade it in/out independently of every
        // other flare and of the disc/granulation cross-fade. Unlike the old baked-in version,
        // there's no "is this slot lit" coin flip here — visibility is entirely the opacity
        // animation's job now, so every index always renders its full cluster.
        private static RenderTargetBitmap RenderFlareFrame(BodyScanDetail detail, int width, int height, int index)
        {
            var (cx, cy, R) = GetStarGeometry(width, height);
            if (HasRealRing(detail)) R *= 0.85;
            var (core, mid, _) = GetStarColors(detail.SurfaceTemperature, detail.StarType);
            double activity = GetActivity(detail);
            double seedBase = BodySeed(detail.BodyName);
            int flareCount = GetFlareCount(detail);
            // Same sector-wheel spread as before — each flare still gets its own slice of the
            // circumference so a whole cluster doesn't cluster into one quadrant by chance.
            double wheelStart = Seeded(seedBase + 8000) * Math.PI * 2;
            double sectorSize = Math.PI * 2 / flareCount;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double s = seedBase + index * 29.3 + 900;
                bool isCme = index == 0 && Seeded(s + 5) < 0.4 + activity * 0.3;
                double baseAng = wheelStart + index * sectorSize + (Seeded(s + 1) - 0.5) * sectorSize * 0.7;
                double span = (0.25 + Seeded(s + 2) * 0.35) * (isCme ? 1.6 : 1.0);
                double loopHeight = R * (0.18 + Seeded(s + 3) * 0.28) * (isCme ? 1.8 : 1.0);

                var flareColor = isCme ? core : mid;
                Color Lerp(Color a, Color b, double t) => Color.FromRgb(
                    (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

                // 2-3 filaments normally, up to 4-5 for a CME — a real ejection is a whole
                // bundle of strands, a routine prominence a couple.
                int strandCount = (isCme ? 3 : 1) + (int)(1 + Seeded(s + 6) * 2);
                for (int strand = 0; strand < strandCount; strand++)
                {
                    // Each strand gets its own small offsets from the flare's shared
                    // baseAng/span/loopHeight — same general loop, but no two filaments in the
                    // bundle follow the identical path, angle, or height.
                    double ss = s + strand * 17.3 + 300;
                    double strandAng = baseAng + (Seeded(ss) - 0.5) * span * 0.5;
                    double strandSpan = span * (0.6 + Seeded(ss + 1) * 0.5);
                    double strandHeight = loopHeight * (0.6 + Seeded(ss + 2) * 0.6);
                    double wobbleFreq = 5 + Seeded(ss + 3) * 5;
                    double wobblePhase = Seeded(ss + 4) * Math.PI * 2;

                    Point Center(double t)
                    {
                        double ang = strandAng + (t - 0.5) * strandSpan;
                        double lift = Math.Sin(t * Math.PI) * strandHeight;
                        double rr = R * 1.0 + lift;
                        // Small organic wobble along the strand's own length (zero at both feet,
                        // same as the width taper) — a real filament is never a mathematically
                        // clean arc.
                        rr += Math.Sin(t * wobbleFreq + wobblePhase) * R * 0.018 * Math.Sin(t * Math.PI);
                        return new Point(cx + Math.Cos(ang) * rr, cy + Math.Sin(ang) * rr);
                    }

                    // Tapers to ~0 at both feet; the peak is shifted off-center per strand (real
                    // filaments aren't fattest exactly at their apex) with a little ripple along
                    // its length instead of one smooth hump.
                    double maxHalf = R * (isCme ? 0.026 : 0.013) * (0.7 + Seeded(ss + 5) * 0.6);
                    double peak = 0.3 + Seeded(ss + 6) * 0.4;
                    double HalfWidth(double t)
                    {
                        double taper = t < peak ? t / peak : (1 - t) / (1 - peak);
                        taper = Math.Pow(Math.Clamp(taper, 0, 1), 0.6);
                        double ripple = 0.75 + 0.25 * Math.Sin(t * 9 + ss);
                        return maxHalf * taper * ripple;
                    }

                    // A wider, fainter glow drawn from the SAME tapered center/width functions
                    // (just scaled up), sitting underneath the crisp filament — real depth
                    // instead of one flat-toned ribbon, and since it's still a smooth tapered
                    // shape rather than a stroked line, it stays soft at the blur pass below
                    // without bringing back the constant-width "tube" look.
                    var haloRibbon = BuildTaperedRibbon(Center, t => HalfWidth(t) * 2.6, 26);
                    var haloColor = Lerp(flareColor, Colors.White, 0.2);
                    byte haloAlpha = (byte)((isCme ? 60 : 40) * (0.75 + Seeded(ss + 8) * 0.35));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(haloAlpha, haloColor.R, haloColor.G, haloColor.B)), null, haloRibbon);

                    var ribbon = BuildTaperedRibbon(Center, HalfWidth, 26);
                    // First strand in the bundle is the brightest "core" filament; the rest are
                    // paler/cooler wisps wrapped around it, like the real reference photos show
                    // a bright thread inside a hazier bundle.
                    bool isCore = strand == 0;
                    var strandColor = isCore ? flareColor : Lerp(flareColor, Colors.White, 0.3);
                    byte alpha = (byte)((isCore ? (isCme ? 215 : 175) : (isCme ? 150 : 115)) * (0.75 + Seeded(ss + 7) * 0.35));
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, strandColor.R, strandColor.G, strandColor.B));
                    dc.DrawGeometry(brush, null, ribbon);
                }
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            // Real Gaussian blur (same technique as the planet renderer's fissure/crack-web fix)
            // — softens the filament ribbons so they read as glowing plasma, not flat shapes.
            return ToBlurred(bmp, width, height, R * 0.012);
        }

        // Feathers the disc bitmap's own edge into transparency via a radial OpacityMask (wide
        // opaque center, gradual fade starting well inside R and finishing past it) followed by
        // a soft blur — the mask alone still has an analytic-looking gradient boundary; the
        // blur on top of it is what actually reads as a hazy, "fuzzy" limb rather than a sharp
        // disc with a gradient ring drawn on it.
        private static RenderTargetBitmap FeatherDiscEdge(BitmapSource sharp, int width, int height, double cx, double cy, double R)
        {
            var mask = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy),
                RadiusX = R * 1.15, RadiusY = R * 1.15,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.66),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
                },
            };
            mask.Freeze();
            var img = new Image { Source = sharp, Width = width, Height = height, OpacityMask = mask };
            img.Measure(new Size(width, height));
            img.Arrange(new Rect(0, 0, width, height));
            var masked = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            masked.Render(img);
            masked.Freeze();
            return ToBlurred(masked, width, height, R * 0.05);
        }

        // Used to be a separate static limb-darkening layer, clipped to a hard-edged circle and
        // drawn on top of everything else. That's exactly what produced the "cookie" look a
        // real screenshot called out — a perfectly crisp dark ring sitting over the disc no
        // matter how fuzzy the limb underneath it got, since this layer's own clip never
        // passed through the feather/blur the disc did. Limb darkening now bakes directly into
        // the disc pass in RenderStarSurface instead, so it shares that same feathering. This
        // layer is kept only so GetStarLayers/the 4-image composite in MainWindow don't need
        // restructuring — it now contributes nothing.
        private static RenderTargetBitmap RenderStarTop(BodyScanDetail detail, int width, int height)
        {
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(new DrawingVisual());
            bmp.Freeze();
            return bmp;
        }

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

        private static double BodySeed(string bodyName)
        {
            unchecked
            {
                double h = 0;
                foreach (char c in bodyName ?? "") h = h * 31 + c;
                return Math.Abs(h % 10007) + 1;
            }
        }
    }
}
