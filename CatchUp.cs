using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // "While you were away": what the journal recorded between the moment the app last ran and now. The app writes a heartbeat (and one more on a clean exit) into LastRun.txt beside
    // the program; on the next start the journal lines after that moment are tallied into a few log rows for the CatchUp animation.
    public static class CatchUp
    {
        private static readonly string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LastRun.txt");
        private const int MaxRows = 6;

        public static DateTime? ReadLastRun()
        {
            try
            {
                if (File.Exists(_path) && DateTime.TryParse(File.ReadAllText(_path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) return t;
            }
            catch (Exception ex) { Log.Write($"CatchUp.ReadLastRun error: {ex.Message}"); }
            return null;
        }

        public static void Touch()
        {
            try { File.WriteAllText(_path, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)); }
            catch { /* a read-only folder just means no catch-up next time */ }
        }

        public sealed record Result(string Detail, int Events, bool Playing);

        private sealed record Row(DateTime At, string Kind, string Text, char Color, int Priority);

        private static string Fd(string? s) => (s ?? "").Replace('|', ' ').Replace(';', ' ').Replace('~', ' ').Trim().ToUpperInvariant();

        private static string Money(double v)
        {
            var inv = CultureInfo.InvariantCulture;
            return v >= 1e9 ? (v / 1e9).ToString("0.0", inv) + " B CR" : v >= 1e6 ? (v / 1e6).ToString("0.0", inv) + " M CR" : v >= 1e3 ? (v / 1e3).ToString("0.0", inv) + " K CR" : Math.Round(v).ToString("0", inv) + " CR";
        }

        private static string Span(TimeSpan s)
        {
            if (s.TotalDays >= 1) return $"{(int)s.TotalDays} D {s.Hours} H";
            if (s.TotalHours >= 1) return $"{(int)s.TotalHours} H {s.Minutes} M";
            return $"{Math.Max(1, (int)Math.Round(s.TotalMinutes))} M";
        }

        // Tally the journal since the given moment. Null when nothing worth listing happened. Playing = the newest journal's last LoadGame / Shutdown says a commander is in the game now.
        public static Result? Build(string journalDir, DateTime sinceUtc)
        {
            var rows = new List<Row>();
            bool playing = false;
            int jumps = 0, scans = 0, mapped = 0, firsts = 0, samples = 0, completes = 0, sales = 0, docks = 0, ranks = 0, missions = 0, deaths = 0, trades = 0;
            double ly = 0, missionPay = 0, tradePay = 0;
            DateTime lastJump = default, lastScan = default, lastMap = default, lastFirst = default, lastMission = default, lastTrade = default;
            var bodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sampleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string sinceStamp = sinceUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            try
            {
                var files = new DirectoryInfo(journalDir).GetFiles("Journal.*.log").Where(f => f.LastWriteTimeUtc >= sinceUtc).OrderBy(f => f.Name).ToList();
                var newest = new DirectoryInfo(journalDir).GetFiles("Journal.*.log").OrderBy(f => f.Name).LastOrDefault();
                foreach (var f in files)
                {
                    using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        int ti = line.IndexOf("\"timestamp\":\"", StringComparison.Ordinal);
                        if (ti < 0 || line.Length < ti + 33) continue;
                        string stamp = line.Substring(ti + 13, 19);
                        if (string.CompareOrdinal(stamp, sinceStamp) <= 0) continue;
                        int ei = line.IndexOf("\"event\":\"", StringComparison.Ordinal);
                        if (ei < 0) continue;
                        int ee = line.IndexOf('"', ei + 9);
                        if (ee < 0) continue;
                        string ev = line.Substring(ei + 9, ee - ei - 9);
                        if (ev is not ("FSDJump" or "Scan" or "SAAScanComplete" or "ScanOrganic" or "SellOrganicData" or "SellExplorationData" or "MultiSellExplorationData"
                                       or "Docked" or "Promotion" or "Died" or "MissionCompleted" or "MarketSell" or "LoadGame" or "Shutdown")) continue;
                        if (!DateTime.TryParse(stamp + "Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) continue;
                        JObject o;
                        try { o = JObject.Parse(line); } catch { continue; }
                        switch (ev)
                        {
                            case "LoadGame": if (newest != null && f.Name == newest.Name) playing = true; break;
                            case "Shutdown": if (newest != null && f.Name == newest.Name) playing = false; break;
                            case "FSDJump": jumps++; ly += o.Value<double?>("JumpDist") ?? 0; lastJump = at; break;
                            case "Scan":
                                if ((o.Value<string>("ScanType") ?? "") == "NavBeaconDetail") break;
                                string bn = o.Value<string>("BodyName") ?? "";
                                if (bodies.Add(bn)) { scans++; lastScan = at; }
                                if (o.Value<bool?>("WasDiscovered") == false && !bn.Contains("Belt Cluster", StringComparison.OrdinalIgnoreCase)) { firsts++; lastFirst = at; }
                                break;
                            case "SAAScanComplete": mapped++; lastMap = at; break;
                            case "ScanOrganic":
                            {
                                string sp = Fd(o.Value<string>("Species_Localised") ?? o.Value<string>("Genus_Localised") ?? o.Value<string>("Species"));
                                string st = o.Value<string>("ScanType") ?? "";
                                if (st == "Analyse") { completes++; sampleCount.Remove(sp); rows.Add(new Row(at, "COMPLETE", sp, 'G', 1)); }
                                else
                                {
                                    samples++;
                                    int n = st == "Log" ? 1 : (sampleCount.TryGetValue(sp, out var c) ? c + 1 : 2);
                                    sampleCount[sp] = n;
                                    rows.Add(new Row(at, "SAMPLE", $"{sp} {Math.Min(3, n)} OF 3", 'V', 2));
                                }
                                break;
                            }
                            case "SellOrganicData":
                            {
                                double v = 0;
                                if (o["BioData"] is JArray a) foreach (var b in a) v += (b.Value<double?>("Value") ?? 0) + (b.Value<double?>("Bonus") ?? 0);
                                sales++; rows.Add(new Row(at, "SOLD", "BIO DATA " + Money(v), 'A', 1));
                                break;
                            }
                            case "SellExplorationData":
                            case "MultiSellExplorationData":
                                sales++; rows.Add(new Row(at, "SOLD", "EXPLORATION DATA " + Money(o.Value<double?>("TotalEarnings") ?? 0), 'A', 1));
                                break;
                            case "Docked": docks++; rows.Add(new Row(at, "DOCK", Fd(o.Value<string>("StationName_Localised") ?? o.Value<string>("StationName")), 'A', 3)); break;
                            case "Promotion":
                                foreach (var cat in new[] { "Combat", "Trade", "Explore", "Exobiologist" })
                                    if (o[cat] != null) { ranks++; rows.Add(new Row(at, "RANK", cat.ToUpperInvariant() + " " + RankUpVisual.RankName(cat, o.Value<int?>(cat) ?? 0).ToUpperInvariant(), 'G', 0)); }
                                break;
                            case "Died": deaths++; rows.Add(new Row(at, "DIED", "SHIP DESTROYED", 'R', 0)); break;
                            case "MissionCompleted": missions++; missionPay += o.Value<double?>("Reward") ?? 0; lastMission = at; break;
                            case "MarketSell": trades++; tradePay += o.Value<double?>("TotalSale") ?? 0; lastTrade = at; break;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write($"CatchUp.Build error: {ex.GetType().Name}: {ex.Message}"); return null; }

            // The counted kinds become one row each, at the time of the last one.
            if (jumps > 0) rows.Add(new Row(lastJump, "JUMP", $"{jumps} JUMP{(jumps == 1 ? "" : "S")}  {Math.Round(ly):0} LY", 'C', 4));
            if (scans > 0) rows.Add(new Row(lastScan, "SCAN", $"{scans} BODIES SCANNED".Replace("1 BODIES", "1 BODY"), 'G', 4));
            if (mapped > 0) rows.Add(new Row(lastMap, "MAP", $"{mapped} BODIES MAPPED".Replace("1 BODIES", "1 BODY"), 'G', 3));
            if (firsts > 0) rows.Add(new Row(lastFirst, "FIRST", $"{firsts} FIRST DISCOVER{(firsts == 1 ? "Y" : "IES")}", 'A', 2));
            if (missions > 0) rows.Add(new Row(lastMission, "MISSION", $"{missions} COMPLETED  {Money(missionPay)}", 'A', 2));
            if (trades > 0) rows.Add(new Row(lastTrade, "TRADE", "CARGO SOLD " + Money(tradePay), 'A', 3));
            if (rows.Count == 0) return null;

            int events = jumps + scans + mapped + samples + completes + sales + docks + ranks + missions + deaths + trades;
            var keep = rows.OrderBy(r => r.Priority).ThenByDescending(r => r.At).Take(MaxRows).OrderBy(r => r.At).ToList();
            string time(DateTime t) => t.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            string body = string.Join(";", keep.Select(r => $"{time(r.At)}~{r.Kind}~{r.Text}~{r.Color}"));
            int more = rows.Count - keep.Count;
            return new Result($"{Span(DateTime.UtcNow - sinceUtc)}|{events}|{more}|{body}", events, playing);
        }
    }
}
