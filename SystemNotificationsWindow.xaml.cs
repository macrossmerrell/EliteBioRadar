using System.Collections.Generic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EliteBioRadar
{
    // The second animation surface: a resizable window of its own, for a second monitor or a spot beside the game. It registers with the
    // AnimationRouter while it is open, so an animation switched on for "System Notifications" plays here only when this window exists
    // (and is not minimised). Position, size and "was open" are remembered like the System Scan window's.
    public partial class SystemNotificationsWindow : Window, IAnimationSurface
    {
        private Action? _done;
        private readonly Dictionary<string, AnimationVisual> _visuals;

        public SystemNotificationsWindow()
        {
            InitializeComponent();
            RestorePlacement();
            _visuals = new Dictionary<string, AnimationVisual>
            {
                ["first-discovery"] = firstDiscoveryVisual,
                ["landed"] = landedVisual,
                ["liftoff"] = liftoffVisual,
                ["first-footfall"] = firstFootfallVisual,
                ["arrival"] = arrivalVisual,
                ["hyperspace-jump"] = jumpDepartureVisual,
                ["fuel-scoop"] = fuelScoopVisual,
                ["combat-mode"] = combatModeVisual,
                ["analysis-mode"] = analysisModeVisual,
                ["landing-gear"] = landingGearVisual,
                ["hardpoints"] = hardpointsVisual,
                ["cargo-scoop"] = cargoScoopVisual,
                ["headlights"] = headlightsVisual,
                ["night-vision"] = nightVisionVisual,
                ["shields"] = shieldsVisual,
                ["low-fuel"] = lowFuelVisual,
                ["overheat"] = overheatVisual,
                ["interdiction"] = interdictionVisual,
                ["silent-running"] = silentRunningVisual,
                ["supercruise"] = supercruiseVisual,
                ["supercruise-overdrive"] = supercruiseOverdriveVisual,
                ["supercruise-assist"] = supercruiseAssistVisual,
                ["supercruise-exit"] = supercruiseExitVisual,
                ["supercharge"] = superchargeVisual,
                ["route"] = routeVisual,
                ["fss-complete"] = fssCompleteVisual,
                ["codex-entry"] = codexEntryVisual,
                ["material"] = materialVisual,
                ["mission"] = missionVisual,
                ["payout"] = payoutVisual,
                ["bounty"] = bountyVisual,
                ["cargo"] = cargoVisual,
                ["planet-mapped"] = mappedVisual,
                ["srv"] = srvVisual,
                ["organism-scan"] = organismScanVisual,
                ["colony-range"] = colonyRangeVisual,
                ["hull-damage"] = hullDamageVisual,
                ["vitals"] = vitalsVisual,
                ["rank-up"] = rankUpVisual,
                ["thermal"] = thermalVisual,
                ["ship-lost"] = shipLostVisual,
                ["fighter"] = fighterVisual,
                ["mass-lock"] = massLockVisual,
                ["fsd-cooldown"] = fsdCooldownVisual,
                ["docking-request"] = dockingRequestVisual,
                ["drive-assist"] = driveAssistVisual,
                ["handbrake"] = handbrakeVisual,
                ["turret"] = turretVisual,
                ["flight-assist"] = flightAssistVisual,
                ["disembark"] = disembarkVisual,
                ["embark"] = embarkVisual,
                ["docked"] = dockedVisual,
            };
            foreach (var def in AnimationCatalog.All)
                if (!def.Live && !_visuals.ContainsKey(def.Id)) Log.Write($"Animation '{def.Id}' has no visual in the System Notifications window");
            foreach (var v in _visuals.Values)
                v.Finished += () => { var d = _done; _done = null; d?.Invoke(); };
            AnimationRouter.Register(this);
            Closed += (_, _) => { AnimationRouter.Unregister(this); SaveWindowState(); };
            // On a fresh start the window is reopened while the app is at its busiest, and WPF can miss its first frame, leaving the window plain white
            // (nothing else redraws an empty window). Force a repaint once the UI is idle, and again a little later in case the first one was swallowed too.
            Loaded += (_, _) =>
            {
                Dispatcher.InvokeAsync(Repaint, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += (_, _) => { t.Stop(); Repaint(); };
                t.Start();
            };
        }

        private void Repaint()
        {
            if (!IsLoaded) return;
            InvalidateVisual();
            if (Content is UIElement c) c.InvalidateVisual();
            UpdateLayout();
        }

        public AnimationSurfaces Kind => AnimationSurfaces.Notifications;
        public bool IsAvailable => IsVisible && WindowState != WindowState.Minimized;

        public void Play(AnimationRequest request, Action done)
        {
            _done = done;
            Log.Write($"Animation '{request.Def.Id}' playing in the System Notifications window: {request.Subject}");
            if (!_visuals.TryGetValue(request.Def.Id, out var v)) { Log.Write($"Animation '{request.Def.Id}' has no visual in the System Notifications window"); _done = null; done(); return; }
            v.BackdropOpacity = request.Def.BackdropOpacity;
            v.Start(request.Subject, request.Detail);
        }

        // The canvas MainWindow draws the atmosphere scene into while a phase this window is switched on for is under way (a wide layout, scaled to
        // the window with the aspect kept). Called every frame; returns the canvas to fill.
        // The scene width that matches this window's shape at the given scene height (in 10-unit steps, so a drag-resize does not rebuild it every pixel).
        public double SceneWidthFor(double height)
        {
            double cw = liveSceneBox.Parent is FrameworkElement p && p.ActualHeight > 0 ? p.ActualWidth / p.ActualHeight : 16.0 / 9;
            return Math.Round(Math.Clamp(height * cw, height * 1.2, height * 3.2) / 10) * 10;
        }

        public Canvas ShowLiveScene(double width, double height)
        {
            if (liveSceneCanvas.Width != width) liveSceneCanvas.Width = width;
            if (liveSceneBox.Visibility != Visibility.Visible)
            {
                liveSceneCanvas.Height = height;
                liveSceneBox.Visibility = Visibility.Visible;
                Log.Write("Live atmosphere scene shown in the System Notifications window");
            }
            return liveSceneCanvas;
        }

        public void HideLiveScene()
        {
            if (liveSceneBox.Visibility == Visibility.Collapsed) return;
            liveSceneBox.Visibility = Visibility.Collapsed;
            liveSceneCanvas.Children.Clear();
        }

        private void RestorePlacement()
        {
            var saved = AppSettings.Load();
            if (!saved.SystemNotificationsLeft.HasValue || !saved.SystemNotificationsTop.HasValue) return;
            double left = saved.SystemNotificationsLeft.Value, top = saved.SystemNotificationsTop.Value;
            double width = saved.SystemNotificationsWidth ?? Width, height = saved.SystemNotificationsHeight ?? Height;
            // Same guard as the other windows: a position saved on a monitor that is no longer attached must not open off-screen.
            bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s =>
                left < s.WorkingArea.Right && left + width > s.WorkingArea.Left &&
                top < s.WorkingArea.Bottom && top + height > s.WorkingArea.Top);
            if (!onScreen) return;
            Left = left; Top = top; Width = width; Height = height;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        private void SaveWindowState()
        {
            var s = AppSettings.Load();
            s.SystemNotificationsLeft = Left;
            s.SystemNotificationsTop = Top;
            s.SystemNotificationsWidth = Width;
            s.SystemNotificationsHeight = Height;
            AppSettings.Save(s);
        }
    }
}
