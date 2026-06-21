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
        // Body whose data is currently loaded into the in-memory display lists
        // (ScannedOrganisms, KnownGenera, CompletedGenera, KnownGeoSites, BiologyCount,
        // GeologyCount, WasFootfalled). Diverges from CurrentBody during a preview
        // (when the user clicks another planet in the sidebar). Live event handlers
        // gate their in-memory mutations on this so a scan on the actual current
        // body doesn't pollute the previewed planet's display.
        public string DisplayedBody  { get; private set; } = "";
        public event EventHandler? PlanetListChanged;
        public string StarSystem        { get; private set; } = "";
        public string CachedBodyName    { get; private set; } = "";
        public bool   WasFootfalled     { get; private set; } = false;
        private bool  _pendingFirstFootfall     = false;
        private string _pendingFirstFootfallBody = "";
        private string _backfillSystem  = "";  // correct system derived during backfill
        private bool  _bodySetByStatus  = false;

        // Set to true when a new journal file is detected that contains no location
        // context (FSDJump / Location / Touchdown) — meaning the game just launched
        // into a fresh file before writing any position events.
        // While this flag is true, BackfillJournal will NOT fall back to the cached
        // body, because we have no way to verify the cached body is in the current
        // system. Cleared as soon as any real location event arrives.
        private bool _awaitingLocationFix = false;

        // Set to true while BackfillJournal is processing the newest (first) journal file.
        // Used by the cross-genus abandonment logic — only abandon within the newest file,
        // never let an older file's Log event remove dots placed by the newer file.
        private bool _backfillIsLatestFile = false;
        private string _backfillLastIncompleteGenus = "";

        private readonly object _planetLock = new();

        // When the user previews another planet, we stash the current body's full
        // in-memory state (including incomplete scans that aren't in cache yet) so
        // we can restore it exactly when they click back to CurrentBody.
        private List<ScannedOrganism>? _stashedOrganisms  = null;
        private List<string>?          _stashedGenera      = null;
        private string                 _stashedForBody     = "";

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
            _stashedOrganisms = null;
            _stashedGenera    = null;
            _stashedForBody   = "";
            SetDisplayedBody("");
        }

        // Forces the watcher to treat the current journal as brand-new:
        // clears all in-memory state, resets the journal file pointer so
        // BackfillJournal re-runs on the next JournalLoop tick, and clears
        // system/planet lists so they are rebuilt from scratch.
        // Use this when the app has picked up incorrect data after a journal
        // file switch, instead of restarting the app entirely.
        public void ForceRefresh()
        {
            Log.Write("ForceRefresh: user-requested full state reset");

            // Clear all body-level state
            ClearCurrentBody();

            // Clear system-level state
            StarSystem      = "";
            _backfillSystem = "";
            lock (_planetLock)
            {
                SystemBioPlanets.Clear();
                SystemGeoPlanets.Clear();
            }
            lock (_bodyBioSignals)
                _bodyBioSignals.Clear();

            // Reset journal tail so JournalLoop re-detects the current file and
            // runs BackfillJournal again — exactly as it does on first startup
            _currentJournalFile = "";
            _journalPosition    = 0;

            // Reset status cache so Status.json is re-read immediately
            _lastStatusModified = DateTime.MinValue;

            // Reset the statusReady signal so the journal loop waits for a
            // fresh position fix before proceeding, just like on first start
            _statusReady.Reset();

            // Mark that we have no location context — BackfillJournal will refuse
            // the cached-body fallback until a real location event arrives
            _awaitingLocationFix = true;

            Log.Write("ForceRefresh: reset complete — JournalLoop will re-backfill on next tick");

            // Fire events so the UI clears immediately without waiting for the next tick
            BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
            PlanetListChanged?.Invoke(this, EventArgs.Empty);
        }

        // Updates which body's data is sitting in the in-memory display lists.
        // Call this from every code path that does a wholesale clear or reload
        // of those lists, so live event handlers know whether to mutate them.
        private void SetDisplayedBody(string body)
        {
            body ??= "";
            if (!string.Equals(DisplayedBody, body, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"DisplayedBody: '{DisplayedBody}' → '{body}'");
                DisplayedBody = body;
            }
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

        // Watches for new Journal.*.log files being created by the game.
        // When a new file appears, triggers ForceRefresh after a short delay
        // to give the game time to write the file header before we read it.
        private FileSystemWatcher? _journalWatcher;

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
                SetDisplayedBody(cachedBody);
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

            // Watch for new journal files created by the game.
            // Elite Dangerous rolls to a new Journal.*.log on each game launch,
            // which can confuse the backfill logic. Detecting creation immediately
            // lets us trigger a clean ForceRefresh instead of relying on the
            // partial state-preservation path in the normal file-switch handler.
            try
            {
                if (Directory.Exists(_journalDir))
                {
                    _journalWatcher = new FileSystemWatcher(_journalDir, "Journal.*.log")
                    {
                        NotifyFilter           = NotifyFilters.FileName,
                        IncludeSubdirectories  = false,
                        EnableRaisingEvents    = true,
                    };
                    _journalWatcher.Created += OnNewJournalFileCreated;
                    Log.Write("JournalWatcher: watching for new Journal files");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"JournalWatcher: failed to start — {ex.Message}");
            }

            Log.Write("EliteWatcherService.Start() returning");
        }

        // ---------------------------------------------------------------
        // Poll Status.json every 30ms — catches every game write immediately
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
                Thread.Sleep(30);
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
                                SetDisplayedBody(destName);

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
                    // Only clear pending FF if we've moved to a different body than the one being scanned
                    if (!string.Equals(status.BodyName, _pendingFirstFootfallBody, StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingFirstFootfall     = false;
                        _pendingFirstFootfallBody = "";
                    }
                    // Full cache restore for the new body. List fields are cleared and
                    // reloaded UNCONDITIONALLY so stale entries from the previous body
                    // can't leak through. For Bio/Geo counts we prefer SystemBioPlanets /
                    // SystemGeoPlanets (populated from FSS/DSS) over the body's own cache,
                    // because the body may have known signal counts before it has any
                    // cached scans — and those lists are keyed by full body name, so
                    // there's no stale-carry-over risk.
                    var loaded = ScanCache.LoadForBody(CurrentBody);
                    lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(loaded.Organisms); }
                    lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(loaded.KnownGenera); }
                    lock (KnownGeoSites)    { KnownGeoSites.Clear();    KnownGeoSites.AddRange(loaded.GeoSites); }
                    // Populate CompletedGenera so sidebar Total Payout shows correctly
                    lock (CompletedGenera)
                    {
                        CompletedGenera.Clear();
                        foreach (var o in loaded.Organisms.Where(o => o.IsComplete)
                                             .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                             .Select(g => g.First()))
                            CompletedGenera.Add(o);
                    }
                    lock (_planetLock)
                    {
                        var bp = SystemBioPlanets.FirstOrDefault(p =>
                            string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                        BiologyCount = bp?.BioCount ?? loaded.BiologyCount;
                        var gp = SystemGeoPlanets.FirstOrDefault(p =>
                            string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                        GeologyCount = gp?.GeoCount ?? loaded.GeologyCount;
                    }
                    // Restore First Footfall from cache — game permanence, once true always true
                    WasFootfalled = loaded.WasFootfalled;
                    SetDisplayedBody(CurrentBody);
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
                        lock (KnownGeoSites)    KnownGeoSites.Clear();
                        BiologyCount              = 0;
                        GeologyCount              = 0;
                        WasFootfalled             = false;
                        _pendingFirstFootfall     = false;
                        _pendingFirstFootfallBody = "";
                        SetDisplayedBody("");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                }

                StatusUpdated?.Invoke(this, new StatusUpdatedEventArgs { Status = status });
            }
            catch { }
        }

        // Called when user clicks a planet in the Biological Sites / Geological Sites panel.
        // Loads that planet's cache data into display state.
        //
        // Clicking the CURRENT body is allowed and intentional: it re-loads the body's
        // cache, restoring any sites that were live-scanned while another planet was being
        // previewed. The cache (via SaveGeoSite / SaveBodyMeta) is the source of truth for
        // per-body scan data; this method makes the display catch up to it.
        public void PreviewPlanet(string fullBodyName)
        {
            if (string.IsNullOrEmpty(fullBodyName)) return;

            bool returningToCurrent = string.Equals(fullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase);

            // Stash current in-memory state (including incomplete scans) before
            // wiping ScannedOrganisms for the preview — but only if we haven't
            // already stashed (i.e. don't overwrite the stash with a different
            // planet's preview data).
            if (!string.IsNullOrEmpty(CurrentBody) &&
                !returningToCurrent &&
                string.IsNullOrEmpty(_stashedForBody))
            {
                lock (ScannedOrganisms)
                    _stashedOrganisms = ScannedOrganisms.ToList();
                lock (KnownGenera)
                    _stashedGenera = KnownGenera.ToList();
                _stashedForBody = CurrentBody;
                Log.Write($"PreviewPlanet: stashed {_stashedOrganisms.Count} organisms for '{CurrentBody}'");
            }

            // If returning to CurrentBody, restore the stash rather than loading from cache
            // so incomplete scans that haven't been cached yet are preserved.
            if (returningToCurrent && !string.IsNullOrEmpty(_stashedForBody))
            {
                lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(_stashedOrganisms!); }
                lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(_stashedGenera!); }
                // Rebuild CompletedGenera from restored organisms
                lock (CompletedGenera)
                {
                    CompletedGenera.Clear();
                    foreach (var o in _stashedOrganisms!.Where(o => o.IsComplete)
                                         .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                         .Select(g => g.First()))
                        CompletedGenera.Add(o);
                }
                // Reload geo sites from cache (geo is always cached immediately on discovery)
                var cachedCurrent = ScanCache.LoadForBody(CurrentBody);
                lock (KnownGeoSites) { KnownGeoSites.Clear(); KnownGeoSites.AddRange(cachedCurrent.GeoSites); }

                lock (_planetLock)
                {
                    var bp = SystemBioPlanets.FirstOrDefault(p =>
                        string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                    BiologyCount = bp?.BioCount ?? cachedCurrent.BiologyCount;
                    var gp = SystemGeoPlanets.FirstOrDefault(p =>
                        string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                    GeologyCount = gp?.GeoCount ?? cachedCurrent.GeologyCount;
                }
                WasFootfalled = cachedCurrent.WasFootfalled;

                // Clear stash — we're back on CurrentBody
                _stashedOrganisms = null;
                _stashedGenera    = null;
                _stashedForBody   = "";

                Log.Write($"PreviewPlanet: restored stash for '{CurrentBody}' organisms={ScannedOrganisms.Count}");
                SetDisplayedBody(CurrentBody);
                BodyChanged?.Invoke(this, new BodyChangedEventArgs
                    { BodyName = CurrentBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                return;
            }

            // Normal preview of a different planet — load from cache
            var cached = ScanCache.LoadForBody(fullBodyName);
            lock (KnownGenera)      { KnownGenera.Clear();      foreach (var g in cached.KnownGenera)  KnownGenera.Add(g); }
            lock (ScannedOrganisms) { ScannedOrganisms.Clear(); foreach (var o in cached.Organisms)    ScannedOrganisms.Add(o); }
            lock (KnownGeoSites)    { KnownGeoSites.Clear();    foreach (var g in cached.GeoSites)     KnownGeoSites.Add(g); }
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
            SetDisplayedBody(fullBodyName);

            Log.Write($"PreviewPlanet: '{fullBodyName}' genera={KnownGenera.Count} scans={ScannedOrganisms.Count} geo={KnownGeoSites.Count}");
            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                { BodyName = fullBodyName, BioCount = BiologyCount, GeoCount = GeologyCount });
        }

        // ---------------------------------------------------------------
        // Fired by FileSystemWatcher when a new Journal.*.log is created.
        // Waits 1.5 seconds before triggering ForceRefresh so the game has
        // time to write at least the file header before we try to read it.
        private void OnNewJournalFileCreated(object sender, FileSystemEventArgs e)
        {
            if (_cts.IsCancellationRequested) return;
            Log.Write($"JournalWatcher: new file detected — {Path.GetFileName(e.FullPath)}, scheduling ForceRefresh in 1.5s");
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, _cts.Token);
                    Log.Write("JournalWatcher: delay elapsed, triggering ForceRefresh");
                    ForceRefresh();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Write($"JournalWatcher: ForceRefresh error — {ex.Message}"); }
            });
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
                        // Also track from Disembark and Location — covers sessions where player
                        // started landed (no Touchdown) or on foot (no ApproachBody/Touchdown).
                        // Without this, Analyse events for genera completed in those sessions
                        // would not be found in journalCompleted, causing CompletedCount to be wrong.
                        if (ev == "Disembark" || ev == "Location")
                        {
                            var locBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? "";
                            if (!string.IsNullOrEmpty(locBody)) trackBody = locBody;
                        }
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
                                // If short name equals full body name, system was empty when
                                // GetShortBodyName was called — the entry would display the full
                                // system+body string in the sidebar. Skip it so a subsequent
                                // BackfillSystemPlanets call with a valid system can add it correctly.
                                if (string.Equals(shortName, body, StringComparison.OrdinalIgnoreCase))
                                {
                                    Log.Write($"BackfillSystemPlanets: skipping bio '{body}' — system name unknown, short name would be full body name");
                                }
                                else
                                {
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
                var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByDescending(f => f)
                    .Take(20)  // search more files to catch multi-session planets
                    .ToArray();

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

                // If we scanned the new journal and found no location context at all
                // (no FSDJump, no Touchdown, no Disembark), raise the flag so the
                // cached-body fallback below is suppressed until live events arrive.
                if (string.IsNullOrEmpty(CurrentBody) && !latestEventWasFSDJump)
                {
                    bool newJournalHasLocationContext = false;
                    foreach (var line in SafeReadAllLines(latestFile))
                    {
                        var o = TryParse(line); if (o == null) continue;
                        var ev = o.Value<string>("event");
                        if (ev == "FSDJump" || ev == "CarrierJump" || ev == "Location" ||
                            ev == "Touchdown" || ev == "Disembark")
                        {
                            newJournalHasLocationContext = true;
                            break;
                        }
                    }
                    if (!newJournalHasLocationContext)
                    {
                        _awaitingLocationFix = true;
                        Log.Write("Backfill: new journal has no location events — setting _awaitingLocationFix, will wait for live position");
                    }
                }

                if (string.IsNullOrEmpty(CurrentBody))
                {
                    // If we are waiting for a location fix (new journal, no position events
                    // yet), do NOT fall back to cache — we cannot verify which system we are
                    // in, so any cached body could be from a previous session entirely.
                    // Live events (FSDJump / Location / Touchdown) will clear this flag and
                    // establish the correct body without needing the cached fallback.
                    if (_awaitingLocationFix)
                    {
                        Log.Write($"Backfill: new journal has no location context yet — refusing cached body fallback, waiting for live events");
                        return;
                    }

                    // Only fall back to the cached body if we're still in the same system.
                    // An empty StarSystem means we have no system information at all — treat
                    // that the same as a system mismatch (do not accept the cached body).
                    bool cachedBodyIsInCurrentSystem =
                        !string.IsNullOrEmpty(StarSystem) &&
                        !string.IsNullOrEmpty(CachedBodyName) &&
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

                _backfillLastIncompleteGenus = "";
                bool _isFirstBackfillFile = true;
                foreach (var file in files)
                {
                    _backfillIsLatestFile = _isFirstBackfillFile;
                    _isFirstBackfillFile  = false;
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
                                // Location events include Latitude/Longitude when the player starts
                                // landed or on foot — use them so subsequent ScanOrganic events
                                // in the same journal have a valid position (covers StartLanded sessions
                                // where there is no Touchdown event to set activeLat/activeLon).
                                if (obj["Latitude"]  != null) activeLat = obj.Value<double>("Latitude");
                                if (obj["Longitude"] != null) activeLon = obj.Value<double>("Longitude");
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

                // End-of-backfill abandonment cleanup: if the newest journal had an
                // in-progress scan of a specific genus, remove all OTHER incomplete genera
                // from ScannedOrganisms. These represent abandoned scan attempts from older
                // sessions that were superseded by the current in-progress scan.
                if (!string.IsNullOrEmpty(_backfillLastIncompleteGenus))
                {
                    lock (ScannedOrganisms)
                    {
                        var abandoned = ScannedOrganisms
                            .Where(o => !o.IsComplete &&
                                   !string.Equals(o.Genus, _backfillLastIncompleteGenus,
                                       StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (abandoned.Count > 0)
                        {
                            foreach (var a in abandoned) ScannedOrganisms.Remove(a);
                            Log.Write($"Backfill: removed {abandoned.Count} abandoned incomplete dot(s) for genera other than '{_backfillLastIncompleteGenus}'");
                        }
                    }
                }

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
                        lock (KnownGeoSites)    KnownGeoSites.Clear();
                        BiologyCount              = 0;
                        GeologyCount              = 0;
                        CurrentBody               = "";
                        WasFootfalled             = false;
                        _pendingFirstFootfall     = false;
                        _pendingFirstFootfallBody = "";
                        SetDisplayedBody("");

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
                            if (ev == "SAASignalsFound" && !string.IsNullOrEmpty(lastDssBody))
                            {
                                // Full cache restore for the post-leave targeted body — same
                                // pattern as BackfillSystemPlanets so FF, scans, completed
                                // genera, and bio/geo counts all survive an app-running /
                                // game-restart scenario.
                                var postLeaveCached = ScanCache.LoadForBody(lastDssBody);

                                // Re-extract bio/geo from this SAA event's Signals array so the
                                // sidebar's headline counts reflect the post-leave targeted body.
                                int postLeaveBio = 0, postLeaveGeo = 0;
                                var postLeaveSignals = o["Signals"];
                                if (postLeaveSignals != null)
                                {
                                    foreach (var sig in postLeaveSignals)
                                    {
                                        var t = sig.Value<string>("Type") ?? "";
                                        var c = sig.Value<int>("Count");
                                        if (t.Contains("Biological"))  postLeaveBio = c;
                                        else if (t.Contains("Geological")) postLeaveGeo = c;
                                    }
                                }

                                // Seed KnownGenera from the journal event first (authoritative
                                // genus names), then fall back to cache if journal had none.
                                var journalGenera = new List<string>();
                                var genuses = o["Genuses"];
                                if (genuses != null)
                                {
                                    foreach (var g in genuses)
                                    {
                                        var gName = g.Value<string>("Genus_Localised") ?? g.Value<string>("Genus") ?? "";
                                        if (!string.IsNullOrEmpty(gName)) journalGenera.Add(gName);
                                    }
                                }
                                var generaToUse = journalGenera.Count > 0 ? journalGenera : postLeaveCached.KnownGenera;

                                lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(generaToUse); }
                                lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(postLeaveCached.Organisms); }
                                lock (KnownGeoSites)    { KnownGeoSites.Clear();    KnownGeoSites.AddRange(postLeaveCached.GeoSites); }
                                lock (CompletedGenera)
                                {
                                    CompletedGenera.Clear();
                                    foreach (var comp in postLeaveCached.Organisms
                                                 .Where(org => org.IsComplete)
                                                 .GroupBy(org => org.Genus, StringComparer.OrdinalIgnoreCase)
                                                 .Select(grp => grp.First()))
                                        CompletedGenera.Add(comp);
                                }
                                BiologyCount         = postLeaveBio;
                                GeologyCount         = postLeaveGeo;
                                WasFootfalled        = postLeaveCached.WasFootfalled;
                                TargetedBody         = lastDssBody;
                                TargetedBodyBioCount = postLeaveBio;
                                SetDisplayedBody(lastDssBody);
                                Log.Write($"Backfill: restored sidebar for post-leave body '{lastDssBody}' genera={generaToUse.Count} scans={postLeaveCached.Organisms.Count} bio={postLeaveBio} geo={postLeaveGeo} ff={WasFootfalled}");
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"Backfill leave-check error: {ex.Message}"); }

                // After backfill, the in-memory display lists hold CurrentBody's data
                // (unless the leave-check branch ran a post-leave SAA restore, which set
                // DisplayedBody itself). Only sync DisplayedBody here if it wasn't already
                // set to something other than CurrentBody by that branch.
                if (!string.IsNullOrEmpty(CurrentBody) &&
                    (string.IsNullOrEmpty(DisplayedBody) ||
                     string.Equals(DisplayedBody, CurrentBody, StringComparison.OrdinalIgnoreCase)))
                {
                    SetDisplayedBody(CurrentBody);
                }

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
                    if (!backfill)
                    {
                        var disembarkBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? CurrentBody;

                        // If we were waiting for a location fix, Disembark gives us the body —
                        // clear the flag and trigger a targeted backfill to recover any
                        // incomplete scan dots from the previous journal session.
                        // This covers: launched while in ship on surface, or launched while in SRV.
                        if (_awaitingLocationFix && !string.IsNullOrEmpty(disembarkBody))
                        {
                            _awaitingLocationFix = false;
                            CurrentBody = disembarkBody;
                            Log.Write($"Disembark: location fix received for '{disembarkBody}' — clearing _awaitingLocationFix, triggering backfill");
                            var disKnownSystem = StarSystem;
                            Task.Run(() =>
                            {
                                try
                                {
                                    BackfillJournal(_currentJournalFile);
                                    if (!string.IsNullOrEmpty(disKnownSystem) &&
                                        !string.Equals(disKnownSystem, StarSystem, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Log.Write($"Disembark backfill: restoring StarSystem to '{disKnownSystem}' (was corrupted to '{StarSystem}' by older journal events)");
                                        StarSystem = disKnownSystem;
                                    }
                                }
                                catch (Exception ex) { Log.Write($"Disembark backfill error: {ex.Message}"); }
                            });
                        }

                        // Player stepped off ship — if pending FF, now confirm it
                        if (_pendingFirstFootfall)
                        {
                            WasFootfalled             = true;
                            _pendingFirstFootfall     = false;
                            _pendingFirstFootfallBody = "";
                            Log.Write($"Disembark: First Footfall confirmed for '{disembarkBody}'");
                            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                { BodyName = disembarkBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                        }
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

                    // Update planet bio list for current system.
                    // Skip during backfill — BackfillSystemPlanets builds the list correctly
                    // after backfill completes with a valid StarSystem. Updating it mid-backfill
                    // risks adding wrong short names when StarSystem is empty (e.g. post-ForceRefresh).
                    if (bio > 0 && !backfill)
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
                                if (match != null && !string.IsNullOrEmpty(match.ShortName))
                                {
                                    // Derive system from the existing entry's verified FullBodyName/ShortName
                                    // so we're never relying on StarSystem which may be stale from backfill
                                    var suffix = " " + match.ShortName;
                                    if (match.FullBodyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                        systemForShortName = match.FullBodyName.Substring(0, match.FullBodyName.Length - suffix.Length);
                                }
                                else if (match == null)
                                {
                                    // Prefer StarSystem directly when it's a valid prefix of body —
                                    // StarSystem is now reliably maintained (see Location/Disembark
                                    // backfill restoration fixes). Only fall back to the token-strip
                                    // loop if StarSystem is empty or doesn't match, since that loop
                                    // can over-match when CurrentBody equals body itself (the planet
                                    // you're currently standing on), incorrectly consuming part of the
                                    // body's own designation (e.g. orbit number) into the "system" name.
                                    if (!string.IsNullOrEmpty(StarSystem) &&
                                        body.StartsWith(StarSystem, StringComparison.OrdinalIgnoreCase))
                                    {
                                        systemForShortName = StarSystem;
                                    }
                                    else
                                    {
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

                    // Update SystemGeoPlanets live for geo-only or mixed planets.
                    // Skip during backfill for same reason as bio planets above.
                    if (geo > 0 && !backfill)
                    {
                        string systemForGeo = StarSystem;
                        // Only fall back to the token-strip loop if StarSystem isn't already a
                        // valid prefix of body. The loop can over-match when CurrentBody equals
                        // body itself (standing on the planet being scanned), incorrectly consuming
                        // part of the body's own designation (e.g. orbit number) into "system",
                        // which produces a too-short short name (e.g. "A" instead of "1 A").
                        if (!(!string.IsNullOrEmpty(StarSystem) &&
                              body.StartsWith(StarSystem, StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrEmpty(CurrentBody))
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

                    // SAASignalsFound (detailed surface scan) includes a Genuses array.
                    // Build the event's genera into a local list FIRST so we can save it to the
                    // correct body's cache regardless of whether it matches CurrentBody. The global
                    // KnownGenera (display state) is only updated if this event is for the body
                    // we're tracking (or we're in orbit/space with no current body).
                    var eventGenera = new List<string>();
                    if (evt == "SAASignalsFound")
                    {
                        var genuses = obj["Genuses"];
                        if (genuses != null)
                        {
                            foreach (var g in genuses)
                            {
                                var genusLoc = g.Value<string>("Genus_Localised")
                                            ?? g.Value<string>("Genus") ?? "";
                                if (!string.IsNullOrEmpty(genusLoc))
                                {
                                    var cleaned = CleanInternalName(genusLoc);
                                    if (!eventGenera.Contains(cleaned))
                                        eventGenera.Add(cleaned);
                                }
                            }

                            // Only mutate the global display state if this event is for the body
                            // we're on (or we're in orbit/space, in which case it becomes the
                            // targeted body for sidebar display).
                            bool eventMatchesCurrentBody =
                                string.Equals(body, CurrentBody, StringComparison.OrdinalIgnoreCase)
                                || string.IsNullOrEmpty(CurrentBody);
                            if (eventMatchesCurrentBody && eventGenera.Count > 0)
                            {
                                lock (KnownGenera)
                                {
                                    KnownGenera.Clear();
                                    KnownGenera.AddRange(eventGenera);
                                }
                                Log.Write($"SAASignalsFound: {eventGenera.Count} genera for '{body}': {string.Join(", ", eventGenera)}");

                                // If in space (no current body), set TargetedBody so sidebar shows genera
                                if (string.IsNullOrEmpty(CurrentBody) && !string.IsNullOrEmpty(body))
                                {
                                    TargetedBody         = body;
                                    TargetedBodyBioCount = bio;
                                }
                            }
                            else if (eventGenera.Count > 0)
                            {
                                Log.Write($"SAASignalsFound: {eventGenera.Count} genera for '{body}' (not current body '{CurrentBody}', display unchanged)");
                            }
                        }
                    }

                    if (!backfill)
                    {
                        // Persist to cache for the body in the EVENT (not necessarily CurrentBody).
                        //  - Genera: pass eventGenera for SAASignalsFound, null for FSSBodySignals
                        //    (FSSBodySignals has no Genuses array; SaveBodyMeta will preserve cached genera).
                        //  - WasFootfalled: only meaningful for the body we're on. Pass null otherwise
                        //    so SaveBodyMeta preserves the cached FF value (game permanence).
                        List<string>? generaToSave = (evt == "SAASignalsFound") ? eventGenera : null;
                        bool? ffToSave = string.Equals(body, CurrentBody, StringComparison.OrdinalIgnoreCase)
                            ? (bool?)WasFootfalled
                            : null;
                        ScanCache.SaveBodyMeta(body, bio, generaToSave, ffToSave);

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

                    bool isCurrentBody   = string.Equals(codexBody, CurrentBody, StringComparison.OrdinalIgnoreCase);
                    bool isDisplayedBody = string.Equals(codexBody, DisplayedBody, StringComparison.OrdinalIgnoreCase);
                    // During backfill, DisplayedBody is only synced to CurrentBody at the very end
                    // of BackfillJournal — so while the file loop is still running, DisplayedBody
                    // can be empty even though we ARE backfilling CurrentBody (not previewing
                    // something else). Treat that case as displayed too, so geo sites discovered
                    // on the body actually being loaded aren't silently dropped.
                    bool isLoadingCurrentBody = backfill && isCurrentBody && string.IsNullOrEmpty(DisplayedBody);

                    // Update in-memory KnownGeoSites if we're displaying this body, OR if we're
                    // mid-backfill for this exact body and DisplayedBody just hasn't synced yet.
                    // Otherwise the user is previewing a different planet and adding this site to
                    // in-memory would pollute the previewed display.
                    if (isDisplayedBody || isLoadingCurrentBody)
                    {
                        lock (KnownGeoSites)
                        {
                            if (!KnownGeoSites.Any(g => g.EntryID == entryID))
                            {
                                KnownGeoSites.Add(site);
                                Log.Write($"CodexEntry geo: '{nameLoc}' on '{codexBody}' payout={payout}");
                            }
                        }
                    }
                    else if (isCurrentBody)
                    {
                        Log.Write($"CodexEntry geo: '{nameLoc}' on '{codexBody}' payout={payout} (previewing '{DisplayedBody}', in-memory not updated)");
                    }

                    if (!backfill)
                    {
                        // For the cache save's geoCount metadata, use codexBody's actual
                        // GeoCount from SystemGeoPlanets — not the in-memory GeologyCount,
                        // which reflects the displayed (possibly previewed) body.
                        int actualGeoCount;
                        lock (_planetLock)
                        {
                            var gpForSave = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, codexBody, StringComparison.OrdinalIgnoreCase));
                            actualGeoCount = gpForSave?.GeoCount ?? (isDisplayedBody ? GeologyCount : 0);
                        }
                        ScanCache.SaveGeoSite(codexBody, site, actualGeoCount);
                        if (payout > 0) EarningsTracker.AddEarning(payout);

                        // Update DiscoveredCount from the cache (now including the just-saved
                        // site) rather than from in-memory KnownGeoSites, so the sidebar's
                        // greying logic stays correct even when a different planet is being
                        // previewed.
                        lock (_planetLock)
                        {
                            var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, codexBody, StringComparison.OrdinalIgnoreCase));
                            if (gp != null)
                            {
                                var freshCache = ScanCache.LoadForBody(codexBody);
                                gp.DiscoveredCount = freshCache.GeoSites
                                    .Select(g => g.EntryID).Distinct().Count();
                            }
                        }

                        if (isDisplayedBody)
                        {
                            // Normal case: refresh the planet panel/sidebar for the displayed body.
                            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                { BodyName = DisplayedBody, BioCount = BiologyCount, GeoCount = GeologyCount });
                        }
                        else if (isCurrentBody)
                        {
                            // We're previewing another planet but DiscoveredCount changed for the
                            // current body — nudge the planet list so the GEOLOGICAL SITES row's
                            // grey-out state stays in sync, without resetting the preview.
                            PlanetListChanged?.Invoke(this, EventArgs.Empty);
                        }
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

                    // During backfill, skip genera that are already fully complete.
                    // Check BOTH CompletedGenera and ScannedOrganisms:
                    // - CompletedGenera is populated by Analyse events (even when dots had
                    //   no position and were skipped), so it's the authoritative completion flag.
                    // - ScannedOrganisms check covers the case where dots exist and are greyed.
                    // This prevents older journal scans from re-adding dots for genera that
                    // were completed in a newer journal (e.g. after a game restart mid-scan).
                    if (backfill)
                    {
                        bool alreadyComplete;
                        lock (CompletedGenera)
                            alreadyComplete = CompletedGenera.Any(o =>
                                string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase));
                        if (!alreadyComplete)
                        {
                            // Require ScanCount==3 to distinguish genuine completions from corrupt
                            // cache entries written by the old "grey on switch" code (which set
                            // IsComplete=true with ScanCount=1 when the player switched genera).
                            lock (ScannedOrganisms)
                                alreadyComplete = ScannedOrganisms.Any(o =>
                                    string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                    && o.IsComplete && o.ScanCount == 3)
                                    && !ScannedOrganisms.Any(o =>
                                    string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                    && !o.IsComplete);
                        }
                        if (alreadyComplete)
                        {
                            Log.Write($"Backfill: skipping {genus} — already complete");
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
                    // never fall back to current ship position for historical events.
                    // Exception: Analyse events don't add dots — they only mark a genus complete
                    // in CompletedGenera. Always allow them through regardless of position so that
                    // a genus completed in a newer journal (with no position context) is correctly
                    // recognised as done when older journals are processed for dot reconstruction.
                    if (useLat == 0 && useLon == 0 && scanNum != 4)
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
                        // When a Log scan fires for a new genus, any other genus that has
                        // incomplete dots (no Analyse yet) is considered abandoned — remove those
                        // dots entirely so the Bio Survey shows 0 pips for that genus.
                        // During backfill: only abandon within the newest journal file. Older files
                        // are processed after the newest, so a Log from an old session must NOT
                        // remove dots correctly placed by the newer session.
                        if (scanNum == 1 && (!backfill || _backfillIsLatestFile))
                        {
                            var abandonedGenera = ScannedOrganisms
                                .Where(o => !string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                         && !o.IsComplete)
                                .Select(o => o.Genus)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            foreach (var oldGenus in abandonedGenera)
                            {
                                var toRemove = ScannedOrganisms
                                    .Where(o => string.Equals(o.Genus, oldGenus, StringComparison.OrdinalIgnoreCase)
                                             && !o.IsComplete)
                                    .ToList();
                                foreach (var o in toRemove) ScannedOrganisms.Remove(o);
                                Log.Write($"ScanOrganic: removed abandoned dots for '{oldGenus}' — switched to '{genus}'");
                            }
                            if (!backfill)
                                ScanCache.SaveForBody(CurrentBody, ScannedOrganisms, BiologyCount, KnownGenera, WasFootfalled);
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

                            // If no dots exist for this genus (Log/Sample were all skipped due to
                            // missing or positionless journal data), add a synthetic completion record
                            // so the Bio Survey correctly shows the genus as complete. Latitude=0/Longitude=0
                            // means HasPosition=false, so the radar renderer skips it entirely — no
                            // spurious dot appears on screen.
                            if (!genusOrgs.Any())
                            {
                                ScannedOrganisms.Add(new ScannedOrganism
                                {
                                    Genus      = genus,
                                    Species    = species,
                                    ScanCount  = 3,
                                    IsComplete = true,
                                    Latitude   = 0.0,
                                    Longitude  = 0.0,
                                });
                                Log.Write($"Analyse: no dots for {genus} — added synthetic completion record (position unavailable)");
                            }

                            // Track the last incomplete genus started in the newest file,
                            // so the end-of-backfill cleanup can remove other abandoned genera.
                            if (backfill && _backfillIsLatestFile && scanNum != 4)
                                _backfillLastIncompleteGenus = genus;

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

                            // If this is a fresh Log (scan 1), clear any previous dots for this
                            // genus — including corrupt IsComplete=true/ScanCount<3 entries written
                            // by the old "grey on switch" code. A genuine completion always has
                            // ScanCount==3, so anything with ScanCount<3 and IsComplete=true is
                            // safe to remove when starting a new scan sequence for the same genus.
                            if (scanNum == 1)
                            {
                                var toRemove = ScannedOrganisms
                                    .Where(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase)
                                             && (!o.IsComplete || o.ScanCount < 3))
                                    .ToList();
                                if (toRemove.Count > 0)
                                {
                                    foreach (var r in toRemove) ScannedOrganisms.Remove(r);
                                    Log.Write($"Cleared {toRemove.Count} stale/corrupt dots for '{genus}' — starting fresh scan");
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
                        // A real Touchdown/ApproachBody tells us exactly where we are —
                        // clear the location-fix flag so future backfills can trust the cache
                        if (_awaitingLocationFix)
                        {
                            _awaitingLocationFix = false;
                            Log.Write($"Touchdown/ApproachBody: location fix received for '{body}' — clearing _awaitingLocationFix");
                        }
                        CurrentBody = body;
                        var loaded = ScanCache.LoadForBody(CurrentBody);
                        lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(loaded.Organisms); }
                        lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(loaded.KnownGenera); }
                        // Rebuild CompletedGenera from completed organisms so sidebar Total Payout
                        // shows correctly after returning to a body with previous completed scans
                        lock (CompletedGenera)
                        {
                            CompletedGenera.Clear();
                            foreach (var o in loaded.Organisms.Where(o => o.IsComplete)
                                                 .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                                 .Select(g => g.First()))
                                CompletedGenera.Add(o);
                        }
                        lock (KnownGeoSites)    { KnownGeoSites.Clear();    KnownGeoSites.AddRange(loaded.GeoSites); }
                        // Prefer SystemBioPlanets / SystemGeoPlanets (FSS/DSS authoritative)
                        // over the body's own cache for counts — a body may have known
                        // signal counts before it has any cached scans. Lookup is keyed
                        // by full body name, so no stale-carry-over risk.
                        lock (_planetLock)
                        {
                            var bp = SystemBioPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                            BiologyCount = bp?.BioCount ?? loaded.BiologyCount;
                            var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, CurrentBody, StringComparison.OrdinalIgnoreCase));
                            GeologyCount = gp?.GeoCount ?? loaded.GeologyCount;
                        }
                        // Restore First Footfall from cache — game permanence, once true always true.
                        // Use direct assignment (not one-way OR) so we don't carry stale true
                        // from a previous body. SaveBodyMeta enforces sticky-true at the cache layer.
                        WasFootfalled = loaded.WasFootfalled;
                        SetDisplayedBody(CurrentBody);
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = body, BioCount = BiologyCount, GeoCount = GeologyCount });
                    }
                    break;
                }

                case "Location":
                case "CarrierJump":
                {
                    var sys = obj.Value<string>("StarSystem") ?? "";
                    if (!string.IsNullOrEmpty(sys))
                    {
                        StarSystem = sys;
                        // Location/CarrierJump confirms system — clear the location-fix flag.
                        // Also extract the body name (present when OnFoot=true or landed) and
                        // trigger a targeted backfill to recover any incomplete scan dots from
                        // the previous journal. This covers: launched while on foot on a planet.
                        if (!backfill && _awaitingLocationFix)
                        {
                            _awaitingLocationFix = false;
                            var locBody = obj.Value<string>("Body") ?? "";
                            var bodyType = obj.Value<string>("BodyType") ?? "";
                            var locOnFoot = obj.Value<bool?>("OnFoot") ?? false;
                            var locHasLatLon = obj["Latitude"] != null && obj["Longitude"] != null;
                            // BodyType=="Planet" alone is NOT proof of being ON the planet — Location
                            // events report the nearest body even while in supercruise just passing by
                            // (no OnFoot, no Latitude/Longitude, often followed by StartJump/SupercruiseEntry).
                            // Require OnFoot=true OR Latitude/Longitude present to confirm the player is
                            // actually on the surface before treating this as the current body and
                            // triggering a backfill (which can otherwise pull stale scan data and
                            // incorrectly activate First Footfall for a planet never actually visited
                            // this session).
                            if (!string.IsNullOrEmpty(locBody) && bodyType == "Planet" && (locOnFoot || locHasLatLon))
                            {
                                CurrentBody = locBody;
                                Log.Write($"Location/CarrierJump: location fix received — on planet '{locBody}' in '{sys}', triggering backfill");
                                var locKnownSystem = sys;
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        BackfillJournal(_currentJournalFile);
                                        // BackfillJournal may update StarSystem from older journal events
                                        // (e.g. FSDJumps to previous systems). Restore the confirmed system
                                        // from the Location event that triggered this backfill.
                                        if (!string.IsNullOrEmpty(locKnownSystem) &&
                                            !string.Equals(locKnownSystem, StarSystem, StringComparison.OrdinalIgnoreCase))
                                        {
                                            Log.Write($"Location backfill: restoring StarSystem to '{locKnownSystem}' (was corrupted to '{StarSystem}' by older journal events)");
                                            StarSystem = locKnownSystem;
                                        }
                                    }
                                    catch (Exception ex) { Log.Write($"Location backfill error: {ex.Message}"); }
                                });
                            }
                            else
                            {
                                Log.Write($"Location/CarrierJump: location fix received for system '{sys}' — clearing _awaitingLocationFix");
                            }
                        }
                    }
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
                        _stashedOrganisms     = null;
                        _stashedGenera        = null;
                        _stashedForBody       = "";
                        SetDisplayedBody("");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                    break;
                }

                case "FSDJump":
                {
                    if (!backfill)
                    {
                        // FSDJump confirms the current system — clear the location-fix flag
                        if (_awaitingLocationFix)
                        {
                            _awaitingLocationFix = false;
                            Log.Write("FSDJump: location fix received — clearing _awaitingLocationFix");
                        }
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
                        _stashedOrganisms     = null;
                        _stashedGenera        = null;
                        _stashedForBody       = "";
                        SetDisplayedBody("");
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
            _journalWatcher?.Dispose();
            _statusReady.Dispose();
        }
    }
}
