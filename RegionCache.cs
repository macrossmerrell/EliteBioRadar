using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    // ---------------------------------------------------------------
    //  Persists StarSystem -> galactic region name, learned from
    //  CodexEntry.Region_Localised. Region belongs to the system, not
    //  an individual body, so this is kept separate from ScanCache
    //  (which is keyed per body) rather than duplicated per body.
    // ---------------------------------------------------------------
    public static class RegionCache
    {
        public const string UnknownRegion = "Unknown";

        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.regions.json");

        private static readonly object _lock = new();

        // Region has two sources — CodexEntry.Region_Localised (the game's own text,
        // ground truth) and the offline boundary classifier in RegionMap.cs (a
        // fan-maintained dataset that's usually right but occasionally drifts from
        // the game's exact wording). Known drift gets normalized here so both
        // sources always land in the same bucket instead of splitting a region in two.
        private static readonly Dictionary<string, string> _aliases =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "Formidine Rift", "The Formidine Rift" },
        };

        private static string Canonical(string region) =>
            _aliases.TryGetValue(region, out var canon) ? canon : region;

        public static void SaveRegion(string system, string region)
        {
            if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(region)) return;
            region = Canonical(region);
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (all.TryGetValue(system, out var existing) &&
                        string.Equals(existing, region, StringComparison.OrdinalIgnoreCase))
                        return;

                    all[system] = region;
                    WriteAll(all);
                }
            }
            catch (Exception ex) { Log.Write($"RegionCache.SaveRegion error: {ex.Message}"); }
        }

        public static string GetRegion(string system)
        {
            if (string.IsNullOrEmpty(system)) return UnknownRegion;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    return all.TryGetValue(system, out var region) ? region : UnknownRegion;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"RegionCache.GetRegion error: {ex.Message}");
                return UnknownRegion;
            }
        }

        public static Dictionary<string, string> LoadAll()
        {
            try
            {
                lock (_lock) { return ReadAll(); }
            }
            catch (Exception ex)
            {
                Log.Write($"RegionCache.LoadAll error: {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void ClearAll()
        {
            try
            {
                lock (_lock)
                {
                    WriteAll(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    Log.Write("RegionCache: cleared");
                }
            }
            catch (Exception ex) { Log.Write($"RegionCache.ClearAll error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        private static Dictionary<string, string> ReadAll()
        {
            if (!File.Exists(_path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var json = File.ReadAllText(_path);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void WriteAll(Dictionary<string, string> data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_path, json);
        }
    }
}
