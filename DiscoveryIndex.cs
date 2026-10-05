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

        // Footfall: a Scan's WasFootfalled=true only says "someone had already walked here before
        // this scan" - and once you walk there yourself, every later scan says true too. So, same
        // idea as discovery: remember what the FIRST Scan of each body you ever made said, plus
        // whether you have stepped onto it on foot since.
        //   first scan said true                       -> another commander was there before you ever saw it
        //   first scan said false, you never walked it -> someone else got there before your next scan
        //   first scan said false, you did walk it     -> the footfall was yours
        private static readonly Dictionary<string, bool> _firstScanFootfalled = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _walkedByMe = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsFootfalledByOther(string bodyName)
        {
            lock (_lock)
            {
                if (!_firstScanFootfalled.TryGetValue(bodyName, out var firstTrue)) return false;
                return firstTrue || !_walkedByMe.Contains(bodyName);
            }
        }

        // Called for every scan (history pass and live). Only the first one per body is kept.
        public static void NoteScanFootfall(string bodyName, bool? wasFootfalled)
        {
            if (string.IsNullOrEmpty(bodyName) || !wasFootfalled.HasValue) return;
            lock (_lock) { if (_firstScanFootfalled.TryAdd(bodyName, wasFootfalled.Value)) Version++; }
        }

        // An on-foot Disembark onto a planet (not SRV, not a taxi).
        public static void MarkWalkedByMe(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (_lock) { if (_walkedByMe.Add(bodyName)) Version++; }
        }

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
                var firstFoot  = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var walked     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Oldest first, so "the first scan of each body" really is the first.
                foreach (var file in Directory.GetFiles(journalDir, "Journal.*.log").OrderBy(File.GetLastWriteTimeUtc))
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
                            bool isScanEv = line.Contains("\"event\":\"Scan\"");
                            bool isScan = isScanEv && line.Contains("\"WasDiscovered\":false");
                            bool isMap  = !isScan && line.Contains("\"event\":\"SAAScanComplete\"");
                            bool isFootScan = isScanEv && line.Contains("\"WasFootfalled\":");
                            bool isWalk = !isScanEv && line.Contains("\"event\":\"Disembark\"") &&
                                line.Contains("\"SRV\":false") && line.Contains("\"Taxi\":false") && line.Contains("\"OnPlanet\":true");
                            if (!isScan && !isMap && !isFootScan && !isWalk) continue;

                            if (isWalk)
                            {
                                string? walkBody = ExtractField(line, "\"Body\":\"");
                                if (walkBody != null) walked.Add(walkBody);
                                continue;
                            }

                            string? name = ExtractBodyName(line);
                            if (name == null) continue;
                            if (isFootScan) firstFoot.TryAdd(name, line.Contains("\"WasFootfalled\":true"));
                            if (isScan) discovered.Add(name); else if (isMap) mapped.Add(name);
                        }
                    }
                    catch (Exception ex) { Log.Write($"DiscoveryIndex: skipped {Path.GetFileName(file)} — {ex.Message}"); }
                }

                lock (_lock)
                {
                    _discoveredByMe.UnionWith(discovered);
                    _mappedByMe.UnionWith(mapped);
                    // The history pass is oldest-first and complete, so it is authoritative: it must overwrite anything the
                    // live/backfill scan handler recorded while this pass was still running (those can be later scans).
                    foreach (var kv in firstFoot) _firstScanFootfalled[kv.Key] = kv.Value;
                    _walkedByMe.UnionWith(walked);
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
        private static string? ExtractField(string line, string key)
        {
            int i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            int j = line.IndexOf('"', i);
            return j > i ? line.Substring(i, j - i) : null;
        }

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
