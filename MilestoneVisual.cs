using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A lifetime milestone (a round number of jumps, light-years, hours played, bounties or billions of credits): an engraved metal plate with rivets scales in, the number is cut into it,
    // and a shine sweeps across.
    //   LIVE gold, one milestone the moment it is passed    RECAP cyan, what was passed since the last session (one to three, listed)
    // Detail = "LIVE or RECAP|short label~full label~value~step;..." (a milestone per item).
    public sealed class MilestoneVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private static readonly Color GoldC = Color.FromRgb(255, 216, 100), CyanC = Color.FromRgb(85, 242, 255);
        private static readonly Brush GoldBrush = Frozen(GoldC), CyanBrush = Frozen(CyanC);
        private static readonly Pen GoldEdge = FrozenPen(GoldC, 1.6), CyanEdge = FrozenPen(CyanC, 1.6), GoldPlateEdge = FrozenPen(Color.FromArgb(153, GoldC.R, GoldC.G, GoldC.B), 1), CyanPlateEdge = FrozenPen(Color.FromArgb(153, CyanC.R, CyanC.G, CyanC.B), 1);
        private static readonly Brush Metal = Frozen(Color.FromRgb(0x3a, 0x44, 0x48)), Rivet = Frozen(Color.FromRgb(0x8a, 0xa0, 0xa8)), LabelBrush = Frozen(Color.FromRgb(0xcf, 0xe8, 0xe8)), Shine = Frozen(Color.FromArgb(70, 255, 255, 255));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));

        private sealed class Item { public string Short = "", Full = ""; public double Value, Step; }
        private readonly List<Item> _items = new();
        private bool _recap;
        private string _title = "", _name = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        private static string N(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _recap = parts[0].Trim().ToUpperInvariant() == "RECAP";
            _items.Clear();
            if (parts.Length > 1)
                foreach (var raw in parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var q = raw.Split('~');
                    if (q.Length < 4) continue;
                    var it = new Item { Short = q[0].Trim().ToUpperInvariant(), Full = q[1].Trim().ToUpperInvariant() };
                    double.TryParse(q[2], NumberStyles.Float, CultureInfo.InvariantCulture, out it.Value);
                    double.TryParse(q[3], NumberStyles.Float, CultureInfo.InvariantCulture, out it.Step);
                    _items.Add(it);
                    if (_items.Count == 3) break;
                }
            if (_items.Count == 0) _items.Add(new Item { Short = "JUMPS", Full = "HYPERSPACE JUMPS", Value = 1000, Step = 1000 });
            if (_items.Count == 1)
            {
                var it = _items[0];
                _title = "MILESTONE"; _name = $"{N(it.Value)} {it.Full}";
                _sub = _recap ? "SINCE LAST SESSION" : $"NEXT AT {N(it.Value + it.Step)}";
            }
            else
            {
                _title = "MILESTONES PASSED"; _name = $"{_items.Count} SINCE LAST SESSION";
                _sub = string.Join("  ", _items.ConvertAll(i => i.Short));
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var solid = _recap ? CyanBrush : GoldBrush;
            var edge = _recap ? CyanEdge : GoldEdge;
            var plateEdge = _recap ? CyanPlateEdge : GoldPlateEdge;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            bool big = _items.Count == 1;
            double pw = 152, ph = big ? 64 : 74;
            double s = EaseOut(Phase(t, 0.1, 0.5));
            if (s > 0.01)
            {
                double sc = 0.6 + 0.4 * s;
                dc.PushOpacity(s);
                dc.PushTransform(new TranslateTransform(120, 50));
                dc.PushTransform(new ScaleTransform(sc, sc));
                var rect = new Rect(-pw / 2, -ph / 2, pw, ph);
                dc.DrawRectangle(Metal, null, rect);
                dc.DrawRectangle(null, edge, new Rect(-pw / 2 + 3, -ph / 2 + 3, pw - 6, ph - 6));
                foreach (var (sx, sy) in new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) })
                    dc.DrawEllipse(Rivet, null, new Point(sx * (pw / 2 - 7), sy * (ph / 2 - 7)), 1.8, 1.8);
                for (int i = 0; i < _items.Count; i++)
                {
                    var it = _items[i];
                    double p = EaseOut(Phase(t, 0.5 + i * 0.4, 0.5));
                    if (p <= 0) continue;
                    if (big)
                    {
                        string num = N(it.Value);
                        DrawText(dc, num, MonoFace, Math.Min(22, 120 / (num.Length * 0.62)), solid, 0, -ph / 2 + 12, p, TextAlignment.Center);
                        DrawText(dc, it.Full, MonoFace, Fit(it.Full, 7, 120), LabelBrush, 0, ph / 2 - 19, p, TextAlignment.Center);
                    }
                    else
                    {
                        double y = -ph / 2 + 10 + i * 20;
                        DrawText(dc, N(it.Value), MonoFace, 11, solid, -pw / 2 + 12, y, p, TextAlignment.Left);
                        DrawText(dc, it.Full, MonoFace, Fit(it.Full, 6.5, 62), LabelBrush, pw / 2 - 12, y + 3, p, TextAlignment.Right);
                    }
                }
                // The shine: a pale band sweeping across the plate.
                double sp = Phase(t, 1.2, 1.2);
                if (sp > 0 && sp < 1)
                {
                    dc.PushClip(ClipOf(rect));
                    dc.PushOpacity(0.5 * (1 - Math.Abs(sp - 0.5) * 2));
                    dc.DrawRectangle(Shine, null, new Rect(-pw / 2 - 10 + sp * (pw + 20), -ph / 2, 8, ph));
                    dc.Pop(); dc.Pop();
                }
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, plateEdge, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
