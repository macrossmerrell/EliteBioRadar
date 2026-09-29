using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // EDSM (Elite Dangerous Star Map) integration — two directions:
    //   - Upload: forwards this commander's own exploration-relevant journal events to EDSM's
    //     journal API live, as they happen — the same role EDMC/EDDiscovery play for players
    //     running those tools.
    //   - Download: looks up already-known system/body data for systems this commander hasn't
    //     personally visited yet, via EDSM's public (unauthenticated) system/body API. Not yet
    //     surfaced in any UI — GetSystemBodiesAsync is the building block for that (planned:
    //     Destination route enrichment, then a system map with estimated scan values).
    // Off by default (see AppSettingsData.EdsmEnabled) — no method here is ever called by
    // anything unless the user has explicitly opted in via the Settings panel.
    public static class EdsmService
    {
        private static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { BaseAddress = new Uri("https://www.edsm.net/") };
            // EDSM asks integrators to identify their tool with a real name/version (not a
            // generic default) so any problem submissions can be traced back to this app.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EliteBioRadar", AppVersion));
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        // Exploration-relevant journal events worth forwarding — the same set this app already
        // parses locally for its own features. EDSM's journal endpoint silently discards any
        // event type it doesn't want server-side, so a slightly generous list here is harmless;
        // no need to hand-match EDSM's own internal allow-list exactly.
        // Loadout/LoadGame added after a real report: EDSM's own "system traffic" ship-type
        // tracking comes from these two events specifically, and neither was ever in this list —
        // every upload was an exploration event only, so EDSM had no real ship data for this
        // commander at all and fell back to its own default (Sidewinder) for every jump logged.
        private static readonly HashSet<string> UploadableEvents = new(StringComparer.OrdinalIgnoreCase)
        {
            "FSDJump", "CarrierJump", "Location", "Scan", "FSSDiscoveryScan", "FSSBodySignals",
            "SAAScanComplete", "SAASignalsFound", "ApproachBody", "Touchdown", "LeaveBody",
            "CodexEntry", "ScanOrganic", "Loadout", "LoadGame",
        };

        // Exposed so BackfillEdsmAsync (EliteWatcherService) can filter journal lines the same
        // way the live hook does, without duplicating this list.
        public static bool IsUploadable(string eventName) => UploadableEvents.Contains(eventName);

        // Fire-and-forget entry point for the live journal-processing loop — never let a
        // network hiccup slow down or break local journal processing. Backfill/replay lines
        // must never be passed here (the caller gates on that): forwarding hundreds of replayed
        // historical lines on every app restart would hammer EDSM for no benefit, since it
        // already has that same historical data from the first time it was played live.
        // gameVersion/gameBuild come from the current journal file's own "Fileheader" line —
        // EDSM's journal API rejects every submission with msgnum 207 ("Game/Build version not
        // found") without them, confirmed against a real rejection log.
        public static void SubmitJournalEventFireAndForget(string eventName, string rawLine, string gameVersion, string gameBuild, long? shipId = null)
        {
            if (!UploadableEvents.Contains(eventName)) return;
            var settings = AppSettings.Load();
            if (!settings.EdsmEnabled ||
                string.IsNullOrWhiteSpace(settings.EdsmCommanderName) ||
                string.IsNullOrWhiteSpace(settings.EdsmApiKey))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await SubmitJournalEventAsync(settings.EdsmCommanderName, settings.EdsmApiKey, eventName, rawLine, gameVersion, gameBuild, shipId);
                }
                catch (Exception ex) { Log.Write($"EdsmService upload error ({eventName}): {ex.Message}"); }
            });
        }

        // Public + awaitable (unlike the fire-and-forget wrapper above) so a caller that needs
        // to know success/failure and control pacing — namely BackfillEdsmAsync's catch-up loop
        // — can await each submission directly instead of firing a burst of untracked tasks.
        public static async Task<bool> SubmitJournalEventAsync(string commanderName, string apiKey, string eventName, string rawLine, string gameVersion, string gameBuild, long? shipId = null)
        {
            // EDSM's journal API is one generic passthrough: the raw journal event JSON goes in
            // "message" verbatim, alongside commander identity and software attribution — there
            // is no per-event-type request shape to build... EXCEPT for "_shipId". Confirmed
            // against EDSM's own server-side source (EDSM-NET/Journal-Events, Event.php's
            // findShipId): a traffic-log entry (e.g. what a system page shows at the bottom for
            // "last seen in") gets its ship from this transient key if present, and ONLY falls
            // back to EDSM's own server-side "current ship" session state when it's absent — a
            // fallback that proved unreliable across separate live submissions (real report:
            // ship showed as the default Sidewinder despite Loadout/LoadGame both carrying the
            // correct ship). EDMC's edsm.py injects this same key on every event it sends for
            // exactly this reason — mirrored here rather than depending on EDSM's own state.
            string message = rawLine;
            if (shipId.HasValue)
            {
                try
                {
                    var lineObj = JObject.Parse(rawLine);
                    lineObj["_shipId"] = shipId.Value;
                    message = lineObj.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch { /* malformed line — fall back to sending it unmodified */ }
            }
            var form = new Dictionary<string, string>
            {
                ["commanderName"]       = commanderName,
                ["apiKey"]              = apiKey,
                ["fromSoftware"]        = "Elite Bio Radar",
                ["fromSoftwareVersion"] = AppVersion,
                ["message"]             = message,
            };
            if (!string.IsNullOrWhiteSpace(gameVersion)) form["fromGameVersion"] = gameVersion;
            if (!string.IsNullOrWhiteSpace(gameBuild))   form["fromGameBuild"]   = gameBuild;
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync("api-journal-v1", content);
            if (!response.IsSuccessStatusCode)
            {
                Log.Write($"EdsmService: upload of {eventName} failed — HTTP {(int)response.StatusCode}");
                return false;
            }
            var body = await response.Content.ReadAsStringAsync();
            // Only log rejections (msgnum outside the 1xx success range), not every successful
            // submission — this fires once per exploration event during active play. (A
            // per-event raw-line-and-response dump lived here temporarily while diagnosing a
            // real EDSM ship-type bug — see the _shipId injection above — removed now that it's
            // confirmed fixed and verified live: EDSM correctly showed the right ship after a
            // real jump.)
            try
            {
                var obj = JObject.Parse(body);
                int msgnum = obj["msgnum"]?.Value<int>() ?? 0;
                if (msgnum != 0 && (msgnum < 100 || msgnum >= 200))
                {
                    Log.Write($"EdsmService: {eventName} rejected — {body}");
                    return false;
                }
            }
            catch { /* non-JSON response body — not fatal, treat as success (HTTP was OK) */ }
            return true;
        }

        // ---- Download side ----

        // Session-lifetime cache — a route's hops don't need re-fetching on every Destination
        // tab repaint (same idea as the RouteCache/backfill caching used elsewhere in this app).
        private static readonly ConcurrentDictionary<string, JObject?> _systemBodiesCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Raw JObject rather than a typed DTO — EDSM's body schema has many optional/version-
        // dependent fields, and consumers of this (Destination route enrichment, an eventual
        // system map with estimated scan values) don't exist yet, so keeping it untyped avoids
        // a model change here every time a new feature needs one more field out of it. Public
        // GET endpoint, no API key required — but still gated on EdsmEnabled so this app makes
        // zero network calls of any kind until the user opts in.
        public static async Task<JObject?> GetSystemBodiesAsync(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName)) return null;
            if (!AppSettings.Load().EdsmEnabled) return null;

            if (_systemBodiesCache.TryGetValue(systemName, out var cached)) return cached;

            try
            {
                var url = $"api-system-v1/bodies?systemName={Uri.EscapeDataString(systemName)}";
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Write($"EdsmService: GetSystemBodies({systemName}) failed — HTTP {(int)response.StatusCode}");
                    return null;
                }
                var body = await response.Content.ReadAsStringAsync();
                var result = string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body);
                _systemBodiesCache[systemName] = result;
                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"EdsmService: GetSystemBodies({systemName}) error — {ex.Message}");
                return null;
            }
        }
    }
}
