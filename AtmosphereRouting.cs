using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EliteBioRadar
{
    // Routes the three atmosphere scenes (descent, climb, hyperspace jump from an atmosphere) to the windows chosen in Notification Settings.
    // UpdateDeorbitPanel draws the scene into the Deorbit tab's canvas (620 wide) and, when the System Notifications window wants the current
    // phase, a second time into that window's wide canvas (WideSceneWidth): the same scene state, laid out for the wider view. If only the
    // Notifications window wants a phase, the Deorbit tab stays out of the way.
    public partial class MainWindow
    {
        public const double WideSceneWidth = 1030;   // about 16:9 against the 580 high scene

        // Everything that belongs to one canvas the scene is drawn into: its width and its own copy of the planet surface (an element can have only one parent).
        private sealed class SceneSlot
        {
            public double W = DesignW;
            public FrameworkElement? Surface;
            public string SurfaceBody = "";
            public LandableWorldShaderEffect? Effect;
            public double FrameCx, FrameCy, FrameR0;
        }

        private readonly SceneSlot _mainSlot = new();
        private readonly SceneSlot _wideSlot = new() { W = WideSceneWidth };
        private SceneSlot? _slotOverride;
        private SceneSlot Slot => _slotOverride ?? _mainSlot;
        private Canvas? _atmoWideCanvas;           // set each frame by PrepareAtmoOutputs when the wide view is wanted
        private bool _atmoWideFailed;              // the failure is logged once per scene

        private AnimationDef? _atmoPhase;          // null until the direction of travel is known
        private bool _sceneActive;                 // ShouldShowDeorbit as of the last RefreshAll
        private AppSettingsData? _atmoSettings;
        private DateTime _atmoSettingsAt = DateTime.MinValue;

        private static readonly AnimationDef[] AtmoDefs = { AnimationCatalog.Descent, AnimationCatalog.Climb, AnimationCatalog.AtmosphericJump };

        // Settings are re-read at most once a second: this is asked every frame.
        private AnimationSurfaces AtmoSurfaces(AnimationDef def)
        {
            if (_atmoSettings == null || (DateTime.UtcNow - _atmoSettingsAt).TotalSeconds > 1)
            {
                _atmoSettings = AppSettings.Load();
                _atmoSettingsAt = DateTime.UtcNow;
            }
            return AnimationCatalog.SurfacesFor(def, _atmoSettings);
        }

        // Does the current phase want this window? Before the phase is known, any phase that does.
        private bool AtmoWants(AnimationSurfaces surface) =>
            _atmoPhase != null ? (AtmoSurfaces(_atmoPhase) & surface) != 0 : AtmoDefs.Any(d => (AtmoSurfaces(d) & surface) != 0);

        private bool AtmoMainWanted() => AtmoWants(AnimationSurfaces.Main);

        // Called from UpdateDeorbitPanel once the phase is known. Returns whether anything is showing the scene, i.e. whether to draw it.
        private bool PrepareAtmoOutputs()
        {
            bool onMainTab = _lastMode == InfoPanelMode.Deorbit && AtmoMainWanted();
            var win = _systemNotificationsWindow;
            bool wide = win is { IsAvailable: true } && AtmoWants(AnimationSurfaces.Notifications);
            if (wide) _atmoWideCanvas = win!.ShowLiveScene(WideSceneWidth, DescentH);
            else { _atmoWideCanvas = null; win?.HideLiveScene(); }
            return onMainTab || wide;
        }

        // The scene as a whole started or ended (edge of ShouldShowDeorbit). Starting and ending reset the animation state, so the next scene
        // never opens on the last one's leftovers; mode changes in the middle of a scene (a phase the Deorbit tab is switched off for) do not.
        private void OnAtmoSceneEdge(bool active)
        {
            _sceneActive = active;
            _atmoWideFailed = false;
            ResetDeorbitState();
            if (!active)
            {
                _atmoPhase = null;
                _atmoWideCanvas = null;
                _systemNotificationsWindow?.HideLiveScene();
            }
            SyncAnimSubscription();
        }

        private void SyncAnimSubscription()
        {
            bool needsFastAnim = _lastMode == InfoPanelMode.Deorbit || _lastMode == InfoPanelMode.FssScanner || _sceneActive;
            if (needsFastAnim && !_animRunning) { System.Windows.Media.CompositionTarget.Rendering += AnimFrame; _animRunning = true; }
            else if (!needsFastAnim && _animRunning) { System.Windows.Media.CompositionTarget.Rendering -= AnimFrame; _animRunning = false; }
        }
    }
}
