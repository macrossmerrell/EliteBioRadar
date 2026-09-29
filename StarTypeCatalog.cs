using System.Collections.Generic;

namespace EliteBioRadar
{
    // Curated subset of the canonical journal StarType enum (elite-journal.readthedocs.io,
    // Appendix: Star Descriptions) offered in the Star Finder dropdown — the full raw enum is
    // ~80 spectral sub-codes, most of which nobody hunting for a specific star cares to pick
    // individually (e.g. every DAB/DAV/DAZ white dwarf variant separately). Each entry maps a
    // player-facing label to every exact Spansh "subtype" string it should match — Spansh's
    // bodies/search filter takes an array as "match any of these", so a grouped label like
    // "White Dwarf" still resolves to one query across all of its spectral variants.
    public static class StarTypeCatalog
    {
        public static readonly (string Label, string[] Subtypes)[] Entries = new (string, string[])[]
        {
            ("Neutron Star", new[] { "Neutron Star" }),
            ("Black Hole", new[] { "Black Hole" }),
            ("Supermassive Black Hole", new[] { "Supermassive Black Hole" }),
            ("White Dwarf", new[]
            {
                "White Dwarf (D) Star", "White Dwarf (DA) Star", "White Dwarf (DAB) Star",
                "White Dwarf (DAV) Star", "White Dwarf (DAZ) Star", "White Dwarf (DB) Star",
                "White Dwarf (DBV) Star", "White Dwarf (DBZ) Star", "White Dwarf (DC) Star",
                "White Dwarf (DCV) Star", "White Dwarf (DQ) Star",
            }),
            ("Wolf-Rayet Star", new[]
            {
                "Wolf-Rayet Star", "Wolf-Rayet C Star", "Wolf-Rayet N Star",
                "Wolf-Rayet NC Star", "Wolf-Rayet O Star",
            }),
            ("Herbig Ae/Be Star", new[] { "Herbig Ae/Be Star" }),
            ("T Tauri Star", new[] { "T Tauri Star" }),
            ("Carbon Star", new[] { "C Star", "CN Star", "CJ Star", "MS-type Star", "S-type Star" }),
            ("Brown Dwarf", new[] { "L (Brown dwarf) Star", "T (Brown dwarf) Star", "Y (Brown dwarf) Star" }),
            ("O (Blue-White) Star", new[] { "O (Blue-White) Star" }),
            ("A (Blue-White) Star", new[] { "A (Blue-White) Star" }),
            ("A (Blue-White Super Giant)", new[] { "A (Blue-White super giant) Star" }),
            ("B (Blue-White) Star", new[] { "B (Blue-White) Star" }),
            ("B (Blue-White Super Giant)", new[] { "B (Blue-White super giant) Star" }),
            ("F (White) Star", new[] { "F (White) Star" }),
            ("F (White Super Giant)", new[] { "F (White super giant) Star" }),
            ("G (White-Yellow) Star", new[] { "G (White-Yellow) Star" }),
            ("G (White-Yellow Super Giant)", new[] { "G (White-Yellow super giant) Star" }),
            ("K (Yellow-Orange) Star", new[] { "K (Yellow-Orange) Star" }),
            ("K (Orange Giant)", new[] { "K (Yellow-Orange giant) Star" }),
            ("M (Red Dwarf) Star", new[] { "M (Red dwarf) Star" }),
            ("M (Red Giant)", new[] { "M (Red giant) Star" }),
            ("M (Red Super Giant)", new[] { "M (Red super giant) Star" }),
        };
    }
}
