using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EliteBioRadar
{
    public class TripData
    {
        public DateTime TripStartUtc { get; set; } = DateTime.UtcNow;
        // Keyed by body name, value = current best estimated value for that body — replaced,
        // not accumulated, so a later re-scan (e.g. a mapped-bonus update) corrects the same
        // body's entry instead of double-counting it. The trip total is just this dict's sum.
        public Dictionary<string, double> BodyValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Resettable, cross-system "how much has this exploration trip earned so far" counter —
    // separate from EarningsTracker's lifetime/session totals (which track BIO organism sales
    // specifically), and separate from the per-system System Scan window's own per-body
    // estimates (this is what accumulates those estimates over a whole trip). "Start New Trip"
    // just wipes BodyValues and stamps a fresh start time — real bodies scanned after that point
    // repopulate it from zero.
    public static class TripTracker
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.trip.json");

        private static readonly object _lock = new();
        private static TripData _data = new();
        private static bool _loaded;

        public static TripData Load()
        {
            lock (_lock)
            {
                if (_loaded) return _data;
                try
                {
                    if (File.Exists(_path))
                    {
                        var json = File.ReadAllText(_path);
                        _data = JsonConvert.DeserializeObject<TripData>(json) ?? new TripData();
                    }
                }
                catch (Exception ex) { Log.Write($"TripTracker.Load error: {ex.Message}"); }
                _loaded = true;
                return _data;
            }
        }

        public static DateTime TripStartUtc { get { Load(); lock (_lock) return _data.TripStartUtc; } }
        public static double   TripTotal    { get { Load(); lock (_lock) return _data.BodyValues.Values.Sum(); } }
        public static int      TripBodyCount{ get { Load(); lock (_lock) return _data.BodyValues.Count; } }

        public static void RecordScanValue(string bodyName, double estimatedValue)
        {
            if (string.IsNullOrEmpty(bodyName) || estimatedValue <= 0) return;
            Load();
            lock (_lock)
            {
                _data.BodyValues[bodyName] = estimatedValue;
                Save();
            }
        }

        // Merges historical scan values into the current trip. Same replace-by-body-name rule as
        // RecordScanValue, so a body already counted is corrected rather than doubled. If the
        // imported data reaches back before the trip's recorded start, the start moves back too
        // so the bar's "started ..." time stays truthful.
        public static void ImportScanValues(Dictionary<string, double> values, DateTime? earliestUtc)
        {
            Load();
            lock (_lock)
            {
                foreach (var kv in values)
                    if (kv.Value > 0) _data.BodyValues[kv.Key] = kv.Value;
                if (earliestUtc.HasValue && earliestUtc.Value < _data.TripStartUtc)
                    _data.TripStartUtc = earliestUtc.Value;
                Save();
                Log.Write($"TripTracker: imported {values.Count} bodies from journal history");
            }
        }

        public static void StartNewTrip()
        {
            lock (_lock)
            {
                _data = new TripData { TripStartUtc = DateTime.UtcNow };
                _loaded = true;
                Save();
                Log.Write("TripTracker: new trip started");
            }
        }

        private static void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                AtomicFile.WriteAllText(_path, json);
            }
            catch (Exception ex) { Log.Write($"TripTracker.Save error: {ex.Message}"); }
        }
    }
}
