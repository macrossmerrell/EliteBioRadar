using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    // Elite Dangerous saves screenshots as huge, uncompressed .bmp files with no way to
    // configure that from the journal side — this converts them to .png (WPF's own
    // BmpBitmapDecoder/PngBitmapEncoder, no external dependency needed) and deletes the
    // original .bmp once the .png is confirmed written. Off by default; needs a real source
    // folder configured in Settings (the journal's own Screenshot event only ever reports a
    // relative in-game path, never the real folder on disk).
    public static class ScreenshotConverterService
    {
        // Fired after a LIVE conversion completes (png filename) — MainWindow subscribes to
        // show a brief toast. Deliberately not raised from ConvertAllInFolder's own bulk
        // catch-up path (Settings already shows its own summary text there; a toast per file
        // would just be spam for a whole backlog).
        public static event Action<string>? Converted;

        // Called for every live "Screenshot" journal event — no-ops entirely unless the user
        // has opted in and configured a source folder, same self-gating convention as
        // EdsmService.SubmitJournalEventFireAndForget.
        public static void OnScreenshotEventFireAndForget(JObject obj)
        {
            var settings = AppSettings.Load();
            if (!settings.ScreenshotConversionEnabled || string.IsNullOrWhiteSpace(settings.ScreenshotSourceFolder))
                return;

            // The journal's own path ("\ED_Pictures\Screenshot_0008.bmp") is relative to
            // wherever the game is actually configured to save screenshots, which isn't
            // reported anywhere in the journal — only the filename itself is trustworthy.
            var rawFilename = obj.Value<string>("Filename") ?? "";
            var fileName = Path.GetFileName(rawFilename.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName)) return;

            var sourceFolder = settings.ScreenshotSourceFolder;
            var destFolder = string.IsNullOrWhiteSpace(settings.ScreenshotDestFolder) ? sourceFolder : settings.ScreenshotDestFolder;

            _ = Task.Run(async () =>
            {
                try
                {
                    var bmpPath = Path.Combine(sourceFolder, fileName);
                    // The game can still be finishing the write when this event fires — real
                    // confirmed behavior: the file can take a couple of real seconds to actually
                    // appear after the event fires, not just a brief write-lock window. Wait up
                    // to 15s (60 x 250ms) for it to exist and be readable rather than failing
                    // once and never trying again.
                    const int maxAttempts = 60;
                    for (int i = 0; i < maxAttempts && !(File.Exists(bmpPath) && IsFileReady(bmpPath)); i++)
                        await Task.Delay(250);

                    if (!File.Exists(bmpPath))
                    {
                        Log.Write($"ScreenshotConverter: '{bmpPath}' never appeared, skipping");
                        return;
                    }
                    ConvertAndDelete(bmpPath, destFolder, notify: true);
                }
                catch (Exception ex) { Log.Write($"ScreenshotConverter error: {ex.Message}"); }
            });
        }

        private static bool IsFileReady(string path)
        {
            try { using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
            catch (IOException) { return false; }
        }

        // Converts one .bmp to .png in destFolder, then deletes the original .bmp — but only
        // once the .png is confirmed on disk with real content, never before.
        public static void ConvertAndDelete(string bmpPath, string destFolder, bool notify = false)
        {
            Directory.CreateDirectory(destFolder);
            var pngPath = Path.Combine(destFolder, Path.GetFileNameWithoutExtension(bmpPath) + ".png");
            using (var inFs = new FileStream(bmpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new BmpBitmapDecoder(inFs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                using var outFs = new FileStream(pngPath, FileMode.Create, FileAccess.Write);
                encoder.Save(outFs);
            }

            if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 0)
            {
                File.Delete(bmpPath);
                Log.Write($"ScreenshotConverter: converted '{Path.GetFileName(bmpPath)}' -> '{Path.GetFileName(pngPath)}'");
                if (notify) Converted?.Invoke(Path.GetFileName(pngPath));
            }
            else
            {
                Log.Write($"ScreenshotConverter: '{pngPath}' missing or empty after save, leaving original .bmp in place");
            }
        }

        // Manual "catch up on the backlog" — scans sourceFolder for every .bmp file (not just
        // ones that arrived via a live Screenshot event, e.g. from before this feature was
        // enabled) and converts each one.
        public static (int converted, int failed) ConvertAllInFolder(string sourceFolder, string destFolder)
        {
            int converted = 0, failed = 0;
            if (!Directory.Exists(sourceFolder)) return (0, 0);
            var actualDest = string.IsNullOrWhiteSpace(destFolder) ? sourceFolder : destFolder;
            foreach (var bmp in Directory.GetFiles(sourceFolder, "*.bmp"))
            {
                try { ConvertAndDelete(bmp, actualDest); converted++; }
                catch (Exception ex)
                {
                    Log.Write($"ScreenshotConverter: failed to convert '{bmp}': {ex.Message}");
                    failed++;
                }
            }
            return (converted, failed);
        }
    }
}
