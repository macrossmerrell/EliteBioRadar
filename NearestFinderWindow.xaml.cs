using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EliteBioRadar
{
    // "Star Finder" — queries Spansh's galaxy database for the nearest star of a chosen type
    // relative to the commander's current system, and lets the user copy a result's system name
    // to paste into the in-game galaxy map search box. Deliberately just a lookup tool: this app
    // has no way to plot a route or inject input into the game itself (see design discussion —
    // Elite Dangerous exposes no such API), so copy-to-clipboard is the actual deliverable here.
    public partial class NearestFinderWindow : Window
    {
        private readonly EliteWatcherService _watcher;

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private static readonly SolidColorBrush CardBg     = Brush("#0e1c1c");
        private static readonly SolidColorBrush CardBorder = Brush("#1a4444");
        private static readonly SolidColorBrush TextBright = Brush("#cfe8e8");
        private static readonly SolidColorBrush TextMid    = Brush("#88bbbb");
        private static readonly SolidColorBrush TextDim    = Brush("#4c7373");
        private static readonly SolidColorBrush Accent     = Brush("#00e5ff");

        public NearestFinderWindow(EliteWatcherService watcher)
        {
            InitializeComponent();
            _watcher = watcher;

            foreach (var (label, _) in StarTypeCatalog.Entries) cmbStarType.Items.Add(label);
            cmbStarType.SelectedIndex = 0;

            txtOrigin.Text = string.IsNullOrWhiteSpace(_watcher.StarSystem) ? "(no current system yet)" : _watcher.StarSystem;

            var saved = AppSettings.Load();
            if (saved.StarFinderLeft.HasValue && saved.StarFinderTop.HasValue)
            {
                double left = saved.StarFinderLeft.Value, top = saved.StarFinderTop.Value;
                double width = saved.StarFinderWidth ?? Width, height = saved.StarFinderHeight ?? Height;
                bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s =>
                    left < s.WorkingArea.Right && left + width > s.WorkingArea.Left &&
                    top < s.WorkingArea.Bottom && top + height > s.WorkingArea.Top);
                if (onScreen)
                {
                    Left = left; Top = top; Width = width; Height = height;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }

            Closed += (_, __) => SaveWindowState();
        }

        private void SaveWindowState()
        {
            var s = AppSettings.Load();
            s.StarFinderLeft = Left;
            s.StarFinderTop = Top;
            s.StarFinderWidth = Width;
            s.StarFinderHeight = Height;
            AppSettings.Save(s);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnFind_Click(object sender, RoutedEventArgs e)
        {
            string originSystem = _watcher.StarSystem;
            if (string.IsNullOrWhiteSpace(originSystem))
            {
                txtStatus.Text = "No current system known yet.";
                return;
            }
            if (!int.TryParse(txtCount.Text, out int count) || count < 1) count = 10;
            count = Math.Clamp(count, 1, 50);

            var (label, subtypes) = StarTypeCatalog.Entries[Math.Max(0, cmbStarType.SelectedIndex)];

            btnFind.IsEnabled = false;
            resultsStack.Children.Clear();
            txtStatus.Text = $"Resolving {originSystem}...";

            var origin = await SpanshService.ResolveSystemCoordsAsync(originSystem);
            if (origin == null)
            {
                txtStatus.Text = $"Couldn't resolve coordinates for '{originSystem}'.";
                btnFind.IsEnabled = true;
                return;
            }

            txtStatus.Text = $"Searching for nearest {label}...";
            var results = await SpanshService.FindNearestStarsAsync(subtypes, origin, count);
            btnFind.IsEnabled = true;

            if (results.Count == 0)
            {
                txtStatus.Text = $"No {label} results found.";
                return;
            }

            txtStatus.Text = $"{results.Count} result(s) — nearest {results[0].DistanceLy:N1} ly";
            foreach (var r in results) resultsStack.Children.Add(BuildResultRow(r));
        }

        private UIElement BuildResultRow(SpanshService.NearestStar r)
        {
            var card = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 6),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = r.SystemName, Foreground = TextBright, FontSize = 12.5, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.Equals(r.Name, r.SystemName, StringComparison.OrdinalIgnoreCase))
            {
                info.Children.Add(new TextBlock
                {
                    Text = $"star: {r.Name}", Foreground = TextDim, FontSize = 10, Margin = new Thickness(0, 1, 0, 0),
                });
            }
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            sub.Children.Add(new TextBlock { Text = $"{r.DistanceLy:N1} ly", Foreground = Accent, FontSize = 10.5 });
            if (!string.IsNullOrWhiteSpace(r.Region))
                sub.Children.Add(new TextBlock { Text = $"   {r.Region}", Foreground = TextMid, FontSize = 10.5 });
            info.Children.Add(sub);
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var btnCopy = new Button
            {
                Content = "Copy", Padding = new Thickness(8, 3, 8, 3),
                Background = Brush("#0a1616"), Foreground = TextMid,
                BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"), FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            btnCopy.Click += (_, __) => CopyAndFlash(r.SystemName, btnCopy);
            Grid.SetColumn(btnCopy, 1);
            grid.Children.Add(btnCopy);

            card.Child = grid;
            return card;
        }

        // Brief "Copied" confirmation instead of a toast — this is a small per-row action the
        // user may repeat several times in a row (copying a few candidate systems), so a
        // full-window toast per click would be noisier than useful here.
        private void CopyAndFlash(string text, Button button)
        {
            try { Clipboard.SetText(text); } catch (Exception ex) { Log.Write($"NearestFinderWindow: clipboard error — {ex.Message}"); return; }
            string original = (string)button.Content;
            button.Content = "Copied";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            timer.Tick += (_, __) => { button.Content = original; timer.Stop(); };
            timer.Start();
        }

        private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
        {
            var names = resultsStack.Children.OfType<Border>()
                .Select(b => ((Grid)b.Child).Children.OfType<StackPanel>().FirstOrDefault())
                .Where(p => p != null)
                .Select(p => ((TextBlock)p!.Children[0]).Text)
                .ToList();
            if (names.Count == 0) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, names));
                txtStatus.Text = $"Copied {names.Count} system name(s).";
            }
            catch (Exception ex) { Log.Write($"NearestFinderWindow: copy-all clipboard error — {ex.Message}"); }
        }
    }
}
