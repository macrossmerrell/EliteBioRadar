using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    // Persists the full plotted route (not just the "remaining" view NavRoute.json gives us)
    // so hop count / progress survive an app or game restart mid-route. Keyed by the route's
    // final destination, which is the one thing that stays constant for a route's entire
    // lifetime — NavRoute.json only ever shows current-position-onward, shrinking each jump,
    // so the final entry is the sole stable identity signal available to detect "same route
    // continuing" vs "a new route was plotted" (see EliteWatcherService.EnsureRouteState).
    public class RouteCacheData
    {
        public long   FinalDestinationAddress { get; set; }
        public string FinalDestinationName    { get; set; } = "";
        public List<RouteHop> KnownHops       { get; set; } = new();
        // KnownHops/TotalRouteLy only ever describe the CURRENT leg (from wherever the most
        // recent snapshot started). HopsCompleted/LyCompleted carry forward how much of the
        // journey happened in EARLIER legs, toward this SAME final destination, before a
        // mid-route re-plot shortened/changed the remaining path — a real report: a
        // recalibration mid-journey (13 real jumps already flown) otherwise reset progress
        // to "hop 1" / ~1% instead of the correct ~15%, since the old leg's own history was
        // simply discarded whenever the remaining path stopped matching hop-for-hop. Only
        // reset to zero when the final destination itself actually changes — see
        // EliteWatcherService.EnsureRouteState.
        public double TotalRouteLy            { get; set; }
        public int    HopsCompleted           { get; set; }
        public double LyCompleted             { get; set; }
    }

    public static class RouteCache
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.route.json");

        public static RouteCacheData? Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    return JsonConvert.DeserializeObject<RouteCacheData>(json);
                }
            }
            catch (Exception ex) { Log.Write($"RouteCache.Load error: {ex.Message}"); }
            return null;
        }

        public static void Save(RouteCacheData data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                AtomicFile.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"RouteCache.Save error: {ex.Message}"); }
        }

        // Wipes the persisted cache so a stale/unrelated route doesn't resurrect itself on the
        // next load (see EliteWatcherService's FSDJump handler — arriving somewhere that isn't
        // anywhere in the cached route's hop list).
        public static void Delete()
        {
            try { File.Delete(_path); }
            catch (Exception ex) { Log.Write($"RouteCache.Delete error: {ex.Message}"); }
        }
    }
}
