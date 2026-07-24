using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EliteBioRadar
{
    public partial class ScanLogWindow : Window
    {
        private static readonly SolidColorBrush BioAccent       = Brush("#00e5ff");
        private static readonly SolidColorBrush GeoAccent       = Brush("#ffaa00");
        private static readonly SolidColorBrush StellarAccent   = Brush("#ffd166");
        private static readonly SolidColorBrush WorldsAccent    = Brush("#7ee787");
        private static readonly SolidColorBrush PhenomenaAccent = Brush("#c792ea");
        private static readonly SolidColorBrush JournalsAccent  = Brush("#9db4c9");
        private static readonly SolidColorBrush DimLabel        = Brush("#4c7373");
        private static readonly SolidColorBrush RowHoverBg      = Brush("#123030");
        private static readonly SolidColorBrush RowBg           = Brush("#0a1515");
        private static readonly SolidColorBrush RowBorder       = Brush("#153636");
        private static readonly SolidColorBrush EarthlikeColor  = Brush("#eafff0");
        private static readonly SolidColorBrush ExoticColor     = Brush("#fff1cf");
        private static readonly SolidColorBrush ChipActiveBg    = Brush("#123018");

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

        // Loaded once per window-open (and refreshed after Scan All / Clear), then
        // reused by every tab render and every filter toggle. Each Render* method
        // used to call ScanCache.LoadAll() itself — a full JSON deserialize of the
        // entire cache (18,000+ bodies once Stellar/Worlds are populated) — five
        // times on window open and again on every single filter click. That's pure
        // CPU-bound parsing work (no disk I/O, which is why it showed no disk
        // activity while hanging) and is what caused the freezes.
        private Dictionary<string, LoadedBodyData> _cache = new();

        // Same problem, smaller file: RegionCache.GetRegion() re-reads and
        // re-parses regions.json on every single call. Called once per body in
        // every aggregation loop (thousands of times per render), that's
        // thousands of redundant file reads under a shared lock. Load once here
        // and look up in memory instead.
        private Dictionary<string, string> _regions = new(StringComparer.OrdinalIgnoreCase);

        private string GetRegionFor(string system) =>
            string.IsNullOrEmpty(system) ? RegionCache.UnknownRegion
                : _regions.TryGetValue(system, out var region) ? region : RegionCache.UnknownRegion;

        // ---------------------------------------------------------------
        //  Lifetime / Date Range / Since-date filter — shared across all five
        //  data tabs. Null/null means "Lifetime" (no filtering). Pure in-memory
        //  filtering over _cache, same as the Worlds Terraformable/Footfalled
        //  chips — no disk access, so switching ranges should always be instant.
        // ---------------------------------------------------------------
        private DateTime? _rangeFrom;
        private DateTime? _rangeTo;
        private string _rangeMode = "daterange"; // or "since"

        private bool InRange(DateTime t) =>
            (!_rangeFrom.HasValue || t >= _rangeFrom.Value) && (!_rangeTo.HasValue || t < _rangeTo.Value);

        private bool HasActiveRange => _rangeFrom.HasValue || _rangeTo.HasValue;

        // ---------------------------------------------------------------
        //  Group By (Region / <tab-specific type>) — one toggle per tab, all
        //  default to Region. Swapping just changes which value is the outer
        //  vs inner aggregation key; the existing 3-level row hierarchy
        //  (outer -> inner -> leaf) already supports either order unchanged.
        // ---------------------------------------------------------------
        private bool _bioGroupByRegion = true;
        private bool _geoGroupByRegion = true;
        private bool _stellarGroupByRegion = true;
        private bool _worldsGroupByRegion = true;
        private bool _phenomenaGroupByRegion = true;

        private void SetGroupChips(Border regionChip, TextBlock regionText, Border typeChip, TextBlock typeText,
                                    SolidColorBrush accent, bool byRegion)
        {
            regionChip.BorderBrush = byRegion ? accent : DimLabel;
            regionChip.Background  = byRegion ? ChipActiveBg : Brushes.Transparent;
            regionText.Foreground  = byRegion ? accent : Color(0x88, 0xbb, 0xbb);

            typeChip.BorderBrush = !byRegion ? accent : DimLabel;
            typeChip.Background  = !byRegion ? ChipActiveBg : Brushes.Transparent;
            typeText.Foreground  = !byRegion ? accent : Color(0x88, 0xbb, 0xbb);
        }

        private void ChipBioGroupRegion_Click(object sender, MouseButtonEventArgs e)
        { _bioGroupByRegion = true; SetGroupChips(chipBioGroupRegion, txtChipBioGroupRegion, chipBioGroupGenus, txtChipBioGroupGenus, BioAccent, true); RenderBiology(); }
        private void ChipBioGroupGenus_Click(object sender, MouseButtonEventArgs e)
        { _bioGroupByRegion = false; SetGroupChips(chipBioGroupRegion, txtChipBioGroupRegion, chipBioGroupGenus, txtChipBioGroupGenus, BioAccent, false); RenderBiology(); }

        private void ChipGeoGroupRegion_Click(object sender, MouseButtonEventArgs e)
        { _geoGroupByRegion = true; SetGroupChips(chipGeoGroupRegion, txtChipGeoGroupRegion, chipGeoGroupType, txtChipGeoGroupType, GeoAccent, true); RenderGeology(); }
        private void ChipGeoGroupType_Click(object sender, MouseButtonEventArgs e)
        { _geoGroupByRegion = false; SetGroupChips(chipGeoGroupRegion, txtChipGeoGroupRegion, chipGeoGroupType, txtChipGeoGroupType, GeoAccent, false); RenderGeology(); }

        private void ChipStellarGroupRegion_Click(object sender, MouseButtonEventArgs e)
        { _stellarGroupByRegion = true; SetGroupChips(chipStellarGroupRegion, txtChipStellarGroupRegion, chipStellarGroupClass, txtChipStellarGroupClass, StellarAccent, true); RenderStellar(); }
        private void ChipStellarGroupClass_Click(object sender, MouseButtonEventArgs e)
        { _stellarGroupByRegion = false; SetGroupChips(chipStellarGroupRegion, txtChipStellarGroupRegion, chipStellarGroupClass, txtChipStellarGroupClass, StellarAccent, false); RenderStellar(); }

        private void ChipWorldsGroupRegion_Click(object sender, MouseButtonEventArgs e)
        { _worldsGroupByRegion = true; SetGroupChips(chipWorldsGroupRegion, txtChipWorldsGroupRegion, chipWorldsGroupType, txtChipWorldsGroupType, WorldsAccent, true); RenderWorlds(); }
        private void ChipWorldsGroupType_Click(object sender, MouseButtonEventArgs e)
        { _worldsGroupByRegion = false; SetGroupChips(chipWorldsGroupRegion, txtChipWorldsGroupRegion, chipWorldsGroupType, txtChipWorldsGroupType, WorldsAccent, false); RenderWorlds(); }

        private void ChipPhenomenaGroupRegion_Click(object sender, MouseButtonEventArgs e)
        { _phenomenaGroupByRegion = true; SetGroupChips(chipPhenomenaGroupRegion, txtChipPhenomenaGroupRegion, chipPhenomenaGroupCategory, txtChipPhenomenaGroupCategory, PhenomenaAccent, true); RenderPhenomena(); }
        private void ChipPhenomenaGroupCategory_Click(object sender, MouseButtonEventArgs e)
        { _phenomenaGroupByRegion = false; SetGroupChips(chipPhenomenaGroupRegion, txtChipPhenomenaGroupRegion, chipPhenomenaGroupCategory, txtChipPhenomenaGroupCategory, PhenomenaAccent, false); RenderPhenomena(); }

        private void RenderAll()
        {
            RenderBiology();
            RenderGeology();
            RenderStellar();
            RenderWorlds();
            RenderPhenomena();
        }

        public ScanLogWindow()
        {
            InitializeComponent();
            _cache = ScanCache.LoadAll();
            _regions = RegionCache.LoadAll();
            RenderAll();
        }

        // ---------------------------------------------------------------
        //  Tabs
        // ---------------------------------------------------------------
        private void SelectTab(string tab)
        {
            bioPanel.Visibility       = tab == "bio"       ? Visibility.Visible : Visibility.Collapsed;
            geoPanel.Visibility       = tab == "geo"       ? Visibility.Visible : Visibility.Collapsed;
            stellarPanel.Visibility   = tab == "stellar"   ? Visibility.Visible : Visibility.Collapsed;
            worldsPanel.Visibility    = tab == "worlds"    ? Visibility.Visible : Visibility.Collapsed;
            phenomenaPanel.Visibility = tab == "phenomena" ? Visibility.Visible : Visibility.Collapsed;
            journalsPanel.Visibility  = tab == "journals"  ? Visibility.Visible : Visibility.Collapsed;

            txtTabBio.Foreground       = tab == "bio"       ? BioAccent       : DimLabel;
            txtTabGeo.Foreground       = tab == "geo"       ? GeoAccent       : DimLabel;
            txtTabStellar.Foreground   = tab == "stellar"   ? StellarAccent   : DimLabel;
            txtTabWorlds.Foreground    = tab == "worlds"    ? WorldsAccent    : DimLabel;
            txtTabPhenomena.Foreground = tab == "phenomena" ? PhenomenaAccent : DimLabel;
            txtTabJournals.Foreground  = tab == "journals"  ? JournalsAccent  : DimLabel;

            tabBio.BorderBrush       = tab == "bio"       ? BioAccent       : Brushes.Transparent;
            tabGeo.BorderBrush       = tab == "geo"       ? GeoAccent       : Brushes.Transparent;
            tabStellar.BorderBrush   = tab == "stellar"   ? StellarAccent   : Brushes.Transparent;
            tabWorlds.BorderBrush    = tab == "worlds"    ? WorldsAccent    : Brushes.Transparent;
            tabPhenomena.BorderBrush = tab == "phenomena" ? PhenomenaAccent : Brushes.Transparent;
            tabJournals.BorderBrush  = tab == "journals"  ? JournalsAccent  : Brushes.Transparent;
        }

        private void TabBio_Click(object sender, MouseButtonEventArgs e)       => SelectTab("bio");
        private void TabGeo_Click(object sender, MouseButtonEventArgs e)       => SelectTab("geo");
        private void TabStellar_Click(object sender, MouseButtonEventArgs e)   => SelectTab("stellar");
        private void TabWorlds_Click(object sender, MouseButtonEventArgs e)    => SelectTab("worlds");
        private void TabPhenomena_Click(object sender, MouseButtonEventArgs e) => SelectTab("phenomena");
        private void TabJournals_Click(object sender, MouseButtonEventArgs e)  => SelectTab("journals");

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ---------------------------------------------------------------
        //  Biology aggregation: region -> genus -> species -> count/last body
        // ---------------------------------------------------------------
        private class SpeciesAgg { public int Count; public DateTime LastSeen; public string LastBody = ""; }
        private class GenusAgg   { public int Total; public Dictionary<string, SpeciesAgg> Species = new(StringComparer.OrdinalIgnoreCase); }
        private class RegionAgg  { public int Total; public Dictionary<string, GenusAgg> Genera = new(StringComparer.OrdinalIgnoreCase); }

        private void RenderBiology()
        {
            bioStack.Children.Clear();
            var all = _cache;
            var regions = new Dictionary<string, RegionAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in all)
            {
                var body = kvp.Value;
                var region = GetRegionFor(body.System);

                foreach (var org in body.Organisms.Where(o => o.IsComplete && InRange(o.LastSeen)))
                {
                    var outerKey = _bioGroupByRegion ? region : org.Genus;
                    var innerKey = _bioGroupByRegion ? org.Genus : region;

                    if (!regions.TryGetValue(outerKey, out var ragg)) { ragg = new RegionAgg(); regions[outerKey] = ragg; }
                    ragg.Total++;

                    if (!ragg.Genera.TryGetValue(innerKey, out var gagg)) { gagg = new GenusAgg(); ragg.Genera[innerKey] = gagg; }
                    gagg.Total++;

                    var speciesKey = string.IsNullOrEmpty(org.Species) ? org.Genus : org.DisplayName;
                    if (!gagg.Species.TryGetValue(speciesKey, out var sagg)) { sagg = new SpeciesAgg(); gagg.Species[speciesKey] = sagg; }
                    sagg.Count++;
                    if (org.LastSeen >= sagg.LastSeen) { sagg.LastSeen = org.LastSeen; sagg.LastBody = kvp.Key; }
                }
            }

            if (regions.Count == 0)
            {
                bioStack.Children.Add(EmptyMessage(HasActiveRange
                    ? "No biology scans in this range."
                    : "No biology scans yet — run Scan All from the Journals tab."));
                return;
            }

            foreach (var rkvp in regions.OrderByDescending(r => r.Value.Total))
            {
                var detail = new StackPanel { Margin = new Thickness(18, 2, 0, 6), Visibility = Visibility.Collapsed };
                foreach (var gkvp in rkvp.Value.Genera.OrderByDescending(g => g.Value.Total))
                {
                    var speciesDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                    foreach (var skvp in gkvp.Value.Species.OrderByDescending(s => s.Value.Count))
                    {
                        speciesDetail.Children.Add(BuildLeafRow(skvp.Key, $"last: {skvp.Value.LastBody}",
                            skvp.Value.Count, BioAccent));
                    }
                    detail.Children.Add(BuildExpandableRow(gkvp.Key, gkvp.Value.Total, BioAccent, speciesDetail, indent: false));
                    detail.Children.Add(speciesDetail);
                }
                bioStack.Children.Add(BuildExpandableRow(rkvp.Key, rkvp.Value.Total, BioAccent, detail, indent: true));
                bioStack.Children.Add(detail);
            }
        }

        // ---------------------------------------------------------------
        //  Geology aggregation: region -> site type -> instance (body/payout/last seen)
        // ---------------------------------------------------------------
        private class GeoInstance { public string Body = ""; public long Payout; public DateTime LastSeen; }
        private class GeoTypeAgg  { public int Total; public List<GeoInstance> Instances = new(); }
        private class GeoRegionAgg{ public int Total; public Dictionary<string, GeoTypeAgg> Types = new(StringComparer.OrdinalIgnoreCase); }

        private void RenderGeology()
        {
            geoStack.Children.Clear();
            var all = _cache;
            var regions = new Dictionary<string, GeoRegionAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in all)
            {
                var body = kvp.Value;
                var region = GetRegionFor(body.System);

                foreach (var site in body.GeoSites.Where(s => InRange(s.LastSeen)))
                {
                    var outerKey = _geoGroupByRegion ? region : site.Name;
                    var innerKey = _geoGroupByRegion ? site.Name : region;

                    if (!regions.TryGetValue(outerKey, out var ragg)) { ragg = new GeoRegionAgg(); regions[outerKey] = ragg; }
                    ragg.Total++;

                    if (!ragg.Types.TryGetValue(innerKey, out var tagg)) { tagg = new GeoTypeAgg(); ragg.Types[innerKey] = tagg; }
                    tagg.Total++;
                    tagg.Instances.Add(new GeoInstance { Body = kvp.Key, Payout = site.Payout, LastSeen = site.LastSeen });
                }
            }

            if (regions.Count == 0)
            {
                geoStack.Children.Add(EmptyMessage(HasActiveRange
                    ? "No geology sites in this range."
                    : "No geology sites yet — run Scan All from the Journals tab."));
                return;
            }

            foreach (var rkvp in regions.OrderByDescending(r => r.Value.Total))
            {
                var detail = new StackPanel { Margin = new Thickness(18, 2, 0, 6), Visibility = Visibility.Collapsed };
                foreach (var tkvp in rkvp.Value.Types.OrderByDescending(t => t.Value.Total))
                {
                    var instDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                    foreach (var inst in tkvp.Value.Instances.OrderByDescending(i => i.LastSeen))
                    {
                        var sub = inst.Payout > 0 ? $"{inst.Payout:N0} Cr" : "no discovery bonus";
                        instDetail.Children.Add(BuildLeafRow(inst.Body, sub, 1, GeoAccent));
                    }
                    detail.Children.Add(BuildExpandableRow(tkvp.Key, tkvp.Value.Total, GeoAccent, instDetail, indent: false));
                    detail.Children.Add(instDetail);
                }
                geoStack.Children.Add(BuildExpandableRow(rkvp.Key, rkvp.Value.Total, GeoAccent, detail, indent: true));
                geoStack.Children.Add(detail);
            }
        }

        // ---------------------------------------------------------------
        //  Stellar aggregation: region -> star class -> First Discovery/Already
        //  Catalogued -> instance (system name).
        // ---------------------------------------------------------------
        private class StatusAgg  { public int Total; public List<(string system, DateTime lastSeen)> Instances = new(); }
        private class StarClassAgg { public int Total; public bool Exotic; public StatusAgg First = new(); public StatusAgg Catalogued = new(); }
        private class StellarRegionAgg { public int Total; public Dictionary<string, StarClassAgg> Classes = new(StringComparer.OrdinalIgnoreCase); }

        private void RenderStellar()
        {
            stellarStack.Children.Clear();
            var all = _cache;
            var regions = new Dictionary<string, StellarRegionAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in all)
            {
                var body = kvp.Value;
                if (string.IsNullOrEmpty(body.StarType)) continue;
                if (!body.ScannedAt.HasValue || !InRange(body.ScannedAt.Value)) continue;

                var region = GetRegionFor(body.System);
                var className = StarClassNames.GetDisplayName(body.StarType);
                var outerKey = _stellarGroupByRegion ? region : className;
                var innerKey = _stellarGroupByRegion ? className : region;

                if (!regions.TryGetValue(outerKey, out var ragg)) { ragg = new StellarRegionAgg(); regions[outerKey] = ragg; }
                ragg.Total++;

                if (!ragg.Classes.TryGetValue(innerKey, out var cagg))
                {
                    cagg = new StarClassAgg { Exotic = StarClassNames.IsExotic(body.StarType) };
                    ragg.Classes[innerKey] = cagg;
                }
                cagg.Total++;

                var status = body.WasDiscovered == false ? cagg.First : cagg.Catalogued;
                status.Total++;
                status.Instances.Add((string.IsNullOrEmpty(body.System) ? kvp.Key : body.System, body.ScannedAt.Value));
            }

            if (regions.Count == 0)
            {
                stellarStack.Children.Add(EmptyMessage(HasActiveRange
                    ? "No stars scanned in this range."
                    : "No stars scanned yet — run Scan All from the Journals tab."));
                return;
            }

            foreach (var rkvp in regions.OrderByDescending(r => r.Value.Total))
            {
                var detail = new StackPanel { Margin = new Thickness(18, 2, 0, 6), Visibility = Visibility.Collapsed };
                foreach (var ckvp in rkvp.Value.Classes.OrderByDescending(c => c.Value.Total))
                {
                    var statusDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                    AddStatusRows(statusDetail, ckvp.Value.First, ckvp.Value.Catalogued, StellarAccent);

                    var label = ckvp.Value.Exotic ? $"{ckvp.Key} ✦" : ckvp.Key;
                    detail.Children.Add(BuildExpandableRow(label, ckvp.Value.Total, StellarAccent, statusDetail,
                        indent: false, bold: ckvp.Value.Exotic, boldColor: ckvp.Value.Exotic ? ExoticColor : null));
                    detail.Children.Add(statusDetail);
                }

                // When grouped by star class, the outer key IS the class — carry its
                // exotic marker up here instead (any child shares the same flag, since
                // it's a property of the class, not of whichever region it's nested under).
                bool outerExotic = !_stellarGroupByRegion && rkvp.Value.Classes.Values.FirstOrDefault()?.Exotic == true;
                var outerLabel = outerExotic ? $"{rkvp.Key} ✦" : rkvp.Key;
                stellarStack.Children.Add(BuildExpandableRow(outerLabel, rkvp.Value.Total, StellarAccent, detail,
                    indent: true, bold: outerExotic, boldColor: outerExotic ? ExoticColor : null));
                stellarStack.Children.Add(detail);
            }
        }

        // Shared by Stellar and Worlds: renders the First Discovery / Already
        // Catalogued sub-groups under a type row — the type itself is never
        // repeated underneath, only the status heading + last-seen system.
        private void AddStatusRows(StackPanel parent, StatusAgg first, StatusAgg catalogued, SolidColorBrush accent)
        {
            if (first.Total > 0)
            {
                var firstDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                parent.Children.Add(BuildExpandableRow("First Discovery", first.Total, accent, firstDetail,
                    indent: false, bold: false, boldColor: accent,
                    onFirstExpand: () => PopulateInstanceRows(firstDetail, first.Instances, accent)));
                parent.Children.Add(firstDetail);
            }
            if (catalogued.Total > 0)
            {
                var catDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                parent.Children.Add(BuildExpandableRow("Already Catalogued", catalogued.Total, accent, catDetail,
                    indent: false, bold: false, boldColor: DimLabel,
                    onFirstExpand: () => PopulateInstanceRows(catDetail, catalogued.Instances, accent)));
                parent.Children.Add(catDetail);
            }
        }

        // ---------------------------------------------------------------
        //  Worlds aggregation: region -> planet class -> First Discovery/Already
        //  Catalogued (+ First Footfall) -> instance (system name).
        // ---------------------------------------------------------------
        private class WorldClassAgg
        {
            public int Total; public bool Earthlike; public bool AnyTerraformable; public bool AnyFootfalled;
            public StatusAgg First = new(); public StatusAgg Catalogued = new(); public StatusAgg Footfall = new();
        }
        private class WorldsRegionAgg { public int Total; public Dictionary<string, WorldClassAgg> Classes = new(StringComparer.OrdinalIgnoreCase); }

        private bool _filterTerraformable;
        private bool _filterFootfalled;

        private void RenderWorlds()
        {
            worldsStack.Children.Clear();
            var all = _cache;
            var regions = new Dictionary<string, WorldsRegionAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in all)
            {
                var body = kvp.Value;
                if (string.IsNullOrEmpty(body.PlanetClass)) continue;

                // Which timestamp is "relevant" depends on what's being filtered.
                // ScannedAt is when this body's classification was last (re)scanned —
                // fine for browsing by exploration recency. But a world is usually
                // footfalled once and never rescanned again, while ScannedAt keeps
                // advancing on unrelated later scans of the same body. Gating
                // "Footfalled only" by ScannedAt excluded worlds the player had
                // genuinely walked on, just because their last classification scan
                // predated the range — use FootfallAt instead when that filter is on.
                var relevantDate = _filterFootfalled ? body.FootfallAt : body.ScannedAt;
                if (!relevantDate.HasValue || !InRange(relevantDate.Value)) continue;

                bool isTerraformable = string.Equals(body.TerraformState, "Terraformable", StringComparison.OrdinalIgnoreCase);
                bool isFootfalled    = body.Landable && body.WasFootfalled;

                if (_filterTerraformable && !isTerraformable) continue;
                if (_filterFootfalled && !isFootfalled) continue;

                var region = GetRegionFor(body.System);
                var outerKey = _worldsGroupByRegion ? region : body.PlanetClass;
                var innerKey = _worldsGroupByRegion ? body.PlanetClass : region;

                if (!regions.TryGetValue(outerKey, out var ragg)) { ragg = new WorldsRegionAgg(); regions[outerKey] = ragg; }
                ragg.Total++;

                if (!ragg.Classes.TryGetValue(innerKey, out var cagg))
                {
                    cagg = new WorldClassAgg { Earthlike = body.PlanetClass.Contains("Earthlike", StringComparison.OrdinalIgnoreCase) };
                    ragg.Classes[innerKey] = cagg;
                }
                cagg.Total++;
                cagg.AnyTerraformable |= isTerraformable;
                cagg.AnyFootfalled    |= isFootfalled;

                var lastSystem = string.IsNullOrEmpty(body.System) ? kvp.Key : body.System;
                var status = body.WasDiscovered == false ? cagg.First : cagg.Catalogued;
                status.Total++;
                // ScannedAt is always set alongside PlanetClass in HandleScan, so this is
                // safe even though relevantDate above may have checked FootfallAt instead.
                status.Instances.Add((lastSystem, body.ScannedAt!.Value));

                if (isFootfalled)
                {
                    cagg.Footfall.Total++;
                    cagg.Footfall.Instances.Add((lastSystem, body.FootfallAt ?? body.ScannedAt.Value));
                }
            }

            if (regions.Count == 0)
            {
                worldsStack.Children.Add(EmptyMessage(_filterTerraformable || _filterFootfalled
                    ? "No worlds match the current filter."
                    : HasActiveRange
                        ? "No worlds scanned in this range."
                        : "No worlds scanned yet — run Scan All from the Journals tab."));
                return;
            }

            foreach (var rkvp in regions.OrderByDescending(r => r.Value.Total))
            {
                var detail = new StackPanel { Margin = new Thickness(18, 2, 0, 6), Visibility = Visibility.Collapsed };
                foreach (var ckvp in rkvp.Value.Classes.OrderByDescending(c => c.Value.Total))
                {
                    var statusDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                    AddStatusRows(statusDetail, ckvp.Value.First, ckvp.Value.Catalogued, WorldsAccent);

                    // First Footfall only appears when at least one instance of this
                    // type has actually been walked on — invisible for gas giants,
                    // invisible until the player's first landing on that class.
                    if (ckvp.Value.Footfall.Total > 0)
                    {
                        var ffInstances = ckvp.Value.Footfall.Instances;
                        var ffDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                        statusDetail.Children.Add(BuildExpandableRow("First Footfall", ckvp.Value.Footfall.Total, WorldsAccent,
                            ffDetail, indent: false, bold: true, boldColor: EarthlikeColor,
                            onFirstExpand: () => PopulateInstanceRows(ffDetail, ffInstances, WorldsAccent)));
                        statusDetail.Children.Add(ffDetail);
                    }

                    string label = ckvp.Key;
                    if (ckvp.Value.Earthlike) label += "  RARE";
                    var row = BuildExpandableRow(label, ckvp.Value.Total, WorldsAccent, statusDetail,
                        indent: false, bold: ckvp.Value.Earthlike, boldColor: ckvp.Value.Earthlike ? EarthlikeColor : null);
                    detail.Children.Add(row);
                    detail.Children.Add(statusDetail);

                    if (ckvp.Value.AnyTerraformable)
                        detail.Children.Add(SmallNote("↳ includes terraformable candidates", WorldsAccent));
                }

                // When grouped by planet type, the outer key IS the type — carry its
                // Earthlike/terraformable flags up here instead (every child shares
                // them, since they're properties of the type, not of the region it's
                // nested under).
                bool outerEarthlike = !_worldsGroupByRegion && rkvp.Value.Classes.Values.FirstOrDefault()?.Earthlike == true;
                bool outerTerraformable = !_worldsGroupByRegion && rkvp.Value.Classes.Values.Any(c => c.AnyTerraformable);
                var outerLabel = rkvp.Key + (outerEarthlike ? "  RARE" : "");
                worldsStack.Children.Add(BuildExpandableRow(outerLabel, rkvp.Value.Total, WorldsAccent, detail,
                    indent: true, bold: outerEarthlike, boldColor: outerEarthlike ? EarthlikeColor : null));
                worldsStack.Children.Add(detail);
                if (outerTerraformable)
                    worldsStack.Children.Add(SmallNote("↳ includes terraformable candidates", WorldsAccent));
            }
        }

        private void ChipTerraformable_Click(object sender, MouseButtonEventArgs e)
        {
            _filterTerraformable = !_filterTerraformable;
            chipTerraformable.Background = _filterTerraformable ? ChipActiveBg : Brushes.Transparent;
            txtChipTerraformable.Foreground = _filterTerraformable ? WorldsAccent : Color(0x88, 0xbb, 0xbb);
            RenderWorlds();
        }

        private void ChipFootfalled_Click(object sender, MouseButtonEventArgs e)
        {
            _filterFootfalled = !_filterFootfalled;
            chipFootfalled.Background = _filterFootfalled ? ChipActiveBg : Brushes.Transparent;
            txtChipFootfalled.Foreground = _filterFootfalled ? WorldsAccent : Color(0x88, 0xbb, 0xbb);
            RenderWorlds();
        }

        // ---------------------------------------------------------------
        //  Range strip (Lifetime / Date Range / Since date) — shared toolbar
        //  above every tab. See _rangeFrom/_rangeTo/InRange above.
        // ---------------------------------------------------------------
        private void ChipRange_Click(object sender, MouseButtonEventArgs e) =>
            rangePanel.Visibility = rangePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;

        private void ModeDateRange_Click(object sender, MouseButtonEventArgs e) => SetRangeMode("daterange");
        private void ModeSince_Click(object sender, MouseButtonEventArgs e)     => SetRangeMode("since");

        private void SetRangeMode(string mode)
        {
            _rangeMode = mode;
            dateRangeFields.Visibility = mode == "daterange" ? Visibility.Visible : Visibility.Collapsed;
            sinceFields.Visibility     = mode == "since"     ? Visibility.Visible : Visibility.Collapsed;

            modeDateRange.BorderBrush = mode == "daterange" ? BioAccent : DimLabel;
            ((TextBlock)modeDateRange.Child).Foreground = mode == "daterange" ? BioAccent : Color(0x88, 0xbb, 0xbb);
            modeSince.BorderBrush = mode == "since" ? BioAccent : DimLabel;
            txtModeSince.Foreground = mode == "since" ? BioAccent : Color(0x88, 0xbb, 0xbb);
        }

        private void BtnRangeApply_Click(object sender, MouseButtonEventArgs e)
        {
            if (_rangeMode == "since")
            {
                if (dpSince.SelectedDate == null) return;
                _rangeFrom = dpSince.SelectedDate.Value.Date;
                _rangeTo   = null;
                txtChipRange.Text = $"Since {_rangeFrom.Value:yyyy-MM-dd} ▾";
            }
            else
            {
                if (dpFrom.SelectedDate == null && dpTo.SelectedDate == null) return;
                _rangeFrom = dpFrom.SelectedDate?.Date;
                // Include the full end day, same convention MainWindow's date-range
                // journal scan already uses.
                _rangeTo   = dpTo.SelectedDate?.Date.AddDays(1);
                var fromTxt = dpFrom.SelectedDate?.ToString("yyyy-MM-dd") ?? "…";
                var toTxt   = dpTo.SelectedDate?.ToString("yyyy-MM-dd") ?? "…";
                txtChipRange.Text = $"{fromTxt} → {toTxt} ▾";
            }

            rangePanel.Visibility = Visibility.Collapsed;
            RenderAll();
        }

        private void BtnRangeClear_Click(object sender, MouseButtonEventArgs e)
        {
            _rangeFrom = null;
            _rangeTo   = null;
            dpFrom.SelectedDate  = null;
            dpTo.SelectedDate    = null;
            dpSince.SelectedDate = null;
            txtChipRange.Text = "Lifetime ▾";
            rangePanel.Visibility = Visibility.Collapsed;
            RenderAll();
        }

        // ---------------------------------------------------------------
        //  Phenomena aggregation: region -> category -> instance (name + last system)
        // ---------------------------------------------------------------
        private class PhenomenonInstance { public string Name = ""; public string System = ""; public DateTime LastSeen; }
        private class PhenomenonCategoryAgg { public int Total; public List<PhenomenonInstance> Instances = new(); }
        private class PhenomenaRegionAgg { public int Total; public Dictionary<string, PhenomenonCategoryAgg> Categories = new(StringComparer.OrdinalIgnoreCase); }

        private static readonly string[] PhenomenonCategoryOrder =
            { "Anomalies", "Mineral Formations", "Molluscs", "Plants", "Seed Pods", "Other" };

        private void RenderPhenomena()
        {
            phenomenaStack.Children.Clear();
            var all = _cache;
            var regions = new Dictionary<string, PhenomenaRegionAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in all)
            {
                var body = kvp.Value;
                if (body.Phenomena.Count == 0) continue;

                var region = GetRegionFor(body.System);

                foreach (var p in body.Phenomena.Where(p => InRange(p.LastSeen)))
                {
                    var outerKey = _phenomenaGroupByRegion ? region : p.Category;
                    var innerKey = _phenomenaGroupByRegion ? p.Category : region;

                    if (!regions.TryGetValue(outerKey, out var ragg)) { ragg = new PhenomenaRegionAgg(); regions[outerKey] = ragg; }
                    ragg.Total++;
                    if (!ragg.Categories.TryGetValue(innerKey, out var cagg)) { cagg = new PhenomenonCategoryAgg(); ragg.Categories[innerKey] = cagg; }
                    cagg.Total++;
                    cagg.Instances.Add(new PhenomenonInstance
                    {
                        Name = p.Name,
                        System = string.IsNullOrEmpty(body.System) ? kvp.Key : body.System,
                        LastSeen = p.LastSeen,
                    });
                }
            }

            if (regions.Count == 0)
            {
                phenomenaStack.Children.Add(EmptyMessage(HasActiveRange
                    ? "No Notable Stellar Phenomena finds in this range."
                    : "No Notable Stellar Phenomena finds yet — run Scan All from the Journals tab."));
                return;
            }

            // Categories are a fixed taxonomy (not sorted by count) wherever they
            // appear — as the outer grouping when grouped by Category, or the inner
            // grouping when grouped by Region (the default).
            var outerOrder = _phenomenaGroupByRegion
                ? regions.OrderByDescending(r => r.Value.Total)
                : PhenomenonCategoryOrder.Where(c => regions.ContainsKey(c))
                    .Select(c => new KeyValuePair<string, PhenomenaRegionAgg>(c, regions[c]));

            foreach (var rkvp in outerOrder)
            {
                var detail = new StackPanel { Margin = new Thickness(18, 2, 0, 6), Visibility = Visibility.Collapsed };

                var innerOrder = _phenomenaGroupByRegion
                    ? PhenomenonCategoryOrder.Where(c => rkvp.Value.Categories.ContainsKey(c))
                        .Select(c => new KeyValuePair<string, PhenomenonCategoryAgg>(c, rkvp.Value.Categories[c]))
                    : rkvp.Value.Categories.OrderByDescending(c => c.Value.Total);

                foreach (var ckvp in innerOrder)
                {
                    var instDetail = new StackPanel { Margin = new Thickness(18, 2, 0, 4), Visibility = Visibility.Collapsed };
                    foreach (var inst in ckvp.Value.Instances.OrderByDescending(i => i.LastSeen))
                        instDetail.Children.Add(BuildLeafRow(inst.Name, $"last: {inst.System}", 1, PhenomenaAccent));

                    detail.Children.Add(BuildExpandableRow(ckvp.Key, ckvp.Value.Total, PhenomenaAccent, instDetail, indent: false));
                    detail.Children.Add(instDetail);
                }
                phenomenaStack.Children.Add(BuildExpandableRow(rkvp.Key, rkvp.Value.Total, PhenomenaAccent, detail, indent: true));
                phenomenaStack.Children.Add(detail);
            }
        }

        // ---------------------------------------------------------------
        //  Shared row builders
        // ---------------------------------------------------------------
        private UIElement BuildExpandableRow(string name, int count, SolidColorBrush accent, StackPanel detail, bool indent,
                                              bool bold = false, SolidColorBrush? boldColor = null, Action? onFirstExpand = null)
        {
            var chevron = new TextBlock { Text = "▸", Foreground = DimLabel, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };

            var header = new Border
            {
                Background = RowBg,
                BorderBrush = RowBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, indent ? 8 : 2, 0, 0),
                Cursor = Cursors.Hand,
            };

            var row = new DockPanel();
            var countText = new TextBlock
            {
                Text = count.ToString("N0"), Foreground = accent, FontWeight = FontWeights.Bold,
                FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(countText, Dock.Right);

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
            namePanel.Children.Add(chevron);
            namePanel.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = bold ? (boldColor ?? Color(0xcf, 0xe8, 0xe8)) : Color(0xcf, 0xe8, 0xe8),
                FontSize = 13,
                FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
            });

            row.Children.Add(countText);
            row.Children.Add(namePanel);
            header.Child = row;

            // Detail content is potentially thousands of rows (e.g. every individual
            // world of a common planet class) — building all of that eagerly, even
            // while collapsed, is what caused the "Not Responding" freeze on every
            // re-render (filter toggles, Scan All). onFirstExpand defers that work
            // until the row is actually clicked open, and only runs once.
            bool expanded = false;
            header.MouseLeftButtonUp += (s, e) =>
            {
                if (!expanded && onFirstExpand != null) { onFirstExpand(); expanded = true; }
                bool nowOpen = detail.Visibility != Visibility.Visible;
                detail.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = nowOpen ? "▾" : "▸";
            };
            header.MouseEnter += (s, e) => header.Background = RowHoverBg;
            header.MouseLeave += (s, e) => header.Background = RowBg;

            return header;
        }

        // Populates a detail panel with up to `cap` instance leaf rows — even
        // deferred to on-expand, a single class can still hold thousands of
        // instances (e.g. "Icy body"), so this also caps what actually gets built.
        private void PopulateInstanceRows(StackPanel detail, List<(string system, DateTime lastSeen)> instances,
                                           SolidColorBrush accent, int cap = 200)
        {
            foreach (var (system, _) in instances.Take(cap))
                detail.Children.Add(BuildSoloLeafRow($"last: {system}", accent));
            if (instances.Count > cap)
                detail.Children.Add(SmallNote($"+ {instances.Count - cap:N0} more not shown", accent));
        }

        private UIElement BuildLeafRow(string name, string subText, int count, SolidColorBrush accent)
        {
            var border = new Border { BorderBrush = RowBorder, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            var row = new DockPanel();

            var countText = new TextBlock
            {
                Text = count.ToString("N0"), Foreground = Darken(accent), FontWeight = FontWeights.Bold, FontSize = 11.5,
            };
            DockPanel.SetDock(countText, Dock.Right);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = name, Foreground = Color(0x9f, 0xc9, 0xc9), FontSize = 11.5 });
            stack.Children.Add(new TextBlock { Text = subText, Foreground = DimLabel, FontSize = 10, Margin = new Thickness(0, 1, 0, 0) });

            row.Children.Add(countText);
            row.Children.Add(stack);
            border.Child = row;
            return border;
        }

        // A "solo" leaf row is the only line on its row (First Discovery / Already
        // Catalogued / First Footfall sub-groups) — no repeated type name above it,
        // just the last-seen system, rendered at normal name weight/color.
        private UIElement BuildSoloLeafRow(string text, SolidColorBrush accent)
        {
            var border = new Border { BorderBrush = RowBorder, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            border.Child = new TextBlock { Text = text, Foreground = Color(0x9f, 0xc9, 0xc9), FontSize = 11.5 };
            return border;
        }

        private UIElement SmallNote(string text, SolidColorBrush accent) => new TextBlock
        {
            Text = text, Foreground = Darken(accent), FontSize = 10, Margin = new Thickness(8, 2, 0, 4),
        };

        private UIElement EmptyMessage(string text) => new TextBlock
        {
            Text = text, Foreground = DimLabel, FontSize = 12, Margin = new Thickness(0, 20, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        private static SolidColorBrush Color(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));
        private static SolidColorBrush Darken(SolidColorBrush b)
        {
            var c = b.Color;
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                (byte)(c.R * 0.65), (byte)(c.G * 0.65), (byte)(c.B * 0.65)));
        }

        // ---------------------------------------------------------------
        //  Journals tab
        // ---------------------------------------------------------------
        private bool _scanRunning;

        private async void BtnScanAll_Click(object sender, RoutedEventArgs e)
        {
            if (_scanRunning) return;
            _scanRunning = true;
            btnScanAll.IsEnabled = false;
            btnClearLibrary.IsEnabled = false;
            btnScanAll.Content = "Scanning...";
            txtJournalsStatus.Text = "Scanning journal history — this can take a moment...";

            try
            {
                var journalDir = EliteWatcherService.GetJournalDirectory();
                // Both the journal scan AND the cache reload are expensive CPU work —
                // run both off the UI thread so the window stays responsive, and only
                // deserialize the cache once here rather than once per tab.
                var (summary, freshCache, freshRegions) = await Task.Run(() =>
                {
                    var s = JournalHistoryScanner.ScanAll(journalDir);
                    return (s, ScanCache.LoadAll(), RegionCache.LoadAll());
                });
                _cache = freshCache;
                _regions = freshRegions;

                txtJournalsStatus.Text =
                    $"Last full scan: {DateTime.Now:yyyy-MM-dd HH:mm} · {summary.FilesScanned} journal files · " +
                    $"{summary.BodiesTouched} bodies · {summary.BioScansFound} bio scans, {summary.GeoSitesFound} geo sites, " +
                    $"{summary.StarsFound} stars, {summary.WorldsFound} worlds, {summary.PhenomenaFound} phenomena, " +
                    $"{summary.RegionsFound} regions indexed · took {summary.Elapsed.TotalSeconds:N1}s";

                RenderAll();
            }
            catch (Exception ex)
            {
                Log.Write($"ScanLogWindow.BtnScanAll_Click error: {ex.Message}");
                txtJournalsStatus.Text = "Scan failed — see EliteBioRadar.log for details.";
            }
            finally
            {
                btnScanAll.IsEnabled = true;
                btnClearLibrary.IsEnabled = true;
                btnScanAll.Content = "Scan All";
                _scanRunning = false;
            }
        }

        private void BtnClearLibrary_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this,
                "This clears the entire scan library (every region, genus, geology site, star, world, and phenomenon). " +
                "Your Elite Dangerous journal files are not affected — you can Scan All again afterward.\n\nContinue?",
                "Clear Library", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            ScanCache.ClearAll();
            RegionCache.ClearAll();
            _cache = new Dictionary<string, LoadedBodyData>();
            _regions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            txtJournalsStatus.Text = "Library cleared.";
            RenderAll();
        }
    }
}
