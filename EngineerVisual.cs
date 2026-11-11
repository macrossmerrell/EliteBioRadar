using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // An engineer's work: a scan line sweeps the outline of the module (or of the engineer), a stamp thumps down across it, the stat bars fill and a plate names it. Engineering orange.
    // Detail = "CRAFT|blueprint|engineer|grade|effect|label:percent:fill;..." (EngineerCraft: ENGINEERED, the stats are the module's biggest changes),
    //          "UNLOCK|engineer|rank|progress" (EngineerProgress, first unlocked: UNLOCKED) or "RANKUP|engineer|rank|progress" (EngineerProgress, a higher rank: RANK UP).
    public sealed class EngineerVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.5, SceneW = 240, SceneH = 150;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Orange = Color.FromRgb(0xff, 0x96, 0x32);
        private static readonly Brush OrangeBrush = Frozen(Orange), WarmBrush = Frozen(Color.FromRgb(0xff, 0xe8, 0xd0)), MutedBrush = Frozen(Color.FromRgb(0xc9, 0xa2, 0x7a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 20, 10, 4)), StampBack = Frozen(Color.FromArgb(184, 20, 10, 4)), TrackBrush = Frozen(Color.FromArgb(38, Orange.R, Orange.G, Orange.B));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Orange.R, Orange.G, Orange.B), 1), StampEdge = FrozenPen(Orange, 1.8);
        private static readonly Brush ScanTail = MakeScanTail();
        private static readonly Geometry ModuleShape = MakeModule(), PersonShape = MakePerson();

        private static Brush MakeScanTail()
        {
            var b = new LinearGradientBrush(Color.FromArgb(0, Orange.R, Orange.G, Orange.B), Color.FromArgb(140, Orange.R, Orange.G, Orange.B), 0);
            b.Freeze(); return b;
        }
        private static Geometry MakeModule()
        {
            var g = new GeometryGroup();
            g.Children.Add(new RectangleGeometry(new Rect(0, 0, 60, 30), 4, 4));
            g.Children.Add(new RectangleGeometry(new Rect(-8, 8, 8, 14)));
            g.Children.Add(new RectangleGeometry(new Rect(60, 8, 8, 14)));
            g.Children.Add(new LineGeometry(new Point(14, 0), new Point(14, 30)));
            g.Children.Add(new LineGeometry(new Point(46, 0), new Point(46, 30)));
            g.Children.Add(new LineGeometry(new Point(14, 15), new Point(46, 15)));
            g.Freeze(); return g;
        }
        private static Geometry MakePerson()
        {
            var g = new GeometryGroup();
            g.Children.Add(new EllipseGeometry(new Point(0, 0), 11, 11));
            g.Children.Add(Geometry.Parse("M -24,34 Q -22,16 0,15 Q 22,16 24,34"));
            g.Freeze(); return g;
        }

        private bool _craft;
        private string _stamp = "ENGINEERED", _l1 = "", _l2 = "", _l3 = "";
        private readonly List<(string label, string value, double fill)> _rows = new();

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "CRAFT";
            _rows.Clear();
            _craft = kind == "CRAFT";
            if (_craft)
            {
                _stamp = "ENGINEERED";
                _l1 = p.Length > 1 && p[1].Trim().Length > 0 ? p[1].Trim() : "Module engineered";
                _l2 = p.Length > 2 ? p[2].Trim() : "";
                string grade = p.Length > 3 ? p[3].Trim() : "", effect = p.Length > 4 ? p[4].Trim() : "";
                _l3 = (grade.Length > 0 ? "GRADE " + grade : "") + (effect.Length > 0 ? (grade.Length > 0 ? "  -  " : "") + effect : "");
                if (p.Length > 5)
                    foreach (var part in p[5].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var f = part.Split(':');
                        if (f.Length < 3 || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fill)) continue;
                        _rows.Add((f[0].Trim().ToUpperInvariant(), f[1].Trim(), Math.Clamp(fill, 0.05, 1)));
                        if (_rows.Count == 3) break;
                    }
            }
            else
            {
                _stamp = kind == "RANKUP" ? "RANK UP" : "UNLOCKED";
                _l1 = p.Length > 1 ? p[1].Trim() : "Engineer";
                int rank = p.Length > 2 && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Clamp(r, 0, 5) : 0;
                int pct = p.Length > 3 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? Math.Clamp(q, 0, 100) : 0;
                _l2 = rank > 0 ? "RANK " + rank.ToString(CultureInfo.InvariantCulture) + " OF 5" : "";
                _l3 = "";
                if (rank > 0) _rows.Add(("RANK", rank + "/5", rank / 5.0));
                _rows.Add(("PROGRESS", pct + "%", Math.Max(0.03, pct / 100.0)));
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        // A shape drawn on as if by a pen: the first `p` of each figure's length is visible.
        private static Pen DrawOn(double p, double thickness)
        {
            const double L = 300;
            var pen = new Pen(OrangeBrush, thickness) { DashStyle = new DashStyle(new[] { L / thickness, L / thickness }, L * (1 - p) / thickness) };
            return pen;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.25) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The outline, drawn on, and the scan line sweeping across it.
            double draw = Phase(t, 0.2, 1.2);
            dc.PushOpacity(0.9);
            dc.PushTransform(new TranslateTransform(_craft ? 32 : 64, _craft ? 30 : 36));
            dc.DrawGeometry(null, DrawOn(draw, 1.3), _craft ? ModuleShape : PersonShape);
            dc.Pop(); dc.Pop();
            if (draw > 0 && draw < 1)
            {
                double x = 24 + draw * 100;
                dc.DrawRectangle(ScanTail, null, new Rect(x - 14, 16, 14, 56));
                dc.DrawRectangle(OrangeBrush, null, new Rect(x - 0.5, 16, 1.5, 56));
            }

            // The stat bars, after the stamp has landed.
            const double rowStart = 1.9;
            for (int i = 0; i < _rows.Count; i++)
            {
                var (label, value, fill) = _rows[i];
                double y = 14 + i * 22, tl = rowStart + i * 0.35;
                DrawText(dc, label, MonoFace, Fit(label, 8, 52), MutedBrush, 136, y, Phase(t, tl - 0.1, 0.3), TextAlignment.Left);
                dc.DrawRectangle(TrackBrush, null, new Rect(136, y + 11, 74, 4));
                dc.DrawRectangle(OrangeBrush, null, new Rect(136, y + 11, 74 * fill * EaseOut(Phase(t, tl, 0.7)), 4));
                DrawText(dc, value, MonoFace, Fit(value, 8, 30), OrangeBrush, 210, y, Phase(t, tl + 0.3, 0.3), TextAlignment.Right);
            }

            // The stamp thumping down across the outline.
            double sa = Phase(t, 1.4, 0.12);
            if (sa > 0)
            {
                double sc = 1 + 0.9 * (1 - EaseOut(Phase(t, 1.4, 0.2))), bw = _stamp.Length * 8.7 + 12;
                dc.PushOpacity(sa);
                dc.PushTransform(new TranslateTransform(64, 52));
                dc.PushTransform(new RotateTransform(-11.5));
                dc.PushTransform(new ScaleTransform(sc, sc));
                dc.DrawRectangle(StampBack, StampEdge, new Rect(-bw / 2, -11.2, bw, 22.4));
                DrawSpaced(dc, _stamp, MonoFace, 14, OrangeBrush, 0, -8.5, 0.07, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            // The plate, last.
            double pa = Phase(t, 2.9, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                // Tall enough for the lines it has: a name only, a name and a line under it, or all three.
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, _l3.Length > 0 ? 45 : _l2.Length > 0 ? 34 : 22), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _l1, MonoFace, Fit(_l1, 12, 180), OrangeBrush, SceneW / 2, 106, 0.1, pa);
                if (_l2.Length > 0) DrawSpaced(dc, _l2, MonoFace, Fit(_l2, 10, 180), WarmBrush, SceneW / 2, 120, 0.08, Phase(t, 3.1, 0.4));
                if (_l3.Length > 0) DrawSpaced(dc, _l3, MonoFace, Fit(_l3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, 3.3, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
