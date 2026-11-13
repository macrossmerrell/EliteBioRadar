using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Outfitting moments (the journal's ModuleBuy, ModuleSell / ModuleSellRemote, ModuleStore, ModuleRetrieve and MassModuleStore): a module tile scales in with a hexagon icon and spec lines drawing across, then a tilted stamp lands.
    //   BUY  green, FITTED    STORE cyan, STORED    GET violet, RETRIEVED    SELL gold, SOLD    MASS cyan, STORED (several at once)
    // Detail = "KIND|item|detail line" with KIND BUY, SELL, STORE, GET or MASS.
    public sealed class ModuleVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private sealed class Palette
        {
            public readonly Brush Solid, Dim;
            public readonly Pen Edge, StampPen, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Dim = Frozen(Color.FromArgb(178, c.R, c.G, c.B)); Edge = FrozenPen(c, 1.2); StampPen = FrozenPen(c, 1.8); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette BuyPal = new(Color.FromRgb(95, 227, 176)), StorePal = new(Color.FromRgb(85, 242, 255)), GetPal = new(Color.FromRgb(200, 122, 255)), SellPal = new(Color.FromRgb(255, 216, 144));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardBrush = Frozen(Color.FromRgb(0x1b, 0x22, 0x30)), FaintBrush = Frozen(Color.FromArgb(71, 255, 255, 255));
        private static readonly Geometry HexOuter = MakeHex(13.5), HexInner = MakeHex(6);

        private static Geometry MakeHex(double r)
        {
            var pts = new (double, double)[6];
            for (int i = 0; i < 6; i++) pts[i] = (Math.Cos(i * Math.PI / 3) * r, Math.Sin(i * Math.PI / 3) * r);
            return Poly(pts);
        }

        private Palette _pal = BuyPal;
        private string _title = "", _item = "", _line3 = "", _stamp = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _item = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "MODULE";
            _line3 = p.Length > 2 ? p[2].Trim().ToUpperInvariant() : "";
            switch (kind)
            {
                case "SELL": _pal = SellPal; _title = "MODULE SOLD"; _stamp = "SOLD"; break;
                case "STORE": _pal = StorePal; _title = "MODULE STORED"; _stamp = "STORED"; break;
                case "GET": _pal = GetPal; _title = "MODULE RETRIEVED"; _stamp = "RETRIEVED"; break;
                case "MASS": _pal = StorePal; _title = "MODULES STORED"; _stamp = "STORED"; break;
                default: _pal = BuyPal; _title = "MODULE FITTED"; _stamp = "FITTED"; break;
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

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

            // The tile scales in; the hexagon icon, the item name and three spec lines draw across.
            double p = EaseOut(Phase(t, 0.1, 0.6));
            if (p > 0.01)
            {
                dc.PushTransform(new TranslateTransform(SceneW / 2, 47)); dc.PushTransform(new ScaleTransform(p, p));
                dc.PushOpacity(p);
                dc.DrawRoundedRectangle(CardBrush, _pal.Edge, new Rect(-72, -33, 144, 66), 4, 4);
                dc.PushTransform(new TranslateTransform(-46, -4));
                dc.DrawGeometry(null, _pal.Edge, HexOuter);
                dc.DrawGeometry(_pal.Solid, null, HexInner);
                dc.Pop();
                DrawText(dc, _item, MonoFace, Fit(_item, 8, 92), _pal.Solid, -22, -26, 1, TextAlignment.Left);
                for (int i = 0; i < 3; i++)
                {
                    double q = Phase(t, 0.5 + i * 0.25, 0.5);
                    dc.DrawRectangle(FaintBrush, null, new Rect(-22, -12 + i * 8, (75 - i * 10.5) * q, 2.2));
                }
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            // The stamp.
            double s = Phase(t, 1.7, 0.25);
            if (s > 0)
            {
                double sc = 1 + (1 - EaseOut(s)) * 0.8, wBox = _stamp.Length * 7.6 + 12;
                dc.PushTransform(new TranslateTransform(161, 63)); dc.PushTransform(new RotateTransform(-11.5)); dc.PushTransform(new ScaleTransform(sc, sc));
                dc.PushOpacity(EaseOut(s));
                dc.DrawRectangle(null, _pal.StampPen, new Rect(-wBox / 2, -9, wBox, 18));
                DrawSpaced(dc, _stamp, MonoFace, 10, _pal.Solid, 0, -6.5, 0.05, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _line3.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _item, MonoFace, Fit(_item, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _line3, MonoFace, Fit(_line3, 8, 190), _pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
