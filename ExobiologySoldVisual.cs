using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Exobiology data sold (the journal's SellOrganicData): a specimen card slides in, a SOLD stamp thumps onto it and the total counts up in gold; a green plate gives the species and any first-discovery bonus.
    // Detail = "count|first species|base value|bonus".
    public sealed class ExobiologySoldVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, CardX = 65, CardY = 10, CardW = 110, CardH = 70;
        private static readonly Color Green = Color.FromRgb(0x5f, 0xe3, 0xb0), Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush GreenBrush = Frozen(Green), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a)),
            PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardFill = Frozen(Color.FromArgb(15, Green.R, Green.G, Green.B));
        private static readonly Brush[] Lines = AlphaBrushes(Green);
        private static readonly Pen CardEdge = FrozenPen(Color.FromArgb(140, Green.R, Green.G, Green.B), 1.5), StampPen = FrozenPen(Green, 1.5), PlateEdge = FrozenPen(Color.FromArgb(153, Green.R, Green.G, Green.B), 1);

        private int _count = 1;
        private string _name = "";
        private long _base, _bonus;
        private readonly ScaleTransform _stampScale = new();
        private readonly TranslateTransform _stampAt = new();
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _count = p.Length > 0 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Max(1, c) : 1;
            _name = p.Length > 1 ? p[1].Trim() : "";
            _base = p.Length > 2 && long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? Math.Max(0, b) : 0;
            _bonus = p.Length > 3 && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, n) : 0;
            if (_name.Length == 0) _name = "Organic data";
        }

        private static string Credits(double n) => Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) + " CR";

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

            double slide = EaseOut(Phase(t, 0.1, 0.5)), x = CardX + (1 - slide) * 160, cx = x + CardW / 2;
            dc.DrawRectangle(CardFill, CardEdge, new Rect(x, CardY, CardW, CardH));
            string title = _count > 1 ? _count.ToString(CultureInfo.InvariantCulture) + " SPECIES" : _name.ToUpperInvariant();
            string sub = _count > 1 ? _name + "  +" + (_count - 1).ToString(CultureInfo.InvariantCulture) + " more" : "";
            DrawSpaced(dc, title, MonoFace, Fit(title, 9, CardW - 12), NameBrush, cx, CardY + 8, 0.04, 1);
            DrawSpaced(dc, sub, MonoFace, Fit(sub, 6, CardW - 12), MutedBrush, cx, CardY + 21, 0.04, 1);
            for (int i = 0; i < 3; i++) dc.DrawRectangle(Lines[2], null, new Rect(x + 7, CardY + 32 + i * 6, i == 2 ? 50 : 96, 2));

            double st = Phase(t, 0.8, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.2;
                _stampAt.X = cx; _stampAt.Y = 52; _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, StampPen, new Rect(-30, -10, 60, 20));
                DrawSpaced(dc, "SOLD", MonoFace, 10, GreenBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            long total = _base + _bonus;
            DrawSpaced(dc, "+" + Credits(total * EaseOut(Phase(t, 1.1, 1.0))), MonoFace, 9, GoldBrush, cx, CardY + CardH - 14, 0.04, Phase(t, 1.1, 0.2));

            string l2 = _count + (_count > 1 ? " SPECIES" : " SPECIES") + "  -  " + Credits(total), l3 = _bonus > 0 ? "FIRST DISCOVERY BONUS  +" + Credits(_bonus) : "";
            double plateAt = 1.9, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "EXOBIOLOGY DATA SOLD", MonoFace, 11, GreenBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), GoldBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
