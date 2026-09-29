using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EliteBioRadar
{
    // Answers "did YOU discover/map this body, or did another commander?" — something the
    // journal can't say directly. A Scan event's WasDiscovered/WasMapped only mean "someone
    // already had, before this scan", and that someone includes you: revisit a body you
    // discovered yourself and the journal reports WasDiscovered=true just like it would for
    // another commander's find. The reliable signal is your own history:
    //   - a Scan you made with WasDiscovered=false  -> you were the first to discover it
    //   - a SAAScanComplete you made               -> you mapped it
    // Built once at startup by reading the journal files (in the background — the History
    // scanner does a similar full pass in seconds), then kept current live as new scans and maps
    // happen. Deliberately independent of ScanCache: that cache is overwritten by the latest scan
    // of each body (so a rescan's "true" would erase the "false" that proves you were first), and
    // the live scan path never wrote WasDiscovered to it at all.
    public static class DiscoveryIndex
    {
        private static readonly object _lock = new();
        private static readonly HashSet<string> _discoveredByMe = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _mappedByMe     = new(StringComparer.OrdinalIgnoreCase);

        // False until the startup pass finishes. While false, callers must NOT report a body as
        // "discovered by another commander" — an unfinished index would wrongly blame anything
        // you discovered yourself.
        public static bool IsReady { get; private set; }

        // Bumped on every change so a window polling for "did anything change" (System Scan's
        // refresh signature) notices the index finishing or a live discovery landing.
        public static int Version { get; private set; }

        public static bool IsDiscoveredByMe(string bodyName) { lock (_lock) return _discoveredByMe.Contains(bodyName); }
        public static bool IsMappedByMe(string bodyName)     { lock (_lock) return _mappedByMe.Contains(bodyName); }

        public static void MarkDiscoveredByMe(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (_lock) { if (_discoveredByMe.Add(bodyName)) Version++; }
        }

        public static void MarkMappedByMe(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (_lock) { if (_mappedByMe.Add(bodyName)) Version++; }
        }

        public static Task BuildAsync(string journalDir) => Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(journalDir) || !Directory.Exists(journalDir)) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var mapped     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(journalDir, "Journal.*.log"))
                {
                    try
                    {
                        // Shared read — the live game may be appending to the newest file.
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var sr = new StreamReader(fs);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            // Cheap substring filters first — the vast majority of journal lines
                            // are neither, and full JSON parsing every one of them is what would
                            // make a pass over ~700 files slow.
                            bool isScan = line.Contains("\"event\":\"Scan\"") && line.Contains("\"WasDiscovered\":false");
                            bool isMap  = !isScan && line.Contains("\"event\":\"SAAScanComplete\"");
                            if (!isScan && !isMap) continue;

                            string? name = ExtractBodyName(line);
                            if (name == null) continue;
                            if (isScan) discovered.Add(name); else mapped.Add(name);
                        }
                    }
                    catch (Exception ex) { Log.Write($"DiscoveryIndex: skipped {Path.GetFileName(file)} — {ex.Message}"); }
                }

                lock (_lock)
                {
                    _discoveredByMe.UnionWith(discovered);
                    _mappedByMe.UnionWith(mapped);
                    IsReady = true;
                    Version++;
                }
                Log.Write($"DiscoveryIndex: built in {sw.ElapsedMilliseconds}ms — {discovered.Count} discovered by you, {mapped.Count} mapped by you");
            }
            catch (Exception ex) { Log.Write($"DiscoveryIndex.BuildAsync error: {ex.Message}"); }
        });

        // Journal lines are one flat JSON object; pulling the one field we need with IndexOf is
        // far cheaper than parsing the whole thing, and BodyName never contains an escaped quote
        // in practice (body names are plain text like "Qiedea LF-G b30-1 2").
        private static string? ExtractBodyName(string line)
        {
            const string key = "\"BodyName\":\"";
            int i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            int j = line.IndexOf('"', i);
            return j > i ? line.Substring(i, j - i) : null;
        }
    }
}
