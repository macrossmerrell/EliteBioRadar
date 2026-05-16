using System;
using System.Collections.Generic;

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
    }

    // ---------------------------------------------------------------
    //  Elite Status.json model
    // ---------------------------------------------------------------
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

        // Elite Dangerous Status.json Flags bitmasks
        public bool Docked      => (Flags & (1u << 0))  != 0;
        public bool Landed      => (Flags & (1u << 1))  != 0;
        public bool Supercruise => (Flags & (1u << 4))  != 0;
        public bool InSRV       => (Flags & (1u << 26)) != 0;
        public bool HasLatLong  => (Flags & (1u << 19)) != 0;
        public bool OnFoot      => (Flags2 & (1u << 0)) != 0;

        // True when we have any positional data (flag OR non-zero coords)
        public bool HasPosition => HasLatLong || Latitude != 0 || Longitude != 0;
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
