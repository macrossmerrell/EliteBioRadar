using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EliteBioRadar
{
    public class StatusUpdatedEventArgs : EventArgs { public EliteStatus Status { get; set; } = new(); }
    public class OrganismScannedEventArgs : EventArgs { public ScannedOrganism Organism { get; set; } = new(); }
    public class BodyChangedEventArgs : EventArgs { public string BodyName { get; set; } = ""; public int BioCount { get; set; } public int GeoCount { get; set; } }

    public class EliteWatcherService : IDisposable
    {
        public event EventHandler<StatusUpdatedEventArgs>?   StatusUpdated;
        public event EventHandler<OrganismScannedEventArgs>? OrganismScanned;
        public event EventHandler<BodyChangedEventArgs>?     BodyChanged;

        public EliteStatus CurrentStatus { get; private set; } = new();
        public string CurrentBody    { get; private set; } = "";
        public event EventHandler? PlanetListChanged;
        public string StarSystem        { get; private set; } = "";
        public string CachedBodyName    { get; private set; } = "";
        public bool   WasFootfalled     { get; private set; } = false;
        private bool  _pendingFirstFootfall     = false;
        private string _pendingFirstFootfallBody = "";
        private string _backfillSystem  = "";  // correct system derived during backfill
        private bool  _bodySetByStatus  = false;

        private readonly object _planetLock = new();

        public static string GetShortBodyName(string fullBodyName, string starSystem)
        {
            if (string.IsNullOrEmpty(fullBodyName)) return fullBodyName;
            // Strip star system prefix (e.g. "Hypheerld AC-P c8-10 5 c" → "5 c")
            if (!string.IsNullOrEmpty(starSystem) &&
                fullBodyName.StartsWith(starSystem, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = fullBodyName.Substring(starSystem.Length).Trim();
                return string.IsNullOrEmpty(suffix) ? fullBodyName : suffix;
            }
            return fullBodyName;
        }

        public void ClearCurrentBody()
        {
            CurrentBody               = "";
            _bodySetByStatus          = false;
            WasFootfalled             = false;
            _pendingFirstFootfall     = false;
            _pendingFirstFootfallBody = "";
            lock (ScannedOrganisms) ScannedOrganisms.Clear();
            lock (KnownGenera)      KnownGenera.Clear();
            lock (CompletedGenera)  CompletedGenera.Clear();
            lock (KnownGeoSites)    KnownGeoSites.Clear();
            BiologyCount = 0;
            GeologyCount = 0;
        }
        public int    BiologyCount  { get; private set; }
        public int    GeologyCount  { get; private set; }
        public string TargetedBody  { get; private set; } = "";
        public int    TargetedBodyBioCount { get; private set; }

        // Per-body biology signal counts for current system — populated from FSS/DSS scans
        // Key = short body name (e.g. "5 c"), Value = (BioCount, BodyName full)
        public class PlanetBioInfo
        {
            public string FullBodyName   { get; set; } = "";
            public string ShortName      { get; set; } = "";
            public int    BioCount       { get; set; }
            public int    CompletedCount { get; set; }
        }

        public class PlanetGeoInfo
        {
            public string FullBodyName   { get; set; } = "";
            public string ShortName      { get; set; } = "";
            public int    GeoCount       { get; set; }
            public int    DiscoveredCount { get; set; } // unique CodexEntry types found
        }

        public readonly List<PlanetBioInfo> SystemBioPlanets = new();
        public readonly List<PlanetGeoInfo> SystemGeoPlanets = new();

        private readonly Dictionary<string, int> _bodyBioSignals =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Genus names known from detailed planet scan (SAASignalsFound Genuses array)
        public List<string>          KnownGenera     { get; } = new();
        public List<ScannedOrganism> ScannedOrganisms { get; } = new();
        // Genera fully logged (3rd scan complete) — kept for sidebar display until body changes
        public List<ScannedOrganism> CompletedGenera  { get; } = new();
        // Geological sites discovered via CodexEntry on current body
        public List<ScannedGeoSite>  KnownGeoSites    { get; } = new();

        private readonly string _journalDir;
        private readonly string _statusFile;
        private string _currentJournalFile = "";
        private long   _journalPosition;
        private DateTime _lastStatusModified = DateTime.MinValue;
        private readonly CancellationTokenSource _cts = new();

        public EliteWatcherService(string journalDir)
        {
            _journalDir = journalDir;
            _statusFile = Path.Combine(journalDir, "Status.json");
        }

        // ---------------------------------------------------------------
        private readonly ManualResetEventSlim _statusReady = new ManualResetEventSlim(false);

        public void Start()
        {
            Log.Write("EliteWatcherService.Start() called");

            // Immediately load the last active body from cache — works even if the game
            // isn't running yet. The ship hasn't moved since last session.
            var (cachedBody, cachedData) = ScanCache.LoadLastActiveBody();
            if (!string.IsNullOrEmpty(cachedBody))
            {
                CurrentBody    = cachedBody;
                CachedBodyName = cachedBody;

                // Only load COMPLETED organisms from cache — incomplete ones will be
                // rebuilt correctly from journal backfill to ensure correct colour/state
                var completedOnly = cachedData.Organisms.Where(o => o.IsComplete).ToList();
                lock (ScannedOrganisms)
                {
                    ScannedOrganisms.Clear();
                    ScannedOrganisms.AddRange(completedOnly);
                }
                // Populate CompletedGenera so sidebar Total Payout shows correctly after restart
                lock (CompletedGenera)
                {
                    CompletedGenera.Clear();
                    foreach (var o in completedOnly.GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                        CompletedGenera.Add(o);
                }
                BiologyCount  = cachedData.BiologyCount;
                GeologyCount  = cachedData.GeologyCount;
                WasFootfalled = cachedData.WasFootfalled;
                lock (KnownGenera)
                {
                    KnownGenera.Clear();
                    KnownGenera.AddRange(cachedData.KnownGenera);
                }
                lock (KnownGeoSites)
                {
                    KnownGeoSites.Clear();
                    KnownGeoSites.AddRange(cachedData.GeoSites);
                }
                lock (_bodyBioSignals)
                    if (cachedData.BiologyCount > 0)
                        _bodyBioSignals[cachedBody] = cachedData.BiologyCount;
                Log.Write($"Start: loaded {completedOnly.Count} completed organisms for '{cachedBody}' (incomplete will rebuild from journal)");
            }

            // Determine the current system from recent journals BEFORE JournalLoop starts.
            // This ensures BackfillJournal can correctly reject cached bodies from other systems.
            try
            {
                var recentJournals = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByDescending(f => f).Take(10).ToArray();
                foreach (var jf in recentJournals)
                {
                    var lines = SafeReadAllLines(jf);
                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        var o = TryParse(lines[i]); if (o == null) continue;
                        var ev = o.Value<string>("event");
                        if (ev == "FSDJump" || ev == "CarrierJump" || ev == "Location")
                        {
                            var sys = o.Value<string>("StarSystem") ?? "";
                            if (!string.IsNullOrEmpty(sys))
                            {
                                StarSystem = sys;
                                Log.Write($"Start: current system from journals='{StarSystem}'");
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(StarSystem)) break;
                }
            }
            catch (Exception ex) { Log.Write($"Start: failed to read current system from journals: {ex.Message}"); }

            Task.Run(() => StatusPollLoop(_cts.Token));
            Task.Run(() => JournalLoop(_cts.Token));
            Log.Write("EliteWatcherService.Start() returning");
        }

        // ---------------------------------------------------------------
        // Poll Status.json every 300ms — Elite writes it every ~250ms anyway
        // Initial 500ms delay staggers us off the Stream Deck plugin's polling tick
        private void StatusPollLoop(CancellationToken ct)
        {
            Log.Write("StatusPollLoop started — waiting 500ms to stagger off Stream Deck tick");
            Thread.Sleep(500);
            Log.Write("StatusPollLoop: stagger delay done, starting poll");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(_statusFile))
                    {
                        var modified = File.GetLastWriteTimeUtc(_statusFile);
                        if (modified != _lastStatusModified)
                        {
                            _lastStatusModified = modified;
                            ReadStatus();
                            // Signal journal loop that we have a valid status reading
                            if (CurrentStatus.HasPosition && !_statusReady.IsSet)
                            {
                                Log.Write("StatusPollLoop: position confirmed, signalling journal loop");
                                _statusReady.Set();
                            }
                        }
                    }
                }
                catch { }
                Thread.Sleep(300);
            }
        }

        // ---------------------------------------------------------------
        private void ReadStatus()
        {
            try
            {
                if (!File.Exists(_statusFile)) return;

                string json;
                using (var fs = new FileStream(_statusFile, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                    json = sr.ReadToEnd();

                if (string.IsNullOrWhiteSpace(json)) return;

                var obj = JObject.Parse(json);
                var flagsRaw  = obj.Value<long?>("Flags")  ?? 0;
                var flags2Raw = obj.Value<long?>("Flags2") ?? 0;

                var status = new EliteStatus
                {
                    Flags        = (uint)(flagsRaw  & 0xFFFFFFFF),
                    Flags2       = (uint)(flags2Raw & 0xFFFFFFFF),
                    Latitude     = obj.Value<double?>("Latitude")     ?? 0,
                    Longitude    = obj.Value<double?>("Longitude")    ?? 0,
                    Altitude     = obj.Value<double?>("Altitude")     ?? 0,
                    Heading      = obj.Value<double?>("Heading")      ?? 0,
                    BodyName     = obj.Value<string>("BodyName")      ?? "",
                    PlanetRadius = obj.Value<double?>("PlanetRadius") ?? 0,
                };

                CurrentStatus = status;

                // Track targeted/destination body for bio count display
                var dest = obj.Value<string>("BodyName") ?? "";
                // If we have a destination from the nav panel, use it
                var destObj = obj["Destination"];
                if (destObj != null)
                {
                    var destName = destObj.Value<string>("Name") ?? "";
                    if (!string.IsNullOrEmpty(destName))
                    {
                        bool nameChanged = destName != TargetedBody;
                        TargetedBody = destName;

                        // Look up bio count — prefer SystemBioPlanets (backfilled) over _bodyBioSignals
                        int bioCount = 0;
                        lock (_planetLock)
                        {
                            var tp = SystemBioPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, destName, StringComparison.OrdinalIgnoreCase));
                            if (tp != null) bioCount = tp.BioCount;
                        }
                        if (bioCount == 0)
                            lock (_bodyBioSignals)
                                _bodyBioSignals.TryGetValue(destName, out bioCount);

                        // Look up geo count from SystemGeoPlanets
                        int geoCount = 0;
                        lock (_planetLock)
                        {
                            var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, destName, StringComparison.OrdinalIgnoreCase));
                            if (gp != null) geoCount = gp.GeoCount;
                        }

                        bool bioChanged = bioCount != TargetedBodyBioCount;
                        TargetedBodyBioCount = bioCount;

                        if (nameChanged || (bioChanged && bioCount > 0) || (nameChanged && geoCount > 0))
                        {
                            Log.Write($"Targeting: {destName} bio={TargetedBodyBioCount} geo={geoCount}");
                            if (string.IsNullOrEmpty(CurrentBody) && (bioCount > 0 || geoCount > 0))
                            {
                                BiologyCount = bioCount;
                                GeologyCount = geoCount;

                                // Load cache data for this body so sidebar shows known genera + completed scans
                                var cached = ScanCache.LoadForBody(destName);
                                lock (KnownGenera)
                                {
                                    KnownGenera.Clear();
                                    foreach (var g in cached.KnownGenera)
                                        KnownGenera.Add(g);
                                }
                                lock (ScannedOrganisms)
                                {
                                    ScannedOrganisms.Clear();
                                    foreach (var o in cached.Organisms)
                                        ScannedOrganisms.Add(o);
                                }
                                // Populate CompletedGenera so sidebar Total Payout shows correctly
                                lock (CompletedGenera)
                                {
                                    CompletedGenera.Clear();
                                    foreach (var o in cached.Organisms.Where(o => o.IsComplete)
                                                         .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                                         .Select(g => g.First()))
                                        CompletedGenera.Add(o);
                                }
                                lock (KnownGeoSites)
                                {
                                    KnownGeoSites.Clear();
                                    foreach (var g in cached.GeoSites)
                                        KnownGeoSites.Add(g);
                                }
                                WasFootfalled = cached.WasFootfalled;

                                BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                    { BodyName = destName, BioCount = bioCount });
                            }
                            else
                                StatusUpdated?.Invoke(this, new StatusUpdatedEventArgs { Status = status });
                        }
                    }
                }

                if (!string.IsNullOrEmpty(status.BodyName) && status.BodyName != CurrentBody)
                {
                    // Approaching or landed on a new body — load its cache
                    CurrentBody      = status.BodyName;
                    _bodySetByStatus = true;
                    WasFootfalled    = false;
                    // Only clear pending FF if we've moved to a different body than the one being scanned
                    if (!string.Equals(status.BodyName, _pendingFirstFootfallBody, StringComparison.OrdinalIgnoreCase))
                        _pendingFirstFootfall = false;
                        _pendingFirstFootfallBody = "";
                    lock (ScannedOrganisms)
                    {
                        ScannedOrganisms.Clear();
                        var loaded = ScanCache.LoadForBody(CurrentBody);
                        ScannedOrganisms.AddRange(loaded.Organisms);
                        BiologyCount = loaded.BiologyCount > 0 ? loaded.BiologyCount : BiologyCount;
                        GeologyCount = loaded.GeologyCount > 0 ? loaded.GeologyCount : GeologyCount;
                        if (loaded.KnownGenera.Count > 0)
                            lock (KnownGenera) { KnownGenera.Clear(); KnownGenera.AddRange(loaded.KnownGenera); }
                        if (loaded.GeoSites.Count > 0)
                            lock (KnownGeoSites) { KnownGeoSites.Clear(); KnownGeoSites.AddRange(loaded.GeoSites); }
                        // Populate CompletedGenera so sidebar Total Payout shows correctly
                        lock (CompletedGenera)
                        {
                            CompletedGenera.Clear();
                            foreach (var o in loaded.Organisms.Where(o => o.IsComplete)
                                                 .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                                 .Select(g => g.First()))
                                CompletedGenera.Add(o);
                        }
                    }
                    BodyChanged?.Invoke(this, new BodyChangedEventArgs
                        { BodyName = CurrentBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                }
                else if (string.IsNullOrEmpty(status.BodyName) && !string.IsNullOrEmpty(CurrentBody))
                {
                    // Empty BodyName in status — either game not running, or genuinely in space.
                    // Only clear if we previously got CurrentBody FROM status (not from backfill/journal).
                    // If _bodySetByStatus is false, backfill set it — don't wipe it.
                    if (_bodySetByStatus)
                    {
                        CurrentBody = "";
                        _bodySetByStatus = false;
                        lock (ScannedOrganisms) ScannedOrganisms.Clear();
                        lock (KnownGenera)      KnownGenera.Clear();
                        lock (CompletedGenera)  CompletedGenera.Clear();
                        BiologyCount = 0;
                        GeologyCount = 0;
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                }

                StatusUpdated?.Invoke(this, new StatusUpdatedEventArgs { Status = status });
            }
            catch { }
        }

        // Called when user clicks a planet in the Biological Sites panel while on a different body
        // Loads that planet's cache data into display state temporarily
        public void PreviewPlanet(string fullBodyName)
        {
            if (string.Equals(fullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase))
                return;

            var cached = ScanCache.LoadForBody(fullBodyName);
            lock (KnownGenera)      { KnownGenera.Clear();      foreach (var g in cached.KnownGenera)  KnownGenera.Add(g); }
            lock (ScannedOrganisms) { ScannedOrganisms.Clear(); foreach (var o in cached.Organisms)    ScannedOrganisms.Add(o); }
            lock (KnownGeoSites)    { KnownGeoSites.Clear();    foreach (var g in cached.GeoSites)     KnownGeoSites.Add(g); }
            // Populate CompletedGenera so sidebar Total Payout shows correctly for previewed planet
            lock (CompletedGenera)
            {
                CompletedGenera.Clear();
                foreach (var o in cached.Organisms.Where(o => o.IsComplete)
                                     .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                     .Select(g => g.First()))
                    CompletedGenera.Add(o);
            }

            var bioPlanet = SystemBioPlanets.FirstOrDefault(p =>
                string.Equals(p.FullBodyName, fullBodyName, StringComparison.OrdinalIgnoreCase));
            BiologyCount = bioPlanet?.BioCount ?? cached.BiologyCount;

            var geoPlanet = SystemGeoPlanets.FirstOrDefault(p =>
                string.Equals(p.FullBodyName, fullBodyName, StringComparison.OrdinalIgnoreCase));
            GeologyCount = geoPlanet?.GeoCount ?? cached.GeologyCount;

            WasFootfalled = cached.WasFootfalled;

            Log.Write($"PreviewPlanet: '{fullBodyName}' genera={KnownGenera.Count} scans={ScannedOrganisms.Count} geo={KnownGeoSites.Count}");
            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                { BodyName = fullBodyName, BioCount = BiologyCount, GeoCount = GeologyCount });
        }

        // ---------------------------------------------------------------
        private void JournalLoop(CancellationToken ct)
        {
            Log.Write("JournalLoop started — waiting for status position...");
            // Wait up to 10 seconds for Status.json to give us a valid position
            // before processing any journal events, so ScanOrganic has real coords
            _statusReady.Wait(TimeSpan.FromSeconds(10), ct);
            if (!_statusReady.IsSet)
                Log.Write("JournalLoop: status timeout — proceeding without confirmed position");
            else
                Log.Write("JournalLoop: status ready, starting journal processing");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var latest = Directory.GetFiles(_journalDir, "Journal.*.log")
                        .OrderByDescending(f => f)
                        .FirstOrDefault();

                    if (latest == null) { Thread.Sleep(2000); continue; }

                    if (latest != _currentJournalFile)
                    {
                        Log.Write($"JournalLoop: new file {Path.GetFileName(latest)}");
                        _currentJournalFile = latest;
                        Log.Write("JournalLoop: starting backfill...");

                        // Remember the system we already know before backfill potentially
                        // corrupts _backfillSystem via the cached-body fallback path
                        string knownSystemBeforeBackfill = StarSystem;

                        BackfillJournal(latest);
                        Log.Write($"JournalLoop: backfill done, {ScannedOrganisms.Count} organisms loaded");

                        // Prefer the system we knew before backfill if it gets corrupted
                        string systemForPlanets = _backfillSystem;
                        if (!string.IsNullOrEmpty(knownSystemBeforeBackfill) &&
                            !string.Equals(knownSystemBeforeBackfill, systemForPlanets, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Write($"JournalLoop: backfill set system to '{systemForPlanets}' but known system was '{knownSystemBeforeBackfill}' — keeping known system");
                            systemForPlanets = knownSystemBeforeBackfill;
                        }
                        if (string.IsNullOrEmpty(systemForPlanets))
                        {
                            foreach (var jf in Directory.GetFiles(_journalDir, "Journal.*.log")
                                .OrderByDescending(f => f).Take(10))
                            {
                                foreach (var line in SafeReadAllLines(jf).AsEnumerable().Reverse())
                                {
                                    var o = TryParse(line); if (o == null) continue;
                                    var ev = o.Value<string>("event");
                                    if (ev == "FSDJump" || ev == "CarrierJump" || ev == "Location")
                                    {
                                        systemForPlanets = o.Value<string>("StarSystem") ?? "";
                                        if (!string.IsNullOrEmpty(systemForPlanets)) break;
                                    }
                                }
                                if (!string.IsNullOrEmpty(systemForPlanets)) break;
                            }
                        }

                        if (!string.IsNullOrEmpty(systemForPlanets))
                        {
                            StarSystem = systemForPlanets;
                            BackfillSystemPlanets(systemForPlanets);
                        }
                        else
                            Log.Write("JournalLoop: no system found in journals, skipping BackfillSystemPlanets");

                        // Set position to END of file so the tail only picks up new events
                        // Never replay historical lines as live events
                        using (var fs2 = new FileStream(latest, FileMode.Open, FileAccess.Read,
                                   FileShare.ReadWrite | FileShare.Delete))
                            _journalPosition = fs2.Length;

                        Log.Write($"JournalLoop: journal position set to {_journalPosition} (end of file)");
                    }

                    // Tail new lines
                    using (var fs = new FileStream(latest, FileMode.Open, FileAccess.Read,
                               FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (_journalPosition > fs.Length) _journalPosition = 0;
                        fs.Seek(_journalPosition, SeekOrigin.Begin);
                        using var sr = new StreamReader(fs);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                            if (!string.IsNullOrWhiteSpace(line))
                                ProcessJournalLine(line, backfill: false, lat: 0, lon: 0);
                        _journalPosition = fs.Position;
                    }
                }
                catch { }

                Thread.Sleep(500);
            }
        }

        // ---------------------------------------------------------------
        // Scans all journal files for FSSBodySignals/SAASignalsFound events
        // in the current star system and populates SystemBioPlanets.
        private void BackfillSystemPlanets(string knownSystem = "")
        {
            string system = knownSystem;

            // Only derive system if not already provided
            if (string.IsNullOrEmpty(system) && !string.IsNullOrEmpty(CurrentBody))
            {
                // Search journals for FSDJump/Location that preceded this body's scans
                foreach (var f in Directory.GetFiles(_journalDir, "Journal.*.log").OrderByDescending(x => x).Take(30))
                {
                    string lastSys = "";
                    foreach (var line in SafeReadAllLines(f))
                    {
                        var obj = TryParse(line); if (obj == null) continue;
                        var ev = obj.Value<string>("event");
                        if (ev == "FSDJump" || ev == "Location" || ev == "CarrierJump")
                            lastSys = obj.Value<string>("StarSystem") ?? lastSys;
                        if ((ev == "FSSBodySignals" || ev == "SAASignalsFound" || ev == "ApproachBody") &&
                            string.Equals(obj.Value<string>("BodyName") ?? obj.Value<string>("Body") ?? "",
                                CurrentBody, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(lastSys))
                        {
                            system = lastSys;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(system)) break;
                }
            }

            // Final fallback to StarSystem property
            if (string.IsNullOrEmpty(system)) system = StarSystem;
            if (string.IsNullOrEmpty(system)) return;
            Log.Write($"BackfillSystemPlanets: using system '{system}'");
            try
            {
                // Clear stale planet data before rebuilding for the current system
                lock (_planetLock) { SystemBioPlanets.Clear(); SystemGeoPlanets.Clear(); }

                var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByDescending(f => f)
                    .ToArray();
                Log.Write($"BackfillSystemPlanets: scanning {files.Length} files for system '{system}'");

                // Build a map of body → completed genera from journal Analyse events
                // This works even when the cache has been deleted
                var journalCompleted = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                string trackSystem = "";
                string trackBody   = "";
                foreach (var file in files)
                {
                    foreach (var line in SafeReadAllLines(file))
                    {
                        var obj = TryParse(line); if (obj == null) continue;
                        var ev  = obj.Value<string>("event");
                        if (ev == "FSDJump" || ev == "Location" || ev == "CarrierJump")
                            trackSystem = obj.Value<string>("StarSystem") ?? trackSystem;
                        if (ev == "ApproachBody" || ev == "Touchdown")
                            trackBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? trackBody;
                        if (ev == "ScanOrganic" && obj.Value<string>("ScanType") == "Analyse" &&
                            string.Equals(trackSystem, system, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(trackBody))
                        {
                            var genus = obj.Value<string>("Genus_Localised") ?? obj.Value<string>("Genus") ?? "";
                            if (!string.IsNullOrEmpty(genus))
                            {
                                if (!journalCompleted.ContainsKey(trackBody))
                                    journalCompleted[trackBody] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                journalCompleted[trackBody].Add(genus);
                            }
                        }
                    }
                }
                Log.Write($"BackfillSystemPlanets: found {journalCompleted.Count} bodies with completed scans in journals");

                bool pastCurrentSystem = false;

                foreach (var file in files)
                {
                    if (pastCurrentSystem) break;

                    var lines = SafeReadAllLines(file);
                    bool fileHadCurrentSystem = false;
                    bool fileHadOtherSystem   = false;

                    // Pass 1: does this file mention the current system?
                    foreach (var line in lines)
                    {
                        var obj = TryParse(line); if (obj == null) continue;
                        var evt = obj.Value<string>("event");
                        if (evt == "FSDJump" || evt == "CarrierJump" || evt == "Location")
                        {
                            var sys = obj.Value<string>("StarSystem") ?? "";
                            if (string.Equals(sys, system, StringComparison.OrdinalIgnoreCase))
                                fileHadCurrentSystem = true;
                            else if (!string.IsNullOrEmpty(sys))
                                fileHadOtherSystem = true;
                        }
                        // Body name prefix is a reliable fallback
                        if ((evt == "FSSBodySignals" || evt == "SAASignalsFound"))
                        {
                            var body = obj.Value<string>("BodyName") ?? "";
                            if (!string.IsNullOrEmpty(body) &&
                                body.StartsWith(system, StringComparison.OrdinalIgnoreCase))
                                fileHadCurrentSystem = true;
                        }
                    }

                    if (!fileHadCurrentSystem)
                    {
                        if (fileHadOtherSystem) pastCurrentSystem = true;
                        continue;
                    }

                    // Pass 2: collect bio planets, gated by active system context
                    string activeSystem = "";
                    foreach (var line in lines)
                    {
                        var obj = TryParse(line); if (obj == null) continue;
                        var evt = obj.Value<string>("event");

                        if (evt == "FSDJump" || evt == "CarrierJump" || evt == "Location")
                            activeSystem = obj.Value<string>("StarSystem") ?? activeSystem;

                        if (evt == "FSSBodySignals" || evt == "SAASignalsFound")
                        {
                            var body    = obj.Value<string>("BodyName") ?? "";
                            var signals = obj["Signals"];
                            if (string.IsNullOrEmpty(body) || signals == null) continue;

                            bool inSystem =
                                string.Equals(activeSystem, system, StringComparison.OrdinalIgnoreCase)
                                || body.StartsWith(system, StringComparison.OrdinalIgnoreCase);
                            if (!inSystem) continue;

                            int bio = 0, geo = 0;
                            foreach (var sig in signals)
                            {
                                var t = sig.Value<string>("Type") ?? "";
                                if (t.Contains("Biological"))  bio = sig.Value<int>("Count");
                                if (t.Contains("Geological"))  geo = sig.Value<int>("Count");
                            }

                            if (bio > 0)
                            {
                                var shortName = GetShortBodyName(body, system);
                                lock (_planetLock)
                                {
                                    if (!SystemBioPlanets.Any(p =>
                                        string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var cached = ScanCache.LoadForBody(body);
                                        var completedGenera = cached.Organisms
                                            .Where(o => o.IsComplete)
                                            .Select(o => o.Genus)
                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                                        if (journalCompleted.TryGetValue(body, out var journalGenera))
                                            foreach (var g in journalGenera) completedGenera.Add(g);
                                        SystemBioPlanets.Add(new PlanetBioInfo
                                        {
                                            FullBodyName   = body,
                                            ShortName      = shortName,
                                            BioCount       = bio,
                                            CompletedCount = completedGenera.Count,
                                        });
                                        Log.Write($"BackfillSystemPlanets: added bio '{shortName}' bio={bio}");
                                    }
                                }
                            }

                            if (geo > 0)
                            {
                                var shortName = GetShortBodyName(body, system);
                                lock (_planetLock)
                                {
                                    if (!SystemGeoPlanets.Any(p =>
                                        string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var cached = ScanCache.LoadForBody(body);
                                        int discovered = cached.GeoSites
                                            .Select(g => g.EntryID).Distinct().Count();
                                        SystemGeoPlanets.Add(new PlanetGeoInfo
                                        {
                                            FullBodyName    = body,
                                            ShortName       = shortName,
                                            GeoCount        = geo,
                                            DiscoveredCount = discovered,
                                        });
                                        Log.Write($"BackfillSystemPlanets: added geo '{shortName}' geo={geo} discovered={discovered}");
                                    }
                                }
                            }
                        }

                        // Also collect CodexEntry geo events for backfilling KnownGeoSites
                        if (evt == "CodexEntry")
                        {
                            var subCat = obj.Value<string>("SubCategory") ?? "";
                            if (!subCat.Contains("Geology_and_Anomalies")) continue;
                            var codexBody = obj.Value<string>("BodyName") ?? "";
                            if (string.IsNullOrEmpty(codexBody)) continue;
                            bool inSystem = codexBody.StartsWith(system, StringComparison.OrdinalIgnoreCase);
                            if (!inSystem) continue;

                            var nameLoc = obj.Value<string>("Name_Localised") ?? obj.Value<string>("Name") ?? "";
                            var entryID = obj.Value<int>("EntryID");
                            var payout  = obj.Value<long?>("VoucherAmount") ?? 0;

                            if (string.IsNullOrEmpty(nameLoc) || entryID == 0) continue;

                            // Update discovered count on geo planet
                            lock (_planetLock)
                            {
                                var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, codexBody, StringComparison.OrdinalIgnoreCase));
                                if (gp != null)
                                {
                                    // Count unique entry IDs from cache
                                    var cached = ScanCache.LoadForBody(codexBody);
                                    gp.DiscoveredCount = cached.GeoSites
                                        .Select(g => g.EntryID).Distinct().Count();
                                }
                            }
                        }
                    }
                }  // end foreach (var file in files)

                Log.Write($"BackfillSystemPlanets: found {SystemBioPlanets.Count} bio planets, {SystemGeoPlanets.Count} geo planets");

                // Sync targeted body bio count so sidebar shows unknown slots
                // After building the planet list, refresh the sidebar if we're in space
                // targeting a planet with bio signals
                if (string.IsNullOrEmpty(CurrentBody))
                {
                    var target = !string.IsNullOrEmpty(TargetedBody) ? TargetedBody
                        : CurrentStatus?.BodyName ?? "";

                    if (!string.IsNullOrEmpty(target))
                    {
                        lock (_planetLock)
                        {
                            var tp = SystemBioPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, target, StringComparison.OrdinalIgnoreCase));
                            if (tp != null && tp.BioCount > 0)
                            {
                                TargetedBody         = target;
                                TargetedBodyBioCount = tp.BioCount;
                                BiologyCount         = tp.BioCount;
                                Log.Write($"BackfillSystemPlanets: firing BodyChanged for '{target}' bio={tp.BioCount}");
                                BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                    { BodyName = target, BioCount = tp.BioCount });
                            }
                            else
                            {
                                // Check for geo-only planet
                                var gp2 = SystemGeoPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, target, StringComparison.OrdinalIgnoreCase));
                                if (gp2 != null && gp2.GeoCount > 0)
                                {
                                    TargetedBody = target;
                                    GeologyCount = gp2.GeoCount;
                                    Log.Write($"BackfillSystemPlanets: firing BodyChanged for geo '{target}' geo={gp2.GeoCount}");
                                    BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                        { BodyName = target, BioCount = 0, GeoCount = gp2.GeoCount });
                                }
                            }
                        }
                    }
                }

                PlanetListChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Write($"BackfillSystemPlanets error: {ex.Message}");
            }
        }
        // ---------------------------------------------------------------
        // Backfill: reads last 5 journal files, filters ScanOrganic by current body,
        // uses the lat/lon EMBEDDED in the journal line (written by previous sessions via
        // CodexEntry which does include position), not current ship position.
        private void BackfillJournal(string latestFile)
        {
            try
            {
                // Progressive journal search: start with 20 files, expand in batches of 10
                // if we haven't found all needed data yet, up to a ceiling of 60 files
                const int initialBatch   = 20;
                const int batchIncrement = 10;
                const int maxFiles       = 60;

                var allFiles = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByDescending(f => f)
                    .ToArray();

                int fileCount = Math.Min(initialBatch, allFiles.Length);
                string[] files = allFiles.Take(fileCount).ToArray();

                // Make sure CurrentBody is set — Status.json may already have it,
                // but fall back to scanning the latest journal file in reverse
                // so we get the MOST RECENT body, not the first one in the file
                bool latestEventWasFSDJump = false;
                if (string.IsNullOrEmpty(CurrentBody))
                {
                    var latestLines = SafeReadAllLines(latestFile);
                    for (int i = latestLines.Count - 1; i >= 0; i--)
                    {
                        var o = TryParse(latestLines[i]); if (o == null) continue;
                        var ev = o.Value<string>("event");
                        if (ev == "FSDJump" || ev == "CarrierJump")
                        {
                            var jumpSystem = o.Value<string>("StarSystem") ?? "";
                            if (!string.IsNullOrEmpty(jumpSystem))
                            {
                                StarSystem            = jumpSystem;
                                _backfillSystem       = jumpSystem;
                                latestEventWasFSDJump = true;
                            }
                            Log.Write($"Backfill: latest event was FSDJump to '{jumpSystem}', no current body");
                            break;
                        }
                        // Only Touchdown/Disembark guarantee a landable planet body
                        // Location/ApproachBody can match star names, not just landable bodies
                        if (ev == "Touchdown" || ev == "Disembark")
                        {
                            CurrentBody = o.Value<string>("Body") ?? o.Value<string>("BodyName") ?? "";
                            if (!string.IsNullOrEmpty(CurrentBody))
                            {
                                Log.Write($"Backfill: most recent body from journal='{CurrentBody}'");
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(CurrentBody))
                {
                    // Only fall back to the cached body if we're still in the same system.
                    bool cachedBodyIsInCurrentSystem = true;
                    if (!string.IsNullOrEmpty(StarSystem) && !string.IsNullOrEmpty(CachedBodyName))
                        cachedBodyIsInCurrentSystem =
                            CachedBodyName.StartsWith(StarSystem, StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(CachedBodyName) && cachedBodyIsInCurrentSystem)
                    {
                        CurrentBody = CachedBodyName;
                        Log.Write($"Backfill: no body in new journal, using cached body '{CurrentBody}'");
                    }
                    else if (!string.IsNullOrEmpty(CachedBodyName) && !cachedBodyIsInCurrentSystem)
                    {
                        Log.Write($"Backfill: cached body '{CachedBodyName}' is from a different system ('{StarSystem}') — skipping scan backfill");
                        return;
                    }
                    else
                    {
                        Log.Write("Backfill: no current body, skipping scan backfill");
                        return;
                    }
                }

                Log.Write($"Backfill: scanning {files.Length} files for '{CurrentBody}'");

                // Search ALL journal files for the Scan event for this body to get WasFootfalled
                // It may be in a much older journal file from a previous session
                var allJournalFiles = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByDescending(f => f)
                    .ToArray();

                foreach (var file in allJournalFiles)
                {
                    bool found = false;
                    foreach (var line in SafeReadAllLines(file))
                    {
                        var o = TryParse(line); if (o == null) continue;
                        if (o.Value<string>("event") != "Scan") continue;
                        var bodyName = o.Value<string>("BodyName") ?? "";
                        if (!string.Equals(bodyName, CurrentBody, StringComparison.OrdinalIgnoreCase)) continue;
                        var wf = o.Value<bool?>("WasFootfalled") ?? true;
                        WasFootfalled = !wf;
                        Log.Write($"Backfill: found Scan for '{CurrentBody}' in {System.IO.Path.GetFileName(file)} WasFootfalled={wf} → FirstFootfall={WasFootfalled}");
                        found = true;
                        break;
                    }
                    if (found) break;
                }

                foreach (var file in files)
                {
                    string? activeBody = null;
                    double  activeLat  = 0, activeLon = 0;

                    foreach (var line in SafeReadAllLines(file))
                    {
                        var obj = TryParse(line); if (obj == null) continue;
                        var evt = obj.Value<string>("event"); if (string.IsNullOrEmpty(evt)) continue;

                        switch (evt)
                        {
                            case "ApproachBody":
                                activeBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? activeBody;
                                break;

                            case "Location":
                            {
                                activeBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? activeBody;
                                var locSys = obj.Value<string>("StarSystem") ?? "";
                                if (!string.IsNullOrEmpty(locSys)) StarSystem = locSys;
                                break;
                            }

                            case "Touchdown":
                            case "Disembark":
                            {
                                activeBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? activeBody;
                                if (obj["Latitude"]  != null) activeLat = obj.Value<double>("Latitude");
                                if (obj["Longitude"] != null) activeLon = obj.Value<double>("Longitude");

                                // Confirm first footfall if pending — covers the case where
                                // Disembark was replayed as backfill (e.g. app started after landing)
                                if (_pendingFirstFootfall &&
                                    string.Equals(activeBody, CurrentBody, StringComparison.OrdinalIgnoreCase))
                                {
                                    WasFootfalled         = true;
                                    _pendingFirstFootfall = false;
                        _pendingFirstFootfallBody = "";
                                    Log.Write($"Backfill Disembark: First Footfall confirmed for '{CurrentBody}'");
                                }
                                break;
                            }

                            case "LeaveBody":
                            case "SupercruiseEntry":
                                activeBody = null;
                                activeLat  = 0;
                                activeLon  = 0;
                                break;

                            case "FSDJump":
                            case "CarrierJump":
                            {
                                var fsdSys = obj.Value<string>("StarSystem") ?? "";
                                if (!string.IsNullOrEmpty(fsdSys)) StarSystem = fsdSys;
                                activeBody = null;
                                activeLat  = 0;
                                activeLon  = 0;
                                break;
                            }

                            // CodexEntry DOES include lat/lon — use it to track last known position
                            case "CodexEntry":
                            {
                                if (obj["Latitude"] != null)  activeLat = obj.Value<double>("Latitude");
                                if (obj["Longitude"] != null) activeLon = obj.Value<double>("Longitude");
                                // Process geo codex entries for current body
                                var subCat = obj.Value<string>("SubCategory") ?? "";
                                if (subCat.Contains("Geology_and_Anomalies"))
                                {
                                    var codexBody2 = obj.Value<string>("BodyName") ?? obj.Value<string>("Body") ?? activeBody ?? "";
                                    if (string.Equals(codexBody2, CurrentBody, StringComparison.OrdinalIgnoreCase))
                                        ProcessJournalLine(line, backfill: true, lat: activeLat, lon: activeLon);
                                }
                                break;
                            }

                            case "Liftoff":
                            {
                                if (obj["Latitude"]  != null) activeLat = obj.Value<double>("Latitude");
                                if (obj["Longitude"] != null) activeLon = obj.Value<double>("Longitude");
                                break;
                            }

                            case "ScanOrganic":
                            {
                                // Only process events for the current body
                                if (!string.Equals(activeBody, CurrentBody, StringComparison.OrdinalIgnoreCase))
                                    break;
                                // Use tracked position
                                ProcessJournalLine(line, backfill: true, lat: activeLat, lon: activeLon);
                                break;
                            }

                            case "FSSBodySignals":
                            case "SAASignalsFound":
                            {
                                var body = obj.Value<string>("BodyName") ?? "";
                                if (string.Equals(body, CurrentBody, StringComparison.OrdinalIgnoreCase))
                                    ProcessJournalLine(line, backfill: true, lat: 0, lon: 0);
                                break;
                            }
                        }
                    }
                }

                // Progressive expansion: if we found no scan data and more files exist, search deeper
                while (ScannedOrganisms.Count == 0 && fileCount < maxFiles && fileCount < allFiles.Length)
                {
                    int nextCount = Math.Min(fileCount + batchIncrement, Math.Min(maxFiles, allFiles.Length));
                    Log.Write($"Backfill: no scan data found in {fileCount} files — expanding search to {nextCount} files");
                    var extraFiles = allFiles.Skip(fileCount).Take(nextCount - fileCount).ToArray();
                    fileCount = nextCount;

                    foreach (var file in extraFiles)
                    {
                        string? activeBody = null;
                        double  activeLat  = 0, activeLon = 0;

                        foreach (var line in SafeReadAllLines(file))
                        {
                            var obj = TryParse(line); if (obj == null) continue;
                            var evt = obj.Value<string>("event"); if (string.IsNullOrEmpty(evt)) continue;

                            if (evt == "ApproachBody")
                                activeBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? activeBody;
                            else if (evt == "Touchdown" || evt == "Disembark")
                            {
                                activeBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? activeBody;
                                if (obj["Latitude"]  != null) activeLat = obj.Value<double>("Latitude");
                                if (obj["Longitude"] != null) activeLon = obj.Value<double>("Longitude");
                            }
                            else if (evt == "FSDJump" || evt == "CarrierJump" || evt == "LeaveBody")
                            { activeBody = null; activeLat = 0; activeLon = 0; }
                            else if (evt == "ScanOrganic" &&
                                     string.Equals(activeBody, CurrentBody, StringComparison.OrdinalIgnoreCase))
                                ProcessJournalLine(line, backfill: true, lat: activeLat, lon: activeLon);
                            else if ((evt == "SAASignalsFound" || evt == "FSSBodySignals") &&
                                     string.Equals(obj.Value<string>("BodyName") ?? "", CurrentBody, StringComparison.OrdinalIgnoreCase))
                                ProcessJournalLine(line, backfill: true, lat: 0, lon: 0);
                        }
                    }
                }
                if (fileCount > initialBatch)
                    Log.Write($"Backfill: progressive search completed — searched {fileCount} files total, found {ScannedOrganisms.Count} organisms");

                // Don't overwrite if we already set it from an FSDJump above
                _backfillSystem = latestEventWasFSDJump ? _backfillSystem : StarSystem;
                if (!string.IsNullOrEmpty(CurrentBody) && !latestEventWasFSDJump)
                {
                    // Derive system from the body's journal context
                    foreach (var jf in Directory.GetFiles(_journalDir, "Journal.*.log")
                        .OrderByDescending(f => f).Take(30))
                    {
                        string lastSys2 = "";
                        bool found2 = false;
                        foreach (var line in SafeReadAllLines(jf))
                        {
                            var o2 = TryParse(line); if (o2 == null) continue;
                            var ev2 = o2.Value<string>("event");
                            if (ev2 == "FSDJump" || ev2 == "Location" || ev2 == "CarrierJump")
                                lastSys2 = o2.Value<string>("StarSystem") ?? lastSys2;
                            if ((ev2 == "ApproachBody" || ev2 == "Touchdown" || ev2 == "SAASignalsFound") &&
                                string.Equals(
                                    o2.Value<string>("Body") ?? o2.Value<string>("BodyName") ?? "",
                                    CurrentBody, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(lastSys2))
                            { _backfillSystem = lastSys2; found2 = true; break; }
                        }
                        if (found2) break;
                    }
                }
                Log.Write($"Backfill: system for BackfillSystemPlanets='{_backfillSystem}'");

                // Check if the player has left the body since the last scan
                // by finding whether LeaveBody/FSDJump appears after the last ScanOrganic
                // in the most recent journal file
                try
                {
                    var recentLines = SafeReadAllLines(latestFile);
                    int lastScanIdx   = -1;
                    int lastLeaveIdx  = -1;
                    for (int i = 0; i < recentLines.Count; i++)
                    {
                        var o = TryParse(recentLines[i]); if (o == null) continue;
                        var ev = o.Value<string>("event");
                        if (ev == "ScanOrganic" || ev == "Touchdown" || ev == "Disembark")
                            lastScanIdx = i;
                        if (ev == "LeaveBody" || ev == "FSDJump" || ev == "SupercruiseEntry")
                            lastLeaveIdx = i;
                    }
                    if (lastLeaveIdx > lastScanIdx && lastLeaveIdx >= 0)
                    {
                        Log.Write($"Backfill: LeaveBody/FSDJump after last scan — clearing body");
                        lock (ScannedOrganisms) ScannedOrganisms.Clear();
                        lock (KnownGenera)      KnownGenera.Clear();
                        lock (CompletedGenera)  CompletedGenera.Clear();
                        BiologyCount   = 0;
                        CurrentBody    = "";
                        WasFootfalled  = false;

                        // Check if there's a SAASignalsFound AFTER the LeaveBody
                        // (player scanned a new planet from orbit) — populate sidebar with its genera
                        string lastDssBody = "";
                        for (int i = lastLeaveIdx; i < recentLines.Count; i++)
                        {
                            var o = TryParse(recentLines[i]); if (o == null) continue;
                            var ev = o.Value<string>("event");
                            if (ev == "SAASignalsFound" || ev == "FSSBodySignals")
                            {
                                var body    = o.Value<string>("BodyName") ?? "";
                                var signals = o["Signals"];
                                if (!string.IsNullOrEmpty(body) && signals != null)
                                {
                                    int bio = 0;
                                    foreach (var sig in signals)
                                    {
                                        if ((sig.Value<string>("Type") ?? "").Contains("Biological"))
                                            bio = sig.Value<int>("Count");
                                    }
                                    if (bio > 0) lastDssBody = body;
                                }
                            }
                            if (ev == "SAASignalsFound")
                            {
                                var genuses = o["Genuses"];
                                if (genuses != null && !string.IsNullOrEmpty(lastDssBody))
                                {
                                    lock (KnownGenera) { KnownGenera.Clear(); }
                                    foreach (var g in genuses)
                                    {
                                        var gName = g.Value<string>("Genus_Localised") ?? g.Value<string>("Genus") ?? "";
                                        if (!string.IsNullOrEmpty(gName))
                                            lock (KnownGenera) KnownGenera.Add(gName);
                                    }
                                    TargetedBody         = lastDssBody;
                                    TargetedBodyBioCount = KnownGenera.Count;
                                    Log.Write($"Backfill: populated sidebar for targeted body '{lastDssBody}' with {KnownGenera.Count} genera");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"Backfill leave-check error: {ex.Message}"); }

                // Fire BodyChanged so the UI refreshes with correct WasFootfalled state
                BodyChanged?.Invoke(this, new BodyChangedEventArgs
                    { BodyName = CurrentBody, BioCount = BiologyCount, GeoCount = GeologyCount });
            }
            catch (Exception ex)
            {
                Log.Write($"Backfill exception: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        private static List<string> SafeReadAllLines(string file)
        {
            var result = new List<string>();
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null)
                    if (!string.IsNullOrWhiteSpace(line))
                        result.Add(line);
            }
            catch { }
            return result;
        }

        private static JObject? TryParse(string line)
        {
            try { return JObject.Parse(line); } catch { return null; }
        }

        // ---------------------------------------------------------------
        // lat/lon params allow backfill to pass historical position instead of current ship pos
        private void ProcessJournalLine(string line, bool backfill, double lat, double lon)
        {
            if (!line.Contains("\"event\"")) return;
            var obj = TryParse(line); if (obj == null) return;
            var evt = obj.Value<string>("event"); if (string.IsNullOrEmpty(evt)) return;

            switch (evt)
            {
                case "Scan":
                {
                    var bodyName      = obj.Value<string>("BodyName") ?? "";
                    var wasFootfalled = obj.Value<bool?>("WasFootfalled") ?? true;
                    // Match against current body, targeted body, OR any body name
                    // (WasFootfalled applies to ALL landable planets, not just bio ones)
                    if (!string.IsNullOrEmpty(bodyName) &&
                        (string.IsNullOrEmpty(CurrentBody) || // in space — accept any body scan
                         string.Equals(bodyName, CurrentBody, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(bodyName, TargetedBody, StringComparison.OrdinalIgnoreCase)))
                    {
                        _pendingFirstFootfall     = !wasFootfalled;
                        _pendingFirstFootfallBody = bodyName;
                        if (backfill && _pendingFirstFootfall)
                            WasFootfalled = true;
                        Log.Write($"Scan: {bodyName} WasFootfalled={wasFootfalled} → PendingFF={_pendingFirstFootfall}");
                    }
                    break;
                }

                case "Disembark":
                {
                    // Player stepped off ship — if pending FF, now confirm it
                    if (_pendingFirstFootfall && !backfill)
                    {
                        WasFootfalled         = true;
                        _pendingFirstFootfall = false;
                        _pendingFirstFootfallBody = "";
                        // Use body from event if CurrentBody not yet set (e.g. status poll hasn't fired)
                        var disembarkBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? CurrentBody;
                        Log.Write($"Disembark: First Footfall confirmed for '{disembarkBody}'");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs
                            { BodyName = disembarkBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                    }
                    break;
                }

                case "FSSBodySignals":
                case "SAASignalsFound":
                {
                    var body    = obj.Value<string>("BodyName") ?? "";
                    var signals = obj["Signals"];
                    if (string.IsNullOrEmpty(body) || signals == null) break;

                    int bio = 0, geo = 0;
                    foreach (var sig in signals)
                    {
                        var t = sig.Value<string>("Type") ?? "";
                        var c = sig.Value<int>("Count");
                        if (t.Contains("Biological"))  bio = c;
                        else if (t.Contains("Geological")) geo = c;
                    }

                    // Store per-body so we can show counts when targeting any planet
                    lock (_bodyBioSignals) _bodyBioSignals[body] = bio;

                    // Update planet bio list for current system
                    if (bio > 0)
                    {
                        // Derive system name reliably from CurrentBody or existing planet entries
                        // rather than StarSystem which may reflect a different current location
                        string systemForShortName = StarSystem;
                        if (!string.IsNullOrEmpty(CurrentBody))
                        {
                            // Find system from known planets that share this body's prefix
                            lock (_planetLock)
                            {
                                var match = SystemBioPlanets.FirstOrDefault(p =>
                                    body.StartsWith(p.FullBodyName.Contains(" ")
                                        ? string.Join(" ", p.FullBodyName.Split(' ').Take(p.FullBodyName.Split(' ').Length - 2))
                                        : p.FullBodyName, StringComparison.OrdinalIgnoreCase));
                                if (match == null)
                                {
                                    // Derive from CurrentBody — strip last 1-2 tokens to get system
                                    var parts = CurrentBody.Split(' ');
                                    for (int i = parts.Length - 1; i >= 2; i--)
                                    {
                                        var candidate = string.Join(" ", parts.Take(i));
                                        if (body.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                                        { systemForShortName = candidate; break; }
                                    }
                                }
                            }
                        }
                        var shortName = GetShortBodyName(body, systemForShortName);
                        lock (_planetLock)
                        {
                            var existing = SystemBioPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase));
                            if (existing == null)
                                SystemBioPlanets.Add(new PlanetBioInfo
                                    { FullBodyName = body, ShortName = shortName, BioCount = bio });
                            else
                            {
                                existing.BioCount  = bio;
                                existing.ShortName = shortName;
                            }
                        }
                        PlanetListChanged?.Invoke(this, EventArgs.Empty);
                    }

                    // Update SystemGeoPlanets live for geo-only or mixed planets
                    if (geo > 0)
                    {
                        string systemForGeo = StarSystem;
                        if (!string.IsNullOrEmpty(CurrentBody))
                        {
                            var parts = CurrentBody.Split(' ');
                            for (int i = parts.Length - 1; i >= 2; i--)
                            {
                                var candidate = string.Join(" ", parts.Take(i));
                                if (body.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                                { systemForGeo = candidate; break; }
                            }
                        }
                        var geoShortName = GetShortBodyName(body, systemForGeo);
                        lock (_planetLock)
                        {
                            var existing = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase));
                            if (existing == null)
                                SystemGeoPlanets.Add(new PlanetGeoInfo
                                    { FullBodyName = body, ShortName = geoShortName, GeoCount = geo });
                            else
                            {
                                existing.GeoCount  = geo;
                                existing.ShortName = geoShortName;
                            }
                        }
                        PlanetListChanged?.Invoke(this, EventArgs.Empty);
                    }
                    if (string.Equals(body, CurrentBody, StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(CurrentBody))
                    {
                        BiologyCount = bio;
                        GeologyCount = geo;
                    }
                    if (string.Equals(body, TargetedBody, StringComparison.OrdinalIgnoreCase))
                        TargetedBodyBioCount = bio;

                    // SAASignalsFound (detailed surface scan) includes a Genuses array
                    if (evt == "SAASignalsFound")
                    {
                        var genuses = obj["Genuses"];
                        if (genuses != null)
                        {
                            lock (KnownGenera) KnownGenera.Clear();
                            foreach (var g in genuses)
                            {
                                var genusLoc = g.Value<string>("Genus_Localised")
                                            ?? g.Value<string>("Genus") ?? "";
                                if (!string.IsNullOrEmpty(genusLoc))
                                {
                                    var cleaned = CleanInternalName(genusLoc);
                                    lock (KnownGenera)
                                        if (!KnownGenera.Contains(cleaned))
                                            KnownGenera.Add(cleaned);
                                }
                            }
                            Log.Write($"SAASignalsFound: {KnownGenera.Count} genera: {string.Join(", ", KnownGenera)}");

                            // If in space (no current body), set TargetedBody so sidebar shows genera
                            if (string.IsNullOrEmpty(CurrentBody) && !string.IsNullOrEmpty(body))
                            {
                                TargetedBody         = body;
                                TargetedBodyBioCount = bio;
                            }
                        }
                    }

                    if (!backfill)
                    {
                        // Persist bio count and known genera to cache for this body
                        List<string> generaSnapshot;
                        lock (KnownGenera) generaSnapshot = KnownGenera.ToList();
                        ScanCache.SaveBodyMeta(body, bio, generaSnapshot, WasFootfalled);

                        BodyChanged?.Invoke(this, new BodyChangedEventArgs
                            { BodyName = body, BioCount = bio, GeoCount = geo });
                    }
                    break;
                }

                case "CodexEntry":
                {
                    var subCat = obj.Value<string>("SubCategory") ?? "";
                    if (!subCat.Contains("Geology_and_Anomalies")) break;

                    var codexBody = obj.Value<string>("BodyName") ?? obj.Value<string>("Body") ?? CurrentBody;
                    if (string.IsNullOrEmpty(codexBody)) break;

                    var nameLoc = obj.Value<string>("Name_Localised") ?? obj.Value<string>("Name") ?? "";
                    var entryID = obj.Value<int>("EntryID");
                    var payout  = obj.Value<long?>("VoucherAmount") ?? 0;
                    var geoLat  = obj.Value<double?>("Latitude")  ?? lat;
                    var geoLon  = obj.Value<double?>("Longitude") ?? lon;

                    if (string.IsNullOrEmpty(nameLoc) || entryID == 0) break;

                    var site = new ScannedGeoSite
                    {
                        Latitude  = geoLat,
                        Longitude = geoLon,
                        Name      = nameLoc,
                        EntryID   = entryID,
                        Payout    = payout,
                        LastSeen  = DateTime.UtcNow,
                    };

                    bool isCurrentBody = string.Equals(codexBody, CurrentBody, StringComparison.OrdinalIgnoreCase);
                    if (isCurrentBody)
                    {
                        lock (KnownGeoSites)
                        {
                            if (!KnownGeoSites.Any(g => g.EntryID == entryID))
                            {
                                KnownGeoSites.Add(site);
                                Log.Write($"CodexEntry geo: '{nameLoc}' on '{codexBody}' payout={payout}");
                            }
                        }
                        lock (_planetLock)
                        {
                            var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, codexBody, StringComparison.OrdinalIgnoreCase));
                            if (gp != null)
                                gp.DiscoveredCount = KnownGeoSites.Select(g => g.EntryID).Distinct().Count();
                        }
                    }

                    if (!backfill)
                    {
                        ScanCache.SaveGeoSite(codexBody, site, GeologyCount);
                        if (payout > 0) EarningsTracker.AddEarning(payout);
                        if (isCurrentBody)
                            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                { BodyName = CurrentBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                    }
                    break;
                }

                case "ScanOrganic":
                {
                    var genusRaw    = obj.Value<string>("Genus")             ?? "";
                    var speciesRaw  = obj.Value<string>("Species")           ?? "";
                    var genusLoc    = obj.Value<string>("Genus_Localised")   ?? "";
                    var speciesLoc  = obj.Value<string>("Species_Localised") ?? "";
                    var scanTypeStr = obj.Value<string>("ScanType")          ?? "";

                    var genus   = !string.IsNullOrEmpty(genusLoc)   ? genusLoc   : CleanInternalName(genusRaw);
                    // Species_Localised often contains the full name e.g. "Bacterium Cerbrus"
                    // Strip the genus prefix if present to avoid "Bacterium Bacterium Cerbrus"
                    var speciesFull = !string.IsNullOrEmpty(speciesLoc) ? speciesLoc : CleanInternalName(speciesRaw);
                    var species = speciesFull.StartsWith(genus + " ", StringComparison.OrdinalIgnoreCase)
                        ? speciesFull.Substring(genus.Length + 1).Trim()
                        : speciesFull;

                    // Scan sequence: Log=1st, Sample=2nd OR 3rd, Analyse=completion (no new dot)
                    // existingCount = non-complete dots only (completed ones don't count toward sequence)
                    int existingCount;
                    lock (ScannedOrganisms)
                        existingCount = ScannedOrganisms.Count(o =>
                            string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                            && !o.IsComplete);

                    // During backfill, skip genera that are already fully complete in cache
                    // to avoid duplicate dots and incorrect sequence counting
                    if (backfill)
                    {
                        bool alreadyComplete;
                        lock (ScannedOrganisms)
                            alreadyComplete = ScannedOrganisms.Any(o =>
                                string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                && o.IsComplete)
                                && !ScannedOrganisms.Any(o =>
                                string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                && !o.IsComplete);
                        if (alreadyComplete)
                        {
                            Log.Write($"Backfill: skipping {genus} — already complete in cache");
                            break;
                        }
                    }

                    int scanNum = scanTypeStr switch
                    {
                        "Log"     => 1,
                        "Sample"  => existingCount >= 2 ? 3 : 2,
                        "Analyse" => 4,
                        _         => 1
                    };

                    // Position priority: embedded in line > caller-supplied (backfill) > current status (live only)
                    double useLat = obj["Latitude"]  != null ? obj.Value<double>("Latitude")  :
                                    lat != 0                 ? lat                             :
                                    CurrentStatus.Latitude;
                    double useLon = obj["Longitude"] != null ? obj.Value<double>("Longitude") :
                                    lon != 0                 ? lon                             :
                                    CurrentStatus.Longitude;

                    // Reject if no real position — during backfill lat/lon must come from caller (CodexEntry),
                    // never fall back to current ship position for historical events
                    if (useLat == 0 && useLon == 0)
                    {
                        Log.Write($"ScanOrganic: no position for {genus}, skipping");
                        break;
                    }

                    // For live scans, also require the status flag confirms we have lat/long
                    if (!backfill && !CurrentStatus.HasPosition)
                    {
                        Log.Write($"ScanOrganic: status has no position yet for {genus}, skipping");
                        break;
                    }

                    double radius = CurrentStatus.PlanetRadius > 0 ? CurrentStatus.PlanetRadius : 6_371_000;

                    lock (ScannedOrganisms)
                    {
                        // During live scanning, if the genus switches, grey out the previous
                        // incomplete genus. Skip this during backfill — we replay history in
                        // journal order and the Analyse event will handle completion correctly.
                        if (!backfill)
                        {
                            var incompleteDifferentGenus = ScannedOrganisms
                                .Where(o => !string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                         && !o.IsComplete)
                                .Select(o => o.Genus)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            foreach (var oldGenus in incompleteDifferentGenus)
                            {
                                var toGrey = ScannedOrganisms
                                    .Where(o => string.Equals(o.Genus, oldGenus, StringComparison.OrdinalIgnoreCase)
                                             && !o.IsComplete)
                                    .ToList();
                                foreach (var o in toGrey) o.IsComplete = true;
                                ScanCache.SaveForBody(CurrentBody, ScannedOrganisms, BiologyCount, KnownGenera, WasFootfalled);
                                Log.Write($"Greyed incomplete dots for '{oldGenus}' — switched to '{genus}'");
                            }
                        }

                        // Get highest scan number already recorded for this genus genus
                        int highestSeen = ScannedOrganisms
                            .Where(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase))
                            .Select(o => o.ScanCount)
                            .DefaultIfEmpty(0)
                            .Max();

                        // Analyse (scanNum=4) = completion only — grey out all dots, no new dot
                        if (scanNum == 4)
                        {
                            var genusOrgs = ScannedOrganisms
                                .Where(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var o in genusOrgs) o.IsComplete = true;

                            // Add to CompletedGenera for sidebar
                            lock (CompletedGenera)
                            {
                                if (!CompletedGenera.Any(o =>
                                    string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)))
                                {
                                    CompletedGenera.Add(new ScannedOrganism
                                    {
                                        Genus      = genus,
                                        Species    = species,
                                        ScanCount  = 3,
                                        IsComplete = true,
                                    });
                                }
                            }

                            ScanCache.SaveForBody(CurrentBody, ScannedOrganisms, BiologyCount, KnownGenera, WasFootfalled);
                            Log.Write($"Analyse complete for {genus} — all dots greyed");

                            // Record payout — only for live scans, not backfill
                            // Backfill earnings are handled by the journal scan feature
                            if (!backfill)
                            {
                                var speciesName = !string.IsNullOrEmpty(species)
                                    ? $"{genus} {species}".Trim()
                                    : genus;
                                var payout = PayoutData.GetValue(speciesName, WasFootfalled);
                                if (payout > 0)
                                {
                                    EarningsTracker.AddEarning(payout);
                                    Log.Write($"Payout: {PayoutData.FormatCredits(payout)} for {speciesName} (firstFootfall={WasFootfalled})");
                                }
                            }

                            // Update planet completion count
                            lock (_planetLock)
                            {
                                var planet = SystemBioPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                                if (planet != null) planet.CompletedCount++;
                            }

                            if (!backfill)
                                OrganismScanned?.Invoke(this, new OrganismScannedEventArgs
                                    { Organism = new ScannedOrganism { Genus = genus, Species = species, ScanCount = 3, IsComplete = true } });                            break;
                        }

                        // Skip if we already have this scan number or higher (duplicate prevention)
                        if (scanNum <= highestSeen && scanNum < 3)
                        {
                            Log.Write($"ScanOrganic: skipping scan={scanNum} for {genus}, already have scan={highestSeen}");
                            break;
                        }

                        if (scanNum == 3)
                        {
                            // Third sample location — add orange dot immediately
                            // Analyse event (fired seconds later) will grey everything out
                            if (useLat != 0 || useLon != 0)
                            {
                                var org = new ScannedOrganism
                                {
                                    Latitude   = useLat,
                                    Longitude  = useLon,
                                    Genus      = genus,
                                    Species    = species,
                                    ScanCount  = 3,
                                    IsComplete = false,  // orange until Analyse fires
                                };
                                ScannedOrganisms.Add(org);
                                Log.Write($"Added 3rd dot (orange): {genus} at {useLat:F4},{useLon:F4}");

                                if (!backfill)
                                {
                                    ScanCache.SaveForBody(CurrentBody, ScannedOrganisms, BiologyCount, KnownGenera, WasFootfalled);
                                    OrganismScanned?.Invoke(this, new OrganismScannedEventArgs { Organism = org });
                                }
                            }
                        }
                        else
                        {
                            // Scan 1 (Log) or 2 (Sample) — add a new dot at this location

                            // If this is a fresh Log (scan 1) for a genus that has
                            // greyed-out abandoned dots, clear them so the sidebar
                            // pips reset to show the new attempt from scratch
                            if (scanNum == 1)
                            {
                                var abandoned = ScannedOrganisms
                                    .Where(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                             && o.IsComplete)
                                    .ToList();
                                if (abandoned.Count > 0)
                                {
                                    foreach (var a in abandoned) ScannedOrganisms.Remove(a);
                                    Log.Write($"Cleared {abandoned.Count} abandoned grey dots for '{genus}' — restarting scan");
                                }
                            }

                            var org = new ScannedOrganism
                            {
                                Latitude  = useLat,
                                Longitude = useLon,
                                Genus     = genus,
                                Species   = species,
                                ScanCount = scanNum,
                            };
                            ScannedOrganisms.Add(org);
                            Log.Write($"Added dot: {genus} {species} scan={scanNum} at {useLat:F4},{useLon:F4}  total dots for genus={ScannedOrganisms.Count(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase))}");

                            if (!backfill)
                            {
                                ScanCache.SaveForBody(CurrentBody, ScannedOrganisms, BiologyCount, KnownGenera, WasFootfalled);
                                OrganismScanned?.Invoke(this, new OrganismScannedEventArgs { Organism = org });
                            }
                        }
                    }
                    break;
                }

                case "ApproachBody":
                case "Touchdown":
                {
                    var body = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? "";
                    if (!string.IsNullOrEmpty(body) && body != CurrentBody && !backfill)
                    {
                        CurrentBody = body;
                        var loaded = ScanCache.LoadForBody(CurrentBody);
                        lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(loaded.Organisms); }
                        lock (KnownGenera)     { KnownGenera.Clear(); if (loaded.KnownGenera.Count > 0) KnownGenera.AddRange(loaded.KnownGenera); }
                        lock (CompletedGenera) CompletedGenera.Clear();
                        if (loaded.BiologyCount > 0) BiologyCount = loaded.BiologyCount;
                        if (loaded.WasFootfalled) WasFootfalled = true;
                        GeologyCount = 0;
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = body, BioCount = BiologyCount });
                    }
                    break;
                }

                case "Location":
                case "CarrierJump":
                {
                    var sys = obj.Value<string>("StarSystem") ?? "";
                    if (!string.IsNullOrEmpty(sys)) StarSystem = sys;
                    break;
                }
                case "LeaveBody":
                {
                    if (!backfill)
                    {
                        // Left the planet — clear display but keep cache in case we return
                        Log.Write($"LeaveBody: clearing display for '{CurrentBody}', cache preserved");
                        lock (ScannedOrganisms) ScannedOrganisms.Clear();
                        lock (KnownGenera)      KnownGenera.Clear();
                        lock (CompletedGenera)  CompletedGenera.Clear();
                        lock (KnownGeoSites)    KnownGeoSites.Clear();
                        BiologyCount          = 0;
                        GeologyCount          = 0;
                        CurrentBody           = "";
                        WasFootfalled         = false;
                        _pendingFirstFootfall = false;
                        _pendingFirstFootfallBody = "";
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                    break;
                }

                case "FSDJump":
                {
                    if (!backfill)
                    {
                        var newSystem = obj.Value<string>("StarSystem") ?? "";
                        if (!string.Equals(newSystem, StarSystem, StringComparison.OrdinalIgnoreCase))
                        {
                            StarSystem = newSystem;
                            lock (_planetLock) { SystemBioPlanets.Clear(); SystemGeoPlanets.Clear(); }
                            PlanetListChanged?.Invoke(this, EventArgs.Empty);
                        }
                        // Clear old body cache on jump
                        if (!string.IsNullOrEmpty(CurrentBody))
                        {
                            Log.Write($"FSDJump: clearing cache for '{CurrentBody}'");
                            ScanCache.ClearBody(CurrentBody);
                        }
                        lock (ScannedOrganisms) ScannedOrganisms.Clear();
                        lock (KnownGenera)      KnownGenera.Clear();
                        lock (CompletedGenera)  CompletedGenera.Clear();
                        lock (KnownGeoSites)    KnownGeoSites.Clear();
                        BiologyCount          = 0;
                        GeologyCount          = 0;
                        CurrentBody           = "";
                        WasFootfalled         = false;
                        _pendingFirstFootfall = false;
                        _pendingFirstFootfallBody = "";
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                    break;
                }
            }
        }

        private static string CleanInternalName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            raw = raw.TrimStart('$').TrimEnd(';');
            foreach (var prefix in new[] { "Codex_Ent_", "Codex_" })
                if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    raw = raw.Substring(prefix.Length);
            if (raw.EndsWith("_Name", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(0, raw.Length - 5);
            return raw.Replace("_", " ").Trim();
        }

        // ---------------------------------------------------------------
        public static double DistanceMeters(double lat1, double lon1,
                                            double lat2, double lon2,
                                            double planetRadius)
        {
            if (planetRadius <= 0) planetRadius = 6_371_000;
            const double D2R = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * D2R;
            double dLon = (lon2 - lon1) * D2R;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * D2R) * Math.Cos(lat2 * D2R)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return planetRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
        {
            const double D2R = Math.PI / 180.0;
            double dLon = (lon2 - lon1) * D2R;
            double y    = Math.Sin(dLon) * Math.Cos(lat2 * D2R);
            double x    = Math.Cos(lat1 * D2R) * Math.Sin(lat2 * D2R)
                        - Math.Sin(lat1 * D2R) * Math.Cos(lat2 * D2R) * Math.Cos(dLon);
            return (Math.Atan2(y, x) * 180.0 / Math.PI + 360) % 360;
        }

        // ---------------------------------------------------------------
        public static string GetJournalDirectory()
        {
            IntPtr path = IntPtr.Zero;
            try
            {
                var guid = new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");
                SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out path);
                var savedGames = Marshal.PtrToStringUni(path)!;
                var dir = Path.Combine(savedGames, "Frontier Developments", "Elite Dangerous");
                if (Directory.Exists(dir)) return dir;
            }
            catch { }
            finally { if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path); }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games", "Frontier Developments", "Elite Dangerous");
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
            uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        public void Dispose()
        {
            _cts.Cancel();
            _statusReady.Dispose();
        }
    }
}
