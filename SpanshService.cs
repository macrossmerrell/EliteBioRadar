using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // Spansh (spansh.co.uk) integration — read-only lookups against its precomputed galaxy
    // database, used by the Star Finder window to find the nearest star of a chosen type.
    // Every call here is directly triggered by the user clicking "Find" in that window; there
    // is no background/journal-driven use of this service, so unlike EdsmService there's no
    // opt-in setting gating it — same trust level as any other explicit, user-initiated lookup.
    public static class SpanshService
    {
        private static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { BaseAddress = new Uri("https://www.spansh.co.uk/") };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EliteBioRadar", AppVersion));
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        }

        public record StarCoords(double X, double Y, double Z);

        // Resolves a system name to galactic coordinates via EDSM's public system lookup — used
        // here rather than Spansh's own system search because EDSM computes coordinates from a
        // system's procedural name even when nobody has ever visited/reported it, while Spansh
        // only knows systems its own EDDN-fed catalog has actually recorded. Independent of
        // EdsmService/AppSettingsData.EdsmEnabled: that flag gates the exploration-data upload
        // and download features, a separate concern from this one-off coordinate lookup.
        public static async Task<StarCoords?> ResolveSystemCoordsAsync(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName)) return null;
            try
            {
                using var edsm = new HttpClient { BaseAddress = new Uri("https://www.edsm.net/") };
                edsm.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EliteBioRadar", AppVersion));
                edsm.Timeout = TimeSpan.FromSeconds(15);
                var url = $"api-v1/system?systemName={Uri.EscapeDataString(systemName)}&showCoordinates=1";
                using var response = await edsm.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body) || body == "[]") return null;
                var obj = JObject.Parse(body);
                var coords = obj["coords"];
                if (coords == null) return null;
                return new StarCoords(coords.Value<double>("x"), coords.Value<double>("y"), coords.Value<double>("z"));
            }
            catch (Exception ex)
            {
                Log.Write($"SpanshService: ResolveSystemCoords({systemName}) error — {ex.Message}");
                return null;
            }
        }

        // SystemName is what you'd actually type into the in-game galaxy map to plot a route —
        // Name is the star's own body name, which differs from SystemName for a non-primary star
        // (e.g. a black hole companion like "HD 17520 B" orbiting system "HD 17520").
        public record NearestStar(string Name, string SystemName, double DistanceLy, string Region);

        // subtypes: one or more exact Spansh body "subtype" strings (see StarTypeCatalog) — an
        // array means "any of these", used for grouped choices like "White Dwarf" covering every
        // DA/DB/DC/... spectral variant with one query.
        public static async Task<List<NearestStar>> FindNearestStarsAsync(IEnumerable<string> subtypes, StarCoords origin, int count)
        {
            var result = new List<NearestStar>();
            try
            {
                var filters = new JObject
                {
                    ["subtype"] = new JObject { ["value"] = new JArray(subtypes) }
                };
                var body = new JObject
                {
                    ["filters"] = filters,
                    ["sort"] = new JArray(new JObject { ["distance"] = new JObject { ["direction"] = "asc" } }),
                    ["reference_coords"] = new JObject { ["x"] = origin.X, ["y"] = origin.Y, ["z"] = origin.Z },
                    ["size"] = Math.Clamp(count, 1, 50),
                };
                using var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync("api/bodies/search", content);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Write($"SpanshService: FindNearestStars failed — HTTP {(int)response.StatusCode}");
                    return result;
                }
                var respBody = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(respBody);
                var results = obj["results"] as JArray;
                if (results == null) return result;
                foreach (var r in results)
                {
                    result.Add(new NearestStar(
                        r.Value<string>("name") ?? "?",
                        r.Value<string>("system_name") ?? r.Value<string>("name") ?? "?",
                        r.Value<double?>("distance") ?? 0,
                        r.Value<string>("system_region") ?? ""));
                }
            }
            catch (Exception ex)
            {
                Log.Write($"SpanshService: FindNearestStars error — {ex.Message}");
            }
            return result;
        }
    }
}
