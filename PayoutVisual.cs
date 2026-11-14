using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Exploration data sold (the journal's SellExplorationData / MultiSellExplorationData): one star lights up per system sold (up to 14), each throws a small payout that flies into the running
    // total in gold; the stars that carry first-discovery bonus are gold, the rest cyan. The plate says the total, the systems and the base / bonus split.
    // Detail = "systems|base|bonus|total".
    public sealed class PayoutVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 5.4, TotalX = 120, TotalY = 74, MaxStars = 14, Step = 0.14, Flight = 0.8;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush CyanBrush = Frozen(Cyan), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a)), DarkStar = Frozen(Color.FromArgb(70, 136, 150, 160));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush[] CyanLevels = AlphaBrushes(Cyan), GoldLevels = AlphaBrushes(Gold);
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Gold.R, Gold.G, Gold.B), 1);
        private static readonly Pen[] RingPens = AlphaPens(Gold, 1.2);

        private int _systems = 1, _stars = 1;
        private long _base, _bonus, _total;
        private string _label = "";
        private bool[] _gold = Array.Empty<bool>();
        private Point[] _at = Array.Empty<Point>();
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            long L(int i) => p.Length > i && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;
            _systems = (int)Math.Max(1, L(0)); _base = L(1); _bonus = L(2); _total = L(3);
            if (_total <= 0) _total = _base + _bonus;
            _stars = (int)Math.Min(MaxStars, _systems);
            double per = (double)_total / _stars;
            _label = "+" + Compact(per);
            // Which stars carry bonus: spread evenly by the bonus share of the total.
            int goldCount = _total > 0 ? (int)Math.Round(_stars * (double)_bonus / _total) : 0;
            if (_bonus > 0 && goldCount == 0) goldCount = 1;
            _gold = new bool[_stars];
            for (int i = 0; i < _stars; i++) _gold[i] = goldCount > 0 && (int)Math.Floor((i + 1.0) * goldCount / _stars) > (int)Math.Floor(i * (double)goldCount / _stars);
            _at = new Point[_stars];
            for (int i = 0; i < _stars; i++) _at[i] = new Point(35 + Hash(i * 3 + 1) * 170, 8 + Hash(i * 3 + 2) * 50);
        }

        private static string Compact(double n) => n >= 1e6 ? (n / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M" : n >= 1e3 ? Math.Round(n / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k" : Math.Round(n).ToString("0", CultureInfo.InvariantCulture);
        private static string Credits(double n) => Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) + " CR";

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double landed = 0, per = (double)_total / _stars, last = 0;
            for (int i = 0; i < _stars; i++)
            {
                double s = 0.3 + i * Step, on = Phase(t, s, 0.25), f = Phase(t, s + 0.1, Flight);
                var pt = _at[i];
                if (on <= 0) { dc.DrawEllipse(DarkStar, null, pt, 2, 2); continue; }
                var set = _gold[i] ? GoldLevels : CyanLevels;
                double pulse = 1 - Math.Abs(on - 0.5) * 2;
                dc.DrawEllipse(set[(int)Math.Round((0.3 + 0.7 * on) * 8)], null, pt, 2.2 + 1.8 * pulse, 2.2 + 1.8 * pulse);
                if (f > 0 && f < 1)
                {
                    double e = EaseOut(f), x = pt.X + (TotalX - pt.X) * e, y = pt.Y + (TotalY - pt.Y) * e;
                    DrawText(dc, _label, MonoFace, 6.5, GoldBrush, x, y - 4, 1 - f * 0.6);
                }
                else if (f >= 1) { landed += per; last = Math.Max(last, s + 0.1 + Flight); }
            }

            // The running total: ticks up as each payout lands, a ring pulses on the last landing.
            if (landed > 0)
            {
                string total = Credits(Math.Min(_total, landed));
                DrawText(dc, total, MonoFace, 16, GoldBrush, TotalX, TotalY - 8, 1);
                double ring = Phase(t, last, 0.5);
                if (ring > 0 && ring < 1)
                {
                    double g = ring * 14;
                    dc.DrawRectangle(null, RingPens[Math.Clamp((int)Math.Round((1 - ring) * 8), 0, 8)], new Rect(TotalX - 55 - g, TotalY - 10 - g, 110 + g * 2, 20 + g * 2));
                }
            }

            double plateAt = 0.3 + (_stars - 1) * Step + 1.1 + 0.2, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "EXPLORATION DATA SOLD", MonoFace, 12, GoldBrush, SceneW / 2, 106, 0.08, pa);
                string l2 = "+" + Credits(_total);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string l3 = _systems.ToString(CultureInfo.InvariantCulture) + (_systems == 1 ? " system" : " systems") + (_bonus > 0 ? "  -  base " + Compact(_base) + " + bonus " + Compact(_bonus) : "");
                DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
