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

        // Info panel (STAR/PLANET/DESTINATION) — live-session-only state, not persisted to ScanCache
        public event EventHandler? StarScanUpdated;
        public event EventHandler? PlanetTargetUpdated;
        public event EventHandler? DestinationUpdated;
        public BodyScanDetail?  CurrentStarDetail    { get; private set; }
        // Looked up from _bodyScanDetails by current TargetedBody, so a planet targeted
        // AFTER it was already scanned this session still shows its detail immediately.
        // Excludes stars: the primary star itself can also be the nav-panel target (e.g.
        // targeting it from the system map), and that should keep showing STAR mode/detail,
        // not be mistaken for a planet target.
        public BodyScanDetail?  TargetedPlanetDetail =>
            GetBodyDetail(TargetedBody) is { IsStar: false } d ? d : null;
        // Same lookup, but for the opposite case: the targeted body IS a star. In a multi-star
        // system this can be a secondary/tertiary star (CurrentStarDetail only ever tracks the
        // primary), so without this a targeted B/C star matched neither TargetedPlanetDetail
        // (excluded, it's a star) nor CurrentStarDetail (wrong body name) and fell through to
        // whatever destination happened to exist instead of showing STAR mode.
        public BodyScanDetail?  TargetedStarDetail =>
            GetBodyDetail(TargetedBody) is { IsStar: true } d ? d : null;
        // The body actually under the player's feet (Status.json BodyName) rather than whatever
        // is nav-panel targeted — while landed there's usually no in-system target at all, so
        // without this the PLANET tab had nothing to show for the one body most worth showing.
        public BodyScanDetail?  CurrentBodyDetail =>
            GetBodyDetail(CurrentBody) is { IsStar: false } cd ? cd : null;
        public DestinationInfo? CurrentDestination    { get; private set; }
        public DateTime         PlanetTargetedAt      { get; private set; }
        public DateTime         FsdTargetedAt         { get; private set; }
        // When the current game session finished loading (the journal's LoadGame event). Elite re-announces
        // the active route target (NavRoute + FSDTarget) about a minute after loading, which looks exactly like
        // the player just targeting a jump - see the FSDTarget handler for why that must not count.
        private DateTime _lastLoadGameAt;
        // Bumped on every real system change. A destination route that was only queued BEFORE
        // this arrival (the common auto-route case: the next hop's FSDTarget fires mid-flight,
        // before the FSDJump that confirms arrival) shouldn't keep forcing DESTINATION mode
        // once you've landed and are looking around the new system — that's what MainWindow.
        // ComputeMode uses this for. The route data itself (CurrentDestination) is untouched;
        // this only affects which mode auto-shows.
        public DateTime         SystemArrivedAt       { get; private set; }
        // True from the moment the FSD starts charging for an actual hyperspace jump (the
        // "StartJump" event) until arrival — forces DESTINATION mode outright, overriding
        // whatever in-system target happens to be more "recent" by timestamp. Supercruise
        // (boosted or not) never sets this.
        public bool             IsChargingJump        { get; private set; }
        // Armed by the FSDJump handler, cleared once Status.json's charging flag has been seen
        // false (or after a few seconds) — see the ReadStatus comment where it's consumed.
        private volatile bool   _chargeFlagStaleSinceArrival;
        private DateTime        _chargeStaleSetAtUtc;
        private bool            _loggedStaleChargeSuppress;

        // Assigns (and remembers) a random belt art variant (1-5) for this body name, so the
        // same belt keeps the same look for as long as it's targeted this session.
        public int GetBeltVariant(string bodyName)
        {
            lock (_beltVariants)
            {
                if (!_beltVariants.TryGetValue(bodyName, out var variant))
                {
                    variant = _beltVariantRandom.Next(1, 6);
                    _beltVariants[bodyName] = variant;
                }
                return variant;
            }
        }

        // How many numbered art variants (<code>_1.png .. <code>_N.png) exist per planet icon
        // code, on top of the original base <code>.png. Classes not listed here have no variant
        // pool and always render their base image.
        private static readonly Dictionary<string, int> PlanetVariantCounts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ICY"] = 6, ["HMC"] = 6, ["RBD"] = 6, ["RIB"] = 6, ["MRB"] = 6,
            ["WTR"] = 3, ["AMW"] = 3, ["ELW"] = 3,
            ["GG1"] = 2, ["GG2"] = 2, ["GG3"] = 2, ["GG4"] = 2, ["GG5"] = 2,
            ["GGA"] = 2, ["GGH"] = 2, ["GGW"] = 2, ["WTG"] = 2,
        };

        // Assigns (and remembers) a random art variant for this body's planet class, so the
        // same body keeps the same look for as long as it's targeted this session — same idea
        // as GetBeltVariant. Returns 0 (meaning "use the base <code>.png") for classes with no
        // variant pool, or when picked at random alongside the numbered variants.
        public int GetPlanetVariant(string bodyName, string planetCode)
        {
            if (string.IsNullOrEmpty(planetCode) || !PlanetVariantCounts.TryGetValue(planetCode, out var count))
                return 0;
            lock (_planetVariants)
            {
                if (!_planetVariants.TryGetValue(bodyName, out var variant))
                {
                    variant = _beltVariantRandom.Next(0, count + 1); // 0 = base image, 1..count = numbered variants
                    _planetVariants[bodyName] = variant;
                }
                return variant;
            }
        }

        public EliteStatus CurrentStatus { get; private set; } = new();
        public string CurrentBody    { get; private set; } = "";

        // Ship Departure Range — remembered surface points for the ship and a parked SRV,
        // plus whether the player has strayed past the departure threshold this excursion.
        // See ScanCache.SaveShipAnchor/SaveSrvAnchor for persistence.
        public AnchorPoint? ShipAnchor              { get; private set; }
        public AnchorPoint? SrvAnchor               { get; private set; }
        public bool         ShipDepartureCrossed    { get; private set; }
        public double       ShipDepartureThresholdMetres { get; set; } = 1975;
        public const double ShipDepartureRevealMarginMetres = 300;
        private EliteStatus? _prevStatus;
        // Body whose data is currently loaded into the in-memory display lists
        // (ScannedOrganisms, KnownGenera, CompletedGenera, KnownGeoSites, BiologyCount,
        // GeologyCount, WasFootfalled). Diverges from CurrentBody during a preview
        // (when the user clicks another planet in the sidebar). Live event handlers
        // gate their in-memory mutations on this so a scan on the actual current
        // body doesn't pollute the previewed planet's display.
        public string DisplayedBody  { get; private set; } = "";
        public event EventHandler? PlanetListChanged;
        public string StarSystem        { get; private set; } = "";
        // Current ship's numeric ShipID, from the most recent Loadout/LoadGame event — tracked
        // purely so it can be injected as EDSM's "_shipId" transient field on every outgoing
        // upload (see EdsmService). EDSM's own journal API falls back to server-side session
        // state to attribute a traffic-log entry's ship when a submission doesn't carry this,
        // and that fallback proved unreliable (confirmed via a real report: ship showed as the
        // default Sidewinder on EDSM despite Loadout/LoadGame both carrying the correct ship) —
        // matches EDMC's own approach of always sending this itself rather than relying on that.
        public long?  CurrentShipId     { get; private set; } = null;
        // Real per-system population from FSDJump/Location's own "Population" field — used by
        // the Earthlike renderer to gate city lights (only inhabited systems would show any),
        // not tracked anywhere else in the app before now.
        public long   SystemPopulation  { get; private set; } = 0;
        public string CachedBodyName    { get; private set; } = "";
        public bool   WasFootfalled     { get; private set; } = false;
        // Tracks bodies confirmed (via a Scan event) to have WasFootfalled=false and not yet
        // disembarked-on. Per-body so scanning one body (e.g. a moon on approach) can never
        // clobber the pending state of another body actually being visited.
        private readonly HashSet<string> _pendingFirstFootfallBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        // Per-genus position in the Log→Sample→Sample sequence as REPLAYED from the journal
        // (Log=1, first Sample=2, second Sample=3). Backfill used to infer a Sample's number
        // from how many dots were already in memory, so replaying the journal onto dots that
        // were already there (app kept running through a game crash, or a cache reload) turned
        // the real 2nd sample into a phantom 3rd.
        private readonly Dictionary<string, int> _backfillSampleSeq = new(StringComparer.OrdinalIgnoreCase);

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
            _pendingFirstFootfallBodies.Clear();
            lock (ScannedOrganisms) ScannedOrganisms.Clear();
            lock (KnownGenera)      KnownGenera.Clear();
            lock (CompletedGenera)  CompletedGenera.Clear();
            lock (KnownGeoSites)    KnownGeoSites.Clear();
            BiologyCount = 0;
            GeologyCount = 0;
            ShipAnchor = null;
            SrvAnchor  = null;
            ShipDepartureCrossed = false;
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
                SystemMiningPlanets.Clear();
            }
            lock (_bodyBioSignals)
                _bodyBioSignals.Clear();

            // Reset journal tail so JournalLoop re-detects the current file and
            // runs BackfillJournal again — exactly as it does on first startup
            _currentJournalFile = "";
            _journalPosition    = 0;

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
        // Non-empty while the nav-panel target is a surface signal (e.g. a Planetary Mining
        // Location Signal), not a real body — e.g. "Mining Location Signal (13)". See the
        // Destination-parsing block below for how this is resolved. Cleared once the target
        // goes back to a real body or is cleared entirely.
        public string TargetedSignalLabel { get; private set; } = "";

        // Nav-panel signal targets never carry a usable body NAME — Destination.Name is
        // something like "$SAA_Unknown_Signal:#type=$PlanetaryMiningLocation_Name;:#index=13;",
        // which matches no real body. Destination.Body is the numeric BodyID of the signal's
        // PARENT planet, which IS resolvable. Real report: selecting a mining signal made the
        // radar jump to the primary star, because the old code fed that raw signal string
        // through as if it were a body name and nothing matched it.
        private static readonly System.Text.RegularExpressions.Regex SignalDestinationRegex =
            new(@"\$SAA_Unknown_Signal:#type=\$(\w+)_Name;(?:#index=(\d+);)?", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Known internal type names -> the label shown on the Planet tab. Anything not in this
        // list still gets a readable fallback (split on internal capitalization) rather than
        // showing the raw "$PlanetaryMiningLocation_Name;"-style string.
        private static readonly Dictionary<string, string> SignalTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PlanetaryMiningLocation"] = "Mining Location Signal",
        };
        private static string FormatSignalLabel(string internalType, string index)
        {
            if (!SignalTypeNames.TryGetValue(internalType, out var label))
                label = System.Text.RegularExpressions.Regex.Replace(internalType, "(?<!^)([A-Z])", " $1") + " Signal";
            return string.IsNullOrEmpty(index) ? label : $"{label} ({index})";
        }

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

        // Same shape as PlanetGeoInfo, minus DiscoveredCount — there's no confirmed per-instance
        // discovery event for a mining location yet (nobody's actually visited one to capture
        // journal data from it), so unlike geo sites this list can never show "all found" greyed
        // out, only the raw orbital-scan count.
        public class PlanetMiningInfo
        {
            public string FullBodyName { get; set; } = "";
            public string ShortName    { get; set; } = "";
            public int    MiningCount  { get; set; }
        }

        public readonly List<PlanetBioInfo> SystemBioPlanets = new();
        public readonly List<PlanetGeoInfo> SystemGeoPlanets = new();
        public readonly List<PlanetMiningInfo> SystemMiningPlanets = new();

        // Ring hotspot signals, keyed by the ring's own BodyName (e.g. "...2 A Ring") — a
        // distinct signal shape from Bio/Geo/Mining (Type is the raw material name itself, not
        // a localised signal-type key). Confirmed against real 2024 journal data, so this has
        // existed far longer than the new planetary mining signal; the app just hasn't parsed it
        // until now. Live-session-only, same as _bodyScanDetails.
        public readonly Dictionary<string, List<RingHotspotSignal>> RingHotspots =
            new Dictionary<string, List<RingHotspotSignal>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _bodyBioSignals =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Physical scan detail (star or planet) for every body scanned this session, keyed
        // by full body name — backs CurrentStarDetail/TargetedPlanetDetail. Live-session-only,
        // not persisted (see plan: this data is intentionally not written to ScanCache).
        private readonly Dictionary<string, BodyScanDetail> _bodyScanDetails =
            new Dictionary<string, BodyScanDetail>(StringComparer.OrdinalIgnoreCase);
        // FSS Scanner tab: total bodies in the current system (from FSSDiscoveryScan, the
        // "honk") and every body resolved so far, in resolution order. Both reset on a real
        // system change (FSDJump), same lifetime as _bodyScanDetails.
        public int SystemBodyCount { get; private set; }
        private readonly List<string> _resolvedBodiesOrder = new();
        public IReadOnlyList<string> ResolvedBodiesOrder => _resolvedBodiesOrder;
        // Scan detail for every body (star OR planet) seen this session, keyed by full body
        // name — unlike _bodyScanDetails this is NEVER cleared on a system change. Frontier only
        // fires the automatic arrival "Scan" event (and re-fires per-planet Scan/FSS events) the
        // FIRST time a body is discovered; revisiting an already-discovered system produces none
        // of that, so without this fallback CurrentStarDetail/TargetedPlanetDetail (which key off
        // _bodyScanDetails, cleared on every FSDJump) would never get repopulated on a revisit —
        // the STAR tab would sit on "AWAITING STAR SCAN" and targeting a known planet would show
        // nothing and never switch to PLANET mode.
        private readonly Dictionary<string, BodyScanDetail> _sessionBodyDetails =
            new Dictionary<string, BodyScanDetail>(StringComparer.OrdinalIgnoreCase);

        // Looks up a body's scan detail, preferring the current system's live _bodyScanDetails
        // and falling back to the never-cleared _sessionBodyDetails for a revisited body that
        // didn't get a fresh Scan event this time.
        private BodyScanDetail? GetBodyDetail(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return null;
            if (_bodyScanDetails.TryGetValue(bodyName, out var d)) return d;
            return _sessionBodyDetails.TryGetValue(bodyName, out var sd) ? sd : null;
        }
        // Public passthrough — the DEORBIT screen needs the gravity/atmosphere of whatever
        // body Status.json's BodyName names mid-glide (not necessarily CurrentBody, since
        // you haven't landed yet) or the FSS Scanner needs a specific resolved body's detail.
        public BodyScanDetail? GetKnownBodyDetail(string bodyName) => GetBodyDetail(bodyName);

        // For the System Scan window — every locally-known detail for the CURRENT system only
        // (_bodyScanDetails already clears on a real system change, so no name-prefix filtering
        // needed). A defensive copy: the window builds its own snapshot rather than holding a
        // live reference into state this service keeps mutating.
        public List<BodyScanDetail> GetCurrentSystemBodyDetails() => _bodyScanDetails.Values.ToList();

        // A "Belt Cluster" body's own Scan event carries almost nothing (BodyName/BodyID/
        // Parents/SemiMajorAxis at most — no RingClass, no composition) — that real data lives
        // on the parent RING's own entry instead, inside whichever star/planet it orbits' own
        // Rings list, under the ring's real Name (e.g. "Sol A Belt"). A cluster's own name is
        // always "<that ring name> Cluster <N>", so stripping the " Cluster N" suffix recovers
        // the exact ring name to search for.
        private static readonly System.Text.RegularExpressions.Regex BeltClusterSuffix =
            new(@" Cluster \d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string GetBeltRingName(string beltClusterBodyName) => BeltClusterSuffix.Replace(beltClusterBodyName, "");

        // Real ring class (Icy/Rocky/Metallic/MetalRich, exact in-game spelling) for a belt
        // cluster's parent ring, found by searching every known body's own Rings list for a
        // name match — checks the current system first, then the never-cleared session cache
        // so a revisited system's belts still resolve.
        public string? GetBeltRingClass(string beltClusterBodyName)
        {
            var ringName = GetBeltRingName(beltClusterBodyName);
            string? Search(Dictionary<string, BodyScanDetail> details) => details.Values
                .SelectMany(d => d.Rings)
                .FirstOrDefault(r => string.Equals(r.Name, ringName, StringComparison.OrdinalIgnoreCase))?.RingClass;
            return Search(_bodyScanDetails) ?? Search(_sessionBodyDetails);
        }

        // Real distance from the star (metres) for the RING a belt cluster belongs to — for the
        // System Scan window's ordering. A belt cluster's own SemiMajorAxis (from its own Scan
        // event) turned out NOT to be a reliable real distance-from-star for this purpose (a
        // real screenshot showed belts sorting to the very end despite genuinely being closest
        // to the star) — same root-cause pattern already documented elsewhere in this file for
        // co-orbital clusters: a cluster's own reported position is relative to its LOCAL
        // grouping, not the star. The parent ring's own InnerRad, from the star/planet's own
        // Rings list, is real orbital data actually measured from the star and doesn't have
        // that problem.
        public double? GetBeltRingInnerRadius(string beltClusterBodyName)
        {
            var ringName = GetBeltRingName(beltClusterBodyName);
            double? Search(Dictionary<string, BodyScanDetail> details) => details.Values
                .SelectMany(d => d.Rings)
                .FirstOrDefault(r => string.Equals(r.Name, ringName, StringComparison.OrdinalIgnoreCase))?.InnerRad;
            return Search(_bodyScanDetails) ?? Search(_sessionBodyDetails);
        }

        // Random asteroid-belt art variant (1-5), assigned once per body the first time it's
        // looked up and kept for as long as that belt stays targeted — cleared alongside
        // _bodyScanDetails on a real system change so a new belt gets a fresh roll.
        private readonly Dictionary<string, int> _beltVariants =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Random _beltVariantRandom = new();
        // Random planet art variant, assigned once per body the first time its icon is looked
        // up and kept for as long as that body stays targeted — same lifecycle as _beltVariants.
        private readonly Dictionary<string, int> _planetVariants =
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
        private readonly string _navRouteFile;
        private string? _lastNavRouteJson;
        private string _currentJournalFile = "";
        private long   _journalPosition;
        private int _lastLoggedGuiFocus = 0;
        // Captured from each journal file's own "Fileheader" line (game version/build) — EDSM's
        // journal API rejects every submission with msgnum 207 ("Game/Build version not found")
        // without these attached, so they need to be known before the first live upload of the
        // session. Captured during backfill too (Fileheader appears once per file, replayed or
        // not) so they're already set by the time the first real live event fires.
        private string _gameVersion = "";
        private string _gameBuild   = "";
        private readonly CancellationTokenSource _cts = new();

        // The route's full history/total, persisted via RouteCache so it survives an app or
        // game restart mid-route — see EnsureRouteState. Loaded lazily on first use.
        private RouteCacheData? _routeCache;

        // Watches for new Journal.*.log files being created by the game.
        // When a new file appears, triggers ForceRefresh after a short delay
        // to give the game time to write the file header before we read it.
        private FileSystemWatcher? _journalWatcher;

        public EliteWatcherService(string journalDir)
        {
            _journalDir = journalDir;
            _statusFile = Path.Combine(journalDir, "Status.json");
            _navRouteFile = Path.Combine(journalDir, "NavRoute.json");
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
                    .OrderByJournalDateDescending().Take(10).ToArray();
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
                    // Always re-read Status.json rather than gating on a changed
                    // File.GetLastWriteTimeUtc — that static, path-based overload is a known
                    // .NET/Windows footgun: it can cache file-attribute data internally per
                    // process and keep returning the SAME stale timestamp for the rest of the
                    // process's life even while the file is actively being rewritten. That
                    // silently froze CurrentStatus at whatever it read on the very first poll —
                    // a real captured symptom: the app stayed stuck on the Deorbit screen
                    // indefinitely after the player left the body, because HasPosition never
                    // updated again for the rest of the session. The file is tiny and this
                    // parses in microseconds, so reading it unconditionally every 30ms tick
                    // costs nothing and removes the whole failure mode.
                    if (File.Exists(_statusFile))
                    {
                        ReadStatus();
                        // Signal journal loop that we have a valid status reading
                        if (CurrentStatus.HasPosition && !_statusReady.IsSet)
                        {
                            Log.Write("StatusPollLoop: position confirmed, signalling journal loop");
                            _statusReady.Set();
                        }
                    }
                    // Same footgun as Status.json above, same fix: the static path-based
                    // File.GetLastWriteTimeUtc overload this used to gate on can cache a file's
                    // attribute data internally per process and keep returning the SAME stale
                    // timestamp for the rest of the process's life even while NavRoute.json is
                    // actively being rewritten — which silently froze LoadNavRoute() from ever
                    // running again after its first read, leaving REMAINING DIST stuck at the
                    // route's original total forever. The file is tiny, so just re-read it
                    // unconditionally every tick.
                    if (File.Exists(_navRouteFile))
                        LoadNavRoute();
                }
                catch { }
                Thread.Sleep(30);
            }
        }

        // ---------------------------------------------------------------
        // Derives ShipAnchor/SrvAnchor/ShipDepartureCrossed purely from Status.json flag
        // transitions — no journal parsing needed. Runs on every poll tick.
        private void UpdateShipDepartureTracking(EliteStatus status)
        {
            var prev = _prevStatus;
            var bodyForAnchor = !string.IsNullOrEmpty(status.BodyName) ? status.BodyName : CurrentBody;

            bool abroadGroundedShip = status.Landed && !status.InSRV && !status.OnFoot && !status.InFighter;
            bool wasAbroadGroundedShip = prev != null && prev.Landed && !prev.InSRV && !prev.OnFoot && !prev.InFighter;

            if (prev == null || abroadGroundedShip != wasAbroadGroundedShip)
                Log.Write($"ShipDeparture: abroadGroundedShip={abroadGroundedShip} Landed={status.Landed} InSRV={status.InSRV} OnFoot={status.OnFoot} InFighter={status.InFighter} HasPosition={status.HasPosition} body='{bodyForAnchor}' lat={status.Latitude} lon={status.Longitude}");

            if (abroadGroundedShip && status.HasPosition && !string.IsNullOrEmpty(bodyForAnchor))
            {
                // Continuously refresh while aboard the grounded ship — handles touchdown,
                // resuming mid-session, and re-boarding all for free. Freezes the instant
                // the player leaves (this branch stops running), holding the last position.
                if (ShipAnchor == null)
                    Log.Write($"ShipDeparture: ShipAnchor first set for '{bodyForAnchor}' at {status.Latitude},{status.Longitude}");
                ShipAnchor = new AnchorPoint { Latitude = status.Latitude, Longitude = status.Longitude };
                ScanCache.SaveShipAnchor(bodyForAnchor, status.Latitude, status.Longitude);
                ShipDepartureCrossed = false;
            }

            if (prev != null && !string.IsNullOrEmpty(bodyForAnchor))
            {
                if (prev.InSRV && status.OnFoot)
                {
                    // Just climbed out of the SRV — it's parked where we're standing.
                    Log.Write($"ShipDeparture: SrvAnchor set for '{bodyForAnchor}' at {status.Latitude},{status.Longitude}");
                    SrvAnchor = new AnchorPoint { Latitude = status.Latitude, Longitude = status.Longitude };
                    ScanCache.SaveSrvAnchor(bodyForAnchor, status.Latitude, status.Longitude);
                }
                else if (prev.OnFoot && status.InSRV)
                {
                    // Climbed back into the SRV we were tracking.
                    Log.Write($"ShipDeparture: SrvAnchor cleared for '{bodyForAnchor}' (re-embarked)");
                    SrvAnchor = null;
                    ScanCache.ClearSrvAnchor(bodyForAnchor);
                }
            }

            if (!ShipDepartureCrossed && ShipAnchor != null &&
                (status.InSRV || status.OnFoot || status.InFighter) && status.HasPosition)
            {
                double dist = DistanceMeters(status.Latitude, status.Longitude,
                    ShipAnchor.Latitude, ShipAnchor.Longitude, status.PlanetRadius);
                if (dist >= ShipDepartureThresholdMetres)
                    ShipDepartureCrossed = true;
            }

            _prevStatus = status;
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

                var fuelObj = obj["Fuel"];
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
                    FuelMain      = fuelObj?.Value<double?>("FuelMain")      ?? 0,
                    FuelReservoir = fuelObj?.Value<double?>("FuelReservoir") ?? 0,
                    GuiFocus      = (int)(obj.Value<long?>("GuiFocus") ?? -1),
                };

                // GuiFocus 9 is our best-effort read of "FSS scanner has focus" — not
                // independently confirmed against a real capture the way IsGliding was.
                // Log every distinct non-zero value so the first real FSS session can
                // confirm or correct it.
                if (status.GuiFocus != 0 && status.GuiFocus != _lastLoggedGuiFocus)
                {
                    Log.Write($"ReadStatus: GuiFocus={status.GuiFocus}");
                    _lastLoggedGuiFocus = status.GuiFocus;
                }
                else if (status.GuiFocus == 0)
                {
                    _lastLoggedGuiFocus = 0;
                }

                CurrentStatus = status;
                if (CurrentDestination != null)
                {
                    CurrentDestination.FuelMain      = status.FuelMain;
                    CurrentDestination.FuelReservoir = status.FuelReservoir;
                    CurrentDestination.CurrentJumpRange = ComputeJumpRange(CurrentDestination, status.FuelMain);
                }

                // FSD hyperdrive charging read live from Status.json (Flags2 bit 19) rather than
                // relying solely on the journal's "StartJump" event — that journal write can lag
                // or occasionally not land promptly, while Status.json is polled every tick and
                // reflects the charge the moment it starts (and clears the moment it stops).
                // Real report: after a completed jump the panel often stayed on Destination
                // instead of switching to Star, and only a restart fixed it. Status.json is
                // polled independently of the journal, so right after the FSDJump line (which
                // sets IsChargingJump = false) a poll can still read the OLD charging flag for a
                // moment. That looked like a brand-new charge starting: it re-armed
                // IsChargingJump AND stamped FsdTargetedAt with "now" — a time later than the
                // arrival — so the "was the next hop targeted after arrival" check
                // (MainWindow.ComputeMode's hasDestTarget) stayed true even after the flag
                // dropped, pinning the panel on Destination. A restart rebuilds these from the
                // journal's own timestamps, which is why it always came back correct. After an
                // arrival, don't trust "charging" until the flag has been seen false once (or
                // enough time has passed that it can only be a genuine new charge).
                bool rawCharging = status.FsdHyperdriveCharging;
                if (_chargeFlagStaleSinceArrival)
                {
                    if (!rawCharging || (DateTime.UtcNow - _chargeStaleSetAtUtc).TotalSeconds > 6)
                        _chargeFlagStaleSinceArrival = false;
                    else
                    {
                        rawCharging = false;
                        if (!_loggedStaleChargeSuppress)
                        {
                            _loggedStaleChargeSuppress = true;
                            Log.Write("ReadStatus: ignoring lingering FsdHyperdriveCharging flag right after arrival (stale, not a new charge)");
                        }
                    }
                }
                bool wasChargingJump = IsChargingJump;
                IsChargingJump = rawCharging;
                if (IsChargingJump && !wasChargingJump)
                {
                    FsdTargetedAt = DateTime.UtcNow;
                    DestinationUpdated?.Invoke(this, EventArgs.Empty);
                }

                UpdateShipDepartureTracking(status);

                // Track targeted/destination body for bio count display
                var dest = obj.Value<string>("BodyName") ?? "";
                // If we have a destination from the nav panel, use it
                var destObj = obj["Destination"];
                if (destObj != null)
                {
                    var destName = destObj.Value<string>("Name") ?? "";

                    // Resolve a signal target to its real parent body BEFORE anything below
                    // treats destName as a body name — see SignalDestinationRegex/
                    // TargetedSignalLabel. Only the surface-mining case gets its own dedicated
                    // image (MainWindow's RenderSignalTargetPanel); any other signal type still
                    // benefits from this fix (no more falling back to the star), just without
                    // the special illustration.
                    string newSignalLabel = "";
                    var sigMatch = SignalDestinationRegex.Match(destName);
                    if (sigMatch.Success)
                    {
                        var bodyId = destObj.Value<int?>("Body");
                        var parentBody = bodyId.HasValue
                            ? _bodyScanDetails.Values.FirstOrDefault(b => b.BodyID == bodyId.Value)
                            : null;
                        if (parentBody != null)
                        {
                            newSignalLabel = FormatSignalLabel(sigMatch.Groups[1].Value, sigMatch.Groups[2].Value);
                            destName = parentBody.BodyName;
                        }
                        else
                        {
                            // Parent not resolved yet (rare — usually already scanned by the time
                            // its signals are visible in the nav panel). Leave destName as-is;
                            // downstream lookups will simply find nothing, same as before this
                            // fix, rather than guessing.
                            Log.Write($"Destination: signal target's parent BodyID={bodyId} not yet known — can't resolve");
                        }
                    }
                    if (newSignalLabel != TargetedSignalLabel)
                    {
                        TargetedSignalLabel = newSignalLabel;
                        PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                    }

                    if (!string.IsNullOrEmpty(destName))
                    {
                        bool nameChanged = destName != TargetedBody;
                        TargetedBody = destName;
                        if (nameChanged)
                        {
                            PlanetTargetedAt = DateTime.UtcNow;
                            PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                        }

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
                    else if (!string.IsNullOrEmpty(TargetedBody))
                    {
                        // Destination block present but empty — target was cleared
                        TargetedBody = "";
                        TargetedBodyBioCount = 0;
                        TargetedSignalLabel = "";
                        PlanetTargetedAt = DateTime.UtcNow;
                        PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (!string.IsNullOrEmpty(TargetedBody))
                {
                    // No Destination block at all — target was cleared
                    TargetedBody = "";
                    TargetedBodyBioCount = 0;
                    TargetedSignalLabel = "";
                    PlanetTargetedAt = DateTime.UtcNow;
                    PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                }

                if (!string.IsNullOrEmpty(status.BodyName) && status.BodyName != CurrentBody)
                {
                    // Approaching or landed on a new body — load its cache
                    CurrentBody      = status.BodyName;
                    _bodySetByStatus = true;
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
                    ShipAnchor = loaded.ShipAnchor;
                    SrvAnchor  = loaded.SrvAnchor;
                    ShipDepartureCrossed = false;
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

                    ReconcileFirstFootfallAsync(CurrentBody, "StatusPoll");
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
                        SetDisplayedBody("");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                }

                StatusUpdated?.Invoke(this, new StatusUpdatedEventArgs { Status = status });
            }
            catch { }
        }

        // ---------------------------------------------------------------
        // Reads NavRoute.json (the plotted jump route) — same file-read idiom as ReadStatus().
        // The journal's own "NavRoute" event carries no data; it's just a signal to re-read
        // this file, so this is also called directly from the new "FSDTarget" case below.
        private void LoadNavRoute()
        {
            try
            {
                if (!File.Exists(_navRouteFile)) return;

                string json;
                using (var fs = new FileStream(_navRouteFile, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                    json = sr.ReadToEnd();

                if (string.IsNullOrWhiteSpace(json)) return;

                // Re-read unconditionally every poll tick now (see the stale-mtime footgun
                // comment at the call site), but only actually do anything below when the raw
                // content has genuinely changed — DestinationUpdated firing 30ms/tick regardless
                // was resetting the Destination tab's manual mode override (see MainWindow's
                // DestinationUpdated handler) on every single tick, which made clicking any tab
                // (Radar/Star/Planet/Destination) instantly snap back to the auto-selected one.
                // _lastNavRouteJson is only committed once ALL of the processing below has
                // actually succeeded (see the end of the try block) — committing it here first
                // meant any exception partway through processing (EnsureRouteState, hop parsing,
                // etc.) left CurrentDestination stuck half-updated while this same unchanged
                // file content got silently skipped on every single tick afterward forever, with
                // no way to ever retry: a real captured symptom, the whole Destination tab
                // going permanently blank ("route is still set in game") after one bad tick.
                if (json == _lastNavRouteJson) return;

                var obj = JObject.Parse(json);
                var route = obj["Route"];
                if (route == null) return;

                var hops = new List<RouteHop>();
                foreach (var r in route)
                {
                    var posArr = r["StarPos"];
                    var pos = new double[3];
                    if (posArr != null)
                        for (int i = 0; i < 3 && i < posArr.Count(); i++)
                            pos[i] = posArr[i]!.Value<double>();

                    hops.Add(new RouteHop
                    {
                        StarSystem    = r.Value<string>("StarSystem") ?? "",
                        SystemAddress = r.Value<long?>("SystemAddress") ?? 0,
                        StarPos       = pos,
                        StarClass     = r.Value<string>("StarClass") ?? "",
                    });
                }

                for (int i = 1; i < hops.Count; i++)
                    hops[i].DistanceFromPrevLy = Distance3D(hops[i - 1].StarPos, hops[i].StarPos);

                CurrentDestination ??= new DestinationInfo();
                CurrentDestination.Hops = hops;
                // RemainingDistanceLy is set inside EnsureRouteState instead, anchored off the
                // same "current position" logic as RemainingJumpsInRoute — see its comment.
                EnsureRouteState(CurrentDestination, hops);
                _lastNavRouteJson = json;
                DestinationUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { Log.Write($"LoadNavRoute error: {ex.Message}"); }
        }

        private static double Distance3D(double[] a, double[] b) =>
            Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));

        // NavRoute.json only ever shows "current position onward" — it shrinks every jump and
        // never shows hops already passed. Its LAST entry (the final destination) is the one
        // thing that stays constant for a route's entire lifetime, so it's the only reliable
        // signal for "is this still the same route" across a jump, or across an app/game
        // restart. currentHops here is that remaining view; _routeCache is the persisted full
        // history, keyed by that final destination — reset the moment it changes (a new route
        // was plotted), otherwise reused as-is so TotalRouteJumps/TotalRouteLy and hop position
        // stay stable across restarts instead of re-anchoring to "hop 1" every launch.
        private void EnsureRouteState(DestinationInfo dest, List<RouteHop> currentHops)
        {
            if (currentHops.Count == 0) return;

            _routeCache ??= RouteCache.Load();

            var finalHop = currentHops[^1];
            bool sameDestination = _routeCache != null &&
                _routeCache.FinalDestinationAddress != 0 &&
                _routeCache.FinalDestinationAddress == finalHop.SystemAddress;

            // Same final destination alone isn't proof it's still the same PLOTTED PATH — a
            // mid-route re-plot to the same endpoint (a normal recalibration after a game
            // restart, a new FSD, or just re-optimizing) can produce a genuinely different
            // path, which the final-destination-only check above can't tell apart from "same
            // route, just further along" since it never inspects the hops in between. Verify
            // currentHops is an actual contiguous SUFFIX of the cached route (every system,
            // same order, from wherever it starts in the cache) before trusting it as-is.
            bool samePath = false;
            int startIdx = -1;
            if (sameDestination)
            {
                startIdx = _routeCache!.KnownHops.FindIndex(h => h.SystemAddress == currentHops[0].SystemAddress);
                samePath = startIdx >= 0 && _routeCache.KnownHops.Count - startIdx == currentHops.Count;
                for (int i = 0; samePath && i < currentHops.Count; i++)
                    if (_routeCache.KnownHops[startIdx + i].SystemAddress != currentHops[i].SystemAddress)
                        samePath = false;
            }

            // New route (or nothing cached yet), or the cache is somehow shorter than what's
            // currently visible (shouldn't normally happen — remaining only ever shrinks for
            // the same path — but if it does, this snapshot is the fullest picture we have).
            if (!samePath || currentHops.Count > _routeCache!.KnownHops.Count)
            {
                // Real report: without this, a mid-journey re-plot (13 real jumps already
                // flown, toward the SAME final destination) reset progress to "hop 1" (~1%)
                // instead of the correct ~15% (13 of ~85 jumps) — the old leg's own history
                // was simply discarded the moment the remaining path stopped matching
                // hop-for-hop. Hops/Ly already completed toward THIS SAME destination carry
                // forward into the new leg's own baseline; only an actual destination change
                // (sameDestination false) starts the count over, per direct feedback that an
                // automatic recalibration should never lose progress, only a real new trip
                // should reset it.
                int carriedHops = 0; double carriedLy = 0;
                if (sameDestination && startIdx >= 0)
                {
                    carriedHops = _routeCache!.HopsCompleted + startIdx;
                    carriedLy   = _routeCache.LyCompleted + _routeCache.KnownHops.Take(startIdx + 1).Sum(h => h.DistanceFromPrevLy);
                }
                else if (sameDestination)
                {
                    // Same destination but current position isn't found anywhere in the old
                    // cached path at all (e.g. a real detour) — can't say how much of THAT
                    // cache was completed, so just keep whatever was already known rather
                    // than guessing or discarding it.
                    carriedHops = _routeCache!.HopsCompleted;
                    carriedLy   = _routeCache.LyCompleted;
                }

                _routeCache = new RouteCacheData
                {
                    FinalDestinationAddress = finalHop.SystemAddress,
                    FinalDestinationName    = finalHop.StarSystem,
                    KnownHops               = currentHops.Select(CloneHop).ToList(),
                    TotalRouteLy            = currentHops.Sum(h => h.DistanceFromPrevLy),
                    HopsCompleted           = carriedHops,
                    LyCompleted             = carriedLy,
                };
                RouteCache.Save(_routeCache);
            }

            dest.FullRouteHops = _routeCache.KnownHops;
            dest.TotalRouteJumps = _routeCache.KnownHops.Count + _routeCache.HopsCompleted;
            dest.TotalRouteLy    = _routeCache.TotalRouteLy + _routeCache.LyCompleted;
            // Anchor off dest.NextSystem (set directly from the FSDTarget journal event, so it's
            // never stale) against the stable, never-shrinking FullRouteHops cache — this is the
            // exact same list the hop-list UI draws and highlights "next" from, so RemainingJumpsInRoute
            // (which drives both HOP x/y and the "already passed" dimming) stays in lockstep with it
            // by construction. Previously this matched StarSystem against currentHops (the raw
            // NavRoute.json "remaining" list) instead, which is NOT rewritten on every jump on a long
            // auto-plotted route — it can sit unchanged for several real jumps while StarSystem keeps
            // advancing — and falling back to the FSDTarget event's own RemainingJumpsInRoute field in
            // that case produced a counter one hop behind the list (e.g. "HOP 16/28" while the list
            // showed system 17 as current and 18 as next).
            int nextIdx = !string.IsNullOrEmpty(dest.NextSystem)
                ? dest.FullRouteHops.FindIndex(h => string.Equals(h.StarSystem, dest.NextSystem, StringComparison.OrdinalIgnoreCase))
                : -1;
            // RemainingDistanceLy used to just be the raw file's own hop list summed top to
            // bottom (in LoadNavRoute) — correct in theory ("current position onward") but that
            // premise is exactly what the comment above already found false for a long auto-
            // plotted route: the file can sit unchanged for several real jumps while StarSystem
            // keeps advancing, so that sum kept including hops already passed and read as stuck
            // at the full route's total. Anchoring off the same nextIdx used for
            // RemainingJumpsInRoute (dest.NextSystem against the stable FullRouteHops cache)
            // instead means it shrinks in lockstep with the hop counter and the hop-list UI.
            if (nextIdx > 0)
            {
                // Against FullRouteHops.Count (the CURRENT leg only), not dest.TotalRouteJumps
                // — that now also carries forward hops completed in EARLIER legs (see above),
                // which "remaining" must never count against, or it'd overshoot by however
                // many hops were carried in.
                dest.RemainingJumpsInRoute = dest.FullRouteHops.Count - nextIdx;
                dest.RemainingDistanceLy = dest.FullRouteHops.Skip(nextIdx).Sum(h => h.DistanceFromPrevLy);
            }
            else
            {
                int currentIdx = currentHops.FindIndex(h => string.Equals(h.StarSystem, StarSystem, StringComparison.OrdinalIgnoreCase));
                if (currentIdx >= 0)
                {
                    dest.RemainingJumpsInRoute = currentHops.Count - 1 - currentIdx;
                    dest.RemainingDistanceLy = currentHops.Skip(currentIdx + 1).Sum(h => h.DistanceFromPrevLy);
                }
            }
        }

        private static RouteHop CloneHop(RouteHop h) => new RouteHop
        {
            StarSystem = h.StarSystem, SystemAddress = h.SystemAddress,
            StarPos = h.StarPos, StarClass = h.StarClass, DistanceFromPrevLy = h.DistanceFromPrevLy,
        };

        // Stock (un-engineered) FSD stats by internal item name, sourced from EDCD/coriolis-data's
        // frame_shift_drive.json — covers both standard and SCO ("overcharge") drives, sizes 2-8,
        // ratings E-A. Used as the baseline for CurrentJumpRange; engineered ships override
        // FsdOptimalMass/FsdMaxFuelPerJump from the Loadout event's own Engineering.Modifiers,
        // since those are exact rather than looked up.
        private static readonly Dictionary<string, (double FuelMul, double FuelPower, double OptMass, double MaxFuel)> FsdStockStats =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "int_hyperdrive_size2_class1", (0.011, 2.00, 48,    0.6) },
            { "int_hyperdrive_size2_class2", (0.010, 2.00, 54,    0.6) },
            { "int_hyperdrive_size2_class3", (0.008, 2.00, 60,    0.6) },
            { "int_hyperdrive_size2_class4", (0.010, 2.00, 75,    0.8) },
            { "int_hyperdrive_size2_class5", (0.012, 2.00, 90,    0.9) },
            { "int_hyperdrive_size3_class1", (0.011, 2.15, 80,    1.2) },
            { "int_hyperdrive_size3_class2", (0.010, 2.15, 90,    1.2) },
            { "int_hyperdrive_size3_class3", (0.008, 2.15, 100,   1.2) },
            { "int_hyperdrive_size3_class4", (0.010, 2.15, 125,   1.5) },
            { "int_hyperdrive_size3_class5", (0.012, 2.15, 150,   1.8) },
            { "int_hyperdrive_size4_class1", (0.011, 2.30, 280,   2.0) },
            { "int_hyperdrive_size4_class2", (0.010, 2.30, 315,   2.0) },
            { "int_hyperdrive_size4_class3", (0.008, 2.30, 350,   2.0) },
            { "int_hyperdrive_size4_class4", (0.010, 2.30, 437.5, 2.5) },
            { "int_hyperdrive_size4_class5", (0.012, 2.30, 525,   3.0) },
            { "int_hyperdrive_size5_class1", (0.011, 2.45, 560,   3.3) },
            { "int_hyperdrive_size5_class2", (0.010, 2.45, 630,   3.3) },
            { "int_hyperdrive_size5_class3", (0.008, 2.45, 700,   3.3) },
            { "int_hyperdrive_size5_class4", (0.010, 2.45, 875,   4.1) },
            { "int_hyperdrive_size5_class5", (0.012, 2.45, 1050,  5.0) },
            { "int_hyperdrive_size6_class1", (0.011, 2.60, 960,   5.3) },
            { "int_hyperdrive_size6_class2", (0.010, 2.60, 1080,  5.3) },
            { "int_hyperdrive_size6_class3", (0.008, 2.60, 1200,  5.3) },
            { "int_hyperdrive_size6_class4", (0.010, 2.60, 1500,  6.6) },
            { "int_hyperdrive_size6_class5", (0.012, 2.60, 1800,  8.0) },
            { "int_hyperdrive_size7_class1", (0.011, 2.75, 1440,  8.5) },
            { "int_hyperdrive_size7_class2", (0.010, 2.75, 1620,  8.5) },
            { "int_hyperdrive_size7_class3", (0.008, 2.75, 1800,  8.5) },
            { "int_hyperdrive_size7_class4", (0.010, 2.75, 2250,  10.6) },
            { "int_hyperdrive_size7_class5", (0.012, 2.75, 2700,  12.8) },
            { "int_hyperdrive_overcharge_size2_class1", (0.008, 2.00, 60,   0.6) },
            { "int_hyperdrive_overcharge_size2_class2", (0.012, 2.00, 90,   0.9) },
            { "int_hyperdrive_overcharge_size2_class3", (0.012, 2.00, 90,   0.9) },
            { "int_hyperdrive_overcharge_size2_class4", (0.012, 2.00, 90,   0.9) },
            { "int_hyperdrive_overcharge_size2_class5", (0.013, 2.00, 100,  1.0) },
            { "int_hyperdrive_overcharge_size3_class1", (0.008, 2.15, 100,  1.2) },
            { "int_hyperdrive_overcharge_size3_class2", (0.012, 2.15, 150,  1.8) },
            { "int_hyperdrive_overcharge_size3_class3", (0.012, 2.15, 150,  1.8) },
            { "int_hyperdrive_overcharge_size3_class4", (0.012, 2.15, 150,  1.8) },
            { "int_hyperdrive_overcharge_size3_class5", (0.013, 2.15, 167,  1.9) },
            { "int_hyperdrive_overcharge_size4_class1", (0.008, 2.30, 350,  2.0) },
            { "int_hyperdrive_overcharge_size4_class2", (0.012, 2.30, 525,  3.0) },
            { "int_hyperdrive_overcharge_size4_class3", (0.012, 2.30, 525,  3.0) },
            { "int_hyperdrive_overcharge_size4_class4", (0.012, 2.30, 525,  3.0) },
            { "int_hyperdrive_overcharge_size4_class5", (0.013, 2.30, 585,  3.2) },
            { "int_hyperdrive_overcharge_size5_class1", (0.008, 2.45, 700,  3.3) },
            { "int_hyperdrive_overcharge_size5_class2", (0.012, 2.45, 1050, 5.0) },
            { "int_hyperdrive_overcharge_size5_class3", (0.012, 2.45, 1050, 5.0) },
            { "int_hyperdrive_overcharge_size5_class4", (0.012, 2.45, 1050, 5.0) },
            { "int_hyperdrive_overcharge_size5_class5", (0.013, 2.45, 1175, 5.2) },
            { "int_hyperdrive_overcharge_size6_class1", (0.008, 2.60, 1200, 5.3) },
            { "int_hyperdrive_overcharge_size6_class2", (0.012, 2.60, 1800, 8.0) },
            { "int_hyperdrive_overcharge_size6_class3", (0.012, 2.60, 1800, 8.0) },
            { "int_hyperdrive_overcharge_size6_class4", (0.012, 2.60, 1800, 8.0) },
            { "int_hyperdrive_overcharge_size6_class5", (0.013, 2.60, 2000, 8.3) },
            { "int_hyperdrive_overcharge_size7_class1", (0.008, 2.75, 1800, 8.5) },
            { "int_hyperdrive_overcharge_size7_class2", (0.012, 2.75, 2700, 12.8) },
            { "int_hyperdrive_overcharge_size7_class3", (0.012, 2.75, 2700, 12.8) },
            { "int_hyperdrive_overcharge_size7_class4", (0.012, 2.75, 2700, 12.8) },
            { "int_hyperdrive_overcharge_size7_class5", (0.013, 2.75, 3000, 13.1) },
            { "int_hyperdrive_overcharge_size8_class1", (0.008, 2.90, 2800, 13.6) },
            { "int_hyperdrive_overcharge_size8_class2", (0.012, 2.90, 4200, 20.4) },
            { "int_hyperdrive_overcharge_size8_class3", (0.012, 2.90, 4200, 20.4) },
            { "int_hyperdrive_overcharge_size8_class4", (0.012, 2.90, 4200, 20.4) },
            { "int_hyperdrive_overcharge_size8_class5", (0.013, 2.90, 4670, 20.7) },
        };

        // Guardian FSD Booster: flat ly bonus added on top of the calculated jump range, by size.
        private static readonly Dictionary<int, double> GuardianBoosterBonusBySize = new()
        {
            { 1, 4.0 }, { 2, 6.0 }, { 3, 7.75 }, { 4, 9.25 }, { 5, 10.5 },
        };

        // Reads the FrameShiftDrive module (and any Guardian FSD Booster) out of a Loadout
        // event's Modules array, so CurrentJumpRange can be recalculated from live fuel/mass
        // instead of relying on the stale MaxJumpRange snapshot from whenever Loadout last fired.
        private static void ParseFsdStats(JObject obj, DestinationInfo dest)
        {
            var modules = obj["Modules"];
            if (modules == null) return;

            foreach (var m in modules)
            {
                var slot = m.Value<string>("Slot") ?? "";
                var item = m.Value<string>("Item") ?? "";

                if (slot == "FrameShiftDrive")
                {
                    if (FsdStockStats.TryGetValue(item, out var stock))
                    {
                        dest.FsdFuelMul        = stock.FuelMul;
                        dest.FsdFuelPower      = stock.FuelPower;
                        dest.FsdOptimalMass    = stock.OptMass;
                        dest.FsdMaxFuelPerJump = stock.MaxFuel;
                    }

                    var modifiers = m["Engineering"]?["Modifiers"];
                    if (modifiers != null)
                    {
                        foreach (var mod in modifiers)
                        {
                            var label = mod.Value<string>("Label") ?? "";
                            var value = mod.Value<double?>("Value");
                            if (value == null) continue;
                            if (label == "FSDOptimalMass") dest.FsdOptimalMass = value.Value;
                            else if (label == "MaxFuelPerJump") dest.FsdMaxFuelPerJump = value.Value;
                        }
                    }
                }
                else if (item.StartsWith("int_guardianfsdbooster_size", StringComparison.OrdinalIgnoreCase))
                {
                    var sizeChar = item.Length > "int_guardianfsdbooster_size".Length
                        ? item["int_guardianfsdbooster_size".Length]
                        : '\0';
                    if (char.IsDigit(sizeChar) && GuardianBoosterBonusBySize.TryGetValue(sizeChar - '0', out var bonus))
                        dest.GuardianBoosterBonusLy = bonus;
                }
            }
        }

        // Rough current jump range: the real Coriolis-verified FSD formula (optimal mass, fuel
        // used this jump capped at the drive's per-jump max, current total mass) plus any
        // Guardian FSD Booster's flat bonus. Cargo tonnage isn't tracked by this app, so mass
        // is UnladenMass + current main tank fuel only — close enough for a "roughly" estimate,
        // but will run a bit high for a ship carrying cargo.
        private static double ComputeJumpRange(DestinationInfo dest, double currentFuelMain)
        {
            if (dest.FsdOptimalMass <= 0 || dest.FsdMaxFuelPerJump <= 0 || dest.FsdFuelMul <= 0) return 0;
            double totalMass = dest.UnladenMass + currentFuelMain;
            if (totalMass <= 0) return 0;
            double fuelUsed = Math.Min(currentFuelMain, dest.FsdMaxFuelPerJump);
            double range = dest.FsdOptimalMass * Math.Pow(fuelUsed / dest.FsdFuelMul, 1.0 / dest.FsdFuelPower) / totalMass;
            return range + dest.GuardianBoosterBonusLy;
        }

        // Shared star/planet field extraction for a "Scan" event — used both by the live
        // dispatcher (ProcessJournalLine) and by BackfillInfoPanelState's historical replay.
        internal static BodyScanDetail ParseBodyScanDetail(JObject obj, string bodyName, bool isStar)
        {
            var detail = new BodyScanDetail
            {
                BodyName           = bodyName,
                IsStar             = isStar,
                IsBelt             = bodyName.Contains("Belt Cluster", StringComparison.OrdinalIgnoreCase),
                ScanType           = obj.Value<string>("ScanType") ?? "",
                SurfaceTemperature = obj.Value<double?>("SurfaceTemperature") ?? 0,
                SemiMajorAxis      = obj.Value<double?>("SemiMajorAxis") ?? 0,
                BodyID             = obj.Value<int?>("BodyID") ?? -1,
                WasDiscovered      = obj.Value<bool?>("WasDiscovered"),
                WasMapped          = obj.Value<bool?>("WasMapped"),
                WasFootfalled      = obj.Value<bool?>("WasFootfalled"),
            };

            // Parents[0] is USUALLY the immediate parent — {"Star":N} for a planet orbiting the
            // star directly, {"Planet":N} for a moon orbiting another planet — but a co-orbital
            // moon cluster (siblings sharing a barycenter, e.g. real journal data for
            // "...1 f"/"...1 g") inserts a {"Null":N} placeholder entry ahead of it instead:
            // Parents:[{"Null":14},{"Planet":7},{"Star":0}]. Taking only Parents[0] then missed
            // the real Planet parent entirely, defaulted ParentBodyID/IsMoon to "not a moon", and
            // let that body's tiny distance-from-its-actual-parent SemiMajorAxis get compared
            // directly against real planets' distance-from-star (confirmed root cause of the FSS
            // Scanner manifest sorting bodies like "6 f"/"6 g" ahead of "1"/"2" — those tiny
            // values look like the closest thing to the star). Walk the array instead, skipping
            // any non-Planet/non-Star (e.g. Null/Ring) entries, and use the first real one found.
            var parentsArr = obj["Parents"];
            if (parentsArr != null)
            {
                foreach (var p in parentsArr)
                {
                    if (p is not JObject po) continue;
                    // A Null seen before the real parent is the barycenter this body shares with
                    // its co-orbiting siblings (first one only — later Nulls are further up the tree).
                    var baryId = po.Value<int?>("Null");
                    if (baryId.HasValue && detail.BarycenterID < 0) detail.BarycenterID = baryId.Value;
                    var planetParent = po.Value<int?>("Planet");
                    if (planetParent.HasValue)
                    {
                        detail.ParentBodyID = planetParent.Value;
                        detail.IsMoon = true;
                        break;
                    }
                    var starParent = po.Value<int?>("Star");
                    if (starParent.HasValue)
                    {
                        detail.ParentBodyID = starParent.Value;
                        break;
                    }
                    // Null/Ring/etc — not a real parent, keep scanning.
                }
            }

            if (isStar)
            {
                detail.StarType          = obj.Value<string>("StarType") ?? "";
                detail.Subclass          = obj.Value<int?>("Subclass") ?? 0;
                detail.StellarMass       = obj.Value<double?>("StellarMass") ?? 0;
                detail.Radius            = obj.Value<double?>("Radius") ?? 0;
                detail.AbsoluteMagnitude = obj.Value<double?>("AbsoluteMagnitude") ?? 0;
                detail.AgeMY             = obj.Value<double?>("Age_MY") ?? 0;
                detail.Luminosity        = obj.Value<string>("Luminosity") ?? "";
                detail.RotationPeriod    = obj.Value<double?>("RotationPeriod") ?? 0;
            }
            else
            {
                detail.PlanetClass    = obj.Value<string>("PlanetClass") ?? "";
                detail.Radius         = obj.Value<double?>("Radius") ?? 0;
                detail.AtmosphereType = obj.Value<string>("AtmosphereType") ?? "None";
                detail.Volcanism      = obj.Value<string>("Volcanism") ?? "";
                detail.SurfaceGravity  = obj.Value<double?>("SurfaceGravity") ?? 0;
                detail.SurfacePressure = obj.Value<double?>("SurfacePressure") ?? 0;
                detail.TidalLock      = obj.Value<bool?>("TidalLock") ?? false;
                detail.TerraformState = obj.Value<string>("TerraformState") ?? "";
                detail.Landable       = obj.Value<bool?>("Landable") ?? false;
                detail.MassEM         = obj.Value<double?>("MassEM") ?? 0;
                var comp = obj["Composition"];
                if (comp != null)
                {
                    detail.IceComposition   = comp.Value<double?>("Ice")   ?? 0;
                    detail.RockComposition  = comp.Value<double?>("Rock")  ?? 0;
                    detail.MetalComposition = comp.Value<double?>("Metal") ?? 0;
                }

                // Gas giants have no discrete AtmosphereType at all (confirmed against real
                // scan data), only this list — without parsing it there was simply no real
                // atmosphere data available for them, and it silently showed "None" even on a
                // real, thick hydrogen/helium atmosphere.
                var atmoComp = obj["AtmosphereComposition"];
                if (atmoComp != null)
                    foreach (var g in atmoComp)
                    {
                        var name = g.Value<string>("Name") ?? "";
                        if (string.IsNullOrEmpty(name)) continue;
                        detail.AtmosphereComposition.Add((name, g.Value<double?>("Percent") ?? 0));
                    }

                // Only present when Landable — drives the terrain renderer's real surface tint.
                var materialsArr = obj["Materials"];
                if (materialsArr != null)
                    foreach (var m in materialsArr)
                    {
                        var name = m.Value<string>("Name") ?? "";
                        if (string.IsNullOrEmpty(name)) continue;
                        detail.Materials.Add((name, m.Value<double?>("Percent") ?? 0));
                    }
            }

            var ringsArr = obj["Rings"];
            if (ringsArr != null)
                foreach (var r in ringsArr)
                    detail.Rings.Add(new RingInfo
                    {
                        Name      = r.Value<string>("Name") ?? "",
                        RingClass = r.Value<string>("RingClass") ?? "",
                        InnerRad  = r.Value<double?>("InnerRad") ?? 0,
                        OuterRad  = r.Value<double?>("OuterRad") ?? 0,
                    });

            return detail;
        }

        // Historical catch-up for the info panel (STAR/PLANET/DESTINATION), mirroring
        // BackfillSystemPlanets' pattern: JournalLoop deliberately seeks past history in the
        // current file without replaying it live (see the "never replay historical lines as
        // live events" comment in JournalLoop), so this data needs its own dedicated replay
        // — the live "Scan"/"FSDTarget"/"Loadout" cases in ProcessJournalLine only fire for
        // NEW lines written after the app is already running.
        // Scoped to the latest journal file only (current session) — resets its accumulated
        // state on each FSDJump/CarrierJump within the file so a jump partway through a long
        // session correctly drops the previous system's star/planet/route data.
        private void BackfillInfoPanelState()
        {
            try
            {
                string trackSystem = "";
                BodyScanDetail? starDetail = null;
                var planetDetails = new Dictionary<string, BodyScanDetail>(StringComparer.OrdinalIgnoreCase);
                // Every Scan (star OR planet) seen anywhere in this replay window, keyed by body
                // name — unlike starDetail/planetDetails (reset on every FSDJump) this persists
                // across the whole replay, so a revisited body (no fresh Scan event fires for an
                // already-discovered one) can still restore its detail instead of leaving it null.
                // Merged into the live _sessionBodyDetails cache at the end so GetBodyDetail's
                // fallback works immediately at startup too, not just for bodies re-scanned live.
                var allBodyDetailsSeen = new Dictionary<string, BodyScanDetail>(StringComparer.OrdinalIgnoreCase);
                DestinationInfo? dest = null;
                DateTime fsdTargetedAt = default;
                DateTime systemArrivedAt = default;
                DateTime lastLoadGameAt = default;   // see _lastLoadGameAt: the post-load route re-announcement must not count as a new target
                bool charging = false;
                // Bio/geo signal counts, keyed per body — SystemBioPlanets/SystemGeoPlanets
                // aren't populated yet at this point (BackfillSystemPlanets runs afterwards),
                // so this replay tracks signal counts itself rather than depending on them.
                var bioSignals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var geoSignals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var miningSignals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var mappedBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // FSS Scanner manifest state — reset on each FSDJump alongside planetDetails, so
                // by the end of the replay these reflect only the current system, same as
                // everything else here. Without this, restarting the app mid-system left the
                // FSS Scanner showing "0 / ?" forever: Frontier doesn't re-fire FSSDiscoveryScan
                // or per-body Scan events for a system already fully honked/scanned earlier this
                // session, so nothing would ever populate these live post-restart.
                int lastBodyCount = 0;
                var resolvedOrder = new List<string>();

                // Scan the last several files in chronological order, not just latestFile —
                // if the game rotated to a fresh journal file (new session, same system), the
                // star/planet scans from earlier in this system live in the PREVIOUS file.
                // Replaying them as one continuous timeline (resetting on each FSDJump, same
                // as live play) means the final state correctly reflects the current system
                // even though the data that produced it came from an older file.
                var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByJournalDateDescending()
                    .Take(20)
                    .OrderByJournalDate() // oldest to newest
                    .ToArray();

                foreach (var file in files)
                foreach (var line in SafeReadAllLines(file))
                {
                    var obj = TryParse(line); if (obj == null) continue;
                    var ev = obj.Value<string>("event");

                    // "Location" fires (instead of another FSDJump) whenever the game confirms
                    // current position without an actual jump having just happened — most
                    // notably right after a game crash/relaunch mid-flight. Missing it here (this
                    // loop used to only match FSDJump/CarrierJump, unlike the other backfill
                    // replays elsewhere in this file, which all already include it) left
                    // trackSystem stuck on the last real FSDJump seen, one or more systems behind
                    // the true current one — a real captured bug: after a crash, that stale
                    // trackSystem then failed the "is the restored route still relevant" check
                    // below (the truly-current system genuinely isn't near where that stale one
                    // was in the route), wrongly deleting a route cache for a route that was
                    // still very much active.
                    if (ev == "FSDJump" || ev == "CarrierJump" || ev == "Location")
                    {
                        charging = false;
                        var sys = obj.Value<string>("StarSystem") ?? "";
                        if (!string.IsNullOrEmpty(sys) && !string.Equals(sys, trackSystem, StringComparison.OrdinalIgnoreCase))
                        {
                            trackSystem = sys;
                            systemArrivedAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                            // Try to restore the arrival star's detail from anywhere earlier in
                            // this replay before falling back to null — see allBodyDetailsSeen.
                            var arrivalBody = obj.Value<string>("Body") ?? "";
                            starDetail = string.Equals(obj.Value<string>("BodyType") ?? "", "Star", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(arrivalBody) &&
                                allBodyDetailsSeen.TryGetValue(arrivalBody, out var cachedArrivalStar)
                                    ? cachedArrivalStar : null;
                            planetDetails.Clear();
                            // On an auto-plotted route the NEXT hop's FSDTarget can fire mid-
                            // flight, before this arrival event — if dest already points past
                            // the system we just reached, it's that next-hop target, not stale.
                            if (dest == null || string.IsNullOrEmpty(dest.NextSystem) ||
                                string.Equals(dest.NextSystem, sys, StringComparison.OrdinalIgnoreCase))
                            {
                                dest = null;
                            }
                            bioSignals.Clear();
                            geoSignals.Clear();
                            miningSignals.Clear();
                            lastBodyCount = 0;
                            resolvedOrder.Clear();
                        }
                        continue;
                    }

                    if (ev == "FSSDiscoveryScan")
                    {
                        lastBodyCount = obj.Value<int?>("BodyCount") ?? 0;
                        continue;
                    }

                    if (ev == "FSSBodySignals" || ev == "SAASignalsFound")
                    {
                        var sigBody = obj.Value<string>("BodyName") ?? "";
                        var signals = obj["Signals"];
                        if (!string.IsNullOrEmpty(sigBody) && signals != null)
                        {
                            // Ring hotspots — same reasoning as the live handler: written
                            // straight into RingHotspots rather than a local dict, since it
                            // isn't nested inside a per-body detail that needs retroactive
                            // patching. Restarting the app after a ring was already scanned
                            // earlier this session would otherwise lose that data entirely,
                            // same bug class as the Mining Sites sidebar fix.
                            if (sigBody.EndsWith(" Ring", StringComparison.OrdinalIgnoreCase))
                            {
                                var hotspots = new List<RingHotspotSignal>();
                                foreach (var sig in signals)
                                {
                                    var mat = sig.Value<string>("Type_Localised") ?? sig.Value<string>("Type") ?? "";
                                    var cnt = sig.Value<int>("Count");
                                    if (!string.IsNullOrEmpty(mat)) hotspots.Add(new RingHotspotSignal { Material = mat, Count = cnt });
                                }
                                lock (RingHotspots) RingHotspots[sigBody] = hotspots;
                                continue;
                            }
                            foreach (var sig in signals)
                            {
                                var t = sig.Value<string>("Type") ?? "";
                                var c = sig.Value<int>("Count");
                                if (t.Contains("Biological"))       bioSignals[sigBody] = c;
                                else if (t.Contains("Geological"))  geoSignals[sigBody] = c;
                                else if (t.Contains("Mining"))      miningSignals[sigBody] = c;
                            }
                            // Retroactively apply to a body already scanned earlier in this replay
                            if (planetDetails.TryGetValue(sigBody, out var already))
                            {
                                if (bioSignals.TryGetValue(sigBody, out var b)) already.BioSignalCount = b;
                                if (geoSignals.TryGetValue(sigBody, out var g)) already.GeoSignalCount = g;
                                if (miningSignals.TryGetValue(sigBody, out var m)) already.MiningSignalCount = m;
                            }
                        }
                        continue;
                    }

                    if (ev == "SAAScanComplete")
                    {
                        var mb = obj.Value<string>("BodyName") ?? "";
                        if (!string.IsNullOrEmpty(mb)) mappedBodies.Add(mb);
                        continue;
                    }

                    if (ev == "Scan")
                    {
                        var bodyName = obj.Value<string>("BodyName") ?? "";
                        if (string.IsNullOrEmpty(bodyName)) continue;
                        bool isStar = obj["StarType"] != null;
                        var detail = ParseBodyScanDetail(obj, bodyName, isStar);
                        if (!isStar)
                        {
                            if (bioSignals.TryGetValue(bodyName, out var b)) detail.BioSignalCount = b;
                            if (geoSignals.TryGetValue(bodyName, out var g)) detail.GeoSignalCount = g;
                            if (miningSignals.TryGetValue(bodyName, out var m)) detail.MiningSignalCount = m;
                        }
                        planetDetails[bodyName] = detail;
                        allBodyDetailsSeen[bodyName] = detail;
                        if (!resolvedOrder.Contains(bodyName, StringComparer.OrdinalIgnoreCase))
                            resolvedOrder.Add(bodyName);

                        bool isPrimaryStar = isStar &&
                            (string.Equals(bodyName, trackSystem, StringComparison.OrdinalIgnoreCase) ||
                             (obj.Value<double?>("DistanceFromArrivalLS") ?? -1) == 0);
                        if (isPrimaryStar) starDetail = detail;
                        continue;
                    }

                    if (ev == "StartJump" && string.Equals(obj.Value<string>("JumpType") ?? "", "Hyperspace", StringComparison.OrdinalIgnoreCase))
                    {
                        charging = true;
                        fsdTargetedAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                        continue;
                    }

                    if (ev == "LoadGame")
                    {
                        lastLoadGameAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                        continue;
                    }

                    if (ev == "FSDTarget")
                    {
                        dest ??= new DestinationInfo();
                        dest.NextSystem            = obj.Value<string>("Name") ?? "";
                        dest.StarClass             = obj.Value<string>("StarClass") ?? "";
                        dest.RemainingJumpsInRoute = obj.Value<int?>("RemainingJumpsInRoute") ?? 0;
                        var replayTargetTs = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                        bool replayStartupAnnouncement = lastLoadGameAt != default &&
                            (replayTargetTs - lastLoadGameAt).TotalSeconds is >= 0 and < 120;
                        if (!replayStartupAnnouncement) fsdTargetedAt = replayTargetTs;
                        continue;
                    }

                    if (ev == "Loadout")
                    {
                        dest ??= new DestinationInfo();
                        dest.MaxJumpRange = obj.Value<double?>("MaxJumpRange") ?? 0;
                        dest.UnladenMass  = obj.Value<double?>("UnladenMass") ?? 0;
                        var fuelCap = obj["FuelCapacity"];
                        if (fuelCap != null) dest.FuelCapacityMain = fuelCap.Value<double?>("Main") ?? 0;
                        ParseFsdStats(obj, dest);
                        // Real report: EDSM kept not showing the right ship on a live jump.
                        // Root cause — this whole replay is THE startup backfill that picks up
                        // the session's Loadout/LoadGame (ProcessJournalLine's own backfill path
                        // only ever routes CodexEntry/ScanOrganic/FSSBodySignals/SAASignalsFound
                        // through itself, never Loadout/LoadGame), and this block was extracting
                        // FSD stats from Loadout without ever touching ShipID — so CurrentShipId
                        // stayed null for the entire session unless a NEW live Loadout happened
                        // to fire after the app was already running (ship swap, module change),
                        // which most sessions never do. Every live event's EDSM upload injects
                        // _shipId from CurrentShipId (see EdsmService/ProcessJournalLine), so a
                        // null CurrentShipId meant no ship attribution on any of them.
                        var loadoutShipId = obj.Value<long?>("ShipID");
                        if (loadoutShipId.HasValue) CurrentShipId = loadoutShipId;
                        continue;
                    }

                    // LoadGame carries ShipID too (fires once, at actual game launch) — captured
                    // here for the same reason as Loadout just above; wasn't handled by this
                    // replay loop in any form before.
                    if (ev == "LoadGame")
                    {
                        var loadGameShipId = obj.Value<long?>("ShipID");
                        if (loadGameShipId.HasValue) CurrentShipId = loadGameShipId;
                        continue;
                    }
                }

                if (starDetail != null) CurrentStarDetail = starDetail;
                foreach (var mb in mappedBodies)
                    if (planetDetails.TryGetValue(mb, out var mappedPlanet)) mappedPlanet.IsMapped = true;
                foreach (var kv in planetDetails) _bodyScanDetails[kv.Key] = kv.Value;
                // Seed the persistent session cache from everything scanned across the whole
                // replay window (not just the current system) so a revisited, not-freshly-scanned
                // body's detail is available via GetBodyDetail immediately at startup.
                foreach (var kv in allBodyDetailsSeen) _sessionBodyDetails[kv.Key] = kv.Value;
                // Restores the FSS Scanner manifest for the current system across a restart —
                // see lastBodyCount/resolvedOrder's declaration comment above.
                SystemBodyCount = lastBodyCount;
                _resolvedBodiesOrder.Clear();
                _resolvedBodiesOrder.AddRange(resolvedOrder);
                IsChargingJump = charging;
                SystemArrivedAt = systemArrivedAt;
                if (dest != null)
                {
                    CurrentDestination = dest;
                    FsdTargetedAt = fsdTargetedAt;
                    CurrentDestination.CurrentJumpRange = ComputeJumpRange(CurrentDestination, CurrentStatus.FuelMain);
                }
                // LoadNavRoute -> EnsureRouteState derives TotalRouteJumps/TotalRouteLy/
                // FullRouteHops from the persisted route cache, restoring true hop position
                // across the restart instead of re-anchoring to "hop 1". CurrentDestination was
                // just replaced with a fresh object above, which still needs that hydration even
                // when NavRoute.json's own content hasn't changed since the last time it was
                // read — LoadNavRoute's "skip unchanged content" short-circuit exists to avoid
                // redundant work/events on repeat polls of the SAME destination object, not to
                // skip hydrating a brand new one. Forcing it through here by clearing the cached
                // content is what that short-circuit needs to not silently no-op on this call.
                _lastNavRouteJson = null;
                LoadNavRoute();

                // Same staleness check as the live FSDJump handler (see its comment there): if
                // the current system isn't anywhere in the route we just restored, it's a leftover
                // from an earlier, unrelated journey (e.g. the GAME was restarted mid-route without
                // reopening the galaxy map, so NavRoute.json never refreshed to prove otherwise) —
                // don't keep showing it as if it were current.
                if (CurrentDestination != null && CurrentDestination.FullRouteHops.Count > 0 &&
                    !string.IsNullOrEmpty(trackSystem) &&
                    !CurrentDestination.FullRouteHops.Any(h => string.Equals(h.StarSystem, trackSystem, StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Write($"BackfillInfoPanelState: current system '{trackSystem}' isn't in the restored route — clearing stale cache");
                    CurrentDestination = null;
                    _routeCache = null;
                    RouteCache.Delete();
                }

                Log.Write($"BackfillInfoPanelState: star={(starDetail != null ? starDetail.BodyName : "none")} " +
                          $"planets={planetDetails.Count} dest={(dest != null ? dest.NextSystem : "none")}");
            }
            catch (Exception ex) { Log.Write($"BackfillInfoPanelState error: {ex.Message}"); }
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
                        .OrderByJournalDateDescending()
                        .FirstOrDefault();

                    if (latest == null) { Thread.Sleep(2000); continue; }

                    if (latest != _currentJournalFile)
                    {
                        Log.Write($"JournalLoop: new file {Path.GetFileName(latest)}");
                        _currentJournalFile = latest;

                        // Real bug found via a live EDSM rejection log: BackfillJournal/
                        // BackfillInfoPanelState below use their own independent line-scanning
                        // loops, never ProcessJournalLine — so its "Fileheader" capture (for
                        // EDSM's required fromGameVersion/fromGameBuild) never actually ran for
                        // a file reached through this path, i.e. EVERY file, including a brand
                        // new one from a fresh game session, since position also jumps straight
                        // to EOF below without replaying anything through ProcessJournalLine
                        // either. Reading the file's own first few lines directly here is the
                        // one path guaranteed to run for every file this app ever sees.
                        CaptureFileheader(latest);

                        Log.Write("JournalLoop: starting backfill...");

                        // Remember the system we already know before backfill potentially
                        // corrupts _backfillSystem via the cached-body fallback path
                        string knownSystemBeforeBackfill = StarSystem;

                        BackfillJournal(latest);
                        Log.Write($"JournalLoop: backfill done, {ScannedOrganisms.Count} organisms loaded");

                        // Set the live-tail position to end-of-file HERE, right after
                        // BackfillJournal's own read of `latest` — not after the slower work
                        // below (BackfillSystemPlanets alone can scan hundreds of journal files
                        // and take several real seconds). Real report: a scan's final Analyse
                        // event, written by the still-running game in that multi-second gap
                        // (relaunching the app doesn't pause play), landed in the file AFTER
                        // BackfillJournal's own read but BEFORE this position capture used to
                        // run — so it was never in the backfill snapshot, and the live tail then
                        // started from an EOF position that was already PAST it, permanently
                        // skipping that one event (an orange 3rd-sample dot that could never
                        // grey out, though the Bio Survey panel still separately picks up its own
                        // completed-scan signal, appearing to disagree with the radar dot).
                        long tailStartPosition;
                        using (var fsTail = new FileStream(latest, FileMode.Open, FileAccess.Read,
                                   FileShare.ReadWrite | FileShare.Delete))
                            tailStartPosition = fsTail.Length;

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
                                .OrderByJournalDateDescending().Take(10))
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

                        // Position captured right after BackfillJournal's own read, above —
                        // never replay historical lines as live events, but don't let the slow
                        // work in between (BackfillSystemPlanets) push the position past events
                        // the game wrote during that gap.
                        _journalPosition = tailStartPosition;

                        Log.Write($"JournalLoop: journal position set to {_journalPosition} (end of file at backfill time)");
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
                foreach (var f in Directory.GetFiles(_journalDir, "Journal.*.log").OrderByJournalDateDescending().Take(30))
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
                lock (_planetLock) { SystemBioPlanets.Clear(); SystemGeoPlanets.Clear(); SystemMiningPlanets.Clear(); }

                var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByJournalDateDescending()
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

                            int bio = 0, geo = 0, mining = 0;
                            foreach (var sig in signals)
                            {
                                var t = sig.Value<string>("Type") ?? "";
                                if (t.Contains("Biological"))  bio = sig.Value<int>("Count");
                                if (t.Contains("Geological"))  geo = sig.Value<int>("Count");
                                if (t.Contains("Mining"))      mining = sig.Value<int>("Count");
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

                            if (mining > 0)
                            {
                                var miningShortName = GetShortBodyName(body, system);
                                lock (_planetLock)
                                {
                                    if (!SystemMiningPlanets.Any(p =>
                                        string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        SystemMiningPlanets.Add(new PlanetMiningInfo
                                        {
                                            FullBodyName = body,
                                            ShortName    = miningShortName,
                                            MiningCount  = mining,
                                        });
                                        Log.Write($"BackfillSystemPlanets: added mining '{miningShortName}' mining={mining}");
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
                                // A body with BOTH bio and geo signals used to lose its geo count here (only the geo-only branch
                                // below set it), so the Geo Survey showed just the scanned site and no "? Unknown" slots.
                                var tgp = SystemGeoPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, target, StringComparison.OrdinalIgnoreCase));
                                if (tgp != null) GeologyCount = tgp.GeoCount;
                                Log.Write($"BackfillSystemPlanets: firing BodyChanged for '{target}' bio={tp.BioCount} geo={GeologyCount}");
                                BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                    { BodyName = target, BioCount = tp.BioCount, GeoCount = GeologyCount });
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
        // Searches every journal file (newest first) for the Scan event that recorded
        // this body's WasFootfalled flag. May be many sessions old — a body only needs
        // to be DSS-scanned once, so a body visited again later may have no fresh Scan
        // event this session at all. Returns null if no Scan record exists for the body.
        private bool? FindWasFootfalledInJournalHistory(string bodyName, out string foundInFile)
        {
            foundInFile = "";
            var allJournalFiles = Directory.GetFiles(_journalDir, "Journal.*.log")
                .OrderByJournalDateDescending()
                .ToArray();

            foreach (var file in allJournalFiles)
            {
                foreach (var line in SafeReadAllLines(file))
                {
                    var o = TryParse(line); if (o == null) continue;
                    if (o.Value<string>("event") != "Scan") continue;
                    var bn = o.Value<string>("BodyName") ?? "";
                    if (!string.Equals(bn, bodyName, StringComparison.OrdinalIgnoreCase)) continue;
                    foundInFile = System.IO.Path.GetFileName(file);
                    return o.Value<bool?>("WasFootfalled") ?? true;
                }
            }
            return null;
        }

        // Async reconciliation for bodies that reach CurrentBody via a live path other than
        // BackfillJournal (which already does this search synchronously). Covers a body that
        // was DSS-scanned in a past session — no live Scan event will fire this session, so
        // without this lookup the cache's default "not footfalled" never gets corrected.
        private void ReconcileFirstFootfallAsync(string bodyName, string source)
        {
            if (string.IsNullOrEmpty(bodyName) || WasFootfalled || _pendingFirstFootfallBodies.Contains(bodyName))
                return;

            Task.Run(() =>
            {
                try
                {
                    var histWf = FindWasFootfalledInJournalHistory(bodyName, out var histFile);
                    if (histWf.HasValue && histWf.Value == false)
                    {
                        _pendingFirstFootfallBodies.Add(bodyName);
                        Log.Write($"{source}: historical Scan found for '{bodyName}' in {histFile} WasFootfalled=False → pending FF");
                        if (string.Equals(bodyName, CurrentBody, StringComparison.OrdinalIgnoreCase))
                        {
                            // histWf==false means nobody (including us) has footfalled this body
                            // yet — it's PENDING, not achieved. This was previously (incorrectly)
                            // setting WasFootfalled=true here, which is exactly backwards: it
                            // credited First Footfall the moment a never-visited body was merely
                            // approached, before ever actually landing on it.
                            WasFootfalled = false;
                            BodyChanged?.Invoke(this, new BodyChangedEventArgs
                                { BodyName = bodyName, BioCount = BiologyCount, GeoCount = GeologyCount });
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"{source} FF history lookup error: {ex.Message}"); }
            });
        }

        // ---------------------------------------------------------------
        // Backfill: reads last 5 journal files, filters ScanOrganic by current body,
        // uses the lat/lon EMBEDDED in the journal line (written by previous sessions via
        // CodexEntry which does include position), not current ship position.
        private void BackfillJournal(string latestFile)
        {
            BackfillInfoPanelState();

            try
            {
                var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                    .OrderByJournalDateDescending()
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

                // Search ALL journal files for the Scan event for this body to get WasFootfalled.
                // It may be in a much older journal file from a previous session.
                var histWf = FindWasFootfalledInJournalHistory(CurrentBody, out var histFile);
                if (histWf.HasValue)
                {
                    WasFootfalled = !histWf.Value;
                    Log.Write($"Backfill: found Scan for '{CurrentBody}' in {histFile} WasFootfalled={histWf.Value} → FirstFootfall={WasFootfalled}");
                }

                _backfillLastIncompleteGenus = "";
                _backfillSampleSeq.Clear();
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
                                if (!string.IsNullOrEmpty(activeBody) &&
                                    string.Equals(activeBody, CurrentBody, StringComparison.OrdinalIgnoreCase) &&
                                    _pendingFirstFootfallBodies.Remove(activeBody))
                                {
                                    WasFootfalled = true;
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
                        .OrderByJournalDateDescending().Take(30))
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
        // ---------------------------------------------------------------
        // EDSM catch-up backfill — real trigger: a bug (fixed separately) meant the journal
        // API's fromGameVersion/fromGameBuild fields were missing, so EDSM silently rejected
        // every single live upload with msgnum 207 for as long as that bug stood, even though
        // this app's own local journal correctly recorded everything as normal. Rather than try
        // to reverse-engineer what's missing from EDSM's own state (querying its last-known
        // location and diffing), the local journal files already ARE the ground truth for
        // everything that happened — so this just re-walks them from a watermark forward and
        // re-submits, the same way the live hook would have if it had been working. EDSM's
        // journal endpoint is a descriptive log, not a ledger, so re-submitting something
        // already known is harmless (no double-counting risk) if the watermark and live upload
        // ever overlap.
        //
        // Runs both automatically (quietly, once per app start — see MainWindow's startup path)
        // and on demand from the Settings "Sync Journals to EDSM" button; both share this one
        // method; the button just wants progress/completion feedback the automatic run doesn't.
        public async Task<(int sent, int total)> BackfillEdsmAsync(IProgress<(int done, int total)>? progress, CancellationToken ct)
        {
            var settings = AppSettings.Load();
            if (!settings.EdsmEnabled ||
                string.IsNullOrWhiteSpace(settings.EdsmCommanderName) ||
                string.IsNullOrWhiteSpace(settings.EdsmApiKey))
                return (0, 0);

            // Never synced before: catch up a bounded recent window rather than a full lifetime
            // journal replay, which could be a large one-off burst against EDSM for a returning
            // player with years of journal history. A deliberate full resync is a future "pick a
            // date range" option, not this quiet/automatic path's default behavior. Trimmed from
            // an original 7 days down to 2: a real first run against 7 days of active exploring
            // took several minutes to work through at this pace (network latency + the throttle
            // below), which is a long time to wait just to confirm today's scans went up.
            DateTime sinceUtc = settings.LastEdsmSyncUtc ?? DateTime.UtcNow.AddDays(-2);

            var files = Directory.GetFiles(_journalDir, "Journal.*.log")
                .OrderByJournalDate() // oldest to newest — keeps Fileheader's version/build tracking chronologically correct
                // File mtime is a cheap pre-filter only; a whole extra day of slack covers a
                // file that started writing before the cutoff but still has later, relevant
                // lines near its end — the real per-line timestamp check below is authoritative.
                .Where(f => File.GetLastWriteTimeUtc(f) >= sinceUtc.AddDays(-1))
                .ToArray();

            var candidates = new List<(string evt, string line, string gameVersion, string gameBuild, long? shipId)>();
            string gv = "", gb = "";
            // Tracked chronologically alongside gv/gb (both walked oldest-to-newest) so each
            // candidate is tagged with whatever ship was actually current AT THAT POINT in the
            // journal history — not just this session's present-day ship — then injected as
            // EDSM's "_shipId" transient field on send, same as the live path.
            long? sid = null;
            foreach (var file in files)
            {
                foreach (var line in SafeReadAllLines(file))
                {
                    var obj = TryParse(line); if (obj == null) continue;
                    var evt = obj.Value<string>("event"); if (string.IsNullOrEmpty(evt)) continue;
                    if (evt == "Fileheader")
                    {
                        gv = obj.Value<string>("gameversion") ?? gv;
                        gb = obj.Value<string>("build") ?? gb;
                        continue;
                    }
                    if (evt == "Loadout" || evt == "LoadGame")
                    {
                        var s = obj.Value<long?>("ShipID");
                        if (s.HasValue) sid = s;
                    }
                    if (!EdsmService.IsUploadable(evt)) continue;
                    var ts = obj.Value<DateTime?>("timestamp") ?? DateTime.MinValue;
                    if (ts < sinceUtc) continue;
                    candidates.Add((evt, line, gv, gb, sid));
                }
            }

            // Files/lines above are walked oldest-to-newest on purpose (Fileheader's own
            // version/build only makes sense read forward), but sending in THAT order made the
            // most recent, most-likely-to-be-checked events (what you just did a minute ago)
            // the very last thing to sync after however long a real backlog took to drain —
            // exactly backwards from what "did my current session make it up?" wants. Reverse
            // just the send order so newest goes first.
            candidates.Reverse();

            int sent = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var c = candidates[i];
                bool ok = await EdsmService.SubmitJournalEventAsync(
                    settings.EdsmCommanderName, settings.EdsmApiKey, c.evt, c.line, c.gameVersion, c.gameBuild, c.shipId);
                if (ok) sent++;
                progress?.Report((i + 1, candidates.Count));
                // Visible progress without waiting for the whole backlog to finish, same reason
                // as reversing the order above — a real backlog can take a couple minutes.
                if ((i + 1) % 20 == 0 || i == candidates.Count - 1)
                    Log.Write($"EdsmService backfill progress: {i + 1}/{candidates.Count} ({sent} sent ok)");
                // Throttle — this can be a few hundred events after a bug-affected session or a
                // long time away; no reason to burst them all at EDSM at once.
                try { await Task.Delay(120, ct); } catch (TaskCanceledException) { break; }
            }

            if (!ct.IsCancellationRequested)
            {
                settings.LastEdsmSyncUtc = DateTime.UtcNow;
                AppSettings.Save(settings);
            }
            return (sent, candidates.Count);
        }

        // Reads just a file's first few lines looking for "Fileheader" (always line 1 in a real
        // journal file — a few lines of slack costs nothing and guards against any oddity). See
        // the call site in JournalLoop for why this needs to be its own direct read rather than
        // relying on ProcessJournalLine's own Fileheader handling.
        private void CaptureFileheader(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                for (int i = 0; i < 5; i++)
                {
                    var line = sr.ReadLine();
                    if (line == null) break;
                    var obj = TryParse(line);
                    if (obj?.Value<string>("event") == "Fileheader")
                    {
                        _gameVersion = obj.Value<string>("gameversion") ?? _gameVersion;
                        _gameBuild   = obj.Value<string>("build") ?? _gameBuild;
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Write($"CaptureFileheader error: {ex.Message}"); }
        }

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

            // First line of every journal file — captured on backfill too (not just live) so
            // the game version/build are already known by the time the first real live event
            // needs to upload; EDSM's journal API otherwise rejects everything with msgnum 207
            // ("Game/Build version not found").
            if (evt == "Fileheader")
            {
                _gameVersion = obj.Value<string>("gameversion") ?? _gameVersion;
                _gameBuild   = obj.Value<string>("build") ?? _gameBuild;
            }

            // Captured on backfill too (not just live), same reasoning as Fileheader above — so
            // CurrentShipId is already correct by the time the first live event needs to upload
            // after an app restart, instead of starting null until this session's own first
            // Loadout/LoadGame happens to reappear.
            if (evt == "Loadout" || evt == "LoadGame")
            {
                var sid = obj.Value<long?>("ShipID");
                if (sid.HasValue) CurrentShipId = sid;
            }

            // EDSM upload — live events only. Backfill/replay lines are excluded: EDSM already
            // has that same historical data from the first time it was played live, so
            // forwarding hundreds of replayed lines on every app restart would just hammer EDSM
            // for no benefit. No-ops entirely unless the user has opted in via Settings.
            if (!backfill) EdsmService.SubmitJournalEventFireAndForget(evt, line, _gameVersion, _gameBuild, CurrentShipId);

            // Raw-material stock for System Scan's "hide full materials" filter. Live only —
            // MaterialInventory.BuildAsync replays history at startup.
            if (!backfill) MaterialInventory.Apply(evt, obj);

            switch (evt)
            {
                // The initial "honk" — hands us the total body count for the FSS Scanner's
                // manifest before any individual body is resolved. Doesn't reset on its own;
                // the FSDJump handler clears SystemBodyCount/_resolvedBodiesOrder on a real
                // system change, same as the rest of the per-system state.
                case "FSSDiscoveryScan":
                {
                    SystemBodyCount = obj.Value<int?>("BodyCount") ?? 0;
                    break;
                }

                // Live only — backfill would otherwise try to convert (and delete) every
                // screenshot from journal history on every app restart, not just new ones.
                case "Screenshot":
                {
                    if (!backfill) ScreenshotConverterService.OnScreenshotEventFireAndForget(obj);
                    break;
                }

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
                        bool pendingFF = !wasFootfalled;
                        if (pendingFF) _pendingFirstFootfallBodies.Add(bodyName);
                        else           _pendingFirstFootfallBodies.Remove(bodyName);
                        if (backfill && pendingFF)
                            WasFootfalled = true;
                        Log.Write($"Scan: {bodyName} WasFootfalled={wasFootfalled} → PendingFF={pendingFF}");
                    }

                    // Info panel (STAR/PLANET) physical detail — populated on backfill too, so a
                    // body scanned before the app was launched already has its detail available
                    // (only the live-update event is suppressed during backfill, further below).
                    // Keyed per body so a planet targeted AFTER being scanned still shows correctly.
                    if (!string.IsNullOrEmpty(bodyName))
                    {
                        bool isStar = obj["StarType"] != null;
                        var detail = ParseBodyScanDetail(obj, bodyName, isStar);
                        // Mapping re-fires an identical Scan event for the body (confirmed against
                        // real journal data) — carry IsMapped forward so it isn't lost.
                        if (_bodyScanDetails.TryGetValue(bodyName, out var priorDetail) && priorDetail.IsMapped)
                            detail.IsMapped = true;

                        if (!isStar)
                        {
                            // Bio/geo counts — reuse the same SystemBioPlanets/SystemGeoPlanets
                            // lookup pattern already used above for TargetedBody in ReadStatus().
                            lock (_planetLock)
                            {
                                var bp = SystemBioPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, bodyName, StringComparison.OrdinalIgnoreCase));
                                if (bp != null) detail.BioSignalCount = bp.BioCount;
                                var gp = SystemGeoPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, bodyName, StringComparison.OrdinalIgnoreCase));
                                if (gp != null) detail.GeoSignalCount = gp.GeoCount;
                                var mp = SystemMiningPlanets.FirstOrDefault(p =>
                                    string.Equals(p.FullBodyName, bodyName, StringComparison.OrdinalIgnoreCase));
                                if (mp != null) detail.MiningSignalCount = mp.MiningCount;
                            }
                        }

                        // WasDiscovered == false on YOUR scan means nobody had discovered it yet —
                        // you're the discoverer. Recorded here so a later revisit (which reports
                        // WasDiscovered == true, same as for another commander's find) can still
                        // tell your own discovery apart — see DiscoveryIndex.
                        if (detail.WasDiscovered == false) DiscoveryIndex.MarkDiscoveredByMe(bodyName);
                        // Footfall bookkeeping - only the FIRST scan of a body is kept (see DiscoveryIndex).
                        DiscoveryIndex.NoteScanFootfall(bodyName, detail.WasFootfalled);
                        _bodyScanDetails[bodyName] = detail;
                        _sessionBodyDetails[bodyName] = detail;
                        // Trip total — live scans only (matches the EDSM upload hook's own
                        // backfill exclusion just above): a fresh app restart replaying old
                        // journal history shouldn't silently inflate an in-progress trip's total.
                        // Estimate() itself already returns 0 for stars/belts, so this is a
                        // harmless no-op call for those rather than needing its own isStar check.
                        if (!backfill)
                        {
                            var (estValue, _) = ScanValueEstimator.Estimate(detail);
                            TripTracker.RecordScanValue(bodyName, estValue);
                        }
                        // FSS Scanner manifest — every body resolved this system, in the order
                        // it was resolved. Deduped against the mapping re-fire noted above.
                        if (!_resolvedBodiesOrder.Contains(bodyName, StringComparer.OrdinalIgnoreCase))
                            _resolvedBodiesOrder.Add(bodyName);

                        // Primary star of the system: named the same as the system, or at
                        // zero distance from arrival. Avoids a secondary star in a binary/
                        // trinary system overwriting the primary's detail.
                        bool isPrimaryStar = isStar &&
                            (string.Equals(bodyName, StarSystem, StringComparison.OrdinalIgnoreCase) ||
                             (obj.Value<double?>("DistanceFromArrivalLS") ?? -1) == 0);

                        if (isPrimaryStar)
                        {
                            CurrentStarDetail = detail;
                            if (!backfill) StarScanUpdated?.Invoke(this, EventArgs.Empty);
                        }
                        else if (!isStar && !backfill && string.Equals(bodyName, TargetedBody, StringComparison.OrdinalIgnoreCase))
                        {
                            PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    break;
                }

                // DSS mapping complete — a later, distinct milestone than a Detailed scan (see
                // MainWindow's SCAN: tag). Carries no new physical data itself (confirmed against
                // real journal data: the Scan event re-fired right after is byte-for-byte
                // identical to the pre-mapping one) — it just flips this body's status.
                case "SAAScanComplete":
                {
                    var mappedBody = obj.Value<string>("BodyName") ?? "";
                    // You just DSS-mapped it — see DiscoveryIndex for why "mapped by you" has to
                    // be tracked separately from a later Scan's WasMapped flag.
                    DiscoveryIndex.MarkMappedByMe(mappedBody);
                    if (!string.IsNullOrEmpty(mappedBody) &&
                        _bodyScanDetails.TryGetValue(mappedBody, out var mappedDetail) && !mappedDetail.IsMapped)
                    {
                        // Replace with a clone (not a mutate-in-place) — the UI debounces
                        // re-renders by reference equality against the last-rendered detail.
                        var updated = mappedDetail.Clone();
                        updated.IsMapped = true;
                        _bodyScanDetails[mappedBody] = updated;
                        // Re-estimate with IsMapped now true — RecordScanValue replaces rather
                        // than adds, so this corrects the trip total up to the mapped-bonus
                        // value instead of double-counting the unmapped estimate plus this one.
                        if (!backfill)
                        {
                            var (estValue, _) = ScanValueEstimator.Estimate(updated);
                            TripTracker.RecordScanValue(mappedBody, estValue);
                        }
                        if (!backfill && string.Equals(mappedBody, TargetedBody, StringComparison.OrdinalIgnoreCase))
                            PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                }

                case "Disembark":
                {
                    if (!backfill)
                    {
                        var disembarkBody = obj.Value<string>("Body") ?? obj.Value<string>("BodyName") ?? CurrentBody;
                        if (obj.Value<bool?>("SRV") == false && obj.Value<bool?>("Taxi") == false && obj.Value<bool?>("OnPlanet") == true)
                            DiscoveryIndex.MarkWalkedByMe(disembarkBody);

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

                        // Player stepped off ship — if pending FF for this specific body, confirm it
                        if (!string.IsNullOrEmpty(disembarkBody) && _pendingFirstFootfallBodies.Remove(disembarkBody))
                        {
                            WasFootfalled             = true;
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

                    // A ring's own signals look nothing like a planet's — Type is the raw
                    // material name directly ("Alexandrite"), not a "$SAA_SignalType_*;" key —
                    // so ring bodies are parsed as hotspots entirely separately from Bio/Geo/
                    // Mining below.
                    if (body.EndsWith(" Ring", StringComparison.OrdinalIgnoreCase))
                    {
                        var hotspots = new List<RingHotspotSignal>();
                        foreach (var sig in signals)
                        {
                            var mat = sig.Value<string>("Type_Localised") ?? sig.Value<string>("Type") ?? "";
                            var cnt = sig.Value<int>("Count");
                            if (!string.IsNullOrEmpty(mat)) hotspots.Add(new RingHotspotSignal { Material = mat, Count = cnt });
                        }
                        lock (RingHotspots) RingHotspots[body] = hotspots;
                        // Ring hotspots often resolve well after the planet's own Scan event
                        // already rendered the Planet tab — nothing else would ever prompt a
                        // redraw otherwise, since this mutates data behind an object reference
                        // that hasn't itself changed. Piggyback on PlanetTargetUpdated (the
                        // planet-tab-refresh signal) whenever the ring belongs to whichever body
                        // is currently shown.
                        if (!backfill &&
                            ((!string.IsNullOrEmpty(TargetedBody) && body.StartsWith(TargetedBody, StringComparison.OrdinalIgnoreCase)) ||
                             (!string.IsNullOrEmpty(CurrentBody) && body.StartsWith(CurrentBody, StringComparison.OrdinalIgnoreCase))))
                            PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                        break;
                    }

                    int bio = 0, geo = 0, mining = 0;
                    foreach (var sig in signals)
                    {
                        var t = sig.Value<string>("Type") ?? "";
                        var c = sig.Value<int>("Count");
                        if (t.Contains("Biological"))       bio = c;
                        else if (t.Contains("Geological"))  geo = c;
                        else if (t.Contains("Mining"))       mining = c;
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

                    // Update SystemMiningPlanets live, same pattern as geo just above — skipped
                    // during backfill for the same reason. Unlike bio/geo there's no deep
                    // BackfillSystemPlanets equivalent for mining yet, so this list only reflects
                    // what's resolved live in the current session, not the system's full journal
                    // history — acceptable for now since there's no per-instance discovery data
                    // to backfill toward anyway (see PlanetMiningInfo).
                    if (mining > 0 && !backfill)
                    {
                        string systemForMining = StarSystem;
                        if (!(!string.IsNullOrEmpty(StarSystem) &&
                              body.StartsWith(StarSystem, StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrEmpty(CurrentBody))
                        {
                            var parts = CurrentBody.Split(' ');
                            for (int i = parts.Length - 1; i >= 2; i--)
                            {
                                var candidate = string.Join(" ", parts.Take(i));
                                if (body.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                                { systemForMining = candidate; break; }
                            }
                        }
                        var miningShortName = GetShortBodyName(body, systemForMining);
                        lock (_planetLock)
                        {
                            var existing = SystemMiningPlanets.FirstOrDefault(p =>
                                string.Equals(p.FullBodyName, body, StringComparison.OrdinalIgnoreCase));
                            if (existing == null)
                                SystemMiningPlanets.Add(new PlanetMiningInfo
                                    { FullBodyName = body, ShortName = miningShortName, MiningCount = mining });
                            else
                            {
                                existing.MiningCount = mining;
                                existing.ShortName   = miningShortName;
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

                    // Retroactively apply to a body already scanned this session (mirrors the
                    // Bio/Geo lookup in the "Scan" case below, but that one only fires when a NEW
                    // Scan event arrives — this covers the FSSBodySignals/SAASignalsFound arriving
                    // after the Scan already happened, which is the common order in practice).
                    // Real bug found via the System Scan window showing zero bio/geo badges
                    // anywhere: only MiningSignalCount was ever patched back here — Bio/Geo counts
                    // were silently stuck at whatever they were (usually 0) at Scan time whenever
                    // this event arrived after it, which BackfillSystemPlanets's own population
                    // timing makes the common case, not the rare one.
                    if (_bodyScanDetails.TryGetValue(body, out var signalDetail))
                    {
                        if (bio > 0)    signalDetail.BioSignalCount    = bio;
                        if (geo > 0)    signalDetail.GeoSignalCount    = geo;
                        if (mining > 0) signalDetail.MiningSignalCount = mining;
                    }

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
                    var species = string.Equals(speciesFull, genus, StringComparison.OrdinalIgnoreCase)
                        ? ""   // single-species genus (e.g. "Bark Mounds" / "Bark Mounds"): nothing to add after the genus
                        : speciesFull.StartsWith(genus + " ", StringComparison.OrdinalIgnoreCase)
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

                    // Backfill: number Samples by their order in the journal, not by whatever
                    // dots happen to be in memory (see _backfillSampleSeq). If no Log was seen
                    // for this genus in this pass (sequence started in another file), fall
                    // back to the dot-count inference above.
                    if (backfill)
                    {
                        if (scanTypeStr == "Log") _backfillSampleSeq[genus] = 1;
                        else if (scanTypeStr == "Sample" && _backfillSampleSeq.TryGetValue(genus, out var seq))
                        {
                            _backfillSampleSeq[genus] = ++seq;
                            scanNum = seq >= 3 ? 3 : 2;
                        }
                    }

                    // Position priority: embedded in line > caller-supplied (backfill) > current
                    // status — but CurrentStatus is THIS INSTANT's real ship position, so it's
                    // only ever a legitimate source for a LIVE scan happening right now. Real
                    // report: a bio scan made right as the app closed, replayed via backfill
                    // after the game relaunched, showed up at the ship's (new, post-relaunch)
                    // location — this fallback used to apply unconditionally, ignoring the
                    // backfill flag entirely, exactly contradicting the guard a few lines below
                    // ("during backfill lat/lon must come from caller... never fall back to
                    // current ship position for historical events") which the code never
                    // actually enforced. ScanOrganic itself never embeds a real position (only
                    // CodexEntry does, and only for a genus/species's first-ever discovery), so
                    // a live scan of an already-known species has nothing embedded and must rely
                    // on this exact fallback — which is why it can't be removed outright, only
                    // restricted to when it's actually live.
                    double useLat = obj["Latitude"]  != null ? obj.Value<double>("Latitude")  :
                                    lat != 0                 ? lat                             :
                                    !backfill                ? CurrentStatus.Latitude : 0;
                    double useLon = obj["Longitude"] != null ? obj.Value<double>("Longitude") :
                                    lon != 0                 ? lon                             :
                                    !backfill                ? CurrentStatus.Longitude : 0;

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

                        if (scanNum == 3 && backfill && highestSeen >= 3)
                        {
                            Log.Write($"ScanOrganic: skipping replayed scan=3 for {genus}, already have it");
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

                    // A real Touchdown always tells us exactly where the ship now sits — whether
                    // the player flew it down (PlayerControlled:true) or it auto-flew to a recall
                    // (PlayerControlled:false). Update the anchor unconditionally, independent of
                    // the CurrentBody-change gate below, since a recall while already OnFoot/InSRV
                    // on the same body never trips that gate but does move the ship.
                    if (evt == "Touchdown")
                    {
                        var tdLat = obj.Value<double?>("Latitude");
                        var tdLon = obj.Value<double?>("Longitude");
                        var anchorBody = !string.IsNullOrEmpty(body) ? body : CurrentBody;
                        if (tdLat.HasValue && tdLon.HasValue && !string.IsNullOrEmpty(anchorBody))
                        {
                            Log.Write($"Touchdown: ShipAnchor updated for '{anchorBody}' at {tdLat},{tdLon} (backfill={backfill})");
                            ShipAnchor = new AnchorPoint { Latitude = tdLat.Value, Longitude = tdLon.Value };
                            ScanCache.SaveShipAnchor(anchorBody, tdLat.Value, tdLon.Value);
                            ShipDepartureCrossed = false;
                        }
                    }

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

                        // Returning to the exact body we last left — restore the stashed
                        // in-memory state (which still has any incomplete scan progress)
                        // instead of the cache, which never carries incomplete scans.
                        bool restoredFromStash = string.Equals(body, _stashedForBody, StringComparison.OrdinalIgnoreCase);
                        List<ScannedOrganism> organismsForDisplay;
                        if (restoredFromStash)
                        {
                            organismsForDisplay = _stashedOrganisms!;
                            lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(organismsForDisplay); }
                            lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(_stashedGenera!); }
                            Log.Write($"Touchdown/ApproachBody: restored {organismsForDisplay.Count} stashed organisms for '{body}'");
                            _stashedOrganisms = null;
                            _stashedGenera    = null;
                            _stashedForBody   = "";
                        }
                        else
                        {
                            organismsForDisplay = loaded.Organisms;
                            lock (ScannedOrganisms) { ScannedOrganisms.Clear(); ScannedOrganisms.AddRange(organismsForDisplay); }
                            lock (KnownGenera)      { KnownGenera.Clear();      KnownGenera.AddRange(loaded.KnownGenera); }
                        }
                        ShipAnchor = loaded.ShipAnchor;
                        SrvAnchor  = loaded.SrvAnchor;
                        ShipDepartureCrossed = false;
                        // Rebuild CompletedGenera from completed organisms so sidebar Total Payout
                        // shows correctly after returning to a body with previous completed scans
                        lock (CompletedGenera)
                        {
                            CompletedGenera.Clear();
                            foreach (var o in organismsForDisplay.Where(o => o.IsComplete)
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

                        ReconcileFirstFootfallAsync(CurrentBody, "Touchdown/ApproachBody");
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
                        SystemPopulation = obj.Value<long?>("Population") ?? 0;
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
                        // Left the planet — clear display but keep cache in case we return.
                        // ScanCache never persists incomplete (not-yet-Analysed) organism scans,
                        // so stash the live in-memory state first (same pattern as PreviewPlanet's
                        // stash/restore) — otherwise an in-progress scan on the genus being worked
                        // when the player launched is silently lost the moment they touch back down
                        // on the same body, since the cache-only reload below has no record of it.
                        Log.Write($"LeaveBody: clearing display for '{CurrentBody}', cache preserved, stashing in-progress scans");
                        if (!string.IsNullOrEmpty(CurrentBody))
                        {
                            lock (ScannedOrganisms) _stashedOrganisms = ScannedOrganisms.ToList();
                            lock (KnownGenera)      _stashedGenera    = KnownGenera.ToList();
                            _stashedForBody = CurrentBody;
                        }
                        lock (ScannedOrganisms) ScannedOrganisms.Clear();
                        lock (KnownGenera)      KnownGenera.Clear();
                        lock (CompletedGenera)  CompletedGenera.Clear();
                        lock (KnownGeoSites)    KnownGeoSites.Clear();
                        BiologyCount          = 0;
                        GeologyCount          = 0;
                        CurrentBody           = "";
                        WasFootfalled         = false;
                        SetDisplayedBody("");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                    break;
                }

                case "FSDJump":
                {
                    if (!backfill)
                    {
                        IsChargingJump = false;
                        _chargeStaleSetAtUtc = DateTime.UtcNow;
                        _loggedStaleChargeSuppress = false;
                        _chargeFlagStaleSinceArrival = true;
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
                            SystemPopulation = obj.Value<long?>("Population") ?? 0;
                            SystemArrivedAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                            Log.Write($"FSDJump arrival: '{newSystem}' arrivedAt={SystemArrivedAt:O} fsdTargetedAt={FsdTargetedAt:O} nextSystem='{CurrentDestination?.NextSystem}'");
                            // SystemMiningPlanets was missing from this clear — Bio/Geo sidebar
                            // sections correctly emptied on a jump, but Mining Sites kept
                            // showing the previous system's list until BackfillSystemPlanets
                            // happened to repopulate it (or never, if the new system had none).
                            lock (_planetLock) { SystemBioPlanets.Clear(); SystemGeoPlanets.Clear(); SystemMiningPlanets.Clear(); }
                            PlanetListChanged?.Invoke(this, EventArgs.Empty);

                            // New system — clear the previous system's star/planet info-panel
                            // state so it never bleeds into the new one.
                            CurrentStarDetail  = null;
                            _bodyScanDetails.Clear();
                            SystemBodyCount = 0;
                            _resolvedBodiesOrder.Clear();
                            // FSDJump itself names the arrival star ("Body"/"BodyType":"Star") —
                            // if we've already scanned it earlier this session (e.g. revisiting a
                            // system), Frontier won't re-fire the automatic arrival Scan event, so
                            // this is the only way CurrentStarDetail gets repopulated at all.
                            var arrivalBody = obj.Value<string>("Body") ?? "";
                            if (string.Equals(obj.Value<string>("BodyType") ?? "", "Star", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(arrivalBody) &&
                                _sessionBodyDetails.TryGetValue(arrivalBody, out var cachedStarDetail))
                            {
                                CurrentStarDetail = cachedStarDetail;
                                Log.Write($"FSDJump: restored cached star detail for revisited '{arrivalBody}'");
                            }
                            // Targeting an already-known planet in a revisited system also needs
                            // its detail immediately (TargetedPlanetDetail/TargetedStarDetail now
                            // fall back to _sessionBodyDetails via GetBodyDetail — see field doc).
                            // The destination route is a different story: on an auto-plotted
                            // multi-jump route, the game can (and often does) fire the NEXT
                            // hop's FSDTarget mid-flight, BEFORE this FSDJump confirms arrival
                            // at the system it was already charging for. If CurrentDestination
                            // already points somewhere other than the system we just arrived
                            // in, it's that next-hop target — keep it. Only clear it when it's
                            // actually stale (pointing at the system we just reached, or empty).
                            if (CurrentDestination == null ||
                                string.IsNullOrEmpty(CurrentDestination.NextSystem) ||
                                string.Equals(CurrentDestination.NextSystem, newSystem, StringComparison.OrdinalIgnoreCase))
                            {
                                CurrentDestination = null;
                            }
                            // Sanity check for the "kept it, next-hop target raced ahead" branch
                            // above: if the system we just actually arrived in isn't ANYWHERE in
                            // that kept route's hop list, it isn't racing ahead of this arrival at
                            // all — it's a stale, unrelated route. This happens after restarting the
                            // game mid-route without reopening the galaxy map: NavRoute.json isn't
                            // rewritten until the map is opened, so LoadNavRoute (which normally
                            // detects "a different route is now plotted" by comparing the final
                            // destination) never gets a fresh signal, and the last-cached route
                            // (possibly from an earlier, already-finished journey) just keeps
                            // showing forever. FSDTarget/FSDJump still fire correctly without the
                            // map though, so this check is always available regardless. Wipes the
                            // on-disk cache too so it doesn't resurrect on the next app restart.
                            else if (CurrentDestination.FullRouteHops.Count > 0 &&
                                !CurrentDestination.FullRouteHops.Any(h => string.Equals(h.StarSystem, newSystem, StringComparison.OrdinalIgnoreCase)))
                            {
                                Log.Write($"FSDJump: arrived in '{newSystem}', which isn't in the cached route (stale — likely NavRoute.json hasn't refreshed since a game restart) — clearing");
                                CurrentDestination = null;
                                _routeCache = null;
                                RouteCache.Delete();
                            }
                            lock (_beltVariants) _beltVariants.Clear();
                            lock (_planetVariants) _planetVariants.Clear();
                            StarScanUpdated?.Invoke(this, EventArgs.Empty);
                            PlanetTargetUpdated?.Invoke(this, EventArgs.Empty);
                            DestinationUpdated?.Invoke(this, EventArgs.Empty);
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
                        _pendingFirstFootfallBodies.Clear();
                        _stashedOrganisms     = null;
                        _stashedGenera        = null;
                        _stashedForBody       = "";
                        SetDisplayedBody("");
                        BodyChanged?.Invoke(this, new BodyChangedEventArgs { BodyName = "" });
                    }
                    break;
                }

                // Session (re)load - see _lastLoadGameAt for why the post-load route re-announcement must not count as a new target.
                case "LoadGame":
                    _lastLoadGameAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                    break;

                // Fires the moment a jump point is targeted in the system/galaxy map —
                // this is the DESTINATION-mode trigger, distinct from TargetedBody (which
                // is the in-system nav-panel planet target, read from Status.json above).
                // FSD spooling up for an actual hyperspace jump is a strong signal the player's
                // attention is moving to the next system, even if the in-system nav target
                // (e.g. the primary star) was touched more recently — bump FsdTargetedAt so the
                // STAR-vs-DESTINATION recency check in MainWindow.ComputeMode favors DESTINATION.
                // JumpType is "Supercruise" for a plain (or SCO-overcharged) supercruise entry —
                // that should NOT switch the panel, so only "Hyperspace" counts here.
                case "StartJump":
                {
                    var jumpType = obj.Value<string>("JumpType") ?? "";
                    if (string.Equals(jumpType, "Hyperspace", StringComparison.OrdinalIgnoreCase))
                    {
                        IsChargingJump = true;
                        FsdTargetedAt = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                        if (!backfill) DestinationUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                }

                case "FSDTarget":
                {
                    // Populated on backfill too (so a target set before app launch already
                    // shows), using the journal line's own timestamp rather than "now" so
                    // the PLANET-vs-DESTINATION "most recently targeted" comparison in
                    // MainWindow.ComputeMode reflects true in-game chronology, not replay time.
                    CurrentDestination ??= new DestinationInfo();
                    CurrentDestination.NextSystem            = obj.Value<string>("Name") ?? "";
                    CurrentDestination.StarClass             = obj.Value<string>("StarClass") ?? "";
                    // Provisional only — Frontier's own RemainingJumpsInRoute field here has
                    // been observed to drift on a long auto-plotted route (confirmed against a
                    // real screenshot: it read 3 hops ahead of where NextSystem actually sits in
                    // the hop list, dimming three unvisited rows as if already passed). The
                    // EnsureRouteState call below is what's actually supposed to correct this
                    // via real index-matching — but LoadNavRoute only reaches it when
                    // NavRoute.json's own content has changed, which on a long route can sit
                    // unchanged across many real jumps (see its own comment), leaving this raw
                    // value uncorrected for a while. Calling EnsureRouteState directly here too,
                    // against whatever Hops are already cached, means the correction runs on
                    // every single FSDTarget — i.e. every real hop — not just when the file
                    // happens to change.
                    CurrentDestination.RemainingJumpsInRoute = obj.Value<int?>("RemainingJumpsInRoute") ?? 0;
                    // The game re-announces the active route target a short while after loading a session;
                    // that is not the player targeting a jump, so it must not bump FsdTargetedAt (which
                    // is what flips the app onto the Destination tab). The route data above still updates.
                    var fsdTargetTs = obj.Value<DateTime?>("timestamp") ?? DateTime.UtcNow;
                    bool startupAnnouncement = _lastLoadGameAt != default &&
                        (fsdTargetTs - _lastLoadGameAt).TotalSeconds is >= 0 and < 120;
                    if (!startupAnnouncement) FsdTargetedAt = fsdTargetTs;
                    // TotalRouteJumps/TotalRouteLy are (re)derived from the persisted route
                    // cache inside LoadNavRoute -> EnsureRouteState, not tracked here.
                    LoadNavRoute();
                    if (CurrentDestination.Hops.Count > 0)
                        EnsureRouteState(CurrentDestination, CurrentDestination.Hops);
                    if (!backfill) DestinationUpdated?.Invoke(this, EventArgs.Empty);
                    break;
                }

                case "Loadout":
                {
                    // Populated on backfill too — the ship's jump range/fuel capacity from
                    // before app launch should already be available, not just after a fresh
                    // Loadout event (which only re-fires on ship/module changes).
                    CurrentDestination ??= new DestinationInfo();
                    CurrentDestination.MaxJumpRange = obj.Value<double?>("MaxJumpRange") ?? 0;
                    CurrentDestination.UnladenMass  = obj.Value<double?>("UnladenMass") ?? 0;
                    var fuelCap = obj["FuelCapacity"];
                    if (fuelCap != null)
                        CurrentDestination.FuelCapacityMain = fuelCap.Value<double?>("Main") ?? 0;
                    ParseFsdStats(obj, CurrentDestination);
                    CurrentDestination.CurrentJumpRange = ComputeJumpRange(CurrentDestination, CurrentStatus.FuelMain);
                    if (!backfill) DestinationUpdated?.Invoke(this, EventArgs.Empty);
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
