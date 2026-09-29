using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // Live tally of the commander's RAW material stock (the surface/ring elements), so the
    // System Scan window can hide materials the commander is already full on. The journal
    // writes a full "Materials" snapshot at every game launch/relog; everything after that is
    // deltas (collected, discarded, traded, spent on engineering/synthesis/tech broker).
    // Startup: replay the newest journal that has a snapshot. Afterwards: fed live from
    // EliteWatcherService.ProcessJournalLine.
    public static class MaterialInventory
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _fullSet = new(StringComparer.OrdinalIgnoreCase);

        // False until a real "Materials" snapshot has been seen — without one the counts are
        // meaningless, so nothing is ever reported full (and never hidden) rather than guessing.
        public static bool IsReady { get; private set; }

        // Bumped only when the SET of full materials changes, not on every pickup, so the System
        // Scan window's refresh poll doesn't rebuild on each collected fragment.
        public static int Version { get; private set; }

        // In-game storage caps for raw materials, by grade.
        private static readonly Dictionary<string, int> Caps = new(StringComparer.OrdinalIgnoreCase);
        static MaterialInventory()
        {
            void Add(int cap, params string[] names) { foreach (var n in names) Caps[n] = cap; }
            Add(300, "carbon", "iron", "nickel", "phosphorus", "sulphur");
            Add(250, "arsenic", "chromium", "germanium", "manganese", "vanadium", "zinc", "zirconium");
            Add(200, "cadmium", "mercury", "molybdenum", "niobium", "tin", "tungsten");
            Add(150, "antimony", "polonium", "ruthenium", "selenium", "technetium", "tellurium", "yttrium");
        }

        public static int? Cap(string material) => Caps.TryGetValue(material, out var c) ? c : null;

        public static int? Count(string material)
        {
            lock (_lock) return IsReady && _counts.TryGetValue(material, out var n) ? n : IsReady ? 0 : null;
        }

        public static bool IsFull(string material)
        {
            lock (_lock)
            {
                if (!IsReady || !Caps.TryGetValue(material, out var cap)) return false;
                return _counts.TryGetValue(material, out var n) && n >= cap;
            }
        }

        public static Task BuildAsync(string journalDir) => Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(journalDir) || !Directory.Exists(journalDir)) return;
                var files = Directory.GetFiles(journalDir, "Journal.*.log").OrderByJournalDate().ToList();

                // Newest file that contains a snapshot; replay from there forward.
                int start = -1;
                for (int i = files.Count - 1; i >= 0 && start < 0; i--)
                    if (FileHasSnapshot(files[i])) start = i;
                if (start < 0) { Log.Write("MaterialInventory: no Materials snapshot found in journals"); return; }

                for (int i = start; i < files.Count; i++)
                {
                    try
                    {
                        using var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var sr = new StreamReader(fs);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!IsRelevant(line)) continue;
                            try { var o = JObject.Parse(line); Apply(o.Value<string>("event") ?? "", o); } catch { }
                        }
                    }
                    catch (Exception ex) { Log.Write($"MaterialInventory: skipped {Path.GetFileName(files[i])} — {ex.Message}"); }
                }
                int full; lock (_lock) full = _fullSet.Count;
                Log.Write($"MaterialInventory: ready — {full} raw material(s) at full capacity");
            }
            catch (Exception ex) { Log.Write($"MaterialInventory.BuildAsync error: {ex.Message}"); }
        });

        private static bool FileHasSnapshot(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null)
                    if (line.Contains("\"event\":\"Materials\"")) return true;
            }
            catch { }
            return false;
        }

        private static bool IsRelevant(string line) =>
            line.Contains("\"event\":\"Materials\"") || line.Contains("\"event\":\"MaterialCollected\"") ||
            line.Contains("\"event\":\"MaterialDiscarded\"") || line.Contains("\"event\":\"MaterialTrade\"") ||
            line.Contains("\"event\":\"EngineerCraft\"") || line.Contains("\"event\":\"Synthesis\"") ||
            line.Contains("\"event\":\"TechnologyBroker\"") || line.Contains("\"event\":\"EngineerContribution\"");

        // Called from the live journal path and from the startup replay.
        public static void Apply(string evt, JObject o)
        {
            lock (_lock)
            {
                switch (evt)
                {
                    case "Materials":
                        _counts.Clear();
                        if (o["Raw"] is JArray raw)
                            foreach (var m in raw) Set(m.Value<string>("Name"), m.Value<int?>("Count") ?? 0);
                        IsReady = true;
                        break;
                    case "MaterialCollected":
                        if (IsRaw(o.Value<string>("Category"))) Add(o.Value<string>("Name"), o.Value<int?>("Count") ?? 0);
                        break;
                    case "MaterialDiscarded":
                        if (IsRaw(o.Value<string>("Category"))) Add(o.Value<string>("Name"), -(o.Value<int?>("Count") ?? 0));
                        break;
                    case "MaterialTrade":
                        if (o["Paid"] is JObject paid && IsRaw(paid.Value<string>("Category")))
                            Add(paid.Value<string>("Material"), -(paid.Value<int?>("Quantity") ?? 0));
                        if (o["Received"] is JObject rec && IsRaw(rec.Value<string>("Category")))
                            Add(rec.Value<string>("Material"), rec.Value<int?>("Quantity") ?? 0);
                        break;
                    case "EngineerCraft":
                        SpendList(o["Ingredients"], null);
                        break;
                    case "Synthesis":
                        SpendList(o["Materials"], null);
                        break;
                    case "TechnologyBroker":
                        SpendList(o["Materials"], "Category");
                        break;
                    case "EngineerContribution":
                        if (o.Value<string>("Type") == "Material" && IsRaw(o.Value<string>("Category")))
                            Add(o.Value<string>("Material"), -(o.Value<int?>("Quantity") ?? 0));
                        break;
                    default: return;
                }
                RefreshFullSet();
            }
        }

        // Entries are {Name,Count[,Category]}; older journals used an object map name->count.
        private static void SpendList(JToken? t, string? categoryField)
        {
            if (t is JArray arr)
            {
                foreach (var m in arr)
                {
                    if (categoryField != null && !IsRaw(m.Value<string>(categoryField))) continue;
                    Add(m.Value<string>("Name"), -(m.Value<int?>("Count") ?? 0));
                }
            }
            else if (t is JObject map)
                foreach (var p in map.Properties()) Add(p.Name, -(p.Value.Value<int?>() ?? 0));
        }

        private static bool IsRaw(string? category) =>
            category == null || string.Equals(category, "Raw", StringComparison.OrdinalIgnoreCase);

        private static void Set(string? name, int n) { if (!string.IsNullOrEmpty(name)) _counts[name] = Math.Max(0, n); }

        // Only tracked elements matter (non-raw names in Ingredients lists simply won't be in Caps).
        private static void Add(string? name, int delta)
        {
            if (string.IsNullOrEmpty(name) || !Caps.ContainsKey(name)) return;
            _counts.TryGetValue(name, out var cur);
            _counts[name] = Math.Max(0, cur + delta);
        }

        private static void RefreshFullSet()
        {
            var now = new HashSet<string>(
                Caps.Where(kv => _counts.TryGetValue(kv.Key, out var n) && n >= kv.Value).Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);
            if (!now.SetEquals(_fullSet) || (IsReady && Version == 0)) { _fullSet = now; Version++; }
        }
    }
}
