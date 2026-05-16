using System;
using System.IO;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    public class EarningsData
    {
        public long TotalEarned   { get; set; } = 0;
        public long SessionEarned { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public static class EarningsTracker
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.earnings.json");

        private static readonly object _lock = new();
        private static EarningsData _data = new();

        // ---------------------------------------------------------------
        public static EarningsData Load()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_path))
                    {
                        var json = File.ReadAllText(_path);
                        _data = JsonConvert.DeserializeObject<EarningsData>(json) ?? new EarningsData();
                    }
                    return _data;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"EarningsTracker.Load error: {ex.Message}");
                return _data;
            }
        }

        public static long TotalEarned  => _data.TotalEarned;
        public static long SessionEarned => _data.SessionEarned;

        // ---------------------------------------------------------------
        public static void AddEarning(long amount)
        {
            lock (_lock)
            {
                _data.TotalEarned   += amount;
                _data.SessionEarned += amount;
                _data.LastUpdated    = DateTime.UtcNow;
                Save();
                Log.Write($"Earnings: +{PayoutData.FormatCredits(amount)} | Session: {PayoutData.FormatCredits(_data.SessionEarned)} | Total: {PayoutData.FormatCredits(_data.TotalEarned)}");
            }
        }

        // ---------------------------------------------------------------
        public static void Clear()
        {
            lock (_lock)
            {
                _data = new EarningsData();
                Save();
                Log.Write("Earnings cleared");
            }
        }

        // ---------------------------------------------------------------
        private static void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"EarningsTracker.Save error: {ex.Message}"); }
        }
    }
}
