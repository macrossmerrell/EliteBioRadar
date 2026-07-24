using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteBioRadar
{
    // ---------------------------------------------------------------
    //  Colony range lookup table  (from BioCSV.csv)
    // ---------------------------------------------------------------
    public static class ColonyRanges
    {
        private static readonly Dictionary<string, int> _ranges =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bacterium Aurasus",      500 }, { "Bacterium Cerbrus",     500 },
            { "Bacterium Alcyoneum",    500 }, { "Bacterium Bullaris",    500 },
            { "Bacterium Tela",         500 }, { "Bacterium Vesicula",    500 },
            { "Bacterium Informem",     500 }, { "Bacterium Verrata",     500 },
            { "Bacterium Acies",        500 }, { "Bacterium Scopulum",    500 },
            { "Bacterium Omentum",      500 },
            { "Frutexa Acus",           150 }, { "Frutexa Sponsae",       150 },
            { "Frutexa Metallicum",     150 },
            { "Tussock Ignis",          200 }, { "Tussock Ventusa",       200 },
            { "Tussock Virgam",         200 }, { "Tussock Caputus",       200 },
            { "Tussock Cultro",         200 }, { "Tussock Capillum",      200 },
            { "Tussock Pennata",        200 }, { "Tussock Serrati",       200 },
            { "Tussock Albata",         200 }, { "Tussock Triticum",      200 },
            { "Tubus Compagibus",       800 }, { "Tubus Sororibus",       800 },
            { "Stratum Excutitus",      500 }, { "Stratum Paleas",        500 },
            { "Stratum Tectonicas",     500 },
            { "Concha Labiata",         150 }, { "Concha Renibus",        150 },
            { "Cactoida Vermis",        300 }, { "Cactoida Cortexum",     300 },
            { "Clypeus Lacrimam",       150 }, { "Clypeus Margaritus",    150 },
            { "Osseus Discus",          800 }, { "Osseus Fractus",        800 },
            { "Osseus Pumice",          800 }, { "Osseus Pellebantus",    800 },
            { "Fungoida Stabitis",      300 }, { "Fungoida Setisis",      300 },
            { "Recepta Conditivus",     150 }, { "Recepta Deltahedronix", 150 },
            { "Recepta Umbrux",         150 },
            { "Aleoida Laminiae",       150 }, { "Aleoida Coronamus",     150 },
            { "Aleoida Arcus",          150 }, { "Aleoida Gravis",        150 },
            { "Fonticulua Digitos",     500 }, { "Fonticulua Campestris", 500 },
            { "Fonticulua Lapida",      500 },
        };

        private static readonly Dictionary<string, int> _genusFallback =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bacterium",  500 }, { "Frutexa",   150 }, { "Tussock",    200 },
            { "Tubus",      800 }, { "Stratum",   500 }, { "Concha",     150 },
            { "Cactoida",   300 }, { "Clypeus",   150 }, { "Osseus",     800 },
            { "Fungoida",   300 }, { "Recepta",   150 }, { "Aleoida",    150 },
            { "Fonticulua", 500 },
        };

        public static int GetRange(string genus, string species)
        {
            var key = $"{genus} {species}";
            if (_ranges.TryGetValue(key, out var r)) return r;
            if (!string.IsNullOrEmpty(genus) && _genusFallback.TryGetValue(genus, out var gr)) return gr;
            return 150;
        }
    }

    // ---------------------------------------------------------------
    //  A scanned organism location
    // ---------------------------------------------------------------
    public class ScannedOrganism
    {
        public double Latitude   { get; set; }
        public double Longitude  { get; set; }
        public string Genus      { get; set; } = "";
        public string Species    { get; set; } = "";
        public int    ScanCount  { get; set; } = 1;
        public bool   IsComplete { get; set; } = false;  // true = journal confirmed, grey on radar
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        public string DisplayName =>
            string.IsNullOrEmpty(Species) ? Genus : $"{Genus} {Species}";

        public int ColonyRange => ColonyRanges.GetRange(Genus, Species);

        // False when this is a synthetic completion record — Analyse fired but all
        // Log/Sample scans were skipped due to no position data (e.g. missing journal).
        // Used to suppress radar rendering while still allowing the Bio Survey to show
        // the genus as complete. Computed from lat/lon so no cache format changes needed.
        public bool HasPosition => Latitude != 0.0 || Longitude != 0.0;
    }

    // ---------------------------------------------------------------
    //  A discovered geological site (from CodexEntry)
    // ---------------------------------------------------------------
    public class ScannedGeoSite
    {
        public double   Latitude    { get; set; }
        public double   Longitude   { get; set; }
        public string   Name        { get; set; } = "";  // localised name e.g. "Water Ice Geyser"
        public int      EntryID     { get; set; }        // deduplicate by EntryID per body
        public long     Payout      { get; set; }        // VoucherAmount from CodexEntry
        public DateTime LastSeen    { get; set; } = DateTime.UtcNow;

        // Wiki URL lookup
        private static readonly Dictionary<string, string> _wikiSlugs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Fumarole",          "Fumarole" },
            { "Ice Fumarole",      "Ice_Fumarole" },
            { "Gas Vent",          "Gas_Vent" },
            { "Geyser",            "Geyser" },
            { "Ice Geyser",        "Ice_Geyser" },
            { "Water Geyser",      "Geyser" },
            { "Water Ice Geyser",  "Ice_Geyser" },
            { "Lava Spout",        "Lava_Spout" },
        };

        public string WikiUrl
        {
            get
            {
                // Try exact match first, then partial
                foreach (var kvp in _wikiSlugs)
                    if (Name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return $"https://elite-dangerous.fandom.com/wiki/{kvp.Value}";
                return $"https://elite-dangerous.fandom.com/wiki/{Uri.EscapeDataString(Name.Replace(" ", "_"))}";
            }
        }
    }


    // ---------------------------------------------------------------
    //  A remembered surface point — ship touchdown or a parked SRV
    // ---------------------------------------------------------------
    public class AnchorPoint
    {
        public double Latitude  { get; set; }
        public double Longitude { get; set; }
    }

    public class EliteStatus
    {
        public uint   Flags        { get; set; }
        public uint   Flags2       { get; set; }
        public double Latitude     { get; set; }
        public double Longitude    { get; set; }
        public double Altitude     { get; set; }
        public double Heading      { get; set; }
        public string BodyName     { get; set; } = "";
        public double PlanetRadius { get; set; }
        public double FuelMain      { get; set; }
        public double FuelReservoir { get; set; }

        // Elite Dangerous Status.json Flags bitmasks
        public bool Docked      => (Flags & (1u << 0))  != 0;
        public bool Landed      => (Flags & (1u << 1))  != 0;
        public bool Supercruise => (Flags & (1u << 4))  != 0;
        public bool InSRV       => (Flags & (1u << 26)) != 0;
        public bool InFighter   => (Flags & (1u << 25)) != 0;
        public bool HasLatLong  => (Flags & (1u << 19)) != 0;
        public bool OnFoot      => (Flags2 & (1u << 0)) != 0;
        // Set specifically for an actual hyperspace charge-up — distinct from Flags bit17
        // "FsdCharging", which is ambiguous (also set for a plain supercruise charge).
        public bool FsdHyperdriveCharging => (Flags2 & (1u << 19)) != 0;

        // True when we have any positional data (flag OR non-zero coords)
        public bool HasPosition => HasLatLong || Latitude != 0 || Longitude != 0;
    }

    // ---------------------------------------------------------------
    //  Info panel: STAR / PLANET / DESTINATION / RADAR mode
    // ---------------------------------------------------------------
    public enum InfoPanelMode { Radar, Star, Planet, Destination }

    // A ring, from a Scan event's Rings[] array (present on both stars and planets)
    public class RingInfo
    {
        public string Name      { get; set; } = "";
        public string RingClass { get; set; } = ""; // eRingClass_Icy/MetalRich/Metalic/Rocky (exact in-game spelling)
    }

    // Physical detail from a star or planet Scan event. One class covers both —
    // IsStar discriminates which half of the fields is populated.
    public class BodyScanDetail
    {
        public string BodyName { get; set; } = "";
        public bool   IsStar   { get; set; }
        public bool   IsBelt   { get; set; } // asteroid belt cluster (BodyName contains "Belt Cluster") — no StarType/PlanetClass
        public string ScanType { get; set; } = ""; // informational tag only ("AutoScan"/"Detailed"), not a completeness signal
        public bool   IsMapped { get; set; } // DSS-mapped this session (SAAScanComplete) — a later, distinct milestone than a Detailed scan

        // Star fields
        public string StarType          { get; set; } = "";
        public int    Subclass          { get; set; }
        public double StellarMass       { get; set; }
        public double Radius            { get; set; }
        public double AbsoluteMagnitude { get; set; }
        public double AgeMY             { get; set; }
        public string Luminosity        { get; set; } = ""; // short string enum e.g. "Va"
        public double RotationPeriod    { get; set; }

        // Planet fields
        public string PlanetClass      { get; set; } = "";
        public string AtmosphereType   { get; set; } = ""; // short enum ("None" when absent) — drives the atmosphere halo
        public string Volcanism        { get; set; } = "";
        public double SurfaceGravity   { get; set; }
        public double SurfacePressure  { get; set; }
        public bool   TidalLock        { get; set; }
        public string TerraformState   { get; set; } = ""; // "Terraformable" or ""
        public bool   Landable         { get; set; }
        public double IceComposition   { get; set; }
        public double RockComposition  { get; set; }
        public double MetalComposition { get; set; }

        // Shared
        public double SurfaceTemperature { get; set; }
        public List<RingInfo> Rings      { get; set; } = new();
        public int BioSignalCount { get; set; }
        public int GeoSignalCount { get; set; }

        // Shallow copy — used when a field (e.g. IsMapped) needs to change on an already-stored
        // detail without mutating the same object in place, since the UI debounces re-renders
        // by reference equality against the last-rendered detail.
        public BodyScanDetail Clone() => (BodyScanDetail)MemberwiseClone();
    }

    // One hop in the plotted route (from NavRoute.json), with computed distance
    public class RouteHop
    {
        public string   StarSystem         { get; set; } = "";
        public long     SystemAddress      { get; set; }
        public double[] StarPos            { get; set; } = new double[3];
        public string   StarClass          { get; set; } = "";
        public double   DistanceFromPrevLy { get; set; } // computed, not present in the file
    }

    // DESTINATION-mode snapshot: FSDTarget + Loadout (jump range/fuel capacity) + live Status.json fuel + NavRoute
    public class DestinationInfo
    {
        public string NextSystem            { get; set; } = "";
        public string StarClass             { get; set; } = "";
        public int    RemainingJumpsInRoute { get; set; }
        public double MaxJumpRange          { get; set; } // as reported by the last Loadout event — goes stale between Loadouts
        public double CurrentJumpRange      { get; set; } // recalculated from current fuel/mass each Status.json tick
        public double FuelMain              { get; set; }
        public double FuelReservoir         { get; set; }
        public double FuelCapacityMain      { get; set; }
        public double RemainingDistanceLy   { get; set; } // sum of hop distances
        public int    TotalRouteJumps       { get; set; } // total systems in the plotted route, incl. origin
        public double TotalRouteLy          { get; set; } // total route distance, fixed at first sighting
        public List<RouteHop> Hops          { get; set; } = new(); // remaining hops only (from NavRoute.json, current position onward)
        public List<RouteHop> FullRouteHops { get; set; } = new(); // entire route incl. already-passed hops — from RouteCache, for display

        // FSD stats used to recalculate CurrentJumpRange; parsed from Loadout's Modules array
        // (engineered "FSDOptimalMass"/"MaxFuelPerJump" modifiers override the stock table values).
        public double UnladenMass           { get; set; }
        public double FsdOptimalMass        { get; set; }
        public double FsdFuelMul            { get; set; }
        public double FsdFuelPower          { get; set; }
        public double FsdMaxFuelPerJump     { get; set; }
        public double GuardianBoosterBonusLy { get; set; }
    }

    // ---------------------------------------------------------------
    //  ScanOrganic journal event
    // ---------------------------------------------------------------
    public class JournalOrganic
    {
        public string Event               { get; set; } = "";
        public string Genus               { get; set; } = "";
        public string Species             { get; set; } = "";
        public string Genus_Localised     { get; set; } = "";
        public string Species_Localised   { get; set; } = "";
        public string ScanType            { get; set; } = "";  // "Log", "Sample", "Analyse"
        public double Latitude            { get; set; }
        public double Longitude           { get; set; }
        public string SystemBody          { get; set; } = "";
    }
}
