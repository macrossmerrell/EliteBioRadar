using System.IO;

namespace EliteBioRadar
{
    // Atomic file write via write-to-temp-then-rename. Real bug this fixed: every persisted
    // file in this app used a plain File.WriteAllText straight to its real path — if the
    // process was killed mid-write (a hard "taskkill /F", a crash, a Windows shutdown), that
    // leaves the file truncated/corrupt. Confirmed as the actual cause of a real report ("Scan
    // Log lost all my past data"): ScanCache.ReadAll() silently returned an empty dictionary on
    // any parse failure with zero logging, so a single corrupted write went completely
    // unnoticed — and the very next save then overwrote the file with just what had
    // accumulated since, permanently losing months of real history with no trace in the log at
    // all. File.Replace/Move only complete once the temp file has been fully and successfully
    // written, so a kill mid-write leaves the ORIGINAL file exactly as it was instead of
    // corrupting it — the worst case becomes "lost this one save", never "lost everything".
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
