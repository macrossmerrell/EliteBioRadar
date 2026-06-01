using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private bool _planetOverlay  = false;
        private bool _planetPanelOpen = false;
        private string? _activeGenus = null;

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
            _planetOverlay = saved.PlanetOverlay;
            _planetPanelOpen = saved.KeepPlanetPanelOpen;

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

            chkPlanetOverlay.IsChecked  = _planetOverlay;
            chkKeepPlanetOpen.IsChecked = saved.KeepPlanetPanelOpen;
            _radarAnimation = saved.RadarAnimation;
            chkRadarAnimation.IsChecked = _radarAnimation;
            _showGeo = saved.ShowGeologicalSites;
            chkShowGeo.IsChecked = _showGeo;
            if (_planetPanelOpen) ApplyPlanetPanelState();

            // Load earnings
            EarningsTracker.Load();
            UpdateEarningsDisplay();

            // Apply to controls
            chkAutoScale.IsChecked = _autoScale;
            chkSidebar.IsChecked   = _showSidebar;
            sidebarCol.Width       = _showSidebar ? new GridLength(180) : new GridLength(0);
            sidebarPanel.Visibility = _showSidebar ? Visibility.Visible : Visibility.Collapsed;

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
                svc.StatusUpdated     += (_, args) => Dispatcher.InvokeAsync(() => UpdateStatusBar(args.Status));
                svc.BodyChanged       += (_, args) => Dispatcher.InvokeAsync(() => UpdateBodyInfo(args));
                svc.PlanetListChanged += (_, __)   => UpdatePlanetPanel();
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

        // ---------------------------------------------------------------
        private void RefreshAll()
        {
            var status    = _watcher?.CurrentStatus    ?? new EliteStatus();
            var organisms = _watcher?.ScannedOrganisms ?? new List<ScannedOrganism>();

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

            _renderer.Draw(status, organisms, _scaleMetres, _activeGenus, _radarAnimation);

            UpdatePotentialPayout();
            UpdateEarningsDisplay();

            // Nearest organism
            ScannedOrganism? closest = null;
            double minDist = double.MaxValue;
            if (status.HasPosition && organisms.Count > 0)
            {
                lock (organisms)
                {
                    foreach (var o in organisms)
                    {
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

        private void BtnPlanetPanel_Click(object sender, RoutedEventArgs e)
        {
            _planetPanelOpen = !_planetPanelOpen;
            ApplyPlanetPanelState();
            if (chkKeepPlanetOpen?.IsChecked == true) SaveSettings();
        }

        private void ChkKeepPlanetOpen_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
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

        private void ChkPlanetOverlay_Changed(object sender, RoutedEventArgs e)
        {
            _planetOverlay = chkPlanetOverlay.IsChecked == true;
            if (_planetPanelOpen) ApplyPlanetPanelState();
            SaveSettings();
        }

        private void ApplyPlanetPanelState()
        {
            if (_planetPanelOpen)
            {
                planetPanel.Visibility = Visibility.Visible;
                if (_planetOverlay)
                {
                    planetCol.Width = new GridLength(0);
                    planetPanel.SetValue(Grid.ColumnProperty, 1);
                    planetPanel.HorizontalAlignment = HorizontalAlignment.Left;
                    planetPanel.Width = 150;
                    Panel.SetZIndex(planetPanel, 10);
                    // Button sits just to the right of the overlay panel
                    btnPlanetPanel.Margin = new Thickness(154, 4, 0, 0);
                }
                else
                {
                    planetPanel.ClearValue(FrameworkElement.WidthProperty);
                    planetPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                    planetPanel.SetValue(Grid.ColumnProperty, 0);
                    Panel.SetZIndex(planetPanel, 0);
                    planetCol.Width = new GridLength(150);
                    // Button sits at left edge of radar column (panel is in its own column)
                    btnPlanetPanel.Margin = new Thickness(4, 4, 0, 0);
                }
                UpdatePlanetPanel();
            }
            else
            {
                planetPanel.Visibility = Visibility.Collapsed;
                planetCol.Width = new GridLength(0);
                btnPlanetPanel.Margin = new Thickness(4, 4, 0, 0);
            }

            // Toggle button icon: planet when closed, X when open
            planetBtnIcon.Data = System.Windows.Media.Geometry.Parse(
                _planetPanelOpen
                    ? "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12Z"
                    : "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4C14.21,4 16.21,4.86 17.71,6.29C16.32,6.28 14.44,6.93 13.07,8.35C11.69,9.77 11.09,11.66 11.12,13.05C9.7,14.42 8.04,14.31 6.62,13.77C5.57,13.37 4.67,12.67 4.18,12.03C5.17,7.54 8.62,4 12,4M20,12C20,12.23 19.99,12.46 19.97,12.68C19.44,12.42 18.75,12.29 17.95,12.38C17.14,12.47 16.45,12.81 15.97,13.28C15.27,13.95 14.97,14.86 15.08,15.76C15.19,16.71 15.72,17.55 16.43,18.07C15.16,19.27 13.67,20 12,20C9.79,20 7.79,19.14 6.29,17.71L6.53,17.65C7.96,17.3 9.53,16.56 10.63,15.15C11.62,15.3 12.73,15.06 13.73,14.36C14.7,13.69 15.32,12.7 15.49,11.65C16.28,10.36 17.59,9.71 18.87,9.77C19.58,10.46 20,11.18 20,12Z");
        }

        private void UpdatePlanetPanel()
        {
            if (!_planetPanelOpen || _watcher == null) return;

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
                PlanetOverlay        = _planetOverlay,
                KeepPlanetPanelOpen  = chkKeepPlanetOpen?.IsChecked == true && _planetPanelOpen,
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
            _showSidebar        = chkSidebar.IsChecked == true;
            sidebarCol.Width    = _showSidebar ? new GridLength(180) : new GridLength(0);
            sidebarPanel.Visibility = _showSidebar ? Visibility.Visible : Visibility.Collapsed;
            if (_showSidebar) UpdateSidebar();
            SaveSettings();
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
