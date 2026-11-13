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
            var placed = new System.Collections.Generic.HashSet<string>();
            foreach (var (heading, ids) in Layout)
            {
                var defs = new System.Collections.Generic.List<AnimationDef>();
                foreach (var id in ids)
                    foreach (var def in AnimationCatalog.All)
                        if (def.Id == id && placed.Add(id)) defs.Add(def);
                AddSection(heading, defs, settings);
            }
            // An animation added to the catalog but not listed above still shows, under its own catalog group at the end.
            foreach (var g in System.Linq.Enumerable.GroupBy(System.Linq.Enumerable.Where(AnimationCatalog.All, d => !placed.Contains(d.Id)), d => d.Group))
                AddSection(g.Key, System.Linq.Enumerable.ToList(g), settings);
        }

        // The headings in the order of play: arriving somewhere new, getting there, the ground, what you find, the fight, the ship, the haul, the stations, and you.
        // Ids within a heading are in the order things usually happen.
        private static readonly (string Heading, string[] Ids)[] Layout =
        {
            ("Firsts",              new[] { "first-discovery", "first-footfall" }),
            ("Travel",              new[] { "route", "hyperspace-jump", "supercharge", "arrival", "fuel-scoop", "carrier-jump", "supercruise", "supercruise-overdrive", "supercruise-assist", "supercruise-exit" }),
            ("Atmosphere",          new[] { "descent", "climb", "atmospheric-jump" }),
            ("Surface and vehicles", new[] { "landed", "liftoff", "srv", "ship-call", "embark", "disembark", "drive-assist", "handbrake", "turret" }),
            ("Exploration",         new[] { "discovery-scan", "signal-source", "nav-beacon", "fss-complete", "body-signals", "planet-mapped", "codex-entry", "material", "payout" }),
            ("Biology",             new[] { "organism-scan", "colony-range", "exobiology-sold" }),
            ("Combat",              new[] { "combat-alert", "interdiction", "interdiction-outcome", "crime", "bounty", "fighter", "cockpit-breach", "self-destruct", "ship-lost" }),
            ("Ship controls",       new[] { "landing-gear", "hardpoints", "cargo-scoop", "headlights", "night-vision", "flight-assist", "silent-running", "combat-mode", "analysis-mode" }),
            ("Ship alerts",         new[] { "shields", "hull-damage", "low-fuel", "overheat", "mass-lock", "fsd-cooldown" }),
            ("Cargo and trade",     new[] { "mining", "cargo", "search-rescue", "trade-profit", "voucher" }),
            ("Missions",            new[] { "mission" }),
            ("Stations",            new[] { "docking-request", "docked" }),
            ("Commander",           new[] { "rank-up", "vitals", "thermal", "wing-crew", "engineer", "tech-broker" }),
        };

        private void AddSection(string heading, System.Collections.Generic.List<AnimationDef> defs, AppSettingsData settings)
        {
            if (defs.Count == 0) return;
            rows.Children.Add(new TextBlock
            {
                Text = heading.ToUpperInvariant(), Foreground = GroupBrush, FontSize = 12, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 14, 0, 4),
            });
            foreach (var def in defs) rows.Children.Add(BuildRow(def, AnimationCatalog.SurfacesFor(def, settings)));
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var name = new TextBlock { Text = def.Name, Foreground = TextBrush, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = def.Name };
            var main = new CheckBox { IsChecked = current.HasFlag(AnimationSurfaces.Main), VerticalAlignment = VerticalAlignment.Center };
            var notif = new CheckBox { IsChecked = current.HasFlag(AnimationSurfaces.Notifications), VerticalAlignment = VerticalAlignment.Center };
            var play = new Button
            {
                Content = "Play", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Frozen("#1a3333"), Foreground = GroupBrush, BorderBrush = Frozen("#1a4444"), FontSize = 12,
                ToolTip = "Plays it now with sample text, in the window(s) ticked here (alternates between modes where there are two)",
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
            int step = 0;
            play.Click += (_, _) =>
            {
                var cycle = def.PreviewCycle;
                var detail = cycle is { Length: > 0 } ? cycle[step++ % cycle.Length] : def.PreviewDetail;
                AnimationRouter.Raise(new AnimationRequest(def, def.PreviewSubject, detail));
            };

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
