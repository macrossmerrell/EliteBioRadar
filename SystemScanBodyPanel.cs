using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    // The body detail panel: clicking a card in System Scan slides a panel in from the window's right edge with everything the app knows about that
    // body - physical numbers, atmosphere, value, what is on the surface, materials, rings and moons - plus a Details toggle for the lighting and
    // render information the look tuning is based on. The panel stays inside the window, so it never covers the card list's own space.
    public partial class SystemScanWindow
    {
        private const double DetailPanelWidth = 450;
        private const double DetailTextScale = 1.14;   // the panel text one step larger than the cards' own
        private string _detailBodyName = "";
        private bool _detailDetailsOpen;
        private bool _detailExtendedWindow;

        private List<BodyScanDetail> _detailBodies = new();

        // Makes a card, moon row or minor row open the panel for its body.
        private T MakeDetailClickable<T>(T element, BodyScanDetail b) where T : FrameworkElement
        {
            element.Cursor = Cursors.Hand;
            element.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return;   // a moon row inside a planet card: the inner row wins
                e.Handled = true;
                ShowBodyDetail(b);
            };
            return element;
        }

        // Called from Populate with the bodies it is about to draw: keeps an open panel current (a rescan or a mapping completing while it is open).
        private void RefreshDetailPanel(List<BodyScanDetail> bodies)
        {
            _detailBodies = bodies;
            if (string.IsNullOrEmpty(_detailBodyName)) return;
            var b = bodies.FirstOrDefault(x => string.Equals(x.BodyName, _detailBodyName, StringComparison.OrdinalIgnoreCase));
            if (b == null) { CloseBodyDetail(); return; }
            BuildDetailContent(b);
        }

        private void ShowBodyDetail(BodyScanDetail b)
        {
            _detailBodyName = b.BodyName;
            BuildDetailContent(b);
            if (detailSide.Width < 1 || double.IsNaN(detailSide.Width))
            {
                // On a narrow window, grow it by the panel's width so the card list keeps its room; shrunk back on close.
                _detailExtendedWindow = false;
                if (WindowState == WindowState.Normal && ActualWidth < 980)
                {
                    Width = ActualWidth + DetailPanelWidth;
                    _detailExtendedWindow = true;
                }
                detailSide.BeginAnimation(WidthProperty, new DoubleAnimation(0, DetailPanelWidth, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        // The open body panel follows the in-game target: when a new planet or moon is targeted, the panel switches to it. Only while the panel is open (it is
        // not opened by targeting), and only for bodies this window lists.
        private string _lastTargetedBody = "";
        private void FollowTargetedBody()
        {
            string target = _watcher.TargetedBody ?? "";
            if (string.Equals(target, _lastTargetedBody, StringComparison.OrdinalIgnoreCase)) return;
            _lastTargetedBody = target;
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(_detailBodyName)) return;
            if (string.Equals(target, _detailBodyName, StringComparison.OrdinalIgnoreCase)) return;
            var b = _detailBodies.FirstOrDefault(x => !x.IsStar && !x.IsBelt && string.Equals(x.BodyName, target, StringComparison.OrdinalIgnoreCase));
            if (b != null) ShowBodyDetail(b);
        }

        private void CloseBodyDetail()
        {
            _detailBodyName = "";
            var a = new DoubleAnimation(detailSide.ActualWidth, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            detailSide.BeginAnimation(WidthProperty, a);
            if (_detailExtendedWindow && WindowState == WindowState.Normal) { Width = Math.Max(MinWidth, ActualWidth - DetailPanelWidth); }
            _detailExtendedWindow = false;
        }

        private static TextBlock DText(string text, Brush fg, double size = 11.5, bool bold = false, Thickness? margin = null) => new TextBlock
        {
            Text = text, Foreground = fg, FontSize = size * DetailTextScale, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0),
        };

        private UIElement DSection(string title) => new TextBlock
        {
            Text = title, Foreground = AccentDim, FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4),
        };

        private static UIElement DRule() => new Border { Height = 1, Background = BorderSoft, Margin = new Thickness(0, 10, 0, 0) };

        private static string ShortDist(double metres)
        {
            if (metres <= 0) return "—";
            double ls = metres / 299_792_458.0;
            if (ls >= 1) return $"{ls:N0} ls";
            return $"{metres / 1000.0:N0} km";
        }

        private void BuildDetailContent(BodyScanDetail b)
        {
            var s = detailStack;
            s.Children.Clear();
            string shortName = EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem).ToUpperInvariant();

            // Header: name, class, close button.
            var head = new DockPanel();
            var close = new Button
            {
                Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), Background = Brush("#0a1616"), Foreground = TextMid,
                BorderBrush = BorderC, BorderThickness = new Thickness(1), FontFamily = new FontFamily("Consolas"), FontSize = 12,
            };
            close.Click += (_, __) => CloseBodyDetail();
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var nameStack = new StackPanel();
            nameStack.Children.Add(DText(shortName, TextBright, 20, true));
            nameStack.Children.Add(DText(FormatClass(b), ClassText, 11.5, false, new Thickness(0, 1, 0, 0)));
            head.Children.Add(nameStack);
            s.Children.Add(head);

            // Chips.
            var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (b.Landable) chips.Children.Add(Chip("landable", LandableColor, LandableFill));
            if (string.Equals(b.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase)) chips.Children.Add(Chip("terraformable", TerraColor, TerraFill));
            chips.Children.Add(Chip(b.IsMapped ? "mapped" : "scanned", b.IsMapped ? Accent : TextMid, null));
            if (b.WasDiscovered == false) chips.Children.Add(Chip("first discovery", ValueColor, null));
            if (b.WasFootfalled == false && b.Landable) chips.Children.Add(Chip("first footfall available", HzColor, null));
            if (b.TidalLock) chips.Children.Add(Chip("tidally locked", TextMid, null));
            s.Children.Add(chips);

            // Hero: thumbnail beside the physical numbers.
            var hero = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var thumb = new Grid { Width = 128, Height = 128, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            var detailThumb = new Grid { Width = 128, Height = 128 };
            detailThumb.Children.Add(BuildThumb(b, 128));
            thumb.Children.Add(detailThumb);
            Grid.SetColumn(thumb, 0);
            hero.Children.Add(thumb);

            var kv = new Grid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void KV(string k, string v, Brush? vc = null)
            {
                kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var kt = DText(k, TextDim, 11.5, false, new Thickness(0, 1, 12, 1)); Grid.SetRow(kt, row); Grid.SetColumn(kt, 0); kv.Children.Add(kt);
                var vt = DText(v, vc ?? TextBright, 11.5, false, new Thickness(0, 1, 0, 1)); Grid.SetRow(vt, row); Grid.SetColumn(vt, 1); kv.Children.Add(vt);
                row++;
            }
            if (b.SurfaceGravity > 0) KV("Gravity", $"{b.SurfaceGravity / 9.80665:F2} g  ({b.SurfaceGravity:F2} m/s²)");
            if (b.Radius > 0) KV("Radius", $"{b.Radius / 1000.0:N0} km");
            if (b.MassEM > 0) KV("Mass", b.MassEM >= 0.01 ? $"{b.MassEM:F3} Earth" : $"{b.MassEM:E2} Earth");
            if (b.SurfaceTemperature > 0) KV("Temperature", $"{b.SurfaceTemperature:N0} K");
            KV("Pressure", b.SurfacePressure > 0 ? FormatPressure(b.SurfacePressure) : "none");
            if (!string.IsNullOrWhiteSpace(b.Volcanism)) KV("Volcanism", b.Volcanism);
            if (b.DistanceFromArrivalLS > 0) KV("From arrival", $"{b.DistanceFromArrivalLS:N0} ls");
            if (b.SemiMajorAxis > 0) KV("Orbit", ShortDist(b.SemiMajorAxis) + (b.SemiMajorAxis > 1.0e9 ? $"  ({b.SemiMajorAxis / 1.496e11:F2} AU)" : ""));
            Grid.SetColumn(kv, 1);
            hero.Children.Add(kv);
            s.Children.Add(hero);

            // Atmosphere.
            if (b.AtmosphereComposition.Count > 0)
            {
                s.Children.Add(DSection("ATMOSPHERE" + (string.IsNullOrEmpty(b.AtmosphereType) || b.AtmosphereType == "None" ? "" : "  ·  " + System.Text.RegularExpressions.Regex.Replace(b.AtmosphereType, "(?<=[a-z0-9])(?=[A-Z])", " ").ToUpperInvariant())));
                foreach (var (name, pct) in b.AtmosphereComposition.Take(6))
                {
                    var bar = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var nm = DText(name, TextMid, 11.5); nm.Width = 118; DockPanel.SetDock(nm, Dock.Left); bar.Children.Add(nm);
                    var pc = DText($"{pct:F1}%", TextBright, 11.5); pc.Width = 58; pc.TextAlignment = TextAlignment.Right; DockPanel.SetDock(pc, Dock.Right); bar.Children.Add(pc);
                    bar.Children.Add(new Border { Height = 6, Width = Math.Max(3, pct * 1.6), HorizontalAlignment = HorizontalAlignment.Left, Background = Accent, Opacity = 0.7, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 6, 0) });
                    s.Children.Add(bar);
                }
            }

            // Composition.
            if (b.IceComposition + b.RockComposition + b.MetalComposition > 0.001)
            {
                s.Children.Add(DSection("COMPOSITION"));
                s.Children.Add(DText($"ice {b.IceComposition:P0}  ·  rock {b.RockComposition:P0}  ·  metal {b.MetalComposition:P0}", TextBright));
            }

            // Value.
            var (est, _) = ScanValueEstimator.Estimate(b);
            double mappedEst = ScanValueEstimator.EstimateIfMapped(b);
            if (est > 0 || mappedEst > 0)
            {
                s.Children.Add(DSection("VALUE"));
                string vt = $"scan ≈ {PayoutData.FormatCredits((long)est)}";
                if (!b.IsMapped && mappedEst > est) vt += $"   ·   if mapped ≈ {PayoutData.FormatCredits((long)mappedEst)}";
                s.Children.Add(DText(vt, ValueColor));
            }

            // On the surface: biologicals (per organism, from the body's cache), geological and mining counts.
            if (b.BioSignalCount > 0 || b.GeoSignalCount > 0 || b.MiningSignalCount > 0)
            {
                s.Children.Add(DSection("ON THE SURFACE"));
                if (b.BioSignalCount > 0)
                {
                    // The body being worked on right now has live (not yet cached) scans and genera: the cache only keeps finished organisms, so the first
                    // sample of a genus would not show. Live lists win; the cache fills in the rest (other bodies, or earlier sessions).
                    var cached = ScanCache.LoadForBody(b.BodyName);
                    bool live = string.Equals(b.BodyName, _watcher.CurrentBody, StringComparison.OrdinalIgnoreCase);
                    var organisms = new List<ScannedOrganism>();
                    var genera = new List<string>();
                    if (live)
                    {
                        lock (_watcher.ScannedOrganisms) organisms.AddRange(_watcher.ScannedOrganisms);
                        lock (_watcher.KnownGenera) genera.AddRange(_watcher.KnownGenera);
                    }
                    foreach (var o in cached.Organisms)
                        if (!organisms.Any(x => string.Equals(x.Genus, o.Genus, StringComparison.OrdinalIgnoreCase))) organisms.Add(o);
                    foreach (var g in cached.KnownGenera) if (!genera.Contains(g, StringComparer.OrdinalIgnoreCase)) genera.Add(g);
                    foreach (var o in organisms) if (!genera.Contains(o.Genus, StringComparer.OrdinalIgnoreCase)) genera.Add(o.Genus);
                    s.Children.Add(DText($"Biological ({b.BioSignalCount})", TextMid, 11.5, true));
                    foreach (var g in genera)
                    {
                        var org = organisms.Where(o => string.Equals(o.Genus, g, StringComparison.OrdinalIgnoreCase)).OrderByDescending(o => o.IsComplete).ThenByDescending(o => o.ScanCount).FirstOrDefault();
                        bool done = org != null && org.IsComplete;
                        int dots = org == null ? 0 : done ? 3 : Math.Min(2, org.ScanCount);
                        string species = org != null && !string.IsNullOrEmpty(org.Species) ? org.DisplayName : g;
                        long payout = PayoutData.GetValue(species, false);
                        s.Children.Add(BioLine(species, dots, done ? "complete" : org == null ? "not started" : $"{org.ScanCount} of 3", done, payout));
                    }
                    // Signals the surface scan has not named a genus for yet: shown as a plain "Biological" row each, so the count is visible from the start
                    // and the genus replaces the placeholder once it is known.
                    for (int i = genera.Count; i < b.BioSignalCount; i++)
                        s.Children.Add(BioLine("Biological signal", 0, "genus unknown", false, 0, true));
                }
                var counts = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (b.GeoSignalCount > 0) counts.Children.Add(Chip($"geological {b.GeoSignalCount}", GeoColor, null));
                if (b.MiningSignalCount > 0) counts.Children.Add(Chip($"mining hotspots {b.MiningSignalCount}", Brush("#b978ff"), null));
                s.Children.Add(counts);
            }

            // Materials.
            if (b.Materials.Count > 0)
            {
                s.Children.Add(DSection("MATERIALS"));
                // Each material leads with the same coloured symbol chip the cards use, then its name and share.
                var matWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
                foreach (var m in b.Materials.OrderByDescending(x => x.Percent).Take(10))
                {
                    var col = MaterialColors.TryGetValue(m.Name, out var mc) ? mc : Brush("#9fd8e0");
                    var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 5) };
                    item.Children.Add(new Border
                    {
                        BorderBrush = col, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = MaterialSymbol(m.Name), Foreground = col, FontSize = 10.5, FontWeight = FontWeights.Bold },
                    });
                    item.Children.Add(new TextBlock { Text = m.Name, Foreground = TextBright, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                    item.Children.Add(new TextBlock { Text = $" {m.Percent:F0}%", Foreground = TextMid, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                    matWrap.Children.Add(item);
                }
                s.Children.Add(matWrap);
            }

            // Rings, moons, pair partners.
            var moons = _detailBodies.Where(x => !x.IsStar && x.ParentBodyID == b.BodyID && b.BodyID >= 0).OrderBy(x => x.BodyName).ToList();
            var realRings = b.Rings.Where(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase)).ToList();
            if (moons.Count > 0 || realRings.Count > 0 || _baryGroups.ContainsKey(b.BodyName))
            {
                s.Children.Add(DSection("RINGS, MOONS & PARTNERS"));
                foreach (var r in realRings)
                    s.Children.Add(DText($"{MainWindow.FormatRingClass(r.RingClass)} ring  ·  {r.InnerRad / 1000.0:N0} to {r.OuterRad / 1000.0:N0} km", TextBright, 11));
                if (moons.Count > 0)
                    s.Children.Add(DText("Moons: " + string.Join(", ", moons.Select(m => EliteWatcherService.GetShortBodyName(m.BodyName, _watcher.StarSystem).ToUpperInvariant())), TextBright, 11, false, new Thickness(0, 2, 0, 0)));
                if (_baryGroups.TryGetValue(b.BodyName, out var mates))
                {
                    var others = mates.Where(o => !string.Equals(o.BodyName, b.BodyName, StringComparison.OrdinalIgnoreCase));
                    s.Children.Add(DText("Orbits a shared centre with " + string.Join(", ", others.Select(o => EliteWatcherService.GetShortBodyName(o.BodyName, _watcher.StarSystem).ToUpperInvariant())), TextMid, 11, false, new Thickness(0, 2, 0, 0)));
                }
            }

            // Details toggle: lighting and render information (what the look tuning is based on).
            s.Children.Add(DRule());
            var toggleText = DText((_detailDetailsOpen ? "▾" : "▸") + "  Details (lighting & render)", TextMid, 11.5, false, new Thickness(0, 8, 0, 0));
            toggleText.Cursor = Cursors.Hand;
            var details = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = _detailDetailsOpen ? Visibility.Visible : Visibility.Collapsed };
            toggleText.MouseLeftButtonUp += (_, __) =>
            {
                _detailDetailsOpen = !_detailDetailsOpen;
                details.Visibility = _detailDetailsOpen ? Visibility.Visible : Visibility.Collapsed;
                toggleText.Text = (_detailDetailsOpen ? "▾" : "▸") + "  Details (lighting & render)";
            };
            s.Children.Add(toggleText);
            var (lname, ltemp, ldist, lrel) = _watcher.LightingInfo(b);
            void DRow(string k, string v)
            {
                var d = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var kt = DText(k, TextDim, 11); kt.Width = 128; DockPanel.SetDock(kt, Dock.Left); d.Children.Add(kt);
                d.Children.Add(DText(v, TextBright, 11));
                details.Children.Add(d);
            }
            DRow("Lit by", string.IsNullOrEmpty(lname) ? "—" : $"{EliteWatcherService.GetShortBodyName(lname, _watcher.StarSystem)}  ({ltemp:N0} K)");
            if (ldist > 0) DRow("Distance", ldist >= 0.1 ? $"{ldist:F2} AU from that star" : $"{ldist * 149_597_870.7:N0} km from that star");
            DRow("Relative light", lrel > 0 ? $"{lrel:F3} of full brightness (planet dimming x{Math.Clamp(Math.Sqrt(lrel), 0.2, 1.0):F2})" : "unknown");
            DRow("Body id", b.BodyID.ToString());
            DRow("Scan", b.ScanType);
            s.Children.Add(details);

            // Verdict buttons: appended to a plain log beside the app, which the look notes read from.
            var foot = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            Button FootBtn(string text) => new Button
            {
                Content = text, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(6, 0, 0, 0), Background = Brush("#0a1616"), Foreground = TextBright,
                BorderBrush = BorderC, BorderThickness = new Thickness(1), FontFamily = new FontFamily("Consolas"), FontSize = 11, Cursor = Cursors.Hand, Template = DarkButtonTemplate(),
            };
            var copy = FootBtn("copy name"); copy.Margin = new Thickness(0);
            copy.Click += (_, __) => { try { Clipboard.SetText(b.BodyName); copy.Content = "copied ✓"; } catch { } };
            DockPanel.SetDock(copy, Dock.Left); foot.Children.Add(copy);
            var bad = FootBtn("looks wrong ✗"); var good = FootBtn("render looks right ✓");
            void Verdict(string v, Button self, string extra = "")
            {
                try
                {
                    File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "LookVerdicts.log"),
                        $"{DateTime.UtcNow:O}\t{b.BodyName}\t{b.PlanetClass}\t{v}\t{b.SurfaceTemperature:F0}K\t{b.SurfacePressure:F0}Pa\t{extra}\r\n");
                    Verdicts()[b.BodyName] = v;
                    good.IsEnabled = bad.IsEnabled = false; self.Content = "noted ✓";
                }
                catch { }
            }
            if (Verdicts().TryGetValue(b.BodyName, out var given))
            {
                good.IsEnabled = bad.IsEnabled = false;
                (given == "RIGHT" ? good : bad).Content = "noted ✓";
            }
            good.Click += (_, __) => Verdict("RIGHT", good);
            bad.Click += (_, __) => { string appPath = AppRenderPath(b); Verdict("WRONG", bad, appPath); ShowPasteWindow(b, appPath); };
            DockPanel.SetDock(bad, Dock.Right); foot.Children.Add(bad);
            DockPanel.SetDock(good, Dock.Right); foot.Children.Add(good);
            foot.Children.Add(new Border());
            s.Children.Add(foot);
        }

        private UIElement BioLine(string name, int dots, string status, bool done, long payout, bool unknown = false)
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var pay = DText(payout > 0 ? $"≈ {PayoutData.FormatCredits(payout)}" : "", ValueColor, 11); pay.Width = 80; pay.TextAlignment = TextAlignment.Right; DockPanel.SetDock(pay, Dock.Right); line.Children.Add(pay);
            var st = DText(status, done ? TerraColor : TextDim, 11); st.Width = 88; st.TextAlignment = TextAlignment.Right; DockPanel.SetDock(st, Dock.Right); line.Children.Add(st);
            var dt = DText(new string('●', dots) + new string('○', 3 - dots), done ? TerraColor : dots > 0 ? Accent : TextDim, 11); dt.Width = 54; DockPanel.SetDock(dt, Dock.Right); line.Children.Add(dt);
            line.Children.Add(DText(name, unknown ? TextDim : TextBright, 11.5));
            return line;
        }

        // A flat dark button template: the stock WPF one turns white when the button is disabled (the verdict buttons became unreadable after a click).
        // Disabled keeps the dark background and only fades.
        private static ControlTemplate DarkButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            t.Triggers.Add(off);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#00e5ff")));
            t.Triggers.Add(hover);
            return t;
        }

        // Verdicts already given, by body name (RIGHT or WRONG): loaded once from LookVerdicts.log so they survive changing bodies, refreshes and restarts.
        private static Dictionary<string, string>? _verdictMemory;
        private static Dictionary<string, string> Verdicts()
        {
            if (_verdictMemory != null) return _verdictMemory;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "LookVerdicts.log");
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 4) d[parts[1]] = parts[3];
                    }
            }
            catch { }
            return _verdictMemory = d;
        }

        private static string FormatPressure(double pascals)
        {
            if (pascals >= 1.0e9) return $"{pascals / 1.0e9:F2} GPa";
            if (pascals >= 1.0e6) return $"{pascals / 1.0e6:F2} MPa";
            if (pascals >= 1.0e3) return $"{pascals / 1.0e3:F1} kPa";
            return $"{pascals:F0} Pa";
        }
    }
}
