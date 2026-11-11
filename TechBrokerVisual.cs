using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A technology broker unlocking modules (the journal's TechnologyBroker): greyed hexagon tiles with padlocks sit in a row, a scan line sweeps down over them and each flips from locked to lit as it passes,
    // then a plate names what was unlocked. Steel blue for a human broker, emerald for a Guardian one. Detail = "HUMAN|materials spent|name;name" or "GUARDIAN|materials spent|name;name".
    public sealed class TechBrokerVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.2, SceneW = 240, SceneH = 150, CentreX = 120, TileY = 56, HexR = 19;
        protected override double Duration => FadeOutAt + 0.6;

        // Everything that takes the broker's colour, built once per colour.
        private sealed class Palette
        {
            public readonly Brush Solid, Tail, Fill;
            public readonly Pen[] Glyph, Edge, Flash;
            public readonly Pen PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Fill = Frozen(Color.FromArgb(36, c.R, c.G, c.B));
                var tail = new LinearGradientBrush(Color.FromArgb(0, c.R, c.G, c.B), Color.FromArgb(128, c.R, c.G, c.B), 90); tail.Freeze(); Tail = tail;
                Glyph = AlphaPens(c, 1.2); Edge = AlphaPens(c, 1.5); Flash = AlphaPens(c, 1.6);
                PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }

        private static readonly Palette Human = new(Color.FromRgb(90, 170, 255)), Guardian = new(Color.FromRgb(80, 230, 160));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), NameBrush = Frozen(Color.FromRgb(0xe6, 0xf2, 0xff)), LockFill = Frozen(Color.FromArgb(51, 150, 160, 175));
        private static readonly Pen LockedEdge = FrozenPen(Color.FromArgb(160, 140, 150, 165), 1.5), LockPen = FrozenPen(Color.FromArgb(204, 150, 160, 175), 1.6);
        private static readonly Geometry HexShape = Hex(HexR), GlyphShape = MakeGlyph(), LockShape = MakeLock();

        private static Geometry Hex(double r)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = i / 6.0 * Math.PI * 2 + Math.PI / 6;
                    var p = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                    if (i == 0) c.BeginFigure(p, false, true); else c.LineTo(p, true, false);
                }
            }
            g.Freeze(); return g;
        }
        private static Geometry MakeGlyph()
        {
            var g = new GeometryGroup();
            g.Children.Add(new RectangleGeometry(new Rect(-9, -5, 18, 10), 2, 2));
            g.Children.Add(new RectangleGeometry(new Rect(-13, -2, 4, 4)));
            g.Children.Add(new RectangleGeometry(new Rect(9, -2, 4, 4)));
            g.Children.Add(new LineGeometry(new Point(-3, -5), new Point(-3, 5)));
            g.Children.Add(new LineGeometry(new Point(3, -5), new Point(3, 5)));
            g.Freeze(); return g;
        }
        private static Geometry MakeLock()
        {
            var g = new GeometryGroup();
            g.Children.Add(new RectangleGeometry(new Rect(-7, -2, 14, 10)));
            g.Children.Add(Geometry.Parse("M -4.5,-2 L -4.5,-6 A 4.5,4.5 0 0 1 4.5,-6 L 4.5,-2"));
            g.Freeze(); return g;
        }

        private bool _guardian;
        private string _head = "", _l1 = "", _l2 = "", _l3 = "";
        private readonly List<string> _items = new();

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _guardian = p.Length > 0 && p[0].Trim().StartsWith("G", StringComparison.OrdinalIgnoreCase);
            int mats = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? Math.Max(0, m) : 0;
            _items.Clear();
            if (p.Length > 2) foreach (var n in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries)) if (n.Trim().Length > 0) _items.Add(n.Trim());
            if (_items.Count == 0) _items.Add("Module");
            _head = _guardian ? "GUARDIAN TECH BROKER" : "HUMAN TECH BROKER";
            _l1 = _items.Count > 1 ? _items.Count.ToString(CultureInfo.InvariantCulture) + " MODULES UNLOCKED" : "MODULE UNLOCKED";
            _l2 = _items[0] + (_items.Count > 1 ? "  +" + (_items.Count - 1).ToString(CultureInfo.InvariantCulture) + " more" : "");
            _l3 = _head + (mats > 0 ? "  -  " + mats.ToString(CultureInfo.InvariantCulture) + " MATERIALS" : "");
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.25) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var pal = _guardian ? Guardian : Human;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            DrawSpaced(dc, _head, MonoFace, 8, pal.Solid, CentreX, 6, 0.2, Phase(t, 0, 0.3) * 0.9);

            // The tiles: locked and grey until the scan line passes them, then lit.
            int n = Math.Min(3, _items.Count);
            double[] xs = n == 1 ? new[] { 120.0 } : n == 2 ? new[] { 88.0, 152.0 } : new[] { 76.0, 120.0, 164.0 };
            double sy = 18 + Phase(t, 0.5, 1.1) * 60, litAt = 0.5 + (TileY - 4 - 18) / 60.0 * 1.1;
            bool scanned = t > 0.5 && sy > TileY - 4;
            double p = scanned ? Phase(t, litAt, 0.4) : 0;
            foreach (double x in xs)
            {
                dc.PushTransform(new TranslateTransform(x, TileY));
                if (!scanned)
                {
                    dc.DrawGeometry(null, LockedEdge, HexShape);
                    dc.DrawGeometry(LockFill, LockPen, LockShape);
                    dc.DrawEllipse(LockPen.Brush, null, new Point(0, 3), 1.4, 1.4);
                }
                else
                {
                    dc.PushOpacity(p); dc.DrawGeometry(pal.Fill, null, HexShape); dc.Pop();
                    dc.DrawGeometry(null, pal.Edge[(int)Math.Round((0.4 + 0.6 * p) * 8)], HexShape);
                    if (p > 0) dc.DrawGeometry(null, pal.Glyph[(int)Math.Round(p * 8)], GlyphShape);
                    if (p > 0 && p < 1)
                    {
                        double s = (HexR + p * 14) / HexR;
                        dc.PushTransform(new ScaleTransform(s, s));
                        dc.DrawGeometry(null, pal.Flash[(int)Math.Round((1 - p) * 8)], HexShape);
                        dc.Pop();
                    }
                }
                dc.Pop();
            }

            // The scan line sweeping down over the tiles.
            if (t > 0.5 && t < 1.7)
            {
                dc.DrawRectangle(pal.Tail, null, new Rect(30, sy - 16, 180, 16));
                dc.DrawRectangle(pal.Solid, null, new Rect(30, sy, 180, 1.5));
            }

            double pa = Phase(t, 2.3, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, pal.PlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _l1, MonoFace, Fit(_l1, 12, 180), pal.Solid, SceneW / 2, 106, 0.1, pa);
                DrawSpaced(dc, _l2, MonoFace, Fit(_l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, 2.5, 0.4));
                DrawSpaced(dc, _l3, MonoFace, Fit(_l3, 8, 180), pal.Solid, SceneW / 2, 134, 0.08, Phase(t, 2.7, 0.4) * 0.7);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
