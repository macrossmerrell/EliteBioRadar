using System;
using System.Collections.Generic;

namespace EliteBioRadar
{
    // Estimates a planet's exploration scan value using the community-reverse-engineered
    // formula (Frontier has never published the real one) — base = k + (3*k*mass^0.199977/5.3),
    // k varying by PlanetClass, with a second additive term of the same shape (different k') for
    // a terraformable bonus, then a multiplier for DSS mapping / first-discovery / first-mapping.
    // Sourced from community reverse-engineering (Frontier Forums "Exploration value formulae"
    // thread; k constants cross-checked against multiple independent community writeups) — an
    // ESTIMATE, always shown with "≈", not a guarantee of the exact in-game payout.
    public static class ScanValueEstimator
    {
        private static readonly Dictionary<string, long> BaseK = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Metal rich body"]                  = 52292,
            ["Ammonia world"]                     = 232619,
            ["Sudarsky class I gas giant"]        = 3974,
            ["High metal content body"]           = 23168,
            ["Sudarsky class II gas giant"]       = 23168,
            ["Water world"]                       = 155581,
            ["Earthlike body"]                    = 155581,
        };
        // Everything else — Rocky/Icy/Rocky ice, Water-based/Ammonia-based life gas giants,
        // Water giant, Helium-rich gas giant, Sudarsky class III/IV/V gas giants.
        private const long DefaultK = 720;

        // Only these three classes can actually roll Terraformable in-game, so this table only
        // needs exactly these keys.
        private static readonly Dictionary<string, long> TerraformK = new(StringComparer.OrdinalIgnoreCase)
        {
            ["High metal content body"] = 241607,
            ["Water world"]             = 279088,
            ["Earthlike body"]          = 279088,
            ["Rocky body"]              = 223971,
        };

        private static double BaseValue(long k, double massEM)
        {
            double m = Math.Max(massEM, 0.0001); // formula is undefined at exactly 0
            return k + (3.0 * k * Math.Pow(m, 0.199977) / 5.3);
        }

        // Shared by Estimate and EstimateIfMapped — the base/terraform portion doesn't depend
        // on mapped status at all, only the multiplier applied afterward does.
        private static double BaseAndTerraformValue(BodyScanDetail d)
        {
            long k = BaseK.TryGetValue(d.PlanetClass, out var kk) ? kk : DefaultK;
            double value = BaseValue(k, d.MassEM);

            bool terraformable = string.Equals(d.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase);
            if (terraformable)
            {
                long tk = TerraformK.TryGetValue(d.PlanetClass, out var tkk) ? tkk : TerraformK["Rocky body"];
                value += BaseValue(tk, d.MassEM);
            }
            return value;
        }

        // Returns the estimated credit value and a short note on which multiplier tier applied
        // (shown as a sub-caption so the "≈" number isn't presented as more certain than it is).
        public static (double credits, string note) Estimate(BodyScanDetail d)
        {
            if (d.IsStar || d.IsBelt || string.IsNullOrEmpty(d.PlanetClass)) return (0, "");

            double value = BaseAndTerraformValue(d);

            string note;
            if (d.IsMapped)
            {
                if (d.WasDiscovered == false && d.WasMapped == false) { value *= 9.6; note = "first discovered + first mapped"; }
                else if (d.WasMapped == false)                        { value *= 8.1; note = "first mapped"; }
                else                                                  { value *= 3.3333; note = "mapped"; }
            }
            else
            {
                note = "scan only — mapping would multiply this ~3.3x";
            }
            return (value, note);
        }

        // Same value, but always applies whichever mapping multiplier WOULD apply (based on
        // WasDiscovered/WasMapped, which are already known from the Scan event regardless of
        // whether DSS mapping has actually happened yet) — used to flag a body as a worthwhile
        // mapping target even before it's been mapped, not just after.
        public static double EstimateIfMapped(BodyScanDetail d)
        {
            if (d.IsStar || d.IsBelt || string.IsNullOrEmpty(d.PlanetClass)) return 0;

            double value = BaseAndTerraformValue(d);
            if (d.WasDiscovered == false && d.WasMapped == false) value *= 9.6;
            else if (d.WasMapped == false)                        value *= 8.1;
            else                                                  value *= 3.3333;
            return value;
        }

        // Simplified conservative habitable-zone bounds (Stefan-Boltzmann luminosity from the
        // star's own real Radius/SurfaceTemperature, then the standard L/1.1 .. L/0.53 AU band)
        // — good enough to flag "in/near the HZ" on the System Scan window, not a precision
        // astrophysics tool. Returns (innerAU, outerAU), or null if the star's own data can't
        // support the calculation (e.g. a belt/non-star entry, or radius/temp not yet scanned).
        public static (double innerAU, double outerAU)? HabitableZoneAU(BodyScanDetail star)
        {
            if (!star.IsStar || star.Radius <= 0 || star.SurfaceTemperature <= 0) return null;
            const double sunRadiusM = 695_700_000.0;
            const double sunTempK   = 5778.0;
            double rRatio = star.Radius / sunRadiusM;
            double tRatio = star.SurfaceTemperature / sunTempK;
            double luminositySuns = rRatio * rRatio * Math.Pow(tRatio, 4);
            if (luminositySuns <= 0) return null;
            return (Math.Sqrt(luminositySuns / 1.1), Math.Sqrt(luminositySuns / 0.53));
        }
    }
}
