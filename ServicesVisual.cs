using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Station services (RefuelAll / RefuelPartial, RepairAll / Repair, BuyAmmo, RestockVehicle, BuyDrones): one ring gauge per service refills to full,
    // the service name and amount sit under each ring, and the chip at the bottom carries the title and the credit total counting up.
    //   fuel cyan    repairs green    ammo / vehicle / drones amber
    // Detail = "key|LABEL|amount text|cost;key|LABEL|amount text|cost;..." with key f, r or a (the icon and colour), at most four services.
    public sealed class ServicesVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Arc, Edge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Edge = FrozenPen(c, 1.6);
                var p = new Pen(Frozen(c), 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                p.Freeze(); Arc = p;
            }
        }
        private static readonly Palette FuelPal = new(Color.FromRgb(85, 242, 255)), RepairPal = new(Color.FromRgb(95, 227, 176)), AmmoPal = new(Color.FromRgb(255, 180, 80)), GoldPal = new(Color.FromRgb(255, 216, 144));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen TrackPen = MakeTrack(), PlateEdge = FrozenPen(Color.FromArgb(153, 255, 216, 144), 1);
        private static readonly Geometry Drop = Poly(new (double, double)[] { (0, -8), (3, -3), (6, 2), (5, 6), (0, 8), (-5, 6), (-6, 2), (-3, -3) });
        private static readonly Geometry Shell = Poly(new (double, double)[] { (-3, -7), (0, -11), (3, -7), (3, 8), (-3, 8) });

        private static Pen MakeTrack()
        {
            var p = new Pen(Frozen(Color.FromArgb(51, 255, 255, 255)), 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            return p;
        }

        private sealed record Row(char Key, string Label, string Amount, long Cost);
        private readonly List<Row> _rows = new();
        private string _title = "";
        private long _total;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            _rows.Clear(); _total = 0;
            foreach (var part in (detail ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Split('|');
                if (p.Length < 4 || p[0].Length == 0 || _rows.Count >= 4) continue;
                long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long cost);
                _rows.Add(new Row(p[0][0], p[1].Trim().ToUpperInvariant(), p[2].Trim().ToUpperInvariant(), cost));
                _total += cost;
            }
            if (_rows.Count == 1)
                _title = _rows[0].Key switch { 'f' => "REFUELLED", 'r' => "REPAIRED", _ => _rows[0].Label == "REARM" ? "REARMED" : _rows[0].Label == "DRONES" ? "DRONES BOUGHT" : _rows[0].Amount.Length > 0 ? _rows[0].Amount + " RESTOCKED" : "RESTOCKED" };
            else _title = "SHIP SERVICED";
        }

        private static Palette PalFor(char k) => k == 'f' ? FuelPal : k == 'r' ? RepairPal : AmmoPal;
        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        private static Geometry ArcGeo(double cx, double cy, double r, double a0, double sweep)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r), false, false);
                c.ArcTo(new Point(cx + Math.Cos(a0 + sweep) * r, cy + Math.Sin(a0 + sweep) * r), new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private static void DrawIcon(DrawingContext dc, char key, double x, double y, Palette pal)
        {
            dc.PushTransform(new TranslateTransform(x, y));
            if (key == 'f') dc.DrawGeometry(pal.Solid, null, Drop);
            else if (key == 'r')
            {
                dc.DrawLine(pal.Edge, new Point(-6, 6), new Point(3, -3));
                dc.DrawEllipse(null, pal.Edge, new Point(5, -5), 3.6, 3.6);
            }
            else dc.DrawGeometry(pal.Solid, null, Shell);
            dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0 || _rows.Count == 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            int n = _rows.Count;
            double gap = SceneW / (n + 1), R = Math.Min(24, gap / 2 - 5), cy = 38;
            for (int i = 0; i < n; i++)
            {
                var row = _rows[i];
                var pal = PalFor(row.Key);
                double cx = gap * (i + 1), appear = EaseOut(Phase(t, 0.1 + i * 0.3, 0.3)), fill = EaseOut(Phase(t, 0.2 + i * 0.3, 1.2));
                if (appear <= 0) continue;
                dc.PushOpacity(appear);
                dc.DrawGeometry(null, TrackPen, ArcGeo(cx, cy, R, Math.PI * 0.75, Math.PI * 1.5));
                dc.DrawGeometry(null, pal.Arc, ArcGeo(cx, cy, R, Math.PI * 0.75, Math.PI * 1.5 * (0.15 + 0.85 * fill)));
                DrawIcon(dc, row.Key, cx, cy - 1, pal);
                dc.Pop();
                double tx = Phase(t, 1.0 + i * 0.3, 0.4);
                if (tx > 0)
                {
                    DrawSpaced(dc, row.Label, MonoFace, Fit(row.Label, 8, gap - 4), NameBrush, cx, cy + R + 7, 0.04, tx);
                    if (row.Amount.Length > 0) DrawSpaced(dc, row.Amount, MonoFace, Fit(row.Amount, 7, gap - 4), pal.Solid, cx, cy + R + 18, 0.04, tx * 0.9);
                }
            }

            double plateAt = 1.4, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 108, 200, 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), GoldPal.Solid, SceneW / 2, 111, 0.08, pa);
                string cost = "-" + Math.Round(_total * EaseOut(Phase(t, plateAt + 0.1, 1.4))).ToString("N0", CultureInfo.InvariantCulture) + " CR";
                DrawSpaced(dc, cost, MonoFace, 9, NameBrush, SceneW / 2, 125, 0.08, Phase(t, plateAt + 0.2, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
