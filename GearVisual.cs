using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // On-foot gear dealings (BuySuit / SellSuit / UpgradeSuit, BuyWeapon / SellWeapon / UpgradeWeapon, Sell / Buy / TradeMicroResources): the item is scanned on a faint
    // hex grid, a line sweeps down it inside corner brackets, and an upgrade ticks its grade pips up.
    //   BUY green    SELL gold    UP violet (grade pips)    TRADE cyan
    // Detail = "ACTION|ICON|name|chip line|grade before|grade after" with ACTION BUY, SELL, UP or TRADE and ICON suit, gun or box.
    public sealed class GearVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private sealed class Palette
        {
            public readonly Brush Solid, Dim;
            public readonly Pen Edge, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); Dim = Frozen(Color.FromArgb(51, 255, 255, 255)); Edge = FrozenPen(c, 1.3); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette BuyPal = new(Color.FromRgb(95, 227, 176)), SellPal = new(Color.FromRgb(255, 216, 144)), UpPal = new(Color.FromRgb(200, 122, 255)), TradePal = new(Color.FromRgb(85, 242, 255));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), Hole = Frozen(Color.FromRgb(8, 13, 13));
        private static readonly Pen GridPen = FrozenPen(Color.FromArgb(22, 255, 255, 255), 0.8), ScanPen = FrozenPen(Color.FromRgb(255, 255, 255), 1.5);
        private static readonly Geometry HexGrid = MakeGrid();

        private static Geometry MakeGrid()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int j = 0; j < 5; j++)
                    for (int i = 0; i < 10; i++)
                    {
                        double x = 30 + i * 19.5 + (j % 2) * 9.75, y = 10 + j * 18;
                        for (int a = 0; a < 6; a++)
                        {
                            var pt = new Point(x + Math.Cos(a * Math.PI / 3) * 9, y + Math.Sin(a * Math.PI / 3) * 9);
                            if (a == 0) c.BeginFigure(pt, false, true); else c.LineTo(pt, true, false);
                        }
                    }
            }
            g.Freeze();
            return g;
        }

        private Palette _pal = BuyPal;
        private string _action = "BUY", _icon = "box", _name = "", _line = "", _title = "";
        private int _g0, _g1;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _action = P(0).ToUpperInvariant();
            _icon = P(1).ToLowerInvariant();
            _name = P(2).ToUpperInvariant(); _line = P(3).ToUpperInvariant();
            int.TryParse(P(4), out _g0); int.TryParse(P(5), out _g1);
            string what = _icon == "suit" ? "SUIT" : _icon == "gun" ? "WEAPON" : "MICRO-RESOURCES";
            switch (_action)
            {
                case "SELL": _pal = SellPal; _title = what + " SOLD"; break;
                case "UP": _pal = UpPal; _title = what + " UPGRADED"; break;
                case "TRADE": _pal = TradePal; _title = what + " TRADED"; break;
                default: _action = "BUY"; _pal = BuyPal; _title = what + " BOUGHT"; break;
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        private void DrawIcon(DrawingContext dc, double scale)
        {
            var b = _pal.Solid;
            dc.PushTransform(new ScaleTransform(scale, scale));
            if (_icon == "suit")
            {
                dc.DrawEllipse(b, null, new Point(0, -14), 6, 6);
                dc.DrawRoundedRectangle(b, null, new Rect(-9, -6, 18, 20), 3, 3);
                dc.DrawRectangle(b, null, new Rect(-14, -5, 5, 14)); dc.DrawRectangle(b, null, new Rect(9, -5, 5, 14));
                dc.DrawRectangle(b, null, new Rect(-7, 14, 5, 10)); dc.DrawRectangle(b, null, new Rect(2, 14, 5, 10));
            }
            else if (_icon == "gun")
            {
                dc.DrawRectangle(b, null, new Rect(-18, -3, 30, 6)); dc.DrawRectangle(b, null, new Rect(12, -1.5, 10, 3));
                dc.DrawRectangle(b, null, new Rect(-12, 3, 5, 10)); dc.DrawRectangle(b, null, new Rect(-4, -7, 10, 4));
                dc.DrawGeometry(b, null, Poly(new (double, double)[] { (-18, -3), (-24, 4), (-18, 4) }));
            }
            else
            {
                dc.DrawRoundedRectangle(b, null, new Rect(-10, -8, 20, 16), 2, 2);
                dc.DrawRectangle(Hole, null, new Rect(-10, -2, 20, 2)); dc.DrawRectangle(Hole, null, new Rect(-2, -4, 4, 4));
            }
            dc.Pop();
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

            dc.DrawGeometry(null, GridPen, HexGrid);

            double p = EaseOut(Phase(t, 0.2, 0.6));
            if (p > 0.01)
            {
                dc.PushTransform(new TranslateTransform(120, 46)); dc.PushOpacity(p);
                DrawIcon(dc, 1.3 * p);
                dc.Pop(); dc.Pop();
            }

            // The sweep, inside four corner brackets.
            double sc = Phase(t, 0.6, 1.2);
            if (sc > 0 && sc < 1)
            {
                dc.PushOpacity(0.7 * (1 - Phase(t, 1.8, 0.2)));
                double sy = 15 + sc * 63;
                dc.DrawLine(ScanPen, new Point(75, sy), new Point(165, sy));
                dc.Pop();
            }
            double ba = Phase(t, 0.1, 0.4);
            if (ba > 0)
            {
                dc.PushOpacity(ba);
                foreach (var (bx, by, sx, sy2) in new[] { (75.0, 10.5, 1, 1), (165.0, 10.5, -1, 1), (75.0, 80.0, 1, -1), (165.0, 80.0, -1, -1) })
                {
                    dc.DrawLine(_pal.Edge, new Point(bx, by), new Point(bx + sx * 7.5, by));
                    dc.DrawLine(_pal.Edge, new Point(bx, by), new Point(bx, by + sy2 * 7.5));
                }
                dc.Pop();
            }

            if (_action == "UP" && _g1 > 0)
            {
                int grade = t > 1.6 ? _g1 : _g0;
                double pa0 = Phase(t, 0.8, 0.4);
                dc.PushOpacity(pa0);
                for (int i = 0; i < 5; i++) dc.DrawRectangle(i < grade ? _pal.Solid : _pal.Dim, null, new Rect(93 + i * 10.5, 85.5, 7.5, 4));
                dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _line.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _line, MonoFace, Fit(_line, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
