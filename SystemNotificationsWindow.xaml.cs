using System;
using System.Linq;
using System.Windows;

namespace EliteBioRadar
{
    // The second animation surface: a resizable window of its own, for a second monitor or a spot beside the game. It registers with the
    // AnimationRouter while it is open, so an animation switched on for "System Notifications" plays here only when this window exists
    // (and is not minimised). Position, size and "was open" are remembered like the System Scan window's.
    public partial class SystemNotificationsWindow : Window, IAnimationSurface
    {
        private Action? _done;

        public SystemNotificationsWindow()
        {
            InitializeComponent();
            RestorePlacement();
            firstDiscoveryVisual.Finished += () => { var d = _done; _done = null; d?.Invoke(); };
            AnimationRouter.Register(this);
            Closed += (_, _) => { AnimationRouter.Unregister(this); SaveWindowState(); };
        }

        public AnimationSurfaces Kind => AnimationSurfaces.Notifications;
        public bool IsAvailable => IsVisible && WindowState != WindowState.Minimized;

        public void Play(AnimationRequest request, Action done)
        {
            _done = done;
            Log.Write($"Animation '{request.Def.Id}' playing in the System Notifications window: {request.Subject}");
            firstDiscoveryVisual.BackdropOpacity = request.Def.BackdropOpacity;
            firstDiscoveryVisual.Start(request.Subject, request.Detail);
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
