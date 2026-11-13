using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Search and rescue handed in at a station (the journal's SearchAndRescue): a manifest of the items ticks off one by one, then a tilted stamp lands. Occupied escape pods are RESCUED (green);
    // everything else (black boxes, wreckage, personal effects, empty pods) is RECOVERED (amber). The plate gives the count and item and the reward counting up.
    // Detail = "RESCUE|count|item|reward" or "RECOVER|count|item|reward".
    public sealed class SearchRescueVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, ListX = 28, ListY = 14, RowH = 15;
        private static readonly Color Green = Color.FromRgb(95, 227, 176), Amber = Color.FromRgb(255, 190, 90);
        private static readonly Brush GreenBrush = Frozen(Green), AmberBrush = Frozen(Amber), GoldBrush = Frozen(Color.FromRgb(255, 216, 144)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)),
            DimBrush = Frozen(Color.FromArgb(128, 255, 255, 255)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen GreenPen = FrozenPen(Green, 2), AmberPen = FrozenPen(Amber, 2), GreenEdge = FrozenPen(Color.FromArgb(153, 95, 227, 176), 1), AmberEdge = FrozenPen(Color.FromArgb(153, 255, 190, 90), 1);

        private bool _rescue;
        private int _count;
        private string _item = "";
        private long _reward;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _rescue = p.Length > 0 && p[0].Trim().ToUpperInvariant() == "RESCUE";
            _count = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(1, n) : 1;
            _item = p.Length > 2 ? p[2].Trim().ToUpperInvariant() : "ITEM";
            _reward = p.Length > 3 && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Max(0, r) : 0;
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            Brush col = _rescue ? GreenBrush : AmberBrush; Pen tickPen = _rescue ? GreenPen : AmberPen, edge = _rescue ? GreenEdge : AmberEdge;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The manifest: one numbered line per item (the first five), each ticked in turn.
            int rows = Math.Min(_count, 5);
            string single = _item.EndsWith("S") && _count == 1 ? _item[..^1] : _item;
            for (int i = 0; i < rows; i++)
            {
                double q = Phase(t, 0.2 + i * 0.35, 0.3), y = ListY + i * RowH;
                if (q <= 0) continue;
                dc.PushOpacity(q);
                DrawText(dc, (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + single, MonoFace, Fit(single, 9, 120), NameBrush, ListX, y, 1, TextAlignment.Left);
                double c = Phase(t, 0.5 + i * 0.35, 0.25);
                if (c > 0)
                {
                    var tx = ListX + 140;
                    dc.DrawLine(tickPen, new Point(tx, y + 6), new Point(tx + 4 * Math.Min(1, c * 2), y + 6 + 5 * Math.Min(1, c * 2)));
                    if (c > 0.5) dc.DrawLine(tickPen, new Point(tx + 4, y + 11), new Point(tx + 4 + 8 * (c - 0.5) * 2, y + 11 - 10 * (c - 0.5) * 2));
                }
                dc.Pop();
            }
            if (_count > rows) DrawText(dc, "+" + (_count - rows).ToString(CultureInfo.InvariantCulture) + " MORE", MonoFace, 8, DimBrush, ListX, ListY + rows * RowH, Phase(t, 0.2 + rows * 0.35, 0.3), TextAlignment.Left);

            // The stamp.
            string stamp = _rescue ? "RESCUED" : "RECOVERED";
            double s = Phase(t, 1.9, 0.25);
            if (s > 0)
            {
                double sc = 1 + (1 - EaseOut(s)) * 0.8, wBox = stamp.Length * 8.5 + 14;
                dc.PushTransform(new TranslateTransform(172, 52)); dc.PushTransform(new RotateTransform(-11.5)); dc.PushTransform(new ScaleTransform(sc, sc));
                dc.PushOpacity(EaseOut(s));
                dc.DrawRectangle(null, tickPen, new Rect(-wBox / 2, -10, wBox, 20));
                DrawSpaced(dc, stamp, MonoFace, 11, col, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            string item = _count.ToString(CultureInfo.InvariantCulture) + " " + (_count > 1 && !_item.EndsWith("S") ? _item + "S" : _item);
            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool hasReward = _reward > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, edge, new Rect(20, 103, 200, hasReward ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "SEARCH AND RESCUE", MonoFace, 12, col, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, item, MonoFace, Fit(item, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (hasReward)
                {
                    long shown = (long)Math.Round(_reward * EaseOut(Phase(t, plateAt + 0.2, 0.9)));
                    DrawSpaced(dc, "REWARD +" + shown.ToString("N0", CultureInfo.InvariantCulture) + " CR", MonoFace, 8, GoldBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
                }
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
