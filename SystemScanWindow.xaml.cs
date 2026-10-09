using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EliteBioRadar
{
    // Real port of the "System Scan" mockup — see the design conversation for the reference
    // (EDDiscovery's own Scan screen) this was built against. Scoped to LOCAL data only for
    // this first pass: every body shown here has actually been scanned by the player this
    // session (that's the only way it's in GetCurrentSystemBodyDetails()), which is also
    // exactly what the real reference screenshots showed. EDSM-sourced "known but not
    // personally visited" bodies, the orbital distance map, and the co-orbiting-pair bracket
    // are deliberately deferred — noted at their would-be call sites below.
    public partial class SystemScanWindow : Window
    {
        private readonly EliteWatcherService _watcher;

        // Palette lifted straight from the design-mockup HTML so this matches it exactly.
        private static readonly SolidColorBrush Panel2      = Brush("#0e1c1c");
        private static readonly SolidColorBrush BorderC     = Brush("#1a4444");
        private static readonly SolidColorBrush BorderSoft  = Brush("#153636");
        private static readonly SolidColorBrush Accent      = Brush("#00e5ff");
        private static readonly SolidColorBrush AccentDim   = Brush("#0a8fa3");
        private static readonly SolidColorBrush TextBright  = Brush("#cfe8e8");
        private static readonly SolidColorBrush TextMid     = Brush("#88bbbb");
        private static readonly SolidColorBrush TextDim     = Brush("#4c7373");
        // Real Bio/Geo/Mining icons now come from MainWindow.MakeBioBadge/MakeGeoBadge/
        // MakeMiningBadge (own baked-in colors, matching the Planet tab) — GeoColor survives
        // only because the material-rarity pill coloring below still borrows it.
        private static readonly SolidColorBrush GeoColor    = Brush("#ffaa00");
        private static readonly SolidColorBrush TerraColor  = Brush("#7ee787");
        private static readonly SolidColorBrush TerraFill   = Brush("#123018");
        private static readonly SolidColorBrush HzColor     = Brush("#7ee787");
        private static readonly SolidColorBrush ValueColor  = Brush("#ffd166");
        // Same vivid yellow as MainWindow's InfoGravityWarnBrush — deliberately distinct from
        // ValueColor's softer amber (already claimed by "current value"/"high value") so this
        // reads as its own caution signal, not another shade of the value chip.
        private static readonly SolidColorBrush GravityWarnColor = Brush("#ffcc00");
        private static readonly SolidColorBrush GravityWarnFill  = Brush("#332900");
        // Same green as the Nickel material pill (and TerraColor/HzColor, which happen to share
        // it) — reused rather than a new hex value, per direct feedback asking for "the green of
        // Ni" specifically for the high-value "$" marker.
        private static readonly SolidColorBrush HighValueGreen = Brush("#7ee787");
        // Planet-type line ("High Metal Content Body") was TextDim (#4c7373), which read as dull
        // against the card's dark/tinted background. Brighter, but still a step below TextBright
        // (the body name above it) so the name stays the primary line.
        private static readonly SolidColorBrush ClassText = Brush("#a5d3d3");
        // "landable" used to share the muted TextMid grey-teal of every other chip, so a fact
        // that matters for actually going somewhere blended into "scanned"/"mapped". A warm coral
        // isn't used by any outline, signal badge or value color in this window (greens/blues/
        // teal/brown = body class + habitability, amber = credits, purple = mining), so it can't
        // be mistaken for any of them; the faint fill helps it hold up on tinted cards too.
        private static readonly SolidColorBrush LandableColor = Brush("#ff9f6b");
        private static readonly SolidColorBrush LandableFill  = Brush("#2a180f");
        // Class-specific card-outline colors, per direct feedback — these three particular
        // classes are valuable enough to want their own distinct outline even when they'd
        // otherwise just read as "terraformable" (green) or plain default. Earthlike gets the
        // brightest/most noticeable treatment on purpose (explicitly asked to stand out); Water
        // world a step more muted so the two blues read as related-but-different tiers; Ammonia
        // a dark red/brown, explicitly called "for now" — open to revisiting the color later.
        private static readonly SolidColorBrush EarthlikeColor  = Brush("#3d8bff");
        private static readonly SolidColorBrush WaterWorldColor = Brush("#4a6ea8");
        private static readonly SolidColorBrush AmmoniaColor    = Brush("#8a5a3c");
        // "Notable payout" threshold for the "$" marker. Originally 1,000,000 — lowered per
        // direct feedback citing EDDiscovery's own rule of thumb that a High Metal Content body
        // typically scans over 50,000. Verified this app's own number isn't the problem first:
        // hand-computed the formula against a real journal MassEM (0.049206 EM, a genuinely
        // light HMC body) and it matched the app's displayed value exactly (~30.3K) — so a
        // light HMC body legitimately landing under 50K is correct, not a bug, and the "typical"
        // figure is just an average over heavier-than-this bodies.
        private const double HighValueThreshold = 50_000;

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        // Finalized design decision: these classes count as "notable" purely on being a
        // consistently valuable class, regardless of signals/terraform/HZ (real trigger: a
        // Metal-Rich body was being buried in the minor list despite its usual high value).
        private static readonly HashSet<string> HighValueClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Metal rich body", "Water world", "Ammonia world", "Earthlike body", "Water giant",
        };

        // Shared by BuildCard and BuildMoonRow so the two never drift apart (a real earlier bug
        // here: the teal bio outline existed on cards for a while before anyone noticed it was
        // silently missing from moon rows). Priority, highest first: Earthlike/Water
        // world/Ammonia world get their own distinct class-specific color — even over
        // terraformable, per direct feedback ("even if they are terraformable") — since those
        // three are valuable enough on their own to want to stand out rather than blend into the
        // more common green. Terraformable and habitable-zone share the same green (explicitly
        // fine per feedback — terraformable can fall outside the HZ, so it needs SOME outline,
        // and reusing green rather than inventing a new color keeps "green = valuable/habitable
        // context" one consistent meaning). Bio signal (teal, matching the real bio badge) is
        // the weakest of these signals and comes last, before the plain default border.
        //
        // Thickness travels with color rather than being a separate lookup — every "notice me"
        // signal (anything but the plain default) gets 1px more per direct feedback, asking for
        // a little more visual weight on these specifically without disturbing card layout. Card
        // Padding (11px) comfortably absorbs a 1px difference in BorderThickness eating into the
        // content area, so this doesn't measurably shift anything inside.
        //
        // Background travels with it too now, per direct follow-up feedback — the outline alone
        // "doesn't catch the eye as much as hoped". A flat Panel2 background on every card no
        // matter how notable it is meant a 1-2px border edge was the ONLY visual signal; a subtle
        // wash of the same color across the whole card is far harder to miss at a glance, without
        // needing a bright/saturated fill that would fight the text sitting on top of it — see
        // TintedBackground's own blend ratio for how that balance is struck.
        private (SolidColorBrush color, double thickness, SolidColorBrush background) OutlineFor(BodyScanDetail b, bool inHz, bool terraformable)
        {
            if (string.Equals(b.PlanetClass, "Earthlike body", StringComparison.OrdinalIgnoreCase)) return (EarthlikeColor, 2, TintedBackground(EarthlikeColor.Color));
            if (string.Equals(b.PlanetClass, "Water world", StringComparison.OrdinalIgnoreCase))    return (WaterWorldColor, 2, TintedBackground(WaterWorldColor.Color));
            if (string.Equals(b.PlanetClass, "Ammonia world", StringComparison.OrdinalIgnoreCase))  return (AmmoniaColor, 2, TintedBackground(AmmoniaColor.Color));
            if (terraformable) return (HzColor, 2, TintedBackground(HzColor.Color));
            if (inHz) return (HzColor, 2, TintedBackground(HzColor.Color));
            if (b.BioSignalCount > 0) return (Accent, 2, TintedBackground(Accent.Color));
            return (BorderSoft, 1, Panel2);
        }

        // A low, fixed blend of the outline color into the card's own normal Panel2 background —
        // not a semi-transparent overlay (would need the whole card tree to composite over
        // whatever sits behind it, which isn't guaranteed to be uniform) or a bright saturated
        // fill (would fight the white/dim text already sitting on every card). 0.14 was picked
        // by eye: enough of a hue shift to read as "this card is different" from across the
        // window, while every existing text color (TextBright/TextDim/TextMid) still reads
        // clearly against it — push it much higher and pale text starts losing contrast.
        private static SolidColorBrush TintedBackground(Color tint)
        {
            const double amount = 0.14;
            var b = Panel2.Color;
            byte Blend(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
            return new SolidColorBrush(Color.FromRgb(Blend(b.R, tint.R), Blend(b.G, tint.G), Blend(b.B, tint.B)));
        }


        // "Notable Only" (default) vs "All Bodies" — the mockup's own toolbar chips, implemented
        // as one window-wide toggle rather than a per-star chip for now.
        private bool _showAllBodies;
        private bool _hideBelts;
        private bool _hideFullMats;
        // Re-read fresh on every Populate() (see below), same convention as the EDSM chip's own
        // settings read — so a change made in MainWindow's Settings panel while this window is
        // already open takes effect on the next refresh instead of needing a reopen.
        private bool _gravityWarningEnabled;
        private double _gravityWarningThresholdG;

        // Real gap: this window only ever populated on open or a manual ⟳ click — nothing
        // watched for a system change (or, per direct feedback, newly-FSS-scanned bodies
        // within the SAME system) while it sat open. A light poll — building this signature is
        // exactly the same work Populate() already does when it reads GetCurrentSystemBodyDetails,
        // so comparing it every second is cheap; only a real difference triggers the actually
        // expensive full rebuild — rather than hunting for the right event, since
        // EliteWatcherService has no single event that reliably covers both "jumped" and "a
        // body was scanned" cases together.
        private string _lastSystemName = "";
        private string _lastBodySignature = "";
        private DispatcherTimer? _watchTimer;

        // One string capturing everything this window actually displays about each body, so a
        // real change to any of it (a new body appearing, signals arriving, mapping completing)
        // is detected the same way a system change is — a plain string compare.
        private string BuildBodySignature() => string.Join("|",
            _watcher.GetCurrentSystemBodyDetails()
                .OrderBy(b => b.BodyName, StringComparer.OrdinalIgnoreCase)
                .Select(b => $"{b.BodyName}:{b.BioSignalCount}:{b.GeoSignalCount}:{b.MiningSignalCount}:{b.IsMapped}:{b.Materials.Count}:{b.WasDiscovered}:{b.WasMapped}:{b.WasFootfalled}"))
            // Version bumps when the discovery index finishes its startup pass (or a live
            // discovery lands), so the D badges appear/disappear without needing a body to change.
            + "#" + DiscoveryIndex.Version + "#" + MaterialInventory.Version;

        public SystemScanWindow(EliteWatcherService watcher)
        {
            InitializeComponent();
            _watcher = watcher;

            // Underline-on-hover is the only affordance the chip needs — its color already
            // conveys enabled/disabled, and Populate() rewrites that color on every refresh, so
            // hover uses TextDecorations (independent of Foreground) rather than trying to
            // remember and restore a "base" color around the hover state.
            txtEdsmChip.MouseEnter += (_, __) => txtEdsmChip.TextDecorations = TextDecorations.Underline;
            txtEdsmChip.MouseLeave += (_, __) => txtEdsmChip.TextDecorations = null;

            // Same "remember where I left it" convention as the main window, plus the two view
            // toggles — restored before Populate() so the button labels/content reflect the
            // saved state from the very first render, not a default that flips a moment later.
            var saved = AppSettings.Load();
            _showAllBodies = saved.SystemScanShowAllBodies;
            _hideBelts = saved.SystemScanHideBelts;
            btnToggleAll.Content = _showAllBodies ? "All Bodies" : "Notable Only";
            btnToggleBelts.Content = _hideBelts ? "Show Belts" : "Hide Belts";
            _hideFullMats = saved.SystemScanHideFullMaterials;
            btnToggleFullMats.Content = _hideFullMats ? "Show Full Mats" : "Hide Full Mats";

            if (saved.SystemScanLeft.HasValue && saved.SystemScanTop.HasValue)
            {
                double left = saved.SystemScanLeft.Value, top = saved.SystemScanTop.Value;
                double width = saved.SystemScanWidth ?? Width, height = saved.SystemScanHeight ?? Height;
                // Verify it's still on a connected screen first — same guard as the main window,
                // so a saved position from a monitor that's no longer attached doesn't open the
                // window somewhere unreachable off-screen.
                bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s =>
                    left < s.WorkingArea.Right && left + width > s.WorkingArea.Left &&
                    top < s.WorkingArea.Bottom && top + height > s.WorkingArea.Top);
                if (onScreen)
                {
                    Left = left; Top = top; Width = width; Height = height;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }

            Closed += (_, __) =>
            {
                SaveWindowState();
                _watchTimer?.Stop();
            };
            Populate();
            _lastSystemName = _watcher.StarSystem;
            _lastBodySignature = BuildBodySignature();

            _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _watchTimer.Tick += (_, __) =>
            {
                bool systemChanged = !string.Equals(_watcher.StarSystem, _lastSystemName, StringComparison.OrdinalIgnoreCase);
                var bodySig = BuildBodySignature();
                bool bodiesChanged = bodySig != _lastBodySignature;
                if (systemChanged || bodiesChanged)
                {
                    _lastSystemName = _watcher.StarSystem;
                    _lastBodySignature = bodySig;
                    Populate();
                }
            };
            _watchTimer.Start();
        }

        private void SaveWindowState()
        {
            var s = AppSettings.Load();
            s.SystemScanLeft = Left;
            s.SystemScanTop = Top;
            s.SystemScanWidth = Width;
            s.SystemScanHeight = Height;
            s.SystemScanShowAllBodies = _showAllBodies;
            s.SystemScanHideBelts = _hideBelts;
            s.SystemScanHideFullMaterials = _hideFullMats;
            AppSettings.Save(s);
        }

        private void BtnToggleFullMats_Click(object sender, RoutedEventArgs e)
        {
            _hideFullMats = !_hideFullMats;
            btnToggleFullMats.Content = _hideFullMats ? "Show Full Mats" : "Hide Full Mats";
            SaveWindowState();
            Populate();
        }

        // Chips for a body's materials (largest share first), wrapping onto extra rows instead of
        // capping at six. With the filter on, materials the commander is completely full on are
        // left out; returns null when nothing is left to show.
        private UIElement? BuildMaterialChips(BodyScanDetail b, Thickness margin)
        {
            var shown = b.Materials.OrderByDescending(x => x.Percent)
                .Where(x => !(_hideFullMats && MaterialInventory.IsFull(x.Name))).ToList();
            if (shown.Count == 0) return null;
            var mats = new WrapPanel { Margin = margin };
            foreach (var (name, _) in shown) mats.Children.Add(MaterialPill(name, b));
            return mats;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // Opens the same EDSM Integration controls Settings already has (commander name, API
        // key, enable toggle) on the main window, rather than building a second, separate copy
        // of that form here that could drift out of sync with it.
        private void TxtEdsmChip_Click(object sender, MouseButtonEventArgs e)
        {
            if (Owner is MainWindow mw) mw.OpenEdsmSettings();
        }
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Populate();

        private void BtnToggleAll_Click(object sender, RoutedEventArgs e)
        {
            _showAllBodies = !_showAllBodies;
            btnToggleAll.Content = _showAllBodies ? "All Bodies" : "Notable Only";
            SaveWindowState();
            Populate();
        }

        private void BtnToggleBelts_Click(object sender, RoutedEventArgs e)
        {
            _hideBelts = !_hideBelts;
            btnToggleBelts.Content = _hideBelts ? "Show Belts" : "Hide Belts";
            SaveWindowState();
            Populate();
        }

        // Reached by MainWindow when a setting this window reads on every Populate() (currently
        // just the gravity warning) changes while this window is already open — without this,
        // the change would sit unapplied until something else happened to trigger a refresh
        // (a body change, clicking ⟳, or closing and reopening the window).
        public void RefreshFromSettings() => Populate();

        private void Populate()
        {
            rootStack.Children.Clear();
            var systemName = _watcher.StarSystem;
            txtSysName.Text = string.IsNullOrEmpty(systemName) ? "—" : systemName.ToUpperInvariant();

            // Honest status, not the mockup's implied "synced Xh ago" — EDSM download/lookup
            // isn't wired into this window yet (see the design conversation), so this only
            // reflects whether the real upload integration in Settings is actually on.
            var edsmSettings = AppSettings.Load();
            txtEdsmChip.Text = edsmSettings.EdsmEnabled ? "● EDSM upload enabled" : "EDSM integration off";
            txtEdsmChip.Foreground = edsmSettings.EdsmEnabled ? Brush("#7ee787") : TextDim;
            _gravityWarningEnabled = edsmSettings.GravityWarningEnabled;
            _gravityWarningThresholdG = edsmSettings.GravityWarningThresholdG;

            var bodies = _watcher.GetCurrentSystemBodyDetails();
            if (_hideBelts) bodies = bodies.Where(b => !b.IsBelt).ToList();

            // Real report + screenshot: scanning a ring for mining hotspots fires its own Scan/
            // SAASignalsFound events, with a real BodyName ("...BCD 2 A Ring") and a Parents
            // entry pointing at the planet it belongs to — structurally identical to a real
            // moon, so it rendered as one: a blank card with a dash where the class should be
            // (a ring scan carries no PlanetClass/gravity/anything a real body has). That data is
            // already fully captured on the parent's own Rings list (RingClass/InnerRad/OuterRad)
            // and via RingHotspots (keyed by this exact ring name for the Ring Mining badge/stat)
            // — this separate body entry is a byproduct of the scan mechanic, not a real orbiting
            // body, and adds nothing by being shown. Filtered by an exact name match against the
            // claimed parent's own Rings list rather than a name-suffix guess.
            var earlyById = bodies.Where(b => b.BodyID >= 0).GroupBy(b => b.BodyID).ToDictionary(g => g.Key, g => g.First());
            bodies = bodies.Where(b => !(b.IsMoon && earlyById.TryGetValue(b.ParentBodyID, out var maybeParent)
                && maybeParent.Rings.Any(r => string.Equals(r.Name, b.BodyName, StringComparison.OrdinalIgnoreCase)))).ToList();

            // Bodies that genuinely orbit EACH OTHER (binary/trinary/...): same barycenter AND a
            // real Star/Planet parent above it. A body whose Parents is only {"Null":N} orbits a
            // barycenter with no parent (e.g. planets circling a binary star pair) — many
            // unrelated planets can share that, so it isn't a bound group.
            _baryGroups = bodies.Where(b => !b.IsStar && !b.IsBelt && b.BarycenterID >= 0 && b.ParentBodyID >= 0)
                .GroupBy(b => (b.ParentBodyID, b.BarycenterID))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Select(b => (b, list: g.ToList())))
                .ToDictionary(x => x.b.BodyName, x => x.list, StringComparer.OrdinalIgnoreCase);
            rootStack.Children.Add(BuildTripBar());

            if (bodies.Count == 0)
            {
                rootStack.Children.Add(new TextBlock
                {
                    Text = "No scan data for this system yet — honk or scan a few bodies, then hit ⟳.",
                    Foreground = TextDim, FontSize = 13, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap,
                });
                return;
            }

            var byId = bodies.Where(b => b.BodyID >= 0)
                .GroupBy(b => b.BodyID).ToDictionary(g => g.Key, g => g.First());

            rootStack.Children.Add(BuildSummary(bodies, systemName));

            var stars = bodies.Where(b => b.IsStar).OrderBy(b => b.BodyID).ToList();
            if (stars.Count == 0)
            {
                rootStack.Children.Add(BuildStarSection(null, bodies.Where(b => !b.IsStar).ToList(), byId));
            }
            else
            {
                // A star that itself orbits another star (real Parents-array "Star" entry, same
                // resolution any other body already gets) and has no bodies of its own orbiting
                // it doesn't get a whole separate top-level section — real report: a Y-dwarf
                // ("Scheau Phoe HW-T d4-13 1", Parents [{"Null":20},{"Star":0}]) got a full
                // independent section (header, own habitable zone, own divider) for essentially
                // nothing, reading like an unrelated second star system. Per feedback, its own
                // plain-numbered short name ("1", not a lettered secondary-star designation)
                // already marks it as just another orbiting body — folded into its parent's
                // section as a compact card instead (see BuildAttachedStarCard). A star that DOES
                // have its own orbiting bodies still gets a real section, since there's genuine
                // content to show under it.
                bool OrbitsAnotherStar(BodyScanDetail s) =>
                    s.ParentBodyID >= 0 && byId.TryGetValue(s.ParentBodyID, out var p) && p.IsStar;
                var rootStars = new List<BodyScanDetail>();
                var attachedStars = new List<BodyScanDetail>();
                foreach (var s in stars)
                {
                    bool hasOwnChildren = bodies.Any(b => !b.IsStar && RootStarBodyId(b, byId) == s.BodyID);
                    if (OrbitsAnotherStar(s) && !hasOwnChildren) attachedStars.Add(s); else rootStars.Add(s);
                }

                // Root stars that are genuine binary/trinary PEERS — share a barycenter with no
                // resolvable single "Star" parent for either one (the standard real-Elite
                // companion-star pattern; see the same BarycenterID/ParentBodyID convention
                // _baryGroups already uses for planets, just applied to stars here). Real report:
                // these rendered as fully independent sections with nothing at all connecting
                // them, even though they're gravitationally bound. Grouped and rendered together
                // under one shared left-edge bracket instead of individually.
                var rootStarBaryGroups = rootStars.Where(s => s.BarycenterID >= 0 && s.ParentBodyID < 0)
                    .GroupBy(s => s.BarycenterID)
                    .Where(g => g.Count() > 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var placedRootStars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool firstGroup = true;
                foreach (var star in rootStars)
                {
                    if (placedRootStars.Contains(star.BodyName)) continue;

                    List<BodyScanDetail> group = star.BarycenterID >= 0 && rootStarBaryGroups.TryGetValue(star.BarycenterID, out var mates)
                        ? mates
                        : new List<BodyScanDetail> { star };

                    if (!firstGroup) rootStack.Children.Add(BuildStarDivider());
                    firstGroup = false;

                    UIElement BuildOneStarSection(BodyScanDetail s)
                    {
                        var owned = bodies.Where(b => !b.IsStar && RootStarBodyId(b, byId) == s.BodyID).ToList();
                        var attachedHere = attachedStars.Where(a => a.ParentBodyID == s.BodyID).ToList();
                        return BuildStarSection(s, owned, byId, attachedStars: attachedHere);
                    }

                    if (group.Count == 1)
                    {
                        placedRootStars.Add(star.BodyName);
                        rootStack.Children.Add(BuildOneStarSection(star));
                        continue;
                    }

                    var groupStack = new StackPanel();
                    var names = string.Join(", ", group.Select(g => EliteWatcherService.GetShortBodyName(g.BodyName, _watcher.StarSystem)));
                    var label = new TextBlock
                    {
                        Text = GroupWord(group.Count).ToUpperInvariant() + " SYSTEM", Foreground = AccentDim,
                        FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 8, 0, 0),
                    };
                    AttachTip(label, BuildTextTip($"{char.ToUpper(GroupWord(group.Count)[0])}{GroupWord(group.Count).Substring(1)} system — {names}",
                        "These stars orbit each other around a shared barycenter — each keeps its own full section below since they each have their own real bodies."));
                    groupStack.Children.Add(label);
                    for (int gi = 0; gi < group.Count; gi++)
                    {
                        placedRootStars.Add(group[gi].BodyName);
                        if (gi > 0) groupStack.Children.Add(BuildStarDivider());
                        groupStack.Children.Add(BuildOneStarSection(group[gi]));
                    }
                    // A plain left border just read as a thin sidebar, not a bracket — per
                    // feedback, add short top/bottom ticks so it actually reads as "[". Overlaid
                    // in a Grid (no Border/Padding reserving space) rather than wrapped, so the
                    // star sections underneath don't shift or indent at all — the stem/ticks sit
                    // inside BuildStarSection's own existing left margin (12px) instead.
                    var groupGrid = new Grid();
                    groupGrid.Children.Add(groupStack);
                    var stem = new Border
                    {
                        BorderBrush = AccentDim, BorderThickness = new Thickness(2, 0, 0, 0),
                        Width = 8, Margin = new Thickness(0, 14, 0, 14),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch,
                    };
                    var topTick = new Border
                    {
                        BorderBrush = AccentDim, BorderThickness = new Thickness(0, 2, 0, 0),
                        Width = 8, Margin = new Thickness(0, 14, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    };
                    var bottomTick = new Border
                    {
                        BorderBrush = AccentDim, BorderThickness = new Thickness(0, 0, 0, 2),
                        Width = 8, Margin = new Thickness(0, 0, 0, 14),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    };
                    groupGrid.Children.Add(stem);
                    groupGrid.Children.Add(topTick);
                    groupGrid.Children.Add(bottomTick);
                    rootStack.Children.Add(groupGrid);
                }

                // Real bug: every body was only ever shown under a star it could be traced up to,
                // so anything that couldn't silently vanished from this window. Two real ways
                // that happens:
                //  - Orbits a barycenter, not a star — the journal's Parents is only {"Null":N}
                //    (real case: "ABC 1"-"ABC 4" circling the shared center of a three-star
                //    system, none of them tied to any one star). Root id comes back -1.
                //  - Its parent star (or parent planet, for a moon) hasn't been scanned yet, so
                //    there's no entry to walk up to and the trail ends at an id that isn't any
                //    known star.
                // Each gets its own section below the stars rather than being dropped.
                var starIds = new HashSet<int>(stars.Select(s => s.BodyID));
                var unowned = bodies.Where(b => !b.IsStar && !starIds.Contains(RootStarBodyId(b, byId))).ToList();
                var barycentric = unowned.Where(b => RootStarBodyId(b, byId) == -1).ToList();
                var unresolved  = unowned.Where(b => RootStarBodyId(b, byId) != -1).ToList();
                if (barycentric.Count > 0)
                {
                    rootStack.Children.Add(BuildStarDivider());
                    rootStack.Children.Add(BuildStarSection(null, barycentric, byId, "BARYCENTER ORBIT"));
                }
                if (unresolved.Count > 0)
                {
                    rootStack.Children.Add(BuildStarDivider());
                    rootStack.Children.Add(BuildStarSection(null, unresolved, byId, "PARENT NOT YET SCANNED", "parent star/planet not in this system's scan data yet"));
                }
            }
        }

        // A soft center-bright gradient line rather than a flat one — reads as a deliberate
        // section break instead of a stray leftover rule, and matches the sci-fi-panel feel of
        // the rest of this window (subtle glow, not a hard edge) better than a plain solid bar.
        private UIElement BuildStarDivider() => new Border
        {
            Height = 1, Margin = new Thickness(16, 8, 16, 8),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0.0),
                    new GradientStop(BorderC.Color, 0.5),
                    new GradientStop(Colors.Transparent, 1.0),
                },
                new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5)),
        };

        // Walks a moon's ParentBodyID chain up through any intermediate moons (a moon can
        // itself have a moon) until it reaches a top-level planet, then returns THAT planet's
        // own ParentBodyID — which the real journal schema always bottoms out at a star.
        private static int RootStarBodyId(BodyScanDetail body, Dictionary<int, BodyScanDetail> byId)
        {
            var current = body;
            int guard = 0;
            while (current.IsMoon && guard++ < 12)
            {
                if (!byId.TryGetValue(current.ParentBodyID, out var parent)) break;
                current = parent;
            }
            return current.ParentBodyID;
        }

        // ---------------------------------------------------------------
        //  Trip bar
        // ---------------------------------------------------------------
        private UIElement BuildTripBar()
        {
            var bar = new Border
            {
                Background = Panel2, BorderBrush = BorderC, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8),
            };
            var dock = new DockPanel();

            var resetBtn = new Border
            {
                BorderBrush = BorderC, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(9, 4, 9, 4), Cursor = Cursors.Hand,
            };
            var resetTb = new TextBlock { Text = "Start New Trip", Foreground = TextMid, FontSize = 10.5 };
            resetBtn.Child = resetTb;
            resetBtn.MouseEnter += (_, __) => { resetBtn.BorderBrush = AccentDim; resetTb.Foreground = Accent; };
            resetBtn.MouseLeave += (_, __) => { resetBtn.BorderBrush = BorderC; resetTb.Foreground = TextMid; };
            resetBtn.MouseLeftButtonUp += (_, __) =>
            {
                var result = MessageBox.Show(this, "Start a new trip? This resets the trip total to zero — your lifetime Earnings total in Settings is unaffected.",
                    "Start New Trip", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) { TripTracker.StartNewTrip(); Populate(); }
            };
            DockPanel.SetDock(resetBtn, Dock.Right);
            dock.Children.Add(resetBtn);

            var importBtn = new Border
            {
                BorderBrush = BorderC, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(9, 4, 9, 4), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Add scan values from your journal history (all journals, or a date range) to the trip total",
            };
            var importTb = new TextBlock { Text = "Import Trip Data", Foreground = TextMid, FontSize = 10.5 };
            importBtn.Child = importTb;
            importBtn.MouseEnter += (_, __) => { importBtn.BorderBrush = AccentDim; importTb.Foreground = Accent; };
            importBtn.MouseLeave += (_, __) => { importBtn.BorderBrush = BorderC; importTb.Foreground = TextMid; };
            importBtn.MouseLeftButtonUp += async (_, __) => await RunTripImportAsync();
            DockPanel.SetDock(importBtn, Dock.Right);
            dock.Children.Add(importBtn);

            var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = "TRIP SCANS  ", Foreground = TextDim, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            info.Children.Add(new TextBlock { Text = $"≈ {PayoutData.FormatCredits((long)TripTracker.TripTotal)}", Foreground = ValueColor, FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            var since = TripTracker.TripStartUtc;
            var elapsed = DateTime.UtcNow - since;
            info.Children.Add(new TextBlock
            {
                Text = $"   {TripTracker.TripBodyCount} bodies · started {since.ToLocalTime():HH:mm} ({FormatElapsed(elapsed)} ago)",
                Foreground = TextDim, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
            });
            dock.Children.Add(info);

            bar.Child = dock;
            return bar;
        }

        private bool _tripImporting;

        private async Task RunTripImportAsync()
        {
            if (_tripImporting) return;
            var choice = ShowTripImportDialog();
            if (choice == null) return;
            var (from, to) = choice.Value;

            _tripImporting = true;
            try
            {
                Title = "System Scan — importing trip data…";
                Mouse.OverrideCursor = Cursors.Wait;
                var r = await TripImporter.ImportAsync(EliteWatcherService.GetJournalDirectory(), from, to);
                Mouse.OverrideCursor = null;
                Title = "System Scan";
                Populate();
                MessageBox.Show(this,
                    r.Bodies == 0
                        ? "No scanned bodies were found in that range."
                        : $"Imported {r.Bodies:N0} bodies (≈ {PayoutData.FormatCredits((long)r.Total)}) from {r.FilesRead:N0} journal file{(r.FilesRead == 1 ? "" : "s")} into the trip total.",
                    "Import Trip Data", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                Title = "System Scan";
                Log.Write($"SystemScanWindow trip import error: {ex}");
                MessageBox.Show(this, "Import failed — see EliteBioRadar.log for details.", "Import Trip Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { _tripImporting = false; }
        }

        // Returns null if cancelled; (null,null) means all journals.
        private (DateTime? from, DateTime? to)? ShowTripImportDialog()
        {
            var dlg = new Window
            {
                Title = "Import Trip Data", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                Background = Panel2, ShowInTaskbar = false,
            };
            var sp = new StackPanel { Margin = new Thickness(18, 16, 18, 16), MinWidth = 330 };
            sp.Children.Add(new TextBlock { Text = "Import Trip Data", Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 14 });
            sp.Children.Add(new TextBlock
            {
                Text = "Adds the estimated value of scanned bodies from your journals to the current trip. A body already counted is updated, not double-counted.",
                Foreground = TextMid, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
            });

            var rbAll = new RadioButton { Content = "All journals", IsChecked = true, Foreground = TextBright, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            var rbRange = new RadioButton { Content = "Date range", Foreground = TextBright, FontSize = 12 };
            sp.Children.Add(rbAll); sp.Children.Add(rbRange);

            var today = DateTime.Today;
            var dpFrom = new DatePicker { SelectedDate = TripTracker.TripStartUtc.ToLocalTime().Date, DisplayDateEnd = today, Width = 120 };
            var dpTo   = new DatePicker { SelectedDate = today, DisplayDateEnd = today, Width = 120 };
            var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 0, 0), IsEnabled = false };
            rangeRow.Children.Add(new TextBlock { Text = "From", Foreground = TextMid, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            rangeRow.Children.Add(dpFrom);
            rangeRow.Children.Add(new TextBlock { Text = "to", Foreground = TextMid, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
            rangeRow.Children.Add(dpTo);
            sp.Children.Add(rangeRow);
            rbRange.Checked += (_, __) => rangeRow.IsEnabled = true;
            rbAll.Checked += (_, __) => rangeRow.IsEnabled = false;

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Import", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), IsCancel = true };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            sp.Children.Add(btns);
            dlg.Content = sp;

            ok.Click += (_, __) =>
            {
                if (rbRange.IsChecked == true && dpFrom.SelectedDate > dpTo.SelectedDate)
                {
                    MessageBox.Show(dlg, "The start date is after the end date.", "Import Trip Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                dlg.DialogResult = true;
            };

            if (dlg.ShowDialog() != true) return null;
            return rbRange.IsChecked == true ? (dpFrom.SelectedDate, dpTo.SelectedDate) : (null, null);
        }

        // Real report: a long-running trip (months of real time — "Start New Trip" isn't
        // something most players click often) showed as "4953h 40m", which nobody reads as a
        // duration at a glance. Cascades through months/days/hours/minutes instead, showing the
        // two most significant units present, same convention as the existing "Xh Ym" style.
        // "Month" here means a flat 30 days, not a calendar month — TimeSpan has no calendar
        // awareness, and exact calendar months would need a real start/end date pair this
        // display doesn't have, just an elapsed duration.
        private static string FormatElapsed(TimeSpan t)
        {
            int totalDays = (int)t.TotalDays;
            if (totalDays >= 30)
            {
                int months = totalDays / 30, days = totalDays % 30;
                return days > 0 ? $"{months}mo {days}d" : $"{months}mo";
            }
            if (totalDays >= 1) return t.Hours > 0 ? $"{totalDays}d {t.Hours}h" : $"{totalDays}d";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
            return "just now";
        }

        // ---------------------------------------------------------------
        //  Summary strip
        // ---------------------------------------------------------------
        private UIElement BuildSummary(List<BodyScanDetail> bodies, string systemName)
        {
            var grid = new UniformGrid { Columns = 3 };
            var starCount = bodies.Count(b => b.IsStar);
            var nonStarNonBelt = bodies.Where(b => !b.IsStar && !b.IsBelt).ToList();
            var beltCount = bodies.Count(b => b.IsBelt);
            double totalValue = nonStarNonBelt.Sum(b => ScanValueEstimator.Estimate(b).credits);

            // FSD injection synthesis chip — whether this system's already-scanned bodies,
            // between them, carry every raw material a given recipe needs. Every scanned body's
            // own Materials list contributes (stars/belts simply never have one, so unioning
            // across ALL bodies rather than filtering them out first is equivalent and simpler).
            // The three recipes aren't tiers of each other (Standard isn't just Basic plus more,
            // Premium swaps out Vanadium/Cadmium for Arsenic/Yttrium/Polonium entirely), so more
            // than one could be satisfied at once — per feedback, only the one needing the most
            // materials is shown rather than stacking chips. Computed before the Stars stat cell
            // (rather than appended below the whole row) so it can sit right under Stars, per
            // feedback that it read as too far removed from the summary stats up there.
            var haveMaterials = new HashSet<string>(
                bodies.SelectMany(b => b.Materials.Select(m => m.Name)), StringComparer.OrdinalIgnoreCase);
            bool HasAll(params string[] names) => names.All(haveMaterials.Contains);

            (string label, SolidColorBrush color, string[] needs)? bestFsdRecipe = null;
            void ConsiderRecipe(string label, SolidColorBrush color, params string[] needs)
            {
                if (!HasAll(needs)) return;
                if (bestFsdRecipe == null || needs.Length > bestFsdRecipe.Value.needs.Length)
                    bestFsdRecipe = (label, color, needs);
            }
            ConsiderRecipe("basic FSD injection materials present", GeoColor, "Carbon", "Vanadium", "Germanium");
            ConsiderRecipe("standard FSD injection materials present", EarthlikeColor, "Carbon", "Vanadium", "Germanium", "Cadmium", "Niobium");
            ConsiderRecipe("premium FSD injection materials present", TerraColor, "Carbon", "Germanium", "Arsenic", "Niobium", "Yttrium", "Polonium");

            UIElement? fsdChip = null;
            if (bestFsdRecipe.HasValue)
            {
                var (label, color, _) = bestFsdRecipe.Value;
                fsdChip = Chip(label, color, TintedBackground(color.Color));
            }

            grid.Children.Add(Stat("Stars", starCount == 1 ? "1 scanned" : $"{starCount} scanned", null, fsdChip));
            grid.Children.Add(Stat("Bodies", $"{bodies.Count(b => !b.IsStar)} scanned", beltCount > 0 ? $"{beltCount} belt cluster{(beltCount == 1 ? "" : "s")}" : null));
            grid.Children.Add(Stat("Est. Value (scanned)", $"≈ {PayoutData.FormatCredits((long)totalValue)}", "mapped-bonus where applicable"));

            var border = new Border { BorderBrush = BorderSoft, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
            return border;
        }

        private UIElement Stat(string key, string value, string? sub, UIElement? extra = null)
        {
            var stack = new StackPanel { Margin = new Thickness(16, 11, 16, 11) };
            stack.Children.Add(new TextBlock { Text = key.ToUpperInvariant(), Foreground = TextDim, FontSize = 10, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = value, Foreground = TextBright, FontSize = 14, FontWeight = FontWeights.Bold });
            if (!string.IsNullOrEmpty(sub))
                stack.Children.Add(new TextBlock { Text = sub, Foreground = TextDim, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) });
            if (extra != null)
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                wrap.Children.Add(extra);
                stack.Children.Add(wrap);
            }
            return new Border { Child = stack };
        }

        // ---------------------------------------------------------------
        //  Per-star section
        // ---------------------------------------------------------------
        // sectionTitle/sectionNote only apply when there's no star (star == null): they name a
        // group of bodies that couldn't be traced to any scanned star — see Populate. Left null,
        // the header stays the old "UNKNOWN STAR" (used when the system has no scanned star at all).
        // attachedStars — other real stars that orbit THIS star but have no orbiting bodies of
        // their own (see Populate) — join the numbered body list below as compact cards
        // (BuildAttachedStarCard) rather than getting their own section.
        private UIElement BuildStarSection(BodyScanDetail? star, List<BodyScanDetail> owned, Dictionary<int, BodyScanDetail> byId,
            string? sectionTitle = null, string? sectionNote = null, List<BodyScanDetail>? attachedStars = null)
        {
            var section = new StackPanel { Margin = new Thickness(12, 12, 12, 4) };

            // Bigger than before per feedback — meant to read as clearly more prominent than a
            // planet card's own name/class text, without going as far as the card's full
            // thumbnail treatment.
            var hz = star != null ? ScanValueEstimator.HabitableZoneAU(star) : null;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            if (star != null)
            {
                // A black hole has no surface temperature to look up — GetStarColors' fallback
                // is a generic 5700K (G-star, yellow-white), which rendered this swatch dot as a
                // plain yellow star instead of a black hole. Real report.
                Brush swatchFill;
                if (StarRenderer.IsBlackHole(star.StarType))
                {
                    // RadialGradientBrush(a, b) puts `a` at the CENTER and `b` at the edge — a
                    // two-stop (amber, black) brush put amber in the center and black at the
                    // rim, the opposite of a black hole (real report: still showed yellow).
                    // Mostly black through most of the radius, with just a thin amber rim right
                    // at the very edge, reads correctly even at this swatch's small 24px size.
                    var bh = new RadialGradientBrush();
                    bh.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                    bh.GradientStops.Add(new GradientStop(Colors.Black, 0.72));
                    bh.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0xb3, 0x47), 0.88));
                    bh.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0x6a, 0x2e), 1.0));
                    swatchFill = bh;
                }
                else
                {
                    var (core, mid, _) = StarRenderer.GetStarColors(star.SurfaceTemperature > 0 ? star.SurfaceTemperature : 5700, star.StarType);
                    swatchFill = new RadialGradientBrush(mid, core);
                }
                head.Children.Add(new Ellipse
                {
                    Width = 24, Height = 24, Margin = new Thickness(0, 0, 11, 0),
                    Fill = swatchFill,
                });
                head.Children.Add(new TextBlock { Text = EliteWatcherService.GetShortBodyName(star.BodyName, _watcher.StarSystem).ToUpperInvariant(), Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) });
                head.Children.Add(new TextBlock { Text = $"{star.StarType}-type · {star.StellarMass:F2} SM", Foreground = TextDim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, hz.HasValue ? 11 : 0, 0) });
                // Moved up onto the header line itself (used to be its own line below) and
                // colored to match the habitable-zone outline/chip color used elsewhere on this
                // window, per feedback — it used to be plain dim text, easy to miss under the
                // header.
                if (hz.HasValue)
                    head.Children.Add(new TextBlock
                    {
                        Text = $"habitable zone ≈ {hz.Value.innerAU * 499:N0}–{hz.Value.outerAU * 499:N0} ls",
                        Foreground = HzColor, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                    });
                if (star.DistanceFromArrivalLS >= 1)
                    head.Children.Add(new TextBlock
                    {
                        Text = $"distance from primary {star.DistanceFromArrivalLS:N0} ls",
                        Foreground = TextDim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(11, 0, 0, 0),
                    });
            }
            else
            {
                head.Children.Add(new TextBlock { Text = sectionTitle ?? "UNKNOWN STAR", Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 18, Margin = new Thickness(0, 0, sectionNote != null ? 11 : 0, 0) });
                if (sectionNote != null)
                    head.Children.Add(new TextBlock { Text = sectionNote, Foreground = TextDim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            }
            section.Children.Add(head);

            // Same ring-class chip as planet/moon cards and the attached-star card — a root
            // star (primary or a true binary/trinary peer) can carry real rings too, and this
            // header was the one place that still didn't show them.
            if (star != null)
            {
                var starRingClasses = star.Rings.Where(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.RingClass).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (starRingClasses.Count > 0)
                {
                    var starRingChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                    foreach (var ringClass in starRingClasses)
                        starRingChips.Children.Add(Chip(MainWindow.FormatRingClass(ringClass).ToLowerInvariant() + " rings", HzColor, null));
                    section.Children.Add(starRingChips);
                }
            }

            // Real regression, caught on a live screenshot: making real SemiMajorAxis the
            // PRIMARY sort key for every top-level body (in order to slot belts in by real
            // position) reintroduced the exact "planets out of 1-2-3 order" bug the switch to
            // BodyOrderKey originally fixed — real orbital distance apparently isn't reliable
            // enough, on its own, to order the numbered planets correctly in this app's data.
            // Fix: keep BodyOrderKey (the name-derived number) as the sole ordering for actual
            // planets — that's the part already confirmed correct — and ONLY use real distance
            // to decide where each belt (whose own "...Cluster N" name-number means nothing
            // about its position among the numbered planets) slots in among that already-correct
            // planet sequence, via a real position-per-planet comparison rather than a shared
            // sort key across both kinds of body.
            // Belts still sorted to the end even after the fix above — real cause: a belt
            // cluster's own SemiMajorAxis isn't a reliable distance-from-star at all (same class
            // of bug already documented elsewhere in this codebase for co-orbital clusters — the
            // cluster's own reported position is local to its own grouping, not the star). The
            // PARENT RING's own InnerRad (from the star's real Rings list) is the actual measured
            // orbital distance and doesn't have that problem.
            double? BeltDistance(BodyScanDetail belt) => _watcher.GetBeltRingInnerRadius(belt.BodyName);
            // Attached stars (see the class comment above) sort into this same numbered sequence
            // via the same BodyOrderKey as any planet — their own short name ("1", "2", ...) is
            // exactly what tells the player it's just another orbiting body, so it needs to
            // actually sit among them, not off on its own.
            var planetsOnly = owned.Where(b => !b.IsMoon && !b.IsBelt)
                .Concat(attachedStars ?? Enumerable.Empty<BodyScanDetail>())
                .OrderBy(BodyOrderKey).ToList();
            var beltsOnly = owned.Where(b => !b.IsMoon && b.IsBelt)
                .OrderBy(b => BeltDistance(b) ?? double.MaxValue).ToList();
            var topLevel = new List<BodyScanDetail>(planetsOnly);
            foreach (var belt in beltsOnly)
            {
                var beltDist = BeltDistance(belt);
                int insertAt = beltDist is > 0
                    ? topLevel.FindIndex(p => p.SemiMajorAxis > 0 && p.SemiMajorAxis > beltDist.Value)
                    : -1; // no real ring distance found — append at the end rather than guess
                if (insertAt < 0) topLevel.Add(belt); else topLevel.Insert(insertAt, belt);
            }
            var childrenByParent = owned.Where(b => b.IsMoon).ToLookup(b => b.ParentBodyID);

            // "Asteroid Belt 1"/"Asteroid Belt 2" — numbered in this same real display order,
            // not by the raw "<ring name> Cluster N" body name, which is what used to show as
            // the card's title.
            var beltNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int beltCounter = 0;
            foreach (var tb in topLevel)
                if (tb.IsBelt) beltNumbers[tb.BodyName] = ++beltCounter;

            // A top-level body only goes to the minor list if NEITHER it NOR any of its moons
            // (recursively — a moon can have its own moon) are notable. Moons only ever render
            // nested under their top-level parent's own card (BuildBodyGroup), never inside the
            // minor list's plain rows — so checking only the top-level body's own notability
            // here was silently dropping a notable moon's entire card, gas giant moon or not,
            // real bug caught via a gas giant's own bio/geo-signal moon vanishing outright.
            // An attached star (see above) is always shown, never sent to the collapsed "minor
            // bodies" list — IsNotable's checks (signals, terraformable, high-value class) are
            // all planet-specific and a star would never trip any of them, which would otherwise
            // bury it exactly where a player wouldn't expect to find a star.
            var attachedStarNames = new HashSet<string>(
                (attachedStars ?? Enumerable.Empty<BodyScanDetail>()).Select(a => a.BodyName), StringComparer.OrdinalIgnoreCase);
            bool GroupHasNotable(BodyScanDetail top) =>
                attachedStarNames.Contains(top.BodyName) || IsNotable(top, hz) || childrenByParent[top.BodyID].Any(GroupHasNotable);

            List<BodyScanDetail> notable, minor;
            if (_showAllBodies)
            {
                notable = topLevel;
                minor = new List<BodyScanDetail>();
            }
            else
            {
                notable = topLevel.Where(GroupHasNotable).ToList();
                minor = topLevel.Where(b => !GroupHasNotable(b)).ToList();
            }

            if (notable.Count > 0)
            {
                // One WrapPanel per name-prefix group rather than one shared WrapPanel for the
                // whole section. For a normal single-star section every body shares the same
                // (empty) prefix, so this is still just one row-wrapping panel as before — but
                // the BARYCENTER ORBIT/PARENT NOT YET SCANNED fallback sections bundle several
                // unrelated hierarchical groups together (e.g. "AB 1"/"ABCD 1"/"CD 1"), and
                // letting the WrapPanel fill a row across a prefix boundary made them run
                // together visually with no indication they're actually separate orbital groups.
                // notable is already sorted by BodyOrderKey (prefix first), so a prefix change
                // here always means "start of the next group", never a group split in two.
                WrapPanel? wrap = null;
                string? currentPrefix = null;
                var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in notable)
                {
                    if (placed.Contains(b.BodyName)) continue;

                    string prefix = BodyOrderKey(b).prefix;
                    if (wrap == null || prefix != currentPrefix)
                    {
                        wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                        currentPrefix = prefix;
                        section.Children.Add(wrap);
                    }

                    // Attached star (see class comment) — its own compact star-styled card, not
                    // the planet-card pipeline below (PlanetClass/gravity/materials are all
                    // planet-only fields a star's own Scan event never carries).
                    if (b.IsStar) { wrap.Children.Add(BuildAttachedStarCard(b)); continue; }

                    // Co-orbiting bodies that are both being shown travel as ONE unit — a bracket
                    // line above their cards — so the WrapPanel can never split them across rows.
                    // (A partner tucked into "minor bodies" simply doesn't join the unit.)
                    List<BodyScanDetail>? mates = null;
                    if (!b.IsMoon && _baryGroups.TryGetValue(b.BodyName, out var all))
                    {
                        var shown = notable.Where(n => all.Any(a => string.Equals(a.BodyName, n.BodyName, StringComparison.OrdinalIgnoreCase))).ToList();
                        if (shown.Count > 1) mates = shown;
                    }

                    if (mates == null) { wrap.Children.Add(BuildBodyGroup(b, hz, childrenByParent, beltNumbers)); continue; }

                    var unit = new StackPanel();
                    unit.Children.Add(BuildBarycenterBracket(mates));
                    var cards = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var m in mates)
                    {
                        placed.Add(m.BodyName);
                        cards.Children.Add(BuildBodyGroup(m, hz, childrenByParent, beltNumbers));
                    }
                    unit.Children.Add(cards);
                    wrap.Children.Add(unit);
                }
            }

            if (minor.Count > 0)
            {
                var expander = new Expander
                {
                    // Header set as an explicit TextBlock (not a plain string) — WPF's default
                    // Expander template doesn't reliably pick up Foreground for an implicitly-
                    // converted string header on every Windows theme, which risked illegible
                    // dark-on-dark text here.
                    Header = new TextBlock { Text = $"Minor bodies — no signals, not terraformable, no notable value ({minor.Count})", Foreground = TextMid, FontSize = 11 },
                    Foreground = TextMid, Background = Brush("#0a0f0f"), BorderBrush = BorderSoft, BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(2),
                };
                var list = new StackPanel();
                foreach (var b in minor)
                    list.Children.Add(BuildMinorRow(b));
                expander.Content = list;
                section.Children.Add(expander);
            }

            return section;
        }

        // bodyName -> every body in its co-orbiting group (including itself); only bodies that
        // actually have partners are present. Rebuilt on each Populate.
        private Dictionary<string, List<BodyScanDetail>> _baryGroups = new(StringComparer.OrdinalIgnoreCase);

        private static string GroupWord(int n) => n switch
        {
            2 => "binary", 3 => "trinary", 4 => "quaternary", _ => $"{n}-body group",
        };

        // A bracket above a set of cards: a line with a small tick at each end pointing down at
        // the first and last card, and the group's name ("BINARY", "TRINARY", ...) in the middle.
        private UIElement BuildBarycenterBracket(List<BodyScanDetail> members)
        {
            var g = new Grid { Height = 16, Margin = new Thickness(0, 0, 10, 3), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left  = new Border { BorderBrush = AccentDim, BorderThickness = new Thickness(1, 1, 0, 0), Margin = new Thickness(0, 7, 6, 0) };
            var right = new Border { BorderBrush = AccentDim, BorderThickness = new Thickness(0, 1, 1, 0), Margin = new Thickness(6, 7, 0, 0) };
            var label = new TextBlock { Text = GroupWord(members.Count).ToUpperInvariant(), Foreground = AccentDim, FontSize = 9.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(left, 0); Grid.SetColumn(label, 1); Grid.SetColumn(right, 2);
            g.Children.Add(left); g.Children.Add(label); g.Children.Add(right);

            var names = string.Join(", ", members.Select(m => EliteWatcherService.GetShortBodyName(m.BodyName, _watcher.StarSystem)));
            AttachTip(g, BuildTextTip($"{char.ToUpper(GroupWord(members.Count)[0])}{GroupWord(members.Count).Substring(1)} — {names}",
                "These bodies orbit each other around a shared barycenter, and that pair (or group) orbits the parent together."));
            return g;
        }

        // First numeric token in the body's own short designation ("2", "A 2", "2 B") — this is
        // the real planet number the game assigns, independent of any orbital-distance quirks.
        // Unparseable (e.g. a belt naming oddity) sorts last rather than crashing the sort.
        //
        // A secondary star's own children ("7 A", "7 B", ...) also match this pattern, but
        // there the number is the STAR's designation, shared by every one of its children, and
        // the real per-body order is the trailing letter — same convention as a gas giant's own
        // moons ("4 a", "4 b"), just surfaced here as top-level "planets" of that star instead
        // of nested moons. Real report: these came out in scan-arrival order ("D, E, F, C, A,
        // B") because they all tied on the shared star number and the letter was never
        // consulted. Mirrors MainWindow's own ShortNameOrderKey fix for the same shape of bug.
        //
        // The BARYCENTER ORBIT fallback section (bodies with no single resolvable parent star)
        // can hold SEVERAL independent hierarchical groups at once — real report + screenshot:
        // "CD 1"/"CD 2"/... and "BCD 1"/"BCD 2"/... are two genuinely different sub-groupings
        // sharing that one section, each with its OWN "1, 2, 3..." numbering, so "BCD 1" and
        // "CD 1" tied on the same number and interleaved based on scan-arrival order instead of
        // staying grouped. Sorting on the PREFIX first (everything before the number token) keeps
        // each group's own members contiguous; the number/letter only break ties within one group.
        private (string prefix, int num, int letter) BodyOrderKey(BodyScanDetail b)
        {
            var shortName = EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem);
            var parts = shortName.Split(' ');
            int num = int.MaxValue, numIdx = -1;
            for (int i = 0; i < parts.Length; i++)
                if (int.TryParse(parts[i], out var n)) { num = n; numIdx = i; break; }

            string prefix = numIdx > 0 ? string.Join(" ", parts.Take(numIdx)) : "";

            int letter = 0;
            if (numIdx >= 0 && numIdx == parts.Length - 2 && parts[^1].Length > 0 && parts[^1].All(char.IsLetter))
                foreach (var ch in parts[^1].ToLowerInvariant())
                    letter = letter * 26 + (ch - 'a' + 1);

            return (prefix, num, letter);
        }

        private bool IsNotable(BodyScanDetail b, (double innerAU, double outerAU)? hz)
        {
            if (b.IsBelt) return true; // belts always get a full card — there's rarely more than one or two
            if (b.BioSignalCount > 0 || b.GeoSignalCount > 0 || b.MiningSignalCount > 0) return true;
            if (string.Equals(b.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase)) return true;
            if (HighValueClasses.Contains(b.PlanetClass)) return true;
            // Gas/water giants are notable on their own regardless of their own signals/value —
            // they're consistently likely to host a moon system worth surfacing, per direct
            // feedback. The only PlanetClass strings the game ever uses that contain "giant"
            // are gas/water giants, so this substring check is safe without an explicit list.
            if ((b.PlanetClass ?? "").Contains("giant", StringComparison.OrdinalIgnoreCase)) return true;
            if (hz.HasValue && b.SemiMajorAxis > 0)
            {
                double bodyAU = b.SemiMajorAxis / 149_597_870_700.0;
                if (bodyAU >= hz.Value.innerAU && bodyAU <= hz.Value.outerAU) return true;
            }
            return false;
        }

        // A star that orbits another star in this system but has no orbiting bodies of its own —
        // sized to match a regular planet card (210 wide) so it sits naturally in the same
        // WrapPanel row instead of needing special layout. Star-appropriate fields only (type,
        // mass, rings) — a star's own Scan event never carries PlanetClass/gravity/materials, so
        // this deliberately doesn't reuse BuildCard.
        private UIElement BuildAttachedStarCard(BodyScanDetail s)
        {
            // Same layout convention as a real planet card (BuildCard) — 59px thumb left, name/
            // class stacked right, a chip row below — per feedback that this card's own smaller
            // circle-and-text layout didn't match the rest of the WrapPanel it sits in.
            var stack = new StackPanel { Width = 210, Margin = new Thickness(0, 0, 10, 14) };
            var card = new Border
            {
                Background = Panel2, BorderBrush = BorderSoft, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(11),
            };
            var col = new StackPanel();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Real report: rings first showed as plain "N rings" text, then as a flattened
            // ellipse sitting flatly BEHIND the star with no sense of wrapping around it — a
            // dim brown dwarf like a Y-dwarf can carry genuine Saturn-style rings (this exact
            // body: two real "eRingClass_Rocky" rings, not asteroid-belt entries), and per
            // feedback they should render the same way a gas giant's own rings do: baked into
            // one real render (back band, sphere, front band crossing the near limb), not a
            // hand-drawn icon. Rendered at a bigger box (140, same convention BuildThumb already
            // uses for a ringed gas giant/terrain thumbnail) then scaled down to the card's own
            // 59px thumb so the ring keeps its proportions instead of looking cramped.
            const int box = 140;
            var thumbWrap = new Grid { Width = 59, Height = 59, Margin = new Thickness(0, 0, 10, 0) };
            thumbWrap.Children.Add(new Image
            {
                Source = StarRenderer.GetStarThumbFrame(s, box, box),
                Width = 59, Height = 59, Stretch = Stretch.Uniform,
            });
            Grid.SetColumn(thumbWrap, 0);
            top.Children.Add(thumbWrap);

            var idStack = new StackPanel();
            idStack.Children.Add(new TextBlock { Text = EliteWatcherService.GetShortBodyName(s.BodyName, _watcher.StarSystem).ToUpperInvariant(), Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 13.5 });
            idStack.Children.Add(new TextBlock { Text = $"{s.StarType}-type · {s.StellarMass:F2} SM", Foreground = ClassText, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(idStack, 1);
            top.Children.Add(idStack);
            col.Children.Add(top);

            // Same "not a belt" filter used elsewhere to tell a real ring apart from a star's
            // own asteroid belt entry.
            var realRing = s.Rings.FirstOrDefault(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase));
            var chips = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            if (realRing != null) chips.Children.Add(Chip(MainWindow.FormatRingClass(realRing.RingClass).ToLowerInvariant() + " rings", HzColor, null));
            chips.Children.Add(Chip("scanned", TextMid, null));
            col.Children.Add(chips);

            card.Child = col;
            stack.Children.Add(card);
            return stack;
        }

        // ---------------------------------------------------------------
        //  Notable body card + nested moons (top-down, same as the mockup/EDDiscovery)
        // ---------------------------------------------------------------
        private UIElement BuildBodyGroup(BodyScanDetail b, (double innerAU, double outerAU)? hz, ILookup<int, BodyScanDetail> childrenByParent, Dictionary<string, int> beltNumbers)
        {
            // Belt cards are half the width of a planet card per feedback — they carry far less
            // content (no signals/materials/moons of their own) and didn't need the same room.
            double width = b.IsBelt ? 84 : 210;
            var stack = new StackPanel { Width = width, Margin = new Thickness(0, 0, 10, 14) };
            int beltNumber = beltNumbers.TryGetValue(b.BodyName, out var bn) ? bn : 0;
            stack.Children.Add(BuildCard(b, hz, beltNumber));

            var moons = childrenByParent[b.BodyID].OrderBy(m => m.SemiMajorAxis).ToList();
            if (moons.Count > 0)
            {
                // Indent halved (was ~32px combined margin+padding) per feedback — frees up
                // real width for the moon row's own text/materials instead of eating into it.
                var moonStack = new StackPanel
                {
                    Margin = new Thickness(10, 7, 0, 0),
                };
                var indentBorder = new Border { BorderBrush = BorderSoft, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(6, 0, 0, 0), Child = moonStack };

                // Co-orbiting moons get a small "[" brace squeezed into the gutter between the
                // card's own indent line and the moon rows — same barycenter grouping as the
                // top-level planet bracket, just vertical and narrow since there's no spare width
                // here for the full bracket-with-label version.
                var placedMoons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in moons)
                {
                    if (placedMoons.Contains(m.BodyName)) continue;

                    List<BodyScanDetail>? mates = null;
                    if (_baryGroups.TryGetValue(m.BodyName, out var all))
                    {
                        var shown = moons.Where(mm => all.Any(a => string.Equals(a.BodyName, mm.BodyName, StringComparison.OrdinalIgnoreCase))).ToList();
                        if (shown.Count > 1) mates = shown;
                    }

                    if (mates == null)
                    {
                        moonStack.Children.Add(BuildMoonGroup(m, hz, childrenByParent));
                        continue;
                    }

                    var groupStack = new StackPanel();
                    for (int mi = 0; mi < mates.Count; mi++)
                    {
                        var mate = mates[mi];
                        placedMoons.Add(mate.BodyName);
                        var mateGroup = BuildMoonGroup(mate, hz, childrenByParent);
                        // Each moon group carries its own bottom margin for spacing to the NEXT
                        // sibling — fine normally, but the last mate's copy of it was making the
                        // bracket (which stretches to match groupStack's full height) run past
                        // the actual bottom card into that trailing gap. Stripped here and
                        // re-added on the group as a whole below instead.
                        if (mi == mates.Count - 1 && mateGroup is FrameworkElement mateFe)
                            mateFe.Margin = new Thickness(mateFe.Margin.Left, mateFe.Margin.Top, mateFe.Margin.Right, 0);
                        groupStack.Children.Add(mateGroup);
                    }

                    var bracketGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    bracketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    bracketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var bracket = new Border
                    {
                        BorderBrush = AccentDim, BorderThickness = new Thickness(1, 1, 0, 1),
                        Width = 5, Margin = new Thickness(0, 4, 4, 4), VerticalAlignment = VerticalAlignment.Stretch,
                    };
                    var mateNames = string.Join(", ", mates.Select(mm => EliteWatcherService.GetShortBodyName(mm.BodyName, _watcher.StarSystem)));
                    AttachTip(bracket, BuildTextTip($"{char.ToUpper(GroupWord(mates.Count)[0])}{GroupWord(mates.Count).Substring(1)} moons — {mateNames}",
                        "These moons orbit each other around a shared barycenter, and that pair (or group) orbits the parent planet together."));
                    Grid.SetColumn(bracket, 0); Grid.SetColumn(groupStack, 1);
                    bracketGrid.Children.Add(bracket); bracketGrid.Children.Add(groupStack);
                    moonStack.Children.Add(bracketGrid);
                }
                stack.Children.Add(indentBorder);
            }
            return stack;
        }

        private UIElement BuildMoonGroup(BodyScanDetail m, (double innerAU, double outerAU)? hz, ILookup<int, BodyScanDetail> childrenByParent)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(BuildMoonRow(m, hz));

            var subMoons = childrenByParent[m.BodyID].OrderBy(x => x.SemiMajorAxis).ToList();
            if (subMoons.Count > 0)
            {
                var sub = new StackPanel { Margin = new Thickness(7, 6, 0, 0) };
                foreach (var sm in subMoons) sub.Children.Add(BuildMoonGroup(sm, hz, childrenByParent));
                stack.Children.Add(sub);
            }
            return stack;
        }

        private UIElement BuildCard(BodyScanDetail b, (double innerAU, double outerAU)? hz, int beltNumber = 0)
        {
            if (b.IsBelt) return BuildBeltCard(b, beltNumber);

            bool inHz = false;
            if (hz.HasValue && b.SemiMajorAxis > 0)
            {
                double bodyAU = b.SemiMajorAxis / 149_597_870_700.0;
                inHz = bodyAU >= hz.Value.innerAU && bodyAU <= hz.Value.outerAU;
            }
            bool terraformable = string.Equals(b.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase);
            var (borderColor, borderThickness, cardBackground) = OutlineFor(b, inHz, terraformable);
            var card = new Border
            {
                Background = cardBackground,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(borderThickness), CornerRadius = new CornerRadius(3), Padding = new Thickness(11),
            };
            var col = new StackPanel();

            // top: thumb + name/type + Landable pinned to the upper-right, in line with the
            // name — that corner was empty space before; real orbital distance dropped entirely
            // per feedback, it wasn't adding much and just crowded the card.
            // Grid, not DockPanel — a DockPanel-docked Landable chip reserves its whole column
            // for the DockPanel's FULL height, not just alongside the name line, so a separate
            // row below it (first fix attempt) landed below the entire 59px thumbnail instead of
            // right under the name (real screenshot: text pushed way down). A Grid overlay lets
            // the chip float over just the name row's corner without reserving space at all, so
            // idStack's second line (class name) gets the full column width immediately below
            // the name, using the room beside/under the chip instead of being pushed down.
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var thumbWrap = new Grid { Width = 59, Height = 59, Margin = new Thickness(0, 0, 10, 0) };
            thumbWrap.Children.Add(BuildThumb(b, 59));
            if (b.IsMapped)
            {
                // ~50% of the thumb's own diameter — covers roughly a quarter of the sphere's
                // area, per direct feedback ("a little hard to see... ok if it covers more").
                var mapBadge = MakeMappedBadge(30);
                mapBadge.HorizontalAlignment = HorizontalAlignment.Left;
                mapBadge.VerticalAlignment = VerticalAlignment.Top;
                thumbWrap.Children.Add(mapBadge);
            }
            // Bottom-right, the opposite corner from the mapped badge (top-left, 30px of this
            // 59px thumb) — a 17px badge here can't reach it.
            if (DiscoveredByOther(b)) thumbWrap.Children.Add(MakeDiscoveredBadge(17));
            if (MappedByOther(b)) thumbWrap.Children.Add(MakeMappedByOtherBadge(17));
            if (FootfalledByOther(b)) thumbWrap.Children.Add(MakeFootfallBadge(17));
            Grid.SetColumn(thumbWrap, 0);
            top.Children.Add(thumbWrap);

            var idStack = new StackPanel();
            idStack.Children.Add(new TextBlock { Text = EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem).ToUpperInvariant(), Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            idStack.Children.Add(new TextBlock { Text = FormatClass(b), Foreground = ClassText, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(idStack, 1);
            top.Children.Add(idStack);

            if (b.Landable)
            {
                // Same column as idStack, added last so it paints on top (Grid z-orders by
                // child order) — floats over the name row's top-right corner rather than
                // reserving a column, which is exactly what lets the class-name line below it
                // use the full width instead of staying squeezed under the chip too.
                var landChip = Chip("landable", LandableColor, LandableFill);
                if (landChip is FrameworkElement landFe)
                {
                    landFe.HorizontalAlignment = HorizontalAlignment.Right;
                    landFe.VerticalAlignment = VerticalAlignment.Top;
                    // Small negative top margin — the chip's own border+padding otherwise sits
                    // visibly lower than the name text's cap-height beside it (real screenshot:
                    // "landable" reads a few px below "A 1"), since a Border's padding adds
                    // height a plain TextBlock doesn't have. Nudges it up to align instead of
                    // pushing the class-name line down, which would grow every card's height.
                    landFe.Margin = new Thickness(0, -3, 0, 0);
                }
                Grid.SetColumn(landChip, 1);
                top.Children.Add(landChip);
            }

            col.Children.Add(top);

            // Real Bio/Geo/Mining badges — the exact same icons the Planet tab uses (same
            // colors/shapes app-wide instead of a second icon language), one row, no counts.
            // Landable already placed above, so this row is signals only here.
            var sigRow = BuildSignalIconRow(b, 22, includeLandable: false);
            if (sigRow != null) col.Children.Add(sigRow);

            // chips
            var chips = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            if (inHz) chips.Children.Add(Chip("habitable zone", HzColor, null));
            if (terraformable) chips.Children.Add(Chip("terraformable", TerraColor, TerraFill));
            if (HighValueClasses.Contains(b.PlanetClass)) chips.Children.Add(Chip("high value", ValueColor, null));
            // Landable-only per feedback — gravity only actually matters if you can set down on
            // the body; also naturally excludes every gas giant (never landable).
            if (_gravityWarningEnabled && b.Landable && b.SurfaceGravity > 0 && b.SurfaceGravity / 9.80665 >= _gravityWarningThresholdG)
                chips.Children.Add(Chip("high gravity", GravityWarnColor, GravityWarnFill));
            // Same "not a belt" filter and chip convention as the attached-star card — one per
            // distinct real ring class (a body can carry more than one, e.g. a Rocky ring plus
            // an Icy one), so a ringed planet/moon shows what its rings are actually made of
            // right alongside its other traits instead of only via the Ring Mining badge/stat.
            foreach (var ringClass in b.Rings.Where(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase))
                         .Select(r => r.RingClass).Distinct(StringComparer.OrdinalIgnoreCase))
                chips.Children.Add(Chip(MainWindow.FormatRingClass(ringClass).ToLowerInvariant() + " rings", HzColor, null));
            chips.Children.Add(Chip(b.IsMapped ? "mapped" : "scanned", b.IsMapped ? Accent : TextMid, null));
            if (chips.Children.Count > 0) col.Children.Add(chips);

            // materials
            var cardMats = BuildMaterialChips(b, new Thickness(0, 6, 0, 0));
            if (cardMats != null) col.Children.Add(cardMats);

            // bottom: current value — wording makes clear this is a live number that moves as
            // the body gets mapped, not a one-time estimate; the "scan only, mapping would
            // multiply..." hint line is dropped per feedback (the wording change covers it).
            var (estValue, _) = ScanValueEstimator.Estimate(b);
            if (estValue > 0)
            {
                var bottom = new Border { BorderBrush = BorderSoft, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Margin = new Thickness(0, 7, 0, 0) };
                var bstack = new StackPanel { Orientation = Orientation.Horizontal };
                bstack.Children.Add(new TextBlock { Text = "Current Value ≈ ", Foreground = TextDim, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
                bstack.Children.Add(new TextBlock { Text = PayoutData.FormatCredits((long)estValue), Foreground = ValueColor, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

                // "$" marker — either already worth a lot, or (if not mapped yet) WOULD be once
                // mapped, so a real payout candidate isn't missed just because DSS hasn't run
                // yet. Same green as the Nickel material pill, same size as the credits figure,
                // per direct feedback.
                bool highValueNow = estValue >= HighValueThreshold;
                bool highValuePotential = !b.IsMapped && ScanValueEstimator.EstimateIfMapped(b) >= HighValueThreshold;
                if (highValueNow || highValuePotential)
                    bstack.Children.Add(new TextBlock { Text = "  $", Foreground = HighValueGreen, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

                bottom.Child = bstack;
                col.Children.Add(bottom);
            }

            card.Child = col;
            return card;
        }

        // Deliberately much lighter than BuildCard — a belt cluster carries none of a planet's
        // signals/materials/moons, so the full card's layout just wasted its own extra width.
        // "Asteroid Belt N" keeps the exact size/color it already had (the dim subtitle style);
        // composition becomes a "titlecard" — a chip, same visual treatment as Landable — rather
        // than the earlier attempt at promoting it to the big bold title line.
        private UIElement BuildBeltCard(BodyScanDetail b, int beltNumber)
        {
            var card = new Border
            {
                Background = Panel2, BorderBrush = BorderSoft, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6),
            };
            // Vertical layout, everything centered — icon on top, name below it (forced onto
            // its own two lines, "Asteroid" / "Belt N", as two separate TextBlocks rather than
            // relying on wrapping to break in a sensible place), composition chip last.
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var thumbWrap = new Grid { Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center };
            thumbWrap.Children.Add(BuildThumb(b, 34));
            col.Children.Add(thumbWrap);

            var nameStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            nameStack.Children.Add(new TextBlock { Text = "Asteroid", Foreground = TextDim, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center });
            nameStack.Children.Add(new TextBlock { Text = $"Belt {beltNumber}", Foreground = TextDim, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(nameStack);

            var rawRingClass = _watcher.GetBeltRingClass(b.BodyName);
            var compositionText = rawRingClass != null ? MainWindow.FormatRingClass(rawRingClass) : "Unknown";
            var chips = new WrapPanel { Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            chips.Children.Add(Chip(compositionText, TextMid, null));
            col.Children.Add(chips);

            card.Child = col;
            return card;
        }

        // Same real icons for both the full card and the compact moon row — no source counts
        // (per feedback — the icon's presence is the signal, the number wasn't needed),
        // consistent icon size everywhere it's used. A plain WrapPanel — the previous
        // non-wrapping StackPanel let a card with bio+geo+mining+landable all present overflow
        // its own width with no way to wrap, cutting off "landable" mid-word (confirmed on a
        // real screenshot) — and Landable gets its own line underneath rather than competing
        // with the badges for the same row's width at all. includeLandable is false for the
        // full card, which instead pins Landable to the card's own top-right corner.
        private UIElement? BuildSignalIconRow(BodyScanDetail b, double iconSize, bool includeLandable = true)
        {
            bool anySignal = b.BioSignalCount > 0 || b.GeoSignalCount > 0 || b.MiningSignalCount > 0;
            bool showLandable = includeLandable && b.Landable;
            if (!anySignal && !showLandable) return null;

            var col = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (anySignal)
            {
                var badges = new WrapPanel();
                void AddBadge(Canvas c, string title, string desc)
                {
                    c.Margin = new Thickness(0, 0, 5, 0);
                    c.IsHitTestVisible = true; // MakeSignalBadge turns hit-testing off (fine on the Planet tab's overlay), which silently killed the tooltip
                    c.Background ??= Brushes.Transparent; // whole icon box hoverable, not just its strokes
                    AttachTip(c, BuildTextTip(title, desc));
                    badges.Children.Add(c);
                }
                if (b.BioSignalCount > 0) AddBadge(MainWindow.MakeBioBadge(0, 0, iconSize), $"Biological signals ({b.BioSignalCount})",
                    "Life detected on this body. Use the Surface Scanner (FSS/DSS) and Genetic Sampler to find and sample species for Exobiology payouts.");
                if (b.GeoSignalCount > 0) AddBadge(MainWindow.MakeGeoBadge(0, 0, iconSize), $"Geological signals ({b.GeoSignalCount})",
                    "Volcanic or geological features (vents, fumaroles, lava spouts). Can be sampled with the Geological Sampler for materials and data.");
                if (b.MiningSignalCount > 0) AddBadge(MainWindow.MakeMiningBadge(0, 0, iconSize), $"Mining signals ({b.MiningSignalCount})",
                    "Ring hotspots with mineable deposits. Fly to the ring and use a Prospector or Collector limpet setup.");
                col.Children.Add(badges);
            }
            if (showLandable)
            {
                var landRow = new WrapPanel { Margin = new Thickness(0, anySignal ? 4 : 0, 0, 0) };
                landRow.Children.Add(Chip("landable", LandableColor, LandableFill));
                col.Children.Add(landRow);
            }
            return col;
        }

        private UIElement BuildMoonRow(BodyScanDetail m, (double innerAU, double outerAU)? hz)
        {
            // Same outline priority as the full card — this was simply never wired up here at
            // all (the card's own border-color logic never got carried over to moon rows),
            // confirmed by a real bio-signal moon showing no teal outline whatsoever.
            bool inHz = false;
            if (hz.HasValue && m.SemiMajorAxis > 0)
            {
                double bodyAU = m.SemiMajorAxis / 149_597_870_700.0;
                inHz = bodyAU >= hz.Value.innerAU && bodyAU <= hz.Value.outerAU;
            }
            bool moonTerraformable = string.Equals(m.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase);
            var (moonBorder, moonBorderThickness, moonBackground) = OutlineFor(m, inHz, moonTerraformable);
            var row = new Border { Background = moonBackground, BorderBrush = moonBorder, BorderThickness = new Thickness(moonBorderThickness), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 6, 7, 6) };
            var outer = new StackPanel();

            // Top line: thumb, name/class, value — value stays a narrow right-hand column here
            // (unlike the full card's bottom placement, there's no room below without making
            // every moon row noticeably taller).
            var dock = new DockPanel();
            var thumbGrid = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 7, 0) };
            thumbGrid.Children.Add(BuildThumb(m, 28));
            if (m.IsMapped)
            {
                // Bumped again — the earlier "already matches the card's own ratio" reasoning
                // didn't hold up against an actual screenshot of a moon card, which still read
                // as too small.
                var mapBadge = MakeMappedBadge(19);
                mapBadge.HorizontalAlignment = HorizontalAlignment.Left;
                mapBadge.VerticalAlignment = VerticalAlignment.Top;
                thumbGrid.Children.Add(mapBadge);
            }
            // 12px in the bottom-right of a 28px thumb vs the 19px mapped badge top-left: the two
            // bounding boxes only meet at a corner, and the mapped badge is round, so no real
            // overlap. Mapped-by-other sits bottom-left, the one corner still free.
            if (DiscoveredByOther(m)) thumbGrid.Children.Add(MakeDiscoveredBadge(12));
            if (MappedByOther(m)) thumbGrid.Children.Add(MakeMappedByOtherBadge(12));
            if (FootfalledByOther(m)) thumbGrid.Children.Add(MakeFootfallBadge(12));
            DockPanel.SetDock(thumbGrid, Dock.Left);
            dock.Children.Add(thumbGrid);

            var (estValue, _) = ScanValueEstimator.Estimate(m);
            if (estValue > 0)
            {
                var valStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                valStack.Children.Add(new TextBlock { Text = $"≈ {PayoutData.FormatCredits((long)estValue)}", Foreground = ValueColor, FontSize = 11, TextAlignment = TextAlignment.Right });
                DockPanel.SetDock(valStack, Dock.Right);
                dock.Children.Add(valStack);
            }

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = EliteWatcherService.GetShortBodyName(m.BodyName, _watcher.StarSystem).ToUpperInvariant(), Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock { Text = FormatClass(m), Foreground = ClassText, FontSize = 10 });
            // Moons can be binary too — no bracket room inside the nested list, so a dim note.
            if (_baryGroups.TryGetValue(m.BodyName, out var moonMates))
            {
                var others = moonMates.Where(o => !string.Equals(o.BodyName, m.BodyName, StringComparison.OrdinalIgnoreCase))
                    .Select(o => EliteWatcherService.GetShortBodyName(o.BodyName, _watcher.StarSystem).ToUpperInvariant());
                info.Children.Add(new TextBlock { Text = $"{GroupWord(moonMates.Count)} with {string.Join(", ", others)}", Foreground = AccentDim, FontSize = 9.5 });
            }
            dock.Children.Add(info);
            outer.Children.Add(dock);

            // Signals and materials go BELOW, spanning the row's full width — they used to live
            // inside the name column only (squeezed to whatever width was left after the value
            // column), which is exactly why materials looked cramped into one narrow strip
            // instead of running across the card the way the full card's do.
            var sigRow = BuildSignalIconRow(m, 22);
            if (sigRow != null) outer.Children.Add(sigRow);

            // Same ring-class chip as the full card, in this row's own compact chip-row style —
            // one per distinct real ring class present.
            var moonRingClasses = m.Rings.Where(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.RingClass).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (moonRingClasses.Count > 0)
            {
                var moonRingChips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var ringClass in moonRingClasses)
                    moonRingChips.Children.Add(Chip(MainWindow.FormatRingClass(ringClass).ToLowerInvariant() + " rings", HzColor, null));
                outer.Children.Add(moonRingChips);
            }

            var moonMats = BuildMaterialChips(m, new Thickness(0, 4, 0, 0));
            if (moonMats != null) outer.Children.Add(moonMats);

            row.Child = outer;
            return row;
        }

        private UIElement BuildMinorRow(BodyScanDetail b)
        {
            var row = new Border { BorderBrush = BorderSoft, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
            var dock = new DockPanel();

            var (estValue, _) = ScanValueEstimator.Estimate(b);
            var val = new TextBlock { Text = estValue > 0 ? $"≈ {PayoutData.FormatCredits((long)estValue)}" : "—", Foreground = ValueColor, FontSize = 10, Width = 90, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(val, Dock.Right);
            dock.Children.Add(val);

            var dist = new TextBlock { Text = b.SemiMajorAxis > 0 ? $"{b.SemiMajorAxis / 299_792_458.0:N0} ls" : "—", Foreground = TextDim, FontSize = 10, Width = 60, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(dist, Dock.Right);
            dock.Children.Add(dist);

            var dot = new Ellipse { Width = 7, Height = 7, Fill = PlanetSwatch(b), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dock.Children.Add(dot);

            var name = new TextBlock { Text = EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem), Foreground = TextMid, FontSize = 10, Width = 140, TextTrimming = TextTrimming.CharacterEllipsis };
            dock.Children.Add(name);

            var cls = new TextBlock { Text = FormatClass(b), Foreground = TextDim, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
            dock.Children.Add(cls);

            row.Child = dock;
            return row;
        }

        // ---------------------------------------------------------------
        //  Small helpers
        // ---------------------------------------------------------------

        // "Mapped" indicator — real feature request: the existing "mapped"/"scanned" text chip
        // wasn't enough on its own for a quick "have I already DSS-mapped this?" glance,
        // especially on a small moon thumbnail. A wireframe-globe glyph (same idea as EDSM's
        // own convention for this) pinned to the thumbnail's top-left corner — opposite the
        // bio/geo/mining badges, which already own the bottom-right.
        // True only when a scan says someone had already discovered this body AND your own
        // journal history says it wasn't you — see DiscoveryIndex for why "WasDiscovered" alone
        // can't tell those apart. Held back until the index has finished its startup pass, so it
        // can never blame a body you discovered yourself just because the index wasn't ready.
        private static bool DiscoveredByOther(BodyScanDetail b) =>
            !b.IsBelt && !b.IsStar && b.WasDiscovered == true &&
            DiscoveryIndex.IsReady && !DiscoveryIndex.IsDiscoveredByMe(b.BodyName);

        // Same idea as DiscoveredByOther, for mapping instead of discovery — a real player
        // question this came from: "do the logs show this body was both discovered AND mapped
        // already?" WasMapped==true on a body's ONLY Scan means someone DSS-mapped it before
        // this scan; DiscoveryIndex.IsMappedByMe tells apart "someone" being the player
        // themselves (an earlier session's own SAAScanComplete) from a genuine other commander,
        // same reasoning as WasDiscovered — see DiscoveryIndex's own class comment.
        private static bool MappedByOther(BodyScanDetail b) =>
            !b.IsBelt && !b.IsStar && b.WasMapped == true &&
            DiscoveryIndex.IsReady && !DiscoveryIndex.IsMappedByMe(b.BodyName);

        // True only when a scan says someone had already walked on this planet AND your own
        // history says it wasn't you who got there first - see DiscoveryIndex.IsFootfalledByOther.
        // Held back until the index finishes its startup pass, same as the other "by other" badges.
        private static bool FootfalledByOther(BodyScanDetail b) =>
            !b.IsBelt && !b.IsStar && b.WasFootfalled == true &&
            DiscoveryIndex.IsReady && DiscoveryIndex.IsFootfalledByOther(b.BodyName);

        // Same dark-disc / red-ring / bold-letter badge as the discovered and mapped-by-other ones,
        // pinned to the upper-right corner of the thumbnail (the one corner they leave free).
        private static FrameworkElement MakeFootfallBadge(double size)
        {
            var red = Brush("#ff5c66");
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = Brush("#d9120a0c"), BorderBrush = red,
                BorderThickness = new Thickness(Math.Max(1, size * 0.07)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "First footfall already taken by another commander",
                Child = new TextBlock
                {
                    Text = "F", Foreground = red, FontWeight = FontWeights.Bold, FontSize = size * 0.62,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        // Small red-ish "D" in a dark disc so it stays legible over any planet art (light icy
        // bodies and dark rocky ones alike).
        private static FrameworkElement MakeDiscoveredBadge(double size)
        {
            var red = Brush("#ff5c66");
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = Brush("#d9120a0c"), BorderBrush = red,
                BorderThickness = new Thickness(Math.Max(1, size * 0.07)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = "Already discovered by another commander",
                Child = new TextBlock
                {
                    Text = "D", Foreground = red, FontWeight = FontWeights.Bold, FontSize = size * 0.62,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        // Same disc-badge convention as MakeDiscoveredBadge (dark disc, red ring, bold letter),
        // bottom-LEFT instead of bottom-right — same red, same "annoyed someone beat you to it"
        // read as the discovery badge, per feedback. Distinct from the wireframe-globe
        // MakeMappedBadge above: that one means "this body has been DSS-mapped by anyone,
        // including you"; this one specifically means "already mapped by ANOTHER commander
        // before you got here."
        private static FrameworkElement MakeMappedByOtherBadge(double size)
        {
            var red = Brush("#ff5c66");
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = Brush("#d9120a0c"), BorderBrush = red,
                BorderThickness = new Thickness(Math.Max(1, size * 0.07)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = "Already mapped by another commander",
                Child = new TextBlock
                {
                    Text = "M", Foreground = red, FontWeight = FontWeights.Bold, FontSize = size * 0.62,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        private static FrameworkElement MakeMappedBadge(double size)
        {
            var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            var outline = new Ellipse
            {
                Width = size, Height = size,
                Stroke = HzColor, StrokeThickness = Math.Max(1, size * 0.09),
                Fill = Brush("#0a1010"),
            };
            canvas.Children.Add(outline);
            var equator = new Ellipse
            {
                Width = size * 0.92, Height = size * 0.34,
                Stroke = HzColor, StrokeThickness = Math.Max(0.8, size * 0.06),
            };
            Canvas.SetLeft(equator, size * 0.04);
            Canvas.SetTop(equator, size * 0.33);
            canvas.Children.Add(equator);
            var meridian = new Line
            {
                X1 = size / 2, Y1 = size * 0.06, X2 = size / 2, Y2 = size * 0.94,
                Stroke = HzColor, StrokeThickness = Math.Max(0.8, size * 0.06),
            };
            canvas.Children.Add(meridian);
            return canvas;
        }

        private UIElement Chip(string text, SolidColorBrush color, SolidColorBrush? fill) => new Border
        {
            BorderBrush = color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Background = fill ?? System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 5, 4),
            Child = new TextBlock { Text = text, Foreground = color, FontSize = 10.5 },
        };

        private UIElement MaterialPill(string name, BodyScanDetail body)
        {
            var symbol = MaterialSymbol(name);
            var color = MaterialColors.TryGetValue(name, out var c) ? c : Brush("#9fd8e0");
            var pill = new Border
            {
                BorderBrush = color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 4, 4),
                Background = Brushes.Transparent,
                Child = new TextBlock { Text = symbol, Foreground = color, FontSize = 10.5, FontWeight = FontWeights.Bold },
            };
            AttachTip(pill, BuildMaterialTip(body, name));
            return pill;
        }

        // Dark, app-themed hover popup (WPF's default is a pale-yellow box) with a quick show
        // delay and a long show duration so it doesn't vanish mid-read.
        private static void AttachTip(FrameworkElement target, UIElement content)
        {
            target.ToolTip = new ToolTip
            {
                Background = Panel2, BorderBrush = BorderC, BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8), Content = content,
            };
            ToolTipService.SetInitialShowDelay(target, 300);
            ToolTipService.SetShowDuration(target, 60000);
        }

        private static UIElement BuildTextTip(string title, string body)
        {
            var sp = new StackPanel { MaxWidth = 260 };
            sp.Children.Add(new TextBlock { Text = title, Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 12 });
            sp.Children.Add(new TextBlock { Text = body, Foreground = TextMid, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            return sp;
        }

        // Full surface-material breakdown for the body (the journal's own percentages), with the
        // hovered element highlighted. Bars scale to the body's largest share.
        private UIElement BuildMaterialTip(BodyScanDetail body, string highlight)
        {
            var sp = new StackPanel { MinWidth = 190 };
            sp.Children.Add(new TextBlock { Text = "Surface materials", Foreground = TextBright, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 5) });
            var rows = body.Materials.OrderByDescending(x => x.Percent).ToList();
            double max = rows.Count > 0 ? Math.Max(rows[0].Percent, 0.01) : 1;
            foreach (var (mName, pct) in rows)
            {
                bool hot = string.Equals(mName, highlight, StringComparison.OrdinalIgnoreCase);
                var color = MaterialColors.TryGetValue(mName, out var mc) ? mc : Brush("#9fd8e0");
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                bool full = MaterialInventory.IsFull(mName);
                var label = new TextBlock { Text = mName, Foreground = hot ? color : TextMid, FontSize = 11, FontWeight = hot ? FontWeights.Bold : FontWeights.Normal };
                var bar = new Border { Background = color, Opacity = hot ? 1.0 : 0.6, Height = 6, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 70 * pct / max), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
                var val = new TextBlock { Text = pct.ToString("0.0") + "%", Foreground = hot ? TextBright : TextMid, FontSize = 11, FontWeight = hot ? FontWeights.Bold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(label, 0); Grid.SetColumn(bar, 1); Grid.SetColumn(val, 2);
                g.Children.Add(label); g.Children.Add(bar); g.Children.Add(val);
                if (full)
                {
                    // Already at the storage cap — dimmed so it reads as "no need to collect".
                    label.Opacity = bar.Opacity = val.Opacity = 0.45;
                    var tag = new TextBlock { Text = "FULL", Foreground = ValueColor, FontSize = 9.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(tag, 3);
                    g.Children.Add(tag);
                }
                sp.Children.Add(g);
            }
            return sp;
        }

        // Distinct color per element (not just 3 rarity tiers) — feedback was that grouping by
        // rarity alone made every common material look identical; each real element now gets
        // its own recognizable hue, so "this body has Iron" is visually distinct from "this
        // body has Nickel" at a glance, the same way the game's own material list works.
        private static readonly Dictionary<string, SolidColorBrush> MaterialColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Carbon"]     = Brush("#a0a0a0"),
            ["Iron"]       = Brush("#ff8a5c"),
            ["Nickel"]     = Brush("#7ee787"),
            ["Phosphorus"] = Brush("#c9a45c"),
            ["Sulphur"]    = Brush("#f5e05a"),
            ["Chromium"]   = Brush("#6ec6ff"),
            ["Germanium"]  = Brush("#d88ae8"),
            ["Manganese"]  = Brush("#ff6ec7"),
            ["Vanadium"]   = Brush("#4fd6c0"),
            ["Zinc"]       = Brush("#a0a8ff"),
            ["Zirconium"]  = Brush("#ffb84f"),
            ["Arsenic"]    = Brush("#8fe0a0"),
            ["Cadmium"]    = Brush("#e8a0d8"),
            ["Mercury"]    = Brush("#b8b8ff"),
            ["Molybdenum"] = Brush("#c08a5c"),
            ["Niobium"]    = Brush("#5ce0d8"),
            ["Tin"]        = Brush("#d0d0a0"),
            ["Tungsten"]   = Brush("#9090c0"),
            ["Antimony"]   = Brush("#e0a0a0"),
            ["Polonium"]   = Brush("#ff5c5c"),
            ["Ruthenium"]  = Brush("#a0c0ff"),
            ["Selenium"]   = Brush("#ffcf5c"),
            ["Technetium"] = Brush("#b05cff"),
            ["Tellurium"]  = Brush("#ff8ac0"),
            ["Yttrium"]    = Brush("#5cffb0"),
        };

        // Journal Materials come through as full element names ("Mercury"), not the periodic
        // symbols the mockup/EDDiscovery display — this is the small, fixed lookup for the
        // ones that actually show up as planetary surface materials.
        private static readonly Dictionary<string, string> MaterialSymbols = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Carbon"] = "C", ["Iron"] = "Fe", ["Nickel"] = "Ni", ["Phosphorus"] = "P", ["Sulphur"] = "S",
            ["Chromium"] = "Cr", ["Germanium"] = "Ge", ["Manganese"] = "Mn", ["Vanadium"] = "V", ["Zinc"] = "Zn", ["Zirconium"] = "Zr",
            ["Arsenic"] = "As", ["Cadmium"] = "Cd", ["Mercury"] = "Hg", ["Molybdenum"] = "Mo", ["Niobium"] = "Nb", ["Tin"] = "Sn", ["Tungsten"] = "W",
            ["Antimony"] = "Sb", ["Polonium"] = "Po", ["Ruthenium"] = "Ru", ["Technetium"] = "Tc", ["Tellurium"] = "Te", ["Selenium"] = "Se", ["Yttrium"] = "Y",
        };
        private static string MaterialSymbol(string name) => MaterialSymbols.TryGetValue(name, out var s) ? s : name.Length > 2 ? name.Substring(0, 2) : name;

        private static string FormatClass(BodyScanDetail b)
        {
            if (b.IsBelt) return "Asteroid Belt";
            if (string.IsNullOrEmpty(b.PlanetClass)) return "—";

            // "Sudarsky class III gas giant" -> "Gas Giant III" — shorter and reads better than
            // the full Sudarsky name, which was also long enough to get cut off in a narrow card.
            var lower = b.PlanetClass.ToLowerInvariant();
            if (lower.StartsWith("sudarsky class ") && lower.EndsWith(" gas giant"))
            {
                var numeral = b.PlanetClass.Substring("Sudarsky class ".Length,
                    b.PlanetClass.Length - "Sudarsky class ".Length - " gas giant".Length);
                return $"Gas Giant {numeral.ToUpperInvariant()}";
            }

            // "High metal content body" -> "High Metal Content Body"
            var words = b.PlanetClass.Split(' ');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        // GPU thumbnails: the same shader scenes the Planet tab uses (gas giants with the ring shader, thick-atmosphere
        // worlds, landable worlds), drawn live in a small host with Time frozen at a per-body offset so each planet shows a
        // different, still rotation. The host is bitmap-cached (MainWindow.Supersample), so each shader renders once.
        // Returns null for anything the shaders don't cover (landable rocky ice, ringed terrain bodies, software rendering)
        // so the caller keeps the old bitmap renders for those.
        private UIElement? TryBuildShaderThumb(BodyScanDetail b, string iconCode, double size)
        {
            if ((System.Windows.Media.RenderCapability.Tier >> 16) == 0) return null;
            bool gas = PlanetRenderer.IsGasGiantFamily(iconCode);
            bool atmo = PlanetRenderer.IsAtmoWorld(b, iconCode);
            bool landable = !atmo && PlanetRenderer.IsLandableWorld(b, iconCode);
            if (!gas && !atmo && !landable && !(PlanetRenderer.IsTerrainFamily(iconCode) && b.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0))) return null;
            double t = PlanetRenderer.StableHash(b.BodyName) % 900;
            var elements = new List<FrameworkElement>();
            int box;
            double cx, cy;
            if (gas)
            {
                bool ringed = b.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
                box = ringed ? (int)size : (int)Math.Floor(size * 0.8 / 0.46);
                var (ringBack, top) = PlanetRenderer.GetGasGiantShaderStaticLayers(b, iconCode, box, box, shaderRings: ringed);
                var look = PlanetRenderer.GetGasGiantLook(b, iconCode);
                double sphereR;
                (cx, cy, sphereR) = PlanetRenderer.GetGasGiantSphere(b, box, box);
                var effect = GasGiantShaderEffect.Create(look, new Point(cx / box, cy / box), new Point(sphereR / box, sphereR / box));
                effect.SetValue(GasGiantShaderEffect.TimeProperty, t);
                FrameworkElement Surface(Effect e) => MainWindow.Supersample(new Rectangle { Width = box, Height = box, Fill = Brushes.Black, Effect = e });
                if (ringed)
                {
                    var light = (System.Windows.Media.Media3D.Vector3D)effect.GetValue(GasGiantShaderEffect.LightDirProperty);
                    elements.Add(Surface(RingShaderEffect.Create(b, box, box, false, light)));
                    elements.Add(Surface(effect));
                    elements.Add(Surface(RingShaderEffect.Create(b, box, box, true, light)));
                }
                else
                {
                    if (ringBack != null) elements.Add(new Image { Width = box, Height = box, Source = ringBack });
                    elements.Add(Surface(effect));
                }
                elements.Add(new Image { Width = box, Height = box, Source = top });
            }
            else
            {
                bool ringedBody = b.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
                box = ringedBody ? (int)size : (int)Math.Floor(size * 0.82 / 0.68);
                double r;
                (cx, cy, r) = PlanetRenderer.GetTerrainGeometry(box, box, b);
                var ringLight = new System.Windows.Media.Media3D.Vector3D(-0.55, 0.55, 0.63);
                FrameworkElement Surface(Effect e) => MainWindow.Supersample(new Rectangle { Width = box, Height = box, Fill = Brushes.Black, Effect = e });
                if (ringedBody) elements.Add(Surface(RingShaderEffect.Create(b, box, box, false, ringLight, r)));
                if (atmo)
                {
                    var e = AtmoWorldShaderEffect.Create(PlanetRenderer.GetAtmoWorldLook(b, iconCode), new Point(cx / box, cy / box), new Point(r / box, r / box));
                    e.SetValue(AtmoWorldShaderEffect.TimeProperty, t);
                    elements.Add(Surface(e));
                }
                else if (landable)
                {
                    var e = LandableWorldShaderEffect.Create(PlanetRenderer.GetLandableWorldLook(b, iconCode), new Point(cx / box, cy / box), new Point(r / box, r / box));
                    e.SetValue(LandableWorldShaderEffect.TimeProperty, t);
                    elements.Add(Surface(e));
                }
                else
                {
                    // No shader for this planet (e.g. landable rocky ice, airless bodies): the bitmap planet, minus its CPU rings.
                    elements.Add(new Image { Width = box, Height = box, Source = PlanetRenderer.GetTerrainSceneFrame(b, iconCode, box, box, _watcher.SystemPopulation, true) });
                }
                if (ringedBody) elements.Add(Surface(RingShaderEffect.Create(b, box, box, true, ringLight, r)));
            }
            var clip = new Canvas { Width = size, Height = size, ClipToBounds = true };
            foreach (var el in elements)
            {
                Canvas.SetLeft(el, size / 2.0 - cx);
                Canvas.SetTop(el, size / 2.0 - cy);
                clip.Children.Add(el);
            }
            return clip;
        }

        // Real scaled-down renders — the original ask ("use our planet/star/asteroid renders,
        // scaled down") — for terrain-family planets (PlanetRenderer.GetTerrainSceneFrame,
        // already a transparent-background sphere-only render, same technique the Deorbit arc
        // scene uses), gas giants (GetGasGiantLayers — see below), and asteroid belts
        // (AsteroidFieldRenderer). Falls back to a flat gradient swatch only if a render
        // genuinely throws.
        private UIElement BuildThumb(BodyScanDetail b, double size)
        {
            try
            {
                if (b.IsBelt)
                {
                    var frame = AsteroidFieldRenderer.GetAsteroidFieldFrame(b.BodyName, null, (int)size, (int)size);
                    return new Image { Source = frame, Width = size, Height = size, Stretch = Stretch.UniformToFill, ClipToBounds = true };
                }
                var iconCode = MainWindow.MapPlanetClassToIconCode(b.PlanetClass);
                if (iconCode != null && TryBuildShaderThumb(b, iconCode, size) is UIElement shaderThumb) return shaderThumb;
                if (iconCode != null && PlanetRenderer.IsGasGiantFamily(iconCode))
                {
                    // GetGasGiantLayers turned out to already BE 4 static bitmaps — the
                    // animation MainWindow builds is just a cross-fade opacity binding on TWO
                    // of them; the renderer itself isn't inherently animated at all. Compositing
                    // base + one cloud frame + top gives a real single-frame render, no
                    // animation needed.
                    bool hasRings = b.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
                    if (hasRings)
                    {
                        // Rendered whole (not center-cropped like the terrain sphere) since a
                        // ringed gas giant's rings extend well past a tight circle — Stretch=
                        // Uniform shrinks the whole ringed scene to fit instead of cropping the
                        // rings off.
                        const int box = 140;
                        var (baseLayer, cloudA, _, topLayer) = PlanetRenderer.GetGasGiantLayers(b, iconCode, box, box);
                        var grid = new Grid { Width = size, Height = size };
                        grid.Children.Add(new Image { Source = baseLayer, Stretch = Stretch.Uniform });
                        grid.Children.Add(new Image { Source = cloudA, Stretch = Stretch.Uniform });
                        grid.Children.Add(new Image { Source = topLayer, Stretch = Stretch.Uniform });
                        return grid;
                    }
                    else
                    {
                        // No rings — the scene's own sphere radius is a FIXED 0.23×box either
                        // way (real constant read from PlanetRenderer's source), so a ringless
                        // body's sphere only ever fills ~46% of the same box a ringed one's
                        // rings would otherwise fill most of. Stretch=Uniform-ing that same box
                        // made a ringless gas giant look noticeably smaller — real cause,
                        // confirmed by comparing both cases. Same center-crop technique as the
                        // terrain sphere fixes it, but targeting only ~80% fill (not 100%) —
                        // the "top" layer draws an atmosphere glow that extends past the sphere's
                        // own radius, and cropping tight to exactly the sphere hard-clipped that
                        // glow at a visible edge (confirmed on a real screenshot: "getting edges
                        // cut off"). The extra margin gives the glow room to fade out instead.
                        const double fillFraction = 0.8;
                        int box = (int)Math.Floor(size * fillFraction / 0.46);
                        var (baseLayer, cloudA, _, topLayer) = PlanetRenderer.GetGasGiantLayers(b, iconCode, box, box);
                        double cx = box / 2.0, cy = box / 2.0 + box * 0.02;
                        var clip = new Canvas { Width = size, Height = size, ClipToBounds = true };
                        foreach (var src in new[] { baseLayer, cloudA, topLayer })
                        {
                            var img = new Image { Source = src, Width = box, Height = box };
                            Canvas.SetLeft(img, size / 2.0 - cx);
                            Canvas.SetTop(img, size / 2.0 - cy);
                            clip.Children.Add(img);
                        }
                        return clip;
                    }
                }
                if (iconCode != null && PlanetRenderer.IsTerrainFamily(iconCode))
                {
                    // Real report + screenshot: a ringed terrain body's ring came out almost
                    // entirely clipped off in this thumbnail, looking like a stray diagonal
                    // sliver rather than a ring — same root cause the gas giant branch above
                    // already solved for its own ringed case. This crop was built (and correctly
                    // tuned) back when terrain bodies never had rings at all, so it only ever
                    // sized the viewport to the sphere itself. Same fix as gas giants, literally:
                    // same 140x140 box, render the ringed case whole via Stretch=Uniform instead
                    // of center-cropped — GetTerrainGeometry already renders a ringed body at the
                    // gas giant's own sphere fraction and ring proportions now (see its own
                    // comment), so this box doesn't need to be anything other than what the gas
                    // giant branch above already uses for the exact same case.
                    bool hasRings = b.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
                    if (hasRings)
                    {
                        const int ringedBox = 140;
                        var ringedFrame = PlanetRenderer.GetTerrainSceneFrame(b, iconCode, ringedBox, ringedBox, _watcher.SystemPopulation);
                        return new Image { Source = ringedFrame, Width = size, Height = size, Stretch = Stretch.Uniform };
                    }

                    // GetTerrainGeometry's own 0.34 is the sphere's RADIUS as a fraction of the
                    // box (confirmed straight from its source) — so the sphere's DIAMETER is
                    // 0.68 of the box, not 0.34. Dividing by 0.34 here (copying the Deorbit
                    // scene's math without checking it wanted a RADIUS, not a diameter) rendered
                    // every thumbnail at roughly double the intended size, so this tiny clipped
                    // viewport only ever showed a zoomed-in crop of it — exactly the "close-up,
                    // not the whole planet" bug. Dividing by 0.68 (2 × 0.34) gets the sphere's
                    // diameter to actually equal `size`.
                    // Math.Floor (not Ceiling) — rounding UP here meant the sphere's real
                    // diameter came out a hair LARGER than `size`, so its very top/bottom edge
                    // got clipped by the size×size viewport ("flat on top/bottom", confirmed on
                    // a real moon-row screenshot). Rounding down keeps the sphere a hair
                    // *smaller* than the viewport instead, which just leaves a sliver of
                    // transparent margin — invisible — rather than a visible flat clip.
                    // Real report + screenshot: sphere edges "cut off again" on cards and moon
                    // rows once the atmosphere edge glow was added — that glow extends up to
                    // R*1.22 past the sphere (see PlanetRenderer's hasAnyAtmosphere glow), and a
                    // sphere sized to exactly fill `size` left the glow no room, so the viewport
                    // clipped it into a hard flat edge. Same fix the ringless gas-giant branch
                    // above already uses: fill ~82% of the box (1/1.22), leaving the glow room to
                    // fade out. Applied to every terrain body, atmosphere or not, so all cards
                    // keep a consistent planet size rather than airless ones rendering bigger.
                    const double terrainFillFraction = 0.82;
                    int box = (int)Math.Floor(size * terrainFillFraction / 0.68);
                    var frame = PlanetRenderer.GetTerrainSceneFrame(b, iconCode, box, box, _watcher.SystemPopulation);
                    var (cx, cy, _) = PlanetRenderer.GetTerrainGeometry(box, box, b);
                    var img = new Image { Source = frame, Width = box, Height = box };
                    Canvas.SetLeft(img, size / 2.0 - cx);
                    Canvas.SetTop(img, size / 2.0 - cy);
                    var clip = new Canvas { Width = size, Height = size, ClipToBounds = true };
                    clip.Children.Add(img);
                    return clip;
                }
            }
            catch (Exception ex) { Log.Write($"SystemScanWindow.BuildThumb error for {b.BodyName}: {ex.Message}"); }

            return new Ellipse { Width = size, Height = size, Fill = PlanetSwatch(b) };
        }

        // Flat/gradient swatch color by PlanetClass — a small stand-in for the full renderer,
        // not trying to replicate PlanetRenderer's real terrain art at 20-42px.
        private static Brush PlanetSwatch(BodyScanDetail b)
        {
            if (b.IsBelt) return Brush("#5c6f78");
            var c = (b.PlanetClass ?? "").ToLowerInvariant();
            if (c.Contains("metal rich")) return Brush("#8a7f74");
            if (c.Contains("high metal")) return new RadialGradientBrush(Brush("#e8b6a0").Color, Brush("#4a1c10").Color);
            if (c.Contains("water world") || c.Contains("earthlike")) return new LinearGradientBrush(Brush("#7fc4e0").Color, Brush("#2a5878").Color, 45);
            if (c.Contains("ammonia")) return Brush("#c9a45c");
            if (c.Contains("icy") || c.Contains("rocky ice")) return new LinearGradientBrush(Brush("#dfe8ec").Color, Brush("#9fb2bc").Color, 45);
            if (c.Contains("rocky")) return new RadialGradientBrush(Brush("#d9c9a8").Color, Brush("#8a6a3c").Color);
            if (c.Contains("gas giant") || c.Contains("giant")) return new RadialGradientBrush(Brush("#f2d9a0").Color, Brush("#5c3a16").Color);
            return Brush("#8a7f74");
        }
    }
}
