using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Pop-out from the Settings panel: one row per animation in AnimationCatalog, with a tick box for each window that can show it.
    // Both ticked = both windows, neither = off. Each change is saved straight away (AppSettingsData.AnimationTargets); the router reads
    // the file on every raised animation, so nothing needs reloading. Play runs the animation through the router with sample text.
    public partial class NotificationSettingsWindow : Window
    {
        private static readonly Brush TextBrush  = Frozen("#cfe8e8");
        private static readonly Brush GroupBrush = Frozen("#00e5ff");
        private static readonly Brush DimBrush   = Frozen("#446666");

        public NotificationSettingsWindow()
        {
            InitializeComponent();
            var settings = AppSettings.Load();
            string? group = null;
            foreach (var def in AnimationCatalog.All)
            {
                if (def.Group != group)
                {
                    group = def.Group;
                    rows.Children.Add(new TextBlock
                    {
                        Text = group.ToUpperInvariant(), Foreground = GroupBrush, FontSize = 12, FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 12, 0, 4),
                    });
                }
                rows.Children.Add(BuildRow(def, AnimationCatalog.SurfacesFor(def, settings)));
            }
        }

        private static Brush Frozen(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        private Grid BuildRow(AnimationDef def, AnimationSurfaces current)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var name = new TextBlock { Text = def.Name, Foreground = TextBrush, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var main = new CheckBox { IsChecked = current.HasFlag(AnimationSurfaces.Main), VerticalAlignment = VerticalAlignment.Center };
            var notif = new CheckBox { IsChecked = current.HasFlag(AnimationSurfaces.Notifications), VerticalAlignment = VerticalAlignment.Center };
            var play = new Button
            {
                Content = "Play", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Frozen("#1a3333"), Foreground = GroupBrush, BorderBrush = Frozen("#1a4444"), FontSize = 12,
                ToolTip = "Plays it now with sample text, in the window(s) ticked here",
            };

            void Save()
            {
                var value = (main.IsChecked == true ? AnimationSurfaces.Main : 0) | (notif.IsChecked == true ? AnimationSurfaces.Notifications : 0);
                var s = AppSettings.Load();
                s.AnimationTargets[def.Id] = (int)value;
                AppSettings.Save(s);
            }
            main.Checked += (_, _) => Save();
            main.Unchecked += (_, _) => Save();
            notif.Checked += (_, _) => Save();
            notif.Unchecked += (_, _) => Save();
            play.Click += (_, _) => AnimationRouter.Raise(new AnimationRequest(def, def.PreviewSubject, def.PreviewDetail));

            Grid.SetColumn(name, 0); Grid.SetColumn(main, 1); Grid.SetColumn(notif, 2); Grid.SetColumn(play, 3);
            g.Children.Add(name); g.Children.Add(main); g.Children.Add(notif);
            if (def.Live)
            {
                // Follows the ship, so there is nothing to preview from here.
                var live = new TextBlock { Text = "live", Foreground = DimBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(live, 3);
                g.Children.Add(live);
            }
            else g.Children.Add(play);
            return g;
        }
    }
}
