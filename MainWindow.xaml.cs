using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EliteBioRadar
{
    public partial class MainWindow : Window
    {
        private EliteWatcherService? _watcher;
        private RadarRenderer        _renderer = null!;
        private DispatcherTimer      _refreshTimer = null!;
        private DispatcherTimer?     _animTimer;

        private double _scaleMetres  = 1000;
        private double _defaultScale = 1000;
        private bool   _autoScale    = false;
        private bool   _radarAnimation = true;
        private bool   _showGeo        = false;
        private bool _settingsInitializing = true;
        private bool _appShuttingDown = false;
        private bool _showSidebar    = false;
        private bool _showBioSites   = false;
        private string? _activeGenus = null;
        private double _shipDepartureRangeMetres = 1975;
        private bool   _edsmEnabled    = false;
        private string _edsmCommander  = "";
        private string _edsmApiKey     = "";
        private bool   _gravityWarningEnabled    = false;
        private double _gravityWarningThresholdG = 2.0;
        private bool   _screenshotConversionEnabled = false;
        private string _screenshotSourceFolder      = "";
        private string _screenshotDestFolder        = "";

        // Info panel (STAR/PLANET/DESTINATION/RADAR) mode state
        private InfoPanelMode _lastMode = InfoPanelMode.Radar;
        // Set by clicking a tab; sticks until either another tab is clicked or a genuine new
        // in-game event fires (see StarScanUpdated/PlanetTargetUpdated/DestinationUpdated
        // subscriptions below), at which point automatic mode-selection takes back over.
        private InfoPanelMode? _manualMode;
        // When this run of the app started. A destination target that was already sitting in the replayed
        // journal at launch (last session ended right after targeting the next hop) must not pull the
        // app onto the Destination tab on its own - only a target set after launch counts for auto mode.
        private readonly DateTime _appLaunchedUtc = DateTime.UtcNow;
        private bool _wasHasPosition;
        private bool _wasGliding;
        private bool _wasFssActive;
        private BodyScanDetail? _lastRenderedStar;
        // Drives the neutron star jets' live redraw (StarRenderer.RenderNeutronJets on a
        // continuously advancing phase) — a DispatcherTimer, not a 2-frame opacity cross-fade
        // like the granulation/flare layers use, because cross-fading between two sufficiently
        // different static frames showed both overlapping mid-transition ("two tails" at the
        // tips, where the wave amplitude — and so the gap between frames — is largest). Tracked
        // here so UpdateStarPanel/the mode-switch handler can stop a previous one before a new
        // star (or a non-Star mode) takes over; a DispatcherTimer isn't released just because
        // the Image it was updating left the visual tree.
        private DispatcherTimer? _neutronJetTimer;
        private BodyScanDetail? _lastRenderedPlanet;
        private string _lastRenderedSignalKey = "";
        // DEORBIT screen animation state — advanced by real elapsed time each tick, not a
        // fixed frame count, so it stays correct regardless of the 100ms refresh cadence.
        private double _deorbitScroll;
        private DateTime _deorbitLastTick = DateTime.MinValue;
        private double? _deorbitLastAltitude;
        private double? _deorbitLastLat;
        private double? _deorbitLastLon;
        // Status.json (and CurrentStatus) only actually changes a few times a second — far
        // slower than the 60fps repaint — so speed/vertical-speed are computed against the
        // last time the DATA itself changed, not the last animation frame, and held steady
        // between real updates instead of being recomputed (near-zero) or spiking (a whole
        // data interval's movement divided by one animation frame's tiny dt) every frame.
        private DateTime _deorbitLastDataTick = DateTime.MinValue;
        private double _deorbitCachedVs;
        private double _deorbitCachedSpeed = 2500;
        private bool _deorbitHaveRealSpeed;
        // Descent-arc concept: the ship's progress along the fixed trajectory (0 = arc start,
        // 1 = bullseye). Went through several models: a fixed "start altitude" ratio, then pure
        // altitude/sink-rate ETA integration (raced ahead during the pre-Glide dive, since its
        // momentary sink rate doesn't hold once Glide slows things down), then a Glide-only
        // fixed-duration timer (fell short — real feedback + screenshots showed the ship still
        // needed to be well underway, around 2/3 of the way there, by the time Glide even
        // engaged, not still near the start). Current model: real altitude, log-scaled against
        // THIS approach's own observed starting altitude (captured once, not a fixed guess —
        // every approach begins from a different real altitude) down to a low floor near where
        // Glide itself finishes. Log-scale (not linear) because a single approach spans orders
        // of magnitude of altitude — linear barely moves across most of that range and then
        // rushes at the very end. Smoothed toward that real-altitude-derived target every frame
        // (not snapped straight to it) so motion stays visually continuous between Status.json's
        // own much-less-frequent updates — see the isDescending block for the actual formula.
        private double? _deorbitProgress;
        private double _deorbitStartAltitude;
        private DateTime _deorbitClimbSince = DateTime.MinValue;
        // Once this descent has successfully shown a real planet render for a body, that exact
        // detail/iconCode is "locked in" and reused for the rest of the glide even if a later
        // frame's fresh lookup comes back null or briefly reports a different (e.g. not-yet-
        // updated) PlanetClass — see the lock/unlock logic in UpdateDeorbitPanel. Real report:
        // the planet's rendered size visibly jumped larger partway through a descent, well after
        // it had already been rendering correctly — a body lookup blip (CurrentBody/journal
        // state can shuffle briefly near end of approach) most likely re-triggered the same
        // "haven't resolved real detail yet" placeholder path this was already hardened against
        // for the START of a descent, just now happening later. Locking removes the possibility
        // entirely: once real detail has been shown once this glide, nothing can un-show it.
        private string _deorbitLockedBodyName = "";
        private BodyScanDetail? _deorbitLockedDetail;
        // Cosmetic course-deviation wander (no real attitude data backs this) — a slow random
        // walk purely for visual interest, same idea as the original concept mockups.
        private double _deorbitDevValue;
        private double _deorbitDevTarget;
        private readonly Random _deorbitDevRandom = new();
        // FSS SCANNER screen — slow ambient orbital drift, and per-body flash timing for the
        // moment each body resolves (keyed by body name, cleared on system change alongside
        // the watcher's own per-system state).
        private double _fssOrbitPhase;
        private DateTime _fssLastTick = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> _fssResolvedAt = new(StringComparer.OrdinalIgnoreCase);
        private int _fssLastResolvedCount = -1;
        private DateTime _fssLastMoonScanAt = DateTime.MinValue;
        private string _fssLastSystem = "";
        // Tracks the route position (0-based index into the hop list, see hereIndex in
        // UpdateDestinationPanel) the hop list was last auto-scrolled for — NOT dest.NextSystem's
        // name. On an auto-plotted route the next hop's FSDTarget (which is what sets NextSystem)
        // can fire mid-flight, before the FSDJump confirming arrival — tracking by name meant that
        // race could silently "consume" the scroll trigger (NextSystem already changed once before
        // arrival) so the real arrival never scrolled anything. hereIndex only moves when the
        // player's actual position changes, so it stays correct regardless of that race. -2 is a
        // sentinel distinct from any real hereIndex (which starts at -1 before any hop is reached).
        private int _lastScrolledHereIndex = -2;
        private const string IconBaseUri = "pack://application:,,,/Assets/";

        // Pip colours
        private static readonly SolidColorBrush PipEmptyBorder1  = new(Color.FromRgb(0x22, 0x44, 0x55));
        private static readonly SolidColorBrush PipEmptyBorder2  = new(Color.FromRgb(0x22, 0x55, 0x44));
        private static readonly SolidColorBrush PipEmptyBorder3  = new(Color.FromRgb(0x55, 0x44, 0x22));
        private static readonly SolidColorBrush PipFill1         = new(Color.FromRgb(0x44, 0xaa, 0xff));  // blue
        private static readonly SolidColorBrush PipFill2         = new(Color.FromRgb(0x00, 0xff, 0x44));  // green
        private static readonly SolidColorBrush PipFill3         = new(Color.FromRgb(0xff, 0xaa, 0x00));  // orange
        private static readonly SolidColorBrush PipEmptyFill1    = new(Color.FromRgb(0x00, 0x11, 0x22));
        private static readonly SolidColorBrush PipEmptyFill2    = new(Color.FromRgb(0x11, 0x22, 0x11));
        private static readonly SolidColorBrush PipEmptyFill3    = new(Color.FromRgb(0x22, 0x11, 0x00));

        public MainWindow()
        {
            Log.Clear();
            Log.Write("Before InitializeComponent");
            InitializeComponent();
            Log.Write("After InitializeComponent");
            Loaded += MainWindow_Loaded;
            // Closing (not Closed) — WPF auto-closes owned windows (ScanLogWindow,
            // SystemScanWindow) as part of THIS window's own close sequence, which happens
            // between Closing and Closed firing. Real bug this fixed: those child windows'
            // own Closed handlers clear their "WasOpen" persisted flag, assuming a Closed event
            // always means the user deliberately closed that one window — true for a real
            // click on its own X button, false when it's only closing because the whole app
            // is shutting down and dragging it along. _appShuttingDown lets those handlers
            // tell the two cases apart, and has to be set before Closing returns to be in time.
            Closing += (_, __) => _appShuttingDown = true;
            Closed += MainWindow_Closed;
            Log.Write("Constructor done");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log.Write("Loaded event fired");
            _renderer = new RadarRenderer(radarCanvas);

            // Fires from a background thread (the conversion itself runs off the UI thread) —
            // marshal to the dispatcher before touching any control.
            ScreenshotConverterService.Converted += pngFileName =>
                Dispatcher.Invoke(() => ShowToast($"Screenshot converted: {pngFileName}"));

            // Load persisted settings
            var saved = AppSettings.Load();
            _defaultScale  = saved.DefaultScale;
            _scaleMetres   = saved.DefaultScale;
            _autoScale     = saved.AutoScale;
            _showSidebar   = saved.ShowSidebar;
            _showBioSites  = saved.KeepPlanetPanelOpen;
            _shipDepartureRangeMetres = saved.ShipDepartureRangeMetres;
            _edsmEnabled   = saved.EdsmEnabled;
            _edsmCommander = saved.EdsmCommanderName;
            _edsmApiKey    = saved.EdsmApiKey;
            _gravityWarningEnabled    = saved.GravityWarningEnabled;
            _gravityWarningThresholdG = saved.GravityWarningThresholdG;
            _screenshotConversionEnabled = saved.ScreenshotConversionEnabled;
            _screenshotSourceFolder      = saved.ScreenshotSourceFolder;
            _screenshotDestFolder        = saved.ScreenshotDestFolder;

            // Restore window position/size — verify it's on a connected screen first
            if (saved.WindowLeft.HasValue && saved.WindowTop.HasValue)
            {
                var left   = saved.WindowLeft.Value;
                var top    = saved.WindowTop.Value;
                var width  = saved.WindowWidth  ?? this.Width;
                var height = saved.WindowHeight ?? this.Height;

                // Check if the saved position falls within any connected screen's bounds
                bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s =>
                    left < s.WorkingArea.Right  &&
                    left + width  > s.WorkingArea.Left &&
                    top  < s.WorkingArea.Bottom &&
                    top  + height > s.WorkingArea.Top);

                if (onScreen)
                {
                    this.Left   = left;
                    this.Top    = top;
                    this.Width  = width;
                    this.Height = height;
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }

            chkBioSites.IsChecked = _showBioSites;
            planetCol.Width       = _showBioSites ? new GridLength(150) : new GridLength(0);
            planetPanel.Visibility = _showBioSites ? Visibility.Visible : Visibility.Collapsed;
            if (_showBioSites) UpdatePlanetPanel();
            _radarAnimation = saved.RadarAnimation;
            chkRadarAnimation.IsChecked = _radarAnimation;
            _showGeo = saved.ShowGeologicalSites;
            chkShowGeo.IsChecked = _showGeo;
            chkEdsm.IsChecked = _edsmEnabled;
            txtEdsmCommander.Text = _edsmCommander;
            txtEdsmApiKey.Password = _edsmApiKey;
            UpdateEdsmStatus();
            chkGravityWarning.IsChecked = _gravityWarningEnabled;
            txtGravityThreshold.Text = _gravityWarningThresholdG.ToString("0.##");
            chkScreenshotConvert.IsChecked = _screenshotConversionEnabled;
            txtScreenshotSource.Text = _screenshotSourceFolder;
            txtScreenshotDest.Text = _screenshotDestFolder;

            // Load earnings
            EarningsTracker.Load();
            UpdateEarningsDisplay();

            // Apply to controls
            chkAutoScale.IsChecked = _autoScale;
            chkSidebar.IsChecked   = _showSidebar;
            UpdateSidebarVisibility();

            // Set default scale dropdown to match saved value
            foreach (System.Windows.Controls.ComboBoxItem item in cmbDefaultScale.Items)
                if (double.TryParse(item.Tag?.ToString(), out double v) && Math.Abs(v - _defaultScale) < 1)
                    { cmbDefaultScale.SelectedItem = item; break; }

            UpdateScaleLabel();
            _settingsInitializing = false;  // Allow settings saves from here on

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += (_, __) => RefreshAll();
            _refreshTimer.Start();

            // DEORBIT/FSS SCANNER redraw at a much higher rate than the general 100ms refresh
            // tick — their tunnel/orbit motion looked visibly stepped at 10fps. Only runs while
            // one of those two is actually showing (started/stopped in ApplyInfoPanelMode); the
            // 100ms tick still handles mode *detection* for everything, this just repaints
            // whichever of the two is currently active.
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += (_, __) =>
            {
                if (_lastMode == InfoPanelMode.Deorbit) UpdateDeorbitPanel(force: true);
                else if (_lastMode == InfoPanelMode.FssScanner) UpdateFssScannerPanel(force: true);
            };

            // Save position whenever the window is moved or resized
            this.LocationChanged += (_, __) => SaveSettings();

            System.Threading.Tasks.Task.Run(StartWatcher);
            Log.Write("Loaded event done");
        }

        private void StartWatcher()
        {
            Log.Write("StartWatcher begin");
            try
            {
                var journalDir = EliteWatcherService.GetJournalDirectory();
                Log.Write($"Journal dir: {journalDir}  exists={Directory.Exists(journalDir)}");

                var svc = new EliteWatcherService(journalDir);
                svc.ShipDepartureThresholdMetres = _shipDepartureRangeMetres;
                svc.StatusUpdated     += (_, args) => Dispatcher.InvokeAsync(() => UpdateStatusBar(args.Status));
                svc.BodyChanged       += (_, args) => Dispatcher.InvokeAsync(() => UpdateBodyInfo(args));
                svc.PlanetListChanged += (_, __)   => UpdatePlanetPanel();
                svc.StarScanUpdated     += (_, __) => Dispatcher.InvokeAsync(() => { _manualMode = null; RefreshAll(); });
                // force:true bypasses UpdateInfoPlanetPanel's own debounce (it skips redrawing
                // when the TargetedPlanetDetail *object* hasn't changed) — needed because ring
                // hotspot data can arrive and mutate that same object well after it was first
                // rendered, with nothing else about the reference itself changing.
                svc.PlanetTargetUpdated += (_, __) => Dispatcher.InvokeAsync(() => { _manualMode = null; RefreshAll(); UpdateInfoPlanetPanel(force: true); });
                svc.DestinationUpdated  += (_, __) => Dispatcher.InvokeAsync(() => { _manualMode = null; RefreshAll(); });
                svc.OrganismScanned   += (_, args) => Dispatcher.InvokeAsync(() =>
                {
                    _activeGenus = args.Organism.Genus;
                    UpdateSidebar();
                    UpdateBioCounter();
                    UpdatePlanetPanel();
                    RefreshAll();
                });

                svc.Start();
                Log.Write("svc.Start() returned");

                Dispatcher.InvokeAsync(() =>
                {
                    _watcher = svc;
                    Log.Write("Watcher assigned");

                    // Reopen whichever pop-out windows were open when the app last shut down —
                    // only possible here, not earlier in Loaded, since SystemScanWindow needs a
                    // live _watcher and this is the first point it's assigned.
                    // Background pass over the journals to learn which bodies YOU discovered/mapped
                    // (see DiscoveryIndex) — until it finishes, nothing is flagged as another
                    // commander's discovery, so a slow disk can only delay the "D" badges, never
                    // wrongly show one on your own finds.
                    _ = DiscoveryIndex.BuildAsync(journalDir);
                    _ = MaterialInventory.BuildAsync(journalDir);

                    var reopen = AppSettings.Load();
                    Log.Write($"Reopen check: ScanLogWasOpen={reopen.ScanLogWasOpen} SystemScanWasOpen={reopen.SystemScanWasOpen}");
                    if (reopen.ScanLogWasOpen)    OpenScanLogWindow();
                    if (reopen.SystemScanWasOpen) OpenSystemScanWindow();

                    // Quiet EDSM catch-up — no-ops instantly if EDSM isn't enabled (checked
                    // inside BackfillEdsmAsync). Delayed slightly so it doesn't compete with the
                    // app's own startup backfill/journal work for the disk and UI thread.
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(4000);
                        try
                        {
                            Log.Write("EDSM startup catch-up: starting");
                            var (sent, total) = await svc.BackfillEdsmAsync(null, System.Threading.CancellationToken.None);
                            Log.Write($"EDSM startup catch-up: {sent}/{total} events synced");
                        }
                        catch (Exception ex) { Log.Write($"EDSM startup catch-up error: {ex}"); }
                    });
                    if (!Directory.Exists(journalDir))
                        txtBodyName.Text = "Journal not found — launch Elite first";

                    var cachedBody   = svc.CachedBodyName;
                    bool gameRunning = System.IO.File.Exists(System.IO.Path.Combine(journalDir, "Status.json"));
                    string statusBody = svc.CurrentStatus.BodyName;

                    // Show cached scans only if:
                    // - Game not running (can't know where we are, assume same spot)
                    // - OR game running AND status confirms we're at the cached body
                    // If game is running but BodyName is empty = in space, don't show stale scans
                    bool bodyMatches = !string.IsNullOrEmpty(cachedBody) &&
                                       (!gameRunning ||
                                        string.Equals(statusBody, cachedBody, StringComparison.OrdinalIgnoreCase));

                    if (bodyMatches)
                    {
                        Log.Write($"Post-assign: showing cached scans for '{cachedBody}'");
                        txtBodyName.Text = cachedBody;
                        UpdateBioCounter();
                        UpdateSidebar();
                        UpdatePlanetPanel();
                        RefreshAll();
                    }
                    else
                    {
                        // In space or body mismatch — clear everything
                        Log.Write($"Post-assign: in space (status='{statusBody}', cached='{cachedBody}') — clearing state");
                        svc.ClearCurrentBody();
                        UpdatePlanetPanel();
                        RefreshAll();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write($"StartWatcher EXCEPTION: {ex}");
                Dispatcher.InvokeAsync(() => txtBodyName.Text = $"Error: {ex.Message}");
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _refreshTimer?.Stop();
            _watcher?.Dispose();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_watcher == null) return;

            Log.Write("BtnRefresh_Click: user triggered ForceRefresh");

            // Snapshot the current log before wiping state so there's a point-in-time
            // record of exactly what the app saw when the problem occurred.
            // File lands in the app folder with a timestamp — no dialog, no extra steps.
            try
            {
                var appDir   = AppDomain.CurrentDomain.BaseDirectory;
                var src      = System.IO.Path.Combine(appDir, "EliteBioRadar.log");
                var stamp    = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var snapPath = System.IO.Path.Combine(appDir, $"EliteBioRadar_{stamp}.log");
                if (System.IO.File.Exists(src))
                {
                    System.IO.File.Copy(src, snapPath, overwrite: false);
                    Log.Write($"BtnRefresh_Click: log snapshot saved to {System.IO.Path.GetFileName(snapPath)}");
                }
            }
            catch (Exception snapEx)
            {
                Log.Write($"BtnRefresh_Click: log snapshot failed — {snapEx.Message}");
            }

            // Disable button briefly so the user gets visual feedback and can't spam it
            btnRefresh.IsEnabled = false;
            btnRefresh.Opacity   = 0.4;

            _watcher.ForceRefresh();

            // Clear UI immediately — the journal loop will repopulate within its next tick
            txtBodyName.Text = "Refreshing…";
            UpdateBioCounter();
            UpdateSidebar();
            UpdatePlanetPanel();
            RefreshAll();

            // Re-enable after a short delay (backfill typically takes < 1 second)
            var refreshBtnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            refreshBtnTimer.Tick += (_, __) =>
            {
                refreshBtnTimer.Stop();
                btnRefresh.IsEnabled = true;
                btnRefresh.Opacity   = 1.0;
                Log.Write("BtnRefresh_Click: button re-enabled");
            };
            refreshBtnTimer.Start();
        }

        // ---------------------------------------------------------------
        private void RefreshAll()
        {
            var status    = _watcher?.CurrentStatus    ?? new EliteStatus();
            var organisms = _watcher?.ScannedOrganisms ?? new List<ScannedOrganism>();

            UpdateDeorbitSequenceState(status);
            var autoMode = ComputeMode(status);

            // Landing (Lat/Long just became available) auto-switches to RADAR, same as any
            // other genuine new event clearing a manual tab selection — but only at the moment
            // of landing itself, not on every tick. Once shown, the player can click into
            // another tab (e.g. to check planet info) and it stays put — same as clicking away
            // from the STAR tab already works — until they either return to RADAR themselves or
            // land again.
            bool justLanded = status.HasPosition && !_wasHasPosition;
            _wasHasPosition = status.HasPosition;
            if (justLanded) _manualMode = null;

            // Same idea for Deorbit and the FSS scanner — either edge (just started, or just
            // ended) hands control back to automatic mode-selection, so a manual tab click
            // from before/during either one doesn't linger and hide the real current state.
            // Tracks the full ShouldShowDeorbit condition, not just raw IsGliding — see that
            // method's comment for why the two drifting apart was a real bug.
            bool showDeorbitNow = ShouldShowDeorbit(status);
            if (showDeorbitNow != _wasGliding) _manualMode = null;
            _wasGliding = showDeorbitNow;
            bool fssActive = status.GuiFocus == 9;
            if (fssActive != _wasFssActive) _manualMode = null;
            _wasFssActive = fssActive;

            InfoPanelMode mode = _manualMode ?? autoMode;
            if (mode != _lastMode)
            {
                Log.Write($"RefreshAll: mode {_lastMode} -> {mode} (autoMode={autoMode} HasPosition={status.HasPosition} watcherNull={_watcher == null})");
                // Deorbit's animation state (progress along the arc, last altitude, the "do we
                // know the direction yet" flag) used to be reset only from inside
                // UpdateDeorbitPanel — which stops running the moment the mode leaves Deorbit, so
                // the reset rarely happened and the NEXT visit started with the previous one's
                // leftovers: an ascent opened on the old descent arc instead of INITIALIZING (real
                // report). Reset on every entry to and exit from Deorbit instead.
                if (mode == InfoPanelMode.Deorbit || _lastMode == InfoPanelMode.Deorbit) ResetDeorbitState();
                // Same reasoning as the Deorbit reset above — UpdateStarPanel simply stops being
                // called once the mode leaves Star, so a running neutron jet timer would
                // otherwise keep redrawing an Image nobody's looking at indefinitely.
                if (_lastMode == InfoPanelMode.Star && mode != InfoPanelMode.Star)
                {
                    _neutronJetTimer?.Stop();
                    _neutronJetTimer = null;
                }
                _lastMode = mode;
                ApplyInfoPanelMode(mode);
            }
            if (mode != InfoPanelMode.Radar)
            {
                // Refresh whichever panel is showing (cheap no-ops when nothing changed —
                // each Update* method debounces against the last-rendered detail reference).
                switch (mode)
                {
                    case InfoPanelMode.Star:        UpdateStarPanel(); break;
                    case InfoPanelMode.Planet:      UpdateInfoPlanetPanel(); break;
                    case InfoPanelMode.Destination: UpdateDestinationPanel(); break;
                    // Deorbit/FssScanner are repainted by _animTimer at 60fps instead — see
                    // ApplyInfoPanelMode — so there's nothing to do for them here.
                }
                return;
            }

            // Auto scale
            if (_autoScale && status.HasPosition)
            {
                lock (organisms)
                {
                    // Only scale to active (incomplete) dots — ignore completed grey ones
                    var activeDots = organisms.Where(o => !o.IsComplete).ToList();
                    if (activeDots.Count > 0)
                    {
                        var furthest = activeDots
                            .OrderByDescending(o => EliteWatcherService.DistanceMeters(
                                status.Latitude, status.Longitude,
                                o.Latitude, o.Longitude, status.PlanetRadius))
                            .First();
                        double maxDist = EliteWatcherService.DistanceMeters(
                            status.Latitude, status.Longitude,
                            furthest.Latitude, furthest.Longitude, status.PlanetRadius);
                        double target = Math.Max(_defaultScale, (maxDist + furthest.ColonyRange) * 1.2);
                        if (Math.Abs(target - _scaleMetres) > 10)
                        {
                            _scaleMetres = target;
                            UpdateScaleLabel();
                        }
                    }
                    else if (_scaleMetres != _defaultScale)
                    {
                        // No active dots — return to default scale
                        _scaleMetres = _defaultScale;
                        UpdateScaleLabel();
                    }
                }
            }

            var geoSites = _watcher?.KnownGeoSites ?? new List<ScannedGeoSite>();
            List<ScannedGeoSite> geoSnapForRadar;
            lock (geoSites) geoSnapForRadar = geoSites.ToList();
            _renderer.Draw(status, organisms, _scaleMetres, _activeGenus, _radarAnimation, geoSnapForRadar,
                _watcher?.ShipAnchor, _watcher?.SrvAnchor,
                _watcher?.ShipDepartureCrossed ?? false,
                _watcher?.ShipDepartureThresholdMetres ?? 1975);

            UpdatePotentialPayout();
            UpdateEarningsDisplay();

            // Nearest organism — incomplete only, so completed genera don't crowd out active ones
            ScannedOrganism? closest = null;
            double minDist = double.MaxValue;
            if (status.HasPosition && organisms.Count > 0)
            {
                lock (organisms)
                {
                    foreach (var o in organisms)
                    {
                        if (o.IsComplete) continue;
                        var d = EliteWatcherService.DistanceMeters(
                            status.Latitude, status.Longitude,
                            o.Latitude, o.Longitude, status.PlanetRadius);
                        if (d < minDist) { minDist = d; closest = o; }
                    }
                }
            }

            if (closest != null)
            {
                string distStr = minDist < 1000 ? $"{minDist:F0}m" : $"{minDist / 1000:F2}km";
                txtScanOne.Text = $"{closest.Genus}  {distStr}";
                if (_activeGenus == null) _activeGenus = closest.Genus;
            }
            else
            {
                txtScanOne.Text = "—";
            }

            UpdatePips(status, organisms);
        }

        // ---------------------------------------------------------------
        //  Info panel: STAR / PLANET / DESTINATION / RADAR mode
        // ---------------------------------------------------------------
        private static readonly SolidColorBrush InfoLeaderBrush = new(Color.FromRgb(0x1a, 0x44, 0x44));
        private static readonly SolidColorBrush InfoValueBrush  = new(Color.FromRgb(0x00, 0xe5, 0xff));
        // Star/planet callout value text defaults to bright white; headings default to the
        // same teal as the star/planet designation label at the bottom of the panel.
        private static readonly SolidColorBrush InfoBrightValueBrush = new(Color.FromRgb(0xf2, 0xfc, 0xfc));
        private static readonly SolidColorBrush InfoOrangeBrush = new(Color.FromRgb(0xff, 0xaa, 0x00));
        private static readonly SolidColorBrush InfoDimBrush    = new(Color.FromRgb(0x44, 0x66, 0x66));
        // DEORBIT's "correct course now" tier — distinct from the amber "drifting" warning.
        private static readonly SolidColorBrush InfoDangerBrush = new(Color.FromRgb(0xff, 0x4d, 0x3d));
        // High-gravity warning — a vivid, saturated yellow rather than the desaturated gold
        // MakeTraitChip already uses for TERRAFORMABLE/TIDALLY LOCKED, so this reads as its own
        // distinct "caution" signal instead of blending in as just another trait chip.
        private static readonly SolidColorBrush InfoGravityWarnBrush = new(Color.FromRgb(0xff, 0xcc, 0x00));
        // The same soft teal-green already used for the top bar's SYSTEM/BODY/BIO/SCALE
        // labels ("BarLabel" style in XAML) — trying it for planet/star classification text.
        private static readonly SolidColorBrush InfoLabelGreenBrush = new(Color.FromRgb(0x88, 0xbb, 0xbb));
        // Mining signals — violet, deliberately as far from both BIO's cyan and GEO's amber as
        // this palette allows, so a third signal type reads as genuinely distinct at a glance.
        private static readonly SolidColorBrush InfoMiningBrush = new(Color.FromRgb(0xb9, 0x78, 0xff));
        // Ring hotspots — same violet family as surface mining (still "mining"), but a paler
        // tint so the two location types read as related-but-distinct rather than identical.
        private static readonly SolidColorBrush InfoRingMiningBrush = new(Color.FromRgb(0xd8, 0xb8, 0xff));

        // Stateful, edge-triggered half of the Deorbit trigger — see ShouldShowDeorbit below
        // for the instantaneous half. Guessing "are we approaching" from Supercruise/atmosphere
        // flags at any given instant kept missing real cases (a not-yet-known body's exact
        // flag timing isn't reliable enough), so descent instead just latches on the moment
        // position data appears at all — HasPosition only ever becomes true when closing in on
        // a landable body, so its rising edge unambiguously IS the start of an approach, no
        // Supercruise/atmosphere guesswork needed — and holds until Glide actually completes.
        // Must run exactly once per tick (called from RefreshAll, before anything reads it).
        //
        // The rising edge also requires Supercruise still true at that instant — otherwise the
        // very first tick after (re)connecting to a running game (app launched or reattached
        // while the game/Status.json was unavailable, then a status poll picks up a commander
        // already flying at low altitude near a surface, engines only, no Supercruise) looks
        // identical to "position just appeared" and wrongly started the whole sequence (real
        // report: reopening the game while already at 179m altitude falsely showed Deorbit).
        // A genuine descent always still has Supercruise true for this exact instant — glide
        // proper doesn't drop it until afterward — so this costs nothing for the real case.
        private bool _deorbitSeqActive;
        private bool _prevHasPositionForSeq;
        private bool _prevIsGlidingForSeq;
        private void UpdateDeorbitSequenceState(EliteStatus status)
        {
            if (status.HasPosition && !_prevHasPositionForSeq && status.Supercruise)
                _deorbitSeqActive = true;

            // Ends exactly when Glide completes — matches "end once glide completes" for a
            // normal descent. Ascent's tail end is covered separately, by the instantaneous
            // Supercruise+HasPosition check in ShouldShowDeorbit picking back up right after —
            // Supercruise regains true at the same instant Glide falls on the way up (per the
            // captured data), so this reset doesn't cut an ascent short.
            if (_prevIsGlidingForSeq && !status.IsGliding)
                _deorbitSeqActive = false;

            // Safety net: an aborted approach, or an airless body that never glides at all,
            // would otherwise leave this stuck on forever — the reset above only fires if
            // Glide actually engaged in the first place.
            if (!status.HasPosition && !status.IsGliding)
                _deorbitSeqActive = false;

            _prevHasPositionForSeq = status.HasPosition;
            _prevIsGlidingForSeq = status.IsGliding;
        }

        // Shared by ComputeMode, UpdateDeorbitPanel, and RefreshAll's manual-override-clearing
        // edge detector — those three used to each recompute this independently, and when the
        // trigger was broadened (see comment below) only ComputeMode and UpdateDeorbitPanel got
        // updated. The edge detector kept watching the old, narrower condition (raw IsGliding),
        // so a manual tab click from earlier could sit through the entire broadened approach
        // phase and only get cleared once real Glide finally flipped the flag it was still
        // watching — which is exactly why it only ever seemed to catch the last few seconds.
        // One method now, so all three can never drift apart like that again.
        private bool ShouldShowDeorbit(EliteStatus status)
        {
            // Deorbit covers the whole controlled descent AND ascent, not just the narrow Glide
            // window: it's live any time Supercruise is true while position data is also live
            // (still technically Supercruise, but close enough to a body to have lat/long) —
            // that covers both "dropping into a close atmospheric approach, before Glide proper
            // begins" on the way down, and "climbing back out after re-engaging Supercruise,
            // before finally leaving the body's vicinity" on the way up.
            //
            // This used to also require NOT already knowing the body was airless (via a cached
            // Scan's AtmosphereType), on the theory that a truly airless body never glides and
            // shouldn't show this screen at all. That gating was removed after a real captured
            // ascent (2026-08-30) proved it unreliable in practice: Supercruise+HasPosition was
            // confirmed true for the entire ~46s climb off a body that HAD just shown a real
            // Glide on the way down (i.e. it clearly has an atmosphere), yet the screen never
            // switched — meaning the cached body-detail lookup was producing a false "airless"
            // read at that moment. Since the real spec is just "Supercruise + close to a body",
            // dropping the atmosphere check entirely removes that whole failure mode; a body
            // that's genuinely airless will already exclude itself (Supercruise stays true from
            // approach through leaving, but you're never close enough to trigger the Glide-linked
            // parts anyway, so nothing regresses functionally for that case).
            //
            // IsGliding is checked as its own independent path so a quick "boost to low orbit
            // and glide back down" reposition can re-enter Glide directly without repeating the
            // Supercruise+position approach first.
            // Deliberately does NOT start on FsdChargingAny (the FSD charge-up before
            // Supercruise engages) — that was tried and made the screen pop up the instant the
            // player merely engaged the charge, before Supercruise itself actually kicked in.
            // Confirmed by feedback: ascent should wait for Supercruise to actually be live.
            bool nearBodyInSupercruise = status.Supercruise && status.HasPosition;
            // A hyperspace charge started while still near a body (an atmospheric jump) gets its own screen too.
            return _deorbitSeqActive || nearBodyInSupercruise || status.IsGliding || JumpSceneActive(status);
        }

        private InfoPanelMode ComputeMode(EliteStatus status)
        {
            if (PreviewStarType() != null) return InfoPanelMode.Star;
            if (ShouldShowDeorbit(status)) return InfoPanelMode.Deorbit;

            // FSD spooling up for a real hyperspace jump wins next — even from an
            // atmospheric planet's surface. Odyssey allows charging the FSD directly from
            // within a landable atmosphere without launching first, so Latitude/Longitude
            // being present (HasPosition true) can't be trusted alone to mean "show the radar"
            // while a jump is actively charging; check this before the HasPosition/Radar gate.
            if (_watcher != null && _watcher.IsChargingJump) return InfoPanelMode.Destination;

            // GuiFocus 9 = "the FSS scanner has focus" — confirmed against a real capture
            // (2026-08-30).
            if (status.GuiFocus == 9) return InfoPanelMode.FssScanner;

            if (status.HasPosition) return InfoPanelMode.Radar;
            if (_watcher == null) return InfoPanelMode.Star;

            // Any star — primary or a secondary/tertiary in a multi-star system — can also be
            // the in-system nav target (e.g. targeted from the system map). That's a target
            // just like a planet, it just belongs to STAR mode rather than PLANET mode.
            bool targetIsStar = _watcher.TargetedStarDetail != null;

            bool hasPlanetTarget   = _watcher.TargetedPlanetDetail != null && !string.IsNullOrEmpty(_watcher.TargetedBody);
            bool hasInSystemTarget = targetIsStar || hasPlanetTarget;
            // A route queued before this arrival (the common auto-route case: the next hop's
            // FSDTarget fires mid-flight, before the FSDJump that confirms arrival) shouldn't
            // keep forcing DESTINATION mode once you've landed and are looking around — only a
            // destination target that's as new as the arrival itself (freshly (re)targeted, or
            // a fresh FSD charge bumping FsdTargetedAt again) counts for automatic mode here.
            // The route data itself is untouched; this only gates the auto-switch.
            bool hasDestTarget = _watcher.CurrentDestination != null &&
                !string.IsNullOrEmpty(_watcher.CurrentDestination.NextSystem) &&
                _watcher.FsdTargetedAt >= _watcher.SystemArrivedAt &&
                _watcher.FsdTargetedAt >= _appLaunchedUtc;

            if (hasInSystemTarget && hasDestTarget)
                return _watcher.PlanetTargetedAt >= _watcher.FsdTargetedAt
                    ? (targetIsStar ? InfoPanelMode.Star : InfoPanelMode.Planet)
                    : InfoPanelMode.Destination;
            if (hasInSystemTarget) return targetIsStar ? InfoPanelMode.Star : InfoPanelMode.Planet;
            if (hasDestTarget)     return InfoPanelMode.Destination;
            return InfoPanelMode.Star;
        }

        private void ApplyInfoPanelMode(InfoPanelMode mode)
        {
            radarCanvas.Visibility         = mode == InfoPanelMode.Radar       ? Visibility.Visible : Visibility.Collapsed;
            starPanelViewbox.Visibility    = mode == InfoPanelMode.Star        ? Visibility.Visible : Visibility.Collapsed;
            planetPanelViewbox.Visibility  = mode == InfoPanelMode.Planet      ? Visibility.Visible : Visibility.Collapsed;
            destinationPanel.Visibility    = mode == InfoPanelMode.Destination ? Visibility.Visible : Visibility.Collapsed;
            deorbitPanelViewbox.Visibility = mode == InfoPanelMode.Deorbit     ? Visibility.Visible : Visibility.Collapsed;
            fssScannerPanel.Visibility     = mode == InfoPanelMode.FssScanner  ? Visibility.Visible : Visibility.Collapsed;

            // DEORBIT/FSS SCANNER don't get their own tab buttons — they temporarily take over
            // the RADAR/STAR tab's slot and label for as long as they're actually relevant,
            // reverting the instant mode moves on.
            tabRadar.Content = mode == InfoPanelMode.Deorbit ? "⛛ DEORBIT" : "◎ RADAR";
            tabStar.Content  = mode == InfoPanelMode.FssScanner ? "📡 FSS SCANNER" : "☉ STAR";

            // The 60fps repaint timer only needs to run for these two — everything else is
            // either static until its next real event or already animated by RadarRenderer.
            // _animTimer isn't constructed until MainWindow_Loaded, but ApplyInfoPanelMode can
            // run before that — Window_SizeChanged fires during the window's initial layout
            // pass, which happens before Loaded — so this needs to tolerate it still being
            // null (crashed on exactly this the first time, via that exact call path).
            bool needsFastAnim = mode == InfoPanelMode.Deorbit || mode == InfoPanelMode.FssScanner;
            if (needsFastAnim && _animTimer?.IsEnabled == false) _animTimer.Start();
            else if (!needsFastAnim && _animTimer?.IsEnabled == true) _animTimer.Stop();

            // Hide the right BIO SURVEY sidebar in every non-Radar mode without touching the
            // persisted _showSidebar setting — it restores exactly as configured the moment
            // RADAR mode returns. The left BIO SITES panel is deliberately NOT touched here —
            // it stays mounted in every mode per _showBioSites, controlled only by its own
            // settings checkbox.
            UpdateSidebarVisibility();

            switch (mode)
            {
                case InfoPanelMode.Star:        UpdateStarPanel(force: true); break;
                case InfoPanelMode.Planet:      UpdateInfoPlanetPanel(force: true); break;
                case InfoPanelMode.Destination: UpdateDestinationPanel(force: true); break;
                case InfoPanelMode.Deorbit:     UpdateDeorbitPanel(force: true); break;
                case InfoPanelMode.FssScanner:  UpdateFssScannerPanel(force: true); break;
            }

            UpdateModeTabHighlight(mode);
        }

        private void UpdateModeTabHighlight(InfoPanelMode mode)
        {
            void Style(Button b, bool selected)
            {
                b.Background = selected ? new SolidColorBrush(Color.FromRgb(0x0d, 0x1a, 0x1a)) : new SolidColorBrush(Color.FromRgb(0x08, 0x0d, 0x0d));
                b.Foreground = selected ? InfoValueBrush : new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55));
            }
            Style(tabRadar,       mode == InfoPanelMode.Radar   || mode == InfoPanelMode.Deorbit);
            Style(tabStar,        mode == InfoPanelMode.Star    || mode == InfoPanelMode.FssScanner);
            Style(tabPlanet,      mode == InfoPanelMode.Planet);
            Style(tabDestination, mode == InfoPanelMode.Destination);
        }

        private void ModeTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!Enum.TryParse<InfoPanelMode>(tagStr, out var clicked)) return;

            // The Radar and Star tab buttons relabel themselves ("⛛ DEORBIT", "📡 FSS SCANNER")
            // and take over their own tab's content while those temporary modes are active, but
            // their Tag — and so what a click actually requests — never changes from the plain
            // Radar/Star it started as. Real report: clicking away from the Deorbit animation to
            // another tab mid-glide left no way back to it — the tab still SAYS "DEORBIT", but
            // clicking it asked for literal Radar, which shows nothing (no position yet), and
            // the edge-detection that clears a manual override only fires when Deorbit/FSS
            // Scanner starts or stops, not just because the player wants to look at it again
            // mid-glide. Clicking the tab should always resolve to whichever of the pair is
            // actually current right now, matching what the button itself displays.
            var status = _watcher?.CurrentStatus ?? new EliteStatus();
            if (clicked == InfoPanelMode.Radar && ShouldShowDeorbit(status)) clicked = InfoPanelMode.Deorbit;
            else if (clicked == InfoPanelMode.Star && status.GuiFocus == 9) clicked = InfoPanelMode.FssScanner;

            _manualMode = clicked;
            _lastMode = clicked;
            ApplyInfoPanelMode(clicked);
        }

        private void UpdateStarPanel(bool force = false)
        {
            // Prefer whichever star is actually targeted (a secondary/tertiary star in a
            // multi-star system) over the primary — falls back to the primary when nothing
            // is targeted, or when the primary itself is the target.
            var detail = _watcher?.TargetedStarDetail ?? _watcher?.CurrentStarDetail;
            if (PreviewStarType() is string previewType) detail = GetPreviewStar(previewType);
            if (!force && ReferenceEquals(detail, _lastRenderedStar)) return;
            _lastRenderedStar = detail;

            // The displayed star is genuinely changing (or re-rendering under `force`) — stop
            // any neutron jet timer from whatever was shown before. RenderNeutronStarPanel
            // starts a fresh one below if the new star needs it.
            _neutronJetTimer?.Stop();
            _neutronJetTimer = null;

            starPanelCanvas.Children.Clear();
            starPanelCanvas.Children.Add(MakeGridBackground(713, 580));

            if (detail == null)
            {
                starPanelCanvas.Children.Add(MakeCenterLabel("AWAITING STAR SCAN…", 356.5, 280, InfoDimBrush, 13));
                return;
            }

            // Neutron stars AND white dwarfs share the same jet-and-core render (see
            // StarRenderer's class-level comment / IsWhiteDwarf's own comment — direct request,
            // "they are similar looking", not a claim that white dwarfs really have jets).
            // Checked before IsProceduralStarFamily since neither "N" nor a "D"-prefixed class
            // is part of that real-fusion family at all.
            if (StarRenderer.IsNeutronStar(detail.StarType) || StarRenderer.IsWhiteDwarf(detail.StarType))
            {
                RenderNeutronStarPanel(detail);
                return;
            }

            // Real fusion stars (O through M, Wolf-Rayet, brown dwarfs) get the full
            // art-direction rework: a real granulated/flaring procedural sphere and the same
            // no-leader-line stacked HUD the Planet tab already has.
            if (StarRenderer.IsProceduralStarFamily(detail.StarType))
            {
                RenderStarPanel(detail);
                return;
            }

            // Black hole — a deliberately modest animated render (the existing flat icon's own
            // black-disc-plus-amber-glow-ring look, just alive via a cross-faded glow pulse)
            // rather than the ambitious accretion-disk/lensing redesign attempted and explicitly
            // abandoned earlier — that needs a real in-game reference before another attempt.
            if (StarRenderer.IsBlackHole(detail.StarType))
            {
                RenderBlackHoleStarPanel(detail);
                return;
            }

            // Only reachable with a null/empty StarType (IsProceduralStarFamily covers every
            // other value, known or not) — no art to show, just the stats. The old flat icon
            // fallback and its star_*.png assets have been removed entirely.
            AddStarHudStats(detail);
        }

        // Procedural star family — real granulation/flare sphere (StarRenderer) plus the same
        // no-leader-line stacked HUD as the gas-giant/terrain panels, reusing that exact same
        // scene-box/column/font machinery (AddStackedStat/MakeHudCenterLabel/MakeTraitChip).
        private void RenderStarPanel(BodyScanDetail detail)
        {
            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;

            // Real rings — only a handful of dim stars (brown dwarfs) actually have them, so
            // this is entirely additive: two extra layers around the existing granulation/flare
            // stack rather than anything touching that pipeline's own sphere geometry. Same
            // "not a belt" filter the STAR tab's own legacy RINGS callout already uses.
            var realStarRing = detail.Rings.FirstOrDefault(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));
            if (realStarRing != null)
            {
                var (ringBack, _) = StarRenderer.GetStarRingLayers(detail, sceneW, sceneH);
                var imgRingBack = new Image { Width = sceneW, Height = sceneH, Source = ringBack };
                Canvas.SetLeft(imgRingBack, sceneX); Canvas.SetTop(imgRingBack, sceneY);
                starPanelCanvas.Children.Add(imgRingBack);
            }

            bool starOnGpu = TryAddStarShaderScene(detail, sceneW, sceneH, sceneX, sceneY);
            if (!starOnGpu)
            {
            var (baseLayer, surfaceA, surfaceB, topLayer) = StarRenderer.GetStarLayers(detail, sceneW, sceneH);
            var imgBase = new Image { Width = sceneW, Height = sceneH, Source = baseLayer };
            var imgSurfaceA = new Image { Width = sceneW, Height = sceneH, Source = surfaceA };
            var imgSurfaceB = new Image { Width = sceneW, Height = sceneH, Source = surfaceB, Opacity = 0 };
            var imgTop = new Image { Width = sceneW, Height = sceneH, Source = topLayer };
            foreach (var img in new[] { imgBase, imgSurfaceA, imgSurfaceB, imgTop })
            { Canvas.SetLeft(img, sceneX); Canvas.SetTop(img, sceneY); }
            starPanelCanvas.Children.Add(imgBase);
            starPanelCanvas.Children.Add(imgSurfaceA);
            starPanelCanvas.Children.Add(imgSurfaceB);
            starPanelCanvas.Children.Add(imgTop);
            // Was 3s (same as the gas-giant cloud layer) — slowed ~60% per direct feedback on
            // the granulation "bubbles and spots" specifically, so they fade in/out gradually
            // instead of visibly churning. 3s / (1 - 0.6) = 7.5s.
            imgSurfaceB.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = 0, To = 1, Duration = TimeSpan.FromSeconds(7.5),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase(),
            });

            // Flares — each its own independent layer/animation now, not baked into the
            // surfaceA/B cross-fade above. That old 2-frame system meant any flare slot lit in
            // BOTH frames just sat there looking frozen while only the ones that differed
            // between frames ever visibly changed — reported directly: "you can watch three of
            // them disappear and come back, the others never change." A negative BeginTime
            // starts each flare's clock already partway into its own cycle, so a whole cluster
            // fades in/out on staggered, overlapping schedules instead of in lockstep.
            int flareCount = StarRenderer.GetFlareCount(detail);
            for (int f = 0; f < flareCount; f++)
            {
                var flareFrame = StarRenderer.GetFlareFrame(detail, f, sceneW, sceneH);
                var imgFlare = new Image { Width = sceneW, Height = sceneH, Source = flareFrame, Opacity = 0 };
                Canvas.SetLeft(imgFlare, sceneX); Canvas.SetTop(imgFlare, sceneY);
                starPanelCanvas.Children.Add(imgFlare);
                var (period, beginOffset) = StarRenderer.GetFlareTiming(detail, f);
                // No EasingFunction here (unlike the sphere/cloud cross-fades) — a "slow, EVEN
                // fade" was the explicit ask, and SineEase speeds up through the middle of the
                // ramp and eases at both ends, which reads as uneven at a duration this long.
                // Plain linear interpolation is what actually looks like a constant, even fade.
                imgFlare.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromSeconds(period),
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(-beginOffset),
                });
            }

            }

            // Front ring sliver, drawn last so it passes in front of the whole sphere/flare
            // stack — the classic ring-crosses-the-near-limb illusion.
            if (realStarRing != null)
            {
                var (_, ringFront) = StarRenderer.GetStarRingLayers(detail, sceneW, sceneH);
                var imgRingFront = new Image { Width = sceneW, Height = sceneH, Source = ringFront };
                Canvas.SetLeft(imgRingFront, sceneX); Canvas.SetTop(imgRingFront, sceneY);
                starPanelCanvas.Children.Add(imgRingFront);
            }

            AddStarHudStats(detail);
        }

        // GPU star: one Rectangle carrying StarShaderEffect draws the photosphere, sunspots, halo,
        // coronal loops and mass ejections and animates itself from Time. Returns false (caller
        // falls back to the CPU layers) on software-only rendering or any setup failure.
        private bool TryAddStarShaderScene(BodyScanDetail detail, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                var look = StarRenderer.GetStarLook(detail);
                var (cx, cy, R) = StarRenderer.GetStarShaderDisc(detail, sceneW, sceneH);
                var effect = StarShaderEffect.Create(look,
                    new Point(cx / sceneW, cy / sceneH), new Point(R / sceneW, R / sceneH));
                effect.BeginAnimation(StarShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                var surface = new System.Windows.Shapes.Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect };
                { var ssHost = Supersample(surface); Canvas.SetLeft(ssHost, sceneX); Canvas.SetTop(ssHost, sceneY);
                starPanelCanvas.Children.Add(ssHost); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Star shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        // Neutron stars ("N") AND white dwarfs ("D"-prefixed) share this dedicated jet-and-core
        // render instead of the granulated procedural-star pipeline — a degenerate remnant has
        // no photosphere to granulate, and the two look similar enough to share one treatment
        // (direct request). Same stacked HUD stats as every other star, just different art
        // underneath.
        //
        // The jets are redrawn live on a timer (a continuously advancing phase, one fresh frame
        // per tick) rather than cross-faded between two cached static frames — see
        // StarRenderer.GetNeutronCore's comment for why the 2-frame approach broke down once the
        // wave amplitude got large enough to look like real whip motion (real screenshot: it
        // showed as two overlapping "tails" at the tips during the cross-fade transition).
        private void RenderNeutronStarPanel(BodyScanDetail detail)
        {
            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;

            if ((StarRenderer.IsNeutronStar(detail.StarType) && TryAddNeutronShaderScene(detail, sceneW, sceneH, sceneX, sceneY))
                || (StarRenderer.IsWhiteDwarf(detail.StarType) && TryAddWhiteDwarfShaderScene(detail, sceneW, sceneH, sceneX, sceneY)))
            {
                AddStarHudStats(detail);
                return;
            }

            var core = StarRenderer.GetNeutronCore(detail, sceneW, sceneH);
            var imgCore = new Image { Width = sceneW, Height = sceneH, Source = core };
            var imgJets = new Image { Width = sceneW, Height = sceneH, Source = StarRenderer.RenderNeutronJets(detail, sceneW, sceneH, 0.0) };
            foreach (var img in new[] { imgJets, imgCore })
            { Canvas.SetLeft(img, sceneX); Canvas.SetTop(img, sceneY); }
            // Jets drawn first, core on top — it sits right where both arms converge, the same
            // "glow behind, disc/core in front" layering the procedural star panel uses.
            starPanelCanvas.Children.Add(imgJets);
            starPanelCanvas.Children.Add(imgCore);

            // ~20fps, a full phase cycle every ~8s — fast enough to read as a continuous sweep
            // (matching the approved mockup's "cone shape as they spin" look), slow enough not
            // to be frantic. _neutronJetTimer is stopped/replaced by UpdateStarPanel whenever
            // the displayed star changes, and by the mode-switch handler on leaving Star mode.
            double phase = 0;
            _neutronJetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _neutronJetTimer.Tick += (_, __) =>
            {
                phase += 0.0393;
                imgJets.Source = StarRenderer.RenderNeutronJets(detail, sceneW, sceneH, phase);
            };
            _neutronJetTimer.Start();

            AddStarHudStats(detail);
        }

        // Black hole — the existing flat icon's own look (black disc + amber glow ring), just
        // alive via a 2-frame cross-fade of the glow (same opacity-DoubleAnimation idiom every
        // other panel here uses) instead of a static PNG. See StarRenderer.GetBlackHoleLayers.
        private void RenderBlackHoleStarPanel(BodyScanDetail detail)
        {
            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;
            if (TryAddBlackHoleShaderScene(detail, sceneW, sceneH, sceneX, sceneY))
            {
                AddStarHudStats(detail);
                return;
            }

            var (core, glowA, glowB) = StarRenderer.GetBlackHoleLayers(detail, sceneW, sceneH);
            var imgGlowA = new Image { Width = sceneW, Height = sceneH, Source = glowA };
            var imgGlowB = new Image { Width = sceneW, Height = sceneH, Source = glowB, Opacity = 0 };
            var imgCore = new Image { Width = sceneW, Height = sceneH, Source = core };
            foreach (var img in new[] { imgGlowA, imgGlowB, imgCore })
            { Canvas.SetLeft(img, sceneX); Canvas.SetTop(img, sceneY); }
            starPanelCanvas.Children.Add(imgGlowA);
            starPanelCanvas.Children.Add(imgGlowB);
            starPanelCanvas.Children.Add(imgCore);
            imgGlowB.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = 0, To = 1, Duration = TimeSpan.FromSeconds(3.2),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase(),
            });

            AddStarHudStats(detail);
        }

        // Shared by RenderStarPanel and RenderNeutronStarPanel — same six stats/positions
        // regardless of which art renders underneath them.
        private void AddStarHudStats(BodyScanDetail detail)
        {
            double leftX = 164, rightX = 548, rowY = 78;
            const double sectionGap = 34;

            // sizeBump: 1.5 — the Star tab's own labels are all shorter than the Planet tab's
            // worst case ("SURFACE TEMP", used here too), which already had real headroom in
            // the 160px column at the base size, so there's room to run this whole panel a
            // notch larger without it wrapping or overflowing its column.
            const double starSizeBump = 1.5;
            // Habitable zone added under SURFACE TEMP (direct request), which pushed AGE out of the
            // left column — moved to the top of the right column, above SOLAR MASS, rather than
            // squeezing a fourth row into the left. Four rows at ~80px each (label + value + gap)
            // still end well above the star name at y=500, so nothing here needed shrinking.
            // Same conservative HZ bounds and AU→light-second conversion (~499 ls/AU) the System
            // Scan window shows, so the two screens can never quote different ranges. Shown as
            // "—" when the star's radius/temperature aren't known well enough to derive it.
            string habitableZoneText = "—";
            var hz = ScanValueEstimator.HabitableZoneAU(detail);
            if (hz.HasValue)
            {
                double innerLs = hz.Value.innerAU * 499, outerLs = hz.Value.outerAU * 499;
                string fmt = outerLs < 20 ? "N1" : "N0"; // tiny dwarfs' zones are only a few ls wide
                habitableZoneText = $"{innerLs.ToString(fmt)} – {outerLs.ToString(fmt)} ls";
            }

            double classBottomY = AddStackedStat(starPanelCanvas, leftX, rowY, false, "CLASS",
                $"{detail.StarType} ({StarClassNames.GetDisplayName(detail.StarType)})", InfoLabelGreenBrush, sizeBump: starSizeBump);
            double ageBottomY = AddStackedStat(starPanelCanvas, rightX, rowY, true, "AGE",
                detail.AgeMY > 0 ? $"{detail.AgeMY:N0} My" : "—", InfoLabelGreenBrush, sizeBump: starSizeBump);
            rowY = Math.Max(classBottomY, ageBottomY) + sectionGap;

            double tempBottomY = AddStackedStat(starPanelCanvas, leftX, rowY, false, "SURFACE TEMP",
                detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—", InfoLabelGreenBrush, sizeBump: starSizeBump);
            double massBottomY = AddStackedStat(starPanelCanvas, rightX, rowY, true, "SOLAR MASS",
                detail.StellarMass > 0 ? $"{detail.StellarMass:F2}" : "—", InfoLabelGreenBrush, sizeBump: starSizeBump);
            rowY = Math.Max(tempBottomY, massBottomY) + sectionGap;

            double hzBottomY = AddStackedStat(starPanelCanvas, leftX, rowY, false, "HABITABLE ZONE",
                habitableZoneText, InfoLabelGreenBrush, sizeBump: starSizeBump);
            double radiusBottomY = AddStackedStat(starPanelCanvas, rightX, rowY, true, "RADIUS (SOL)",
                detail.Radius > 0 ? $"{detail.Radius / 6.957e8:F2}" : "—", InfoLabelGreenBrush, sizeBump: starSizeBump);
            rowY = Math.Max(hzBottomY, radiusBottomY) + sectionGap;

            // Stars never have Saturn-style rings — a star's "Rings" entry is always an
            // asteroid belt (Name contains "Belt"), not a true ring, so exclude those rather
            // than mislabelling a belt as "the star has rings".
            var trueStarRing = detail.Rings.FirstOrDefault(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));
            double ringsBottomY = AddStackedStat(starPanelCanvas, rightX, rowY, true, "RINGS",
                trueStarRing != null ? FormatRingClass(trueStarRing.RingClass) : "None", InfoLabelGreenBrush, sizeBump: starSizeBump);
            rowY = ringsBottomY + sectionGap;

            bool isPrimary = _watcher != null && ReferenceEquals(detail, _watcher.CurrentStarDetail);
            var bottomLabel = EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "");
            starPanelCanvas.Children.Add(MakeHudCenterLabel(bottomLabel.ToUpperInvariant(), 356.5, 500, InfoBrightValueBrush, 16.5));
            starPanelCanvas.Children.Add(MakeHudCenterLabel(isPrimary ? "PRIMARY STAR" : "TARGETED STAR", 356.5, 522, InfoOrangeBrush, 14.5));
            AddDiscoveryLine(starPanelCanvas, detail, 356.5, 544, 13);
        }

        private void UpdateInfoPlanetPanel(bool force = false)
        {
            // While landed there's typically no in-system nav-panel target at all, so fall
            // back to whatever body the player is actually standing on.
            var detail = _watcher?.TargetedPlanetDetail ?? _watcher?.CurrentBodyDetail;

            // A nav-panel SIGNAL (e.g. a Planetary Mining Location Signal) is the current
            // target, and we're not close enough to have a real position yet — show the
            // signal illustration instead of the normal planet render. The moment a real
            // position appears (descent has begun), this falls away and normal rendering
            // takes over — see EliteWatcherService.TargetedSignalLabel for how the signal is
            // resolved to its real parent body in the first place.
            var status = _watcher?.CurrentStatus;
            string signalLabel = _watcher?.TargetedSignalLabel ?? "";
            bool showSignal = !string.IsNullOrEmpty(signalLabel) && status?.HasPosition != true;
            string signalKey = showSignal ? signalLabel : "";

            if (!force && ReferenceEquals(detail, _lastRenderedPlanet) && signalKey == _lastRenderedSignalKey) return;
            _lastRenderedPlanet = detail;
            _lastRenderedSignalKey = signalKey;

            planetPanelCanvas.Children.Clear();
            // 713, not 620 — the canvas itself was widened 15% for the gas-giant HUD's text
            // columns; the background grid has to cover the real width or the right side of
            // a wider canvas would show as a plain unlit strip for every body type.
            planetPanelCanvas.Children.Add(MakeGridBackground(713, 580));

            if (showSignal)
            {
                RenderSignalTargetPanel(signalLabel, detail);
                return;
            }

            if (detail == null)
            {
                planetPanelCanvas.Children.Add(MakeCenterLabel("NO PLANET TARGETED", 356.5, 280, InfoDimBrush, 13));
                return;
            }

            if (detail.IsBelt)
            {
                RenderAsteroidBeltPanel(detail);
                return;
            }

            // Gas-giant family gets the full art-direction rework now: real ring geometry
            // (not the flat per-class ring PNG), a bigger render, and the locked no-leader-
            // line stacked HUD — everything else still uses the pre-rework layout below until
            // its own phase lands.
            string? gasGiantCode = MapPlanetClassToIconCode(detail.PlanetClass);
            if (gasGiantCode != null && PlanetRenderer.IsGasGiantFamily(gasGiantCode))
            {
                RenderGasGiantPanel(detail, gasGiantCode);
                return;
            }

            // Terrain family — High Metal Content first (real craters/fissures/material tint).
            // Everything else non-gas-giant, non-terrain still uses the pre-rework flat-PNG
            // layout below until its own phase lands.
            if (gasGiantCode != null && PlanetRenderer.IsTerrainFamily(gasGiantCode))
            {
                RenderTerrainPanel(detail, gasGiantCode);
                return;
            }

            // Icon stack: ring back -> base -> ring front -> atmosphere -> terraformable -> bio/geo badge
            string? ringCode = detail.Rings.Count > 0 ? MapRingClass(detail.Rings[0].RingClass) : null;
            if (ringCode != null)
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/ring_{ringCode}_back.png", 217, 197, 186));

            // Gas-giant family already returned above via RenderGasGiantPanel, so anything
            // reaching here is one of the classes still on the pre-rework flat PNG set.
            string? planetCode = MapPlanetClassToIconCode(detail.PlanetClass);
            if (planetCode != null)
            {
                // Random-but-stable art variant per body, same idea as the asteroid belt art —
                // picked once per body and kept for as long as it's targeted this session.
                int variant = _watcher?.GetPlanetVariant(detail.BodyName, planetCode) ?? 0;
                string variantSuffix = variant > 0 ? $"_{variant}" : "";
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/{planetCode}{variantSuffix}.png", 217, 197, 186));
            }

            if (ringCode != null)
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/ring_{ringCode}_front.png", 217, 197, 186));

            if (!string.IsNullOrEmpty(detail.AtmosphereType) &&
                !string.Equals(detail.AtmosphereType, "None", StringComparison.OrdinalIgnoreCase))
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_atmosphere.png", 217, 197, 186));

            if (string.Equals(detail.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase))
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_terraformable.png", 217, 197, 186));

            bool hasBio = detail.BioSignalCount > 0, hasGeo = detail.GeoSignalCount > 0;
            bool hasMining = detail.MiningSignalCount > 0;
            if (hasBio && hasGeo)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_combo.png", 217, 197, 186));
            else if (hasBio)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_bio.png", 217, 197, 186));
            else if (hasGeo)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_geo.png", 217, 197, 186));
            // No PNG art for this one yet (brand new signal type) — drawn as a small vector
            // badge instead, same dark-disc-plus-glow language as the real bio/geo art, in an
            // open corner of the icon box the existing badges don't occupy.
            // Mirrored across the planet from the bio/geo badge, measured directly off the real
            // overlay_badge_bio.png asset (its circle sits ~26% across, ~74% down its own 186x186
            // icon box — right at the outer edge, not tucked inward) rather than guessed from a
            // screenshot, so this lands at the same height and the same distance from the edge.
            if (hasMining)
                planetPanelCanvas.Children.Add(MakeMiningBadge(355, 334, 28));

            // Ring hotspots — a separate location type from the surface signal above (a planet
            // can have both a minable ring AND minable surface locations at once), so it gets
            // its own badge rather than folding into MiningSignalCount. Aggregated across every
            // ring this body has (usually one, but up to 3 — A/B/C — are possible) since
            // RingHotspots is keyed by the ring's own body name, not the planet's.
            var ringHotspots = new List<RingHotspotSignal>();
            if (_watcher != null)
                foreach (var ring in detail.Rings)
                    if (_watcher.RingHotspots.TryGetValue(ring.Name, out var spots))
                        ringHotspots.AddRange(spots);
            int ringHotspotTotal = ringHotspots.Sum(s => s.Count);
            if (ringHotspotTotal > 0)
                // Placed at the ring's own left tip (measured off the real ring_Rocky_back.png
                // art — the full ellipse's outer edge sits there, clear of the planet body and
                // every other badge) rather than on the planet itself, since this is a ring
                // signal, not a surface one.
                planetPanelCanvas.Children.Add(MakeRingMiningBadge(221, 303, 26));

            bool isGasGiantFamily = planetCode != null &&
                (planetCode.StartsWith("GG", StringComparison.Ordinal) || planetCode == "WTG");
            AddCallout(planetPanelCanvas, new (double, double)[] { (246, 213), (208, 180), (150, 180) }, 180, false,
                "PLANET CLASS", isGasGiantFamily ? FormatGasGiantClass(detail.PlanetClass, planetCode) : detail.PlanetClass);
            AddCallout(planetPanelCanvas, new (double, double)[] { (210, 290), (150, 290) }, 290, false,
                "SURFACE TEMP", detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—");
            AddCallout(planetPanelCanvas, new (double, double)[] { (246, 367), (208, 400), (150, 400) }, 400, false,
                "GEO SIGNALS", hasGeo ? $"{detail.GeoSignalCount} found" : "None", keyBrush: InfoOrangeBrush);

            // Journal's SurfaceGravity is m/s², not G (confirmed against real data — values
            // range up to ~30, Earth is 9.8) — convert using standard gravity (9.80665 m/s²/G).
            double legacyGravityG = detail.SurfaceGravity > 0 ? detail.SurfaceGravity / 9.80665 : 0;
            AddCallout(planetPanelCanvas, new (double, double)[] { (375, 213), (412, 180), (470, 180) }, 180, true,
                "GRAVITY", detail.SurfaceGravity > 0 ? $"{legacyGravityG:F2} G" : "—",
                valueBrush: IsHighGravity(legacyGravityG, detail.Landable) ? InfoGravityWarnBrush : null);
            AddCallout(planetPanelCanvas, new (double, double)[] { (410, 290), (470, 290) }, 290, true,
                "ATMOSPHERE", FormatAtmosphere(detail.AtmosphereType));
            AddCallout(planetPanelCanvas, new (double, double)[] { (375, 367), (412, 400), (470, 400) }, 400, true,
                "BIO SIGNALS", hasBio ? $"{detail.BioSignalCount} found" : "None", keyBrush: InfoValueBrush);

            // Mining Sites — centered under the planet, same row as GEO/BIO, but with no
            // leader line: at this distance from the planet a line down to it added noise
            // without actually clarifying anything a plain centered label didn't already say.
            if (hasMining)
            {
                planetPanelCanvas.Children.Add(MakeCenterLabel("MINING SITES", 310, 390, InfoMiningBrush, 13.5));
                planetPanelCanvas.Children.Add(MakeCenterLabel($"{detail.MiningSignalCount} found", 310, 412, InfoBrightValueBrush, 15));
            }

            // Ring Mining — separate row from Mining Sites above (a ring and a surface can both
            // be minable on the same body), sharing its slot when surface mining is absent so
            // the two don't fight for space when both are present.
            if (ringHotspotTotal > 0)
            {
                double ringY = hasMining ? 432 : 390;
                planetPanelCanvas.Children.Add(MakeCenterLabel("RING MINING", 310, ringY, InfoRingMiningBrush, 13.5));
                var materialsStr = string.Join(", ",
                    ringHotspots.OrderByDescending(s => s.Count).Select(s => $"{s.Material} ×{s.Count}"));
                var ringValueTb = new TextBlock
                {
                    Text = $"{ringHotspotTotal} found — {materialsStr}",
                    Foreground = InfoBrightValueBrush, FontFamily = new FontFamily("Consolas"), FontSize = 12,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Width = 260,
                };
                Canvas.SetLeft(ringValueTb, 310 - 130);
                Canvas.SetTop(ringValueTb, ringY + 20);
                planetPanelCanvas.Children.Add(ringValueTb);
            }

            // DSS mapping reveals no new physical fields over a Detailed scan (confirmed against
            // real journal data — the re-fired Scan event is identical) except one real reward:
            // Ice/Rock/Metal composition. Worth surfacing once mapped, so it gets its own callout
            // in the empty space above the planet instead of just a "MAPPED" label with no payoff.
            // Centered above the planet (not left/right like the other callouts), so it's built
            // by hand here rather than through AddCallout's left/right-only text alignment.
            double compTotal = detail.IceComposition + detail.RockComposition + detail.MetalComposition;
            if (compTotal > 0)
            {
                var compPoly = new Polyline { Stroke = InfoLeaderBrush, StrokeThickness = 1.6 };
                foreach (var p in new (double x, double y)[] { (300, 200), (180, 66), (250, 66) })
                    compPoly.Points.Add(new Point(p.x, p.y));
                planetPanelCanvas.Children.Add(compPoly);

                planetPanelCanvas.Children.Add(MakeCenterLabel("COMPOSITION", 310, 56, InfoValueBrush, 13.5));
                planetPanelCanvas.Children.Add(MakeCenterLabel($"ICE {detail.IceComposition * 100:F0}%", 310, 76, InfoBrightValueBrush, 13));
                planetPanelCanvas.Children.Add(MakeCenterLabel($"ROCK {detail.RockComposition * 100:F0}%", 310, 94, InfoBrightValueBrush, 13));
                planetPanelCanvas.Children.Add(MakeCenterLabel($"METAL {detail.MetalComposition * 100:F0}%", 310, 112, InfoBrightValueBrush, 13));
            }

            // For gas-giant-family planets the bottom label shows the broad type ("Gas Giant" /
            // "Water Giant") instead of the short body designation — the top toolbar already
            // shows the full body name, so this spot is more useful as a plain-language type.
            var bottomLabel = isGasGiantFamily
                ? FormatGasGiantType(planetCode)
                : EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "");
            // Bumped up (was a fixed 500 start) and laid out with a running cursor instead of
            // hardcoded y's for LANDABLE/TERRAFORMABLE — both are conditional, so a fixed gap
            // between every line regardless of which ones are actually present either wasted
            // space or (with both bumped-up SCAN and a re-added TERRAFORMABLE line) ran short.
            double y = 494;
            planetPanelCanvas.Children.Add(MakeCenterLabel(bottomLabel.ToUpperInvariant(), 310, y, InfoValueBrush, 17));
            y += 21;
            if (detail.Landable)
            {
                planetPanelCanvas.Children.Add(MakeCenterLabel("LANDABLE", 310, y, InfoValueBrush, 13));
                y += 17;
            }
            if (string.Equals(detail.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase))
            {
                planetPanelCanvas.Children.Add(MakeCenterLabel("TERRAFORMABLE", 310, y, InfoOrangeBrush, 13));
                y += 17;
            }
            // Matches the callouts' own value size (CLASS/GRAVITY/etc via AddCallout use 15,
            // bold) — this was a much smaller 12 despite being just as important a readout.
            var scanTag = detail.IsMapped ? "MAPPED" : string.IsNullOrEmpty(detail.ScanType) ? "UNKNOWN" : detail.ScanType.ToUpperInvariant();
            var scanLabel = MakeCenterLabel($"SCAN: {scanTag}", 310, y, InfoOrangeBrush, 15);
            scanLabel.FontWeight = FontWeights.Bold;
            planetPanelCanvas.Children.Add(scanLabel);
        }

        // Gas-giant family Planet tab — the locked art-direction rework: real ring geometry
        // and ring-hotspot glints instead of the flat per-class ring PNG, a bigger render (the
        // narrow leader-line columns this replaces were wasting width the render can use
        // instead), and the class title moved to the top instead of the bottom. Everything
        // else (rocky/icy/metal terrain, etc.) still uses the pre-rework layout below this
        // method until its own phase lands.
        // Renders a shader scene at 2x and smooths it down. A pixel shader cannot antialias its own contours (band
        // edges, storm outlines, cracks), which showed up as single-pixel stair-steps; a 2x bitmap cache with
        // high-quality downscaling is a cheap supersample for these small panels.
        private static FrameworkElement Supersample(FrameworkElement scene)
        {
            var host = new Canvas { Width = scene.Width, Height = scene.Height, CacheMode = new BitmapCache(2.0) };
            RenderOptions.SetBitmapScalingMode(host, BitmapScalingMode.HighQuality);
            host.Children.Add(scene);
            return host;
        }

        // GPU gas giant: ring back-pass image, then a Rectangle carrying GasGiantShaderEffect (which
        // draws the whole lit sphere and animates its own rotation from Time), then the
        // front-sliver/atmosphere layer. Returns false (caller falls back to the CPU layers) on
        // software-only rendering or any setup failure.
        private bool TryAddGasGiantShaderScene(BodyScanDetail detail, string iconCode, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                var (ringBack, top) = PlanetRenderer.GetGasGiantShaderStaticLayers(detail, iconCode, sceneW, sceneH);
                var look = PlanetRenderer.GetGasGiantLook(detail, iconCode);
                var (cx, cy, sphereR) = PlanetRenderer.GetGasGiantSphere(detail, sceneW, sceneH);
                var effect = GasGiantShaderEffect.Create(look,
                    new Point(cx / sceneW, cy / sceneH), new Point(sphereR / sceneW, sphereR / sceneH));
                // Linear, effectively endless (10h) - the shader treats Time as seconds of rotation.
                effect.BeginAnimation(GasGiantShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));

                var imgRing = new Image { Width = sceneW, Height = sceneH, Source = ringBack };
                var surface = new System.Windows.Shapes.Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect };
                var imgTop = new Image { Width = sceneW, Height = sceneH, Source = top };
                foreach (var el in new FrameworkElement[] { imgRing, Supersample(surface), imgTop })
                { Canvas.SetLeft(el, sceneX); Canvas.SetTop(el, sceneY); planetPanelCanvas.Children.Add(el); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Gas giant shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        private void RenderGasGiantPanel(BodyScanDetail detail, string iconCode)
        {
            bool hasBio = detail.BioSignalCount > 0, hasGeo = detail.GeoSignalCount > 0;
            bool hasMining = detail.MiningSignalCount > 0;

            // Diagnostic for the still-missing-rings report — three rounds of guessing at the
            // rendering math without being able to see the actual data behind it. This tells
            // us on the next test whether detail.Rings/Radius genuinely has nothing in it
            // (a data problem, not a rendering one) or whether the ring math itself is still
            // wrong despite the last two fixes.
            Log.Write($"RenderGasGiantPanel: {detail.BodyName} radius={detail.Radius} rings={detail.Rings.Count} " +
                string.Join(" | ", detail.Rings.Select(r => $"{r.RingClass} in={r.InnerRad:E2} out={r.OuterRad:E2}")));

            var ringHotspots = new List<RingHotspotSignal>();
            if (_watcher != null)
                foreach (var ring in detail.Rings)
                    if (_watcher.RingHotspots.TryGetValue(ring.Name, out var spots))
                        ringHotspots.AddRange(spots);
            int ringHotspotTotal = ringHotspots.Sum(s => s.Count);

            // No separate top title — the mockup never had one either; "Gas Giant" and its
            // trait are the PLANET CLASS stat's own value and chip (see below), not a
            // standalone header. The first pass of this rework added one by mistake.

            // Scene — real ring geometry, hotspot glints, cross-faded cloud drift. Wider than
            // the first pass (360→460) and the sphere itself smaller relative to it (was 34%
            // of the box, now 26%) specifically so a real wide ring (this body's outer ring
            // sits at ~2.9x its own planet radius) has room to actually extend beyond the
            // sphere instead of the two competing for the same limited width.
            // Narrower again than the previous pass (460→400→370) to give the text columns
            // still more width per the user's repeated feedback that even the 90px-wide
            // columns still felt cramped — "Planet Class" and "Water-Based Life" have to
            // fit without wrapping, which narrower columns never could regardless of font
            // tuning.
            // sceneY nudged down from 18 — the whole panel (scene + text) was rendering high
            // in the 580-tall canvas, leaving dead space above the bottom SCAN/body-name
            // labels instead of filling the space evenly.
            // sceneX recentered for the widened (620→713) canvas — sceneW/sceneH unchanged,
            // all 93px of extra width goes to the text columns below, not the scene itself.
            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;
            // Four layers, not two — base/top are static (rings, sphere gradient, limb
            // darkening, front-sliver, atmosphere glow) and drawn once at full opacity; only
            // the two cloud frames cross-fade. The old 2-frame version baked the ENTIRE scene
            // into both frames and cross-faded the whole thing via opacity — since every static
            // element is pixel-identical between frame A and B, stacking two semi-transparent
            // copies of the same content doesn't reproduce one opaque layer (alpha compositing
            // math), so the rings/glow/limb all visibly "breathed" over the cycle even though
            // none of them ever actually changed. Splitting the bake so only the cloud bands
            // sit in the cross-fade fixes that at the source instead of just hiding it.
            // GPU shader path first (rotating, turbulent bands - see GasGiantShaderEffect); the
            // original CPU layers below remain as the fallback for software-rendering machines.
            if (!TryAddGasGiantShaderScene(detail, iconCode, sceneW, sceneH, sceneX, sceneY))
            {
            var (baseLayer, cloudA, cloudB, topLayer) = PlanetRenderer.GetGasGiantLayers(detail, iconCode, sceneW, sceneH);
            var imgBase = new Image { Width = sceneW, Height = sceneH, Source = baseLayer };
            var imgCloudA = new Image { Width = sceneW, Height = sceneH, Source = cloudA };
            var imgCloudB = new Image { Width = sceneW, Height = sceneH, Source = cloudB, Opacity = 0 };
            var imgTop = new Image { Width = sceneW, Height = sceneH, Source = topLayer };
            foreach (var img in new[] { imgBase, imgCloudA, imgCloudB, imgTop })
            { Canvas.SetLeft(img, sceneX); Canvas.SetTop(img, sceneY); }
            planetPanelCanvas.Children.Add(imgBase);
            planetPanelCanvas.Children.Add(imgCloudA);
            planetPanelCanvas.Children.Add(imgCloudB);
            planetPanelCanvas.Children.Add(imgTop);
            // Duration was 9s (paired with the clouds' old 1.1 phase, in PlanetRenderer.cs) —
            // the drift was barely perceptible without close attention. 3s cycles the same
            // cross-fade noticeably faster; combined with the clouds' wider phase swing there,
            // this is the actual "more active movement" fix, not just a speed change on its own.
            // Revert both together for the original subtle look.
            imgCloudB.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = 0, To = 1, Duration = TimeSpan.FromSeconds(3),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase(),
            });
            }

            // Bio/Geo/Mining/Ring-Mining badges — carried over from the pre-rework layout
            // (dropped by mistake in the first pass of this rework). Mining badge stays a
            // fixed offset from the scene's bottom-right corner (it isn't tied to any specific
            // geometry). Ring-mining badge now anchors to the actual rendered ring's own
            // midline instead — a fixed corner offset only ever lined up by coincidence and
            // fell apart once ring geometry started varying per body (proportional rescale,
            // multiple ring bands, etc).
            if (hasMining)
                planetPanelCanvas.Children.Add(MakeMiningBadge(sceneX + sceneW - 50, sceneY + sceneH - 50, 28));
            if (ringHotspotTotal > 0)
            {
                var anchor = PlanetRenderer.GetRingMiningAnchor(detail, sceneW, sceneH);
                if (anchor.HasValue)
                    planetPanelCanvas.Children.Add(MakeRingMiningBadge(sceneX + anchor.Value.X, sceneY + anchor.Value.Y, 26));
            }

            // Stacked stats, no leader lines — the narrow columns beside the scene replace
            // the old diagonal-line callouts entirely for this family. Generic stats use the
            // same neutral teal as the mockup; only the signal-type rows carry their own
            // category color. leftX/rightX are the column's OUTER edge (right edge of the
            // left column, left edge of the right column) — with the canvas widened 620→713
            // and StatColWidth 114→160 (all 93px of new width going to these columns, not the
            // scene), that puts the left column at x:4-164 and the right column at x:548-708,
            // a 4-5px margin inside the panel's own 0-713 edges, and the same small ~7px gap
            // from the scene on both sides as before.
            // A fixed rowGap couldn't work once one column had extra content the other
            // didn't — Planet Class's trait chip made the left column taller than the right
            // column's plain Gravity row, so a rowGap sized for the shorter row let Surface
            // Temp start almost on top of the chip below it. Instead, each section's start is
            // now the REAL measured bottom of whichever column (including the chip) ran
            // longest, plus one fixed sectionGap — that's what actually gives uniform visual
            // spacing between sections regardless of how tall any one row's content is.
            // Shifted down from the previous 58 start — the whole block (scene + text) was
            // sitting high in the panel, well clear of the bottom SCAN/body-name labels.
            double leftX = 164, rightX = 548, rowY = 78;
            const double sectionGap = 24;   // columns flow independently

            // Planet Class shows the broad type ("Gas Giant") as its value, with the specific
            // trait (a Sudarsky class, or a life/composition variant) as a chip underneath —
            // this IS the mockup's actual design; a standalone header at the top (the first
            // pass of this rework) was never part of it.
            double classBottomY = AddStackedStat(planetPanelCanvas, leftX, rowY, false, "PLANET CLASS", FormatGasGiantType(iconCode), InfoLabelGreenBrush, sizeBump: 1.5);
            // Positioned against the real measured bottom of the label+value above, with a
            // few px of its own breathing room — the first two passes guessed a fixed offset
            // assuming both were always one line, which is what actually caused the chip to
            // land on top of the value text.
            // rightAlign:false to match Planet Class's own column alignment above it — the
            // chip was centering under the column instead of lining up with the rest of that
            // column's flush-right text.
            var traitChip = MakeTraitChip(FormatGasGiantClass(detail.PlanetClass, iconCode).ToUpperInvariant(),
                leftX, classBottomY + 4, StatColWidth, rightAlign: false);
            planetPanelCanvas.Children.Add(traitChip);
            double chipBottomY = classBottomY + 4 + traitChip.DesiredSize.Height;

            double gasGiantGravityG = detail.SurfaceGravity > 0 ? detail.SurfaceGravity / 9.80665 : 0;
            bool gasGiantHighGravity = IsHighGravity(gasGiantGravityG, detail.Landable);
            double gravBottomY = AddStackedStat(planetPanelCanvas, rightX, rowY, true, "GRAVITY",
                detail.SurfaceGravity > 0 ? $"{gasGiantGravityG:F2} G" : "—", InfoLabelGreenBrush, sizeBump: 1.5,
                valueBrush: gasGiantHighGravity ? InfoGravityWarnBrush : null,
                chipAbove: gasGiantHighGravity ? MakeSmallChip("HIGH GRAVITY", InfoGravityWarnBrush.Color) : null);
            double leftY = chipBottomY + sectionGap, rightY = gravBottomY + sectionGap;

            double tempBottomY = AddStackedStat(planetPanelCanvas, leftX, leftY, false, "SURFACE TEMP", detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—", InfoLabelGreenBrush, sizeBump: 1.5);
            double atmoBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "ATMOSPHERE", FormatGasGiantAtmosphere(detail), InfoLabelGreenBrush, sizeBump: 1.5);
            leftY = tempBottomY + sectionGap; rightY = atmoBottomY + sectionGap;

            double geoBottomY = AddStackedStat(planetPanelCanvas, leftX, leftY, false, "GEO SIGNALS", hasGeo ? $"{detail.GeoSignalCount} found" : "None", InfoOrangeBrush, sizeBump: 1.5);
            double bioBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "BIO SIGNALS", hasBio ? $"{detail.BioSignalCount} found" : "None", InfoValueBrush, sizeBump: 1.5);
            leftY = geoBottomY + sectionGap; rightY = bioBottomY + sectionGap;

            if (ringHotspotTotal > 0)
            {
                var materials = string.Join(", ", ringHotspots.OrderByDescending(h => h.Count).Select(h => $"{h.Material} ×{h.Count}"));
                AddStackedStat(planetPanelCanvas, leftX, leftY, false, "RING MINING", $"{ringHotspotTotal} found", InfoRingMiningBrush, materials, sizeBump: 1.5);
            }
            if (hasMining)
                AddStackedStat(planetPanelCanvas, rightX, rightY, true, "MINING SITES", $"{detail.MiningSignalCount} found", InfoMiningBrush, sizeBump: 1.5);

            // Recentered for the widened canvas: 713/2 = 356.5.
            var bottomLabel = EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "");
            planetPanelCanvas.Children.Add(MakeHudCenterLabel(bottomLabel.ToUpperInvariant(), 356.5, 500, InfoBrightValueBrush, 16.5));
            var scanTag = detail.IsMapped ? "MAPPED" : string.IsNullOrEmpty(detail.ScanType) ? "UNKNOWN" : detail.ScanType.ToUpperInvariant();
            var scanLabel = MakeHudCenterLabel($"SCAN: {scanTag}", 356.5, 522, InfoOrangeBrush, 14.5);
            scanLabel.FontWeight = FontWeights.Bold;
            planetPanelCanvas.Children.Add(scanLabel);
            AddDiscoveryLine(planetPanelCanvas, detail, 356.5, 544, 13);
        }

        // Terrain family (High Metal Content first) — the locked "High Metal Content Render
        // Concept" mockup: real impact craters, fissures gated on real Volcanism, surface tint
        // from real Materials, tidal-lock terminator, hot/cold airless limb. Same no-leader-
        // line stacked HUD as the gas-giant panel, reusing the exact same scene box/columns/
        // font/spacing machinery — only the stat rows themselves differ (Planet Class's trait
        // chip is conditional on TidalLock here, not always present; Geo Signals gets a real
        // Volcanism sub-line the same way Ring Mining gets a materials sub-line).
        // GPU landable world (landable HMC): one Rectangle carrying LandableWorldShaderEffect draws the
        // lit sphere with terrain patches, craters, rust streaks, frost and glowing flecks, and animates
        // itself from Time. Returns false (caller uses the CPU terrain scene) for any other body, on
        // software-only rendering, or on any setup failure.
        private bool TryAddLandableWorldScene(BodyScanDetail detail, string iconCode, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if (!PlanetRenderer.IsLandableWorld(detail, iconCode)) return false;
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                var look = PlanetRenderer.GetLandableWorldLook(detail, iconCode);
                var (cx, cy, R) = PlanetRenderer.GetTerrainGeometry(sceneW, sceneH, detail);
                var effect = LandableWorldShaderEffect.Create(look,
                    new Point(cx / sceneW, cy / sceneH), new Point(R / sceneW, R / sceneH));
                effect.BeginAnimation(LandableWorldShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                var surface = new System.Windows.Shapes.Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect };
                { var ssHost = Supersample(surface); Canvas.SetLeft(ssHost, sceneX); Canvas.SetTop(ssHost, sceneY);
                planetPanelCanvas.Children.Add(ssHost); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Landable world shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        // GPU thick-atmosphere world (non-landable HMC): one Rectangle carrying AtmoWorldShaderEffect
        // draws the lit sphere, haze, clouds, cyclones and caps and animates itself from Time.
        // Returns false (caller uses the CPU terrain scene) when the body isn't one of those, on
        // software-only rendering, or on any setup failure.
        private bool TryAddAtmoWorldScene(BodyScanDetail detail, string iconCode, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if (!PlanetRenderer.IsAtmoWorld(detail, iconCode)) return false;
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                var look = PlanetRenderer.GetAtmoWorldLook(detail, iconCode);
                var (cx, cy, R) = PlanetRenderer.GetTerrainGeometry(sceneW, sceneH, detail);
                var effect = AtmoWorldShaderEffect.Create(look,
                    new Point(cx / sceneW, cy / sceneH), new Point(R / sceneW, R / sceneH));
                effect.BeginAnimation(AtmoWorldShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                var surface = new System.Windows.Shapes.Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect };
                { var ssHost = Supersample(surface); Canvas.SetLeft(ssHost, sceneX); Canvas.SetTop(ssHost, sceneY);
                planetPanelCanvas.Children.Add(ssHost); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Atmosphere world shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        private void RenderTerrainPanel(BodyScanDetail detail, string iconCode)
        {
            bool hasBio = detail.BioSignalCount > 0, hasGeo = detail.GeoSignalCount > 0;
            bool hasMining = detail.MiningSignalCount > 0;

            var ringHotspots = new List<RingHotspotSignal>();
            if (_watcher != null)
                foreach (var ring in detail.Rings)
                    if (_watcher.RingHotspots.TryGetValue(ring.Name, out var spots))
                        ringHotspots.AddRange(spots);
            int ringHotspotTotal = ringHotspots.Sum(s => s.Count);

            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;
            if (!TryAddAtmoWorldScene(detail, iconCode, sceneW, sceneH, sceneX, sceneY) &&
                !TryAddLandableWorldScene(detail, iconCode, sceneW, sceneH, sceneX, sceneY))
            {
                var frame = PlanetRenderer.GetTerrainSceneFrame(detail, iconCode, sceneW, sceneH, _watcher?.SystemPopulation ?? 0);
                var img = new Image { Width = sceneW, Height = sceneH, Source = frame };
                Canvas.SetLeft(img, sceneX); Canvas.SetTop(img, sceneY);
                planetPanelCanvas.Children.Add(img);
            }

            // Badges anchor to the SPHERE's own actual center/radius, not a fixed offset from
            // the scene box's corner — this scene's sphere doesn't fill nearly as much of its
            // box as the gas-giant scene's does (no rings needing the extra room), so a
            // corner-relative badge floated well away from the visible planet instead of
            // sitting on its edge. Geo sits bottom-left and Mining bottom-right (mirrored
            // diagonal corners, matching the legacy flat-icon renderer's own "mirrored across
            // the planet" convention). Bio used to share Geo's bottom-left corner, but that put
            // it on the opposite side of the sphere from its own BIO SIGNALS stat, which lives
            // in the right-hand column alongside Gravity/Atmosphere/Mining — moved to the
            // sphere's right edge, above Mining, so it sits on the same side as its label.
            var (sphereCx, sphereCy, sphereR) = PlanetRenderer.GetTerrainGeometry(sceneW, sceneH, detail);
            double badgeR = sphereR * 0.92;
            Point BadgePos(double angleDeg) => new Point(
                sceneX + sphereCx + Math.Cos(angleDeg * Math.PI / 180) * badgeR,
                sceneY + sphereCy + Math.Sin(angleDeg * Math.PI / 180) * badgeR);

            if (hasMining)
            {
                var p = BadgePos(45); // bottom-right
                planetPanelCanvas.Children.Add(MakeMiningBadge(p.X, p.Y, 28));
            }
            if (hasGeo)
            {
                var p = BadgePos(135); // bottom-left
                planetPanelCanvas.Children.Add(MakeGeoBadge(p.X, p.Y, 26));
            }
            if (hasBio)
            {
                var p = BadgePos(0); // right edge, mid-height — same side as the BIO SIGNALS stat
                planetPanelCanvas.Children.Add(MakeBioBadge(p.X, p.Y, 26));
            }
            if (ringHotspotTotal > 0)
            {
                var anchor = PlanetRenderer.GetRingMiningAnchor(detail, sceneW, sceneH);
                var p = anchor.HasValue
                    ? new Point(sceneX + anchor.Value.X, sceneY + anchor.Value.Y)
                    : BadgePos(-45); // top-right fallback — real ring geometry is rare on this body type
                planetPanelCanvas.Children.Add(MakeRingMiningBadge(p.X, p.Y, 26));
            }

            double leftX = 164, rightX = 548, rowY = 78;
            const double sectionGap = 34;

            double classBottomY = AddStackedStat(planetPanelCanvas, leftX, rowY, false, "PLANET CLASS", FormatTerrainType(iconCode), InfoLabelGreenBrush, sizeBump: 1.5);
            double terrainGravityG = detail.SurfaceGravity > 0 ? detail.SurfaceGravity / 9.80665 : 0;
            bool terrainHighGravity = IsHighGravity(terrainGravityG, detail.Landable);
            double gravBottomY = AddStackedStat(planetPanelCanvas, rightX, rowY, true, "GRAVITY",
                detail.SurfaceGravity > 0 ? $"{terrainGravityG:F2} G" : "—", InfoLabelGreenBrush, sizeBump: 1.5,
                valueBrush: terrainHighGravity ? InfoGravityWarnBrush : null,
                chipAbove: terrainHighGravity ? MakeSmallChip("HIGH GRAVITY", InfoGravityWarnBrush.Color) : null);
            // Unlike the gas-giant family's Sudarsky-class chip (always present), these traits
            // are conditional on the body's own real data — only bodies that actually have them
            // get a chip. Both can stack (a body can be both tidally locked and terraformable at
            // once); each new chip anchors off the real measured bottom of whatever came before
            // it, same convention as the gas-giant panel's own chip-under-value positioning.
            // Terraformable used to be a badge-on-sphere icon (the legacy flat-icon renderer's
            // overlay_terraformable.png) plus a separate bottom-of-panel text label — neither
            // carried over to this rework at all, and the icon's usual bottom-right spot is
            // exactly where the Mining badge now sits on this HUD. A gold chip matching Tidally
            // Locked's own style sidesteps that conflict entirely instead of hunting for a new
            // free corner on the sphere.
            if (detail.Landable)
            {
                // Same coral as the System Scan window's "landable" chip.
                var landableChip = MakeTraitChip("LANDABLE", leftX, classBottomY + 4, StatColWidth, rightAlign: false,
                    solidColor: Color.FromRgb(0xff, 0x9f, 0x6b), solidFill: Color.FromRgb(0x2a, 0x18, 0x0f));
                planetPanelCanvas.Children.Add(landableChip);
                classBottomY = classBottomY + 4 + landableChip.DesiredSize.Height;
            }
            if (detail.TidalLock)
            {
                var tidalChip = MakeTraitChip("TIDALLY LOCKED", leftX, classBottomY + 4, StatColWidth, rightAlign: false);
                planetPanelCanvas.Children.Add(tidalChip);
                classBottomY = classBottomY + 4 + tidalChip.DesiredSize.Height;
            }
            if (string.Equals(detail.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase))
            {
                // Same green + dark-green fill as the System Scan window's "terraformable" chip.
                var terraformChip = MakeTraitChip("TERRAFORMABLE", leftX, classBottomY + 4, StatColWidth, rightAlign: false,
                    solidColor: Color.FromRgb(0x7e, 0xe7, 0x87), solidFill: Color.FromRgb(0x12, 0x30, 0x18));
                planetPanelCanvas.Children.Add(terraformChip);
                classBottomY = classBottomY + 4 + terraformChip.DesiredSize.Height;
            }
            // Left and right columns now stack independently (a tall left item no longer pushes the right column down).
            double leftY = classBottomY + sectionGap, rightY = gravBottomY + sectionGap;

            double tempBottomY = AddStackedStat(planetPanelCanvas, leftX, leftY, false, "SURFACE TEMP", detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—", InfoLabelGreenBrush, sizeBump: 1.5);
            double atmoBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "ATMOSPHERE", FormatAtmosphere(detail.AtmosphereType), InfoLabelGreenBrush, sizeBump: 1.5);
            leftY = tempBottomY + sectionGap; rightY = atmoBottomY + sectionGap;

            // Geo Signals gets a real-Volcanism sub-line — a confirmed physical trait from the
            // orbital scan itself (same tier of information as Landable/TidalLock), not pinned
            // to the signals' own unknown locations, same as Ring Mining's materials sub-line
            // on the gas-giant panel.
            bool hasVolcanism = !string.IsNullOrWhiteSpace(detail.Volcanism)
                && !detail.Volcanism.Equals("No volcanism", StringComparison.OrdinalIgnoreCase);
            string? volcanismSub = hasVolcanism ? FormatVolcanism(detail.Volcanism) : null;
            double geoBottomY = AddStackedStat(planetPanelCanvas, leftX, leftY, false, "GEO SIGNALS", hasGeo ? $"{detail.GeoSignalCount} found" : "None", InfoOrangeBrush, volcanismSub, sizeBump: 1.5);
            double bioBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "BIO SIGNALS", hasBio ? $"{detail.BioSignalCount} found" : "None", InfoValueBrush, sizeBump: 1.5);
            leftY = geoBottomY + sectionGap; rightY = bioBottomY + sectionGap;

            double ringBottomY = leftY - sectionGap, miningBottomY = rightY - sectionGap;
            if (ringHotspotTotal > 0)
            {
                var materials = string.Join(", ", ringHotspots.OrderByDescending(h => h.Count).Select(h => $"{h.Material} ×{h.Count}"));
                ringBottomY = AddStackedStat(planetPanelCanvas, leftX, leftY, false, "RING MINING", $"{ringHotspotTotal} found", InfoRingMiningBrush, materials, sizeBump: 1.5);
            }
            if (hasMining)
                miningBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "MINING SITES", $"{detail.MiningSignalCount} found", InfoMiningBrush, sizeBump: 1.5);
            leftY = ringBottomY + sectionGap; rightY = miningBottomY + sectionGap;

            // Composition — the real Ice/Rock/Metal split. Previously gated on DSS mapping
            // (IsMapped) on the assumption it was a mapping-only reward, but the Scan journal
            // event already carries these fields on a plain FSS/Detailed scan — mapping adds no
            // new composition data, so gating on it just hid real data the app already had.
            // Right column so it doesn't collide with a real Ring Mining materials list on the
            // left when one exists — this was the unused block in the bottom-right a real
            // screenshot pointed out.
            double compTotal = detail.IceComposition + detail.RockComposition + detail.MetalComposition;
            if (compTotal > 0)
            {
                var compValue = $"Ice {detail.IceComposition * 100:F0}%\nRock {detail.RockComposition * 100:F0}%\nMetal {detail.MetalComposition * 100:F0}%";
                int before = planetPanelCanvas.Children.Count;
                double compBottomY = AddStackedStat(planetPanelCanvas, rightX, rightY, true, "COMPOSITION", compValue, InfoLabelGreenBrush, sizeBump: 1.5);
                // Real report: with Mining Sites (or other stacked rows) present, this 3-line
                // block ran down into the "MAPPED BY OTHER CMDR" discovery line at y≈544. Keep its
                // bottom clear of that line by lifting the whole block (label + value) if needed.
                const double compMaxBottom = 536;
                if (compBottomY > compMaxBottom)
                {
                    double lift = compBottomY - compMaxBottom;
                    for (int ci = before; ci < planetPanelCanvas.Children.Count; ci++)
                    {
                        var el = planetPanelCanvas.Children[ci];
                        Canvas.SetTop(el, Canvas.GetTop(el) - lift);
                    }
                }
            }

            var bottomLabel = EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "");
            planetPanelCanvas.Children.Add(MakeHudCenterLabel(bottomLabel.ToUpperInvariant(), 356.5, 500, InfoBrightValueBrush, 16.5));
            var scanTag = detail.IsMapped ? "MAPPED" : string.IsNullOrEmpty(detail.ScanType) ? "UNKNOWN" : detail.ScanType.ToUpperInvariant();
            var scanLabel = MakeHudCenterLabel($"SCAN: {scanTag}", 356.5, 522, InfoOrangeBrush, 14.5);
            scanLabel.FontWeight = FontWeights.Bold;
            planetPanelCanvas.Children.Add(scanLabel);
            AddDiscoveryLine(planetPanelCanvas, detail, 356.5, 544, 13);
        }

        // Asteroid Belt Cluster — real art built from two in-cockpit reference screenshots
        // (icy field, rocky field) instead of the old fixed "belt_N.png" (one of five pieces of
        // art with no connection to the actual body). A cluster's own Scan event carries almost
        // no data; the real composition/mining info lives on the parent RING's own entry (see
        // EliteWatcherService.GetBeltRingClass), so this pulls that in wherever it's known.
        // Shown on the Planet tab while a nav-panel SIGNAL (not a real body) is the current
        // target and there's no real position yet — see UpdateInfoPlanetPanel. detail is the
        // resolved parent body if known (already scanned), null if not (rare).
        private void RenderSignalTargetPanel(string signalLabel, BodyScanDetail? detail)
        {
            bool nonHuman = signalLabel.Contains("nonhuman", StringComparison.OrdinalIgnoreCase) || signalLabel.Contains("non human", StringComparison.OrdinalIgnoreCase);
            planetPanelCanvas.Children.Add(nonHuman ? MakeThargoidSensorIllustration(356.5, 256, 0.62) : MakeMiningSiteScanIllustration(356.5, 256, 0.62, detail?.MiningSignalCount ?? 0));
            planetPanelCanvas.Children.Add(MakeCenterLabel(signalLabel.ToUpperInvariant(), 356.5, 470, InfoValueBrush, 15));
            var sub = detail != null
                ? $"on {EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "").ToUpperInvariant()}"
                : "resolving target…";
            planetPanelCanvas.Children.Add(MakeCenterLabel(sub, 356.5, 494, InfoDimBrush, 12));
        }

        // Violet auger boring into broken ground — the same icon language as the other signal
        // badges (glow + accent color), but rendered as a full scene, not a small badge, since
        // this is the PRIMARY image for this state. cx/cy is the icon's own visual center
        // (roughly where the drill tip meets the ground); scale multiplies the ~120x150 local
        // coordinate space it's authored in.
        private static Canvas MakeMiningSignalIllustration(double cx, double cy, double scale)
        {
            var violet = InfoMiningBrush;
            var violetLight = new SolidColorBrush(Color.FromRgb(0xd8, 0xb8, 0xff));
            var rock = new SolidColorBrush(Color.FromRgb(0x3a, 0x4a, 0x52));

            var inner = new Canvas { Width = 120, Height = 150, IsHitTestVisible = false };

            // Glow behind the tip — same convention as MakeSignalBadge's own glow ring.
            var glow = new Ellipse
            {
                Width = 70, Height = 70, Fill = violet, Opacity = 0.35,
                Effect = new BlurEffect { Radius = 22 },
            };
            Canvas.SetLeft(glow, 25); Canvas.SetTop(glow, 45);
            inner.Children.Add(glow);

            // Scattered rock debris around the drill point.
            void Chunk(string points, Brush fill, double opacity = 1.0)
            {
                var p = new Polygon { Points = PointCollection.Parse(points), Fill = fill, Opacity = opacity };
                inner.Children.Add(p);
            }
            var chunkRing1 = new Polygon
            {
                Points = PointCollection.Parse("16,120 9,112 15,104 10,96 20,100 18,90 28,96 27,86 38,93 37,82 48,90"),
                Stroke = rock, StrokeThickness = 1.3, Fill = Brushes.Transparent, Opacity = 0.85,
            };
            inner.Children.Add(chunkRing1);
            var chunkRing2 = new Polygon
            {
                Points = PointCollection.Parse("104,120 111,112 105,104 110,96 100,100 102,90 92,96 93,86 82,93 83,82 72,90"),
                Stroke = rock, StrokeThickness = 1.3, Fill = Brushes.Transparent, Opacity = 0.85,
            };
            inner.Children.Add(chunkRing2);
            Chunk("20,128 26,121 32,126 29,132", rock);
            Chunk("88,131 94,125 100,129 96,136", rock);
            Chunk("34,134 39,128 45,133 41,139", violet, 0.85);
            Chunk("76,136 81,130 87,134 83,141", rock);

            // Shaft + collar.
            var shaft = new Rectangle { Width = 8, Height = 42, Fill = new SolidColorBrush(Color.FromRgb(0x8a, 0x9a, 0xa5)) };
            Canvas.SetLeft(shaft, 56); Canvas.SetTop(shaft, 6);
            inner.Children.Add(shaft);
            var collar = new Rectangle { Width = 24, Height = 7, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.FromRgb(0xa9, 0xb7, 0xbf)) };
            Canvas.SetLeft(collar, 48); Canvas.SetTop(collar, 46);
            inner.Children.Add(collar);

            // Tapered auger tip — same path used for both the fill and the clip that bounds
            // the diagonal flute stripes to its silhouette.
            const string tipPath = "M44,56 Q44,50 50,50 L70,50 Q76,50 76,56 L76,78 Q76,94 60,112 Q44,94 44,78 Z";
            var tip = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(tipPath), Fill = new SolidColorBrush(Color.FromRgb(0x15, 0x0c, 0x26)),
                Stroke = violet, StrokeThickness = 1.6,
            };
            inner.Children.Add(tip);

            // Diagonal flute stripes, clipped to the tip's silhouette. WPF's RenderTransform
            // rotates around a center given in the ELEMENT'S OWN local coordinates (0,0 = the
            // rectangle's own top-left), unlike SVG's rotate(angle,cx,cy) which uses the same
            // absolute canvas coordinates as the shape's x/y — so the rotation center here is
            // (targetAbsoluteX - Left, targetAbsoluteY - Top) = (60-20, 3) for every stripe,
            // not the SVG mockup's own (60, <stripe's absolute y>) values.
            var flutes = new Canvas { Clip = Geometry.Parse(tipPath) };
            for (int i = 0; i < 5; i++)
            {
                double y = 55 + i * 12;
                var stripe = new Rectangle
                {
                    Width = 90, Height = 6, Fill = violet,
                    RenderTransform = new RotateTransform(-18, 40, 3),
                };
                Canvas.SetLeft(stripe, 20); Canvas.SetTop(stripe, y - 3);
                flutes.Children.Add(stripe);
            }
            inner.Children.Add(flutes);

            // Contact point + short radiating lines where the drill meets the ground.
            var contact = new Ellipse { Width = 6, Height = 6, Fill = violetLight };
            Canvas.SetLeft(contact, 57); Canvas.SetTop(contact, 109);
            inner.Children.Add(contact);
            void Spark(double x2, double y2, double opacity)
            {
                inner.Children.Add(new Line { X1 = 60, Y1 = 112, X2 = x2, Y2 = y2, Stroke = violetLight, StrokeThickness = 1, Opacity = opacity });
            }
            Spark(46, 122, 0.7); Spark(74, 122, 0.7); Spark(60, 128, 0.55);

            inner.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(scale, scale), new TranslateTransform(cx - 60 * scale, cy - 75 * scale) },
            };
            var outer = new Canvas { IsHitTestVisible = false };
            outer.Children.Add(inner);
            return outer;
        }

        private void RenderAsteroidBeltPanel(BodyScanDetail detail)
        {
            const int sceneW = 370, sceneH = 420, sceneX = 171, sceneY = 40;
            string? rawRingClass = _watcher?.GetBeltRingClass(detail.BodyName);
            FrameworkElement field;
            try { field = AsteroidFieldRenderer.CreateAnimatedField(detail.BodyName, rawRingClass, sceneW, sceneH); }
            catch (Exception ex)
            {
                Log.Write($"Animated asteroid field failed, using static frame: {ex.Message}");
                field = new Image { Width = sceneW, Height = sceneH, Source = AsteroidFieldRenderer.GetAsteroidFieldFrame(detail.BodyName, rawRingClass, sceneW, sceneH) };
            }
            Canvas.SetLeft(field, sceneX); Canvas.SetTop(field, sceneY);
            planetPanelCanvas.Children.Add(field);

            double leftX = 164, rightX = 548, rowY = 78;
            const double sectionGap = 34;

            double classBottomY = AddStackedStat(planetPanelCanvas, leftX, rowY, false, "BODY TYPE", "Asteroid Belt", InfoLabelGreenBrush, sizeBump: 1.5);
            double compBottomY = rawRingClass != null
                ? AddStackedStat(planetPanelCanvas, rightX, rowY, true, "COMPOSITION", FormatRingClass(rawRingClass), InfoLabelGreenBrush, sizeBump: 1.5)
                : AddStackedStat(planetPanelCanvas, rightX, rowY, true, "COMPOSITION", "Unknown", InfoDimBrush, "Scan the ring to reveal", sizeBump: 1.5);
            rowY = Math.Max(classBottomY, compBottomY) + sectionGap;

            var ringHotspots = new List<RingHotspotSignal>();
            if (_watcher != null)
            {
                var ringName = EliteWatcherService.GetBeltRingName(detail.BodyName);
                if (_watcher.RingHotspots.TryGetValue(ringName, out var spots)) ringHotspots.AddRange(spots);
            }
            int ringHotspotTotal = ringHotspots.Sum(s => s.Count);
            if (ringHotspotTotal > 0)
            {
                var materials = string.Join(", ", ringHotspots.OrderByDescending(h => h.Count).Select(h => $"{h.Material} ×{h.Count}"));
                AddStackedStat(planetPanelCanvas, leftX, rowY, false, "RING MINING", $"{ringHotspotTotal} found", InfoRingMiningBrush, materials, sizeBump: 1.5);
            }
            if (detail.SemiMajorAxis > 0)
            {
                // AU is the real journal unit here (SemiMajorAxis for a belt cluster is its
                // distance from the ring's own focus, not from the star) — shown in Ls to match
                // the convention the rest of the app uses for in-system distances.
                double ls = detail.SemiMajorAxis / 299792458.0;
                AddStackedStat(planetPanelCanvas, rightX, rowY, true, "DISTANCE", ls >= 1 ? $"{ls:N0} Ls" : $"{detail.SemiMajorAxis:N0} m", InfoLabelGreenBrush, sizeBump: 1.5);
            }

            var beltBottomLabel = EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "");
            planetPanelCanvas.Children.Add(MakeHudCenterLabel(beltBottomLabel.ToUpperInvariant(), 356.5, 500, InfoBrightValueBrush, 16.5));
            planetPanelCanvas.Children.Add(MakeHudCenterLabel("ASTEROID BELT", 356.5, 522, InfoOrangeBrush, 14.5));
        }

        private static string FormatTerrainType(string iconCode) => iconCode switch
        {
            "HMC" => "High Metal Content",
            "ICY" => "Icy Body",
            "RBD" => "Rocky Body",
            "RIB" => "Rocky Ice Body",
            "WTR" => "Water World",
            "MRB" => "Metal Rich Body",
            "ELW" => "Earthlike World",
            "AMW" => "Ammonia World",
            _ => "Unknown",
        };

        // The actual mockup font, embedded — JetBrains Mono isn't installed on most machines
        // (confirmed absent here too), so rather than assume/fall back to something close
        // (Cascadia Mono), the Regular/Medium/Bold TTFs are bundled as app resources and
        // referenced by pack URI, the same way the WPF docs describe embedding a font that
        // must render identically regardless of what's installed on the user's system.
        // Scoped to just the gas-giant stat text/trait chip for now — baby steps toward using
        // it app-wide later, not a blanket swap in this pass.
        private static readonly FontFamily GasGiantFont =
            new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono");
        // 114→160 — the columns now get all of the canvas's 620→713 (15%) widening, verified
        // by measuring the actual embedded font: even the widest label ("SURFACE TEMP") and
        // trait chip ("AMMONIA-BASED LIFE") still fit on one line with real margin to spare.
        private const double StatColWidth = 160;

        // Approximates CSS letter-spacing for the gas-giant panel's uppercase bold labels —
        // classic WPF's TextBlock has no letter-spacing/character-spacing property at all, so
        // a thin space (U+2009) between letters and a wider en space (U+2002) between words is
        // the standard WPF workaround for that tracked-out "tech" look the mockup has.
        private static string Track(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c == ' ') { sb.Append(' '); continue; }
                sb.Append(c).Append(' ');
            }
            return sb.ToString();
        }

        // One stacked label/value pair, no leader line — the locked art-direction HUD for the
        // gas-giant family. rightAlign puts the text flush against x on its left edge (for the
        // right-hand column); otherwise it's flush against x on its right edge. Returns the Y
        // just past whatever was actually drawn (measured, not guessed at a fixed line count)
        // so a caller — the trait chip below Planet Class — can sit against it precisely
        // instead of overlapping or leaving a gap depending on how many lines things wrapped to.
        // canvas param added when the Star tab picked up this same no-leader-line HUD — the
        // gas-giant/terrain call sites below now pass planetPanelCanvas explicitly instead of
        // this closing over it, so the star panel can pass starPanelCanvas instead.
        // sizeBump lets one caller (the Star tab, so far) run its whole stat block a bit larger
        // than the gas-giant/terrain baseline without touching that baseline — the Star tab's
        // own labels ("CLASS", "SOLAR MASS", "RADIUS (SOL)"...) are all shorter than the
        // Planet tab's worst case ("SURFACE TEMP"), which already had real headroom in the
        // 160px column at the base size, so there's room to spare here specifically. Defaults
        // to 0 so every existing gas-giant/terrain call site is completely unaffected.
        // True once the body's real gravity (already converted to G) meets or exceeds the
        // player's own configured warning threshold — off entirely unless they've opted in via
        // Settings, since "dangerous" gravity is a matter of ship/SRV loadout and playstyle this
        // app has no way to know. Landable-only per feedback: gravity only actually matters if
        // you can set down on the body — this also naturally excludes every gas giant (never
        // landable) without needing its own separate check.
        private bool IsHighGravity(double gravityG, bool landable) =>
            _gravityWarningEnabled && landable && gravityG >= _gravityWarningThresholdG;

        // Small unpositioned chip for AddStackedStat's chipAbove slot — unlike MakeTraitChip,
        // this doesn't set its own Canvas position; AddStackedStat places it using the same
        // column x/rightAlign math as the key/value text it sits between.
        private static Border MakeSmallChip(string text, Color color) => new Border
        {
            Padding = new Thickness(4, 2, 4, 2),
            Background = new SolidColorBrush(Color.FromArgb(0x28, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = Track(text), Foreground = new SolidColorBrush(color), FontFamily = GasGiantFont,
                FontSize = 9.5, FontWeight = FontWeights.Bold,
            },
        };

        private double AddStackedStat(Canvas canvas, double x, double y, bool rightAlign, string key, string value, Brush keyBrush, string? sub = null, double sizeBump = 0, Brush? valueBrush = null, UIElement? chipAbove = null)
        {
            var keyTb = new TextBlock
            {
                // Mockup's uppercase bold labels used letter-spacing: .08em for that tracked-out
                // "tech" look — classic WPF's TextBlock has no letter-spacing property at all
                // (that only exists on WinUI/UWP's TextBlock, a different type entirely), so
                // Track() approximates it the standard WPF way: inserting a thin space between
                // each letter. Never had this before; the app's own labels rendered with the
                // plain, untracked text the whole time.
                Text = Track(key), Foreground = keyBrush, FontFamily = GasGiantFont,
                // 10→11.5 — the wider column (StatColWidth 114→160) measured real room to
                // spare even at the worst-case label ("SURFACE TEMP", ~118.6px vs 160px).
                FontSize = 11.5 + sizeBump, FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, Width = StatColWidth,
                TextAlignment = rightAlign ? TextAlignment.Left : TextAlignment.Right,
            };
            keyTb.Measure(new Size(StatColWidth, double.PositiveInfinity));

            var valTb = new TextBlock
            {
                // 13.5→15 — same reasoning; even the widest measured value ("Water Giant")
                // fits with room to spare well past 16pt.
                Text = value, Foreground = valueBrush ?? InfoBrightValueBrush, FontFamily = GasGiantFont,
                FontSize = 15 + sizeBump, Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap, Width = StatColWidth,
                TextAlignment = rightAlign ? TextAlignment.Left : TextAlignment.Right,
            };
            double keyX = rightAlign ? x : x - StatColWidth;
            double valX = keyX;
            double valY = y + keyTb.DesiredSize.Height + 3;
            Canvas.SetLeft(keyTb, keyX); Canvas.SetTop(keyTb, y);
            canvas.Children.Add(keyTb);

            // Chip sits between the label and the value (per feedback — the trait chips
            // elsewhere on this HUD go BELOW their value, but a warning needs to read before
            // the number that triggered it, not after).
            if (chipAbove != null)
            {
                Canvas.SetLeft(chipAbove, keyX); Canvas.SetTop(chipAbove, valY);
                canvas.Children.Add(chipAbove);
                chipAbove.Measure(new Size(StatColWidth, double.PositiveInfinity));
                valY += chipAbove.DesiredSize.Height + 3;
            }

            Canvas.SetLeft(valTb, valX); Canvas.SetTop(valTb, valY);
            canvas.Children.Add(valTb);

            valTb.Measure(new Size(StatColWidth, double.PositiveInfinity));
            double bottomY = valY + valTb.DesiredSize.Height + 2;
            if (sub != null)
            {
                var subTb = new TextBlock
                {
                    // 8.5→10.5→12 — same wider-column headroom as the label/value above.
                    Text = sub, Foreground = InfoDimBrush, FontFamily = GasGiantFont,
                    FontSize = 12 + sizeBump, Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap, Width = StatColWidth,
                    TextAlignment = rightAlign ? TextAlignment.Left : TextAlignment.Right,
                };
                Canvas.SetLeft(subTb, valX); Canvas.SetTop(subTb, bottomY);
                canvas.Children.Add(subTb);
                subTb.Measure(new Size(StatColWidth, double.PositiveInfinity));
                bottomY += subTb.DesiredSize.Height + 2;
            }
            return bottomY;
        }

        private void UpdateDestinationPanel(bool force = false)
        {
            var dest = _watcher?.CurrentDestination;
            destSummaryStack.Children.Clear();
            destHopStack.Children.Clear();

            if (dest == null || string.IsNullOrEmpty(dest.NextSystem))
            {
                destSummaryStack.Children.Add(new TextBlock
                {
                    Text = AppFonts.TrackLight("NO ROUTE ACTIVE"), Foreground = InfoDimBrush,
                    FontFamily = AppFonts.Mono, FontSize = 13
                });
                return;
            }

            int hopIndex = dest.TotalRouteJumps > 0 ? Math.Max(1, dest.TotalRouteJumps - dest.RemainingJumpsInRoute) : 0;

            var headerRow = new DockPanel();
            if (hopIndex > 0 && dest.TotalRouteJumps > 0)
            {
                var hopTb = new TextBlock
                {
                    Text = AppFonts.TrackLight($"HOP {hopIndex} / {dest.TotalRouteJumps}"), Foreground = InfoDimBrush,
                    FontFamily = AppFonts.Mono, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(hopTb, Dock.Right);
                headerRow.Children.Add(hopTb);
            }
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = AppFonts.TrackLight(dest.NextSystem.ToUpperInvariant()), Foreground = InfoValueBrush,
                FontFamily = AppFonts.Mono, FontSize = 20, FontWeight = FontWeights.Bold
            });
            if (!string.IsNullOrEmpty(dest.StarClass))
            {
                titlePanel.Children.Add(new Border
                {
                    BorderBrush = InfoLeaderBrush, BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = AppFonts.TrackLight($"{dest.StarClass}-CLASS"), Foreground = InfoOrangeBrush,
                        FontFamily = AppFonts.Mono, FontSize = 11
                    }
                });
            }
            headerRow.Children.Add(titlePanel);
            destSummaryStack.Children.Add(headerRow);

            var statsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 4) };
            void AddStat(string label, string value, Brush? brush = null)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 14, 10) };
                var bar = new Border { Background = InfoLeaderBrush, Width = 3, Margin = new Thickness(0, 1, 8, 1) };
                DockPanel.SetDock(bar, Dock.Left);
                row.Children.Add(bar);
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = AppFonts.TrackLight(label), Foreground = InfoDimBrush, FontFamily = AppFonts.Mono, FontSize = 9.5 });
                sp.Children.Add(new TextBlock { Text = AppFonts.TrackLight(value), Foreground = brush ?? InfoValueBrush, FontFamily = AppFonts.Mono, FontSize = 17, Margin = new Thickness(0, 3, 0, 0) });
                row.Children.Add(sp);
                statsGrid.Children.Add(row);
            }
            var nextHop = dest.Hops.FirstOrDefault(h => string.Equals(h.StarSystem, dest.NextSystem, StringComparison.OrdinalIgnoreCase));
            double jumpRange = dest.CurrentJumpRange > 0 ? dest.CurrentJumpRange : dest.MaxJumpRange;
            AddStat("JUMP RANGE", jumpRange > 0 ? $"{jumpRange:F1} ly" : "—");
            AddStat("NEXT JUMP DIST", nextHop != null && nextHop.DistanceFromPrevLy > 0 ? $"{nextHop.DistanceFromPrevLy:F1} ly" : "—");
            AddStat("FUEL LEVEL", $"{dest.FuelMain:F1} / {dest.FuelCapacityMain:F1} t");
            AddStat("REMAINING DIST", dest.RemainingDistanceLy > 0 ? $"{dest.RemainingDistanceLy:F1} ly" : "—");
            AddStat("TOTAL ROUTE", dest.TotalRouteLy > 0 ? $"{dest.TotalRouteLy:F1} ly" : "—");
            AddStat("JUMPS LEFT", dest.RemainingJumpsInRoute > 0 ? dest.RemainingJumpsInRoute.ToString() : "—", InfoOrangeBrush);
            destSummaryStack.Children.Add(statsGrid);

            if (dest.TotalRouteJumps > 0)
            {
                double pct = Math.Clamp(100.0 * hopIndex / dest.TotalRouteJumps, 0, 100);
                var progressRow = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var pctTb = new TextBlock
                {
                    Text = AppFonts.TrackLight($"{pct:F0}%"), Foreground = InfoValueBrush,
                    FontFamily = AppFonts.Mono, FontSize = 10.5
                };
                DockPanel.SetDock(pctTb, Dock.Right);
                progressRow.Children.Add(pctTb);
                progressRow.Children.Add(new TextBlock
                {
                    Text = AppFonts.TrackLight("ROUTE PROGRESS"), Foreground = InfoDimBrush,
                    FontFamily = AppFonts.Mono, FontSize = 10.5
                });
                destSummaryStack.Children.Add(progressRow);

                var barHost = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 12) };
                barHost.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0d, 0x1a, 0x1a)),
                    CornerRadius = new CornerRadius(4)
                });
                var barOverlay = new Grid();
                barOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 0.01), GridUnitType.Star) });
                barOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 0.01), GridUnitType.Star) });
                var barFill = new Border { Background = InfoValueBrush, CornerRadius = new CornerRadius(4) };
                Grid.SetColumn(barFill, 0);
                barOverlay.Children.Add(barFill);
                barHost.Children.Add(barOverlay);
                destSummaryStack.Children.Add(barHost);
            }

            // Full route including already-passed hops (from the persisted route cache) so the
            // list stays complete and scrollable across the whole journey — falls back to the
            // remaining-only view if the cache hasn't populated yet (e.g. very first tick).
            var fullRoute = dest.FullRouteHops.Count > 0 ? dest.FullRouteHops : dest.Hops;
            // 0-based index of "where we are right now" — everything before this is history.
            // hopIndex counts the whole journey (TotalRouteJumps includes hops flown in EARLIER
            // legs before a mid-route re-plot), but the list below only holds the CURRENT leg — so
            // the carried-over hops have to come off before using it as a list position. Without
            // that, a route re-plotted after 3 completed hops dimmed the next three upcoming rows
            // as if they'd already been passed (real report: rows 8 and 9 grayed out ahead of the
            // "next" row 7).
            int carriedHops = dest.FullRouteHops.Count > 0 ? Math.Max(0, dest.TotalRouteJumps - dest.FullRouteHops.Count) : 0;
            int hereIndex = hopIndex > 0 ? Math.Max(-1, hopIndex - 1 - carriedHops) : -1;

            Border? currentRow = null;
            // Fallback scroll target for when no row matches NextSystem at all (e.g. NextSystem
            // was retargeted to a system outside the cached route) — the actual current-position
            // row, so the view still moves to reflect where we are instead of doing nothing.
            Border? hereRow = null;
            for (int i = 0; i < fullRoute.Count; i++)
            {
                var hop = fullRoute[i];
                bool isNext = string.Equals(hop.StarSystem, dest.NextSystem, StringComparison.OrdinalIgnoreCase);
                bool isPastOrHere = hereIndex >= 0 && i <= hereIndex;

                var row = new Border
                {
                    Background = isNext ? new SolidColorBrush(Color.FromRgb(0x0d, 0x22, 0x22)) : new SolidColorBrush(Color.FromRgb(0x0d, 0x1a, 0x1a)),
                    BorderBrush = isNext ? InfoValueBrush : InfoLeaderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 7, 8, 7),
                    Margin = new Thickness(0, 0, 0, 6),
                    Opacity = isPastOrHere && !isNext ? 0.45 : 1.0,
                };
                var rowPanel = new DockPanel();
                var idxTb = new TextBlock { Text = (i + 1).ToString(), Foreground = isNext ? InfoValueBrush : InfoDimBrush, Width = 26, FontFamily = AppFonts.Mono, FontSize = 13 };
                DockPanel.SetDock(idxTb, Dock.Left);
                var distTb = new TextBlock { Text = AppFonts.TrackMinimal(hop.DistanceFromPrevLy > 0 ? $"{hop.DistanceFromPrevLy:F1} ly" : "—"), Foreground = InfoDimBrush, FontFamily = AppFonts.Mono, FontSize = 11.5, Width = 68, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(distTb, Dock.Right);
                var clsTb = new TextBlock { Text = AppFonts.TrackMinimal(hop.StarClass), Foreground = InfoOrangeBrush, FontFamily = AppFonts.Mono, FontSize = 12, Width = 34, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(clsTb, Dock.Right);
                var sysTb = new TextBlock { Text = AppFonts.TrackMinimal(hop.StarSystem.ToUpperInvariant()), Foreground = isNext ? InfoValueBrush : new SolidColorBrush(Color.FromRgb(0x88, 0xbb, 0xbb)), FontFamily = AppFonts.Mono, FontSize = 13 };

                rowPanel.Children.Add(idxTb);
                rowPanel.Children.Add(distTb);
                rowPanel.Children.Add(clsTb);
                if (IsScoopableStar(hop.StarClass))
                {
                    // Drawn instead of an emoji glyph — color emoji ignore Foreground entirely,
                    // rendering as a barely-visible dark pump icon against this dark background.
                    var scoopIcon = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M5,0 C5,0 0,6.4 0,9 A5,5 0 1,0 10,9 C10,6.4 5,0 5,0 Z"),
                        Fill = InfoValueBrush,
                        Width = 10, Height = 12, Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
                    };
                    DockPanel.SetDock(scoopIcon, Dock.Right);
                    rowPanel.Children.Add(scoopIcon);
                }
                rowPanel.Children.Add(sysTb);
                row.Child = rowPanel;
                destHopStack.Children.Add(row);
                if (isNext) currentRow = row;
                if (i == hereIndex) hereRow = row;
            }

            // Scroll to the next-jump row when the tab is freshly shown, or when progress has
            // actually advanced to a new hop — not on every routine refresh tick, which would
            // otherwise fight a manual scroll while sitting on the tab looking at older hops.
            // BringIntoView() only scrolls the minimum distance needed, which tends to leave the
            // next-hop row sitting right at the bottom edge of the viewport — technically
            // "visible" but with no context around it. Instead, position it a row's-height below
            // the top of the viewport so the just-passed hop stays visible above it, and a couple
            // more upcoming hops show below (however many fit in the remaining viewport height).
            //
            // "Advanced" is judged by hereIndex (route position), not dest.NextSystem's name — see
            // _lastScrolledHereIndex's doc comment for why the name races ahead unreliably. Falls
            // back to hereRow when nothing matches NextSystem at all (e.g. retargeted off-route).
            var scrollTarget = currentRow ?? hereRow;
            bool advanced = hereIndex != _lastScrolledHereIndex;
            if ((force || advanced) && scrollTarget != null)
            {
                _lastScrolledHereIndex = hereIndex;
                destHopStack.UpdateLayout();
                double rowTop = scrollTarget.TranslatePoint(new Point(0, 0), destHopStack).Y;
                double contextRowHeight = scrollTarget.ActualHeight + scrollTarget.Margin.Bottom;
                destHopScroll.ScrollToVerticalOffset(Math.Max(0, rowTop - contextRowHeight));
            }
        }

        // ---------------------------------------------------------------
        //  DEORBIT — "Depth Charge": a receding tunnel of gates, temporarily replacing the
        //  RADAR tab for the duration of a glide (see EliteStatus.IsGliding). Deliberately
        //  simpler than the original concept mockup: real Status.json has no ship attitude
        //  (pitch/bank) at all, so there's no genuine "off course" signal to fake a left/right
        //  drift from — every readout here is real data instead (altitude, gravity,
        //  atmosphere, a derived vertical speed), and gravity alone shapes the tunnel's
        //  geometry (steeper/tighter for a heavy world), same as the "Depth Charge" pitch.
        // ---------------------------------------------------------------
        private void AddHudStat(Canvas canvas, double x, double y, bool alignRight, string label, string value, Brush valueBrush)
        {
            // InfoValueBrush (bright cyan), not InfoDimBrush — matches how AddCallout's key
            // labels read elsewhere in the app (STAR/PLANET tabs); dim grey was hard to read.
            var labelTb = new TextBlock
            {
                Text = label, Foreground = InfoValueBrush, FontFamily = new FontFamily("Consolas"),
                FontSize = 14, FontWeight = FontWeights.Bold,
            };
            labelTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var valueTb = new TextBlock
            {
                Text = value, Foreground = valueBrush, FontFamily = new FontFamily("Consolas"),
                FontSize = 19, FontWeight = FontWeights.Bold,
            };
            valueTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lx = alignRight ? x - labelTb.DesiredSize.Width : x;
            double vx = alignRight ? x - valueTb.DesiredSize.Width : x;
            Canvas.SetLeft(labelTb, lx); Canvas.SetTop(labelTb, y);
            Canvas.SetLeft(valueTb, vx); Canvas.SetTop(valueTb, y + 19);
            canvas.Children.Add(labelTb);
            canvas.Children.Add(valueTb);
        }

        private void ResetDeorbitState()
        {
            _deorbitLastTick = DateTime.MinValue;
            _deorbitLastAltitude = null;
            _deorbitLastLat = null;
            _deorbitLastLon = null;
            _deorbitLastDataTick = DateTime.MinValue;
            _deorbitHaveRealSpeed = false;
            _deorbitCachedSpeed = 2500;
            _deorbitCachedVs = 0;
            _deorbitProgress = null;
            _deorbitStartAltitude = 0;
            _deorbitClimbSince = DateTime.MinValue;
            ResetAscentState();
            // Deliberately NOT cleared here — see UpdateDeorbitPanel's own body-name check for
            // why. Real report: the planet still jumped ~5% bigger partway through a descent
            // even with the "lock once" fix, because ResetDeorbitState runs on EVERY mode
            // transition where either side is Deorbit (see RefreshAll) — including entering it,
            // not just leaving. A brief mode flicker mid-glide (ShouldShowDeorbit's own condition
            // can overlap briefly with Radar's near the tail end of an approach, per its own
            // comment) fires this on the way out AND the way back in, clearing the lock both
            // times — so "lock once per descent" was actually re-arming itself every time the
            // mode blinked, same as if the lock never existed. The body-name check in
            // UpdateDeorbitPanel is what actually decides "is this still the same descent" now;
            // clearing the lock here too was actively working against that.
        }

        private void UpdateDeorbitPanel(bool force = false)
        {
            // Defaults to a blank status (glide/supercruise both false) when the watcher isn't
            // up yet, so "showing" below still correctly comes out false — status is non-null
            // from here on, no repeated null-forgiving needed.
            var status = _watcher?.CurrentStatus ?? new EliteStatus();
            // Same trigger ComputeMode used to decide this mode in the first place — see
            // ShouldShowDeorbit's comment for why this needs to be one shared method.
            var bodyDetail = _watcher?.GetKnownBodyDetail(status.BodyName);
            bool showing = ShouldShowDeorbit(status);
            if (!showing)
            {
                // Mode is switching away this same tick — nothing to draw, and resetting the
                // clock/position memory means the next approach starts its animation cleanly.
                ResetDeorbitState();
                return;
            }

            // A genuine body change (a real new target, not a data blip) clears any stale lock
            // from the previous approach before it could ever apply to this one.
            if (!string.Equals(status.BodyName, _deorbitLockedBodyName, StringComparison.OrdinalIgnoreCase))
            {
                _deorbitLockedBodyName = status.BodyName ?? "";
                _deorbitLockedDetail = null;
            }
            {
                // Real report: the planet still visibly jumped larger partway through a descent
                // even with the lock in place — because the lock only re-armed itself EVERY
                // frame a fresh lookup succeeded, so it never actually stopped anything; it just
                // meant "use the latest successful lookup," same as before. A real report + "like
                // it was redrawn" pointed at the actual mechanism: the render's own geometry
                // depends on whether the body HAS RINGS (a smaller sphere fraction plus the ring
                // band when it does, a plain larger sphere when it doesn't — see
                // GetTerrainGeometry). Ring data lives on the body's OWN scan detail, which can
                // legitimately update mid-descent (a fuller/later copy of the same body replacing
                // an earlier, incomplete one in _bodyScanDetails) even though the body itself
                // never changed — and re-fetching fresh every single frame meant the render
                // would faithfully switch geometry the instant that happened, mid-glide. Locking
                // ONCE — first successful resolution wins for the rest of THIS descent, never
                // re-armed after — makes the whole scene fully static once it starts, which is
                // what actually stops the jump: nothing about the render can change again until
                // the next approach.
                if (_deorbitLockedDetail == null)
                {
                    var freshIconCode = bodyDetail != null ? MapPlanetClassToIconCode(bodyDetail.PlanetClass) : null;
                    bool freshIsRealRender = bodyDetail != null && freshIconCode != null && PlanetRenderer.IsTerrainFamily(freshIconCode);
                    if (freshIsRealRender)
                    {
                        _deorbitLockedDetail = bodyDetail;
                        Log.Write($"Deorbit: planet render locked for '{status.BodyName}' — rings={bodyDetail!.Rings.Count} pressure={bodyDetail.SurfacePressure:F0}");
                    }
                }
                else
                {
                    bodyDetail = _deorbitLockedDetail;
                }
            }

            var now = DateTime.UtcNow;
            double dt = _deorbitLastTick == DateTime.MinValue ? 0 : Math.Min((now - _deorbitLastTick).TotalSeconds, 0.5);
            _deorbitLastTick = now;
            // Constant scroll rate — represents the glide's locked ~2,500 m/s ground speed,
            // not anything computed from deviation (see the wander sim below, which is purely
            // cosmetic and never touches this).
            _deorbitScroll = (_deorbitScroll + dt * 0.55) % 1.0;

            // Status.json only actually changes a few times a second — far slower than this
            // 60fps repaint — so speed/vertical-speed are only recomputed when the underlying
            // altitude/position values have genuinely moved, using the real elapsed time since
            // that last genuine change (not this animation frame's ~16ms). Computing it every
            // frame against the last frame produced near-zero readings almost always (nothing
            // had changed yet) with an occasional huge spike (a whole data interval's worth of
            // movement divided by one frame's tiny dt) on the frame it did change.
            bool dataChanged = !_deorbitLastAltitude.HasValue ||
                status.Altitude != _deorbitLastAltitude.Value ||
                status.Latitude != _deorbitLastLat || status.Longitude != _deorbitLastLon;
            if (dataChanged)
            {
                if (_deorbitLastDataTick != DateTime.MinValue)
                {
                    double dataDt = (now - _deorbitLastDataTick).TotalSeconds;
                    if (dataDt > 0.02)
                    {
                        double vsNow = _deorbitLastAltitude.HasValue ? (status.Altitude - _deorbitLastAltitude.Value) / dataDt : 0;
                        double horizSpeed = (_deorbitLastLat.HasValue && _deorbitLastLon.HasValue)
                            ? EliteWatcherService.DistanceMeters(_deorbitLastLat.Value, _deorbitLastLon.Value,
                                status.Latitude, status.Longitude, status.PlanetRadius) / dataDt
                            : 0;
                        _deorbitCachedVs = vsNow;
                        _deorbitCachedSpeed = Math.Sqrt(horizSpeed * horizSpeed + vsNow * vsNow);
                        _deorbitHaveRealSpeed = true;
                    }
                }
                _deorbitLastDataTick = now;
                _deorbitLastAltitude = status.Altitude;
                _deorbitLastLat = status.Latitude;
                _deorbitLastLon = status.Longitude;
            }
            double totalSpeed = _deorbitCachedSpeed;
            bool haveRealSpeed = _deorbitHaveRealSpeed;

            double gravityG = bodyDetail != null && bodyDetail.SurfaceGravity > 0 ? bodyDetail.SurfaceGravity / 9.80665 : 1.0;
            string atmosphere = bodyDetail != null ? FormatAtmosphere(bodyDetail.AtmosphereType) : "—";

            // Cosmetic course-deviation wander, re-added purely for visual interest (there's no
            // real attitude data behind it) — a slow random walk, same idea as the original
            // concept mockups. With Deorbit now covering the whole approach instead of just the
            // ~10s glide window, there's a lot more time for this to actually be visible.
            if (Math.Abs(_deorbitDevValue - _deorbitDevTarget) < 0.05 && _deorbitDevRandom.NextDouble() < 0.01)
                _deorbitDevTarget = (_deorbitDevRandom.NextDouble() * 2 - 1) * 1.4;
            _deorbitDevValue += (_deorbitDevTarget - _deorbitDevValue) * Math.Min(1, dt * 1.2);

            deorbitPanelCanvas.Children.Clear();
            deorbitPanelCanvas.Children.Add(MakeGridBackground(620, 580));
            // The placeholder planet below deliberately extends past the canvas edge (same
            // composition as the reference mockup) — clip so it doesn't bleed into whatever
            // sits beside this panel once the Viewbox scales it up.
            deorbitPanelCanvas.ClipToBounds = true;

            // Ground-proximity tint layers on top of the deviation colour below it — whichever
            // is more severe wins, same as the concept mockups' devColor().
            double closeness = Math.Max(0, Math.Min(1, 1.0 - status.Altitude / 12000.0));
            double devAbs = Math.Abs(_deorbitDevValue);

            // The new arc scene is a DESCENT-specific visualization (an arc onto a planet only
            // makes sense heading toward one) — it replaced the old direction-agnostic tunnel
            // outright at first, which meant it was showing during LAUNCH too, an ascent away
            // from the surface the arc's own imagery contradicts. Only route to it once the
            // real vertical-speed reading has actually confirmed descent; launch (and the
            // brief ambiguous window before there's a real reading yet) keeps the old tunnel
            // for now. A dedicated launch-specific animation is real future work, not a
            // temporary stand-in for this one reused backwards.
            //
            // Real report: a landing opened with the launch animation, then swapped to the descent
            // arc and the ship ran BACKWARDS up the arc until it reached the right height. Two
            // causes, both from a single noisy Status.json reading (altitude only updates a few
            // times a second, and the first samples of an approach can jump):
            //  1. One positive vertical-speed sample was enough to commit to "launch". Now a climb
            //     must be sustained (CLIMB_CONFIRM_S) before the launch scene shows; until then the
            //     neutral holding state shows, and once the arc has started a brief upward blip
            //     doesn't knock it back out.
            //  2. A bad altitude estimate could push the ship far along the arc, then the next
            //     real reading pulled it back. Progress is now monotonic within a descent, and the
            //     dead-reckoned altitude is bounded (see below), so it can't overshoot to begin with.
            const double ClimbConfirmSeconds = 2.0;
            bool climbingNow = haveRealSpeed && _deorbitCachedVs > 0;
            if (climbingNow) { if (_deorbitClimbSince == DateTime.MinValue) _deorbitClimbSince = now; }
            else _deorbitClimbSince = DateTime.MinValue;
            bool sustainedClimb = climbingNow && (now - _deorbitClimbSince).TotalSeconds >= ClimbConfirmSeconds;
            bool arcStarted = _deorbitProgress.HasValue;
            bool isDescending = haveRealSpeed && (!climbingNow || (arcStarted && !sustainedClimb));
            bool undecided = !haveRealSpeed || (climbingNow && !sustainedClimb && !arcStarted);
            // Altitude only updates a few times a second, so extrapolate it from the last reading and the real climb rate
            // (bounded) to keep the planet gliding instead of moving in bursts. Shared by the climb and jump screens.
            double estAltForScene = status.Altitude;
            if (_deorbitLastAltitude.HasValue && _deorbitLastDataTick != DateTime.MinValue)
            {
                double since = Math.Min((now - _deorbitLastDataTick).TotalSeconds, 1.0);
                estAltForScene = Math.Min(_deorbitLastAltitude.Value + Math.Max(_deorbitCachedVs, 0) * since, Math.Max(_deorbitLastAltitude.Value * 1.5, 1000));
            }
            bool jumpScene = JumpSceneActive(status);
            string jumpPhase = "";
            if (jumpScene) jumpPhase = DrawJumpScene(deorbitPanelCanvas, bodyDetail, status, estAltForScene, dt);
            else if (isDescending)
            {
            // ---- Descent-arc scene — first WPF port of the "Deorbit Trajectory Concept"
            // mockup (a curved glide path with fixed gates the ship flies through, ending on a
            // planet, instead of a straight-ahead tunnel). ----

            if (!_deorbitProgress.HasValue)
            {
                _deorbitProgress = 0;
                // Captured once, right when tracking starts — THIS approach's own real starting
                // altitude, not a fixed guess, since that varies a lot by body/approach.
                _deorbitStartAltitude = status.Altitude > 0 ? status.Altitude : 1000_000;
                Log.Write($"Deorbit: arc started — startAlt={_deorbitStartAltitude / 1000:F1}km vs={_deorbitCachedVs:F0}m/s body='{status.BodyName}'");
            }

            // Real altitude, log-scaled between this approach's own starting altitude and a low
            // floor near where Glide itself actually finishes. Log-scale, not linear: a single
            // approach spans orders of magnitude of altitude (hundreds+ km down to single-digit
            // km), and a linear ratio barely moves across most of that range before rushing at
            // the very end — log tracks real altitude loss smoothly across the whole span
            // instead.
            //
            // FloorKm was 2.0 — real report: with that value, the ship only made it ~50% of the
            // way along the final arc by the time Glide actually finished, meaning 2km sat well
            // BELOW where real Glide hands back control, so the curve still had a long way left
            // to go (in log terms) when the panel's own real endpoint had already arrived. Raised
            // to 8.0: still just an estimate (real Glide's hand-off altitude isn't exposed
            // anywhere in Status.json, and clearly varies by body — a fixed floor can't be exact
            // for all of them), but it should land closer to "done by the time Glide ends" more
            // often than not, erring toward finishing a little early rather than short again
            // (arriving early and holding reads as arrived; falling short reads as unfinished).
            const double FloorKm = 8.0;
            // Real report: motion "lurches" instead of flowing — root cause is that
            // status.Altitude itself only actually changes a few times a second (same
            // Status.json refresh-rate limit noted above for vs/speed), so feeding it straight
            // into altitudeT gave a step function: the smoothing lerp below would nearly finish
            // catching up to a stale target well before the NEXT real update arrived, then
            // visibly jump again the instant it did — smooth-then-stall-then-jump, repeating.
            // Extrapolating altitude from the last real reading plus the real sink rate already
            // being tracked (_deorbitCachedVs, held steady between updates the same way) gives
            // a target that itself changes every single animation frame, not just a few times a
            // second — genuinely smooth AND still accurate, since it's real telemetry-derived
            // dead-reckoning, not a guess, and self-corrects the instant fresh real data arrives.
            double estimatedAltitude = status.Altitude;
            if (_deorbitLastAltitude.HasValue && _deorbitLastDataTick != DateTime.MinValue)
            {
                // Bounded dead-reckoning: at most 1s past the last real reading, and never more
                // than a quarter of the altitude below it — one odd sink-rate sample (the first
                // readings of an approach can jump) must not fling the estimate to the surface.
                double sinceData = Math.Min((now - _deorbitLastDataTick).TotalSeconds, 1.0);
                if (sinceData > 0)
                    estimatedAltitude = Math.Max(_deorbitLastAltitude.Value + _deorbitCachedVs * sinceData,
                                                 _deorbitLastAltitude.Value * 0.75);
            }
            // An altitude of 0 (or less) before real telemetry is populated isn't a position —
            // it would read as "already at the ground" (progress clamps to 1).
            if (estimatedAltitude <= 0 && _deorbitStartAltitude > 0) estimatedAltitude = _deorbitStartAltitude;
            double altKm = Math.Max(estimatedAltitude, 1) / 1000.0;
            double xNow = Math.Log10(altKm);
            double xStart = Math.Log10(Math.Max(_deorbitStartAltitude / 1000.0, FloorKm * 1.01));
            double xFloor = Math.Log10(FloorKm);
            double altitudeT = Math.Clamp((xStart - xNow) / (xStart - xFloor), 0, 1);

            // Still smoothed on top of the extrapolated target (not snapped) — mainly to erase
            // the small discontinuity at the instant a fresh real reading supersedes the
            // extrapolation (if the real sink rate shifted slightly since the last update).
            if (dt > 0)
            {
                const double SmoothingPerSecond = 2.0;
                double lerp = 1.0 - Math.Exp(-SmoothingPerSecond * dt);
                // Forward only: a descent never runs the ship back up the arc, whatever one
                // noisy reading says (the arc restarts cleanly with the next approach).
                if (altitudeT > _deorbitProgress.Value)
                    _deorbitProgress = _deorbitProgress.Value + (altitudeT - _deorbitProgress.Value) * lerp;
            }
            double descentT = _deorbitProgress.Value;


            // Real planet render — GetTerrainSceneFrame (already public, used by the Planet
            // tab) turned out to need no new "sphere-only" export at all: it clips to a circle
            // and never fills anything outside it, so its own output is already a transparent-
            // background image of just the sphere + its own real atmosphere haze, nothing else
            // baked in. Deorbit is glide-only, and only landable terrain-family bodies are ever
            // glide targets (gas giants aren't landable), so this covers every real case.
            // Sized so the sphere GetTerrainSceneFrame draws (always 0.34x its own box) comes
            // out at exactly this scene's planetR, then positioned using that same method's own
            // real sphere center (GetTerrainGeometry) rather than assuming it's the box's exact
            // middle. Falls back to a plain placeholder gradient when there's no scan data to
            // render from at all (an unscanned body — confirmed with a real screenshot) or the
            // body isn't a recognized terrain class.
            string? deorbitIconCode = bodyDetail != null ? MapPlanetClassToIconCode(bodyDetail.PlanetClass) : null;
            bool haveRealPlanetRender = bodyDetail != null && deorbitIconCode != null && PlanetRenderer.IsTerrainFamily(deorbitIconCode);

            if (!haveRealPlanetRender)
            {
                // Real report: "the planet jumps and grows bigger... about half way down." Root
                // cause — the placeholder circle previously drawn here (below) was sized to
                // match an UNRINGED real render exactly (planetR = that render's own sphere
                // radius). A RINGED body's real render extends its rings well past that, out to
                // nearly 1.35x planetR. bodyDetail/PlanetClass doesn't always resolve the
                // instant Deorbit engages (Status.json's BodyName, and this app's own known-body
                // lookup, have a real timing gap), so the scene used to draw the smaller
                // placeholder for the first several frames, then swap to the bigger ringed
                // render the instant real detail arrived — the reported jump. Holding on a
                // neutral state instead means nothing full-size is ever drawn before the real
                // (possibly ringed) size is known, so there's nothing to jump FROM. The
                // overwhelming common case — the target was already scanned in orbit before the
                // glide began — resolves this within a frame or two; this hold is rarely even
                // visible.
                deorbitPanelCanvas.Children.Add(MakeCenterLabel("RESOLVING BODY DATA…", 310, 270, InfoDimBrush, 14));
            }
            else
            {
                DrawDescentScene(deorbitPanelCanvas, bodyDetail!, deorbitIconCode!, descentT, dt, _deorbitDevValue, _deorbitCachedVs);
            }
            }
            else if (undecided)
            {
                // Neutral holding state for the brief ambiguous window before there's a real
                // vertical-speed reading yet. Direct feedback: showing the OLD tunnel here (then
                // swapping to the new arc scene the instant descent is confirmed) was exactly
                // the jarring "flash the wrong animation, then swap" that got reported — the
                // wait-for-direction gate above only ever controlled which scene commits, not
                // what shows while nothing's committed yet. Better to show nothing committal for
                // the one or two ticks this usually lasts than commit to the wrong one, however
                // briefly.
                deorbitPanelCanvas.Children.Add(MakeCenterLabel("INITIALIZING…", 310, 270, InfoDimBrush, 14));
            }
            else
            {
            }

            // The glide flag doesn't distinguish direction — it's Frontier's general assisted-
            // atmospheric-flight state, so it's just as active climbing out as it is coming
            // down. Vertical speed's sign is the only thing that actually tells them apart, and
            // guessing "descending" before there's a real reading yet was actively wrong often
            // enough to be worse than saying nothing — a neutral label until it's known beats a
            // confident wrong one for the first second.
            string modeLabel = jumpScene ? "FSD JUMP" : undecided ? "ATMOSPHERIC TRANSIT"
                : isDescending ? "DEORBIT" : "LAUNCH TRAJECTORY";
            tabRadar.Content = jumpScene ? "⛛ JUMP" : undecided ? "⛛ TRANSIT" : isDescending ? "⛛ DEORBIT" : "⤒ LAUNCH";

            // Three tiers, matching the gate colouring above — "drifting" was missing entirely
            // before (only the two extremes had distinct text), so most of the wander's range
            // never showed anything different from steady cruising.
            string statusText = jumpScene ? $"{modeLabel} — {jumpPhase}" : closeness > 0.8 ? $"{modeLabel} — GROUND PROXIMITY"
                : devAbs > 0.9 ? $"{modeLabel} — CORRECT COURSE NOW"
                : devAbs > 0.45 ? $"{modeLabel} — DEVIATION DETECTED"
                : $"{modeLabel} — ON COURSE";
            var statusBrush = jumpScene ? InfoValueBrush : closeness > 0.8 || devAbs > 0.9 ? InfoDangerBrush : devAbs > 0.45 ? InfoOrangeBrush : InfoValueBrush;

            // A solid plate behind the text, not just the text on its own — the tunnel gates
            // pass directly behind/through it constantly, and bare text with no backing card
            // was frequently unreadable against them mid-animation.
            var statusLabelInner = new TextBlock
            {
                Text = statusText, Foreground = statusBrush, FontFamily = new FontFamily("Consolas"),
                FontSize = 15, FontWeight = FontWeights.Bold,
            };
            var statusPlate = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xe6, 0x08, 0x0d, 0x0d)),
                BorderBrush = statusBrush, BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                Child = statusLabelInner,
            };
            statusPlate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(statusPlate, 310 - statusPlate.DesiredSize.Width / 2);
            Canvas.SetTop(statusPlate, 10);
            deorbitPanelCanvas.Children.Add(statusPlate);

            // Large numbers (tens of thousands of m/s during the high-altitude approach, well
            // before glide proper) read as km/s instead, same treatment as altitude.
            static string FormatRate(double v) => Math.Abs(v) >= 1000 ? $"{v / 1000:N1} km/s" : $"{v:N0} m/s";

            string altText = status.Altitude >= 1000 ? $"{status.Altitude / 1000:N1} km"
                : status.Altitude > 0 ? $"{status.Altitude:N0} m" : "—";
            AddHudStat(deorbitPanelCanvas, 20, 24, false, "ALTITUDE", altText, InfoBrightValueBrush);
            AddHudStat(deorbitPanelCanvas, 600, 24, true, "GRAVITY", $"{gravityG:F2} G", InfoBrightValueBrush);
            // One speed reading, not two — a separate "vertical speed" stat here and a total
            // speed label at the bottom both showing numbers at once read as two conflicting
            // speeds. This is the total (always non-negative — it's a vector magnitude), with
            // the known glide-speed constant as a placeholder before there's a real reading yet.
            AddHudStat(deorbitPanelCanvas, 20, 516, false, "SPEED",
                haveRealSpeed ? FormatRate(totalSpeed) : "~2.5 km/s", InfoBrightValueBrush);
            AddHudStat(deorbitPanelCanvas, 600, 516, true, "ATMOSPHERE", atmosphere, InfoBrightValueBrush);
        }

        // ---------------------------------------------------------------
        //  FSS SCANNER — "Orbital Map": temporarily replaces the STAR tab while the FSS has
        //  focus (GuiFocus — confirmed 2026-08-30). FSSDiscoveryScan hands us the total body
        //  count before anything is individually resolved, so every planet gets a dot
        //  immediately — dim/unresolved at first, turning amber the moment its Scan event
        //  actually arrives. Ring placement is index-based (this app doesn't parse
        //  SemiMajorAxis), not real orbital distance — a reasonable simplification.
        //
        //  Moons auto-zoom: scanning a moon switches this screen from the system overview to
        //  a close-up of just that moon's parent planet and its own moons, purely reactive to
        //  whichever body was scanned MOST RECENTLY (see zoomParent below) — no manual
        //  control, no persisted "current view" state. The moment a plain planet (or the
        //  star) resolves, it snaps back to the overview on its own.
        // ---------------------------------------------------------------
        private void AddFssFlashRing(Canvas canvas, double x, double y, double life, double baseR)
        {
            if (life <= 0) return;
            foreach (var (rr, alpha) in new (double, double)[] { (baseR + (1 - life) * 30, life * 0.85), (baseR + (1 - life) * 30 - 7, life * 0.5) })
            {
                if (rr <= 0) continue;
                var e = new Ellipse { Width = rr * 2, Height = rr * 2, Stroke = InfoValueBrush, StrokeThickness = 1.5, Opacity = Math.Max(0, alpha) };
                Canvas.SetLeft(e, x - rr);
                Canvas.SetTop(e, y - rr);
                canvas.Children.Add(e);
            }
        }

        private double FssFlashLife(string name, DateTime now) =>
            _fssResolvedAt.TryGetValue(name, out var t0) ? Math.Max(0, 1 - (now - t0).TotalSeconds / 0.7) : 0;

        private void UpdateFssScannerPanel(bool force = false)
        {
            var watcher = _watcher;
            if (watcher == null) return;

            // Per-system flash-tracking state needs to reset when the system actually changes —
            // the watcher already clears SystemBodyCount/ResolvedBodiesOrder on arrival, but
            // this view's own bookkeeping (which bodies we've already flashed for) doesn't
            // know that on its own.
            if (!string.Equals(watcher.StarSystem, _fssLastSystem, StringComparison.OrdinalIgnoreCase))
            {
                _fssLastSystem = watcher.StarSystem;
                _fssResolvedAt.Clear();
                _fssLastResolvedCount = -1;
                _fssLastMoonScanAt = DateTime.MinValue;
            }

            var now = DateTime.UtcNow;
            double dt = _fssLastTick == DateTime.MinValue ? 0 : Math.Min((now - _fssLastTick).TotalSeconds, 0.5);
            _fssLastTick = now;
            _fssOrbitPhase += dt * 0.05;

            // Asteroid belt clusters get a Scan event same as any real body, but they're not
            // planets and don't belong on this screen — excluded here so nothing downstream
            // (rings, manifest, totals, auto-zoom) ever has to know they exist.
            var resolved = watcher.ResolvedBodiesOrder
                .Where(n => watcher.GetKnownBodyDetail(n)?.IsBelt != true)
                .ToList();
            int total = Math.Max(watcher.SystemBodyCount, resolved.Count);

            var detailsByName = new Dictionary<string, BodyScanDetail?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in resolved) detailsByName[name] = watcher.GetKnownBodyDetail(name);

            // Auto-zoom — see the class comment above. Whatever was scanned last decides, with
            // one addition: if 5 seconds pass with no further moon of the zoomed planet
            // resolving, it drops back to the overview on its own instead of sitting zoomed on
            // the last planet you worked forever (e.g. you fly off without scanning anything
            // else — "last resolved was a moon" would otherwise stay true indefinitely).
            string? lastResolved = resolved.Count > 0 ? resolved[resolved.Count - 1] : null;
            var lastDetail = lastResolved != null ? detailsByName.GetValueOrDefault(lastResolved) : null;
            if (resolved.Count != _fssLastResolvedCount)
            {
                foreach (var name in resolved)
                    if (!_fssResolvedAt.ContainsKey(name)) _fssResolvedAt[name] = now;
                if (lastDetail != null && lastDetail.IsMoon) _fssLastMoonScanAt = now;
                _fssLastResolvedCount = resolved.Count;
            }
            BodyScanDetail? zoomPlanet = null;
            if (lastDetail != null && lastDetail.IsMoon && (now - _fssLastMoonScanAt).TotalSeconds <= 5)
                zoomPlanet = detailsByName.Values.FirstOrDefault(d => d != null && d.BodyID == lastDetail.ParentBodyID);

            fssScannerCanvas.Children.Clear();
            fssScannerCanvas.Children.Add(MakeGridBackground(620, 580));

            if (zoomPlanet != null)
                RenderFssZoomedPlanet(watcher, zoomPlanet, resolved, detailsByName, now);
            else
                RenderFssOverview(watcher, resolved, detailsByName, total, now);

            // ---- manifest — always the full system, independent of the canvas's zoom state ----
            fssManifestCount.Text = $"{resolved.Count} / {(total > 0 ? total.ToString() : "?")}";
            fssManifestStack.Children.Clear();

            Border MakeManifestRow(string title, string subtitle, int bioCount, int geoCount, int miningCount,
                bool isStarRow, bool isResolvedRow, bool isMoonRow)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0d, 0x1c, 0x1c)),
                    BorderBrush = isStarRow ? InfoOrangeBrush : InfoLeaderBrush,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(isMoonRow ? 14 : 0, 0, 0, 4),
                    Opacity = isResolvedRow ? 1.0 : 0.45,
                };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = (isMoonRow ? "↳ " : "") + title, Foreground = isResolvedRow ? InfoBrightValueBrush : InfoDimBrush,
                    FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                // Base classification stays the same teal as before; BIO/GEO counts get their
                // own highlight colors (same ones used elsewhere for BIO/GEO SIGNALS) so a
                // signal-bearing body actually stands out in the scrolled list instead of
                // blending into the rest of the subtitle text.
                var subtitleBlock = new TextBlock
                {
                    Margin = new Thickness(10, 1, 0, 0),
                    FontFamily = new FontFamily("Consolas"), FontSize = 9.5,
                    // Long real classifications ("Gas giant with water based life") were
                    // overflowing the manifest's fixed column width and getting silently
                    // clipped by the ScrollViewer instead of wrapping — TextBlock only wraps
                    // within its stretched width when explicitly told to.
                    TextWrapping = TextWrapping.Wrap,
                };
                subtitleBlock.Inlines.Add(new Run(subtitle) { Foreground = InfoLabelGreenBrush });
                if (bioCount > 0)
                    subtitleBlock.Inlines.Add(new Run($" · {bioCount} BIO") { Foreground = InfoValueBrush });
                if (geoCount > 0)
                    subtitleBlock.Inlines.Add(new Run($" · {geoCount} GEO") { Foreground = InfoOrangeBrush });
                if (miningCount > 0)
                    // "MINE" (not "MINING") to match BIO/GEO's terse 3-letter style, and keep
                    // this already-crowded line shorter now that it wraps.
                    subtitleBlock.Inlines.Add(new Run($" · {miningCount} MINE") { Foreground = InfoMiningBrush });
                stack.Children.Add(subtitleBlock);
                border.Child = stack;
                return border;
            }

            string RowSub(BodyScanDetail? d) => d == null ? "—"
                : d.IsStar ? $"{d.StarType}-CLASS STAR"
                : string.IsNullOrEmpty(d.PlanetClass) ? "BODY" : d.PlanetClass.ToUpperInvariant();

            // Sorted by the game's own body designation, not real orbital distance — a planet's
            // number doesn't always track its true SemiMajorAxis order closely enough (confirmed
            // against real data: a system here resolved as designations 1, 2, 3, 5, 6 but
            // SemiMajorAxis order came out 5, 1, 2, 6, 3), and the designation is what's actually
            // printed on each row anyway, so sorting by anything else looks "out of order" to
            // the player even when it's technically distance-accurate. Planets sort by their
            // leading number (PlanetNumberKey); each planet's own moons sort by their trailing
            // letter (MoonLetterKey, base-26 so multi-letter designations still order correctly).
            int PlanetNumberKey(string name)
            {
                var token = EliteWatcherService.GetShortBodyName(name, watcher.StarSystem).Split(' ')[0];
                return int.TryParse(token, out var n) ? n : int.MaxValue;
            }
            int MoonLetterKey(string name)
            {
                var suffix = EliteWatcherService.GetShortBodyName(name, watcher.StarSystem);
                var letters = suffix.Contains(' ') ? suffix[(suffix.LastIndexOf(' ') + 1)..] : suffix;
                int val = 0;
                foreach (var ch in letters.ToLowerInvariant())
                {
                    if (ch < 'a' || ch > 'z') return int.MaxValue;
                    val = val * 26 + (ch - 'a' + 1);
                }
                return val;
            }
            var starName = resolved.FirstOrDefault(n => detailsByName.GetValueOrDefault(n)?.IsStar == true);
            var planetNames = resolved
                .Where(n => n != starName && detailsByName.GetValueOrDefault(n)?.IsMoon != true)
                .OrderBy(PlanetNumberKey)
                .ToList();
            var moonsByParentId = resolved
                .Where(n => detailsByName.GetValueOrDefault(n)?.IsMoon == true)
                .GroupBy(n => detailsByName[n]!.ParentBodyID)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(MoonLetterKey)
                    .ToList());

            var rows = new List<(string title, string sub, int bio, int geo, int mining, bool isStar, bool resolved, bool isMoon)>();
            if (starName != null)
            {
                var d = detailsByName[starName];
                rows.Add((EliteWatcherService.GetShortBodyName(starName, watcher.StarSystem).ToUpperInvariant(), RowSub(d), 0, 0, 0, true, true, false));
            }
            else if (total > 0)
            {
                rows.Add(("PRIMARY STAR", "awaiting scan…", 0, 0, 0, false, false, false));
            }
            foreach (var p in planetNames)
            {
                var d = detailsByName.GetValueOrDefault(p);
                rows.Add((EliteWatcherService.GetShortBodyName(p, watcher.StarSystem).ToUpperInvariant(), RowSub(d),
                    d?.BioSignalCount ?? 0, d?.GeoSignalCount ?? 0, d?.MiningSignalCount ?? 0, false, true, false));
                if (d != null && moonsByParentId.TryGetValue(d.BodyID, out var moons))
                    foreach (var m in moons)
                    {
                        var md = detailsByName.GetValueOrDefault(m);
                        rows.Add((EliteWatcherService.GetShortBodyName(m, watcher.StarSystem).ToUpperInvariant(), RowSub(md),
                            md?.BioSignalCount ?? 0, md?.GeoSignalCount ?? 0, md?.MiningSignalCount ?? 0, false, true, true));
                    }
            }
            // Generic unresolved placeholders (unknown identity, so unnumbered position among
            // the sorted list above isn't meaningful) always trail at the end.
            int knownCount = 1 + planetNames.Count + moonsByParentId.Values.Sum(l => l.Count); // star slot always counted, resolved or not
            int unresolvedCount = Math.Max(0, total - knownCount);
            for (int i = 0; i < unresolvedCount; i++)
                rows.Add(("UNRESOLVED BODY", "awaiting scan…", 0, 0, 0, false, false, false));

            // No separate numbering added here — the game's own body designation (already in
            // row.title via GetShortBodyName, e.g. "3" for a planet or "3 A" for its moon) IS
            // the real numbering scheme: stars get letters (A/B/C in a multi-star system),
            // planets get numbers relative to their star, moons get "<planet> <letter>". Adding
            // our own sequential count on top of that just duplicated it and, worse, offset it
            // by however many rows came before (planet "1" showing up as row "2." etc).
            foreach (var row in rows)
                fssManifestStack.Children.Add(MakeManifestRow(row.title, row.sub, row.bio, row.geo, row.mining, row.isStar, row.resolved, row.isMoon));
        }

        // System-wide view: star fixed at centre, every planet (resolved or not, moons
        // excluded entirely here) gets a dot on one of the available distance rings — see
        // RenderFssOverview's rank-based ring assignment.
        private void RenderFssOverview(EliteWatcherService watcher, IReadOnlyList<string> resolved,
            Dictionary<string, BodyScanDetail?> detailsByName, int total, DateTime now)
        {
            const double cx = 310, cy = 290, maxR = 250;
            // Planets only — moons never land here (see planetNames below), so slot count alone
            // decides how many rings are worth drawing. More rings when there's more to spread
            // out, capped so the innermost ones don't crowd the star.
            int knownMoonCountForRings = resolved.Count(n => detailsByName.GetValueOrDefault(n)?.IsMoon == true);
            int totalPlanetSlotsForRings = Math.Max(0, total - 1 - knownMoonCountForRings);
            int rings = Math.Clamp(totalPlanetSlotsForRings, 5, 9);
            for (int r = 1; r <= rings; r++)
            {
                var ring = new Ellipse
                {
                    Width = maxR * 2 * r / rings, Height = maxR * 2 * r / rings,
                    Stroke = InfoLeaderBrush, StrokeThickness = 1, Opacity = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                };
                Canvas.SetLeft(ring, cx - ring.Width / 2);
                Canvas.SetTop(ring, cy - ring.Height / 2);
                fssScannerCanvas.Children.Add(ring);
            }

            var starName = resolved.FirstOrDefault(n => detailsByName.GetValueOrDefault(n)?.IsStar == true);
            bool starResolved = starName != null;

            AddFssFlashRing(fssScannerCanvas, cx, cy, starName != null ? FssFlashLife(starName, now) : 0, 6);
            var starDot = new Ellipse
            {
                Width = starResolved ? 12 : 8, Height = starResolved ? 12 : 8,
                Fill = starResolved ? InfoBrightValueBrush : new SolidColorBrush(Color.FromArgb(90, 0xf2, 0xfc, 0xfc)),
            };
            Canvas.SetLeft(starDot, cx - starDot.Width / 2);
            Canvas.SetTop(starDot, cy - starDot.Height / 2);
            fssScannerCanvas.Children.Add(starDot);

            // Planets only — moons resolve into their parent's zoomed view instead, never onto
            // these system-wide rings, so they can't crowd out unrelated planets several times
            // further out.
            var planetNames = resolved.Where(n => n != starName && detailsByName.GetValueOrDefault(n)?.IsMoon != true).ToList();
            int knownMoonCount = resolved.Count(n => detailsByName.GetValueOrDefault(n)?.IsMoon == true);
            int totalPlanetSlots = Math.Max(0, total - 1 - knownMoonCount);

            // Ring assignment by RANK among this system's own known distances, not fixed
            // absolute AU bands (RingForDistance's old job) — a real report: many systems put
            // most planets inside the same absolute band (e.g. everything under 10 AU), which
            // piled them onto 1-2 rings while the others sat empty ("collide into one another
            // ... clearly more space to spread them onto"). Ranking spreads whatever planets
            // this system actually has across every available ring, regardless of the absolute
            // distances involved.
            var knownDistances = planetNames
                .Select((n, idx) => (idx, detail: detailsByName.GetValueOrDefault(n)))
                .Where(x => x.detail != null && x.detail.SemiMajorAxis > 0)
                .OrderBy(x => x.detail!.SemiMajorAxis)
                .ToList();
            var ringByPlanetIndex = new Dictionary<int, int>();
            for (int rank = 0; rank < knownDistances.Count; rank++)
            {
                int ringNum = knownDistances.Count == 1 ? rings
                    : 1 + (int)Math.Round((double)rank * (rings - 1) / (knownDistances.Count - 1));
                ringByPlanetIndex[knownDistances[rank].idx] = ringNum;
            }

            for (int i = 0; i < totalPlanetSlots; i++)
            {
                bool isResolved = i < planetNames.Count;
                string? name = isResolved ? planetNames[i] : null;
                var detail = name != null ? detailsByName.GetValueOrDefault(name) : null;
                // Ranked ring when the real distance is known; a guessed slot (still spread
                // across all rings, just not distance-ordered) for anything not yet scanned or
                // scanned but reporting no distance at all.
                int ring = isResolved && ringByPlanetIndex.TryGetValue(i, out var rk) ? rk : (i % rings) + 1;
                // Per-body start angle and orbit speed, seeded from the body's own name (or the
                // system + slot for a not-yet-resolved placeholder) so each one is stable frame to
                // frame and run to run, but no two share the same lockstep motion — the old
                // formula (fixed 0.87 rad spacing by slot, speed by slot % rings) marched every
                // dot around in a visibly regular pattern. Inner rings still orbit faster than
                // outer ones (loosely Keplerian), with a random 0.6x-1.4x spread on top.
                string seedKey = name ?? $"{watcher.StarSystem}#{i}";
                double startAngle = FssSeed(seedKey, 1) * Math.PI * 2;
                double speed = 1.5 * Math.Pow((double)rings / ring, 0.75) * (0.6 + FssSeed(seedKey, 2) * 0.8);
                double angle = startAngle + _fssOrbitPhase * speed;
                double rr = maxR * ring / rings;
                double x = cx + Math.Cos(angle) * rr;
                double y = cy + Math.Sin(angle) * rr;

                if (isResolved) AddFssFlashRing(fssScannerCanvas, x, y, FssFlashLife(name!, now), 4);

                var dot = new Ellipse
                {
                    Width = isResolved ? 9 : 6, Height = isResolved ? 9 : 6,
                    Fill = isResolved ? InfoOrangeBrush : new SolidColorBrush(Color.FromArgb(90, 0xf2, 0xfc, 0xfc)),
                };
                Canvas.SetLeft(dot, x - dot.Width / 2);
                Canvas.SetTop(dot, y - dot.Height / 2);
                fssScannerCanvas.Children.Add(dot);

                // Small tick mark for a planet with at least one already-resolved moon, so the
                // overview hints there's more here without drawing the moons themselves.
                if (isResolved && detail != null &&
                    resolved.Any(n => detailsByName.GetValueOrDefault(n) is { IsMoon: true } m && m.ParentBodyID == detail.BodyID))
                {
                    var tick = new Ellipse { Width = 3, Height = 3, Fill = InfoDimBrush };
                    Canvas.SetLeft(tick, x + 7);
                    Canvas.SetTop(tick, y - 7);
                    fssScannerCanvas.Children.Add(tick);
                }
            }
        }

        // Deterministic 0..1 value from a string + salt (FNV-1a). string.GetHashCode is
        // randomized per process in .NET, which would reshuffle every orbit on each app launch.
        private static double FssSeed(string key, int salt)
        {
            uint h = 2166136261u ^ (uint)salt;
            foreach (char c in key) { h ^= c; h *= 16777619u; }
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }

        // Zoomed view: the just-scanned moon's parent planet sits at centre (in place of the
        // star), its own resolved moons ringed close around it. No system-wide total exists
        // for "how many moons does this planet have," so unlike the overview there are no
        // placeholder dots for unresolved moons — just what's actually been scanned so far.
        private void RenderFssZoomedPlanet(EliteWatcherService watcher, BodyScanDetail planet,
            IReadOnlyList<string> resolved, Dictionary<string, BodyScanDetail?> detailsByName, DateTime now)
        {
            const double cx = 310, cy = 290, maxR = 220;
            const int rings = 3;
            for (int r = 1; r <= rings; r++)
            {
                var ring = new Ellipse
                {
                    Width = maxR * 2 * r / rings, Height = maxR * 2 * r / rings,
                    Stroke = InfoLeaderBrush, StrokeThickness = 1, Opacity = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                };
                Canvas.SetLeft(ring, cx - ring.Width / 2);
                Canvas.SetTop(ring, cy - ring.Height / 2);
                fssScannerCanvas.Children.Add(ring);
            }

            AddFssFlashRing(fssScannerCanvas, cx, cy, FssFlashLife(planet.BodyName, now), 8);
            var planetDot = new Ellipse { Width = 16, Height = 16, Fill = InfoOrangeBrush };
            Canvas.SetLeft(planetDot, cx - 8);
            Canvas.SetTop(planetDot, cy - 8);
            fssScannerCanvas.Children.Add(planetDot);

            var moonNames = resolved.Where(n => detailsByName.GetValueOrDefault(n) is { IsMoon: true } m && m.ParentBodyID == planet.BodyID).ToList();
            for (int i = 0; i < moonNames.Count; i++)
            {
                int ring = (i % rings) + 1;
                double angle = (i * 1.3) + _fssOrbitPhase * (rings - (i % rings) + 1);
                double rr = maxR * ring / rings;
                double x = cx + Math.Cos(angle) * rr;
                double y = cy + Math.Sin(angle) * rr;

                AddFssFlashRing(fssScannerCanvas, x, y, FssFlashLife(moonNames[i], now), 4);
                var dot = new Ellipse { Width = 8, Height = 8, Fill = InfoBrightValueBrush };
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);
                fssScannerCanvas.Children.Add(dot);
            }

            string planetShortName = EliteWatcherService.GetShortBodyName(planet.BodyName, watcher.StarSystem);
            fssScannerCanvas.Children.Add(MakeCenterLabel(
                $"{planetShortName.ToUpperInvariant()} — {moonNames.Count} MOON{(moonNames.Count == 1 ? "" : "S")} RESOLVED",
                310, 14, InfoValueBrush, 12));
        }

        // Scoopable = main-sequence classes (KGB FOAM) plus Wolf-Rayet stars; white dwarfs,
        // neutron stars, and black holes are not. hop.StarClass from NavRoute.json is a
        // short code like "K"/"WC", matching this set directly.
        private static readonly HashSet<string> ScoopableStarClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "O", "B", "A", "F", "G", "K", "M", "W", "WN", "WNC", "WC", "WO"
        };
        private static bool IsScoopableStar(string starClass) =>
            !string.IsNullOrEmpty(starClass) && ScoopableStarClasses.Contains(starClass);

        // ---- Info panel drawing helpers ----

        // Faint tiled grid, matching the mockup's background — a RadialGradientBrush
        // OpacityMask fades it out toward the panel edges instead of a hard tile boundary.
        private static Rectangle MakeGridBackground(double width, double height)
        {
            var tile = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 40, 40),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing
                {
                    Pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1a, 0x44, 0x44)), 1),
                    Geometry = new GeometryGroup
                    {
                        Children =
                        {
                            new LineGeometry(new Point(0, 0), new Point(40, 0)),
                            new LineGeometry(new Point(0, 0), new Point(0, 40)),
                        }
                    }
                }
            };
            tile.Freeze();

            return new Rectangle
            {
                Width = width, Height = height,
                Fill = tile,
                IsHitTestVisible = false,
                OpacityMask = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x50, 0, 0, 0), 0.0),
                        new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0),
                    },
                    RadiusX = 0.7, RadiusY = 0.7,
                }
            };
        }

        private static Image MakeImg(string relativePath, double x, double y, double size)
        {
            var img = new Image
            {
                Width = size,
                Height = size,
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(IconBaseUri + relativePath, UriKind.Absolute))
            };
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            return img;
        }

        // Shared badge shell — dark disc with a glowing rim (BlurEffect standing in for the
        // approved mockup's SVG gaussian-blur filter) — used by both mining badge variants
        // below. (centerX, centerY) is the badge's center, not its top-left corner.
        private static Canvas MakeSignalBadge(double centerX, double centerY, double size, Brush brush, string glyphData)
        {
            var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            double r = size / 2;

            var glow = new Ellipse
            {
                Width = size, Height = size,
                Stroke = brush, StrokeThickness = size * 0.14, Opacity = 0.6,
                Effect = new BlurEffect { Radius = size * 0.28 },
            };
            canvas.Children.Add(glow);

            var disc = new Ellipse
            {
                Width = size * 0.86, Height = size * 0.86,
                Fill = new SolidColorBrush(Color.FromRgb(0x0d, 0x14, 0x14)),
                Stroke = brush, StrokeThickness = 2,
            };
            Canvas.SetLeft(disc, size * 0.07); Canvas.SetTop(disc, size * 0.07);
            canvas.Children.Add(disc);

            var glyph = new System.Windows.Shapes.Path
            {
                Stroke = brush, StrokeThickness = size * 0.06,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse(glyphData),
                Stretch = Stretch.Uniform, Width = size * 0.6, Height = size * 0.6,
            };
            Canvas.SetLeft(glyph, size * 0.2); Canvas.SetTop(glyph, size * 0.2);
            canvas.Children.Add(glyph);

            Canvas.SetLeft(canvas, centerX - r); Canvas.SetTop(canvas, centerY - r);
            return canvas;
        }

        // Pick & Hammer — surface mining locations. No PNG art exists for this yet (brand new
        // signal type), matching the mockup approved for it.
        // internal (not private) — SystemScanWindow reuses these exact same badges for its own
        // bio/geo/mining icons so they match the Planet tab's colors/shapes instead of a second
        // icon language drifting out of sync with it.
        internal static Canvas MakeMiningBadge(double centerX, double centerY, double size) =>
            MakeSignalBadge(centerX, centerY, size, InfoMiningBrush,
                "M 0.34 0.34 L 0.5 0.5 M 0.66 0.34 L 0.5 0.5 " +
                "M 0.42 0.58 L 0.60 0.58 M 0.42 0.58 L 0.37 0.68 M 0.60 0.58 L 0.65 0.68");

        // Ore Cluster — ring hotspot locations. Deliberately a different glyph (and the paler
        // InfoRingMiningBrush) from the surface badge above, per the approved mockup's second
        // "mining" option, so the two location types read as related but distinct.
        internal static Canvas MakeRingMiningBadge(double centerX, double centerY, double size) =>
            MakeSignalBadge(centerX, centerY, size, InfoRingMiningBrush,
                "M 0.5 0.31 L 0.63 0.44 L 0.56 0.65 L 0.44 0.65 L 0.37 0.44 Z " +
                "M 0.5 0.31 L 0.5 0.65 M 0.37 0.44 L 0.63 0.44");

        // Sprout — bio signal locations, drawn directly on the terrain scene (the gas-giant
        // panel never needed this since it has no on-sphere badge convention at all beyond
        // Mining/Ring Mining; the legacy flat-icon renderer's overlay_badge_bio.png is what this
        // replaces for the terrain family). Same cyan as the Bio Signals stat text.
        internal static Canvas MakeBioBadge(double centerX, double centerY, double size) =>
            MakeSignalBadge(centerX, centerY, size, InfoValueBrush,
                "M 0.5 0.7 L 0.5 0.4 " +
                "M 0.5 0.55 C 0.35 0.5 0.32 0.35 0.4 0.26 C 0.48 0.36 0.5 0.46 0.5 0.55 " +
                "M 0.5 0.48 C 0.65 0.42 0.68 0.28 0.6 0.2 C 0.52 0.3 0.5 0.4 0.5 0.48");

        // Mountain range — geo signal locations, same replacement rationale as the bio badge
        // above. Same orange as the Geo Signals stat text.
        internal static Canvas MakeGeoBadge(double centerX, double centerY, double size) =>
            MakeSignalBadge(centerX, centerY, size, InfoOrangeBrush,
                "M 0.26 0.64 L 0.42 0.36 L 0.52 0.5 L 0.64 0.32 L 0.76 0.64");

        private static TextBlock MakeCenterLabel(string text, double centerX, double y, Brush brush, double fontSize)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontFamily = new FontFamily("Consolas"), FontSize = fontSize };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, y);
            return tb;
        }

        // Same as MakeCenterLabel, but for the gas-giant/terrain HUD's own bottom body-name and
        // SCAN labels specifically — those were still using MakeCenterLabel's plain Consolas
        // with no letter-spacing, the exact same look every OTHER panel on this method (Star
        // tab, asteroid belt, legacy planet icons, Deorbit, FSS Scanner) still correctly uses,
        // so this is its own method rather than changing MakeCenterLabel itself and pulling
        // every one of those unrelated panels along with it.
        private static readonly SolidColorBrush InfoOtherCmdrBrush     = new(Color.FromRgb(0xff, 0x5c, 0x66));
        private static readonly SolidColorBrush InfoFirstDiscoveryBrush = new(Color.FromRgb(0x7e, 0xe7, 0x87));

        // Third line under the body name and SCAN line: who discovered/mapped this body.
        // The journal only says WHETHER someone already had (Scan's WasDiscovered/WasMapped) —
        // never WHO — and that "someone" includes you, so DiscoveryIndex (built from your own
        // journal history) is what separates "you" from "another commander". Shows nothing when
        // the scan carried neither flag. Until the index finishes its startup pass a discovered
        // body just reads "DISCOVERED" rather than risk blaming another commander for your find.
        private static void AddDiscoveryLine(Canvas canvas, BodyScanDetail detail, double centerX, double y, double fontSize)
        {
            var parts = new List<(string Text, Brush Brush)>();

            if (detail.WasDiscovered == false)
                parts.Add(("FIRST DISCOVERY", InfoFirstDiscoveryBrush));
            else if (detail.WasDiscovered == true)
            {
                if (DiscoveryIndex.IsDiscoveredByMe(detail.BodyName)) parts.Add(("DISCOVERED BY YOU", InfoLabelGreenBrush));
                else if (!DiscoveryIndex.IsReady)                     parts.Add(("DISCOVERED", InfoLabelGreenBrush));
                else                                                  parts.Add(("DISCOVERED BY OTHER CMDR", InfoOtherCmdrBrush));
            }

            // Stars can't be DSS-mapped, so the mapped half only applies to planets and moons.
            if (detail.IsStar) { }
            else if (detail.IsMapped || DiscoveryIndex.IsMappedByMe(detail.BodyName)) parts.Add(("MAPPED BY YOU", InfoLabelGreenBrush));
            else if (detail.WasMapped == true)                                  parts.Add(("MAPPED BY OTHER CMDR", InfoOtherCmdrBrush));
            else if (detail.WasMapped == false)                                  parts.Add(("NOT MAPPED", InfoLabelGreenBrush));

            if (parts.Count == 0) return;

            var tb = new TextBlock { FontFamily = GasGiantFont, FontSize = fontSize };
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) tb.Inlines.Add(new System.Windows.Documents.Run("   |   ") { Foreground = InfoDimBrush });
                tb.Inlines.Add(new System.Windows.Documents.Run(Track(parts[i].Text)) { Foreground = parts[i].Brush });
            }
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        private static TextBlock MakeHudCenterLabel(string text, double centerX, double y, Brush brush, double fontSize)
        {
            var tb = new TextBlock { Text = Track(text), Foreground = brush, FontFamily = GasGiantFont, FontSize = fontSize };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, y);
            return tb;
        }

        // A notable qualifier — "Water-Based Life", a Sudarsky class, "Tidally Locked", etc. —
        // as a small bordered gold chip, matching the approved mockup's trait-tag style. A
        // plain text line (what the first pass of the gas-giant rework used) doesn't carry
        // that same distinct, "this is worth noticing" visual weight.
        // maxWidth wraps the chip's text within it (and sizes the chip to that width) instead
        // of letting it measure however wide it wants — a narrow stat column has no room for
        // a one-line "WATER-BASED LIFE" chip without it overflowing into the scene or off the
        // panel's own edge. rightAlign mirrors AddStackedStat's own convention: false means x
        // is the chip's right edge (matching a left-hand column's flush-right text), true
        // means x is its left edge — the chip was centering under its column regardless of how
        // the actual stat text above it aligned, instead of lining up flush with it.
        // solidColor/solidFill: a chip that must match another window's chip exactly (Terraformable
        // uses System Scan's green + dark-green fill) instead of the default translucent gold.
        private static Border MakeTraitChip(string text, double x, double y, double maxWidth, bool rightAlign,
            Color? solidColor = null, Color? solidFill = null)
        {
            var gold = new SolidColorBrush(solidColor ?? Color.FromRgb(0xe8, 0xc7, 0x66));
            var tb = new TextBlock
            {
                // 7.5→9→9.5 — the rest of this HUD went up 1.5pt for the same size pass, but
                // this chip's headroom is much tighter (the longest real trait, "AMMONIA-BASED
                // LIFE", only had ~12px to spare at 9pt) — a smaller, safer +0.5 here instead
                // of matching the 1.5 everywhere else, to avoid pushing that one onto two lines.
                Text = Track(text), Foreground = gold, FontFamily = GasGiantFont,
                FontSize = 9.5, FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
            };
            // Shrink the chip to fit the actual text instead of always rendering at the full
            // column width — a short trait like "Water-Based Life" was rendering inside a
            // border sized for the widest possible trait ("Ammonia-Based Life"), leaving a lot
            // of dead gold-bordered space around it. Measure the text's own natural (unwrapped)
            // width first; only fall back to wrapping at the column width if it's genuinely
            // too long to fit on one line.
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double chipTextWidth;
            if (tb.DesiredSize.Width <= maxWidth)
            {
                chipTextWidth = tb.DesiredSize.Width;
                tb.Width = chipTextWidth;
                tb.TextWrapping = TextWrapping.NoWrap;
            }
            else
            {
                chipTextWidth = maxWidth;
                tb.Width = chipTextWidth;
                tb.TextWrapping = TextWrapping.Wrap;
            }
            var border = new Border
            {
                Child = tb,
                Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = solidColor.HasValue ? gold : new SolidColorBrush(Color.FromArgb(0x66, 0xe8, 0xc7, 0x66)),
                BorderThickness = new Thickness(1),
                Background = solidFill.HasValue ? new SolidColorBrush(solidFill.Value) : new SolidColorBrush(Color.FromArgb(0x14, 0xe8, 0xc7, 0x66)),
            };
            border.Measure(new Size(chipTextWidth + 8, double.PositiveInfinity));
            Canvas.SetLeft(border, rightAlign ? x : x - border.DesiredSize.Width);
            Canvas.SetTop(border, y);
            return border;
        }

        // Draws an angled-then-flat leader line (2 or 3 points) plus a key/value callout at
        // its terminal point — mirrors the approved SVG mockup's layout exactly.
        private static void AddCallout(Canvas canvas, (double x, double y)[] leaderPoints, double rowY,
            bool rightSide, string key, string value, Brush? valueBrush = null, Brush? keyBrush = null,
            bool centered = false)
        {
            var poly = new Polyline { Stroke = InfoLeaderBrush, StrokeThickness = 1.6 };
            foreach (var p in leaderPoints) poly.Points.Add(new Point(p.x, p.y));
            canvas.Children.Add(poly);

            const double calloutWidth = 140; // clearance from the leader's terminal point to the canvas edge

            double textX = leaderPoints[^1].x;
            var keyTb = new TextBlock
            {
                Text = key, Foreground = keyBrush ?? InfoValueBrush, FontFamily = new FontFamily("Consolas"),
                FontSize = 13.5, FontWeight = FontWeights.Bold
            };
            keyTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double keyX = centered ? textX - keyTb.DesiredSize.Width / 2 : rightSide ? textX : textX - keyTb.DesiredSize.Width;
            Canvas.SetLeft(keyTb, keyX); Canvas.SetTop(keyTb, rowY - 10);
            canvas.Children.Add(keyTb);

            // Real game strings vary wildly in length ("Sudarsky class I gas giant",
            // "Gas giant with water based life") — wrap within a fixed-width box instead of
            // letting the text grow past the canvas edge into whatever sits behind the panel.
            var valTb = new TextBlock
            {
                Text = value, Foreground = valueBrush ?? InfoBrightValueBrush,
                FontFamily = new FontFamily("Consolas"), FontSize = 15,
                Width = calloutWidth, TextWrapping = TextWrapping.Wrap,
                TextAlignment = centered ? TextAlignment.Center : rightSide ? TextAlignment.Left : TextAlignment.Right,
            };
            double valX = centered ? textX - calloutWidth / 2 : rightSide ? textX : textX - calloutWidth;
            Canvas.SetLeft(valTb, valX); Canvas.SetTop(valTb, rowY + 12);
            canvas.Children.Add(valTb);
        }

        // internal (not private) so SystemScanWindow can pick the same real terrain/gas-giant
        // renderer for its own body thumbnails instead of a second classification drifting out
        // of sync with this one.
        internal static string? MapPlanetClassToIconCode(string planetClass)
        {
            if (string.IsNullOrEmpty(planetClass)) return null;
            var p = planetClass.ToLowerInvariant();
            if (p.Contains("high metal content")) return "HMC";
            if (p.Contains("metal rich")) return "MRB";
            if (p.Contains("rocky ice")) return "RIB";
            if (p.Contains("rocky")) return "RBD";
            if (p.Contains("icy")) return "ICY";
            if (p.Contains("earthlike")) return "ELW";
            if (p.Contains("ammonia world")) return "AMW";
            if (p.Contains("water world")) return "WTR";
            if (p.Contains("water giant")) return "WTG";
            if (p.Contains("gas giant with water")) return "GGW";
            if (p.Contains("gas giant with ammonia")) return "GGA";
            if (p.Contains("helium")) return "GGH";
            if (p.Contains("class i gas giant")) return "GG1";
            if (p.Contains("class ii gas giant")) return "GG2";
            if (p.Contains("class iii gas giant")) return "GG3";
            if (p.Contains("class iv gas giant")) return "GG4";
            if (p.Contains("class v gas giant")) return "GG5";
            return null;
        }

        // Exact in-game spelling, including the "Metalic" typo which matches the asset filenames
        private static string? MapRingClass(string raw) => raw switch
        {
            "eRingClass_Icy"       => "Icy",
            "eRingClass_MetalRich" => "MetalRich",
            "eRingClass_Metalic"   => "Metalic",
            "eRingClass_Rocky"     => "Rocky",
            _ => null
        };

        // internal — SystemScanWindow reuses this for its own belt cards.
        internal static string FormatRingClass(string raw) => MapRingClass(raw) switch
        {
            "Icy" => "Icy",
            "MetalRich" => "Metal Rich",
            "Metalic" => "Metallic",
            "Rocky" => "Rocky",
            _ => "Unknown"
        };

        private static string FormatAtmosphere(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return "None";
            var sb = new System.Text.StringBuilder();
            foreach (var c in raw)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // The real raw Volcanism string always ends with the redundant word "volcanism"
        // itself (confirmed against real scan data — e.g. "major water geysers volcanism"),
        // which read as an odd repeat under a row already labeled GEO SIGNALS. Strips just
        // that trailing word and capitalizes, so it reads as what's actually on the body
        // ("Major water geysers") rather than restating the field name.
        private static string FormatVolcanism(string raw)
        {
            var trimmed = raw.Trim();
            const string suffix = " volcanism";
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
            return trimmed.Length > 0 ? char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1) : trimmed;
        }

        // Gas giants never carry a discrete AtmosphereType (confirmed real behavior, not a
        // parsing gap) — only AtmosphereComposition, which FormatAtmosphere alone never
        // looked at, so this stat always read "None" for every gas giant regardless of how
        // thick its real atmosphere was. Falls back to FormatAtmosphere for anything that
        // genuinely does have a discrete type.
        private static string FormatGasGiantAtmosphere(BodyScanDetail detail)
        {
            if (!string.IsNullOrEmpty(detail.AtmosphereType) &&
                !detail.AtmosphereType.Equals("None", StringComparison.OrdinalIgnoreCase))
                return FormatAtmosphere(detail.AtmosphereType);
            if (detail.AtmosphereComposition.Count == 0) return "None";
            var top = detail.AtmosphereComposition.OrderByDescending(g => g.Percent).Take(2).Select(g => g.Name);
            return string.Join(" · ", top);
        }

        // "Sudarsky class I gas giant" -> "Class I". The water/ammonia-life, helium-rich and
        // water giant variants don't carry a Sudarsky number, so they show their distinguishing
        // trait here instead — the bottom label (FormatGasGiantType) covers the broad category.
        private static string FormatGasGiantClass(string planetClass, string? iconCode)
        {
            var m = System.Text.RegularExpressions.Regex.Match(planetClass, @"class\s+(I{1,3}|IV|V)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return $"Class {m.Groups[1].Value.ToUpperInvariant()}";
            return iconCode switch
            {
                "GGW" => "Water-Based Life",
                "GGA" => "Ammonia-Based Life",
                "GGH" => "Helium-Rich",
                "WTG" => "Water Giant",
                _ => "Gas Giant"
            };
        }

        private static string FormatGasGiantType(string? iconCode) =>
            iconCode == "WTG" ? "Water Giant" : "Gas Giant";

        private void UpdatePips(EliteStatus status, List<ScannedOrganism> organisms)
        {
            string? targetGenus = _activeGenus;

            // Only try to find nearest genus by distance if we have live position
            // and no active genus is already set
            if (string.IsNullOrEmpty(targetGenus) && status.HasPosition && organisms.Count > 0)
            {
                double minD = double.MaxValue;
                lock (organisms)
                    foreach (var o in organisms.Where(o => !o.IsComplete))
                    {
                        var d = EliteWatcherService.DistanceMeters(
                            status.Latitude, status.Longitude,
                            o.Latitude, o.Longitude, status.PlanetRadius);
                        if (d < minD) { minD = d; targetGenus = o.Genus; }
                    }
            }

            // If still no target, find any incomplete genus from organisms
            if (string.IsNullOrEmpty(targetGenus))
            {
                lock (organisms)
                    targetGenus = organisms.FirstOrDefault(o => !o.IsComplete)?.Genus;
            }

            int sc = 0;
            if (!string.IsNullOrEmpty(targetGenus))
            {
                // Count only non-complete dots for pip display
                lock (organisms)
                    sc = organisms.Count(o =>
                        string.Equals(o.Genus, targetGenus, StringComparison.OrdinalIgnoreCase)
                        && !o.IsComplete);

                // If all dots complete, clear active genus and pips
                bool genusComplete = false;
                lock (organisms)
                    genusComplete = organisms.Any(o =>
                        string.Equals(o.Genus, targetGenus, StringComparison.OrdinalIgnoreCase))
                        && organisms.Where(o =>
                        string.Equals(o.Genus, targetGenus, StringComparison.OrdinalIgnoreCase))
                        .All(o => o.IsComplete);

                if (genusComplete) { _activeGenus = null; sc = 0; }
                else if (_activeGenus == null) _activeGenus = targetGenus;
            }

            pip1Fill.Fill          = sc >= 1 ? PipFill1 : PipEmptyFill1;
            pip1Border.BorderBrush = sc >= 1 ? PipFill1 : PipEmptyBorder1;
            pip1Border.Background  = sc >= 1 ? new SolidColorBrush(Color.FromRgb(0x00, 0x08, 0x15)) : PipEmptyFill1;

            pip2Fill.Fill          = sc >= 2 ? PipFill2 : PipEmptyFill2;
            pip2Border.BorderBrush = sc >= 2 ? PipFill2 : PipEmptyBorder2;
            pip2Border.Background  = sc >= 2 ? new SolidColorBrush(Color.FromRgb(0x08, 0x15, 0x08)) : PipEmptyFill2;

            pip3Fill.Fill          = sc >= 3 ? PipFill3 : PipEmptyFill3;
            pip3Border.BorderBrush = sc >= 3 ? PipFill3 : PipEmptyBorder3;
            pip3Border.Background  = sc >= 3 ? new SolidColorBrush(Color.FromRgb(0x15, 0x08, 0x00)) : PipEmptyFill3;
        }

        // ---------------------------------------------------------------
        private void UpdateBioCounter()
        {
            if (_watcher == null) return;

            var completedFromSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_watcher.CompletedGenera)
                foreach (var o in _watcher.CompletedGenera)
                    completedFromSession.Add(o.Genus);

            lock (_watcher.ScannedOrganisms)
            {
                var genera = _watcher.ScannedOrganisms
                    .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.All(o => o.IsComplete))
                    .Select(g => g.Key);
                foreach (var g in genera)
                    completedFromSession.Add(g);
            }

            int total = !string.IsNullOrEmpty(_watcher.CurrentBody)
                ? _watcher.BiologyCount
                : _watcher.TargetedBodyBioCount;

            txtBioScanned.Text = completedFromSession.Count.ToString();
            txtBioCount.Text   = total.ToString();

            // Geo counter — show only when geo signals exist
            int geoTotal = _watcher.GeologyCount;
            int geoFound = 0;
            lock (_watcher.KnownGeoSites)
                geoFound = _watcher.KnownGeoSites.Select(g => g.EntryID).Distinct().Count();

            if (geoCountPanel != null)
            {
                geoCountPanel.Visibility = geoTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (txtGeoScanned != null) txtGeoScanned.Text = geoFound.ToString();
                if (txtGeoCount   != null) txtGeoCount.Text   = geoTotal.ToString();
            }
        }

        // ---------------------------------------------------------------
        // Sidebar: shows ALL biology slots — known scanned ones with pips,
        // unknown remaining slots as "? Unknown" placeholders
        private void UpdateSidebar()
        {
            if (!_showSidebar) return;

            sidebarStack.Children.Clear();

            // Always show header
            sidebarStack.Children.Add(new TextBlock
            {
                Text       = AppFonts.TrackLight("BIO SURVEY"),
                Foreground = InfoValueBrush,
                FontFamily = AppFonts.Mono,
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
                Margin     = new Thickness(0, 0, 0, 2),
            });

            if (_watcher == null) return;

            // First footfall indicator
            bool ff = _watcher.WasFootfalled;
            var ffPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            ffPanel.Children.Add(new TextBlock
            {
                Text       = AppFonts.TrackLight(ff ? "✓ First Footfall" : "○ First Footfall"),
                Foreground = ff
                    ? new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                FontFamily = AppFonts.Mono,
                FontSize   = 12,
                FontWeight = ff ? FontWeights.Bold : FontWeights.Normal,
            });
            sidebarStack.Children.Add(ffPanel);

            var organisms = _watcher.ScannedOrganisms;
            var status    = _watcher.CurrentStatus;
            int totalBio  = _watcher.BiologyCount;

            List<ScannedOrganism> snap;
            List<string> knownGenera;
            List<ScannedOrganism> completed;
            lock (organisms)            snap        = organisms.ToList();
            lock (_watcher.KnownGenera) knownGenera = _watcher.KnownGenera.ToList();
            lock (_watcher.CompletedGenera) completed = _watcher.CompletedGenera.ToList();

            // Build the display list:
            // 1. Known genera from DSS scan (SAASignalsFound Genuses) — authoritative names
            // 2. Fall back to scanned organisms if no DSS data
            // 3. Fill remaining slots with unknowns up to BiologyCount

            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            long sidebarTotal = 0;

            // Show all known genera from DSS first
            foreach (var genus in knownGenera)
            {
                shown.Add(genus);
                var scanned = snap.FirstOrDefault(o =>
                    string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase));
                var completedOrg = completed.FirstOrDefault(o =>
                    string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase));

                bool isActive = string.Equals(genus, _activeGenus, StringComparison.OrdinalIgnoreCase);
                int  dotCount = snap.Count(o => string.Equals(o.Genus, genus, StringComparison.OrdinalIgnoreCase));
                bool isDone   = completedOrg != null;
                var  species  = scanned?.Species ?? completedOrg?.Species ?? "";
                var  fullName = !string.IsNullOrEmpty(DedupSpecies(genus, species)) ? $"{genus} {DedupSpecies(genus, species)}".Trim() : genus;
                var  payout   = PayoutData.GetValue(fullName, ff);
                // Only add to total once the organism is fully scanned
                if (isDone && payout > 0) sidebarTotal += payout;

                var nameColor = isDone ? Color.FromRgb(0x00, 0x99, 0xaa) :
                    dotCount == 0 ? Color.FromRgb(0x44, 0x88, 0x88) :
                    dotCount == 1 ? Color.FromRgb(0x44, 0xaa, 0xff) :
                    dotCount == 2 ? Color.FromRgb(0x00, 0xff, 0x44) :
                                    Color.FromRgb(0xff, 0xaa, 0x00);

                sidebarStack.Children.Add(MakeSidebarEntry(
                    genus, species, isDone ? 3 : dotCount, nameColor, isActive, payout, ff));
            }

            // Any scanned organisms not in the known genera list
            foreach (var org in snap.Where(o => !shown.Contains(o.Genus)))
            {
                shown.Add(org.Genus);
                bool isActive  = string.Equals(org.Genus, _activeGenus, StringComparison.OrdinalIgnoreCase);
                int  dotCount  = snap.Count(o => string.Equals(o.Genus, org.Genus, StringComparison.OrdinalIgnoreCase));
                bool isDoneOrg = completed.Any(c => string.Equals(c.Genus, org.Genus, StringComparison.OrdinalIgnoreCase));
                var  fullName  = !string.IsNullOrEmpty(DedupSpecies(org.Genus, org.Species)) ? $"{org.Genus} {DedupSpecies(org.Genus, org.Species)}".Trim() : org.Genus;
                var  payout    = PayoutData.GetValue(fullName, ff);
                // Only add to total once the organism is fully scanned
                if (isDoneOrg && payout > 0) sidebarTotal += payout;

                var nameColor = dotCount switch
                {
                    1 => Color.FromRgb(0x44, 0xaa, 0xff),
                    2 => Color.FromRgb(0x00, 0xff, 0x44),
                    _ => Color.FromRgb(0xff, 0xaa, 0x00),
                };
                sidebarStack.Children.Add(MakeSidebarEntry(
                    org.Genus, org.Species, dotCount, nameColor, isActive, payout, ff));
            }

            // Remaining unknown slots
            int unknownCount = Math.Max(0, totalBio - shown.Count);
            for (int i = 0; i < unknownCount; i++)
                sidebarStack.Children.Add(MakeSidebarEntry(
                    "?", "Unknown", 0, Color.FromRgb(0x44, 0x66, 0x66), false, 0));

            // Completed genera at bottom (any that weren't already listed via knownGenera)
            foreach (var comp in completed.Where(c => !shown.Contains(c.Genus)))
            {
                shown.Add(comp.Genus);
                var fullName = !string.IsNullOrEmpty(DedupSpecies(comp.Genus, comp.Species)) ? $"{comp.Genus} {DedupSpecies(comp.Genus, comp.Species)}".Trim() : comp.Genus;
                var payout   = PayoutData.GetValue(fullName, ff);
                if (payout > 0) sidebarTotal += payout;
                sidebarStack.Children.Add(MakeSidebarEntry(
                    comp.Genus, comp.Species, 3, Color.FromRgb(0x00, 0x99, 0xaa), false, payout, ff));
            }

            // Total payout at bottom of sidebar
            if (sidebarTotal > 0)
            {
                sidebarStack.Children.Add(new Border
                {
                    BorderBrush     = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xe5, 0xff)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin          = new Thickness(0, 8, 0, 4),
                });
                sidebarStack.Children.Add(new TextBlock
                {
                    Text       = AppFonts.TrackLight("Total Payout:"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xbb, 0xbb)),
                    FontFamily = AppFonts.Mono,
                    FontSize   = 12,
                    Margin     = new Thickness(0, 2, 0, 0),
                });
                sidebarStack.Children.Add(new TextBlock
                {
                    Text       = AppFonts.TrackLight(PayoutData.FormatCredits(sidebarTotal)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                    FontFamily = AppFonts.Mono,
                    FontSize   = 14,
                    FontWeight = FontWeights.Bold,
                });
            }

            if (totalBio == 0 && snap.Count == 0 && knownGenera.Count == 0)
            {
                string msg = string.IsNullOrEmpty(_watcher.CurrentBody)
                    ? "Not near a planet" : "No bio signals detected";
                sidebarStack.Children.Add(new TextBlock
                {
                    Text       = AppFonts.TrackLight(msg),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x66)),
                    FontFamily = AppFonts.Mono,
                    FontSize   = 13,
                });
            }

            // Geological survey section — only shown when setting is enabled
            if (_showGeo)
            {
            List<ScannedGeoSite> geoSnap;
            lock (_watcher.KnownGeoSites) geoSnap = _watcher.KnownGeoSites.ToList();
            int totalGeo = _watcher.GeologyCount;

            if (totalGeo > 0 || geoSnap.Count > 0)
            {
                // Spacer — matches the gap used in the planet panel
                sidebarStack.Children.Add(new Border { Height = 16 });
                sidebarStack.Children.Add(new Border
                {
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x33, 0x00)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin          = new Thickness(0, 0, 0, 8),
                });

                sidebarStack.Children.Add(new TextBlock
                {
                    Text       = AppFonts.TrackLight("GEO SURVEY"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                    FontFamily = AppFonts.Mono,
                    FontSize   = 13,
                    FontWeight = FontWeights.Bold,
                    Margin     = new Thickness(0, 0, 0, 6),
                });

                // Known geo sites
                foreach (var site in geoSnap.GroupBy(g => g.EntryID).Select(g => g.First()))
                {
                    var geoPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

                    // Site name — clickable wiki link
                    var nameTb = new TextBlock
                    {
                        FontFamily   = AppFonts.Mono,
                        FontSize     = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Cursor       = System.Windows.Input.Cursors.Hand,
                        Margin       = new Thickness(0, 0, 0, 2),
                    };
                    nameTb.Inlines.Add(new System.Windows.Documents.Run(AppFonts.TrackMinimal(site.Name))
                    {
                        Foreground      = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                        TextDecorations = TextDecorations.Underline,
                    });
                    var capturedUrl = site.WikiUrl;
                    nameTb.MouseLeftButtonUp += (_, __) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(capturedUrl) { UseShellExecute = true }); } catch { }
                    };
                    geoPanel.Children.Add(nameTb);

                    // Payout
                    if (site.Payout > 0)
                        geoPanel.Children.Add(new TextBlock
                        {
                            Text       = AppFonts.TrackMinimal($"Payout: {PayoutData.FormatCredits(site.Payout)}"),
                            Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                            FontFamily = AppFonts.Mono,
                            FontSize   = 12,
                        });

                    sidebarStack.Children.Add(geoPanel);
                }

                // Unknown slots for unscanned geo sites
                int knownGeoCount = geoSnap.Select(g => g.EntryID).Distinct().Count();
                for (int u = knownGeoCount; u < totalGeo; u++)
                {
                    sidebarStack.Children.Add(new TextBlock
                    {
                        Text       = AppFonts.TrackMinimal("? Unknown"),
                        Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xff, 0xaa, 0x00)),
                        FontFamily = AppFonts.Mono,
                        FontSize   = 13,
                        Margin     = new Thickness(0, 4, 0, 4),
                    });
                }
            }
            } // end if (_showGeo)
        }

        // Single-species genera (e.g. "Bark Mounds" / "Bark Mounds") carry the genus name as their species - older saved
        // scans still hold it that way, so drop the repeat wherever a name is shown.
        private static string DedupSpecies(string genus, string? species) =>
            string.IsNullOrEmpty(species) || string.Equals(species, genus, StringComparison.OrdinalIgnoreCase) ? "" : species;

        private UIElement MakeSidebarEntry(string genus, string species,
                                           int scanCount, Color nameColor, bool isActive, long payout = 0, bool ff = false)
        {
            species = DedupSpecies(genus, species);
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Wiki URL uses genus name only
            var wikiUrl = $"https://elite-dangerous.fandom.com/wiki/{genus.Replace(" ", "_")}";
            bool hasWiki = genus != "?";

            // Genus + Species on one line — genus underlined/clickable, species plain
            var genusLine = new TextBlock
            {
                FontFamily   = AppFonts.Mono,
                FontSize     = 13,
                FontWeight   = isActive ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Cursor       = hasWiki ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Margin       = new Thickness(0, 0, 0, 1),
            };

            var genusRun = new System.Windows.Documents.Run(AppFonts.TrackMinimal(genus))
            {
                Foreground      = new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                TextDecorations = hasWiki ? TextDecorations.Underline : null,
            };
            genusLine.Inlines.Add(genusRun);

            if (!string.IsNullOrEmpty(species) && species != "Unknown")
            {
                genusLine.Inlines.Add(new System.Windows.Documents.Run(AppFonts.TrackMinimal($" {species}"))
                {
                    Foreground      = new SolidColorBrush(Color.FromArgb(0xcc, 0x00, 0xe5, 0xff)),
                    TextDecorations = null,
                });
            }

            if (hasWiki)
                genusLine.MouseLeftButtonUp += (_, __) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wikiUrl) { UseShellExecute = true }); }
                    catch { }
                };
            panel.Children.Add(genusLine);

            // Payout — show FF Payout if first footfall, otherwise Payout
            if (payout > 0)
                panel.Children.Add(new TextBlock
                {
                    Text       = AppFonts.TrackMinimal($"  {(ff ? "FF Payout:" : "Payout:")} {PayoutData.FormatCredits(payout)}"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                    FontFamily = AppFonts.Mono,
                    FontSize   = 11,
                });

            // Pips — same Border/Ellipse style as bottom bar
            var pipRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(2, 5, 0, 0),
            };

            Color[] pipOn  = { Color.FromRgb(0x44, 0xaa, 0xff), Color.FromRgb(0x00, 0xff, 0x44), Color.FromRgb(0xff, 0xaa, 0x00) };
            Color[] pipBg  = { Color.FromRgb(0x00, 0x08, 0x15), Color.FromRgb(0x08, 0x15, 0x08), Color.FromRgb(0x15, 0x08, 0x00) };
            Color[] pipBdr = { Color.FromRgb(0x22, 0x44, 0x55), Color.FromRgb(0x22, 0x55, 0x44), Color.FromRgb(0x55, 0x44, 0x22) };

            for (int i = 0; i < 3; i++)
            {
                bool filled = scanCount > i;
                // Outer border ring (bright when filled, dim when empty)
                // Background = dark gap ring
                // Inner ellipse = bright centre dot (when filled)
                pipRow.Children.Add(new Border
                {
                    Width           = 16,
                    Height          = 16,
                    CornerRadius    = new CornerRadius(8),
                    BorderThickness = new Thickness(2),
                    BorderBrush     = new SolidColorBrush(filled ? pipOn[i] : pipBdr[i]),
                    Background      = new SolidColorBrush(filled ? pipBg[i] : pipBg[i]),
                    Margin          = new Thickness(1, 0, 3, 0),
                    Child = new Ellipse
                    {
                        Width  = 7,
                        Height = 7,
                        Fill   = new SolidColorBrush(filled ? pipOn[i] : pipBg[i]),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                    },
                });
            }
            panel.Children.Add(pipRow);

            panel.Children.Add(new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xe5, 0xff)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin          = new Thickness(0, 5, 0, 0),
            });

            return panel;
        }

        // ---------------------------------------------------------------
        private void UpdateStatusBar(EliteStatus status)
        {
            txtHeading.Text = $"{status.Heading:F0}°";
            if (status.HasPosition)
            {
                txtLat.Text = $"{status.Latitude:F4}°";
                txtLon.Text = $"{status.Longitude:F4}°";
                txtAlt.Text = status.Altitude < 1000
                    ? $"{status.Altitude:F0}m" : $"{status.Altitude / 1000:F2}km";
            }
            else
            {
                txtLat.Text = "—"; txtLon.Text = "—"; txtAlt.Text = "—";
            }

            if (_watcher == null) return;

            txtSystemName.Text = string.IsNullOrEmpty(_watcher.StarSystem) ? "—" : _watcher.StarSystem;

            // Show current body if on planet, otherwise show targeted body
            if (!string.IsNullOrEmpty(_watcher.CurrentBody))
            {
                txtBodyName.Text = _watcher.CurrentBody;
            }
            else if (!string.IsNullOrEmpty(_watcher.TargetedBody))
            {
                txtBodyName.Text = _watcher.TargetedBody;
            }
            else if (!string.IsNullOrEmpty(status.BodyName))
            {
                txtBodyName.Text = status.BodyName;
            }

            UpdateBioCounter();
        }

        private void UpdateBodyInfo(BodyChangedEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.BodyName))
            {
                txtBodyName.Text   = args.BodyName;
                txtBioCount.Text   = args.BioCount.ToString();
                txtBioScanned.Text = "0";
            }
            else
            {
                // Body cleared — left planet or jumped
                txtBodyName.Text   = "—";
                txtBioCount.Text   = "0";
                txtBioScanned.Text = "0";
            }
            _activeGenus = null;
            UpdateSidebar();
            UpdatePlanetPanel();
        }

        // ---------------------------------------------------------------
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
            => settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;

        // Reached by clicking the "EDSM upload enabled"/"EDSM integration off" chip on the
        // System Scan window (a separate top-level Window, owned by this one) — opens Settings
        // here and scrolls/focuses straight to the EDSM section instead of leaving the player to
        // find it themselves, and brings this window to the front since the click happened on a
        // different window entirely.
        public void OpenEdsmSettings()
        {
            settingsPanel.Visibility = Visibility.Visible;
            Activate();
            txtEdsmCommander.BringIntoView();
            if (chkEdsm.IsChecked != true) chkEdsm.Focus();
            else txtEdsmCommander.Focus();
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        private ScanLogWindow? _scanLogWindow;

        private void BtnScanLog_Click(object sender, RoutedEventArgs e) => OpenScanLogWindow();

        // Pulled out of BtnScanLog_Click so app-startup reopen (MainWindow_Loaded's
        // "WasOpen" check) can share the exact same open/close-tracking logic as a manual
        // toolbar click, instead of duplicating it.
        private void OpenScanLogWindow()
        {
            Log.Write("OpenScanLogWindow called");
            if (_scanLogWindow != null)
            {
                _scanLogWindow.Activate();
                return;
            }

            try
            {
                _scanLogWindow = new ScanLogWindow { Owner = this };
                _scanLogWindow.Closed += (_, _) =>
                {
                    _scanLogWindow = null;
                    // Skip clearing the flag when the app itself is shutting down (see
                    // _appShuttingDown's own comment) — leaving it true is what makes this
                    // window reopen automatically next launch.
                    if (_appShuttingDown) return;
                    // Own-field load-then-save (not the big SaveSettings()) — same reasoning as
                    // that method's own rewrite: never clobber unrelated settings just because
                    // this one flag changed.
                    var s = AppSettings.Load();
                    s.ScanLogWasOpen = false;
                    AppSettings.Save(s);
                };
                _scanLogWindow.Show();
                Log.Write("OpenScanLogWindow: Show() returned");
            }
            catch (Exception ex)
            {
                Log.Write($"OpenScanLogWindow EXCEPTION: {ex}");
                _scanLogWindow = null;
                return;
            }

            var settings = AppSettings.Load();
            settings.ScanLogWasOpen = true;
            AppSettings.Save(settings);
        }

        private NearestFinderWindow? _starFinderWindow;

        private void BtnStarFinder_Click(object sender, RoutedEventArgs e)
        {
            if (_watcher == null) return;
            if (_starFinderWindow != null) { _starFinderWindow.Activate(); return; }

            try
            {
                _starFinderWindow = new NearestFinderWindow(_watcher) { Owner = this };
                _starFinderWindow.Closed += (_, __) => _starFinderWindow = null;
                _starFinderWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Write($"BtnStarFinder_Click EXCEPTION: {ex}");
                _starFinderWindow = null;
            }
        }

        private SystemScanWindow? _systemScanWindow;

        private void BtnSystemScan_Click(object sender, RoutedEventArgs e) => OpenSystemScanWindow();

        private void OpenSystemScanWindow()
        {
            Log.Write("OpenSystemScanWindow called");
            if (_watcher == null) { Log.Write("OpenSystemScanWindow: _watcher is null, aborting"); return; }
            if (_systemScanWindow != null)
            {
                _systemScanWindow.Activate();
                return;
            }

            try
            {
                _systemScanWindow = new SystemScanWindow(_watcher) { Owner = this };
                _systemScanWindow.Closed += (_, _) =>
                {
                    _systemScanWindow = null;
                    if (_appShuttingDown) return;
                    var s = AppSettings.Load();
                    s.SystemScanWasOpen = false;
                    AppSettings.Save(s);
                };
                _systemScanWindow.Show();
                Log.Write("OpenSystemScanWindow: Show() returned");
            }
            catch (Exception ex)
            {
                // No DispatcherUnhandledException handler exists app-wide, so letting this
                // escape uncaught (this can run from the startup reopen path, not just a
                // button click) would take the whole app down with it — worth a real try/catch
                // here specifically, unlike the button-click path where a crash would at least
                // be obviously tied to the click that caused it.
                Log.Write($"OpenSystemScanWindow EXCEPTION: {ex}");
                _systemScanWindow = null;
                return;
            }

            var settings = AppSettings.Load();
            settings.SystemScanWasOpen = true;
            AppSettings.Save(settings);
        }

        private void BtnSettingsClose_Click(object sender, RoutedEventArgs e)
            => settingsPanel.Visibility = Visibility.Collapsed;

        private void UpdateEarningsDisplay()
        {
            if (txtTotalEarned != null)
                txtTotalEarned.Text = $"Total: {PayoutData.FormatCredits(EarningsTracker.TotalEarned)}";
            if (txtSessionEarned != null)
                txtSessionEarned.Text = EarningsTracker.TotalEarned > 0
                    ? PayoutData.FormatCredits(EarningsTracker.TotalEarned)
                    : "—";
        }

        private long GetSpeciesPayout(string genus, string species, bool firstFootfall)
        {
            var fullName = !string.IsNullOrEmpty(species)
                ? $"{genus} {species}".Trim()
                : genus;
            return PayoutData.GetValue(fullName, firstFootfall);
        }

        private void UpdatePotentialPayout()
        {
            if (_watcher == null || txtPotentialPayout == null) return;
            bool ff = _watcher.WasFootfalled;
            long total = 0;

            // Sum payout for all known genera on this planet
            List<string> genera;
            lock (_watcher.KnownGenera) genera = _watcher.KnownGenera.ToList();

            if (genera.Count > 0)
            {
                foreach (var g in genera)
                {
                    // Try to find species name from scanned organisms
                    ScannedOrganism? org = null;
                    lock (_watcher.ScannedOrganisms)
                        org = _watcher.ScannedOrganisms.FirstOrDefault(o =>
                            string.Equals(o.Genus, g, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(o.Species));
                    lock (_watcher.CompletedGenera)
                        org ??= _watcher.CompletedGenera.FirstOrDefault(o =>
                            string.Equals(o.Genus, g, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(o.Species));

                    var name = org != null ? $"{org.Genus} {org.Species}".Trim() : g;
                    total += PayoutData.GetValue(name, ff);
                }
            }

            txtPotentialPayout.Text = total > 0 ? PayoutData.FormatCredits(total) : "—";
        }

        private void BtnScanJournals_Click(object sender, RoutedEventArgs e)
            => RunJournalScan(null, null);

        private void BtnScanJournalsRange_Click(object sender, RoutedEventArgs e)
        {
            var from = dateFrom.SelectedDate;
            var to   = dateTo.SelectedDate?.AddDays(1);  // include the full end day
            if (from == null && to == null)
            {
                RunJournalScan(null, null);
                return;
            }
            RunJournalScan(from, to);
        }

        private void RunJournalScan(DateTime? from, DateTime? to)
        {
            btnScanJournals.IsEnabled      = false;
            btnScanJournalsRange.IsEnabled = false;
            btnScanJournals.Content        = "Scanning...";
            btnScanJournalsRange.Content   = "Scanning...";

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var journalDir = EliteWatcherService.GetJournalDirectory();
                    var allFiles   = System.IO.Directory.GetFiles(journalDir, "Journal.*.log")
                        .OrderByJournalDate().ToArray();

                    // Filter by date range if specified — JournalFileUtil.SortKey handles both
                    // the old (YYMMDDHHMMSS) and new (yyyy-MM-ddTHHmmss) filename formats; the
                    // old substring-based parse here only ever understood the new one, so a
                    // range filter silently included every old-format file regardless of the
                    // requested range instead of actually filtering it.
                    var files = allFiles.Where(f =>
                    {
                        if (from == null && to == null) return true;
                        var fileDate = JournalFileUtil.SortKey(f).Date;
                        if (from != null && fileDate < from.Value.Date) return false;
                        if (to   != null && fileDate > to.Value.Date)   return false;
                        return true;
                    }).ToArray();

                    long total = 0;
                    bool wasFootfalled = false;

                    foreach (var file in files)
                    {
                        foreach (var line in System.IO.File.ReadLines(file))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            Newtonsoft.Json.Linq.JObject? obj = null;
                            try { obj = Newtonsoft.Json.Linq.JObject.Parse(line); } catch { continue; }
                            var evt = obj.Value<string>("event");

                            if (evt == "Scan")
                            {
                                var wf = obj.Value<bool?>("WasFootfalled") ?? true;
                                wasFootfalled = !wf;
                            }
                            else if (evt == "ScanOrganic" && obj.Value<string>("ScanType") == "Analyse")
                            {
                                var speciesLoc = obj.Value<string>("Species_Localised") ?? "";
                                var genusLoc   = obj.Value<string>("Genus_Localised")   ?? "";
                                var name       = !string.IsNullOrEmpty(speciesLoc) ? speciesLoc : genusLoc;
                                total += PayoutData.GetValue(name, wasFootfalled);
                            }
                            else if (evt == "FSDJump")
                            {
                                wasFootfalled = false;
                            }
                        }
                    }

                    EarningsTracker.Clear();
                    if (total > 0) EarningsTracker.AddEarning(total);

                    string rangeLabel = (from != null || to != null)
                        ? $"{from?.ToString("yyyy-MM-dd") ?? "start"} → {to?.AddDays(-1).ToString("yyyy-MM-dd") ?? "now"}"
                        : "all journals";

                    Log.Write($"Journal scan complete ({rangeLabel}): {PayoutData.FormatCredits(total)} from {files.Length} files");

                    Dispatcher.InvokeAsync(() =>
                    {
                        UpdateEarningsDisplay();
                        btnScanJournals.IsEnabled      = true;
                        btnScanJournalsRange.IsEnabled = true;
                        btnScanJournals.Content        = "Scan All Journals";
                        btnScanJournalsRange.Content   = "Scan Date Range";
                    });
                }
                catch (Exception ex)
                {
                    Log.Write($"RunJournalScan error: {ex.Message}");
                    Dispatcher.InvokeAsync(() =>
                    {
                        btnScanJournals.IsEnabled      = true;
                        btnScanJournalsRange.IsEnabled = true;
                        btnScanJournals.Content        = "Scan All Journals";
                        btnScanJournalsRange.Content   = "Scan Date Range";
                    });
                }
            });
        }

        private void BtnClearEarnings_Click(object sender, RoutedEventArgs e)
        {
            EarningsTracker.Clear();
            UpdateEarningsDisplay();
        }

        private void ChkBioSites_Changed(object sender, RoutedEventArgs e)
        {
            _showBioSites = chkBioSites.IsChecked == true;
            planetCol.Width = _showBioSites ? new GridLength(150) : new GridLength(0);
            planetPanel.Visibility = _showBioSites ? Visibility.Visible : Visibility.Collapsed;
            if (_settingsInitializing) return;
            SaveSettings();
            if (_showBioSites) UpdatePlanetPanel();
        }

        private void ChkRadarAnimation_Changed(object sender, RoutedEventArgs e)
        {
            _radarAnimation = chkRadarAnimation.IsChecked == true;
            SaveSettings();
        }

        private void ChkShowGeo_Changed(object sender, RoutedEventArgs e)
        {
            _showGeo = chkShowGeo.IsChecked == true;
            if (_settingsInitializing) return;
            SaveSettings();
            UpdatePlanetPanel();
            UpdateSidebar();
        }

        private void ChkEdsm_Changed(object sender, RoutedEventArgs e)
        {
            _edsmEnabled = chkEdsm.IsChecked == true;
            UpdateEdsmStatus();
            if (_settingsInitializing) return;
            SaveSettings();
        }

        private void TxtEdsmCommander_LostFocus(object sender, RoutedEventArgs e)
        {
            _edsmCommander = txtEdsmCommander.Text.Trim();
            UpdateEdsmStatus();
            if (_settingsInitializing) return;
            SaveSettings();
        }

        private void ChkGravityWarning_Changed(object sender, RoutedEventArgs e)
        {
            _gravityWarningEnabled = chkGravityWarning.IsChecked == true;
            if (_settingsInitializing) return;
            SaveSettings();
            UpdateInfoPlanetPanel(force: true);
            _systemScanWindow?.RefreshFromSettings();
        }

        private void TxtGravityThreshold_LostFocus(object sender, RoutedEventArgs e)
        {
            // Falls back to the previous value on anything unparseable/non-positive rather than
            // silently accepting garbage (e.g. "abc" or a stray negative) as a real threshold.
            if (double.TryParse(txtGravityThreshold.Text, out var g) && g > 0)
                _gravityWarningThresholdG = g;
            txtGravityThreshold.Text = _gravityWarningThresholdG.ToString("0.##");
            if (_settingsInitializing) return;
            SaveSettings();
            UpdateInfoPlanetPanel(force: true);
            _systemScanWindow?.RefreshFromSettings();
        }

        private void ChkScreenshotConvert_Changed(object sender, RoutedEventArgs e)
        {
            _screenshotConversionEnabled = chkScreenshotConvert.IsChecked == true;
            if (_settingsInitializing) return;
            SaveSettings();
        }

        private void TxtScreenshotSource_LostFocus(object sender, RoutedEventArgs e)
        {
            _screenshotSourceFolder = txtScreenshotSource.Text.Trim();
            if (_settingsInitializing) return;
            SaveSettings();
        }

        private void TxtScreenshotDest_LostFocus(object sender, RoutedEventArgs e)
        {
            _screenshotDestFolder = txtScreenshotDest.Text.Trim();
            if (_settingsInitializing) return;
            SaveSettings();
        }

        private void BtnBrowseScreenshotSource_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the folder Elite Dangerous saves screenshots to",
                SelectedPath = Directory.Exists(_screenshotSourceFolder) ? _screenshotSourceFolder : "",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _screenshotSourceFolder = dlg.SelectedPath;
            txtScreenshotSource.Text = _screenshotSourceFolder;
            SaveSettings();
        }

        private void BtnBrowseScreenshotDest_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select where converted PNGs should be saved (leave blank in the field to use the same folder)",
                SelectedPath = Directory.Exists(_screenshotDestFolder) ? _screenshotDestFolder : "",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _screenshotDestFolder = dlg.SelectedPath;
            txtScreenshotDest.Text = _screenshotDestFolder;
            SaveSettings();
        }

        private void BtnConvertExistingScreenshots_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_screenshotSourceFolder) || !Directory.Exists(_screenshotSourceFolder))
            {
                txtScreenshotStatus.Text = "Set a valid screenshots folder first.";
                return;
            }
            var source = _screenshotSourceFolder;
            var dest = _screenshotDestFolder;
            txtScreenshotStatus.Text = "Converting…";
            Task.Run(() => ScreenshotConverterService.ConvertAllInFolder(source, dest)).ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    var (converted, failed) = t.Result;
                    txtScreenshotStatus.Text = failed > 0
                        ? $"Converted {converted}, {failed} failed (see log)."
                        : converted > 0 ? $"Converted {converted} screenshot(s)." : "No .bmp files found.";
                });
            });
        }

        private void TxtEdsmApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _edsmApiKey = txtEdsmApiKey.Password;
            UpdateEdsmStatus();
            if (_settingsInitializing) return;
            SaveSettings();
        }

        // Brief transient notification (e.g. "Screenshot converted: ...png") — fades in, holds,
        // fades out, entirely via one KeyFrame animation on Opacity. Calling this again while one
        // is still showing (BeginAnimation on the same property) cleanly replaces it rather than
        // stacking or fighting the previous animation.
        private void ShowToast(string message)
        {
            txtToast.Text = message;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.7))));
            toastPanel.BeginAnimation(OpacityProperty, anim);
        }

        // Small inline status line under the API key field — surfaces the one thing most likely
        // to trip someone up (enabling the toggle without actually filling in commander name/key
        // yet, which silently means uploads never fire) without needing a whole dialog for it.
        private void UpdateEdsmStatus()
        {
            if (!_edsmEnabled)
            {
                txtEdsmStatus.Text = "";
                return;
            }
            txtEdsmStatus.Text = string.IsNullOrWhiteSpace(_edsmCommander) || string.IsNullOrWhiteSpace(_edsmApiKey)
                ? "Enabled, but Commander Name and API Key are required for uploads to actually send."
                : "Enabled — scans upload to EDSM live as you play.";
        }

        // Manual trigger for BackfillEdsmAsync — the same catch-up that also runs quietly once
        // per app start, but with visible progress/completion feedback, for "I want this to
        // happen right now" (e.g. right after fixing a sync problem, without restarting).
        private async void BtnEdsmSync_Click(object sender, RoutedEventArgs e)
        {
            if (_watcher == null) return;
            btnEdsmSync.IsEnabled = false;
            var originalContent = btnEdsmSync.Content;
            btnEdsmSync.Content = "Syncing…";
            txtEdsmSyncStatus.Text = "";
            try
            {
                var progress = new Progress<(int done, int total)>(p =>
                    txtEdsmSyncStatus.Text = $"Syncing {p.done} / {p.total} events…");
                var (sent, total) = await _watcher.BackfillEdsmAsync(progress, System.Threading.CancellationToken.None);
                txtEdsmSyncStatus.Text = total == 0
                    ? "Nothing to sync — already caught up, or EDSM isn't fully configured above."
                    : $"Synced {sent} / {total} events to EDSM.";
            }
            catch (Exception ex)
            {
                Log.Write($"BtnEdsmSync_Click error: {ex.Message}");
                txtEdsmSyncStatus.Text = "Sync failed — see EliteBioRadar.log for details.";
            }
            finally
            {
                btnEdsmSync.IsEnabled = true;
                btnEdsmSync.Content = originalContent;
            }
        }

        // Real body order for a short designation like "10 B", "2", or a secondary-star body
        // like "B 6" / "B 1 a" (GetShortBodyName only strips the STAR SYSTEM prefix, so a body
        // under a secondary star keeps its own star letter ahead of the planet number — confirmed
        // against real journal data: "Hypo Phla TO-Z c13-0 B 6"). Used to sort the BIO/GEO/MINING
        // SITES sidebar the same way the game numbers bodies, in place of a plain alphabetical
        // string sort (which put "10 B" before "2" — real report: mining sites listed 1, 10 B,
        // 10 C, 10 D, 11, 11 A, 2, 3, 7, 8, 9).
        // The planet number is the first token that parses as an integer, wherever it falls —
        // matching SystemScanWindow.BodyOrderKey's already-correct approach — NOT just the first
        // token (which would treat "B" as unparseable and dump every secondary-star body at the
        // end regardless of its real number). A token immediately after the number is a moon
        // letter (base-26, so multi-letter designations still order correctly); a token BEFORE
        // it (the star letter) is never misread as one. Anything unparseable sorts last rather
        // than crashing.
        private static (int planetNum, int moonLetter) ShortNameOrderKey(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return (int.MaxValue, int.MaxValue);
            var parts = shortName.Split(' ');
            int planetNum = int.MaxValue, numIdx = -1;
            for (int i = 0; i < parts.Length; i++)
                if (int.TryParse(parts[i], out var n)) { planetNum = n; numIdx = i; break; }

            int moonLetter = 0;
            if (numIdx >= 0 && numIdx == parts.Length - 2)
            {
                foreach (var ch in parts[^1].ToLowerInvariant())
                {
                    if (ch < 'a' || ch > 'z') { moonLetter = int.MaxValue; break; }
                    moonLetter = moonLetter * 26 + (ch - 'a' + 1);
                }
            }
            return (planetNum, moonLetter);
        }

        private void UpdatePlanetPanel()
        {
            if (!_showBioSites || _watcher == null) return;

            Dispatcher.InvokeAsync(() =>
            {
                planetStack.Children.Clear();

                planetStack.Children.Add(new TextBlock
                {
                    Text         = AppFonts.TrackLight("BIO SITES"),
                    Foreground   = InfoValueBrush,
                    FontFamily   = AppFonts.Mono,
                    FontSize     = 13,
                    FontWeight   = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 0, 0, 6),
                });

                List<EliteWatcherService.PlanetBioInfo> planets;
                planets = _watcher.SystemBioPlanets
                    .OrderBy(p => ShortNameOrderKey(p.ShortName))
                    .ToList();

                if (planets.Count == 0 && !_showGeo)
                {
                    planetStack.Children.Add(new TextBlock
                    {
                        Text         = AppFonts.TrackLight("No bio planets\nscanned yet"),
                        Foreground   = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                        FontFamily   = AppFonts.Mono,
                        FontSize     = 12,
                        TextWrapping = TextWrapping.Wrap,
                    });
                    return;
                }

                if (planets.Count == 0 && _showGeo)
                {
                    planetStack.Children.Add(new TextBlock
                    {
                        Text         = AppFonts.TrackLight("No bio planets\nscanned yet"),
                        Foreground   = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                        FontFamily   = AppFonts.Mono,
                        FontSize     = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin       = new Thickness(0, 0, 0, 4),
                    });
                }

                foreach (var planet in planets)
                {
                    bool isCurrent = string.Equals(planet.FullBodyName, _watcher.CurrentBody,
                        StringComparison.OrdinalIgnoreCase);

                    // Use live count for current body, cached count for others
                    int completedCount = planet.CompletedCount;
                    if (isCurrent)
                    {
                        lock (_watcher.ScannedOrganisms)
                            completedCount = _watcher.ScannedOrganisms
                                .GroupBy(o => o.Genus, StringComparer.OrdinalIgnoreCase)
                                .Count(g => g.All(o => o.IsComplete));
                        lock (_watcher.CompletedGenera)
                            completedCount = Math.Max(completedCount, _watcher.CompletedGenera.Count);
                    }
                    bool allDone = completedCount >= planet.BioCount;

                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                    // Fixed-width indicator — arrow for current, spaces for others
                    row.Children.Add(new TextBlock
                    {
                        Text       = isCurrent ? "▶ " : "  ",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                        FontFamily = AppFonts.Mono,
                        FontSize   = 12,
                        Width      = 18,
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    // Short body name + count in one TextBlock, same as GEO/Mining below — this
                    // used to be two separate TextBlocks with the count in its own dimmer,
                    // never-bold color, which is exactly why it looked inconsistent with GEO/
                    // Mining (both already combine name+count into a single run).
                    row.Children.Add(new TextBlock
                    {
                        Text       = AppFonts.TrackLight($"{planet.ShortName.ToUpper()} ({planet.BioCount})"),
                        Foreground = allDone
                            ? new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55))
                            : new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                        FontFamily = AppFonts.Mono,
                        FontSize   = 12,
                        FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    var capturedPlanet = planet;
                    row.Cursor = System.Windows.Input.Cursors.Hand;
                    row.MouseLeftButtonUp += (_, __) =>
                    {
                        _watcher.PreviewPlanet(capturedPlanet.FullBodyName);
                    };

                    planetStack.Children.Add(row);
                }

                // Geological Sites section — only shown when setting is enabled
                if (_showGeo)
                {
                    var geoPlanets = _watcher.SystemGeoPlanets
                        .OrderBy(p => ShortNameOrderKey(p.ShortName))
                        .ToList();

                    if (geoPlanets.Count > 0)
                    {
                        // Spacer between bio and geo
                        planetStack.Children.Add(new Border { Height = 16 });

                        planetStack.Children.Add(new TextBlock
                        {
                            Text         = AppFonts.TrackLight("GEO SITES"),
                            Foreground   = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                            FontFamily   = AppFonts.Mono,
                            FontSize     = 13,
                            FontWeight   = FontWeights.Bold,
                            TextWrapping = TextWrapping.Wrap,
                            Margin       = new Thickness(0, 0, 0, 6),
                        });

                        foreach (var planet in geoPlanets)
                        {
                            bool isCurrent = string.Equals(planet.FullBodyName, _watcher.CurrentBody,
                                StringComparison.OrdinalIgnoreCase);
                            bool allDone   = planet.DiscoveredCount >= planet.GeoCount && planet.GeoCount > 0;

                            var geoFg = allDone
                                ? Color.FromArgb(0x66, 0xff, 0xaa, 0x00)
                                : Color.FromRgb(0xff, 0xaa, 0x00);

                            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                            row2.Children.Add(new TextBlock
                            {
                                Text       = isCurrent ? "▶ " : "  ",
                                Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                                FontFamily = AppFonts.Mono,
                                FontSize   = 12,
                                Width      = 18,
                                VerticalAlignment = VerticalAlignment.Center,
                            });

                            row2.Children.Add(new TextBlock
                            {
                                Text       = AppFonts.TrackLight($"{planet.ShortName.ToUpper()} ({planet.GeoCount})"),
                                Foreground = new SolidColorBrush(geoFg),
                                FontFamily = AppFonts.Mono,
                                FontSize   = 12,
                                VerticalAlignment = VerticalAlignment.Center,
                            });

                            var capturedGeoPlanet = planet;
                            row2.Cursor = System.Windows.Input.Cursors.Hand;
                            row2.MouseLeftButtonUp += (_, __) =>
                            {
                                _watcher.PreviewPlanet(capturedGeoPlanet.FullBodyName);
                            };

                            planetStack.Children.Add(row2);
                        }
                    }
                }

                // Mining Sites section — always shown (no separate setting yet) whenever the
                // system has resolved any. Unlike Bio/Geo there's no "all discovered" greying:
                // no per-instance discovery event is confirmed yet for this brand-new signal
                // type (nobody's actually visited one to capture it), so this only ever shows
                // the raw orbital-scan count, same as the Planet tab's own MINING SITES callout.
                var miningPlanets = _watcher.SystemMiningPlanets
                    .OrderBy(p => ShortNameOrderKey(p.ShortName))
                    .ToList();

                if (miningPlanets.Count > 0)
                {
                    planetStack.Children.Add(new Border { Height = 16 });

                    planetStack.Children.Add(new TextBlock
                    {
                        Text         = AppFonts.TrackLight("MINING SITES"),
                        Foreground   = InfoMiningBrush,
                        FontFamily   = AppFonts.Mono,
                        FontSize     = 13,
                        FontWeight   = FontWeights.Bold,
                        TextWrapping = TextWrapping.Wrap,
                        Margin       = new Thickness(0, 0, 0, 6),
                    });

                    foreach (var planet in miningPlanets)
                    {
                        bool isCurrent = string.Equals(planet.FullBodyName, _watcher.CurrentBody,
                            StringComparison.OrdinalIgnoreCase);

                        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                        row3.Children.Add(new TextBlock
                        {
                            Text       = isCurrent ? "▶ " : "  ",
                            Foreground = InfoMiningBrush,
                            FontFamily = AppFonts.Mono,
                            FontSize   = 12,
                            Width      = 18,
                            VerticalAlignment = VerticalAlignment.Center,
                        });

                        row3.Children.Add(new TextBlock
                        {
                            Text       = AppFonts.TrackLight($"{planet.ShortName.ToUpper()} ({planet.MiningCount})"),
                            Foreground = InfoMiningBrush,
                            FontFamily = AppFonts.Mono,
                            FontSize   = 12,
                            VerticalAlignment = VerticalAlignment.Center,
                        });

                        var capturedMiningPlanet = planet;
                        row3.Cursor = System.Windows.Input.Cursors.Hand;
                        row3.MouseLeftButtonUp += (_, __) =>
                        {
                            _watcher.PreviewPlanet(capturedMiningPlanet.FullBodyName);
                        };

                        planetStack.Children.Add(row3);
                    }
                }
            });
        }

        private void SaveSettings()
        {
            if (_settingsInitializing) return;
            // Real bug this fixed: rebuilding the settings object purely from this window's own
            // tracked fields silently reset any field it doesn't track back to its default on
            // every single save — caught via LastEdsmSyncUtc (owned by BackfillEdsmAsync, not
            // this window) getting wiped back to null by the very next checkbox toggle or window
            // resize after a sync completed. Loading the current on-disk value first and only
            // overwriting the fields this window actually owns means a field added anywhere else
            // in the app survives every save here, not just the ones known about today.
            var onDisk = AppSettings.Load();
            onDisk.ShowSidebar          = _showSidebar;
            onDisk.AutoScale            = _autoScale;
            onDisk.DefaultScale         = _defaultScale;
            onDisk.KeepPlanetPanelOpen  = _showBioSites;
            onDisk.RadarAnimation       = _radarAnimation;
            onDisk.ShowGeologicalSites  = _showGeo;
            onDisk.EdsmEnabled          = _edsmEnabled;
            onDisk.EdsmCommanderName    = _edsmCommander;
            onDisk.EdsmApiKey           = _edsmApiKey;
            onDisk.GravityWarningEnabled    = _gravityWarningEnabled;
            onDisk.GravityWarningThresholdG = _gravityWarningThresholdG;
            onDisk.ScreenshotConversionEnabled = _screenshotConversionEnabled;
            onDisk.ScreenshotSourceFolder      = _screenshotSourceFolder;
            onDisk.ScreenshotDestFolder        = _screenshotDestFolder;
            onDisk.WindowLeft           = this.Left;
            onDisk.WindowTop            = this.Top;
            onDisk.WindowWidth          = this.Width;
            onDisk.WindowHeight         = this.Height;
            AppSettings.Save(onDisk);
        }

        private void ChkSidebar_Changed(object sender, RoutedEventArgs e)
        {
            _showSidebar = chkSidebar.IsChecked == true;
            UpdateSidebarVisibility();
            if (_showSidebar) UpdateSidebar();
            SaveSettings();
        }

        // Single source of truth for the right BIO SURVEY sidebar's visibility — it should
        // only ever show when both the setting is on AND we're in RADAR mode. Previously this
        // same show/hide logic was duplicated across MainWindow_Loaded, ApplyInfoPanelMode, and
        // ChkSidebar_Changed; if ChkSidebar_Changed fired (e.g. from setting chkSidebar.IsChecked
        // during startup) after ApplyInfoPanelMode had already run, its unconditional "show it"
        // logic would win and nothing would correct it again since the mode wasn't changing.
        private void UpdateSidebarVisibility()
        {
            bool show = _showSidebar && _lastMode == InfoPanelMode.Radar;
            sidebarPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            sidebarCol.Width        = show ? new GridLength(180) : new GridLength(0);
        }

        private void ChkAutoScale_Changed(object sender, RoutedEventArgs e)
        {
            _autoScale = chkAutoScale.IsChecked == true;
            if (!_autoScale) { _scaleMetres = _defaultScale; UpdateScaleLabel(); }
            SaveSettings();
        }

        private void CmbDefaultScale_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDefaultScale.SelectedItem is ComboBoxItem item &&
                double.TryParse(item.Tag?.ToString(), out double val))
            {
                _defaultScale = val;
                if (!_autoScale) { _scaleMetres = _defaultScale; UpdateScaleLabel(); }
                SaveSettings();
            }
        }

        private void UpdateScaleLabel()
        {
            if (txtScale == null) return;
            txtScale.Text = _scaleMetres >= 1000
                ? $"{_scaleMetres / 1000:F1}km" : $"{_scaleMetres:F0}m";
        }

        private void RadarCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_autoScale) return;
            double factor = e.Delta > 0 ? 0.8 : 1.25;
            _scaleMetres = Math.Clamp(_scaleMetres * factor, 100, 10000);
            UpdateScaleLabel();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_renderer == null) return;
            RefreshAll();
            SaveSettings();
        }
    }
}
