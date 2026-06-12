using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    public class CachedOrganism
    {
        public double   Latitude   { get; set; }
        public double   Longitude  { get; set; }
        public string   Genus      { get; set; } = "";
        public string   Species    { get; set; } = "";
        public int      ScanCount  { get; set; }
        public bool     IsComplete { get; set; }
        public DateTime LastSeen   { get; set; }
    }

    public class CachedGeoSite
    {
        public double   Latitude  { get; set; }
        public double   Longitude { get; set; }
        public string   Name      { get; set; } = "";
        public int      EntryID   { get; set; }
        public long     Payout    { get; set; }
        public DateTime LastSeen  { get; set; }
    }

    public class CachedBodyData
    {
        public int                  BiologyCount  { get; set; }
        public int                  GeologyCount  { get; set; }
        public bool                 WasFootfalled { get; set; }
        public List<string>         KnownGenera   { get; set; } = new();
        public List<CachedOrganism> Scans         { get; set; } = new();
        public List<CachedGeoSite>  GeoSites      { get; set; } = new();
    }

    public class LoadedBodyData
    {
        public int                   BiologyCount  { get; set; }
        public int                   GeologyCount  { get; set; }
        public bool                  WasFootfalled { get; set; }
        public List<string>          KnownGenera   { get; set; } = new();
        public List<ScannedOrganism> Organisms     { get; set; } = new();
        public List<ScannedGeoSite>  GeoSites      { get; set; } = new();
    }

    public static class ScanCache
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.cache.json");

        private static readonly object _lock = new();

        // ---------------------------------------------------------------
        public static (string BodyName, LoadedBodyData Data) LoadLastActiveBody()
        {
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (all.Count == 0) return ("", new LoadedBodyData());

                    var best = all
                        .Where(kvp => kvp.Value.Scans.Count > 0)
                        .OrderByDescending(kvp => kvp.Value.Scans.Max(o => o.LastSeen))
                        .FirstOrDefault();

                    if (best.Key == null) return ("", new LoadedBodyData());

                    Log.Write($"ScanCache.LoadLastActiveBody: '{best.Key}' bio={best.Value.BiologyCount} scans={best.Value.Scans.Count}");
                    return (best.Key, ToLoaded(best.Value));
                }
            }
            catch (Exception ex)
            {
                Log.Write($"ScanCache.LoadLastActiveBody error: {ex.Message}");
                return ("", new LoadedBodyData());
            }
        }

        // ---------------------------------------------------------------
        public static LoadedBodyData LoadForBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return new LoadedBodyData();
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (!all.TryGetValue(bodyName, out var cached)) return new LoadedBodyData();
                    Log.Write($"ScanCache: loaded '{bodyName}' bio={cached.BiologyCount} genera={cached.KnownGenera.Count} scans={cached.Scans.Count}");
                    return ToLoaded(cached);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"ScanCache.LoadForBody error: {ex.Message}");
                return new LoadedBodyData();
            }
        }

        // ---------------------------------------------------------------
        public static void SaveForBody(string bodyName, IEnumerable<ScannedOrganism> organisms,
                                       int biologyCount = 0, IEnumerable<string>? knownGenera = null,
                                       bool wasFootfalled = false)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();

                    // Preserve existing bio count, genera and footfall if not provided
                    all.TryGetValue(bodyName, out var existing);
                    int bioCount    = biologyCount > 0 ? biologyCount : existing?.BiologyCount ?? 0;
                    var genera      = knownGenera?.ToList() ?? existing?.KnownGenera ?? new List<string>();
                    bool footfalled = wasFootfalled || existing?.WasFootfalled == true;

                    var toSave = organisms
                        .Where(o => o.IsComplete)  // never cache incomplete scans
                        .GroupBy(o => $"{o.Genus}|{o.Latitude:F4}|{o.Longitude:F4}|{o.ScanCount}")
                        .Select(g => g.OrderByDescending(o => o.LastSeen).First())
                        .Select(o => new CachedOrganism
                        {
                            Latitude   = o.Latitude,
                            Longitude  = o.Longitude,
                            Genus      = o.Genus,
                            Species    = o.Species,
                            ScanCount  = o.ScanCount,
                            IsComplete = true,
                            LastSeen   = o.LastSeen,
                        }).ToList();

                    if (toSave.Count == 0 && bioCount == 0 && genera.Count == 0)
                        all.Remove(bodyName);
                    else
                        all[bodyName] = new CachedBodyData
                        {
                            BiologyCount  = bioCount,
                            GeologyCount  = existing?.GeologyCount ?? 0,
                            WasFootfalled = footfalled,
                            KnownGenera   = genera,
                            Scans         = toSave,
                            GeoSites      = existing?.GeoSites ?? new List<CachedGeoSite>(),
                        };

                    WriteAll(all);
                    Log.Write($"ScanCache: saved '{bodyName}' bio={bioCount} genera={genera.Count} scans={toSave.Count}");
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.SaveForBody error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        // Both knownGenera and wasFootfalled are nullable:
        //   - Pass knownGenera = null to preserve whatever the cache already has
        //     (use this when saving from FSSBodySignals where no Genuses array exists,
        //      or when the event body isn't the body we're currently tracking).
        //   - Pass wasFootfalled = null to preserve cached FF.
        //     When a value IS passed, FF is sticky-true: once true in cache it stays true,
        //     since First Footfall is a game permanence that can never be undone.
        public static void SaveBodyMeta(string bodyName, int biologyCount,
                                        IEnumerable<string>? knownGenera = null,
                                        bool? wasFootfalled = null)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    all.TryGetValue(bodyName, out var existing);

                    // FF: sticky-true. Never demote true→false from any source.
                    bool finalFF = wasFootfalled.HasValue
                        ? ((existing?.WasFootfalled == true) || wasFootfalled.Value)
                        : (existing?.WasFootfalled ?? false);

                    // KnownGenera: preserve existing if caller passes null
                    var finalGenera = knownGenera?.ToList()
                                      ?? existing?.KnownGenera
                                      ?? new List<string>();

                    all[bodyName] = new CachedBodyData
                    {
                        BiologyCount  = biologyCount > 0 ? biologyCount : existing?.BiologyCount ?? 0,
                        GeologyCount  = existing?.GeologyCount ?? 0,
                        WasFootfalled = finalFF,
                        KnownGenera   = finalGenera,
                        Scans         = existing?.Scans ?? new List<CachedOrganism>(),
                        GeoSites      = existing?.GeoSites ?? new List<CachedGeoSite>(),
                    };
                    WriteAll(all);
                    Log.Write($"ScanCache: saved meta '{bodyName}' bio={biologyCount} ff={finalFF} (FFarg={(wasFootfalled.HasValue ? wasFootfalled.Value.ToString() : "null")})");
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.SaveBodyMeta error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        public static void ClearGenus(string bodyName, string genus)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (!all.TryGetValue(bodyName, out var cached)) return;
                    cached.Scans.RemoveAll(c => string.Equals(c.Genus, genus, StringComparison.OrdinalIgnoreCase));
                    all[bodyName] = cached;
                    WriteAll(all);
                    Log.Write($"ScanCache: cleared genus {genus} from '{bodyName}'");
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.ClearGenus error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        public static void SaveGeoSite(string bodyName, ScannedGeoSite site, int geoCount)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    all.TryGetValue(bodyName, out var existing);
                    var data = existing ?? new CachedBodyData();

                    // Deduplicate by EntryID
                    data.GeoSites ??= new List<CachedGeoSite>();
                    if (!data.GeoSites.Any(g => g.EntryID == site.EntryID))
                    {
                        data.GeoSites.Add(new CachedGeoSite
                        {
                            Latitude  = site.Latitude,
                            Longitude = site.Longitude,
                            Name      = site.Name,
                            EntryID   = site.EntryID,
                            Payout    = site.Payout,
                            LastSeen  = site.LastSeen,
                        });
                    }
                    if (geoCount > 0) data.GeologyCount = geoCount;
                    all[bodyName] = data;
                    WriteAll(all);
                    Log.Write($"ScanCache: saved geo site '{site.Name}' on '{bodyName}'");
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.SaveGeoSite error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        public static void SaveGeoCount(string bodyName, int geoCount)
        {
            if (string.IsNullOrEmpty(bodyName) || geoCount <= 0) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    all.TryGetValue(bodyName, out var existing);
                    var data = existing ?? new CachedBodyData();
                    data.GeologyCount = geoCount;
                    data.GeoSites   ??= new List<CachedGeoSite>();
                    all[bodyName] = data;
                    WriteAll(all);
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.SaveGeoCount error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        public static void ClearBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (all.Remove(bodyName))
                    {
                        WriteAll(all);
                        Log.Write($"ScanCache: cleared body '{bodyName}'");
                    }
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.ClearBody error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        public static void RemoveOrganism(string bodyName, ScannedOrganism org)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            try
            {
                lock (_lock)
                {
                    var all = ReadAll();
                    if (!all.TryGetValue(bodyName, out var cached)) return;
                    cached.Scans.RemoveAll(c =>
                        string.Equals(c.Genus, org.Genus, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(c.Latitude  - org.Latitude)  < 0.0001 &&
                        Math.Abs(c.Longitude - org.Longitude) < 0.0001);
                    all[bodyName] = cached;
                    WriteAll(all);
                }
            }
            catch (Exception ex) { Log.Write($"ScanCache.RemoveOrganism error: {ex.Message}"); }
        }

        // ---------------------------------------------------------------
        private static LoadedBodyData ToLoaded(CachedBodyData cached)
        {
            return new LoadedBodyData
            {
                BiologyCount  = cached.BiologyCount,
                GeologyCount  = cached.GeologyCount,
                WasFootfalled = cached.WasFootfalled,
                KnownGenera   = cached.KnownGenera.ToList(),
                Organisms     = cached.Scans.Select(c => new ScannedOrganism
                {
                    Latitude   = c.Latitude,
                    Longitude  = c.Longitude,
                    Genus      = c.Genus,
                    Species    = c.Species,
                    ScanCount  = c.ScanCount,
                    IsComplete = c.IsComplete || c.ScanCount >= 3,
                    LastSeen   = c.LastSeen,
                }).ToList(),
                GeoSites = (cached.GeoSites ?? new List<CachedGeoSite>()).Select(g => new ScannedGeoSite
                {
                    Latitude  = g.Latitude,
                    Longitude = g.Longitude,
                    Name      = g.Name,
                    EntryID   = g.EntryID,
                    Payout    = g.Payout,
                    LastSeen  = g.LastSeen,
                }).ToList(),
            };
        }

        // ---------------------------------------------------------------
        private static Dictionary<string, CachedBodyData> ReadAll()
        {
            if (!File.Exists(_path))
                return new Dictionary<string, CachedBodyData>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var json = File.ReadAllText(_path);
                // Try new format first
                var result = JsonConvert.DeserializeObject<Dictionary<string, CachedBodyData>>(json);
                if (result != null) return result;
            }
            catch
            {
                // Old format was List<CachedOrganism> per body — migrate it
                try
                {
                    var json = File.ReadAllText(_path);
                    var old = JsonConvert.DeserializeObject<Dictionary<string, List<CachedOrganism>>>(json);
                    if (old != null)
                    {
                        var migrated = old.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new CachedBodyData { Scans = kvp.Value },
                            StringComparer.OrdinalIgnoreCase);
                        WriteAll(migrated);
                        Log.Write("ScanCache: migrated old cache format");
                        return migrated;
                    }
                }
                catch { }
            }
            return new Dictionary<string, CachedBodyData>(StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteAll(Dictionary<string, CachedBodyData> data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_path, json);
        }
    }
}
