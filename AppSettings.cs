using System;
using System.IO;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    public class AppSettingsData
    {
        public bool   ShowSidebar          { get; set; } = false;
        public bool   AutoScale            { get; set; } = false;
        public double DefaultScale         { get; set; } = 1000;
        // Name kept for settings-file compatibility; now just means "show BIO Sites sidebar".
        public bool   KeepPlanetPanelOpen  { get; set; } = false;
        public bool   RadarAnimation       { get; set; } = true;
        public bool   ShowGeologicalSites  { get; set; } = false;
        public double ShipDepartureRangeMetres { get; set; } = 1975;

        // Flags a body's surface gravity as dangerous on the Planet tab and System Scan window
        // once it's at or above the player's own chosen threshold — off by default since "high"
        // is subjective to ship/SRV loadout and playstyle, not something this app should assume.
        public bool   GravityWarningEnabled    { get; set; } = false;
        public double GravityWarningThresholdG { get; set; } = 2.0;

        // Converts Elite's own huge, uncompressed .bmp screenshots to .png and deletes the
        // original once the .png is confirmed written. Off by default — needs a real source
        // folder, which nothing in the journal ever reports (its own Screenshot event's
        // Filename is relative to wherever the game is actually configured to save them).
        // ScreenshotDestFolder empty means "same as source".
        public bool   ScreenshotConversionEnabled { get; set; } = false;
        public string ScreenshotSourceFolder      { get; set; } = "";
        public string ScreenshotDestFolder        { get; set; } = "";

        // EDSM integration — off by default; no network call of any kind happens until the
        // user explicitly opts in via the Settings panel. ApiKey is stored in plain text in
        // this local settings file, same trust boundary as every other local-only setting here
        // (there's no OS-credential-vault integration in this app) — acceptable for a desktop
        // companion app, but worth keeping in mind if this file is ever shared/backed up.
        public bool   EdsmEnabled          { get; set; } = false;
        public string EdsmCommanderName    { get; set; } = "";
        public string EdsmApiKey           { get; set; } = "";
        // Watermark for the EDSM catch-up backfill — null means "never synced", which the
        // backfill treats as a bounded recent window rather than the entire journal history.
        // Updated after each backfill run (manual or automatic) that actually completes.
        public DateTime? LastEdsmSyncUtc   { get; set; } = null;

        // Window position and size — null means "use OS default"
        public double? WindowLeft   { get; set; } = null;
        public double? WindowTop    { get; set; } = null;
        public double? WindowWidth  { get; set; } = null;
        public double? WindowHeight { get; set; } = null;

        // System Scan window — same "remember placement" convention as the main window above,
        // plus its two view-toggle buttons.
        public double? SystemScanLeft   { get; set; } = null;
        public double? SystemScanTop    { get; set; } = null;
        public double? SystemScanWidth  { get; set; } = null;
        public double? SystemScanHeight { get; set; } = null;
        public bool    SystemScanShowAllBodies { get; set; } = false;
        public bool    SystemScanHideBelts     { get; set; } = false;
        public bool    SystemScanHideFullMaterials { get; set; } = false;

        // Star Finder window — same "remember placement" convention, but no "was open" flag:
        // unlike Scan Log/System Scan this isn't a live monitor of the current system, just a
        // one-off lookup tool, so it doesn't reopen automatically on the next launch.
        public double? StarFinderLeft   { get; set; } = null;
        public double? StarFinderTop    { get; set; } = null;
        public double? StarFinderWidth  { get; set; } = null;
        public double? StarFinderHeight { get; set; } = null;

        // Whether these pop-out windows were open when the app last shut down — reopened
        // automatically on the next launch (see MainWindow's OpenScanLogWindow/
        // OpenSystemScanWindow). Set true right after Show(), set back false in each window's
        // own Closed handler — so a normal close (not just an app-wide shutdown) "sticks" too,
        // matching how a user would expect "remember what was open" to behave.
        public bool    ScanLogWasOpen     { get; set; } = false;
        public bool    SystemScanWasOpen  { get; set; } = false;
    }

    public static class AppSettings
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.settings.json");

        public static AppSettingsData Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    return JsonConvert.DeserializeObject<AppSettingsData>(json)
                        ?? new AppSettingsData();
                }
            }
            catch (Exception ex) { Log.Write($"AppSettings.Load error: {ex.Message}"); }
            return new AppSettingsData();
        }

        public static void Save(AppSettingsData settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                AtomicFile.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"AppSettings.Save error: {ex.Message}"); }
        }
    }
}
