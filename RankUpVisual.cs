using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A promotion (the journal's Promotion event): a ladder of rank names scrolls up one step, the new rank lights gold inside a frame that pulses, the old one dims; the plate says "RANK UP" over
    // the category and the new rank, and the step it came from. Detail = "Category|new rank number" (Combat, Trade, Explore, Soldier, Exobiologist, CQC, Federation, Empire).
    public sealed class RankUpVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 5.1, CentreY = 52.5, RowGap = 17;

        // Rank names by number (the Promotion event only carries the number). The game's ranks above the eighth are "Elite I" to "Elite V".
        private static readonly string[] Elite = { "Elite", "Elite I", "Elite II", "Elite III", "Elite IV", "Elite V" };
        private static readonly Dictionary<string, (string title, string[] ranks)> Tables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Combat"] = ("COMBAT", Ranks("Harmless", "Mostly Harmless", "Novice", "Competent", "Expert", "Master", "Dangerous", "Deadly")),
            ["Trade"] = ("TRADE", Ranks("Penniless", "Mostly Penniless", "Peddler", "Dealer", "Merchant", "Broker", "Entrepreneur", "Tycoon")),
            ["Explore"] = ("EXPLORATION", Ranks("Aimless", "Mostly Aimless", "Scout", "Surveyor", "Trailblazer", "Pathfinder", "Ranger", "Pioneer")),
            ["Soldier"] = ("MERCENARY", Ranks("Defenceless", "Mostly Defenceless", "Rookie", "Soldier", "Gunslinger", "Warrior", "Gladiator", "Deadeye")),
            ["Exobiologist"] = ("EXOBIOLOGIST", Ranks("Directionless", "Mostly Directionless", "Compiler", "Collector", "Cataloguer", "Taxonomist", "Ecologist", "Geneticist")),
            ["CQC"] = ("CQC", Ranks("Helpless", "Mostly Helpless", "Amateur", "Semi Professional", "Professional", "Champion", "Hero", "Legend")),
            ["Federation"] = ("FEDERAL NAVY", new[] { "None", "Recruit", "Cadet", "Midshipman", "Petty Officer", "Chief Petty Officer", "Warrant Officer", "Ensign", "Lieutenant", "Lieutenant Commander", "Post Commander", "Post Captain", "Rear Admiral", "Vice Admiral", "Admiral" }),
            ["Empire"] = ("IMPERIAL NAVY", new[] { "None", "Outsider", "Serf", "Master", "Squire", "Knight", "Lord", "Baron", "Viscount", "Count", "Earl", "Marquis", "Duke", "Prince", "King" }),
        };
        private static string[] Ranks(params string[] first8)
        {
            var all = new string[first8.Length + Elite.Length];
            first8.CopyTo(all, 0); Elite.CopyTo(all, first8.Length);
            return all;
        }

        private static readonly Color Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush GoldBrush = Frozen(Gold), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), DimBrush = Frozen(Color.FromRgb(0x9f, 0xb0, 0xb6));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Gold.R, Gold.G, Gold.B), 1);
        private static readonly Pen[] FramePen = AlphaPens(Gold, 1), PulsePen = AlphaPens(Gold, 1.5);

        private string _title = "", _newName = "", _oldName = "";
        private int _old, _new;
        private readonly Dictionary<int, FormattedText> _plain = new(), _gold = new();
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        // One set of transforms per ladder row: the drawing keeps a reference to each, so rows sharing one would all be drawn where the last row was put.
        private readonly ScaleTransform[] _rowScale = Enumerable.Range(0, 6).Select(_ => new ScaleTransform()).ToArray();
        private readonly TranslateTransform[] _rowAt = Enumerable.Range(0, 6).Select(_ => new TranslateTransform()).ToArray(), _rowBack = Enumerable.Range(0, 6).Select(_ => new TranslateTransform()).ToArray();
        private readonly TranslateTransform _shift = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string cat = p.Length > 0 ? p[0].Trim() : "";
            int idx = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(1, n) : 1;
            string[] names = Tables.TryGetValue(cat, out var tab) ? tab.ranks : Array.Empty<string>();
            _title = Tables.TryGetValue(cat, out var t2) ? t2.title : cat.ToUpperInvariant();
            _new = idx; _old = idx - 1;
            string Name(int i) => i >= 0 && i < names.Length ? names[i] : "Rank " + i.ToString(CultureInfo.InvariantCulture);
            _newName = Name(_new); _oldName = Name(_old);

            // One formatted text per visible row, built once (the ladder shows the old rank, the new one, two below it and two above).
            _plain.Clear(); _gold.Clear();
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var bold = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            for (int j = _new - 3; j <= _new + 2; j++)
            {
                if (j < 0 || (names.Length > 0 && j >= names.Length)) continue;
                string label = Name(j).ToUpperInvariant();
                _plain[j] = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, 12, j == _old ? NameBrush : DimBrush, dpi);
                if (j == _new) _gold[j] = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold, 12, GoldBrush, dpi);
            }
        }

        private void DrawRow(DrawingContext dc, int slot, FormattedText ft, double y, double scale, double opacity)
        {
            if (opacity < 0.02) return;
            _rowAt[slot].X = SceneW / 2; _rowAt[slot].Y = y; _rowScale[slot].ScaleX = _rowScale[slot].ScaleY = scale; _rowBack[slot].X = -ft.WidthIncludingTrailingWhitespace / 2; _rowBack[slot].Y = -ft.Height / 2;
            dc.PushOpacity(Math.Min(1, opacity)); dc.PushTransform(_rowAt[slot]); dc.PushTransform(_rowScale[slot]); dc.PushTransform(_rowBack[slot]);
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double sc = EaseOut(Phase(t, 1.2, 1.0)), cen = _old + sc, lit = Phase(t, 1.6, 0.7);
            foreach (var kv in _plain)
            {
                int j = kv.Key;
                double d = Math.Abs(j - cen), y = CentreY + (j - cen) * RowGap, size = (10 + 4 * Clamp01(1 - d * 1.2)) / 12.0, fall = Math.Clamp(1 - d * 0.35, 0.1, 1);
                int slot = j - (_new - 3);   // 0..5
                if (j == _new) { DrawRow(dc, slot, kv.Value, y, size, fall * (1 - lit)); DrawRow(dc, slot, _gold[j], y, size, 0.35 * (1 - lit) + lit); }
                else DrawRow(dc, slot, kv.Value, y, size, j == _old ? fall * (1 - 0.6 * sc) : fall * 0.7);
            }

            double fr = Phase(t, 1.4, 0.4);
            if (fr > 0) dc.DrawRectangle(null, FramePen[Math.Clamp((int)Math.Round(fr * 0.8 * 8), 0, 8)], new Rect(60, CentreY - RowGap / 2, 120, RowGap));
            double pf = Phase(t, 2.0, 0.6);
            if (pf > 0 && pf < 1)
            {
                double g = pf * 8;
                dc.DrawRectangle(null, PulsePen[Math.Clamp((int)Math.Round((1 - pf) * 8), 0, 8)], new Rect(60 - g, CentreY - RowGap / 2 - g, 120 + g * 2, RowGap + g * 2));
            }

            double plateAt = 2.8, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                string line2 = _title + "  -  " + _newName;
                DrawSpaced(dc, "RANK UP", MonoFace, 12, GoldBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, line2, MonoFace, Math.Min(10, 190 / (Math.Max(1, line2.Length) * 0.66)), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string line3 = _oldName + "  >  " + _newName;
                DrawSpaced(dc, line3, MonoFace, Math.Min(8, 190 / (Math.Max(1, line3.Length) * 0.66)), MutedBrush, SceneW / 2, 134, 0.1, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
