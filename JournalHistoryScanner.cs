using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EliteDangerousRegionMap;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    public class ScanSummary
    {
        public int      FilesScanned    { get; set; }
        public int      BodiesTouched   { get; set; }
        public int      BioScansFound   { get; set; }
        public int      GeoSitesFound   { get; set; }
        public int      StarsFound      { get; set; }
        public int      WorldsFound     { get; set; }
        public int      PhenomenaFound  { get; set; }
        public int      RegionsFound    { get; set; }
        public TimeSpan Elapsed         { get; set; }
    }

    // ---------------------------------------------------------------
    //  Standalone, full-history journal importer for the Scan Log window.
    //  Deliberately does NOT reuse EliteWatcherService.ProcessJournalLine /
    //  BackfillJournal — those are entangled with live single-tracked-body
    //  state (CurrentBody, in-memory UI lists, event callbacks) and only
    //  ever look at the most recent journal files. This walks every journal
    //  file on disk, oldest to newest, and writes results for every body it
    //  finds — not just whichever one the player happens to be on right now.
    //
    //  Everything learned during the pass is accumulated in memory (as plain
    //  CachedBodyData per body) and written to ScanCache exactly ONCE at the
    //  end via SaveBulk — not per journal line. Earlier drafts called the
    //  per-body Save* methods directly from inside the line loop, which each
    //  did a full cache-file read+write; across tens of thousands of lines
    //  that turned a "Scan All" into a 20+ minute operation. This is the fix.
    //
    //  Does NOT touch EarningsTracker — re-running this must never inflate
    //  the player's earnings total.
    // ---------------------------------------------------------------
    public static class JournalHistoryScanner
    {
        public static ScanSummary ScanAll(string journalDir, IProgress<string>? progress = null)
        {
            var sw = Stopwatch.StartNew();
            var summary = new ScanSummary();

            if (string.IsNullOrEmpty(journalDir) || !Directory.Exists(journalDir))
            {
                summary.Elapsed = sw.Elapsed;
                return summary;
            }

            var files = Directory.GetFiles(journalDir, "Journal.*.log").OrderBy(f => f).ToArray();
            summary.FilesScanned = files.Length;

            var pending = new Dictionary<string, CachedBodyData>(StringComparer.OrdinalIgnoreCase);
            var regionsHandledThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string activeSystem = "";
            string activeBody   = "";
            double activeLat    = 0;
            double activeLon    = 0;

            CachedBodyData GetAccum(string body)
            {
                if (!pending.TryGetValue(body, out var acc))
                {
                    acc = new CachedBodyData { System = activeSystem };
                    pending[body] = acc;
                }
                else if (string.IsNullOrEmpty(acc.System) && !string.IsNullOrEmpty(activeSystem))
                {
                    acc.System = activeSystem;
                }
                return acc;
            }

            foreach (var file in files)
            {
                progress?.Report($"Scanning {Path.GetFileName(file)}...");

                IEnumerable<string> lines;
                try { lines = File.ReadLines(file); }
                catch (Exception ex)
                {
                    Log.Write($"JournalHistoryScanner: skipping '{file}': {ex.Message}");
                    continue;
                }

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"event\"")) continue;

                    JObject? obj;
                    try { obj = JObject.Parse(line); }
                    catch { continue; }

                    var evt = obj.Value<string>("event");
                    if (string.IsNullOrEmpty(evt)) continue;

                    switch (evt)
                    {
                        case "FSDJump":
                        case "CarrierJump":
                            activeSystem = obj.Value<string>("StarSystem") ?? activeSystem;
                            activeBody   = "";
                            SaveRegionFromStarPos(obj, activeSystem, regionsHandledThisRun);
                            break;

                        case "SupercruiseEntry":
                            activeBody = "";
                            break;

                        case "SupercruiseExit":
                            // No BodyType check here — Notable Stellar Phenomena sites
                            // (Lagrange clouds) exit supercruise with BodyType:"Null",
                            // not "Planet", and still need to be tracked as activeBody
                            // so a following CodexEntry can be attributed correctly.
                            // ApproachBody/Touchdown refine this further for real landings.
                            activeBody = obj.Value<string>("Body") ?? activeBody;
                            break;

                        case "ApproachBody":
                            activeBody = obj.Value<string>("Body") ?? activeBody;
                            break;

                        case "LeaveBody":
                            activeBody = "";
                            break;

                        case "Location":
                            activeSystem = obj.Value<string>("StarSystem") ?? activeSystem;
                            SaveRegionFromStarPos(obj, activeSystem, regionsHandledThisRun);
                            if (IsPlanet(obj))
                            {
                                activeBody = obj.Value<string>("Body") ?? activeBody;
                                activeLat  = obj.Value<double?>("Latitude")  ?? activeLat;
                                activeLon  = obj.Value<double?>("Longitude") ?? activeLon;
                            }
                            break;

                        case "Touchdown":
                            activeBody = obj.Value<string>("Body") ?? activeBody;
                            activeLat  = obj.Value<double?>("Latitude")  ?? activeLat;
                            activeLon  = obj.Value<double?>("Longitude") ?? activeLon;
                            break;

                        case "Disembark":
                            if (obj.Value<bool?>("OnPlanet") ?? false)
                            {
                                var disembarkBody = obj.Value<string>("Body") ?? activeBody;
                                activeBody = disembarkBody;
                                activeLat  = obj.Value<double?>("Latitude")  ?? activeLat;
                                activeLon  = obj.Value<double?>("Longitude") ?? activeLon;

                                // Ground truth for "did I footfall this body" — Scan.WasFootfalled
                                // only updates on a scan that happens AFTER the landing, which
                                // never happens if the player doesn't rescan post-landing (the
                                // common case). Disembark(OnPlanet:true) fires every single time
                                // regardless, so it's the reliable signal.
                                if (!string.IsNullOrEmpty(disembarkBody))
                                {
                                    var acc = GetAccum(disembarkBody);
                                    acc.Landable = true;
                                    acc.WasFootfalled = true;
                                    acc.FootfallAt ??= GetTimestamp(obj);
                                }
                            }
                            break;

                        case "CodexEntry":
                            HandleCodexEntry(obj, activeBody, GetAccum);
                            break;

                        case "ScanOrganic":
                            HandleScanOrganic(obj, activeBody, activeLat, activeLon, GetAccum);
                            break;

                        case "Scan":
                            HandleScan(obj, GetAccum);
                            break;
                    }
                }
            }

            ScanCache.SaveBulk(pending);

            summary.BodiesTouched  = pending.Count;
            summary.BioScansFound  = pending.Values.Sum(b => b.Scans.Count);
            summary.GeoSitesFound  = pending.Values.Sum(b => b.GeoSites.Count);
            summary.PhenomenaFound = pending.Values.Sum(b => b.Phenomena.Count);
            summary.StarsFound     = pending.Values.Count(b => !string.IsNullOrEmpty(b.StarType));
            summary.WorldsFound    = pending.Values.Count(b => !string.IsNullOrEmpty(b.PlanetClass));
            summary.RegionsFound   = RegionCache.LoadAll().Values
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            summary.Elapsed = sw.Elapsed;

            Log.Write($"JournalHistoryScanner: {summary.FilesScanned} files, {summary.BodiesTouched} bodies, " +
                      $"{summary.BioScansFound} bio scans, {summary.GeoSitesFound} geo sites, " +
                      $"{summary.StarsFound} stars, {summary.WorldsFound} worlds, {summary.PhenomenaFound} phenomena, " +
                      $"{summary.RegionsFound} regions, {summary.Elapsed.TotalSeconds:N1}s");

            return summary;
        }

        private static bool IsPlanet(JObject obj) =>
            string.Equals(obj.Value<string>("BodyType") ?? "", "Planet", StringComparison.OrdinalIgnoreCase);

        // Every journal line carries its own real-world "timestamp" (ISO-8601, e.g.
        // "2024-10-07T16:17:14Z") — that's what a date-range filter needs to key off,
        // not DateTime.UtcNow captured at import time (which would cluster everything
        // on whatever day Scan All happened to run, regardless of when it was actually
        // played). Newtonsoft.Json parses the ISO-8601 string natively.
        private static DateTime GetTimestamp(JObject obj) =>
            obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;

        // Region belongs to a system's galactic coordinates ("StarPos") — classified
        // offline via the same boundary map the game itself uses (see RegionMap.cs).
        // This is what actually eliminates "Unknown": CodexEntry.Region_Localised only
        // ever fires for systems where something new happened to be logged for the
        // first time, but every FSDJump/Location carries StarPos regardless.
        // `handledThisRun` skips repeat systems (the same system gets visited many
        // times across a real journal history) so this isn't a per-line cache write.
        private static void SaveRegionFromStarPos(JObject obj, string system, HashSet<string> handledThisRun)
        {
            if (string.IsNullOrEmpty(system) || !handledThisRun.Add(system)) return;
            var pos = obj["StarPos"] as JArray;
            if (pos == null || pos.Count != 3) return;

            var region = RegionMap.FindRegion(pos[0].Value<double>(), pos[1].Value<double>(), pos[2].Value<double>());
            if (region != null) RegionCache.SaveRegion(system, region.Name);
        }

        // ---------------------------------------------------------------
        private static void HandleCodexEntry(JObject obj, string activeBody, Func<string, CachedBodyData> getAccum)
        {
            // Region belongs to the system, not just geology entries — capture it
            // off any CodexEntry, regardless of category. (Cheap: RegionCache only
            // writes when the value actually changes.)
            var system = obj.Value<string>("System") ?? "";
            var region = obj.Value<string>("Region_Localised") ?? "";
            if (!string.IsNullOrEmpty(system) && !string.IsNullOrEmpty(region))
                RegionCache.SaveRegion(system, region);

            // CodexEntry carries no body-name string of its own (only a numeric BodyID) —
            // fall back to the body we're tracking from Touchdown/Disembark/ApproachBody/
            // SupercruiseExit, same as the live game-tracking code does with its CurrentBody.
            var codexBody = obj.Value<string>("BodyName") ?? obj.Value<string>("Body") ?? activeBody;
            var nameLoc   = obj.Value<string>("Name_Localised") ?? obj.Value<string>("Name") ?? "";
            var entryID   = obj.Value<int>("EntryID");
            if (string.IsNullOrEmpty(codexBody) || string.IsNullOrEmpty(nameLoc) || entryID == 0) return;

            var acc = getAccum(codexBody);
            if (!string.IsNullOrEmpty(system) && string.IsNullOrEmpty(acc.System)) acc.System = system;

            // Notable Stellar Phenomena finds (Anomalies, Mineral Formations, Molluscs,
            // Plants, Seed Pods) share SubCategory with ordinary geology/biology entries,
            // but always carry this NearestDestination tag — check it FIRST so these
            // never fall through into the plain Geology bucket below.
            var nearestDest = obj.Value<string>("NearestDestination_Localised") ?? "";
            if (string.Equals(nearestDest, "Notable stellar phenomena", StringComparison.OrdinalIgnoreCase))
            {
                if (acc.Phenomena.Any(p => p.EntryID == entryID)) return;
                acc.Phenomena.Add(new CachedPhenomenon
                {
                    Name     = nameLoc,
                    Category = PhenomenonCategory(nameLoc),
                    EntryID  = entryID,
                    LastSeen = GetTimestamp(obj),
                });
                return;
            }

            var subCat = obj.Value<string>("SubCategory") ?? "";
            if (!subCat.Contains("Geology_and_Anomalies")) return;

            if (acc.GeoSites.Any(g => g.EntryID == entryID)) return;
            acc.GeoSites.Add(new CachedGeoSite
            {
                Latitude  = obj.Value<double?>("Latitude")  ?? 0,
                Longitude = obj.Value<double?>("Longitude") ?? 0,
                Name      = nameLoc,
                EntryID   = entryID,
                Payout    = obj.Value<long?>("VoucherAmount") ?? 0,
                LastSeen  = GetTimestamp(obj),
            });
        }

        // Buckets a Notable Stellar Phenomena find's name into the game's own five
        // categories (see the wiki's "Notable Stellar Phenomena" breakdown) via simple
        // keyword matching — no external reference table needed, since these names are
        // stable and few in number.
        private static string PhenomenonCategory(string name)
        {
            if (name.Contains("Anomaly", StringComparison.OrdinalIgnoreCase)) return "Anomalies";
            if (name.Contains("Mollusc", StringComparison.OrdinalIgnoreCase)) return "Molluscs";
            if (name.Contains("Pod", StringComparison.OrdinalIgnoreCase)) return "Seed Pods";
            if (name.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Void Heart", StringComparison.OrdinalIgnoreCase)) return "Plants";
            if (name.Contains("Crystal", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Sphere", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Plate", StringComparison.OrdinalIgnoreCase)) return "Mineral Formations";
            return "Other";
        }

        // ---------------------------------------------------------------
        private static void HandleScan(JObject obj, Func<string, CachedBodyData> getAccum)
        {
            var bodyName = obj.Value<string>("BodyName") ?? "";
            if (string.IsNullOrEmpty(bodyName)) return;

            var system      = obj.Value<string>("StarSystem") ?? "";
            var starType    = obj.Value<string>("StarType");
            var planetClass = obj.Value<string>("PlanetClass");
            if (string.IsNullOrEmpty(starType) && string.IsNullOrEmpty(planetClass)) return;

            var acc = getAccum(bodyName);
            if (!string.IsNullOrEmpty(system) && string.IsNullOrEmpty(acc.System)) acc.System = system;
            acc.ScannedAt = GetTimestamp(obj);

            // A Scan event is either a star or a planet, never both.
            if (!string.IsNullOrEmpty(starType))
            {
                acc.StarType      = starType;
                acc.StarSubclass  = obj.Value<int?>("Subclass");
                acc.WasDiscovered = obj.Value<bool?>("WasDiscovered");
            }
            else
            {
                acc.PlanetClass    = planetClass;
                acc.TerraformState = obj.Value<string>("TerraformState") ?? "";
                if (obj.Value<bool?>("Landable") ?? false) acc.Landable = true;
                acc.WasDiscovered  = obj.Value<bool?>("WasDiscovered");
                if (obj.Value<bool?>("WasFootfalled") ?? false)
                {
                    acc.WasFootfalled = true;
                    // First sighting of WasFootfalled=true wins — files are processed
                    // oldest-to-newest, so this naturally lands on the earliest report,
                    // closest to when the footfall actually happened (not whatever much
                    // later, unrelated scan of the same body happens to run last).
                    acc.FootfallAt ??= GetTimestamp(obj);
                }
            }
        }

        // ---------------------------------------------------------------
        private static void HandleScanOrganic(JObject obj, string activeBody, double activeLat, double activeLon,
                                               Func<string, CachedBodyData> getAccum)
        {
            if (obj.Value<string>("ScanType") != "Analyse") return;
            if (string.IsNullOrEmpty(activeBody)) return;

            var genusLoc   = obj.Value<string>("Genus_Localised")   ?? "";
            var speciesLoc = obj.Value<string>("Species_Localised") ?? "";
            if (string.IsNullOrEmpty(genusLoc)) return;

            // Species_Localised often contains the full name e.g. "Bacterium Cerbrus" —
            // strip the genus prefix so DisplayName doesn't double it up.
            var species = speciesLoc.StartsWith(genusLoc + " ", StringComparison.OrdinalIgnoreCase)
                ? speciesLoc.Substring(genusLoc.Length + 1).Trim()
                : speciesLoc;

            var lat = obj.Value<double?>("Latitude")  ?? activeLat;
            var lon = obj.Value<double?>("Longitude") ?? activeLon;

            var acc = getAccum(activeBody);
            if (!acc.KnownGenera.Contains(genusLoc, StringComparer.OrdinalIgnoreCase))
                acc.KnownGenera.Add(genusLoc);

            bool alreadyHave = acc.Scans.Any(o =>
                string.Equals(o.Genus, genusLoc, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.Species, species, StringComparison.OrdinalIgnoreCase));
            if (alreadyHave) return;

            acc.Scans.Add(new CachedOrganism
            {
                Genus      = genusLoc,
                Species    = species,
                Latitude   = lat,
                Longitude  = lon,
                ScanCount  = 3,
                IsComplete = true,
                LastSeen   = GetTimestamp(obj),
            });
        }
    }
}
