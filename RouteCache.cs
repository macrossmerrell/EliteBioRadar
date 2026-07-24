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
        public double TotalRouteLy            { get; set; }
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
                File.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"RouteCache.Save error: {ex.Message}"); }
        }
    }
}
