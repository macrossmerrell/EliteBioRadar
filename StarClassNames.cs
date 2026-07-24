using System;
using System.Collections.Generic;

namespace EliteBioRadar
{
    // ---------------------------------------------------------------
    //  Raw Scan.StarType codes -> friendly display names for the Stellar tab.
    //  Same lookup-table pattern as ColonyRanges in Models.cs.
    // ---------------------------------------------------------------
    public static class StarClassNames
    {
        private static readonly Dictionary<string, string> _exact =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // Main sequence — common names (matches the Elite Dangerous wiki's colour
            // descriptions and the community's usual shorthand, e.g. "M" = Red Dwarf).
            { "O", "Blue Star" },
            { "B", "Blue-White Star" },
            { "A", "Blue-White Star" },
            { "F", "White Star" },
            { "G", "Yellow-White Star" },
            { "K", "Orange Star" },
            { "M", "Red Dwarf" },
            { "N",  "Neutron Star" },
            { "H",  "Black Hole" },
            { "SupermassiveBlackHole", "Supermassive Black Hole" },
            { "TTS",  "T Tauri Star" },
            { "AeBe", "Herbig Ae/Be Star" },
            { "W",  "Wolf-Rayet Star" },
            { "WN",  "Wolf-Rayet Star (WN)" },
            { "WNC", "Wolf-Rayet Star (WNC)" },
            { "WC",  "Wolf-Rayet Star (WC)" },
            { "WO",  "Wolf-Rayet Star (WO)" },
            { "C",   "Carbon Star" },
            { "CN",  "Carbon Star (CN)" },
            { "CJ",  "Carbon Star (CJ)" },
            { "CH",  "Carbon Star (CH)" },
            { "CHd", "Carbon Star (CHd)" },
            { "MS",  "MS-Type Star" },
            { "S",   "S-Type Star" },
            { "X",   "Exotic Star" },
        };

        private static readonly HashSet<string> _exotic = new(StringComparer.OrdinalIgnoreCase)
        {
            "N", "H", "SupermassiveBlackHole",
        };

        // Any StarType starting with "D" is a white dwarf family — DA, DAB, DAO, DAZ,
        // DAV, DB, DBZ, DBV, DO, DOV, DQ, DC, DCV, DX, etc.
        private static bool IsWhiteDwarf(string starType) =>
            starType.StartsWith("D", StringComparison.OrdinalIgnoreCase);

        public static string GetDisplayName(string starType)
        {
            if (string.IsNullOrEmpty(starType)) return "Unknown";

            if (IsWhiteDwarf(starType)) return $"White Dwarf ({starType.ToUpperInvariant()})";
            if (_exact.TryGetValue(starType, out var name)) return name;

            // Giant/supergiant suffixes: "K_OrangeGiant" -> "K-Type (Orange Giant)"
            var giantSuffixes = new (string suffix, string label)[]
            {
                ("_RedGiant",              "Red Giant"),
                ("_RedSuperGiant",         "Red Supergiant"),
                ("_OrangeGiant",           "Orange Giant"),
                ("_BlueWhiteSuperGiant",   "Blue-White Supergiant"),
                ("_WhiteSuperGiant",       "White Supergiant"),
                ("_YellowSuperGiant",      "Yellow Supergiant"),
                ("_YellowGiant",           "Yellow Giant"),
            };
            foreach (var (suffix, label) in giantSuffixes)
            {
                if (starType.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var baseType = starType.Substring(0, starType.Length - suffix.Length);
                    return $"{baseType}-Type ({label})";
                }
            }

            // Plain main-sequence / brown-dwarf classes: O, B, A, F, G, K, L, T, Y, W
            return $"{starType}-Type";
        }

        // Matches the mockup's "exotic remnants" definition — Neutron Star and Black
        // Hole only. White dwarfs are unusual but not treated as exotic here.
        public static bool IsExotic(string starType) =>
            !string.IsNullOrEmpty(starType) && _exotic.Contains(starType);
    }
}
