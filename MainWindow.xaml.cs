using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        private double _scaleMetres  = 1000;
        private double _defaultScale = 1000;
        private bool   _autoScale    = false;
        private bool   _radarAnimation = true;
        private bool   _showGeo        = false;
        private bool _settingsInitializing = true;
        private bool _showSidebar    = false;
        private bool _showBioSites   = false;
        private string? _activeGenus = null;
        private double _shipDepartureRangeMetres = 1975;

        // Info panel (STAR/PLANET/DESTINATION/RADAR) mode state
        private InfoPanelMode _lastMode = InfoPanelMode.Radar;
        // Set by clicking a tab; sticks until either another tab is clicked or a genuine new
        // in-game event fires (see StarScanUpdated/PlanetTargetUpdated/DestinationUpdated
        // subscriptions below), at which point automatic mode-selection takes back over.
        private InfoPanelMode? _manualMode;
        private bool _wasHasPosition;
        private BodyScanDetail? _lastRenderedStar;
        private BodyScanDetail? _lastRenderedPlanet;
        private string _lastScrolledNextSystem = "";
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
            Closed += MainWindow_Closed;
            Log.Write("Constructor done");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log.Write("Loaded event fired");
            _renderer = new RadarRenderer(radarCanvas);

            // Load persisted settings
            var saved = AppSettings.Load();
            _defaultScale  = saved.DefaultScale;
            _scaleMetres   = saved.DefaultScale;
            _autoScale     = saved.AutoScale;
            _showSidebar   = saved.ShowSidebar;
            _showBioSites  = saved.KeepPlanetPanelOpen;
            _shipDepartureRangeMetres = saved.ShipDepartureRangeMetres;

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
                svc.PlanetTargetUpdated += (_, __) => Dispatcher.InvokeAsync(() => { _manualMode = null; RefreshAll(); });
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

            InfoPanelMode mode = _manualMode ?? autoMode;
            if (mode != _lastMode)
            {
                Log.Write($"RefreshAll: mode {_lastMode} -> {mode} (autoMode={autoMode} HasPosition={status.HasPosition} watcherNull={_watcher == null})");
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

        private InfoPanelMode ComputeMode(EliteStatus status)
        {
            // FSD spooling up for a real hyperspace jump wins outright — even from an
            // atmospheric planet's surface. Odyssey allows charging the FSD directly from
            // within a landable atmosphere without launching first, so Latitude/Longitude
            // being present (HasPosition true) can't be trusted alone to mean "show the radar"
            // while a jump is actively charging; check this before the HasPosition/Radar gate.
            if (_watcher != null && _watcher.IsChargingJump) return InfoPanelMode.Destination;

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
                _watcher.FsdTargetedAt >= _watcher.SystemArrivedAt;

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
            radarCanvas.Visibility        = mode == InfoPanelMode.Radar       ? Visibility.Visible : Visibility.Collapsed;
            starPanelViewbox.Visibility   = mode == InfoPanelMode.Star        ? Visibility.Visible : Visibility.Collapsed;
            planetPanelViewbox.Visibility = mode == InfoPanelMode.Planet      ? Visibility.Visible : Visibility.Collapsed;
            destinationPanel.Visibility   = mode == InfoPanelMode.Destination ? Visibility.Visible : Visibility.Collapsed;

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
            Style(tabRadar,       mode == InfoPanelMode.Radar);
            Style(tabStar,        mode == InfoPanelMode.Star);
            Style(tabPlanet,      mode == InfoPanelMode.Planet);
            Style(tabDestination, mode == InfoPanelMode.Destination);
        }

        private void ModeTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!Enum.TryParse<InfoPanelMode>(tagStr, out var clicked)) return;

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
            if (!force && ReferenceEquals(detail, _lastRenderedStar)) return;
            _lastRenderedStar = detail;

            starPanelCanvas.Children.Clear();
            starPanelCanvas.Children.Add(MakeGridBackground(620, 580));

            if (detail == null)
            {
                starPanelCanvas.Children.Add(MakeCenterLabel("AWAITING STAR SCAN…", 310, 280, InfoDimBrush, 13));
                return;
            }

            string iconCode = MapStarTypeToIconCode(detail.StarType);
            starPanelCanvas.Children.Add(MakeImg($"StarIcons/png/star_{iconCode}.png", 217, 197, 186));

            AddCallout(starPanelCanvas, new (double, double)[] { (246, 213), (208, 180), (150, 180) }, 180, false,
                "CLASS", $"{detail.StarType} ({StarClassNames.GetDisplayName(detail.StarType)})", InfoOrangeBrush);
            AddCallout(starPanelCanvas, new (double, double)[] { (210, 290), (150, 290) }, 290, false,
                "SOLAR MASS", detail.StellarMass > 0 ? $"{detail.StellarMass:F2}" : "—");
            AddCallout(starPanelCanvas, new (double, double)[] { (246, 367), (208, 400), (150, 400) }, 400, false,
                "AGE", detail.AgeMY > 0 ? $"{detail.AgeMY:N0} My" : "—");

            AddCallout(starPanelCanvas, new (double, double)[] { (375, 213), (412, 180), (470, 180) }, 180, true,
                "SURFACE TEMP", detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—");
            AddCallout(starPanelCanvas, new (double, double)[] { (410, 290), (470, 290) }, 290, true,
                "RADIUS (SOL)", detail.Radius > 0 ? $"{detail.Radius / 6.957e8:F2}" : "—");
            // Stars never have Saturn-style rings in-game — a star's "Rings" entry is always
            // an asteroid belt (its Name contains "Belt", e.g. "Sol A Belt"), not a true ring,
            // so exclude those here rather than mislabelling a belt as "the star has rings".
            var trueStarRing = detail.Rings.FirstOrDefault(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));
            AddCallout(starPanelCanvas, new (double, double)[] { (375, 367), (412, 400), (470, 400) }, 400, true,
                "RINGS", trueStarRing != null ? FormatRingClass(trueStarRing.RingClass) : "None");

            bool isPrimary = _watcher != null && ReferenceEquals(detail, _watcher.CurrentStarDetail);
            starPanelCanvas.Children.Add(MakeCenterLabel(detail.BodyName.ToUpperInvariant(), 310, 500, InfoValueBrush, 17));
            starPanelCanvas.Children.Add(MakeCenterLabel(isPrimary ? "primary star" : "targeted star", 310, 522, InfoDimBrush, 13));
        }

        private void UpdateInfoPlanetPanel(bool force = false)
        {
            // While landed there's typically no in-system nav-panel target at all, so fall
            // back to whatever body the player is actually standing on.
            var detail = _watcher?.TargetedPlanetDetail ?? _watcher?.CurrentBodyDetail;
            if (!force && ReferenceEquals(detail, _lastRenderedPlanet)) return;
            _lastRenderedPlanet = detail;

            planetPanelCanvas.Children.Clear();
            planetPanelCanvas.Children.Add(MakeGridBackground(620, 580));

            if (detail == null)
            {
                planetPanelCanvas.Children.Add(MakeCenterLabel("NO PLANET TARGETED", 310, 280, InfoDimBrush, 13));
                return;
            }

            if (detail.IsBelt)
            {
                // Asteroid belt clusters get a Scan event but no StarType/PlanetClass — none of
                // the physical planet stats below apply, so just show one of the belt art
                // variants (picked deterministically per body so it doesn't change on refresh)
                // and the body name.
                int variant = _watcher?.GetBeltVariant(detail.BodyName) ?? 1;
                planetPanelCanvas.Children.Add(MakeImg($"AsteroidIcons/png/belt_{variant}.png", 217, 197, 186));
                planetPanelCanvas.Children.Add(MakeCenterLabel("ASTEROID BELT", 310, 500, InfoValueBrush, 17));
                planetPanelCanvas.Children.Add(MakeCenterLabel(
                    EliteWatcherService.GetShortBodyName(detail.BodyName, _watcher?.StarSystem ?? "").ToUpperInvariant(),
                    310, 522, InfoDimBrush, 13));
                return;
            }

            // Icon stack: ring back -> base -> ring front -> atmosphere -> terraformable -> bio/geo badge
            string? ringCode = detail.Rings.Count > 0 ? MapRingClass(detail.Rings[0].RingClass) : null;
            if (ringCode != null)
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/ring_{ringCode}_back.png", 217, 197, 186));

            string? planetCode = MapPlanetClassToIconCode(detail.PlanetClass);
            if (planetCode != null)
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/{planetCode}.png", 217, 197, 186));

            if (ringCode != null)
                planetPanelCanvas.Children.Add(MakeImg($"PlanetIcons/png/ring_{ringCode}_front.png", 217, 197, 186));

            if (!string.IsNullOrEmpty(detail.AtmosphereType) &&
                !string.Equals(detail.AtmosphereType, "None", StringComparison.OrdinalIgnoreCase))
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_atmosphere.png", 217, 197, 186));

            if (string.Equals(detail.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase))
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_terraformable.png", 217, 197, 186));

            bool hasBio = detail.BioSignalCount > 0, hasGeo = detail.GeoSignalCount > 0;
            if (hasBio && hasGeo)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_combo.png", 217, 197, 186));
            else if (hasBio)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_bio.png", 217, 197, 186));
            else if (hasGeo)
                planetPanelCanvas.Children.Add(MakeImg("PlanetIcons/png/overlay_badge_geo.png", 217, 197, 186));

            bool isGasGiantFamily = planetCode != null &&
                (planetCode.StartsWith("GG", StringComparison.Ordinal) || planetCode == "WTG");
            AddCallout(planetPanelCanvas, new (double, double)[] { (246, 213), (208, 180), (150, 180) }, 180, false,
                "PLANET CLASS", isGasGiantFamily ? FormatGasGiantClass(detail.PlanetClass, planetCode) : detail.PlanetClass);
            AddCallout(planetPanelCanvas, new (double, double)[] { (210, 290), (150, 290) }, 290, false,
                "SURFACE TEMP", detail.SurfaceTemperature > 0 ? $"{detail.SurfaceTemperature:N0} K" : "—");
            AddCallout(planetPanelCanvas, new (double, double)[] { (246, 367), (208, 400), (150, 400) }, 400, false,
                "GEO SIGNALS", hasGeo ? $"{detail.GeoSignalCount} found" : "None", keyBrush: InfoOrangeBrush);

            AddCallout(planetPanelCanvas, new (double, double)[] { (375, 213), (412, 180), (470, 180) }, 180, true,
                "GRAVITY", detail.SurfaceGravity > 0 ? $"{detail.SurfaceGravity:F2} G" : "—");
            AddCallout(planetPanelCanvas, new (double, double)[] { (410, 290), (470, 290) }, 290, true,
                "ATMOSPHERE", FormatAtmosphere(detail.AtmosphereType));
            AddCallout(planetPanelCanvas, new (double, double)[] { (375, 367), (412, 400), (470, 400) }, 400, true,
                "BIO SIGNALS", hasBio ? $"{detail.BioSignalCount} found" : "None", keyBrush: InfoValueBrush);

            // DSS mapping reveals no new physical fields over a Detailed scan (confirmed against
            // real journal data — the re-fired Scan event is identical) except one real reward:
            // Ice/Rock/Metal composition. Worth surfacing once mapped, so it gets its own callout
            // in the empty space above the planet instead of just a "MAPPED" label with no payoff.
            // Centered above the planet (not left/right like the other callouts), so it's built
            // by hand here rather than through AddCallout's left/right-only text alignment.
            double compTotal = detail.IceComposition + detail.RockComposition + detail.MetalComposition;
            if (detail.IsMapped && compTotal > 0)
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
            planetPanelCanvas.Children.Add(MakeCenterLabel(bottomLabel.ToUpperInvariant(), 310, 500, InfoValueBrush, 17));
            if (detail.Landable)
                planetPanelCanvas.Children.Add(MakeCenterLabel("LANDABLE", 310, 522, InfoValueBrush, 13));
            var scanTag = detail.IsMapped ? "MAPPED" : string.IsNullOrEmpty(detail.ScanType) ? "UNKNOWN" : detail.ScanType.ToUpperInvariant();
            planetPanelCanvas.Children.Add(MakeCenterLabel($"SCAN: {scanTag}", 310, 543, InfoOrangeBrush, 12));
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
                    Text = "NO ROUTE ACTIVE", Foreground = InfoDimBrush,
                    FontFamily = new FontFamily("Consolas"), FontSize = 13
                });
                return;
            }

            int hopIndex = dest.TotalRouteJumps > 0 ? Math.Max(1, dest.TotalRouteJumps - dest.RemainingJumpsInRoute) : 0;

            var headerRow = new DockPanel();
            if (hopIndex > 0 && dest.TotalRouteJumps > 0)
            {
                var hopTb = new TextBlock
                {
                    Text = $"HOP {hopIndex} / {dest.TotalRouteJumps}", Foreground = InfoDimBrush,
                    FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(hopTb, Dock.Right);
                headerRow.Children.Add(hopTb);
            }
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = dest.NextSystem, Foreground = InfoValueBrush,
                FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold
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
                        Text = $"{dest.StarClass}-CLASS", Foreground = InfoOrangeBrush,
                        FontFamily = new FontFamily("Consolas"), FontSize = 11
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
                sp.Children.Add(new TextBlock { Text = label, Foreground = InfoDimBrush, FontFamily = new FontFamily("Consolas"), FontSize = 9.5 });
                sp.Children.Add(new TextBlock { Text = value, Foreground = brush ?? InfoValueBrush, FontFamily = new FontFamily("Consolas"), FontSize = 17, Margin = new Thickness(0, 3, 0, 0) });
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
                    Text = $"{pct:F0}%", Foreground = InfoValueBrush,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10.5
                };
                DockPanel.SetDock(pctTb, Dock.Right);
                progressRow.Children.Add(pctTb);
                progressRow.Children.Add(new TextBlock
                {
                    Text = "ROUTE PROGRESS", Foreground = InfoDimBrush,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10.5
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
            int hereIndex = hopIndex > 0 ? hopIndex - 1 : -1;

            Border? currentRow = null;
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
                var idxTb = new TextBlock { Text = (i + 1).ToString(), Foreground = isNext ? InfoValueBrush : InfoDimBrush, Width = 26, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
                DockPanel.SetDock(idxTb, Dock.Left);
                var distTb = new TextBlock { Text = hop.DistanceFromPrevLy > 0 ? $"{hop.DistanceFromPrevLy:F1} ly" : "—", Foreground = InfoDimBrush, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Width = 68, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(distTb, Dock.Right);
                var clsTb = new TextBlock { Text = hop.StarClass, Foreground = InfoOrangeBrush, FontFamily = new FontFamily("Consolas"), FontSize = 12, Width = 34, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(clsTb, Dock.Right);
                var sysTb = new TextBlock { Text = hop.StarSystem, Foreground = isNext ? InfoValueBrush : new SolidColorBrush(Color.FromRgb(0x88, 0xbb, 0xbb)), FontFamily = new FontFamily("Consolas"), FontSize = 13 };

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
            }

            // Scroll to the next-jump row when the tab is freshly shown, or when progress has
            // actually advanced to a new hop — not on every routine refresh tick, which would
            // otherwise fight a manual scroll while sitting on the tab looking at older hops.
            bool advanced = !string.Equals(dest.NextSystem, _lastScrolledNextSystem, StringComparison.OrdinalIgnoreCase);
            if ((force || advanced) && currentRow != null)
            {
                _lastScrolledNextSystem = dest.NextSystem;
                currentRow.BringIntoView();
            }
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

        private static TextBlock MakeCenterLabel(string text, double centerX, double y, Brush brush, double fontSize)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontFamily = new FontFamily("Consolas"), FontSize = fontSize };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, y);
            return tb;
        }

        // Draws an angled-then-flat leader line (2 or 3 points) plus a key/value callout at
        // its terminal point — mirrors the approved SVG mockup's layout exactly.
        private static void AddCallout(Canvas canvas, (double x, double y)[] leaderPoints, double rowY,
            bool rightSide, string key, string value, Brush? valueBrush = null, Brush? keyBrush = null)
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
            double keyX = rightSide ? textX : textX - keyTb.DesiredSize.Width;
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
                TextAlignment = rightSide ? TextAlignment.Left : TextAlignment.Right,
            };
            double valX = rightSide ? textX : textX - calloutWidth;
            Canvas.SetLeft(valTb, valX); Canvas.SetTop(valTb, rowY + 12);
            canvas.Children.Add(valTb);
        }

        private static string MapStarTypeToIconCode(string starType)
        {
            if (string.IsNullOrEmpty(starType)) return "G";
            switch (starType)
            {
                case "O": case "B": case "A": case "F": case "G": case "K": case "M":
                case "W": case "WC": case "WN": case "WNC": case "WO":
                    return starType;
                case "L": return "BrownDwarf_L";
                case "T": return "BrownDwarf_T";
                case "Y": return "BrownDwarf_Y";
                case "N": return "NeutronStar";
                case "H": case "SupermassiveBlackHole": return "BlackHole";
                default:
                    return starType.StartsWith("D", StringComparison.OrdinalIgnoreCase) ? "WhiteDwarf" : "G";
            }
        }

        private static string? MapPlanetClassToIconCode(string planetClass)
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

        private static string FormatRingClass(string raw) => MapRingClass(raw) switch
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
                Text       = "BIO SURVEY",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                FontFamily = new FontFamily("Consolas"),
                FontSize   = 14,
                FontWeight = FontWeights.Bold,
                Margin     = new Thickness(0, 0, 0, 2),
            });

            if (_watcher == null) return;

            // First footfall indicator
            bool ff = _watcher.WasFootfalled;
            var ffPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            ffPanel.Children.Add(new TextBlock
            {
                Text       = ff ? "✓ First Footfall" : "○ First Footfall",
                Foreground = ff
                    ? new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                FontFamily = new FontFamily("Consolas"),
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
                var  fullName = !string.IsNullOrEmpty(species) ? $"{genus} {species}".Trim() : genus;
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
                var  fullName  = !string.IsNullOrEmpty(org.Species) ? $"{org.Genus} {org.Species}".Trim() : org.Genus;
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
                var fullName = !string.IsNullOrEmpty(comp.Species) ? $"{comp.Genus} {comp.Species}".Trim() : comp.Genus;
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
                    Text       = "Total Payout:",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xbb, 0xbb)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 12,
                    Margin     = new Thickness(0, 2, 0, 0),
                });
                sidebarStack.Children.Add(new TextBlock
                {
                    Text       = PayoutData.FormatCredits(sidebarTotal),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                    FontFamily = new FontFamily("Consolas"),
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
                    Text       = msg,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x66)),
                    FontFamily = new FontFamily("Consolas"),
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
                    Text       = "GEO SURVEY",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                    FontFamily = new FontFamily("Consolas"),
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
                        FontFamily   = new FontFamily("Consolas"),
                        FontSize     = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Cursor       = System.Windows.Input.Cursors.Hand,
                        Margin       = new Thickness(0, 0, 0, 2),
                    };
                    nameTb.Inlines.Add(new System.Windows.Documents.Run(site.Name)
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
                            Text       = $"Payout: {PayoutData.FormatCredits(site.Payout)}",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                            FontFamily = new FontFamily("Consolas"),
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
                        Text       = "? Unknown",
                        Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xff, 0xaa, 0x00)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize   = 13,
                        Margin     = new Thickness(0, 4, 0, 4),
                    });
                }
            }
            } // end if (_showGeo)
        }

        private UIElement MakeSidebarEntry(string genus, string species,
                                           int scanCount, Color nameColor, bool isActive, long payout = 0, bool ff = false)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Wiki URL uses genus name only
            var wikiUrl = $"https://elite-dangerous.fandom.com/wiki/{genus.Replace(" ", "_")}";
            bool hasWiki = genus != "?";

            // Genus + Species on one line — genus underlined/clickable, species plain
            var genusLine = new TextBlock
            {
                FontFamily   = new FontFamily("Consolas"),
                FontSize     = 13,
                FontWeight   = isActive ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Cursor       = hasWiki ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Margin       = new Thickness(0, 0, 0, 1),
            };

            var genusRun = new System.Windows.Documents.Run(genus)
            {
                Foreground      = new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                TextDecorations = hasWiki ? TextDecorations.Underline : null,
            };
            genusLine.Inlines.Add(genusRun);

            if (!string.IsNullOrEmpty(species) && species != "Unknown")
            {
                genusLine.Inlines.Add(new System.Windows.Documents.Run($" {species}")
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
                    Text       = $"  {(ff ? "FF Payout:" : "Payout:")} {PayoutData.FormatCredits(payout)}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 12,
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

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        private ScanLogWindow? _scanLogWindow;

        private void BtnScanLog_Click(object sender, RoutedEventArgs e)
        {
            if (_scanLogWindow != null)
            {
                _scanLogWindow.Activate();
                return;
            }

            _scanLogWindow = new ScanLogWindow { Owner = this };
            _scanLogWindow.Closed += (_, _) => _scanLogWindow = null;
            _scanLogWindow.Show();
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
                        .OrderBy(f => f).ToArray();

                    // Filter by date range if specified
                    // Journal filenames contain the date: Journal.2026-05-10T194435.01.log
                    var files = allFiles.Where(f =>
                    {
                        if (from == null && to == null) return true;
                        var name = System.IO.Path.GetFileName(f);
                        // Extract date portion from filename
                        if (name.Length >= 15 &&
                            DateTime.TryParse(name.Substring(8, 10), out var fileDate))
                        {
                            if (from != null && fileDate < from.Value.Date) return false;
                            if (to   != null && fileDate > to.Value.Date)   return false;
                        }
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

        private void UpdatePlanetPanel()
        {
            if (!_showBioSites || _watcher == null) return;

            Dispatcher.InvokeAsync(() =>
            {
                planetStack.Children.Clear();

                planetStack.Children.Add(new TextBlock
                {
                    Text         = "BIOLOGICAL SITES",
                    Foreground   = new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                    FontFamily   = new FontFamily("Consolas"),
                    FontSize     = 13,
                    FontWeight   = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 0, 0, 6),
                });

                List<EliteWatcherService.PlanetBioInfo> planets;
                planets = _watcher.SystemBioPlanets
                    .OrderBy(p => p.ShortName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (planets.Count == 0 && !_showGeo)
                {
                    planetStack.Children.Add(new TextBlock
                    {
                        Text         = "No bio planets\nscanned yet",
                        Foreground   = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                        FontFamily   = new FontFamily("Consolas"),
                        FontSize     = 12,
                        TextWrapping = TextWrapping.Wrap,
                    });
                    return;
                }

                if (planets.Count == 0 && _showGeo)
                {
                    planetStack.Children.Add(new TextBlock
                    {
                        Text         = "No bio planets\nscanned yet",
                        Foreground   = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55)),
                        FontFamily   = new FontFamily("Consolas"),
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
                        FontFamily = new FontFamily("Consolas"),
                        FontSize   = 12,
                        Width      = 18,
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    // Short body name
                    row.Children.Add(new TextBlock
                    {
                        Text       = planet.ShortName.ToUpper(),
                        Foreground = allDone
                            ? new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55))
                            : new SolidColorBrush(Color.FromRgb(0x00, 0xe5, 0xff)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize   = 12,
                        FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    // Bio count
                    row.Children.Add(new TextBlock
                    {
                        Text       = $" ({planet.BioCount})",
                        Foreground = allDone
                            ? new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x55))
                            : new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0x88)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize   = 12,
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
                        .OrderBy(p => p.ShortName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (geoPlanets.Count > 0)
                    {
                        // Spacer between bio and geo
                        planetStack.Children.Add(new Border { Height = 16 });

                        planetStack.Children.Add(new TextBlock
                        {
                            Text         = "GEOLOGICAL SITES",
                            Foreground   = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x00)),
                            FontFamily   = new FontFamily("Consolas"),
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
                                FontFamily = new FontFamily("Consolas"),
                                FontSize   = 12,
                                Width      = 18,
                                VerticalAlignment = VerticalAlignment.Center,
                            });

                            row2.Children.Add(new TextBlock
                            {
                                Text       = $"{planet.ShortName.ToUpper()} ({planet.GeoCount})",
                                Foreground = new SolidColorBrush(geoFg),
                                FontFamily = new FontFamily("Consolas"),
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
            });
        }

        private void SaveSettings()
        {
            if (_settingsInitializing) return;
            AppSettings.Save(new AppSettingsData
            {
                ShowSidebar          = _showSidebar,
                AutoScale            = _autoScale,
                DefaultScale         = _defaultScale,
                KeepPlanetPanelOpen  = _showBioSites,
                RadarAnimation       = _radarAnimation,
                ShowGeologicalSites  = _showGeo,
                WindowLeft           = this.Left,
                WindowTop            = this.Top,
                WindowWidth          = this.Width,
                WindowHeight         = this.Height,
            });
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
