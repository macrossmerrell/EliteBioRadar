using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The station's ship and module storage listings (the journal's StoredShips / StoredModules): an airport-style departures board whose rows flip in one after another - the item,
    // where it is (HERE, or the system it is in) and what a transfer costs - with the totals on the plate. Ships here and engineered modules are picked out in colour.
    // Detail = "KIND|station|system|figure 1|figure 2|value in credits|more rows|name~location~transfer cost~here 1/0~engineered 1/0;..." with KIND SHIPS (figure 1 = ships here, 2 = elsewhere)
    // or MODS (1 = modules stored, 2 = engineered).
    public sealed class StorageVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.9;
        private static readonly Color Cyan = Color.FromRgb(127, 224, 255), Amber = Color.FromRgb(255, 176, 80);
        private static readonly Brush CyanB = Frozen(Cyan), AmberB = Frozen(Amber), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), BoardBack = Frozen(Color.FromArgb(217, 8, 13, 13)), Stripe = Frozen(Color.FromArgb(14, 255, 255, 255));
        private static readonly Brush RowText = Frozen(Color.FromRgb(0xcf, 0xe8, 0xe8)), Dim = Frozen(Color.FromRgb(0x6f, 0xa0, 0xa0)), Place = Frozen(Color.FromRgb(0x99, 0xaa, 0xbb));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), BoardEdge = FrozenPen(Color.FromRgb(0x4a, 0x6a, 0x72), 1);

        private sealed record Row(string Name, string Place, double Cost, bool Here, bool Eng);
        private readonly List<Row> _rows = new();
        private bool _mods;
        private int _more;
        private string _title = "HANGAR REPORT", _name = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            var inv = CultureInfo.InvariantCulture;
            _mods = P(0).Equals("MODS", StringComparison.OrdinalIgnoreCase);
            string station = P(1).ToUpperInvariant(), system = P(2).ToUpperInvariant();
            int.TryParse(P(3), NumberStyles.Integer, inv, out int f1); int.TryParse(P(4), NumberStyles.Integer, inv, out int f2);
            double.TryParse(P(5), NumberStyles.Float, inv, out double value); int.TryParse(P(6), NumberStyles.Integer, inv, out _more);
            _name = (station + (system.Length > 0 && system != station ? "  " + system : "")).Trim();
            string worth = Money(value, "CR");
            _title = _mods ? "MODULE STORAGE" : "HANGAR REPORT";
            _sub = _mods
                ? $"{f1} MODULE{(f1 == 1 ? "" : "S")}  {f2} ENGINEERED  {worth}"
                : $"{f1} HERE  {f2} ELSEWHERE  FLEET {worth}";
            _rows.Clear();
            foreach (var item in P(7).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var c = item.Split('~');
                if (c.Length < 3) continue;
                double.TryParse(c[2], NumberStyles.Float, inv, out double cost);
                _rows.Add(new Row(c[0].ToUpperInvariant(), c[1].ToUpperInvariant(), cost, c.Length > 3 && c[3] == "1", c.Length > 4 && c[4] == "1"));
                if (_rows.Count >= 7) break;
            }
        }

        // 1,264 -> 1.3K, 482,000 -> 482K, 1,250,000 -> 1.3M, 408,830,314 -> 408.8M
        private static string Money(double v, string unit)
        {
            string s = v >= 1e9 ? (v / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + "B"
                     : v >= 1e6 ? (v / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M"
                     : v >= 1e4 ? Math.Round(v / 1e3).ToString("0", CultureInfo.InvariantCulture) + "K"
                     : v >= 1e3 ? (v / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "K"
                     : Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
            return s + " " + unit;
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            double e = EaseOut(Phase(t, 0.1, 0.3));
            dc.PushOpacity(e);
            const double bx = 8, by = 4, bw = 224, bh = 94, left = 15, mid = 98, right = 225;
            dc.DrawRectangle(BoardBack, BoardEdge, new Rect(bx, by, bw, bh));
            DrawText(dc, _mods ? "MODULE" : "SHIP", MonoFace, 6, Dim, left, by + 3, 1, TextAlignment.Left);
            DrawText(dc, "LOCATION", MonoFace, 6, Dim, mid, by + 3, 1, TextAlignment.Left);
            DrawText(dc, "TRANSFER", MonoFace, 6, Dim, right, by + 3, 1, TextAlignment.Right);

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                double flip = EaseOut(Phase(t, 0.3 + i * 0.2, 0.3));
                if (flip <= 0.02) continue;
                double y = by + 13 + i * 10.5;
                // The row flips in like a split-flap board: it opens from its centre line.
                dc.PushTransform(new TranslateTransform(0, y + 4.5)); dc.PushTransform(new ScaleTransform(1, flip));
                if (i % 2 == 0) dc.DrawRectangle(Stripe, null, new Rect(bx + 1, -4.5, bw - 2, 10));
                Brush main = row.Here ? CyanB : row.Eng ? AmberB : RowText;
                DrawText(dc, row.Name, MonoFace, Fit(row.Name, 6.5, 78), main, left, -4.5, 1, TextAlignment.Left);
                string place = row.Here ? "HERE" : row.Place;
                DrawText(dc, place, MonoFace, Fit(place, 6.5, 80), row.Here ? CyanB : Place, mid, -4.5, 1, TextAlignment.Left);
                DrawText(dc, row.Here || row.Cost <= 0 ? "-" : Money(row.Cost, "CR"), MonoFace, 6.5, Gold, right, -4.5, 1, TextAlignment.Right);
                dc.Pop(); dc.Pop();
            }
            if (_more > 0 && _rows.Count > 0)
                DrawText(dc, "+" + _more + " MORE", MonoFace, 6, Dim, left, by + 13 + _rows.Count * 10.5, Phase(t, 0.3 + _rows.Count * 0.2 + 0.2, 0.3), TextAlignment.Left);
            dc.Pop();

            double plateAt = 3.5, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(10, 103, 220, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, 12, CyanB, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 205), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 205), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
