using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    public class TripImportResult
    {
        public int FilesRead { get; set; }
        public int Bodies { get; set; }
        public double Total { get; set; }
        public DateTime? EarliestUtc { get; set; }
    }

    // Rebuilds trip scan values from journal history: every Scan (and DSS map) inside the chosen
    // window is valued with the same ScanValueEstimator the live path uses, then merged into the
    // current trip. Independent of ScanCache — it reads the journals directly.
    public static class TripImporter
    {
        // fromLocal/toLocal are inclusive local dates (null = unbounded on that side).
        public static Task<TripImportResult> ImportAsync(string journalDir, DateTime? fromLocal, DateTime? toLocal) => Task.Run(() =>
        {
            var result = new TripImportResult();
            if (string.IsNullOrEmpty(journalDir) || !Directory.Exists(journalDir)) return result;

            DateTime? fromUtc = fromLocal?.Date.ToUniversalTime();
            DateTime? toUtc   = toLocal?.Date.AddDays(1).ToUniversalTime(); // exclusive end

            var details = new Dictionary<string, BodyScanDetail>(StringComparer.OrdinalIgnoreCase);
            var mapped  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(journalDir, "Journal.*.log").OrderByJournalDate())
            {
                try
                {
                    // A file last written before the window starts can't hold anything inside it.
                    if (fromUtc.HasValue && File.GetLastWriteTimeUtc(file) < fromUtc.Value) continue;

                    bool touched = false;
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        bool isScan = line.Contains("\"event\":\"Scan\"");
                        bool isMap  = !isScan && line.Contains("\"event\":\"SAAScanComplete\"");
                        if (!isScan && !isMap) continue;

                        JObject obj;
                        try { obj = JObject.Parse(line); } catch { continue; }

                        var ts = obj["timestamp"]?.Value<DateTime?>()?.ToUniversalTime();
                        if (ts.HasValue)
                        {
                            if (fromUtc.HasValue && ts.Value < fromUtc.Value) continue;
                            if (toUtc.HasValue && ts.Value >= toUtc.Value) continue;
                        }

                        var body = obj.Value<string>("BodyName");
                        if (string.IsNullOrEmpty(body)) continue;

                        if (isMap) { mapped.Add(body); }
                        else
                        {
                            details[body] = EliteWatcherService.ParseBodyScanDetail(obj, body, obj["StarType"] != null);
                        }
                        touched = true;
                        if (ts.HasValue && (result.EarliestUtc == null || ts.Value < result.EarliestUtc)) result.EarliestUtc = ts;
                    }
                    if (touched) result.FilesRead++;
                }
                catch (Exception ex) { Log.Write($"TripImporter: skipped {Path.GetFileName(file)} — {ex.Message}"); }
            }

            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, d) in details)
            {
                if (mapped.Contains(name)) d.IsMapped = true;
                var (est, _) = ScanValueEstimator.Estimate(d);
                if (est > 0) values[name] = est;
            }
            // A mapped body whose Scan fell outside the window (map only inside it) has no detail
            // to value — skipped, since there's nothing to estimate from.

            TripTracker.ImportScanValues(values, result.EarliestUtc);
            result.Bodies = values.Count;
            result.Total = values.Values.Sum();
            Log.Write($"TripImporter: {result.Bodies} bodies, {result.Total:N0} cr from {result.FilesRead} files");
            return result;
        });
    }
}
