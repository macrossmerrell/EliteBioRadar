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
        public bool   PlanetOverlay        { get; set; } = false;
        public bool   KeepPlanetPanelOpen  { get; set; } = false;
        public bool   RadarAnimation       { get; set; } = true;
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
                File.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"AppSettings.Save error: {ex.Message}"); }
        }
    }
}
