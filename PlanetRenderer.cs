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
    // Procedural planet art — replaces the flat per-class PNG icons with a render generated
    // from each body's own real scan data, instead of one fixed image shared by every body
    // that scans the same class. Gas-giant family first (Sudarsky I-V, the "with X based
    // life" variants, Helium, Water Giant), including real ring geometry and ring-hotspot
    // glints — this is the full scene from the approved "Planet Render Concept" mockups, not
    // just the sphere.
    //
    // Every render is baked once per body into a cached RenderTargetBitmap and reused until
    // that body's detail object changes — never redrawn per frame — matching the "How this
    // maps to WPF" note carried through every one of those concepts. Two frames (slightly
    // different cloud-band phase) are cross-faded by MainWindow for the cloud-drift motion
    // the concepts had, without redrawing the actual bitmap every frame.
    public static class PlanetRenderer
    {
        private static readonly Dictionary<string, RenderTargetBitmap> _cache =
            new Dictionary<string, RenderTargetBitmap>(StringComparer.OrdinalIgnoreCase);

        public static bool IsGasGiantFamily(string? iconCode) =>
            iconCode != null && (iconCode.StartsWith("GG", StringComparison.Ordinal) || iconCode == "WTG");

        public static void ClearCache() => _cache.Clear();

        // Split into a static layer (rings, sphere base, limb darkening, ring front-sliver,
        // atmosphere glow — none of it depends on cloud phase) and a separate cloud-band-only
        // layer (the only thing that's actually supposed to move). The single-bake version
        // this replaced cross-faded the WHOLE scene via opacity — since every static element is
        // pixel-IDENTICAL between frame A and frame B, stacking two semi-transparent copies of
        // the same content doesn't reproduce the same look as one opaque layer (alpha
        // compositing math), so the rings/glow/limb all visibly "breathed" over the animation
        // cycle even though nothing about them ever changed. Only the cloud layer cross-fades
        // now; everything else renders once and sits still.
        public static (BitmapSource baseLayer, BitmapSource cloudA, BitmapSource cloudB, BitmapSource topLayer) GetGasGiantLayers(
            BodyScanDetail detail, string iconCode, int width = 620, int height = 460)
        {
            var baseKey = detail.BodyName + "|scene|" + iconCode + "|" + width + "x" + height;
            if (!_cache.TryGetValue(baseKey + "|base", out var baseLayer)) { baseLayer = RenderGasGiantBase(detail, iconCode, width, height); _cache[baseKey + "|base"] = baseLayer; }
            // Cloud phase — was a single 1.1 shift, barely moving the band sine arguments frame
            // to frame, which is why the drift read as a faint pulse instead of visible motion.
            // Pi (~3.14) puts the wobble/band sine terms at the opposite point in their own
            // cycle from frame A — the largest possible frame-to-frame difference the same
            // 2-frame cross-fade technique can produce. Revert to 1.1 (paired with the 9s
            // duration in MainWindow.xaml.cs) for the original subtle drift.
            if (!_cache.TryGetValue(baseKey + "|cloudA", out var cloudA)) { cloudA = RenderGasGiantClouds(detail, iconCode, width, height, 0); _cache[baseKey + "|cloudA"] = cloudA; }
            if (!_cache.TryGetValue(baseKey + "|cloudB", out var cloudB)) { cloudB = RenderGasGiantClouds(detail, iconCode, width, height, Math.PI); _cache[baseKey + "|cloudB"] = cloudB; }
            if (!_cache.TryGetValue(baseKey + "|top", out var topLayer)) { topLayer = RenderGasGiantTop(detail, iconCode, width, height); _cache[baseKey + "|top"] = topLayer; }
            return (baseLayer, cloudA, cloudB, topLayer);
        }

        // ---- GPU shader path (GasGiantShaderEffect). The shader draws the sphere itself, so the
        // static layers around it are just the ring back-pass and the front-sliver/atmosphere
        // layer WITHOUT the CPU limb darkening (the shader does its own). ----
        public static (BitmapSource ringBack, BitmapSource top) GetGasGiantShaderStaticLayers(
            BodyScanDetail detail, string iconCode, int width, int height)
        {
            var baseKey = detail.BodyName + "|ggshader|" + iconCode + "|" + width + "x" + height;
            if (!_cache.TryGetValue(baseKey + "|ring", out var ring)) { ring = RenderGasGiantBase(detail, iconCode, width, height, includeSphere: false); _cache[baseKey + "|ring"] = ring; }
            if (!_cache.TryGetValue(baseKey + "|top", out var top)) { top = RenderGasGiantTop(detail, iconCode, width, height, includeLimb: false); _cache[baseKey + "|top"] = top; }
            return (ring, top);
        }

        // Sphere center/radius in scene pixels (same geometry the CPU layers use).
        public static (double cx, double cy, double sphereR) GetGasGiantSphere(BodyScanDetail detail, int width, int height)
        {
            var (cx, cy, R, rings) = ComputeGeometry(detail, width, height);
            return (cx, cy, rings.Count > 0 ? R * 0.85 : R);
        }

        // string.GetHashCode is randomized per process in .NET, which would give a body a
        // different seed (and so a different look) every launch.
        internal static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s ?? "") h = (h ^ c) * 16777619;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // ---- GPU landable worlds (Shaders\LandableWorld.fx) ----
        public static bool IsLandableWorld(BodyScanDetail detail, string iconCode) =>
            (iconCode == "HMC" || iconCode == "ICY" || iconCode == "RBD") && detail.Landable &&
            !detail.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);

        // Calibrated against five real landable HMC screenshots (matched to their journal scans).
        // All five carry the same composition (iron ~22%, nickel ~17%, sulphur ~16%) yet look
        // completely different, so colour comes from five curated looks. Which look a body gets is a
        // best guess from temperature (the hottest is dark rust with glowing flecks, mid-heat bodies
        // are grey/beige, the cold ammonia ones are grey-green or salmon) and then a stable hash of
        // the name picks within that group - treat the temperature mapping as a hypothesis to refine
        // as more bodies come in.
        public static LandableWorldLook GetLandableWorldLook(BodyScanDetail detail, string iconCode = "HMC")
        {
            int hash = StableHash(detail.BodyName);
            Color rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
            double temp = detail.SurfaceTemperature;
            if (iconCode == "ICY") return GetIcyLandableLook(detail, hash, temp);
            if (iconCode == "RBD") return GetRockyLandableLook(detail, hash);

            // Colour does not follow temperature (13 real bodies from 450 K to 900 K ranged over every look), so it is
            // picked from the name hash across all eleven looks.
            // Bodies around the same star tend to share a family of looks (reported from play), so the star group
            // ("<system> <star letters>", the name up to the first number) picks a window of five looks and the
            // body picks within it.
            int look = (hash >> 4) % 11;   // by name alone: a star-group window did not hold up against real rocky bodies

            var l = new LandableWorldLook
            {
                Ice = rgb(206, 226, 238), Hot = rgb(255, 120, 40),
                Seed = (hash % 5000) / 100.0 + 1.0,
                Tilt = (((hash >> 3) % 100) / 100.0 - 0.5) * 0.35,
                Pitch = (((hash >> 12) % 9) - 4) * 0.08,
                Mottle = 0.85 + ((hash >> 7) % 40) / 100.0,
                AtmGlow = detail.SurfacePressure > 0 ? 0.35 : 0.0,
            };
            switch (look)
            {
                case 0: // dark rust brown, tan patches
                    l.Light = rgb(150, 110, 88); l.Dark = rgb(62, 40, 32); l.Accent = rgb(140, 64, 40); l.Bright = rgb(190, 170, 155);
                    l.Contrast = 1.0; l.RustAmt = 0.6; l.BrightAmt = 0.2; l.CraterAmt = 0.7; l.RayAmt = 0.3; break;
                case 1: // grey with white regolith patches and rust streaks
                    l.Light = rgb(146, 136, 128); l.Dark = rgb(86, 78, 74); l.Accent = rgb(118, 52, 46); l.Bright = rgb(214, 208, 202);
                    l.Contrast = 0.9; l.RustAmt = 0.9; l.BrightAmt = 0.8; l.CraterAmt = 0.6; l.RayAmt = 0.2; break;
                case 2: // lunar grey-beige
                    l.Light = rgb(152, 142, 134); l.Dark = rgb(98, 94, 86); l.Accent = rgb(120, 92, 70); l.Bright = rgb(190, 184, 176);
                    l.Contrast = 0.7; l.RustAmt = 0.1; l.BrightAmt = 0.0; l.CraterAmt = 0.9; l.RayAmt = 0.3; break;
                case 3: // dim smooth grey-green
                    l.Light = rgb(84, 90, 78); l.Dark = rgb(58, 64, 56); l.Accent = rgb(84, 80, 60); l.Bright = rgb(150, 156, 144);
                    l.Contrast = 0.35; l.RustAmt = 0.0; l.BrightAmt = 0.0; l.CraterAmt = 0.4; l.RayAmt = 0.0; break;
                case 5: // orange-tan, large smooth pale patches (real: DG-F b25-0 B 1)
                    l.Light = rgb(190, 140, 106); l.Dark = rgb(134, 98, 78); l.Accent = rgb(170, 112, 78); l.Bright = rgb(214, 170, 120);
                    l.Contrast = 0.55; l.RustAmt = 0.15; l.BrightAmt = 0.7; l.CraterAmt = 0.4; l.RayAmt = 0.0; break;
                case 6: // grey-brown with olive-grey basins and a dark-rayed crater (B 2)
                    l.Light = rgb(142, 128, 118); l.Dark = rgb(98, 96, 84); l.Accent = rgb(110, 96, 88); l.Bright = rgb(160, 140, 126);
                    l.Contrast = 0.65; l.RustAmt = 0.15; l.BrightAmt = 0.3; l.CraterAmt = 0.5; l.RayAmt = -0.6; break;
                case 7: // olive-tan with green patches, dark-rayed craters (C 3)
                    l.Light = rgb(150, 138, 112); l.Dark = rgb(104, 106, 78); l.Accent = rgb(100, 112, 70); l.Bright = rgb(108, 128, 70);
                    l.Contrast = 0.7; l.RustAmt = 0.2; l.BrightAmt = 0.55; l.CraterAmt = 0.6; l.RayAmt = -0.9; break;
                case 8: // dark brown mottled (C 4)
                    l.Light = rgb(124, 96, 80); l.Dark = rgb(66, 54, 46); l.Accent = rgb(110, 62, 48); l.Bright = rgb(150, 124, 108);
                    l.Contrast = 0.9; l.RustAmt = 0.4; l.BrightAmt = 0.25; l.CraterAmt = 0.4; l.RayAmt = 0.0; break;
                case 9: // salmon with tiny dark-green flecks and big dark-rayed craters (C 2)
                    l.Light = rgb(206, 156, 118); l.Dark = rgb(150, 118, 96); l.Accent = rgb(60, 70, 44); l.Bright = rgb(224, 184, 150);
                    l.Contrast = 0.6; l.RustAmt = 0.45; l.BrightAmt = 0.25; l.CraterAmt = 0.8; l.RayAmt = -1.0; break;
                case 10: // rust red with rayed craters (C 1)
                    l.Light = rgb(176, 88, 58); l.Dark = rgb(110, 62, 48); l.Accent = rgb(96, 64, 56); l.Bright = rgb(150, 112, 120);
                    l.Contrast = 0.8; l.RustAmt = 0.3; l.BrightAmt = 0.3; l.CraterAmt = 0.8; l.RayAmt = -0.9; break;
                default: // salmon-orange with brown mottling, rayed craters
                    l.Light = rgb(192, 148, 114); l.Dark = rgb(104, 78, 58); l.Accent = rgb(116, 86, 62); l.Bright = rgb(222, 188, 160);
                    l.Contrast = 0.75; l.RustAmt = 0.1; l.BrightAmt = 0.0; l.CraterAmt = 0.9; l.RayAmt = 0.9; break;
            }

            // Frost on the cold bodies.
            if (temp > 0 && temp < 250)
                l.IceAmt = 0.20 + 0.35 * (((hash >> 9) % 100) / 100.0);
            // Glowing flecks on hot magma-volcanic bodies.
            bool magma = !string.IsNullOrEmpty(detail.Volcanism) &&
                detail.Volcanism.Contains("magma", StringComparison.OrdinalIgnoreCase);
            if (magma && temp > 500) l.HotAmt = 0.8;
            return l;
        }

        // Landable ICY bodies, calibrated against nine real in-game screenshots (matched to journal scans).
        // All nine carry the same composition (about 69% ice; sulphur ~23%, carbon ~19%, iron ~16%) yet
        // range from near-white to a dark charcoal, so - as with the landable HMC bodies - colour comes
        // from curated looks picked by a stable name hash (only the coldest bodies, ~27 K, lean towards the
        // light grey cratered look). The coloured blotches of real icy worlds (teal, mint, tan, gold) are
        // rendered through the shader's "pale patch" layer, and icy surfaces are glossy, so every look also
        // carries a sun-glint.
        private static LandableWorldLook GetIcyLandableLook(BodyScanDetail detail, int hash, double temp)
        {
            Color rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
            var l = new LandableWorldLook
            {
                Ice = rgb(206, 226, 238), Hot = rgb(255, 120, 40),
                Seed = (hash % 5000) / 100.0 + 1.0,
                Tilt = (((hash >> 3) % 100) / 100.0 - 0.5) * 0.35,
                Pitch = (((hash >> 12) % 9) - 4) * 0.08,
                Mottle = 0.85 + ((hash >> 7) % 40) / 100.0,
                AtmGlow = detail.SurfacePressure > 0 ? 0.20 : 0.0,
                IceAmt = 0.0, HotAmt = 0.0, SpecAmt = 0.8,
                LightDir = new System.Windows.Media.Media3D.Vector3D(-0.30, 0.30, 0.90),   // real icy shots are nearly full-lit
            };

            int look = hash % 7;
            if (((hash >> 16) % 7) == 0) look = 7;   // ~1 in 7 bodies: white ice with blue crevasses (kept off the main modulus so existing bodies keep their look)
            if (temp > 0 && temp < 35 && (hash % 10) < 7) look = 2;   // the coldest lean towards light grey, cratered
            switch (look)
            {
                case 0: // white with teal-green blotches
                    l.Light = rgb(236, 238, 238); l.Dark = rgb(198, 206, 208); l.Accent = rgb(60, 72, 74); l.Bright = rgb(58, 128, 118);
                    l.Contrast = 0.45; l.RustAmt = 0.35; l.BrightAmt = 0.75; l.CraterAmt = 0.5; l.RayAmt = 0.2; break;
                case 1: // pale mint-white, soft seafoam patches
                    l.Light = rgb(222, 228, 226); l.Dark = rgb(184, 196, 194); l.Accent = rgb(120, 128, 128); l.Bright = rgb(150, 214, 200);
                    l.Contrast = 0.40; l.RustAmt = 0.20; l.BrightAmt = 0.90; l.CraterAmt = 0.4; l.RayAmt = 0.1; break;
                case 2: // light grey with rayed craters and dark brown flecks
                    l.Light = rgb(196, 196, 194); l.Dark = rgb(158, 158, 156); l.Accent = rgb(92, 70, 62); l.Bright = rgb(226, 226, 224);
                    l.Contrast = 0.50; l.RustAmt = 0.50; l.BrightAmt = 0.30; l.CraterAmt = 0.9; l.RayAmt = 0.8; break;
                case 3: // near-white with strong dark flecks
                    l.Light = rgb(240, 240, 238); l.Dark = rgb(212, 212, 210); l.Accent = rgb(70, 66, 64); l.Bright = rgb(250, 250, 250);
                    l.Contrast = 0.30; l.RustAmt = 0.70; l.BrightAmt = 0.20; l.CraterAmt = 0.6; l.RayAmt = 0.4; break;
                case 4: // blue-grey with tan patches
                    l.Light = rgb(150, 162, 176); l.Dark = rgb(100, 112, 126); l.Accent = rgb(70, 76, 86); l.Bright = rgb(232, 196, 156);
                    l.Contrast = 0.90; l.RustAmt = 0.50; l.BrightAmt = 0.70; l.CraterAmt = 0.6; l.RayAmt = 0.3; break;
                case 5: // dark charcoal with mixed-colour speckle
                    l.Light = rgb(88, 84, 80); l.Dark = rgb(50, 48, 46); l.Accent = rgb(150, 100, 100); l.Bright = rgb(140, 130, 120);
                    l.Contrast = 0.80; l.RustAmt = 0.20; l.BrightAmt = 0.30; l.CraterAmt = 0.5; l.RayAmt = 0.1; l.SpecAmt = 1.0; break;
                case 7: // white ice with long blue crevasses and small blue patches (real: DG-F b25-0 A 2 a)
                    l.Light = rgb(238, 240, 240); l.Dark = rgb(226, 229, 230); l.Accent = rgb(150, 156, 160); l.Bright = rgb(120, 170, 200); l.Ice = rgb(66, 128, 176);
                    l.Contrast = 0.12; l.RustAmt = 0.10; l.BrightAmt = 0.05; l.CraterAmt = 0.25; l.RayAmt = 0.1; l.LineAmt = 1.0; l.SpecAmt = 0.3; break;
                default: // dark rust-pink with gold flecks
                    l.Light = rgb(136, 98, 92); l.Dark = rgb(86, 64, 60); l.Accent = rgb(74, 62, 48); l.Bright = rgb(214, 176, 96);
                    l.Contrast = 0.90; l.RustAmt = 0.30; l.BrightAmt = 0.25; l.CraterAmt = 0.5; l.RayAmt = 0.1; l.SpecAmt = 0.9; break;
            }
            return l;
        }

        // Landable ROCKY bodies, calibrated against six real screenshots (ZM-D c12-0 A 5 a; RR-N d6-47 A 3 and A 4 moons). All
        // of them share one composition (rock 86-91%, iron ~19-21%, sulphur ~19%), yet they range from pale grey with a frost cap
        // to rust-orange, charcoal, salmon, dark olive-green and tan - so, as with HMC, the look comes from the star group
        // (a window of three looks) and then the body name.
        private static LandableWorldLook GetRockyLandableLook(BodyScanDetail detail, int hash)
        {
            Color rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
            var l = new LandableWorldLook
            {
                Ice = rgb(226, 232, 238), Hot = rgb(255, 120, 40),
                Seed = (hash % 5000) / 100.0 + 1.0,
                Tilt = (((hash >> 3) % 100) / 100.0 - 0.5) * 0.35,
                Pitch = (((hash >> 12) % 9) - 4) * 0.08,
                Mottle = 0.85 + ((hash >> 7) % 40) / 100.0,
                AtmGlow = 0.0, SpecAmt = 0.0,
            };
            int look = (hash >> 4) % 6;   // by name alone: a star-group window did not hold up (4E was grey beside orange 3 moons)
            switch (look)
            {
                case 0: // pale grey, pink-brown patches, frost (cold bodies)
                    l.Light = rgb(158, 154, 148); l.Dark = rgb(98, 82, 84); l.Accent = rgb(112, 84, 86); l.Bright = rgb(196, 192, 188);
                    l.Contrast = 1.0; l.RustAmt = 0.7; l.BrightAmt = 0.2; l.CraterAmt = 0.6; l.RayAmt = 0.0; l.Mottle = 0.62; break;
                case 1: // rust-orange with tan patches, dark-rayed crater
                    l.Light = rgb(178, 112, 74); l.Dark = rgb(122, 80, 56); l.Accent = rgb(150, 88, 58); l.Bright = rgb(216, 168, 122);
                    l.Contrast = 0.75; l.RustAmt = 0.4; l.BrightAmt = 0.6; l.CraterAmt = 0.7; l.RayAmt = -0.7; break;
                case 2: // dark charcoal with rust patches
                    l.Light = rgb(98, 86, 82); l.Dark = rgb(52, 44, 42); l.Accent = rgb(122, 58, 46); l.Bright = rgb(132, 120, 114);
                    l.Contrast = 0.8; l.RustAmt = 0.9; l.BrightAmt = 0.3; l.CraterAmt = 0.5; l.RayAmt = 0.0; break;
                case 3: // salmon-tan with grey-brown patches
                    l.Light = rgb(206, 152, 114); l.Dark = rgb(150, 128, 108); l.Accent = rgb(124, 102, 86); l.Bright = rgb(224, 182, 142);
                    l.Contrast = 0.6; l.RustAmt = 0.2; l.BrightAmt = 0.3; l.CraterAmt = 0.8; l.RayAmt = 0.0; break;
                case 4: // dark olive-green with red-brown and yellow-green patches
                    l.Light = rgb(82, 86, 64); l.Dark = rgb(44, 46, 36); l.Accent = rgb(98, 50, 46); l.Bright = rgb(142, 150, 72);
                    l.Contrast = 0.7; l.RustAmt = 0.6; l.BrightAmt = 0.3; l.CraterAmt = 0.5; l.RayAmt = 0.0; break;
                default: // tan-orange, crumpled
                    l.Light = rgb(202, 142, 102); l.Dark = rgb(148, 102, 78); l.Accent = rgb(160, 112, 82); l.Bright = rgb(224, 178, 134);
                    l.Contrast = 0.7; l.RustAmt = 0.3; l.BrightAmt = 0.4; l.CraterAmt = 0.9; l.RayAmt = -0.4; break;
            }
            double temp = detail.SurfaceTemperature;
            if (temp > 0 && temp < 160) l.IceAmt = 0.25 + 0.30 * (((hash >> 9) % 100) / 100.0);   // frost cap on the coldest ones
            return l;
        }

        // ---- GPU thick-atmosphere worlds (Shaders\AtmoWorld.fx) ----
        // Non-landable High Metal Content bodies with no rings: a tinted surface seen through a haze,
        // clouds and cyclones. Everything else (and any body with rings) keeps the CPU terrain scene.
        public static bool IsAtmoWorld(BodyScanDetail detail, string iconCode) =>
            (iconCode == "HMC" || iconCode == "ICY" || iconCode == "RIB" || iconCode == "WTR" || iconCode == "ELW" || iconCode == "AMW" || iconCode == "RBD") && !detail.Landable && detail.SurfacePressure > 0 &&
            !detail.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);

        // Calibrated against 12 real in-game non-landable HMC screenshots matched to their journal
        // scans (see the surface palettes below). Findings that drive this:
        //  - surface colour is NOT set by composition (every body reads iron/nickel/sulphur ~22/17/16),
        //    it varies per body - so it is picked from a curated palette by a stable hash of the name
        //  - surface pressure sets how much haze hides the surface: ~14 MPa and ~650 kPa CO2 bodies
        //    are a smooth pale blue-grey, bodies under ~400 kPa show the surface and storms
        //  - ice caps appear on a minority of bodies, bigger on the very cold ones
        public static AtmoWorldLook GetAtmoWorldLook(BodyScanDetail detail, string iconCode = "HMC")
        {
            int hash = StableHash(detail.BodyName);
            Color rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

            double logP = Math.Log10(Math.Max(detail.SurfacePressure, 1.0));
            double haze = Math.Pow(Math.Clamp((logP - 5.1) / 0.8, 0.0, 1.0), 1.5);

            int denseVariant = 0;
            int pick = hash % 10;
            Color s0, s1;
            double contrast = 1.0;
            bool icy = iconCode == "ICY";
            bool rockyIce = iconCode == "RIB";
            bool water = iconCode == "WTR";
            bool elw = iconCode == "ELW";
            bool amw = iconCode == "AMW";
            bool rocky = iconCode == "RBD";
            if (rocky && haze <= 0.80)
            {
                int rk = hash % 3;
                if (rk == 0)      { s0 = rgb(118, 96, 72);  s1 = rgb(206, 180, 138); }   // tan
                else if (rk == 1) { s0 = rgb(116, 108, 98); s1 = rgb(198, 190, 178); }   // grey-tan
                else              { s0 = rgb(160, 162, 164); s1 = rgb(228, 230, 230); }   // pale grey-white
                contrast = 0.7;
            }
            else if (water)
            {
                s0 = rgb(20, 34, 50); s1 = rgb(58, 86, 110); contrast = 0.5;   // deep ocean blue, slightly lighter in places
            }
            else if (elw)
            {
                s0 = rgb(8, 28, 62); s1 = rgb(22, 62, 112); contrast = 0.45;  // open ocean: deep blue, a little lighter in places
            }
            else if (amw)
            {
                s0 = rgb(52, 28, 24); s1 = rgb(92, 52, 40); contrast = 0.35;   // dark maroon-brown basins
            }
            else if (rockyIce)
            {
                // Rocky ice worlds (real: XR-D c12-3 A 6/A 8/A 9, ZM-D c12-0 A 6 - all non-landable at tens to
                // hundreds of MPa): a smooth pale blue-grey / aqua / lavender-white shell with fine cracks.
                int rp = hash % 3;
                if (rp == 0)      { s0 = rgb(150, 170, 178); s1 = rgb(196, 210, 214); }
                else if (rp == 1) { s0 = rgb(140, 170, 172); s1 = rgb(186, 212, 210); }
                else              { s0 = rgb(160, 172, 184); s1 = rgb(204, 212, 222); }
                // Thinner atmospheres (real: DG-F b25-0 A 1, methane 305 kPa; A 2, helium 44 kPa) let the surface
                // show: pale cream (methane) or mint-white ice, so blend from the dense smooth shell towards that.
                bool methane = (detail.AtmosphereType ?? "").Contains("Methane", StringComparison.OrdinalIgnoreCase);
                Color lowS0 = methane ? rgb(216, 210, 192) : rgb(204, 222, 216);
                Color lowS1 = methane ? rgb(248, 244, 232) : rgb(240, 248, 244);
                Color Blend(Color a, Color b, double t) => Color.FromRgb(
                    (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
                s0 = Blend(lowS0, s0, haze);
                s1 = Blend(lowS1, s1, haze);
                contrast = 0.45;
            }
            else if (icy)
            {
                // Icy worlds with a real atmosphere: cool slate / teal-grey / lavender-grey ice under the haze.
                // (Real example: XR-D c12-3 B 4, methane 562 kPa, 148 K - a pale blue-grey, cracked ice shell.)
                int ip = hash % 3;
                // Methane atmospheres tint the shell mint-aqua (real: PW-N d6-2 9, 1.3 GPa, and XR-D c12-3 B 4) - hypothesis.
                bool icyMethane = (detail.AtmosphereType ?? "").Contains("Methane", StringComparison.OrdinalIgnoreCase);
                if (icyMethane) ip = 1;
                if (ip == 0)      { s0 = rgb(110, 128, 140); s1 = rgb(164, 184, 194); }
                else if (ip == 1) { s0 = icyMethane ? rgb(150, 188, 180) : rgb(96, 130, 130); s1 = icyMethane ? rgb(204, 232, 224) : rgb(152, 188, 186); }
                else              { s0 = rgb(118, 120, 140); s1 = rgb(170, 172, 192); }
                contrast = 0.55;
                // Argon-rich atmospheres tint the shell warm ivory (real: PW-N d6-2 9 a, 704 kPa, 103 K) - hypothesis.
                if ((detail.AtmosphereType ?? "").Contains("Argon", StringComparison.OrdinalIgnoreCase))
                { s0 = rgb(188, 184, 166); s1 = rgb(234, 232, 216); }
            }
            else if (haze > 0.80)
            {
                // Very dense atmospheres come in three real looks (PW-N d6-2 1/2/3, all 16-28 MPa CO2 and hot, yet orange-red,
                // near-black and pale blue-grey; the 14 MPa UA-L d9-45 2 is pale blue-grey too), so a name hash picks one.
                denseVariant = (hash >> 2) % 4;
                if (denseVariant == 3) denseVariant = 5;   // 4 is reserved for the very-hot lava look
                // Extremely hot bodies (real: RR-N d6-47 A 1, 2,154 K) are near-black with glowing red lava patches.
                bool veryHot = detail.SurfaceTemperature >= 1500;
                // Methane-rich: one real example (PW-N d6-2 8, 2.9 MPa, 372 K) was olive-green under a teal haze - a guess
                // that methane tints the surface that way, to be confirmed with more methane bodies.
                if ((detail.AtmosphereType ?? "").Contains("Methane", StringComparison.OrdinalIgnoreCase)) denseVariant = 3;
                if (rocky) denseVariant = 0;   // real dense rocky bodies (RR-N d6-47 A 2 a, 3.3 MPa) were a pale blue-grey shell
                if (veryHot)                { denseVariant = 4; s0 = rgb(14, 14, 17); s1 = rgb(34, 33, 38); contrast = 0.8; }
                else if (denseVariant == 3) { s0 = rgb(74, 78, 44); s1 = rgb(132, 130, 78); contrast = 0.5; }   // olive-green
                else if (denseVariant == 1) { s0 = rgb(150, 70, 40); s1 = rgb(206, 104, 58); contrast = 0.5; }   // orange-red
                else if (denseVariant == 5) { s0 = rgb(150, 126, 70); s1 = rgb(236, 208, 128); contrast = 0.9; }   // pale blue-grey with a yellow wash (real: RR-N d6-47 B 3)
                else if (denseVariant == 2) { s0 = rgb(24, 26, 32);  s1 = rgb(46, 50, 58);  contrast = 0.5; }   // near-black
                else                        { s0 = rgb(112, 124, 140); s1 = rgb(162, 174, 188); contrast = 0.7; }   // slate
            }
            else if (pick <= 3)     { s0 = rgb(128, 88, 44);   s1 = rgb(196, 150, 84); }                     // ochre / orange
            else if (pick == 4)     { s0 = rgb(92, 40, 30);    s1 = rgb(150, 72, 52); }                      // rust red
            else if (pick <= 6)     { s0 = rgb(66, 52, 44);    s1 = rgb(118, 96, 80); }                      // brown
            else if (pick == 7)     { s0 = rgb(74, 70, 40);    s1 = rgb(128, 120, 70); }                     // olive
            else                    { s0 = rgb(30, 24, 26);    s1 = rgb(64, 60, 68); contrast = 1.2; }       // dark charcoal with a faint rust undertone (real: PW-N d6-2 6, 63 kPa, 242 K)

            // Ice caps: ~40% of bodies, larger on the cold ones.
            bool hasCap = ((hash >> 5) % 10) < 4;
            double cold = detail.SurfaceTemperature > 0 ? Math.Clamp((330 - detail.SurfaceTemperature) / 200.0, 0, 1) : 0;
            double cap = hasCap ? 0.35 + 0.40 * (((hash >> 9) % 100) / 100.0) + 0.20 * cold : 0.0;

            var atmoLook = new AtmoWorldLook
            {
                Surf0 = s0, Surf1 = s1,
                Haze = rgb(152, 168, 186), Cloud = rgb(238, 244, 250), Cap = rgb(238, 230, 214), Glow = rgb(150, 190, 230),
                HazeAmt = haze,
                SurfContrast = contrast,
                CycloneAmt = haze > 0.8 ? 0.0 : 0.35 + 0.65 * (1.0 - haze),   // very dense atmospheres show no storms
                CloudAmt = 0.55 + 0.35 * (1.0 - haze),
                CapAmt = Math.Clamp(cap, 0, 0.95),
                Tilt = (((hash >> 3) % 100) / 100.0 - 0.5) * 0.35,
                Pitch = (((hash >> 12) % 9) - 4) * 0.08,
                Seed = (hash % 5000) / 100.0 + 1.0,
            };
            if (!icy && !rockyIce && denseVariant == 5)
            {
                atmoLook.Haze = rgb(150, 170, 188); atmoLook.Glow = rgb(130, 170, 205);
                atmoLook.Cloud = rgb(222, 224, 226); atmoLook.CycloneAmt = 0.0; atmoLook.CloudAmt = 0.2; atmoLook.CapAmt = 0;
                atmoLook.HazeAmt = 0.80; atmoLook.FleckAmt = 1.0;   // small dark blotches show through the haze
            }
            else if (!icy && !rockyIce && denseVariant == 4)
            {
                atmoLook.Haze = rgb(34, 34, 40); atmoLook.Glow = rgb(70, 90, 112);
                atmoLook.Cloud = rgb(120, 100, 82); atmoLook.CycloneAmt = 0.0; atmoLook.CloudAmt = 0.18; atmoLook.CapAmt = 0;
                atmoLook.HazeAmt = 0.35;
                atmoLook.HotAmt = Math.Clamp((detail.SurfaceTemperature - 1300.0) / 900.0, 0.45, 1.0);
            }
            else if (!icy && !rockyIce && denseVariant == 3)
            {
                atmoLook.Haze = rgb(118, 128, 96); atmoLook.Glow = rgb(96, 168, 176);
                atmoLook.Cloud = rgb(200, 214, 214); atmoLook.CycloneAmt = 0.05; atmoLook.CloudAmt = 0.10;
                atmoLook.HazeAmt = 0.55;
                atmoLook.Cap = rgb(214, 228, 232); atmoLook.CapAmt = 0.45;
            }
            else             if (!icy && !rockyIce && denseVariant == 1)
            {
                // Real example: Eafots JJ-B c13-2 A 1 (CO2, 4.45 MPa, 808 K) - an orange-red surface seen through a pale blue-grey haze
                // veil that lets the surface show in patches, with a distinct pale rim all the way round and an even, frontal light.
                atmoLook.Haze = rgb(156, 172, 194); atmoLook.Glow = rgb(178, 202, 226);
                atmoLook.Cloud = rgb(160, 178, 202); atmoLook.CycloneAmt = 0.0; atmoLook.CloudAmt = 0.85; atmoLook.CapAmt = 0;   // the blue-grey comes in streaky patches over the orange
                atmoLook.HazeAmt = 0.28; atmoLook.SurfContrast = 1.1;
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.20, 0.20, 0.96);
            }
            else if (!icy && !rockyIce && denseVariant == 2)
            {
                atmoLook.Haze = rgb(40, 44, 52); atmoLook.Glow = rgb(70, 88, 110);
                atmoLook.Cloud = rgb(150, 158, 190); atmoLook.CycloneAmt = 0.0; atmoLook.CloudAmt = 0.15; atmoLook.CapAmt = 0;
                atmoLook.HazeAmt = 0.8;
            }
            if (rocky && haze <= 0.80)
            {
                // Rocky bodies with a thin-to-moderate atmosphere (real: PW-N d6-2 8 a, RR-N d6-47 A 2 b/c/d, 28-84 kPa): tan or grey-tan ground
                // under puffy white cloud and cyclones; dense ones fall through to the pale blue-grey shell below.
                atmoLook.Haze = rgb(160, 176, 192); atmoLook.Glow = rgb(130, 170, 205); atmoLook.Cloud = rgb(236, 242, 248);
                atmoLook.HazeAmt = Math.Min(haze, 0.3);
                atmoLook.CycloneAmt = 0.7; atmoLook.CloudAmt = 0.8; atmoLook.CrackAmt = 0.0;
                atmoLook.Cap = rgb(232, 232, 230); atmoLook.CapAmt = ((hash >> 5) % 10) < 5 ? 0.25 + 0.2 * (((hash >> 9) % 100) / 100.0) : 0.0;
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.40, 0.38, 0.84);
            }
            else if (water)
            {
                // Water worlds (real: PW-N d6-2 4/5/7, 64-77 kPa nitrogen, 223-290 K): a dark ocean under scattered puffy cloud,
                // white cyclones with dark eyes and ice caps that grow with cold (the 223 K one has large caps at both poles).
                atmoLook.Haze = rgb(70, 100, 126); atmoLook.Glow = rgb(120, 170, 205);
                atmoLook.Cloud = rgb(226, 234, 242);
                atmoLook.HazeAmt = Math.Min(haze, 0.25);
                atmoLook.CycloneAmt = 0.85; atmoLook.CloudAmt = 0.70; atmoLook.CrackAmt = 0.0;
                double wcold = detail.SurfaceTemperature > 0 ? 0.20 + (300.0 - detail.SurfaceTemperature) / 160.0 : 0.3;
                atmoLook.Cap = rgb(226, 232, 236); atmoLook.CapAmt = Math.Clamp(wcold, 0.15, 0.85);
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.40, 0.38, 0.84);
            }
            else if (elw)
            {
                // Earth-like worlds (references: web screenshots + 9 scanned bodies, 56-415 kPa, 269-309 K): a blue ocean with
                // olive-green land, tan-orange coasts, scattered white cloud and hurricane-style cyclones, a broad sun glint on
                // the water and ice caps that grow with cold. Land colour and coverage come from the name hash; the colder
                // ones go grey-green with bigger caps (real: Drojia EM-C c29-4 6, 266 K).
                atmoLook.Haze = rgb(86, 124, 162); atmoLook.Glow = rgb(96, 160, 220);
                atmoLook.Cloud = rgb(232, 238, 246);
                atmoLook.HazeAmt = Math.Min(haze, 0.14);
                atmoLook.CycloneAmt = 0.90; atmoLook.CloudAmt = 0.55; atmoLook.CrackAmt = 0.0;
                int lv = (hash >> 6) % 4;
                bool chilly = detail.SurfaceTemperature > 0 && detail.SurfaceTemperature < 278;
                if (lv == 1)      { atmoLook.Land0 = rgb(48, 80, 42);  atmoLook.Land1 = rgb(126, 108, 64); }    // greener
                else if (lv == 2) { atmoLook.Land0 = rgb(78, 86, 50);  atmoLook.Land1 = rgb(138, 104, 62); }    // drier, browner
                else              { atmoLook.Land0 = rgb(56, 76, 40);  atmoLook.Land1 = rgb(140, 104, 58); }    // olive with tan
                if (chilly)       { atmoLook.Land0 = rgb(76, 84, 66);  atmoLook.Land1 = rgb(128, 118, 96); }
                atmoLook.LandAmt = 0.14 + 0.16 * (((hash >> 9) % 100) / 100.0);
                double ecold = detail.SurfaceTemperature > 0 ? (300.0 - detail.SurfaceTemperature) / 90.0 : 0.2;
                atmoLook.Cap = rgb(232, 236, 240); atmoLook.CapAmt = Math.Clamp(0.08 + ecold * 0.8, 0.08, 0.7);
                atmoLook.SurfContrast = 0.25;
                atmoLook.GlintAmt = 0.9;
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.40, 0.38, 0.84);
            }
            else if (amw)
            {
                // Ammonia worlds (references: 5 web screenshots; scans show 3-4 MPa nitrogen/argon, ~180 K): dark maroon-brown
                // basins with grey-olive and tan rough-textured patches over about half the surface, a glossy sheen on the dark
                // areas, a few white storms and an amber-to-olive atmosphere rim. They range from dark and sooty to bright
                // orange-tan, and from clear to a thick yellow-brown haze; a name hash picks the variant.
                int av = (hash >> 5) % 4;
                atmoLook.Cloud = rgb(222, 222, 226);
                atmoLook.CycloneAmt = 0.55; atmoLook.CloudAmt = 0.30; atmoLook.CrackAmt = 0.0; atmoLook.CapAmt = 0.0;
                atmoLook.GlintAmt = 0.75;
                atmoLook.LandAmt = 0.18 + 0.14 * (((hash >> 9) % 100) / 100.0);
                if (av == 0)      // standard: grey-olive on maroon
                {
                    atmoLook.Land0 = rgb(78, 74, 60);  atmoLook.Land1 = rgb(150, 120, 84);
                    atmoLook.Haze = rgb(150, 124, 86); atmoLook.Glow = rgb(190, 150, 90); atmoLook.HazeAmt = 0.18;
                }
                else if (av == 1) // bright orange-tan
                {
                    atmoLook.Land0 = rgb(128, 102, 80); atmoLook.Land1 = rgb(186, 142, 96);
                    atmoLook.Haze = rgb(190, 140, 84); atmoLook.Glow = rgb(214, 160, 90); atmoLook.HazeAmt = 0.22;
                }
                else if (av == 2) // thick yellow-brown haze (real: very hazy, soft-edged patches)
                {
                    atmoLook.Land0 = rgb(112, 98, 72); atmoLook.Land1 = rgb(160, 130, 90);
                    atmoLook.Haze = rgb(166, 138, 82); atmoLook.Glow = rgb(206, 180, 110); atmoLook.HazeAmt = 0.55;
                    atmoLook.CycloneAmt = 0.15;
                }
                else              // dark and sooty
                {
                    atmoLook.Land0 = rgb(54, 46, 40);  atmoLook.Land1 = rgb(120, 92, 68);
                    atmoLook.Haze = rgb(110, 86, 62);  atmoLook.Glow = rgb(170, 120, 70); atmoLook.HazeAmt = 0.14;
                }
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.40, 0.38, 0.84);
            }
            else if (rockyIce)
            {
                bool methaneAtm = (detail.AtmosphereType ?? "").Contains("Methane", StringComparison.OrdinalIgnoreCase);
                Color BlendC(Color a, Color b, double t) => Color.FromRgb(
                    (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
                atmoLook.Haze = BlendC(methaneAtm ? rgb(214, 206, 188) : rgb(196, 214, 208), rgb(176, 196, 204), haze);
                atmoLook.Glow = rgb(140, 170, 195);
                // Storm / geyser ovals: tan on methane worlds, blue-grey otherwise (real DG-F b25-0 A 1 and
                // A 2); they fade out as the atmosphere thickens into the smooth shell of the very dense worlds.
                atmoLook.Cloud = BlendC(methaneAtm ? rgb(206, 176, 134) : rgb(168, 184, 212), rgb(238, 244, 250), haze);
                atmoLook.CycloneAmt = 0.95 * (1.0 - haze);
                atmoLook.CloudAmt = 0.05;
                atmoLook.FleckAmt = 0.9 * (1.0 - haze);
                // Thin atmospheres barely tint the surface; only the very dense ones go fully into the haze.
                double hz = haze * 0.45;
                double smoothT = Math.Clamp((haze - 0.7) / 0.3, 0, 1);
                smoothT = smoothT * smoothT * (3 - 2 * smoothT);
                atmoLook.HazeAmt = hz + (0.88 - hz) * smoothT;
                atmoLook.CrackAmt = 0.55 + 0.25 * (1.0 - haze);
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.28, 0.28, 0.92);   // real rocky-ice shots are nearly full-lit
                // Faint pinkish polar tint on some of the dense worlds.
                atmoLook.Cap = rgb(214, 192, 190);
                atmoLook.CapAmt = (((hash >> 5) % 10) < 6 ? 0.30 + 0.15 * (((hash >> 9) % 100) / 100.0) : 0.0) * haze;
            }
            else if (icy)
            {
                atmoLook.CrackAmt = 0.45;
                atmoLook.LightDir = new System.Windows.Media.Media3D.Vector3D(-0.30, 0.30, 0.90);
                // Real icy atmospheres show a cracked ice shell through a cool haze, with few storms.
                // Pressure still decides how hidden the shell is; the haze/glow are cooler than HMC's.
                atmoLook.Haze = ((detail.AtmosphereType ?? "").Contains("Methane", StringComparison.OrdinalIgnoreCase)) ? rgb(176, 208, 202)
                              : ((detail.AtmosphereType ?? "").Contains("Argon", StringComparison.OrdinalIgnoreCase)) ? rgb(214, 210, 192) : rgb(146, 166, 176);
                atmoLook.Glow = rgb(112, 160, 172);
                atmoLook.CycloneAmt = 0.12 * (1.0 - haze);
                atmoLook.CloudAmt = 0.30 * (1.0 - 0.6 * haze);
                atmoLook.CapAmt = 0.0;
                atmoLook.HazeAmt = Math.Clamp(haze * 0.80 + 0.12, 0, 1);
            }
            return atmoLook;
        }

        // Per-class look, tuned against real in-game screenshots of each Sudarsky class. Several
        // classes genuinely vary in game (Class I is dark olive OR cream; Class IV pale beige OR
        // maroon) so those pick a variant from the body name.
        public static GasGiantLook GetGasGiantLook(BodyScanDetail detail, string iconCode)
        {
            int hash = StableHash(detail.BodyName);
            double seed = (hash % 5000) / 100.0 + 1.0;
            bool variantB = ((hash >> 8) & 1) == 1;
            double tiltJitter = (((hash >> 4) % 100) / 100.0 - 0.5) * 0.30;
            Color rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

            var look = new GasGiantLook { Seed = seed, BandCount = 10, Turb = 1.0, StormAmt = 0.5, BigStorm = 0, Contrast = 1.0, Tilt = -0.35 + tiltJitter, Pitch = 0 };
            switch (iconCode)
            {
                case "GG1":
                    if (((hash >> 12) % 3) == 0)
                    {   // dark red-brown belts between cream zones, many tiny oval storms (real: RR-N d6-47 B 5)
                        look.C0 = rgb(0x2a, 0x0f, 0x08); look.C1 = rgb(0x5a, 0x24, 0x12); look.C2 = rgb(0xb8, 0xb0, 0xa2);
                        look.C3 = rgb(0xdc, 0xd6, 0xc8); look.Band4 = rgb(0x18, 0x08, 0x04); look.Glow = rgb(0xc4, 0xb8, 0xa4);
                        look.BandCount = 9; look.Turb = 0.30; look.StormAmt = 1.0; look.Contrast = 1.35;   // tilt left at the default diagonal (preferred by the user)
                    }
                    else if (!variantB)
                    {   // dark olive / taupe
                        look.C0 = rgb(0x3b, 0x2e, 0x24); look.C1 = rgb(0x7a, 0x6c, 0x58); look.C2 = rgb(0xb5, 0xa8, 0x8e);
                        look.C3 = rgb(0xd8, 0xcf, 0xb8); look.Band4 = rgb(0x1e, 0x16, 0x10); look.Glow = rgb(0xc8, 0xb8, 0x98);
                        look.BandCount = 11; look.Turb = 1.1; look.StormAmt = 0.9; look.Contrast = 1.0;
                    }
                    else
                    {   // cream / tan
                        look.C0 = rgb(0x9a, 0x80, 0x62); look.C1 = rgb(0xc8, 0xbb, 0xa6); look.C2 = rgb(0xee, 0xee, 0xec);
                        look.C3 = rgb(0xdd, 0xd6, 0xc8); look.Band4 = rgb(0x7a, 0x62, 0x48); look.Glow = rgb(0xe8, 0xe4, 0xdc);
                        look.BandCount = 11; look.Turb = 0.9; look.StormAmt = 0.55; look.Contrast = 0.85;
                    }
                    break;
                case "GG2":
                    look.C0 = rgb(0xb8, 0xa8, 0x94); look.C1 = rgb(0xd8, 0xd2, 0xca); look.C2 = rgb(0xf2, 0xf1, 0xef);
                    look.C3 = rgb(0xe6, 0xdf, 0xd2); look.Band4 = rgb(0x9a, 0x86, 0x6e); look.Glow = rgb(0xf0, 0xee, 0xea);
                    look.BandCount = 16; look.Turb = 0.6; look.StormAmt = 0.25; look.Contrast = 0.50;
                    break;
                case "GG3":
                    look.C0 = rgb(0x4a, 0x70, 0xc8); look.C1 = rgb(0x52, 0x84, 0xee); look.C2 = rgb(0x6a, 0x9a, 0xf4);
                    look.C3 = rgb(0x28, 0x86, 0xff); look.Band4 = rgb(0x48, 0x62, 0xa8); look.Glow = rgb(0xa8, 0xd0, 0xff);
                    look.BandCount = 9; look.Turb = 0.9; look.StormAmt = 0.0; look.Contrast = 0.30; look.Tilt = 0.45 + tiltJitter;
                    if (variantB)   // deep navy (real: a ringed Class III, game blue about 22,24,65 in shade and ~30,40,125 lit)
                    {
                        look.C0 = rgb(0x14, 0x1c, 0x6a); look.C1 = rgb(0x1c, 0x28, 0x86); look.C2 = rgb(0x26, 0x36, 0xa2);
                        look.C3 = rgb(0x18, 0x34, 0xb4); look.Band4 = rgb(0x0e, 0x14, 0x52); look.Glow = rgb(0x5a, 0x7a, 0xd0);
                        look.Contrast = 0.35;
                    }
                    break;
                case "GG4":
                    if (!variantB)
                    {   // pale pink-beige with dark brown bands and big swirling storms
                        look.C0 = rgb(0x5a, 0x3e, 0x38); look.C1 = rgb(0x9a, 0x7e, 0x74); look.C2 = rgb(0xe0, 0xd0, 0xc8);
                        look.C3 = rgb(0xf0, 0xe4, 0xdc); look.Band4 = rgb(0x3a, 0x26, 0x24); look.Glow = rgb(0xf0, 0xe0, 0xd8);
                        look.BandCount = 9; look.Turb = 1.3; look.StormAmt = 0.35; look.BigStorm = 1; look.Contrast = 1.15; look.Tilt = 0.03 + tiltJitter * 0.2;
                    }
                    else
                    {   // dark maroon (checked against an earlier real screenshot)
                        look.C0 = rgb(0x3a, 0x1c, 0x14); look.C1 = rgb(0x6a, 0x30, 0x22); look.C2 = rgb(0x8a, 0x48, 0x36);
                        look.C3 = rgb(0x9a, 0x58, 0x44); look.Band4 = rgb(0x1a, 0x0c, 0x08); look.Glow = rgb(0x8a, 0x48, 0x36);
                        look.BandCount = 10; look.Turb = 1.0; look.StormAmt = 0.4; look.Contrast = 0.7;
                    }
                    break;
                case "GG5":
                    look.C0 = rgb(0x8a, 0x94, 0x84); look.C1 = rgb(0xb8, 0xc4, 0xb4); look.C2 = rgb(0xe0, 0xe8, 0xdc);
                    look.C3 = rgb(0xc8, 0xd4, 0xc4); look.Band4 = rgb(0x6a, 0x72, 0x68); look.Glow = rgb(0xd8, 0xe4, 0xd4);
                    look.BandCount = 20; look.Turb = 0.35; look.StormAmt = 0.08; look.Contrast = 0.85; look.Tilt = -0.2 + tiltJitter * 0.3;
                    break;
                case "GGH":
                    look.C0 = rgb(0x1c, 0x16, 0x10); look.C1 = rgb(0x44, 0x38, 0x28); look.C2 = rgb(0xa8, 0x98, 0x80);
                    look.C3 = rgb(0xd0, 0xc4, 0xb0); look.Band4 = rgb(0x10, 0x0c, 0x08); look.Glow = rgb(0xa8, 0x98, 0x80);
                    look.BandCount = 4; look.Turb = 1.7; look.StormAmt = 0.3; look.Contrast = 0.55;
                    break;
                case "GGA":
                    look.C0 = rgb(0x08, 0x06, 0x04); look.C1 = rgb(0x1c, 0x16, 0x10); look.C2 = rgb(0x6a, 0x58, 0x3c);
                    look.C3 = rgb(0x8a, 0x76, 0x52); look.Band4 = rgb(0x04, 0x03, 0x02); look.Glow = rgb(0x6a, 0x58, 0x3c);
                    look.BandCount = 12; look.Turb = 1.1; look.StormAmt = 0.85; look.Contrast = 1.1; look.Tilt = -0.45 + tiltJitter * 0.3;   // real B6 shows many small dark and pale storms
                    break;
                case "GGW":
                    // Two real looks: seen nearly pole-on (concentric rings, ~1 in 3), or - as in a later reference (A3,
                    // 242 K) - a normal pale grey-white giant with tan belts and small pale/tan oval storms.
                    bool ggwRinged = detail.Rings.Any(rg => rg.OuterRad > 0 && rg.InnerRad > 0 && !rg.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));
                    if (!ggwRinged && ((hash >> 10) % 3) == 0)   // pole-on only without rings
                    {
                        look.C0 = rgb(0x24, 0x20, 0x1a); look.C1 = rgb(0x4a, 0x46, 0x3c); look.C2 = rgb(0x8a, 0x88, 0x78);
                        look.C3 = rgb(0x9a, 0x98, 0x88); look.Band4 = rgb(0x16, 0x12, 0x0e); look.Glow = rgb(0x7a, 0x78, 0x68);
                        look.BandCount = 14; look.Turb = 0.5; look.StormAmt = 0.8; look.Contrast = 0.6; look.Pitch = 1.40;
                    }
                    else
                    {
                        look.C0 = rgb(0x6e, 0x6e, 0x6a); look.C1 = rgb(0x96, 0x98, 0x94); look.C2 = rgb(0xc8, 0xca, 0xc6);
                        look.C3 = rgb(0xd2, 0xac, 0x84); look.Band4 = rgb(0x78, 0x68, 0x58); look.Glow = rgb(0xbe, 0xc0, 0xbc);
                        look.BandCount = 12; look.Turb = 0.9; look.StormAmt = 0.9; look.Contrast = 0.55; look.Pitch = 0.0; look.Tilt = 0.02;   // real A3 bands run dead horizontal
                        if (variantB || ggwRinged)   // dark-brown / tan with dark belts (real: ringed A4, 176 K); ringed water-life giants are always brown
                        {
                            look.C0 = rgb(0x24, 0x16, 0x10); look.C1 = rgb(0x50, 0x34, 0x22); look.C2 = rgb(0x8c, 0x6a, 0x46);
                            look.C3 = rgb(0xaa, 0x86, 0x5c); look.Band4 = rgb(0x14, 0x0a, 0x06); look.Glow = rgb(0x7a, 0x5c, 0x3c);
                            look.Contrast = 0.85; look.Turb = 1.1;
                        }
                    }
                    break;
                case "WTG":
                    look.C0 = rgb(0x04, 0x06, 0x18); look.C1 = rgb(0x0a, 0x12, 0x34); look.C2 = rgb(0x2a, 0x3a, 0x80);
                    look.C3 = rgb(0x6a, 0x80, 0xc8); look.Band4 = rgb(0x02, 0x03, 0x0c); look.Glow = rgb(0x30, 0x40, 0x90);
                    look.BandCount = 12; look.Turb = 0.8; look.StormAmt = 0.45; look.Contrast = 0.55; look.Tilt = 0.04;
                    break;
                default:
                    var pal = GetPalette(iconCode);
                    look.C0 = pal.c0; look.C1 = pal.c1; look.C2 = pal.c2; look.C3 = pal.c3; look.Band4 = pal.band4; look.Glow = pal.glow;
                    break;
            }
            // A ringed giant's cloud bands run parallel to its rings (real: a ringed Class III), so the band tilt follows the
            // ring tilt used by the ring renderer.
            if (detail.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0 && !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase)))
            {
                look.Tilt = RingTilt;
                look.Pitch = 0;   // never pole-on with rings: concentric bands would contradict the ring plane
            }
            return look;
        }

        private static double Seeded(double i)
        {
            var x = Math.Sin(i * 999.7) * 43758.5453;
            return x - Math.Floor(x);
        }

        // Per-class band palette: B7 ("gas giant with water based life") set the teal/gold
        // GGW look; Water Giant drops the life tint for pale blue-white; Sudarsky I-V follow
        // real astronomical convention (I ammonia-cream, II water-cloud white-blue, III
        // cloudless deep blue, IV hot tan/charcoal, V hot dark red) rather than one generic
        // "gas giant" look.
        private static (Color c0, Color c1, Color c2, Color c3, Color band4, Color glow) GetPalette(string iconCode) => iconCode switch
        {
            "GGW" => (Color.FromRgb(0x0b,0x3b,0x3f), Color.FromRgb(0x1f,0x7a,0x70), Color.FromRgb(0x8f,0xd9,0xc4), Color.FromRgb(0xd9,0xc9,0x8a), Color.FromRgb(0x12,0x50,0x55), Color.FromRgb(0x8f,0xd9,0xc4)),
            "GGA" => (Color.FromRgb(0x4a,0x2e,0x1e), Color.FromRgb(0x7a,0x3a,0x2e), Color.FromRgb(0xd8,0xb8,0x90), Color.FromRgb(0x5a,0x3a,0x24), Color.FromRgb(0x3a,0x1e,0x14), Color.FromRgb(0xe0,0x8a,0x4e)),
            "WTG" => (Color.FromRgb(0x3a,0x5a,0x64), Color.FromRgb(0x7f,0x9c,0xa6), Color.FromRgb(0xd6,0xe4,0xe8), Color.FromRgb(0xee,0xf4,0xf6), Color.FromRgb(0x5a,0x7a,0x84), Color.FromRgb(0xb4,0xd7,0xe6)),
            "GG1" => (Color.FromRgb(0x6a,0x5a,0x3a), Color.FromRgb(0x9a,0x86,0x5e), Color.FromRgb(0xe8,0xdc,0xc0), Color.FromRgb(0xc9,0xb0,0x80), Color.FromRgb(0x4a,0x3e,0x28), Color.FromRgb(0xe0,0xd0,0xa8)),
            "GG2" => (Color.FromRgb(0x2a,0x4a,0x5e), Color.FromRgb(0x5a,0x82,0x9a), Color.FromRgb(0xd8,0xe8,0xf0), Color.FromRgb(0xa8,0xc8,0xd8), Color.FromRgb(0x1a,0x30,0x40), Color.FromRgb(0xa8,0xd0,0xe8)),
            "GG3" => (Color.FromRgb(0x0e,0x1e,0x3a), Color.FromRgb(0x1e,0x38,0x62), Color.FromRgb(0x4a,0x6a,0x9a), Color.FromRgb(0x2a,0x4a,0x7a), Color.FromRgb(0x06,0x10,0x22), Color.FromRgb(0x4a,0x6a,0xb0)),
            // Shifted more red/maroon than the original tan-brown — checked against a real
            // in-game screenshot of a Class IV giant, whose sphere reads as a fairly uniform
            // dark maroon/red-brown, not the more yellow-brown tan this palette had before.
            "GG4" => (Color.FromRgb(0x3a,0x1c,0x14), Color.FromRgb(0x6a,0x30,0x22), Color.FromRgb(0x8a,0x48,0x36), Color.FromRgb(0x2a,0x14,0x0e), Color.FromRgb(0x1a,0x0c,0x08), Color.FromRgb(0x8a,0x48,0x36)),
            "GG5" => (Color.FromRgb(0x2a,0x0e,0x0a), Color.FromRgb(0x4a,0x1a,0x14), Color.FromRgb(0x6a,0x28,0x1e), Color.FromRgb(0x3a,0x12,0x0e), Color.FromRgb(0x1a,0x08,0x06), Color.FromRgb(0x8a,0x30,0x20)),
            "GGH" => (Color.FromRgb(0x3a,0x3a,0x4a), Color.FromRgb(0x6a,0x6a,0x8a), Color.FromRgb(0xc8,0xc8,0xe0), Color.FromRgb(0x9a,0x9a,0xc0), Color.FromRgb(0x22,0x22,0x30), Color.FromRgb(0xa8,0xa8,0xe0)),
            _     => (Color.FromRgb(0x2a,0x4a,0x5e), Color.FromRgb(0x5a,0x82,0x9a), Color.FromRgb(0xd8,0xe8,0xf0), Color.FromRgb(0xa8,0xc8,0xd8), Color.FromRgb(0x1a,0x30,0x40), Color.FromRgb(0xa8,0xd0,0xe8)),
        };

        // Real ring material tint, from the actual eRingClass_* value — Icy pale blue-white,
        // Rocky tan/brown, Metallic grey-silver, MetalRich warm gold/copper. Previously every
        // ring rendered the same tan regardless of its real class.
        // internal — SystemScanWindow reuses this for its own attached-star ring icon.
        internal static Color GetRingColor(string ringClass) => ringClass switch
        {
            "eRingClass_Icy"       => Color.FromRgb(0xd8, 0xe8, 0xec),
            "eRingClass_Metalic"   => Color.FromRgb(0x9a, 0x9c, 0xa0),
            // Was 0xd4aa5c — a fairly saturated gold that read as "too yellow" against a real
            // MetalRich ring. Shifted to a more muted copper/tan, still warmer than plain
            // Rocky's tan but not a gold/amber tone.
            // Real ringed giants (RR-N d6-47 A 2 and A 4, MetalRich inner + Rocky outer): the thin metal-rich band reads pale
            // grey-tan and the wide rocky band dark brown, not copper / cream.
            "eRingClass_MetalRich" => Color.FromRgb(0xb8, 0xaf, 0x98),
            "eRingClass_Rocky"     => Color.FromRgb(0x6a, 0x4a, 0x32),
            _                      => Color.FromRgb(0xc9, 0xb2, 0x87),
        };

        // Shades one ring band by its position among the body's other rings — inner bands
        // darker, outer bands brighter — so a multi-ring system where every band shares the
        // same real RingClass (a common case; B7's two rings are both Rocky) still reads as
        // visually distinct bands instead of one indistinguishable slab of color.
        private static Color ShadeRing(Color c, int index, int total)
        {
            if (total <= 1) return c;
            double t = (double)index / (total - 1);
            double factor = 0.76 + t * 0.4; // 0.76 (innermost) .. 1.16 (outermost)
            byte Adj(byte ch) => (byte)Math.Clamp(ch * factor, 0, 255);
            return Color.FromRgb(Adj(c.R), Adj(c.G), Adj(c.B));
        }

        private static Geometry MakeRingAnnulus(double cx, double cy, double rIn, double rOut, double tilt, double squash)
        {
            var outerE = new EllipseGeometry(new Point(cx, cy), rOut, rOut);
            var innerE = new EllipseGeometry(new Point(cx, cy), rIn, rIn);
            var annulus = new CombinedGeometry(GeometryCombineMode.Exclude, outerE, innerE);
            var xf = new TransformGroup();
            xf.Children.Add(new ScaleTransform(1, squash, cx, cy));
            xf.Children.Add(new RotateTransform(tilt * 180 / Math.PI, cx, cy));
            annulus.Transform = xf;
            return annulus;
        }

        // Real gas-giant rings (checked against an actual in-game screenshot of a Class IV
        // giant) show fine multi-band texture — many alternating light/dark stripes, like
        // Saturn's real banding — even though the journal only ever reports one flat Ring
        // entry per real ring. Subdividing each real band into several thinner sub-annuli with
        // seeded brightness variation approximates that texture; sub-band count scales with the
        // band's own actual pixel width (targeting a roughly constant stripe thickness) rather
        // than a fixed count, since a real ring system's bands can differ hugely in width (one
        // real Class IV giant's outer ring reaches 8x its own planet radius while the inner
        // ring sits under 2.5x — a fixed sub-band count would make the wide band's stripes look
        // much coarser than the narrow band's).
        internal static void DrawRingBandTextured(DrawingContext dc, double cx, double cy, RingBand band,
            double tilt, double squash, double seedBase, int bandIndex, double planetR)
        {
            double totalWidth = band.OuterPx - band.InnerPx;
            double targetSubWidth = planetR * 0.035;
            int subBands = (int)Math.Clamp(Math.Round(totalWidth / targetSubWidth), 5, 26);
            double subWidth = totalWidth / subBands;
            for (int i = 0; i < subBands; i++)
            {
                double subInner = band.InnerPx + i * subWidth;
                double subOuter = subInner + subWidth + 0.5; // slight overlap so no hairline seams
                double shade = 0.72 + Seeded(seedBase + bandIndex * 97 + i * 7.3) * 0.56; // ~0.72x-1.28x
                byte R2 = (byte)Math.Clamp(band.Color.R * shade, 0, 255);
                byte G2 = (byte)Math.Clamp(band.Color.G * shade, 0, 255);
                byte B2 = (byte)Math.Clamp(band.Color.B * shade, 0, 255);
                var geo = MakeRingAnnulus(cx, cy, subInner, subOuter, tilt, squash);
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(R2, G2, B2)), null, geo);
            }
        }

        // internal — StarRenderer reuses this (plus ComputeRingBands/DrawRingBandTextured/
        // RingTilt/RingSquash below) to draw real rings around a ringed dim star, same
        // technique as a gas giant's own rings.
        internal class RingBand
        {
            public double InnerPx, OuterPx;
            public Color Color;
            public string Name = "";
        }

        internal const double RingTilt = -0.16, RingSquash = 0.30;

        // Shared by RenderScene and GetRingMiningAnchor so the badge anchor math can never
        // drift out of sync with what's actually drawn.
        //
        // Real ring geometry — inner/outer radius scaled against this body's own real
        // physical Radius, not an arbitrary band. Rings with no radius data (never fully
        // resolved) are skipped rather than guessed.
        //
        // A flat per-radius clamp (the previous approach) broke down for a ring system whose
        // real span is wide: B7's two adjacent bands run from 1.75x to 2.93x the planet's own
        // radius, but clamping each ratio to a fixed max (1.85x) collapsed the far band down
        // to almost the same edge as the near one, rendering as one thin ring instead of the
        // real wide double band. Instead, the body's own actual min/max ratio (however wide or
        // narrow it really is) is linearly mapped onto a fixed pixel range that fits the
        // canvas — this preserves the real proportions between multiple rings on the same
        // body while still always fitting inside the scene and always staying outside the
        // sphere's own silhouette.
        // Shared by the gas giant renderer and the terrain renderer (RenderTerrainScene) —
        // real ring geometry, scaled against this body's own real physical Radius, mapped
        // linearly onto a caller-supplied [minPx, maxPx] pixel range rather than a fixed
        // per-radius clamp (a flat clamp broke down for a body whose real ring span is wide:
        // one real ring system ran from 1.75x to 2.93x the planet's own radius, and clamping
        // each ratio to a fixed max collapsed the far band down to almost the same edge as the
        // near one). minPx/maxPx are passed in rather than computed here because the gas giant
        // and terrain families need very different values for the same reason described where
        // each one calls this — their sphere sizes take up very different fractions of the
        // canvas, so a shared fixed formula doesn't fit both.
        internal static List<RingBand> ComputeRingBands(BodyScanDetail detail, double minPx, double maxPx)
        {
            var rings = new List<RingBand>();
            if (detail.Radius <= 0) return rings;

            var defs = detail.Rings.Where(r => r.OuterRad > 0 && r.InnerRad > 0).ToList();
            if (defs.Count == 0) return rings;

            double minRatio = defs.Min(r => r.InnerRad / detail.Radius);
            double maxRatio = defs.Max(r => r.OuterRad / detail.Radius);
            if (maxRatio <= minRatio) maxRatio = minRatio + 0.01;
            double Scale(double ratio) =>
                minPx + (ratio - minRatio) / (maxRatio - minRatio) * (maxPx - minPx);

            for (int i = 0; i < defs.Count; i++)
            {
                var r = defs[i];
                var innerPx = Scale(r.InnerRad / detail.Radius);
                var outerPx = Scale(r.OuterRad / detail.Radius);
                if (outerPx <= innerPx) outerPx = innerPx + (maxPx - minPx) * 0.08;
                rings.Add(new RingBand
                {
                    InnerPx = innerPx,
                    OuterPx = outerPx,
                    // Adjacent bands sharing the same real RingClass (B7's two rings are both
                    // Rocky) rendered as one indistinguishable band — shade each band by its
                    // position in the sequence (inner darker, outer brighter) so multiple rings
                    // actually read as separate bands.
                    Color = ShadeRing(GetRingColor(r.RingClass), i, defs.Count),
                    Name = r.Name,
                });
            }
            return rings;
        }

        private static (double cx, double cy, double R, List<RingBand> rings) ComputeGeometry(
            BodyScanDetail detail, int width, int height)
        {
            double cx = width / 2.0, cy = height / 2.0 + height * 0.02;
            // Tied to min(width,height), not height alone — the scene isn't always square,
            // and R being computed only from height is exactly how the ring math went back to
            // not fitting after an earlier width-only change. 0.23 leaves real room for a wide
            // ring to extend beyond the sphere inside whatever canvas is passed in.
            double R = Math.Min(width, height) * 0.23;
            // 1.08 → 1.4: the ring rendered clear up to the sphere's own edge with no breathing
            // room at all — the mockup has a visible gap between the planet and where the ring
            // actually starts.
            double minPx = R * 1.4;
            double maxPx = Math.Min(width, height) * 0.46; // stays inside the bitmap edge
            var rings = ComputeRingBands(detail, minPx, maxPx);
            return (cx, cy, R, rings);
        }

        // Anchor point for the ring-mining badge, in the same unscaled scene coordinates as
        // the render itself (caller adds its own sceneX/sceneY offset). Previously a fixed
        // pixel offset from the scene's own corner — that only ever lined up with the ring by
        // accident, and broke completely once ring geometry started varying per body. Uses the
        // OUTERMOST ring's midline (was the innermost) directly to the left of the planet —
        // sitting on the near/inner ring put the badge right up against the sphere, colliding
        // with the surface-mining badge on bodies that have both; out on the far ring keeps it
        // clearly separated from the planet itself.
        public static Point? GetRingMiningAnchor(BodyScanDetail detail, int width, int height)
        {
            var (cx, cy, _, rings) = ComputeGeometry(detail, width, height);
            if (rings.Count == 0) return null;
            var band = rings[rings.Count - 1];
            double rr = (band.InnerPx + band.OuterPx) / 2.0;
            const double ang = Math.PI; // due left
            double x = Math.Cos(ang) * rr, y = Math.Sin(ang) * rr * RingSquash;
            double rad = RingTilt;
            double rx = x * Math.Cos(rad) - y * Math.Sin(rad);
            double ry = x * Math.Sin(rad) + y * Math.Cos(rad);
            return new Point(cx + rx, cy + ry);
        }

        // Storm ovals scattered among the cloud bands — 1-3 per body, seeded so the same body
        // always shows the same storms in the same places. Mid-latitude biased (real storms
        // rarely sit right at a pole or dead on the equator), horizontally elongated like real
        // Jovian storm ovals, alternating between the palette's own lighter and darker band
        // tones rather than one fixed storm color.
        private static void DrawGasGiantStorms(DrawingContext dc, double cx, double cy, double sphereR,
            double seedBase, Color lightColor, Color darkColor)
        {
            // A real zoomed screenshot showed 15-20+ small storm ovals visible at once, each
            // with a distinct darker/lighter RING outline around a contrasting filled core —
            // nowhere near the 1-3 large soft blobs this had before, which read as "a pale
            // spot" against the bands. Many small ringed ovals, not a couple of big fuzzy ones.
            // Dialed back from 12-21 (which read as overwhelming) — 1-10, still enough variety
            // for some bodies to look busy and others sparse, without carpeting every giant in
            // storm ovals.
            int stormCount = 1 + (int)(Seeded(seedBase + 600) * 10); // 1-10
            for (int i = 0; i < stormCount; i++)
            {
                double latFrac = (Seeded(seedBase + i * 41 + 601) - 0.5) * 1.5;
                double lonFrac = (Seeded(seedBase + i * 53 + 601) - 0.5) * 1.7;
                double sx = cx + lonFrac * sphereR;
                double sy = cy + latFrac * sphereR * 0.5;

                double stormR = sphereR * (0.02 + Seeded(seedBase + i * 17 + 601) * 0.035);
                double squash = 0.8 + Seeded(seedBase + i * 23 + 601) * 0.2;

                // The ring outline and the core often read as opposite tones (a dark rim around
                // a pale eye, or the reverse) — a flat single-color blob doesn't have that
                // contrast at all.
                bool ringIsDark = Seeded(seedBase + i * 31 + 601) < 0.55;
                var ringColor = ringIsDark ? darkColor : lightColor;
                var coreColor = ringIsDark ? lightColor : darkColor;

                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, coreColor.R, coreColor.G, coreColor.B)),
                    null, new Point(sx, sy), stormR * 0.65, stormR * 0.65 * squash);
                var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(210, ringColor.R, ringColor.G, ringColor.B)), stormR * 0.24);
                dc.DrawEllipse(null, ringPen, new Point(sx, sy), stormR, stormR * squash);
            }
        }

        // ---- static layer: ring back-pass + sphere base gradient only (no bands, no limb
        // darkening yet — limb darkening has to sit ON TOP of the animated cloud layer, so it
        // belongs in RenderGasGiantTop instead, drawn after that layer composites). ----
        private static RenderTargetBitmap RenderGasGiantBase(BodyScanDetail detail, string iconCode, int width, int height, bool includeSphere = true)
        {
            var (cx, cy, R, rings) = ComputeGeometry(detail, width, height);
            var pal = GetPalette(iconCode);
            const double TILT = RingTilt, SQUASH = RingSquash;
            double sphereR = rings.Count > 0 ? R * 0.85 : R;
            // Body-derived, not phase-derived — ring texture is static (same convention as
            // this whole layer), and RenderGasGiantTop needs the SAME seed so its front-sliver
            // pass shows identical sub-band texture at the same physical radius as this back
            // pass, not an unrelated random pattern.
            double ringSeed = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (int i = 0; i < rings.Count; i++)
                    DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);

                if (includeSphere)
                {
                    dc.PushClip(new EllipseGeometry(new Point(cx, cy), sphereR, sphereR));
                    var baseBrush = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.5, 0.5),
                        RadiusX = 0.75, RadiusY = 0.75,
                    };
                    baseBrush.GradientStops.Add(new GradientStop(pal.c2, 0.0));
                    baseBrush.GradientStops.Add(new GradientStop(pal.c1, 0.4));
                    baseBrush.GradientStops.Add(new GradientStop(pal.c0, 0.75));
                    baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x04, 0x08, 0x08), 1.0));
                    dc.DrawRectangle(baseBrush, null, new Rect(cx - sphereR, cy - sphereR, sphereR * 2, sphereR * 2));
                    dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        // ---- dynamic layer: cloud bands only, on an otherwise transparent bitmap — the ONLY
        // thing that's supposed to actually cross-fade between frame A and frame B. ----
        private static RenderTargetBitmap RenderGasGiantClouds(BodyScanDetail detail, string iconCode, int width, int height, double phase)
        {
            var (cx, cy, R, rings) = ComputeGeometry(detail, width, height);
            var pal = GetPalette(iconCode);
            // Storms use the body-only seed (no phase) so they land in the exact same spot in
            // both cross-faded frames — sharing the band wobble's own phase-shifted seed would
            // reroll their position almost entirely between frame A and B (Seeded() is far too
            // sensitive to a shift this large to read as "drift"), producing a storm that
            // visibly teleports during the cross-fade instead of sitting still while the bands
            // wobble around it.
            double stormSeed = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;
            double seedBase = stormSeed + phase;
            double sphereR = rings.Count > 0 ? R * 0.85 : R;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new EllipseGeometry(new Point(cx, cy), sphereR, sphereR));
                var bandColors = new[] { pal.c0, pal.c1, pal.c2, pal.c3, pal.band4 };
                const int bandCount = 26;
                for (int b = 0; b < bandCount; b++)
                {
                    double yFrac = (double)b / bandCount;
                    double y = cy - sphereR + yFrac * sphereR * 2;
                    double wobble = Math.Sin(yFrac * 14 + seedBase) * sphereR * 0.05 + Math.Sin(yFrac * 3 - seedBase * 0.4) * sphereR * 0.03;

                    var geo = new StreamGeometry();
                    using (var gc = geo.Open())
                    {
                        gc.BeginFigure(new Point(cx - sphereR, y + wobble), false, false);
                        var pts = new List<Point>();
                        for (double x = -sphereR; x <= sphereR; x += 8)
                            pts.Add(new Point(cx + x, y + wobble + Math.Sin(x * 0.04 + yFrac * 20 + seedBase) * 3));
                        gc.PolyLineTo(pts, true, false);
                    }
                    var brush = new SolidColorBrush(bandColors[b % bandColors.Length]) { Opacity = 0.5 };
                    dc.DrawGeometry(null, new Pen(brush, sphereR * 2 / bandCount * 1.05), geo);
                }

                // Real gas giants show storm ovals (Jupiter's Great Red Spot and its smaller
                // cousins) among the bands, not just uniform striping — checked against a real
                // gas giant screenshot. Colored from the same per-class palette as the bands
                // themselves, not an invented color.
                DrawGasGiantStorms(dc, cx, cy, sphereR, stormSeed, pal.c2, pal.c3);

                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        // ---- static layer: limb darkening (sits over the cloud layer beneath it), ring
        // front-sliver, atmosphere glow — none of this depends on cloud phase either, so it
        // renders once and composites on top of both the base and cloud layers, staying rock
        // still regardless of the cross-fade animating underneath it. ----
        private static RenderTargetBitmap RenderGasGiantTop(BodyScanDetail detail, string iconCode, int width, int height, bool includeLimb = true)
        {
            var (cx, cy, R, rings) = ComputeGeometry(detail, width, height);
            var pal = GetPalette(iconCode);
            const double TILT = RingTilt, SQUASH = RingSquash;
            double sphereR = rings.Count > 0 ? R * 0.85 : R;
            // Same body-derived seed as RenderGasGiantBase, so this front-sliver pass's texture
            // matches the back pass's at the same physical radius instead of drawing an
            // unrelated random pattern.
            double ringSeed = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (includeLimb)
                {
                    dc.PushClip(new EllipseGeometry(new Point(cx, cy), sphereR, sphereR));
                    var limbBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                    limbBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 4, 10, 14), 0.6));
                    limbBrush.GradientStops.Add(new GradientStop(Color.FromArgb(140, 4, 10, 14), 1.0));
                    dc.DrawRectangle(limbBrush, null, new Rect(cx - sphereR, cy - sphereR, sphereR * 2, sphereR * 2));
                    dc.Pop();
                }

                // ---- ring front sliver — same bands again, clipped to (planet circle) ∩
                // (front half-plane), the classic ring-passes-in-front-of-the-near-limb
                // illusion ----
                if (rings.Count > 0)
                {
                    var planetCircle = new EllipseGeometry(new Point(cx, cy), sphereR, sphereR);
                    // The ring's true "equator" cut: a half-plane through the ring's own
                    // center (cx,cy), rotated by the SAME tilt as the ring geometry itself —
                    // everything below this line is the ring's near/front half. The previous
                    // version used a fixed axis-aligned rectangle offset below center that
                    // never rotated with the ring at all; that only looked right by accident
                    // at the old, much-narrower ring radii — once the ring got proportionally
                    // wider/thicker, the mismatch between the ring's real tilted plane and
                    // this un-rotated rectangle produced a jagged, ill-fitting notch instead
                    // of the mockup's clean diagonal cut.
                    var halfPlane = new RectangleGeometry(new Rect(cx - R * 4, cy, R * 8, R * 4));
                    halfPlane.Transform = new RotateTransform(TILT * 180 / Math.PI, cx, cy);
                    var sliverClip = new CombinedGeometry(GeometryCombineMode.Intersect, planetCircle, halfPlane);
                    dc.PushClip(sliverClip);
                    for (int i = 0; i < rings.Count; i++)
                        DrawRingBandTextured(dc, cx, cy, rings[i], TILT, SQUASH, ringSeed, i, R);
                    dc.Pop();
                }

                // ---- atmosphere glow, drawn last so it sits over the ring too ----
                // Shader path (includeLimb false): the shader draws its own rim, so keep this halo
                // faint and tinted from the same per-class look instead of the old palette.
                var glowColor = includeLimb ? pal.glow : GetGasGiantLook(detail, iconCode).Glow;
                byte glowAlpha = includeLimb ? (byte)110 : (byte)38;
                var glowBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(glowAlpha, glowColor.R, glowColor.G, glowColor.B), 0.83));
                glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 1.0));
                dc.DrawEllipse(glowBrush, null, new Point(cx, cy), sphereR * 1.2, sphereR * 1.2);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        // ==================== Terrain bodies (High Metal Content first) ====================
        // The "High Metal Content Render Concept" mockup — real impact craters (soft bowl
        // gradients, sparse/scattered not confetti), glowing fissures gated on the body's own
        // real Volcanism field, surface tint from its own real Materials list, a tidal-lock
        // terminator when TidalLock is true, and a hot/cold airless-limb shimmer driven by real
        // SurfaceTemperature, and real rings when the body has them (added later — see
        // RenderTerrainScene's own ring back-pass/front-sliver). No clouds, no cross-fade
        // animation — this family is a single static bake per body.
        public static bool IsTerrainFamily(string? iconCode) =>
            iconCode == "HMC" || iconCode == "ICY" || iconCode == "RBD" || iconCode == "RIB" || iconCode == "WTR" || iconCode == "MRB" || iconCode == "ELW" || iconCode == "AMW";

        // systemPopulation gates Earthlike city lights (only an inhabited system would show
        // any) — it's real per-SYSTEM data, not on the body's own BodyScanDetail, so it comes
        // in as its own parameter rather than living on `detail`.
        public static BitmapSource GetTerrainSceneFrame(BodyScanDetail detail, string iconCode, int width = 620, int height = 460, long systemPopulation = 0)
        {
            var key = detail.BodyName + "|terrain|" + iconCode + "|" + width + "x" + height + "|" + (systemPopulation > 0 ? "pop" : "nopop");
            if (!_cache.TryGetValue(key, out var bmp)) { bmp = RenderTerrainScene(detail, iconCode, width, height, systemPopulation); _cache[key] = bmp; }
            return bmp;
        }

        // Real per-material tint — same convention as the mockup's own 5-material list, plus
        // the rest of the common rocky/HMC material set so anything a real scan reports has a
        // sensible color rather than falling through to one generic default every time.
        // Small RGB<->HSL pair used only to widen HMC's base-tone variety (see isHmc below) —
        // real report + screenshot: HMC bodies rendered in a narrow band of muddy brown/grey
        // (every real material's own fixed tint color happens to sit there), when real HMC
        // screenshots range from vivid Mars-like red/orange through to blue-grey, almost
        // Earthlike-looking. Rotating hue/boosting saturation in HSL space (not just lerping
        // toward a fixed color) is what actually reaches those other families while still
        // starting from the body's own real dominant material.
        private static (double h, double s, double l) RgbToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60;
            }
            return (h, s, l);
        }
        private static Color HslToRgb(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            if (s <= 0) { byte v = (byte)Math.Round(l * 255); return Color.FromRgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double Hue(double t)
            {
                t = ((t % 360) + 360) % 360;
                if (t < 60) return p + (q - p) * t / 60;
                if (t < 180) return q;
                if (t < 240) return p + (q - p) * (240 - t) / 60;
                return p;
            }
            byte R = (byte)Math.Round(Math.Clamp(Hue(h + 120), 0, 1) * 255);
            byte G = (byte)Math.Round(Math.Clamp(Hue(h), 0, 1) * 255);
            byte B = (byte)Math.Round(Math.Clamp(Hue(h - 120), 0, 1) * 255);
            return Color.FromRgb(R, G, B);
        }

        private static Color GetMaterialTintColor(string material) => material.ToLowerInvariant() switch
        {
            "iron"        => Color.FromRgb(0x8a, 0x4a, 0x34),
            "nickel"      => Color.FromRgb(0x6d, 0x65, 0x60),
            "sulphur"     => Color.FromRgb(0xc9, 0xa2, 0x4a),
            "carbon"      => Color.FromRgb(0x2a, 0x21, 0x1c),
            "chromium"    => Color.FromRgb(0x9c, 0x7a, 0x5c),
            "phosphorus"  => Color.FromRgb(0xb8, 0xa8, 0x88),
            "manganese"   => Color.FromRgb(0x8a, 0x78, 0x68),
            "zinc"        => Color.FromRgb(0x9a, 0xa0, 0xa0),
            "arsenic"     => Color.FromRgb(0x8a, 0x9a, 0x6a),
            "germanium"   => Color.FromRgb(0x7a, 0x8a, 0x9a),
            "vanadium"    => Color.FromRgb(0x6a, 0x7a, 0x8a),
            "zirconium"   => Color.FromRgb(0xa8, 0x98, 0x78),
            "cadmium"     => Color.FromRgb(0xc9, 0xa8, 0x78),
            "mercury"     => Color.FromRgb(0xb8, 0xb8, 0xc0),
            "molybdenum"  => Color.FromRgb(0x7a, 0x6a, 0x5a),
            "niobium"     => Color.FromRgb(0x8a, 0x7a, 0x6a),
            "tin"         => Color.FromRgb(0xb0, 0xa8, 0x98),
            "tungsten"    => Color.FromRgb(0x5a, 0x50, 0x48),
            "antimony"    => Color.FromRgb(0x9a, 0x8a, 0x7a),
            "polonium"    => Color.FromRgb(0xb0, 0x4a, 0x3a),
            "ruthenium"   => Color.FromRgb(0x7a, 0x7a, 0x8a),
            "selenium"    => Color.FromRgb(0xb8, 0x98, 0x5a),
            "technetium"  => Color.FromRgb(0x6a, 0x5a, 0x7a),
            "tellurium"   => Color.FromRgb(0x9a, 0x8a, 0x9a),
            "yttrium"     => Color.FromRgb(0x8a, 0xb0, 0xa0),
            _             => Color.FromRgb(0x8a, 0x52, 0x36), // neutral rust/ochre default
        };

        // Same "3 offset radial gradients" trick as the mockup — a cheap fake for an irregular,
        // non-circular surface patch instead of one perfectly round gradient, which read as
        // airbrushed against real in-game screenshots.
        private static void DrawSurfaceBlob(DrawingContext dc, double px, double py, double baseR, Color color, double alphaFrac, double seedBase)
        {
            byte a = (byte)Math.Clamp(alphaFrac * 255, 0, 255);
            for (int k = 0; k < 3; k++)
            {
                double offAng = Seeded(seedBase + k * 17) * Math.PI * 2;
                double offDist = baseR * 0.35 * Seeded(seedBase + k * 23);
                double rr = baseR * (0.6 + Seeded(seedBase + k * 29) * 0.6);
                double gx = px + Math.Cos(offAng) * offDist, gy = py + Math.Sin(offAng) * offDist;
                var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                g.GradientStops.Add(new GradientStop(Color.FromArgb(a, color.R, color.G, color.B), 0.0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
                dc.DrawEllipse(g, null, new Point(gx, gy), rr, rr);
            }
        }

        // count/sizeMul default to the original fixed values — every existing call site (the
        // System Scan thumb included) is unaffected unless it explicitly passes something else.
        private static void DrawGrain(DrawingContext dc, double cx, double cy, double R, double seedBase, int count = 1400, double sizeMul = 1.0)
        {
            for (int i = 0; i < count; i++)
            {
                double ang = Seeded(seedBase + i * 2.7 + 71) * Math.PI * 2;
                double dist = Math.Sqrt(Seeded(seedBase + i * 4.3 + 71)) * R; // sqrt so specks distribute evenly by area
                double x = cx + Math.Cos(ang) * dist, y = cy + Math.Sin(ang) * dist;
                bool light = Seeded(seedBase + i * 6.1 + 71) > 0.5;
                var color = light ? Color.FromArgb(13, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);
                double s = (0.6 + Seeded(seedBase + i * 8.3 + 71) * 1.1) * sizeMul;
                dc.DrawEllipse(new SolidColorBrush(color), null, new Point(x, y), s, s);
            }
        }

        private static void DrawCrater(DrawingContext dc, double x, double y, double r)
        {
            var bowl = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            bowl.GradientStops.Add(new GradientStop(Color.FromArgb(128, 15, 8, 6), 0.0));
            bowl.GradientStops.Add(new GradientStop(Color.FromArgb(46, 15, 8, 6), 0.7));
            bowl.GradientStops.Add(new GradientStop(Color.FromArgb(0, 15, 8, 6), 1.0));
            dc.DrawEllipse(bowl, null, new Point(x, y), r, r);
        }

        // Sparse, seeded-per-body scatter (2 large basins + 8 small) instead of the mockup's
        // fixed hand-placed list — every body gets its own distinct but deterministic crater
        // layout, the same "seeded per body" convention as the gas-giant cloud phase.
        //
        // Two real bugs fixed here: (1) `Seeded() * maxDist` is a naive polar sample — a
        // uniform radius draw puts disproportionately MORE points per unit area near the
        // center than near the edge (the same reason DrawGrain already takes a sqrt of its own
        // radius draw; craters never got that same correction). (2) maxDist itself topped out
        // at 0.4R/0.65R, so craters could never reach the visible edge at all, let alone past
        // it. Both together made every body's craters look centrally clumped no matter how the
        // angle/seed varied.
        //
        // Per-body "personality" on top of that fix: some worlds keep craters loosely
        // clustered around one or two impact-heavy regions, others scatter them uniformly
        // across the whole disc (edge and slightly past it — naturally clipped by the sphere's
        // own silhouette, same foreshortening a real limb crater would show) — varied by a
        // per-body seed roll instead of every body getting the same distribution shape.
        private static List<(double x, double y, double r)> BuildCraters(double seedBase)
        {
            double clusterRoll = Seeded(seedBase + 500);
            int clusterCount = clusterRoll > 0.72 ? 2 : clusterRoll > 0.4 ? 1 : 0; // 0 = fully scattered, no clustering
            var clusters = new List<(double x, double y)>();
            for (int i = 0; i < clusterCount; i++)
            {
                double cAng = Seeded(seedBase + 600 + i * 41.3) * Math.PI * 2;
                double cDist = Math.Sqrt(Seeded(seedBase + 600 + i * 23.7)) * 0.7; // uniform-by-area, kept mostly on-disc
                clusters.Add((Math.Cos(cAng) * cDist, Math.Sin(cAng) * cDist));
            }

            (double x, double y) PlaceOne(double sb, double maxDist)
            {
                if (clusters.Count > 0 && Seeded(sb + 3) < 0.75) // most (not all) craters follow the cluster pull
                {
                    var c = clusters[(int)(Seeded(sb + 7) * clusters.Count) % clusters.Count];
                    double ang = Seeded(sb + 11) * Math.PI * 2;
                    // sum of two uniforms for a softer, more natural falloff than a flat disc
                    double spread = (Seeded(sb + 13) + Seeded(sb + 17)) * 0.5 * 0.35;
                    return (c.x + Math.Cos(ang) * spread, c.y + Math.Sin(ang) * spread);
                }
                double freeAng = Seeded(sb + 19) * Math.PI * 2;
                double freeDist = Math.Sqrt(Seeded(sb + 23)) * maxDist; // uniform-by-area across the WHOLE disc
                return (Math.Cos(freeAng) * freeDist, Math.Sin(freeAng) * freeDist);
            }

            var list = new List<(double x, double y, double r)>();
            for (int i = 0; i < 2; i++)
            {
                var (x, y) = PlaceOne(seedBase + i * 11.3, 1.05); // large basins can reach just past the limb
                double r = 0.22 + Seeded(seedBase + i * 3.7) * 0.10;
                list.Add((x, y, r));
            }
            for (int i = 0; i < 8; i++)
            {
                var (x, y) = PlaceOne(seedBase + 100 + i * 9.7, 1.15); // small craters range further past the edge
                double r = 0.05 + Seeded(seedBase + 100 + i * 2.9) * 0.08;
                list.Add((x, y, r));
            }
            return list;
        }

        // Ports the mockup's branching crack-walk almost verbatim — a long, mostly-consistent
        // sweep with a slow curve (not a jagged zigzag), occasionally forking a shorter child
        // branch. Points are relative to the sphere's own center; the caller offsets by cx/cy.
        // trunkCount defaults to 2 (HMC's own "minor volcanism" case) — Rocky/Rocky Ice's
        // "major" volcanism case uses 3, per the locked mockup's own more-fissures rule.
        private static List<(List<Point> pts, int depth)> BuildFissures(double R, double seedBase, int trunkCount = 2)
        {
            var result = new List<(List<Point> pts, int depth)>();

            void BuildCrack(double x0, double y0, double angle0, double len, double sb, int depth)
            {
                var pts = new List<Point> { new Point(x0, y0) };
                double x = x0, y = y0, angle = angle0;
                int steps = 16 + (int)(Seeded(sb) * 8);
                double stepLen = len / steps;
                for (int s = 0; s < steps; s++)
                {
                    angle += (Seeded(sb + s * 1.7) - 0.5) * 0.3;
                    x += Math.Cos(angle) * stepLen;
                    y += Math.Sin(angle) * stepLen;
                    pts.Add(new Point(x, y));
                    if (depth < 1 && Seeded(sb + s * 3.1 + 9) < 0.18)
                    {
                        double childAngle = angle + (Seeded(sb + s * 5) - 0.5) * 1.8;
                        BuildCrack(x, y, childAngle, len * 0.4, sb + s * 13 + 50, depth + 1);
                    }
                }
                result.Add((pts, depth));
            }

            for (int i = 0; i < trunkCount; i++)
            {
                double a0 = Seeded(seedBase + i * 31.7) * Math.PI * 2;
                double startR = R * 0.75; // start near the limb so the walk has room to cross the disc
                double x0 = Math.Cos(a0) * startR, y0 = Math.Sin(a0) * startR;
                double towardCenter = Math.Atan2(-y0, -x0) + (Seeded(seedBase + i * 9) - 0.5) * 0.6;
                BuildCrack(x0, y0, towardCenter, R * (1.5 + Seeded(seedBase + i * 7) * 0.5), seedBase + i * 97 + 1, 0);
            }
            return result;
        }

        // Real per-volcanism-type fissure tint for whatever's left after the caller has
        // already routed "metallic" to lava pools and "magma"/"rocky" to the flow-network
        // terrain stain (both confirmed against real screenshots to look nothing like a
        // glowing fissure line) — water-ice volcanism reads cool pale cyan, silicate vapor
        // reads pale lavender-grey, matching the locked Rocky/Rocky-Ice mockup's own colors.
        private static Color GetFissureColor(string volcanism)
        {
            var v = volcanism.ToLowerInvariant();
            if (v.Contains("water")) return Color.FromRgb(0x7f, 0xd9, 0xd9);
            if (v.Contains("silicate")) return Color.FromRgb(0xc9, 0xa8, 0xd9);
            if (v.Contains("nitrogen") || v.Contains("ammonia") || v.Contains("methane")) return Color.FromRgb(0xa8, 0xc8, 0xe8);
            return Color.FromRgb(0xff, 0x7a, 0x3d); // default — HMC's original warm-amber glow
        }

        private static void DrawFissure(DrawingContext dc, double cx, double cy, List<Point> pts, int depth, Color glow)
        {
            if (pts.Count < 2) return;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(cx + pts[0].X, cy + pts[0].Y), false, false);
                gc.PolyLineTo(pts.Skip(1).Select(p => new Point(cx + p.X, cy + p.Y)).ToList(), true, false);
            }
            // Single clean core line, no fake multi-pass glow — checked against a real
            // in-game screenshot, which showed the old "3 progressively wider, fainter passes"
            // approximation as a stack of hard-edged concentric bands ("sharp pencil lines"),
            // not an actual soft glow. RenderTerrainScene now wraps the whole fissure layer in
            // a real Gaussian BlurEffect instead (the same technique the signal badges already
            // use), which is what actually produces a soft glow; brighter core opacity here
            // since that real blur will spread/dim it, unlike the old fake approach.
            double coreWidth = depth == 0 ? 2.6 : 1.8;
            double coreOpacity = depth == 0 ? 0.8 : 0.55;
            var corePen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(coreOpacity * 255), glow.R, glow.G, glow.B)), coreWidth)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, corePen, geo);
        }

        // Real Gaussian blur for a layer drawn via `draw` — WPF's DrawingContext/Pen has no
        // per-stroke blur of its own (the fake "several progressively wider, fainter passes"
        // approximation this replaced just stacked hard-edged concentric bands, which is
        // exactly what read as "sharp pencil lines" against a real screenshot). BlurEffect only
        // applies during a UIElement's own render pass, not a bare DrawingVisual, so this bakes
        // the sharp content to its own bitmap first, wraps it in an Image with the effect
        // applied, then re-renders THAT — the same technique the signal badges already use for
        // their glow rim, just applied to a whole layer instead of one shape.
        private static BitmapSource RenderBlurredLayer(int width, int height, Action<DrawingContext> draw, double blurRadius)
        {
            var sharpVisual = new DrawingVisual();
            using (var dc = sharpVisual.RenderOpen())
                draw(dc);
            var sharpBmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            sharpBmp.Render(sharpVisual);
            sharpBmp.Freeze();

            var img = new Image
            {
                Source = sharpBmp, Width = width, Height = height,
                Effect = new BlurEffect { Radius = blurRadius, KernelType = KernelType.Gaussian },
            };
            img.Measure(new Size(width, height));
            img.Arrange(new Rect(0, 0, width, height));

            var blurredBmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            blurredBmp.Render(img);
            blurredBmp.Freeze();
            return blurredBmp;
        }

        // Real Metal Rich bodies (checked against an actual in-game screenshot) don't show
        // "metallic magma" as long glowing fissure lines at all — they show small, isolated
        // glowing red-orange lava pools scattered across the surface, sitting in a dense fine
        // dark crack web rather than a couple of long branching cracks. Used only when the
        // real Volcanism text says "metallic" specifically; other magma/geyser types keep the
        // existing branching-fissure look until a real screenshot says otherwise for them too.
        private static List<(double x, double y, double r)> BuildLavaPools(double seedBase, int count)
        {
            var list = new List<(double x, double y, double r)>();
            for (int i = 0; i < count; i++)
            {
                double ang = Seeded(seedBase + i * 17.3 + 700) * Math.PI * 2;
                double dist = Math.Sqrt(Seeded(seedBase + i * 9.7 + 700)) * 0.85; // uniform-by-area
                double r = 0.035 + Seeded(seedBase + i * 5.3 + 700) * 0.05;
                list.Add((Math.Cos(ang) * dist, Math.Sin(ang) * dist, r));
            }
            return list;
        }

        private static void DrawLavaPool(DrawingContext dc, double x, double y, double r)
        {
            // Bright near-white-hot core cooling to deep red-orange, fading to transparent —
            // matches the real screenshot's look far better than a paler "metallic" tint would.
            var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0xdc, 0xb0), 0.0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(230, 0xd8, 0x2a, 0x12), 0.4));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xd8, 0x2a, 0x12), 1.0));
            dc.DrawEllipse(g, null, new Point(x, y), r, r);
        }

        // Real "rocky magma" volcanism (checked against an actual in-game screenshot) isn't a
        // couple of long independent glowing fissure lines OR isolated glowing pools either —
        // it's a dense, dendritic network of rust-orange flow-staining channels, all radiating
        // outward from one or two volcanic vents, covering much of the surface. The prominent
        // "starburst" a real screenshot showed at one vent is this same radiating-branch
        // pattern, not an impact crater (major volcanism still correctly suppresses those).
        // Unlike the fissure/pool layers, this reads as solidified colored TERRAIN, not
        // actively glowing lava — moderate opacity, blended in with only a light blur, no glow.
        private static List<(List<Point> pts, int depth)> BuildLavaFlowNetwork(double R, double seedBase, int ventCount, int branchesPerVent)
        {
            var result = new List<(List<Point> pts, int depth)>();

            void BuildBranch(double x0, double y0, double angle0, double len, double sb, int depth)
            {
                var pts = new List<Point> { new Point(x0, y0) };
                double x = x0, y = y0, angle = angle0;
                int steps = 10 + (int)(Seeded(sb) * 6);
                double stepLen = len / steps;
                for (int s = 0; s < steps; s++)
                {
                    angle += (Seeded(sb + s * 1.7) - 0.5) * 0.5;
                    x += Math.Cos(angle) * stepLen;
                    y += Math.Sin(angle) * stepLen;
                    pts.Add(new Point(x, y));
                    if (depth < 2 && Seeded(sb + s * 3.1 + 9) < 0.28)
                    {
                        double childAngle = angle + (Seeded(sb + s * 5) - 0.5) * 2.0;
                        BuildBranch(x, y, childAngle, len * 0.55, sb + s * 13 + 50, depth + 1);
                    }
                }
                result.Add((pts, depth));
            }

            for (int v = 0; v < ventCount; v++)
            {
                double ventAng = Seeded(seedBase + v * 53.1 + 300) * Math.PI * 2;
                double ventDist = Math.Sqrt(Seeded(seedBase + v * 29.3 + 300)) * 0.35 * R; // vents skew toward center, not the limb
                double vx = Math.Cos(ventAng) * ventDist, vy = Math.Sin(ventAng) * ventDist;
                for (int b = 0; b < branchesPerVent; b++)
                {
                    double dirAngle = Seeded(seedBase + v * 977 + b * 31.7 + 300) * Math.PI * 2;
                    double len = R * (0.55 + Seeded(seedBase + v * 977 + b * 7 + 300) * 0.75);
                    BuildBranch(vx, vy, dirAngle, len, seedBase + v * 977 + b * 131 + 301, 0);
                }
            }
            return result;
        }

        private static void DrawLavaFlowChannel(DrawingContext dc, double cx, double cy, List<Point> pts, int depth, Color color)
        {
            if (pts.Count < 2) return;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(cx + pts[0].X, cy + pts[0].Y), false, false);
                gc.PolyLineTo(pts.Skip(1).Select(p => new Point(cx + p.X, cy + p.Y)).ToList(), true, false);
            }
            double width = depth == 0 ? 5.0 : depth == 1 ? 3.0 : 1.8;
            double opacity = depth == 0 ? 0.5 : depth == 1 ? 0.36 : 0.24;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)), width)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        // Fine dark crack-vein texture — many short scattered segments approximating the real
        // dry-lakebed weathering visible across a whole Metal Rich surface, not just the
        // stippled grain every other body in this family already has.
        private static void DrawCrackWeb(DrawingContext dc, double cx, double cy, double R, double seedBase)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(90, 20, 14, 10)), 1.0);
            for (int i = 0; i < 220; i++)
            {
                double ang = Seeded(seedBase + i * 2.3 + 500) * Math.PI * 2;
                double dist = Math.Sqrt(Seeded(seedBase + i * 4.1 + 500)) * R * 0.96;
                double x = cx + Math.Cos(ang) * dist, y = cy + Math.Sin(ang) * dist;
                double segAngle = Seeded(seedBase + i * 6.7 + 500) * Math.PI * 2;
                double segLen = R * (0.05 + Seeded(seedBase + i * 8.3 + 500) * 0.09);
                dc.DrawLine(pen, new Point(x, y), new Point(x + Math.Cos(segAngle) * segLen, y + Math.Sin(segAngle) * segLen));
            }
        }

        // Real volcanism intensity, not just presence/absence — reconciles the two locked
        // mockups: HMC's own example had "minor" volcanism and showed BOTH craters AND a few
        // fissures (partial resurfacing); the Rocky/Rocky-Ice "major" examples show NO craters
        // at all, just more/brighter fissures (full resurfacing erases old impacts). None means
        // craters only, no fissures.
        private enum VolcanismLevel { None, Minor, Major }
        private static VolcanismLevel ClassifyVolcanism(string volcanism)
        {
            if (string.IsNullOrWhiteSpace(volcanism) || volcanism.Equals("No volcanism", StringComparison.OrdinalIgnoreCase))
                return VolcanismLevel.None;
            return volcanism.Contains("major", StringComparison.OrdinalIgnoreCase) ? VolcanismLevel.Major : VolcanismLevel.Minor;
        }

        // Base tone fallback for a non-landable body (no Materials list at all — real per the
        // Rocky/Rocky-Ice mockup's own note: atmosphere-bearing bodies of this class usually
        // land as non-landable). Blends representative rock/metal/ice tones by this body's own
        // real Composition split instead of guessing a single fixed color.
        private static Color GetCompositionBaseColor(BodyScanDetail detail)
        {
            double rock = detail.RockComposition, metal = detail.MetalComposition, ice = detail.IceComposition;
            double total = rock + metal + ice;
            if (total <= 0) return Color.FromRgb(0x8a, 0x52, 0x36); // no composition data at all — same neutral rust/ochre default
            var rockC = Color.FromRgb(0xa8, 0x94, 0x78);
            var metalC = Color.FromRgb(0x6d, 0x65, 0x60);
            var iceC = Color.FromRgb(0xcd, 0xc6, 0xbb);
            byte Blend(byte r, byte m, byte i) => (byte)Math.Clamp((r * rock + m * metal + i * ice) / total, 0, 255);
            return Color.FromRgb(Blend(rockC.R, metalC.R, iceC.R), Blend(rockC.G, metalC.G, iceC.G), Blend(rockC.B, metalC.B, iceC.B));
        }

        // Real materials are rarely dominated by just the single top one — checked against a
        // real Icy body's screenshot: its top material was sulphur (23.3%, a warm gold) alone,
        // which rendered nowhere near the actual dark reddish-brown look, but its next two
        // (carbon 19.6%, near-black; iron 16.1%, rust-brown) are nearly as prevalent and pull
        // the real composite color much darker/muddier than sulphur alone suggests. Weighted
        // blend of the top 3 by their own real %, not a winner-take-all pick.
        private static Color GetBlendedMaterialColor(List<(string Name, double Percent)> materials, int topN = 3)
        {
            var top = materials.Take(topN).ToList();
            double total = top.Sum(m => m.Percent);
            if (total <= 0) return GetMaterialTintColor(materials.Count > 0 ? materials[0].Name : "");
            double r = 0, g = 0, b = 0;
            foreach (var (name, pct) in top)
            {
                var c = GetMaterialTintColor(name);
                r += c.R * pct; g += c.G * pct; b += c.B * pct;
            }
            return Color.FromRgb((byte)Math.Clamp(r / total, 0, 255), (byte)Math.Clamp(g / total, 0, 255), (byte)Math.Clamp(b / total, 0, 255));
        }

        // Icy body base tone — every body was rendering the exact same fixed pale grey-white
        // regardless of its own real makeup, which is exactly the "they all seem white" gap:
        // real icy moons range from near-white through grey to blue depending on composition.
        // Blends the same real Ice/Rock/Metal split as GetCompositionBaseColor, but with an
        // icy-appropriate ramp (pale blue-white ice, neutral warm-grey rock, cool slate metal),
        // then applies a small seeded hue nudge — toward blue, toward neutral grey, or left
        // alone — so two bodies with near-identical composition percentages still don't render
        // as visually identical worlds.
        private static Color GetIcyBaseColor(BodyScanDetail detail, double seedBase)
        {
            double ice = detail.IceComposition, rock = detail.RockComposition, metal = detail.MetalComposition;
            double total = ice + rock + metal;
            var iceC = Color.FromRgb(0xd8, 0xe2, 0xe6);
            var rockC = Color.FromRgb(0xa8, 0x98, 0x80);
            var metalC = Color.FromRgb(0x78, 0x82, 0x8a);
            Color blended;
            if (total > 0)
            {
                byte Blend(byte i0, byte r0, byte m0) => (byte)Math.Clamp((i0 * ice + r0 * rock + m0 * metal) / total, 0, 255);
                blended = Color.FromRgb(Blend(iceC.R, rockC.R, metalC.R), Blend(iceC.G, rockC.G, metalC.G), Blend(iceC.B, rockC.B, metalC.B));
            }
            else
            {
                blended = iceC; // no composition data at all — pure ice tone
            }

            double hueRoll = Seeded(seedBase + 800);
            if (hueRoll < 0.33)
            {
                // Cool blue nudge.
                blended = Color.FromRgb((byte)(blended.R * 0.94), blended.G, (byte)Math.Min(255, blended.B * 1.08));
            }
            else if (hueRoll < 0.66)
            {
                // Neutral grey nudge — desaturate slightly toward mid grey.
                byte avg = (byte)((blended.R + blended.G + blended.B) / 3);
                blended = Color.FromRgb((byte)((blended.R + avg) / 2), (byte)((blended.G + avg) / 2), (byte)((blended.B + avg) / 2));
            }
            // else: leave as-is — the composition blend alone already covers this third case.
            return blended;
        }

        // Wind-constrained haze dabs — same technique as the water-world clouds this app's
        // mockup phase already locked: stretched/rotated soft ellipses along one shared wind
        // direction, so the layer reads as one atmosphere instead of random overlapping
        // streaks. Static (no drift animation) — this scene is a single bake, not cross-faded
        // like the gas-giant clouds.
        private static List<(double x, double y, double r, double stretch, double rot)> BuildHaze(double seedBase, double windAngle, int count)
        {
            var list = new List<(double, double, double, double, double)>();
            for (int i = 0; i < count; i++)
            {
                double x = (Seeded(seedBase + i * 7.1) - 0.5) * 1.7;
                double y = (Seeded(seedBase + i * 11.3) - 0.5) * 1.5;
                double r = 0.22 + Seeded(seedBase + i * 3.7) * 0.32;
                double stretch = 1.3 + Seeded(seedBase + i * 41) * 1.5;
                double rot = windAngle + (Seeded(seedBase + i * 47) - 0.5) * 0.5;
                list.Add((x, y, r, stretch, rot));
            }
            return list;
        }

        private static void DrawHazeDab(DrawingContext dc, double gx, double gy, double rr, double stretch, double rotRad, Color color, double alphaMul = 1.0)
        {
            dc.PushTransform(new RotateTransform(rotRad * 180 / Math.PI, gx, gy));
            var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(200 * alphaMul), color.R, color.G, color.B), 0.0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(64 * alphaMul), color.R, color.G, color.B), 0.6));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
            dc.DrawEllipse(g, null, new Point(gx, gy), rr * stretch, rr / stretch);
            dc.Pop();
        }

        // A storm cyclone — a bright core plus several progressively smaller, fainter dabs
        // trailing outward along a logarithmic-spiral arc, all built from the SAME dab technique
        // as the scattered cloud cover rather than one clean mathematical spiral curve. A
        // previous attempt at a storm feature used exactly that (a single smooth parametric
        // spiral) tied to Volcanism, and it read as too artificial — pulled after review. This
        // is a different mechanism entirely: real, driven by Surface Pressure (weather, not
        // geology), and built from an organic cluster of soft dabs so it blends into the
        // existing cloud cover instead of looking like a decorative sticker on top of it.
        private static List<(double x, double y, double r, double stretch, double rot, double alphaMul)> BuildStormSwirl(
            double cx0, double cy0, double baseR, double seedBase)
        {
            var list = new List<(double, double, double, double, double, double)>
            {
                (cx0, cy0, baseR * 0.24, 1.0, 0.0, 1.0), // bright core
            };
            int armPoints = 6 + (int)(Seeded(seedBase + 5) * 4);
            double angle0 = Seeded(seedBase + 1) * Math.PI * 2;
            double spiralTightness = 2.2 + Seeded(seedBase + 2) * 1.5;
            bool clockwise = Seeded(seedBase + 3) < 0.5;
            for (int i = 0; i < armPoints; i++)
            {
                double t = (i + 1) / (double)armPoints;
                double ang = angle0 + t * spiralTightness * Math.PI * (clockwise ? 1 : -1);
                double dist = baseR * (0.35 + t * 1.15);
                double x = cx0 + Math.Cos(ang) * dist;
                double y = cy0 + Math.Sin(ang) * dist;
                double r = Math.Max(baseR * 0.05, baseR * (0.17 - t * 0.09) + Seeded(seedBase + 10 + i * 3) * baseR * 0.04);
                double stretch = 1.3 + Seeded(seedBase + 20 + i * 3) * 0.9;
                double rot = ang + Math.PI / 2; // tangent to the spiral, so each dab elongates along the arm
                double alphaMul = Math.Max(0.3, 1.0 - t * 0.65);
                list.Add((x, y, r, stretch, rot, alphaMul));
            }
            return list;
        }

        // Icy body's "linea" fracture network — visually similar walk to HMC's fissures but a
        // structurally distinct feature: always drawn (not gated on Volcanism), no glow, more
        // numerous/finer trunks (7, not 2) and one deeper branch level (real icy moons like
        // Europa show a genuinely branching network, not a couple of long single cracks).
        private static List<(List<Point> pts, int depth)> BuildIcyCrackNetwork(double R, double seedBase)
        {
            var result = new List<(List<Point> pts, int depth)>();

            void BuildCrack(double x0, double y0, double angle0, double len, double sb, int depth)
            {
                var pts = new List<Point> { new Point(x0, y0) };
                double x = x0, y = y0, angle = angle0;
                int steps = 10 + (int)(Seeded(sb) * 8);
                double stepLen = len / steps;
                for (int s = 0; s < steps; s++)
                {
                    angle += (Seeded(sb + s * 1.7) - 0.5) * 0.55;
                    x += Math.Cos(angle) * stepLen;
                    y += Math.Sin(angle) * stepLen;
                    pts.Add(new Point(x, y));
                    if (depth < 2 && Seeded(sb + s * 3.1 + 9) < 0.22)
                    {
                        double childAngle = angle + (Seeded(sb + s * 5) - 0.5) * 1.8;
                        BuildCrack(x, y, childAngle, len * 0.45, sb + s * 13 + 50, depth + 1);
                    }
                }
                result.Add((pts, depth));
            }

            for (int i = 0; i < 7; i++)
            {
                double a0 = Seeded(seedBase + i * 31.7) * Math.PI * 2;
                double startR = Seeded(seedBase + i * 17.3) * R * 0.7;
                double x0 = Math.Cos(a0) * startR, y0 = Math.Sin(a0) * startR;
                double dirAngle = Seeded(seedBase + i * 41.1) * Math.PI * 2;
                double len = R * (0.8 + Seeded(seedBase + i * 7) * 0.6);
                BuildCrack(x0, y0, dirAngle, len, seedBase + i * 97 + 1, 0);
            }
            return result;
        }

        // Real icy-moon fracture networks don't all read the same color — Europa's lineae are
        // a warm tan/rust, Enceladus's "tiger stripes" are a cool blue, other moons show a
        // greenish frost tint — but every body was rendering the identical fixed tan before.
        // Picked once per body (seeded), not per crack, so a body's whole network reads as one
        // coherent feature rather than a mix of random colors.
        private static Color GetIcyCrackColor(double seedBase)
        {
            var palette = new[]
            {
                Color.FromRgb(0x96, 0x6e, 0x50), // warm tan/rust (Europa-style)
                Color.FromRgb(0x5a, 0x8f, 0xa8), // cool blue (Enceladus tiger-stripe style)
                Color.FromRgb(0x6a, 0x9a, 0x7a), // pale green (frost/tholin tint)
                Color.FromRgb(0x7a, 0x7a, 0xa0), // cool violet-grey
            };
            int idx = (int)(Seeded(seedBase + 900) * palette.Length) % palette.Length;
            return palette[idx];
        }

        private static void DrawIcyCrack(DrawingContext dc, double cx, double cy, List<Point> pts, int depth, Color color)
        {
            if (pts.Count < 2) return;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(cx + pts[0].X, cy + pts[0].Y), false, false);
                gc.PolyLineTo(pts.Skip(1).Select(p => new Point(cx + p.X, cy + p.Y)).ToList(), true, false);
            }
            double opacity = depth == 0 ? 0.4 : 0.22;
            double width = depth == 0 ? 1.4 : 0.8;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)), width)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        // Sphere center/radius for the terrain scene — a pure function of the box size (no
        // per-body ring rescaling like the gas-giant family needs). Exposed publicly so
        // MainWindow can anchor badges to the sphere's own actual edge instead of a fixed
        // offset from the scene box's corner, which only ever lined up by coincidence and fell
        // apart for a body type whose sphere doesn't fill nearly as much of its box as the
        // gas-giant scene's does.
        // detail is optional (existing callers that never deal with rings pass nothing, get the
        // old unshrunk behavior exactly as before) — pass it whenever the caller can, so a
        // ringed body's sphere shrinks consistently for EVERYONE who asks about its geometry,
        // not just internally inside RenderTerrainScene. This has to live HERE (not as a local
        // variable only inside RenderTerrainScene) because badge placement elsewhere
        // (MainWindow's Planet tab and Deorbit view) calls this same method separately to anchor
        // itself to the sphere's real edge — if only the internal render shrank, badges would
        // float outside the now-smaller visible sphere instead of sitting on it.
        //
        // Went through a couple of tuning passes here (real screenshots each time): first tried
        // a modest 0.85x shrink of this family's own 0.34 fraction with a family-specific ring
        // range, which wasn't nearly enough gap/reach compared to a real in-game ringed world;
        // then tried widening the render canvas itself for extra ring headroom. Both were solving
        // the wrong problem — direct feedback settled it: a ringed gas giant ALREADY looks right
        // at the exact same square canvas size this family already shares with it (MainWindow's
        // Planet tab renders both at the same 370x420), so canvas shape was never the issue.
        // Simplest fix, and the one that's actually correct: when a terrain body has rings, just
        // use the gas giant's own sphere fraction (0.23) and ring math outright instead of
        // maintaining a second, separately-tuned set of constants for this family.
        public static (double cx, double cy, double R) GetTerrainGeometry(int width, int height, BodyScanDetail? detail = null)
        {
            double cx = width / 2.0, cy = height / 2.0 + height * 0.02;
            bool hasRings = detail != null && detail.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
            // No rings competing for room here, so the sphere can take up much more of the box
            // than the gas-giant scene's own 0.23 — 0.34 matches the mockup's own R=170 on a
            // 640x500 (min 500) canvas. Ringed: literally the gas giant's own fraction, not a
            // family-specific shrink of this one — see the comment above.
            double R = Math.Min(width, height) * (hasRings ? 0.23 : 0.34);
            return (cx, cy, R);
        }

        private static RenderTargetBitmap RenderTerrainScene(BodyScanDetail detail, string iconCode, int width, int height, long systemPopulation = 0)
        {
            // detail passed through here so a ringed body's R already comes back at the gas
            // giant's own sphere fraction (see GetTerrainGeometry's own comment) — everything
            // below that treats R as "the sphere's radius" (the clip circle, every
            // blob/crater/frost/grain placement) automatically draws a proportionally smaller
            // sphere with no other changes needed.
            var (cx, cy, R) = GetTerrainGeometry(width, height, detail);
            double seedBase = (Math.Abs(detail.BodyName.GetHashCode()) % 10000) * 0.01;

            var materials = detail.Materials.Count > 0
                ? detail.Materials.OrderByDescending(m => m.Percent).Take(5).ToList()
                : new List<(string Name, double Percent)>();

            // Real gap this fixed: this whole family never rendered rings at all, even when a
            // body genuinely had them (real report: a ringed High Metal Content world showed no
            // ring whatsoever). Same back-pass/front-sliver technique, and now literally the
            // SAME min/max ring-pixel formula, as the gas giant renderer (ComputeGeometry) —
            // R already reflects the gas giant's own 0.23 fraction when this body has rings (see
            // GetTerrainGeometry), so reusing its exact 1.4x/0.46x constants here produces the
            // same proportions a ringed gas giant already gets, rather than a separately-tuned
            // (and, per two rounds of real feedback, worse-looking) set of terrain-only numbers.
            double ringMinPx = R * 1.4;
            double ringMaxPx = Math.Min(width, height) * 0.46;
            var terrainRings = ComputeRingBands(detail, ringMinPx, ringMaxPx);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (int i = 0; i < terrainRings.Count; i++)
                    DrawRingBandTextured(dc, cx, cy, terrainRings[i], RingTilt, RingSquash, seedBase, i, R);

                dc.PushClip(new EllipseGeometry(new Point(cx, cy), R, R));

                bool isIcy = iconCode == "ICY";
                bool isWaterWorld = iconCode == "WTR";
                bool isMetalRich = iconCode == "MRB";
                bool isEarthlike = iconCode == "ELW";
                bool isAmmonia = iconCode == "AMW";
                bool isHmc = iconCode == "HMC";

                // Base gradient. Icy bodies get a real, per-body varied tone now — blended from
                // this body's own Ice/Rock/Metal Composition (the mockup's fixed pale grey-white
                // was every icy body rendering identically, which is exactly what looked wrong:
                // real icy moons range from near-white through grey to blue depending on their
                // actual makeup) plus a small seeded hue nudge so even two bodies with similar
                // composition still read as individually distinct worlds. The real materials
                // still show up separately in the soft regional zones below, not here.
                // HMC keeps its own dominant-real-material tint (rust/ochre fallback with none).
                Color baseLight, baseMid, baseDark;
                if (isIcy)
                {
                    // Checked against a real 68.5%-ice body's screenshot: it rendered a DARK
                    // reddish-brown/grey base, not pale, because its real materials (sulphur
                    // 23.3%, carbon 19.6%, iron 16.1% — nearly evenly split, not one dominant
                    // winner) actually drive the visible color far more than a high ice
                    // fraction alone does. Landable icy bodies now prefer a real material blend
                    // first, same convention as every other body in this family — the
                    // composition-hue-nudge blend drops back to being the non-landable fallback
                    // only, not the default for every icy body regardless of data. Blending the
                    // top 3 (not just picking the single winner) matters here specifically:
                    // sulphur alone rendered a pale gold nowhere near the real dark look: its
                    // near-black carbon and rust-brown iron are nearly as prevalent and pull the
                    // real composite color much darker/muddier than sulphur alone suggests.
                    baseMid = materials.Count > 0 ? GetBlendedMaterialColor(materials) : GetIcyBaseColor(detail, seedBase);
                    baseLight = Color.FromRgb(
                        (byte)Math.Clamp(baseMid.R + (255 - baseMid.R) * 0.55, 0, 255),
                        (byte)Math.Clamp(baseMid.G + (255 - baseMid.G) * 0.55, 0, 255),
                        (byte)Math.Clamp(baseMid.B + (255 - baseMid.B) * 0.55, 0, 255));
                    // Gentler darken than the rust-toned bodies (0.45 vs 0.28) — an icy limb
                    // should stay pale grey-blue, not go near-black like a rocky terminator.
                    baseDark = Color.FromRgb(
                        (byte)(baseMid.R * 0.45), (byte)(baseMid.G * 0.45), (byte)(baseMid.B * 0.48));
                }
                else if (isWaterWorld)
                {
                    // Fixed ocean blue, matching the locked mockup — not composition/material
                    // tinted, since this is water, not rock. Cloud cover (below) is where this
                    // body's real data (Surface Pressure) actually drives the look.
                    baseLight = Color.FromRgb(0x6f, 0xb3, 0xd6);
                    baseMid   = Color.FromRgb(0x2f, 0x6f, 0x9e);
                    baseDark  = Color.FromRgb(0x0e, 0x2c, 0x48);
                }
                else if (isEarthlike)
                {
                    // Same real ocean blue as Water World — Earthlike bodies never carry a
                    // Materials list (never landable) and always show 0% real Ice composition,
                    // so there's no per-body data to blend a base tone from anyway; the
                    // continents (below) are where this body's own individuality shows up.
                    baseLight = Color.FromRgb(0x6f, 0xb3, 0xd6);
                    baseMid   = Color.FromRgb(0x2f, 0x6f, 0x9e);
                    baseDark  = Color.FromRgb(0x0e, 0x2c, 0x48);
                }
                else if (isAmmonia)
                {
                    // Locked mockup's own finding, confirmed against real scan data: Ammonia
                    // World is "the Earthlike concept, recolored" — same rock/metal-only
                    // composition (66.6%/33.4%, never landable, 0% ice, identical in kind to
                    // Earthlike's own numbers), same continents/cloud technique, just browns
                    // and dark reds ("dusty tan-brown deepening to dark red-brown") instead of
                    // blue ocean.
                    baseLight = Color.FromRgb(0xc9, 0x9a, 0x6e);
                    baseMid   = Color.FromRgb(0x8a, 0x52, 0x36);
                    baseDark  = Color.FromRgb(0x3a, 0x1e, 0x14);
                }
                else if (isMetalRich)
                {
                    // Fixed metallic-silver-with-warm-iron-undertone base, matching the locked
                    // mockup — NOT material-tinted like HMC's base. The real materials still
                    // show up in the surface blobs/patches below, same as every other body in
                    // this family; only the base sphere color itself stays fixed here.
                    baseLight = Color.FromRgb(0xe8, 0xdc, 0xcd);
                    baseMid   = Color.FromRgb(0x9a, 0x85, 0x70);
                    baseDark  = Color.FromRgb(0x24, 0x1d, 0x18);
                }
                else
                {
                    // Materials list when this body is landable (HMC's own convention); when
                    // it isn't — real for the Rocky/Rocky-Ice family, whose own mockup notes
                    // atmosphere-bearing bodies of this class usually land as non-landable — a
                    // blend of this body's real Ice/Rock/Metal Composition split instead.
                    baseMid = materials.Count > 0 ? GetMaterialTintColor(materials[0].Name) : GetCompositionBaseColor(detail);

                    // HMC-only: picks a target hue from a curated real-world palette instead of
                    // freely rotating the material tint's own hue. Real report + a reference
                    // strip of ~20 real HMC thumbnails: a blind ±75° rotation could start from a
                    // material tint that already sat in a green-ish band (e.g. yttrium ~160°,
                    // vanadium ~210°) and rotate into visible green/yellow-green — a hue that
                    // real HMC worlds never actually show (the reference strip is entirely red,
                    // rust, orange, tan, brown, grey, blue-grey, and muted mauve/purple). Each
                    // family below is a real color FROM that reference, picked by weighted seeded
                    // roll (red/rust/orange/brown most common, blue-grey and mauve rarer, matching
                    // how the reference strip actually skews warm) — not derived from the body's
                    // own material hue at all, so it can never land somewhere the reference never
                    // shows. Lightness still comes from the body's own material tint, so bodies
                    // keep some real per-material variation in how light/dark they read.
                    if (isHmc)
                    {
                        var (_, _, l) = RgbToHsl(baseMid);
                        var families = new (double hue, double satMin, double satMax, double weight)[]
                        {
                            (10,  0.35, 0.55, 3), // red/rust
                            (22,  0.40, 0.60, 3), // orange
                            (32,  0.30, 0.50, 3), // brown
                            (40,  0.20, 0.38, 2), // tan
                            (0,   0.00, 0.06, 2), // neutral grey (hue irrelevant at ~0 saturation)
                            (208, 0.12, 0.28, 1), // blue-grey
                            (288, 0.14, 0.30, 1), // muted mauve/purple
                        };
                        double totalWeight = families.Sum(f => f.weight);
                        double roll = Seeded(seedBase + 480) * totalWeight;
                        var pick = families[0];
                        foreach (var f in families) { if (roll < f.weight) { pick = f; break; } roll -= f.weight; }
                        double sat = pick.satMin + Seeded(seedBase + 481) * (pick.satMax - pick.satMin);
                        double lNudge = 0.85 + Seeded(seedBase + 482) * 0.3; // keep some per-body lightness spread
                        baseMid = HslToRgb(pick.hue, sat, Math.Clamp(l * lNudge, 0.15, 0.7));
                    }

                    baseLight = Color.FromRgb(
                        (byte)Math.Clamp(baseMid.R + (255 - baseMid.R) * 0.55, 0, 255),
                        (byte)Math.Clamp(baseMid.G + (255 - baseMid.G) * 0.55, 0, 255),
                        (byte)Math.Clamp(baseMid.B + (255 - baseMid.B) * 0.55, 0, 255));
                    baseDark = Color.FromRgb(
                        (byte)(baseMid.R * 0.28), (byte)(baseMid.G * 0.28), (byte)(baseMid.B * 0.28));
                }
                var baseBrush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.32), Center = new Point(0.5, 0.5),
                    RadiusX = 0.75, RadiusY = 0.75,
                };
                baseBrush.GradientStops.Add(new GradientStop(baseLight, 0.0));
                baseBrush.GradientStops.Add(new GradientStop(baseMid, 0.55));
                baseBrush.GradientStops.Add(new GradientStop(baseDark, 1.0));
                dc.DrawRectangle(baseBrush, null, new Rect(cx - R, cy - R, R * 2, R * 2));

                if (isIcy)
                {
                    // Real icy moons (Europa/Enceladus) aren't scattered crater blotches — a
                    // few large, very soft regional tint zones from the body's own real
                    // dominant materials, plus a branching fracture ("linea") network as the
                    // actual signature feature. No grain texture either — the mockup's surface
                    // reads smooth, not the HMC regolith's grainy look.
                    var zoneDefs = new (double x, double y, double r)[] { (-0.35, -0.2, 0.85), (0.4, 0.3, 0.7), (0.1, -0.5, 0.55) };
                    for (int i = 0; i < Math.Min(materials.Count, zoneDefs.Length); i++)
                    {
                        var color = GetMaterialTintColor(materials[i].Name);
                        var (zx, zy, zr) = zoneDefs[i];
                        var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                        g.GradientStops.Add(new GradientStop(Color.FromArgb(51, color.R, color.G, color.B), 0.0)); // '33' hex ≈ 0.2 alpha
                        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
                        dc.DrawEllipse(g, null, new Point(cx + zx * R, cy + zy * R), zr * R, zr * R);
                    }

                    // Real frost/albedo patches from this body's own Ice composition — same
                    // convention as the rust family (checked against a real Icy body's
                    // screenshot showing one bright patch standing out against an otherwise
                    // dark reddish-brown base). Icy bodies can be ice-dominant by composition
                    // yet still read mostly dark if their real materials are dark ones, so this
                    // is what actually accounts for the one bright deposit rather than assuming
                    // a high ice fraction alone makes the whole sphere pale.
                    if (detail.IceComposition > 0.05)
                    {
                        var frostColor = Color.FromRgb(0xdc, 0xea, 0xf2); // cool blue-white ice tint (was a warm near-white with poor hue contrast against a warm base)
                        int frostPatches = 1 + (int)(Seeded(seedBase + 700) * 2); // 1-2
                        for (int i = 0; i < frostPatches; i++)
                        {
                            double ang = Seeded(seedBase + i * 13.1 + 701) * Math.PI * 2;
                            double dist = Seeded(seedBase + i * 7.7 + 701) * 0.4;
                            double zr = 0.4 + Seeded(seedBase + i * 5.3 + 701) * 0.35 + detail.IceComposition * 0.5;
                            double alphaFrac = Math.Clamp(0.35 + detail.IceComposition * 0.9, 0.35, 0.85);
                            DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, frostColor, alphaFrac, i * 211 + 701);
                        }
                    }

                    // Real bodies vary — craters and a crack network can each show up (or not)
                    // somewhat independently, not "icy bodies always get cracks and never
                    // craters." Volcanism level still weights the odds (matches the rust-family
                    // rule below) but doesn't force either one on or off. Geo Signals still
                    // gets its own real-Volcanism sub-line in the HUD regardless either way.
                    var icyVolLevel = ClassifyVolcanism(detail.Volcanism);
                    double icyCraterChance = icyVolLevel switch { VolcanismLevel.Major => 0.15, VolcanismLevel.Minor => 0.5, _ => 0.6 };
                    double icyCrackChance = icyVolLevel switch { VolcanismLevel.Major => 0.9, VolcanismLevel.Minor => 0.75, _ => 0.55 };
                    if (Seeded(seedBase + 950) < icyCraterChance)
                        foreach (var c in BuildCraters(seedBase))
                            DrawCrater(dc, cx + c.x * R, cy + c.y * R, c.r * R);
                    if (Seeded(seedBase + 960) < icyCrackChance)
                    {
                        var crackColor = GetIcyCrackColor(seedBase);
                        foreach (var (pts, depth) in BuildIcyCrackNetwork(R, seedBase))
                            DrawIcyCrack(dc, cx, cy, pts, depth, crackColor);
                    }
                }
                else if (isWaterWorld)
                {
                    // No terrain at all — it's an ocean. Cloud cover (drawn below, alongside the
                    // real-atmosphere haze layer shared with Rocky/Rocky-Ice) is the only visual
                    // differentiator the locked mockup uses; no invented wave texture on top of
                    // what wasn't in that mockup.
                }
                else if (isEarthlike)
                {
                    // Real Earthlike continents — several large irregular green/tan/brown
                    // landmasses over the ocean base, checked against real screenshots. Same
                    // "3 offset blobs" irregular-patch technique already used for material
                    // surface variation, just with a land palette and covering a real chunk of
                    // the disc rather than a few small accent patches.
                    var landColors = new[]
                    {
                        Color.FromRgb(0x5a, 0x7a, 0x3a), // green
                        Color.FromRgb(0x8a, 0x7a, 0x4a), // tan/khaki
                        Color.FromRgb(0x6a, 0x5a, 0x3a), // brown
                    };
                    int continentCount = 3 + (int)(Seeded(seedBase + 800) * 3); // 3-5
                    for (int i = 0; i < continentCount; i++)
                    {
                        var color = landColors[i % landColors.Length];
                        double ang = Seeded(seedBase + i * 17.3 + 801) * Math.PI * 2;
                        double dist = Seeded(seedBase + i * 9.1 + 801) * 0.7;
                        double zr = 0.22 + Seeded(seedBase + i * 5.7 + 801) * 0.28;
                        DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, color, 0.85, i * 137 + 801);
                    }

                    // Polar ice caps — real Earthlikes range from none to prominent ("some have
                    // ice caps"). Driven by real SurfaceTemperature, not Composition — every
                    // real Earthlike scan reports 0% Ice composition regardless of how cold the
                    // body actually is, unlike every other terrain class where Ice% is real and
                    // meaningful. Continuous, not a random roll: colder bodies just have bigger
                    // caps, the same way real planets work.
                    double capFrac = Math.Clamp((285 - detail.SurfaceTemperature) / 70.0, 0, 1);
                    if (capFrac > 0.05)
                    {
                        double capSize = 0.15 + capFrac * 0.25;
                        double capAlpha = 0.35 + capFrac * 0.45;
                        DrawSurfaceBlob(dc, cx, cy - R * 0.82, R * capSize, Colors.White, capAlpha, 811);
                        DrawSurfaceBlob(dc, cx, cy + R * 0.82, R * capSize, Colors.White, capAlpha, 812);
                    }

                    // City lights — only when this system's own real Population is actually > 0
                    // (an inhabited system). Scattered warm dots over the continents as a
                    // stylized "someone lives here" marker rather than a physically-accurate
                    // day/night lit-city map — this renderer has no day/night terminator at all
                    // for a non-tidally-locked body (real Earthlikes never report TidalLock).
                    if (systemPopulation > 0)
                    {
                        int lightClusters = 3 + (int)(Seeded(seedBase + 820) * 5);
                        var lightColor = Color.FromRgb(0xff, 0xd8, 0x8a);
                        for (int i = 0; i < lightClusters; i++)
                        {
                            double ang = Seeded(seedBase + i * 19.3 + 821) * Math.PI * 2;
                            double dist = Seeded(seedBase + i * 11.1 + 821) * 0.55;
                            double lx = cx + Math.Cos(ang) * dist * R, ly = cy + Math.Sin(ang) * dist * R;
                            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, lightColor.R, lightColor.G, lightColor.B)),
                                null, new Point(lx, ly), R * 0.012, R * 0.012);
                        }
                    }
                }
                else if (isAmmonia)
                {
                    // Locked mockup's own note: "exactly the Earthlike concept locked in last
                    // round — same soft multi-layer landmasses... just recolored" — dark
                    // red/maroon/umber instead of green/tan/brown, matching real screenshots.
                    // No ice caps (not in the mockup, no evidence for one in real screenshots)
                    // and no city lights (never an inhabited class).
                    var landColors = new[]
                    {
                        Color.FromRgb(0x7a, 0x3a, 0x2e), // dark red
                        Color.FromRgb(0x5a, 0x3a, 0x24), // umber
                        Color.FromRgb(0x6e, 0x2f, 0x28), // maroon
                        Color.FromRgb(0x4a, 0x2e, 0x1e), // dark brown
                    };
                    int continentCount = 3 + (int)(Seeded(seedBase + 800) * 3); // 3-5
                    for (int i = 0; i < continentCount; i++)
                    {
                        var color = landColors[i % landColors.Length];
                        double ang = Seeded(seedBase + i * 17.3 + 801) * Math.PI * 2;
                        double dist = Seeded(seedBase + i * 9.1 + 801) * 0.7;
                        double zr = 0.22 + Seeded(seedBase + i * 5.7 + 801) * 0.28;
                        DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, color, 0.85, i * 137 + 801);
                    }
                }
                else
                {
                    // Real material surface variation — irregular tinted patches per material,
                    // sized/weighted by its own real %, plus a handful of smaller secondary specks.
                    for (int i = 0; i < materials.Count; i++)
                    {
                        var (name, pct) = materials[i];
                        var color = GetMaterialTintColor(name);
                        double ang = Seeded(i * 13.7 + 2) * Math.PI * 2;
                        double dist = Seeded(i * 7.3 + 2) * 0.45;
                        double zr = 0.5 + Seeded(i * 5.1 + 2) * 0.5;
                        double alphaFrac = Math.Min(0.34, 0.12 + (pct / 45.0) * 0.3);
                        DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, color, alphaFrac, i * 97 + 2);
                    }
                    if (materials.Count > 0)
                        for (int i = 0; i < 7; i++)
                        {
                            var (name, _) = materials[i % materials.Count];
                            var color = GetMaterialTintColor(name);
                            double ang = Seeded(i * 23.1 + 31) * Math.PI * 2;
                            double dist = Seeded(i * 11.7 + 31) * 0.75;
                            double zr = 0.18 + Seeded(i * 3.3 + 31) * 0.22;
                            DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, color, 0.125, i * 151 + 31);
                        }

                    // HMC-only: dark basin/"sea" patches — the other half of the reference
                    // screenshot that motivated the hue-varied base tone above: large, solid-
                    // reading dark blue-green-black lowland regions against the warm base, the
                    // same Mars-with-maria look. Not on every body (~40% chance, seeded) — the
                    // reference is one specific look among several real HMC variations, not the
                    // only one. Higher opacity than the material patches above (those are meant
                    // to read as a tint; these are meant to read as a real distinct region).
                    if (isHmc && Seeded(seedBase + 490) < 0.4)
                    {
                        var seaColor = Color.FromRgb(0x0e, 0x2a, 0x2c);
                        int seaPatches = 1 + (int)(Seeded(seedBase + 491) * 3); // 1-3
                        for (int i = 0; i < seaPatches; i++)
                        {
                            double ang = Seeded(seedBase + i * 17.9 + 492) * Math.PI * 2;
                            double dist = Seeded(seedBase + i * 9.3 + 492) * 0.5;
                            double zr = 0.28 + Seeded(seedBase + i * 6.1 + 492) * 0.3;
                            DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, seaColor, 0.8, i * 233 + 492);
                        }
                    }

                    // Real frost/albedo patches from this body's own Ice composition — checked
                    // against a real Rocky Ice screenshot showing a large pale patch that its
                    // dominant material (sulphur, a warm tan-gold) couldn't explain at all. Ice
                    // isn't itself a Materials-list entry (that list is metals/rock elements),
                    // so this needed its own pass keyed off Composition instead — larger and
                    // more opaque the more real ice the body actually has.
                    if (detail.IceComposition > 0.05)
                    {
                        // Opacity was topping out at ~0.2 for a modest-ice body like the real
                        // 12.4% example that motivated this — nowhere near visible enough
                        // against a vivid orange/tan base to read as an actual patch. Pushed the
                        // whole curve up so even modest real ice is clearly visible, not just
                        // ice-dominant bodies.
                        var frostColor = Color.FromRgb(0xdc, 0xea, 0xf2); // cool blue-white ice tint (was a warm near-white with poor hue contrast against a warm base)
                        int frostPatches = 1 + (int)(Seeded(seedBase + 700) * 2); // 1-2
                        for (int i = 0; i < frostPatches; i++)
                        {
                            double ang = Seeded(seedBase + i * 13.1 + 701) * Math.PI * 2;
                            double dist = Seeded(seedBase + i * 7.7 + 701) * 0.4;
                            double zr = 0.4 + Seeded(seedBase + i * 5.3 + 701) * 0.35 + detail.IceComposition * 0.5;
                            double alphaFrac = Math.Clamp(0.35 + detail.IceComposition * 0.9, 0.35, 0.85);
                            DrawSurfaceBlob(dc, cx + Math.Cos(ang) * dist * R, cy + Math.Sin(ang) * dist * R, R * zr, frostColor, alphaFrac, i * 211 + 701);
                        }
                    }

                    // Denser/larger grain on the big views — the Planet tab (370x420) and the
                    // Deorbit landing view (~1000px) — vs the System Scan thumb (~60-90px,
                    // depending on card/moon), which is exactly what made the same fixed-
                    // count/fixed-size grain look sparse/smooth at one size and dense/textured at
                    // the other (see DrawGrain — speck size/count were never scaled to R at all).
                    // Started as a one-class experiment on Rocky Ice, confirmed good ("I love
                    // it"), then Icy body too ("looks good"), then widened to every terrain class
                    // and to the Deorbit view per direct follow-up request.
                    //
                    // Gates on R (the actual rendered sphere radius) rather than width/height
                    // directly — R is what actually determines whether this is "a big enough
                    // view to benefit", and stays correct regardless of a ringed body's own
                    // smaller sphere fraction (0.23 vs 0.34 — see GetTerrainGeometry). 68 is the
                    // same cutoff an unringed "width >= 200" square canvas works out to
                    // (R = 0.34 × 200); a ringed thumbnail's own 140x140 box (R = 0.23 × 140 ≈
                    // 32) correctly stays under it, same as before.
                    if (R >= 68)
                        DrawGrain(dc, cx, cy, R, seedBase, count: 2600, sizeMul: 1.6);
                    else
                        DrawGrain(dc, cx, cy, R, seedBase);

                    // Specular sheen — Metal Rich only. A soft bright diagonal band selling
                    // "polished/molten metal" rather than matte rock; nothing else in this
                    // family has one.
                    if (isMetalRich)
                    {
                        var sheen = new LinearGradientBrush
                        {
                            StartPoint = new Point(cx - R * 0.6, cy - R * 0.9),
                            EndPoint = new Point(cx + R * 0.3, cy - R * 0.1),
                            MappingMode = BrushMappingMode.Absolute,
                        };
                        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 250, 240), 0.0));
                        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(41, 255, 250, 240), 0.5)); // .16*255≈41
                        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 250, 240), 1.0));
                        dc.DrawRectangle(sheen, null, new Rect(cx - R, cy - R, R * 2, R * 2));

                        // Dense fine crack web — checked against a real in-game screenshot,
                        // which showed this covering the whole surface, not just the stippled
                        // grain every other body in this family already has. Drawn sharp then
                        // lightly blurred as its own layer (same technique as the fissure glow
                        // below) — undrawn straight to `dc`, these read as sharp hard-edged
                        // scratches instead of weathered cracks; a real screenshot confirmed
                        // this was the actual "sharp pencil lines" complaint, not the (already
                        // properly blurred) glowing fissure itself.
                        var crackWebBlurred = RenderBlurredLayer(width, height,
                            ldc => DrawCrackWeb(ldc, cx, cy, R, seedBase), R * 0.018);
                        dc.DrawImage(crackWebBlurred, new Rect(0, 0, width, height));
                    }

                    // Real volcanism INTENSITY weights the odds, but real bodies show more
                    // variety than a rigid switch — craters and a crack network can each show
                    // up (or not) somewhat independently of each other and of volcanism, across
                    // every class in this family (confirmed: this isn't just an Icy-vs-rust
                    // distinction). Major volcanism makes craters less likely and cracks more
                    // likely; none makes the reverse true; neither is forced to zero or 100%.
                    var volLevel = ClassifyVolcanism(detail.Volcanism);
                    double craterChance = volLevel switch { VolcanismLevel.Major => 0.15, VolcanismLevel.Minor => 0.65, _ => 0.8 };
                    double crackChance = volLevel switch { VolcanismLevel.Major => 0.95, VolcanismLevel.Minor => 0.75, _ => 0.35 };
                    if (Seeded(seedBase + 950) < craterChance)
                        foreach (var c in BuildCraters(seedBase))
                            DrawCrater(dc, cx + c.x * R, cy + c.y * R, c.r * R);
                    if (Seeded(seedBase + 960) < crackChance)
                    {
                        if (volLevel == VolcanismLevel.None)
                        {
                            // A crack roll can succeed even with no real Volcanism at all — an
                            // old, dormant structural crack network rather than an actively
                            // molten one. Reuses the Icy family's own crack generator/palette
                            // (plain lines, no glow) instead of inventing a second one.
                            var crackColor = GetIcyCrackColor(seedBase);
                            var crackNetwork = BuildIcyCrackNetwork(R, seedBase);
                            var blurredCrack = RenderBlurredLayer(width, height, ldc =>
                            {
                                foreach (var (pts, depth) in crackNetwork)
                                    DrawIcyCrack(ldc, cx, cy, pts, depth, crackColor);
                            }, R * 0.015);
                            dc.DrawImage(blurredCrack, new Rect(0, 0, width, height));
                        }
                        // Real "metallic magma" (checked against an actual in-game screenshot)
                        // shows small isolated glowing red-orange lava pools, not long branching
                        // glowing fissure lines — other magma/geyser types keep the fissure-line
                        // look until a real screenshot says otherwise for them too. Both layers
                        // render sharp first, then get a real Gaussian blur applied to the whole
                        // layer at once — much cheaper than blurring every fissure/pool
                        // individually, and the softening reads as one coherent glow instead of
                        // per-shape halos.
                        else if (detail.Volcanism.Contains("metallic", StringComparison.OrdinalIgnoreCase))
                        {
                            int poolCount = volLevel == VolcanismLevel.Major ? 9 : 5;
                            var pools = BuildLavaPools(seedBase, poolCount);
                            var blurred = RenderBlurredLayer(width, height, ldc =>
                            {
                                foreach (var (px, py, pr) in pools)
                                    DrawLavaPool(ldc, cx + px * R, cy + py * R, pr * R);
                            }, R * 0.05);
                            dc.DrawImage(blurred, new Rect(0, 0, width, height));
                        }
                        else if (detail.Volcanism.Contains("magma", StringComparison.OrdinalIgnoreCase)
                            || detail.Volcanism.Contains("rocky", StringComparison.OrdinalIgnoreCase))
                        {
                            // Real "rocky magma" (checked against an actual in-game screenshot)
                            // reads as solidified rust-orange flow-staining, not a glow — so
                            // this gets its own dedicated color rather than GetFissureColor's
                            // (which was tuned for the OLD glowing-line look).
                            int ventCount = volLevel == VolcanismLevel.Major ? 2 : 1;
                            int branchesPerVent = volLevel == VolcanismLevel.Major ? 12 : 7;
                            var flowColor = Color.FromRgb(0xc9, 0x6a, 0x3a);
                            var branches = BuildLavaFlowNetwork(R, seedBase, ventCount, branchesPerVent);
                            var blurred = RenderBlurredLayer(width, height, ldc =>
                            {
                                foreach (var (pts, depth) in branches)
                                    DrawLavaFlowChannel(ldc, cx, cy, pts, depth, flowColor);
                            }, R * 0.02);
                            dc.DrawImage(blurred, new Rect(0, 0, width, height));
                        }
                        else
                        {
                            int trunkCount = volLevel == VolcanismLevel.Major ? 3 : 2;
                            var fissureColor = GetFissureColor(detail.Volcanism);
                            var fissures = BuildFissures(R, seedBase, trunkCount);
                            var blurred = RenderBlurredLayer(width, height, ldc =>
                            {
                                foreach (var (pts, depth) in fissures)
                                    DrawFissure(ldc, cx, cy, pts, depth, fissureColor);
                            }, R * 0.06);
                            dc.DrawImage(blurred, new Rect(0, 0, width, height));
                        }
                    }
                }

                // Two separate questions, deliberately not the same flag anymore:
                //  - hasAnyAtmosphere: does this body have a real atmosphere AT ALL (any named
                //    AtmosphereType, whatever its pressure)? Decides the edge glow below.
                //  - hasRealAtmosphere: is that atmosphere dense enough for actual visible haze/
                //    cloud puffs? Still pressure-gated, unchanged. A trace atmosphere (e.g. 1,834
                //    Pa argon) is real but negligible — no clouds — yet real ED screenshots still
                //    show a visible glow around its limb, which is exactly what got missed: this
                //    used to be ONE flag, so a thin (genuinely common — checked against a real
                //    player's own journals: nearly every atmosphere-bearing body they'd scanned
                //    sat at 100-2,000 Pa) atmosphere fell all the way through to the plain
                //    airless-shimmer rendering below, same as a body with no atmosphere at all.
                // Icy bodies keep their own simpler glow-only treatment for the haze specifically
                // (no mockup shows the elaborate haze on one) — gated off here, still applied
                // after the sphere clip pops below.
                bool hasAnyAtmosphere = !string.IsNullOrEmpty(detail.AtmosphereType)
                    && !detail.AtmosphereType.Equals("None", StringComparison.OrdinalIgnoreCase);
                const double hazeMinPressurePa = 5000;
                bool hasRealAtmosphere = hasAnyAtmosphere && detail.SurfacePressure >= hazeMinPressurePa;
                if (hasRealAtmosphere && !isIcy)
                {
                    double windAngle = Seeded(seedBase + 9) * Math.PI * 2;
                    if (isWaterWorld)
                    {
                        // Cloud coverage scales directly with real Surface Pressure — the
                        // locked mockup's own rule (real meteorology: a denser atmosphere holds
                        // more moisture aloft), calibrated against its 3 actual bodies (3 clouds
                        // at 14,236 Pa, 9 at 49,842 Pa, 22 at 304,331 Pa — a sqrt curve, not
                        // linear, fits those three points much better than a straight line).
                        // Plain white, not gas-tinted — real clouds over an ocean, not a colored
                        // haze obscuring rock.
                        // Real report + screenshot: a "hot thick water rich atmosphere" body
                        // (724 K, 18.8M Pa — the game's own descriptor literally says "hot") had
                        // heavy visible cloud cover in this render but shows essentially clear,
                        // smooth blue in-game. The 3-body pressure calibration this formula is
                        // based on (14,236/49,842/304,331 Pa) never distinguished hot from
                        // temperate water worlds, and pressure alone can't explain a real,
                        // visible difference — physically, a hot enough water world's surface
                        // water never cools enough to condense into visible cloud droplets in
                        // the first place, however much of it the atmosphere is carrying, so
                        // cloud cover needs its own temperature gate independent of pressure.
                        // Fades from the pressure-driven count (unchanged for an ordinary
                        // temperate water world) down to fully clear between 400 K and 700 K.
                        //
                        // Real report: even a non-hot water world was reading as "largely white
                        // with hints of color" — the ocean blue base was barely visible under the
                        // cloud layer. The old ceiling (26 dabs, each ~78% opaque at its core)
                        // compounds fast once dabs overlap, which they do constantly at a real
                        // planet's scale. Lowered the ceiling and softened each dab so the ocean
                        // reads through even under heavy pressure-driven coverage.
                        double heatFrac = Math.Clamp((detail.SurfaceTemperature - 400) / 300.0, 0, 1);
                        int cloudCount = (int)Math.Round(
                            Math.Clamp(Math.Round(0.03 * Math.Sqrt(detail.SurfacePressure) - 1.5), 2, 14) * (1 - heatFrac));
                        foreach (var (hx, hy, hr, stretch, rot) in BuildHaze(seedBase, windAngle, cloudCount))
                            DrawHazeDab(dc, cx + hx * R, cy + hy * R, hr * R, stretch, rot, Colors.White, 0.65);
                    }
                    else if (isAmmonia)
                    {
                        // Real report + screenshot: this render was showing the generic branch's
                        // plain WHITE haze — calibrated against real HMC screenshots, never
                        // against an Ammonia World — plus, at this class's typically extreme
                        // real pressure (this body: 34.3M Pa), the generic branch's pale white-
                        // grey extreme wash on top. Together those bleached the warm amber/brown
                        // continents toward washed-out pale grey. The real screenshot shows a
                        // consistently warm gold-brown haze over the whole disc instead — no
                        // separate pale wash layer, no distinct storm swirls, just thick warm
                        // cloud. Uses the same pressure-driven dab-count curve as the plain-white
                        // branch (an ammonia atmosphere is real, dense gas too, same physics),
                        // just tinted to match — this class's known-warm identity (see its own
                        // continent/base colors above) rather than the neutral white finding
                        // that held for every real HMC body checked.
                        // Per feedback, three rounds: flat tan (too pale/uniform) -> 4-color even
                        // cycle (wanted more orange/red) -> orange/red weighted 2x over a single
                        // brown/amber (wanted more brown back, without losing the orange/red).
                        // Brown now matches orange/red's own 2x weight instead of trailing them
                        // as a single accent, and uses a deeper, more assertive brown so it reads
                        // as a real presence in the mix rather than getting visually lost next to
                        // the more saturated orange/red. Amber stays the sole single-weight accent.
                        var ammoniaHazeColors = new[]
                        {
                            Color.FromRgb(0xe0, 0x5a, 0x1c), // vivid orange
                            Color.FromRgb(0xc2, 0x2e, 0x22), // vivid red
                            Color.FromRgb(0x6e, 0x44, 0x26), // deep brown
                            Color.FromRgb(0xe0, 0x5a, 0x1c), // vivid orange
                            Color.FromRgb(0xc2, 0x2e, 0x22), // vivid red
                            Color.FromRgb(0x6e, 0x44, 0x26), // deep brown
                            Color.FromRgb(0xc0, 0x7a, 0x3a), // deep amber
                        };
                        int ammoniaCloudCount = (int)Math.Clamp(4 + detail.SurfacePressure / 15000.0, 4, 20);
                        int ci = 0;
                        foreach (var (hx, hy, hr, stretch, rot) in BuildHaze(seedBase, windAngle, ammoniaCloudCount))
                            DrawHazeDab(dc, cx + hx * R, cy + hy * R, hr * R, stretch, rot, ammoniaHazeColors[ci++ % ammoniaHazeColors.Length]);

                        double ammoniaExtremeFrac = Math.Clamp((detail.SurfacePressure - 300000) / 2000000.0, 0, 1);
                        if (ammoniaExtremeFrac > 0)
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(ammoniaExtremeFrac * 130), 0xc7, 0x44, 0x1f)),
                                null, new Rect(cx - R, cy - R, R * 2, R * 2));
                    }
                    else
                    {
                        // Plain white, not gas-tinted — checked against 7 real HMC bodies
                        // across 4 different real gases (SO2, CO2, ammonia-rich, nitrogen):
                        // every one of them showed white cloud puffs. The rock BASE underneath
                        // (material/composition-tinted, unrelated to atmosphere) is what
                        // actually carries the body's color, matching real clouds — condensed
                        // droplets read as white/pale regardless of the surrounding gas, the
                        // same reason Water World's own clouds are plain white above.
                        //
                        // Real report: HMC bodies were reading as "largely white with hints of
                        // color" — this got worse once HMC's own base tone became more varied and
                        // colorful (see isHmc's hue-rotated base above), since the same cloud
                        // layer that was tolerable over a muddy brown base now buries a vivid
                        // red/orange/blue one. HMC gets a lighter version (lower ceiling, softer
                        // dabs) so its own new base color actually shows; Rocky/Rocky Ice keep the
                        // original numbers, calibrated against their own real screenshots.
                        int hazeCount = isHmc
                            ? (int)Math.Clamp(2 + detail.SurfacePressure / 30000.0, 2, 10)
                            : (int)Math.Clamp(4 + detail.SurfacePressure / 15000.0, 4, 20);
                        double hazeAlpha = isHmc ? 0.55 : 1.0;
                        foreach (var (hx, hy, hr, stretch, rot) in BuildHaze(seedBase, windAngle, hazeCount))
                            DrawHazeDab(dc, cx + hx * R, cy + hy * R, hr * R, stretch, rot, Colors.White, hazeAlpha);

                        // Storm cyclones — real screenshots showed actual rotating storm cells
                        // at moderate-high (not extreme) pressure, a genuinely different visual
                        // regime from either the light scattered puffs at low pressure or the
                        // fully-obscuring uniform wash at extreme pressure below. 1 cyclone above
                        // 200,000 Pa, 2 above 1,000,000 — real pressure-driven weather, not tied
                        // to Volcanism like the previous (rejected) storm attempt was.
                        if (detail.SurfacePressure > 200000)
                        {
                            int stormCount = detail.SurfacePressure > 1000000 ? 2 : 1;
                            for (int s = 0; s < stormCount; s++)
                            {
                                double stormAng = Seeded(seedBase + s * 61.3 + 400) * Math.PI * 2;
                                double stormDist = Math.Sqrt(Seeded(seedBase + s * 37.1 + 400)) * R * 0.55;
                                double sx = cx + Math.Cos(stormAng) * stormDist, sy = cy + Math.Sin(stormAng) * stormDist;
                                double stormR = R * (0.18 + Seeded(seedBase + s * 19 + 400) * 0.08);
                                foreach (var (x, y, r, stretch, rot, alphaMul) in BuildStormSwirl(sx, sy, stormR, seedBase + s * 777 + 400))
                                    DrawHazeDab(dc, x, y, r, stretch, rot, Colors.White, alphaMul * hazeAlpha);
                            }
                        }

                        // The dab count above maxes out at 20 well before real pressure does —
                        // checked against a real Rocky Ice body at 25,016,064 Pa (~2.7x Venus's
                        // own real surface pressure, two orders of magnitude past the mockup's
                        // own calibration, which topped out around 300,000 Pa). Its screenshot
                        // showed a smooth, uniformly hazy sphere, not patchier clouds than a
                        // merely-thick ~300K Pa body — real Venus looks the same way: a
                        // featureless, fully-obscuring haze, not visible cloud puffs. A uniform
                        // wash layered on top (fading in gradually past the mockup's own range,
                        // not a hard cutoff) covers that extreme case the patchy dabs alone
                        // can't represent. Pale neutral white-grey, not gas-tinted, matching the
                        // white-cloud finding above and a real 14.2M Pa HMC body's screenshot
                        // (pale blue-grey, not tinted toward its real CO2's usual tan).
                        // HMC gets a much weaker version of this wash — per the same feedback
                        // above, a real high-pressure HMC (the reference screenshot that prompted
                        // the hue-varied base tone) still reads as a clearly colored world, not a
                        // pale grey-white sphere. Rocky/Rocky Ice keep the full strength, still
                        // grounded in their own real 25M Pa screenshot.
                        double extremeFrac = Math.Clamp((detail.SurfacePressure - 300000) / 2000000.0, 0, 1);
                        if (extremeFrac > 0)
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(extremeFrac * (isHmc ? 45 : 150)), 235, 238, 240)),
                                null, new Rect(cx - R, cy - R, R * 2, R * 2));
                    }
                }

                // Tidal-lock terminator — only when the body's own real TidalLock is true.
                if (detail.TidalLock)
                {
                    var term = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                    term.GradientStops.Add(new GradientStop(Color.FromArgb(0, 4, 2, 1), 0.0));
                    term.GradientStops.Add(new GradientStop(Color.FromArgb(0, 4, 2, 1), 0.62));
                    term.GradientStops.Add(new GradientStop(Color.FromArgb(178, 4, 2, 1), 1.0));
                    dc.DrawRectangle(term, null, new Rect(cx - R, cy - R, R * 2, R * 2));
                }

                var limbBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                limbBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 10, 4, 2), 0.75));
                limbBrush.GradientStops.Add(new GradientStop(Color.FromArgb(102, 10, 4, 2), 1.0));
                dc.DrawRectangle(limbBrush, null, new Rect(cx - R, cy - R, R * 2, R * 2));

                dc.Pop(); // sphere clip

                // ---- ring front sliver — same bands again, clipped to (planet circle) ∩
                // (front half-plane), the classic ring-passes-in-front-of-the-near-limb
                // illusion — see the gas giant renderer's own front-sliver pass for the same
                // technique explained in more detail. Drawn before the atmosphere/limb glow
                // below so that glow sits over the ring too, same ordering as the gas giant one.
                if (terrainRings.Count > 0)
                {
                    var planetCircle = new EllipseGeometry(new Point(cx, cy), R, R);
                    var halfPlane = new RectangleGeometry(new Rect(cx - R * 4, cy, R * 8, R * 4));
                    halfPlane.Transform = new RotateTransform(RingTilt * 180 / Math.PI, cx, cy);
                    var sliverClip = new CombinedGeometry(GeometryCombineMode.Intersect, planetCircle, halfPlane);
                    dc.PushClip(sliverClip);
                    for (int i = 0; i < terrainRings.Count; i++)
                        DrawRingBandTextured(dc, cx, cy, terrainRings[i], RingTilt, RingSquash, seedBase, i, R);
                    dc.Pop();
                }

                // Terrain bodies aren't always airless — any real (even thin/trace) atmosphere
                // gets a soft pale edge glow around the whole limb, not just the ones dense
                // enough for visible haze/clouds (hasRealAtmosphere, still used above for those).
                // The HUD text already shows AtmosphereType via FormatAtmosphere, so the render
                // has to agree: the mockup's sharp airless shimmer is only for bodies that
                // genuinely have none — showing "Atmosphere: Argon" in the text next to a crisp
                // airless rim in the art would be a real, visible contradiction. Deliberately one
                // plain pale color for every gas (not GetAtmosphereHazeColor's per-gas tint) —
                // per feedback, this is meant to read as "this body has air", not communicate
                // which gas it is.
                if (hasAnyAtmosphere)
                {
                    // Scales with real SurfacePressure, log-mapped between a genuinely thin
                    // atmosphere (100 Pa — common; most of a real player's own scanned bodies
                    // sat in the low hundreds) and a dense one (2,000,000 Pa — the same reference
                    // point the extreme-haze wash above already uses), clamped at both ends so a
                    // trace atmosphere still reads as visibly present rather than vanishing, and
                    // an extreme one (real data: up to ~130,000,000 Pa) doesn't keep growing
                    // past a sensible cap. Both brightness AND size scale together — a thin
                    // atmosphere should look thin, not just dim at the same size.
                    double pT = Math.Clamp(
                        (Math.Log10(Math.Max(detail.SurfacePressure, 1)) - Math.Log10(100)) /
                        (Math.Log10(2_000_000) - Math.Log10(100)), 0, 1);
                    byte glowAlpha = (byte)(50 + pT * 100);       // 50 (trace) .. 150 (dense)
                    double glowRadiusMul = 1.08 + pT * 0.14;      // R*1.08 (trace) .. R*1.22 (dense)
                    double innerStop = 0.90 - pT * 0.10;          // 0.90 (thin sliver) .. 0.80 (thicker band)

                    var glowBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                    glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(glowAlpha, 0xcf, 0xe0, 0xe6), innerStop));
                    glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xcf, 0xe0, 0xe6), 1.0));
                    dc.DrawEllipse(glowBrush, null, new Point(cx, cy), R * glowRadiusMul, R * glowRadiusMul);
                }
                else
                {
                    // Airless limb shimmer — color driven by this body's own real
                    // SurfaceTemperature rather than one fixed style: hot bodies get the
                    // mockup's warm shimmer, cooler ones a neutral-to-cool rim instead.
                    Color limbColor = detail.SurfaceTemperature >= 500 ? Color.FromRgb(0xff, 0xbe, 0x8c)
                        : detail.SurfaceTemperature >= 250 ? Color.FromRgb(0xd8, 0xe8, 0xec)
                        : Color.FromRgb(0xb0, 0xd8, 0xf0);
                    var limbPen = new Pen(new SolidColorBrush(Color.FromArgb(102, limbColor.R, limbColor.G, limbColor.B)), 1.4);
                    dc.DrawEllipse(null, limbPen, new Point(cx, cy), R, R);
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
