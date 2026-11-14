using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Cargo sold at a market (the journal's MarketSell): a grey PAID bar and a SOLD bar rise side by side (green when it is above what was paid, red when below); a dashed line and a bracket mark the difference.
    // The plate gives the tonnes and the profit or loss. Detail = "item|tonnes|average price paid|sold price" (prices per tonne).
    public sealed class TradeProfitVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, Base = 84, MaxBar = 60;
        private sealed class Palette
        {
            public readonly Brush Solid, Bar;
            public readonly Pen Edge;
            public readonly Brush[] Alpha;
            public readonly Pen[] Dash;
            public Palette(Color c)
            {
                Solid = Frozen(c); Bar = Frozen(Color.FromArgb(191, c.R, c.G, c.B)); Edge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
                Alpha = AlphaBrushes(c);
                var list = new Pen[9];
                for (int i = 0; i < 9; i++) { var p = new Pen(AlphaBrushes(c)[i], 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) }; p.Freeze(); list[i] = p; }
                Dash = list;
            }
        }
        private static readonly Palette Win = new(Color.FromRgb(95, 227, 176)), Loss = new(Color.FromRgb(255, 107, 94));
        private static readonly Brush GreyBar = Frozen(Color.FromArgb(153, 165, 175, 195)), GreyText = Frozen(Color.FromRgb(165, 175, 195)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));

        private string _item = "";
        private long _tonnes, _paid, _sold;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _item = p.Length > 0 ? p[0].Trim() : "";
            long L(int i) => p.Length > i && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;
            _tonnes = Math.Max(1, L(1)); _paid = L(2); _sold = L(3);
            if (_item.Length == 0) _item = "Cargo";
        }

        private static string Credits(double n) => Math.Round(Math.Abs(n)).ToString("N0", CultureInfo.InvariantCulture) + " CR";
        private static string Short(double n) => n >= 10000 ? Math.Round(n / 1000).ToString("0", CultureInfo.InvariantCulture) + "K" : Math.Round(n).ToString("0", CultureInfo.InvariantCulture);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            long profit = (_sold - _paid) * _tonnes;
            bool win = profit >= 0;
            var pal = win ? Win : Loss;
            double m = Math.Max(1, Math.Max(_paid, _sold)), h1 = MaxBar * _paid / m, h2 = MaxBar * _sold / m;
            double g1 = h1 * EaseOut(Phase(t, 0.2, 0.5)), g2 = h2 * EaseOut(Phase(t, 0.5, 0.5));

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.DrawRectangle(GreyBar, null, new Rect(70, Base - g1, 36, g1));
            dc.DrawRectangle(pal.Bar, null, new Rect(134, Base - g2, 36, g2));
            DrawSpaced(dc, "PAID", MonoFace, 7, GreyText, 88, Base + 4, 0.05, 1);
            DrawSpaced(dc, "SOLD", MonoFace, 7, pal.Solid, 152, Base + 4, 0.05, 1);
            DrawSpaced(dc, Short(_paid), MonoFace, 7, NameBrush, 88, Base - g1 - 10, 0.04, Phase(t, 0.5, 0.2));
            DrawSpaced(dc, Short(_sold), MonoFace, 7, NameBrush, 152, Base - g2 - 10, 0.04, Phase(t, 0.9, 0.2));

            double gp = Phase(t, 1.1, 0.4);
            if (gp > 0)
            {
                int lv = Math.Clamp((int)Math.Round(gp * 8), 0, 8);
                double y1 = Base - h1, y2 = Base - h2;
                dc.DrawLine(pal.Dash[lv], new Point(66, y1), new Point(174, y1));
                dc.DrawRectangle(pal.Alpha[lv], null, new Rect(176, Math.Min(y1, y2), 4, Math.Max(1, Math.Abs(y1 - y2))));
                DrawText(dc, (win ? "+" : "-") + Short(Math.Abs(_sold - _paid)), MonoFace, 7, pal.Solid, 184, (y1 + y2) / 2 - 3, gp, TextAlignment.Left);
            }

            string l2 = _tonnes.ToString(CultureInfo.InvariantCulture) + " T  " + _item.ToUpperInvariant(), l3 = (win ? "PROFIT  +" : "LOSS  -") + Credits(profit);
            double plateAt = 1.9, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, pal.Edge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "CARGO SOLD", MonoFace, 12, pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
