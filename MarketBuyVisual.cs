using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Cargo bought at a market (the journal's MarketBuy): a paper receipt prints out of a slot with the item, the tonnes, the price per tonne and a total that counts up, how the price
    // compares with the galactic average when the app knows it (green below, red above), and a PAID stamp lands below the figures.
    // Detail = "item|tonnes|price per tonne|total|galactic average price per tonne (0 when unknown)".
    public sealed class MarketBuyVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6, PaperX = 74, PaperW = 92, PaperTop = 11, PaperH = 86;
        private static readonly Color Amber = Color.FromRgb(255, 180, 80), Good = Color.FromRgb(125, 255, 154), Bad = Color.FromRgb(255, 122, 122);
        private static readonly Brush AmberB = Frozen(Amber), GoodB = Frozen(Good), BadB = Frozen(Bad), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), Paper = Frozen(Color.FromRgb(0xf1, 0xed, 0xe0)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a));
        private static readonly Brush InkGood = Frozen(Color.FromRgb(0x1f, 0x8a, 0x42)), InkBad = Frozen(Color.FromRgb(0xb8, 0x32, 0x2a)), SlotBrush = Frozen(Color.FromRgb(0x2a, 0x34, 0x38)), StampBack = Frozen(Color.FromArgb(235, 8, 13, 13));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Amber.R, Amber.G, Amber.B), 1), SlotPen = FrozenPen(Color.FromRgb(0x7f, 0xa0, 0xaa), 1), StampPen = FrozenPen(Amber, 1.4);
        private static readonly Pen Dashed = MakeDashed();
        private static Pen MakeDashed() { var p = new Pen(Frozen(Color.FromRgb(0x99, 0x99, 0x99)), 0.6) { DashStyle = new DashStyle(new double[] { 2.5, 2.5 }, 0) }; p.Freeze(); return p; }

        private string _name = "", _sub = "", _avgLine = "";
        private long _tonnes, _price, _total, _mean;
        private int _avgState;   // 0 unknown, 1 below the average (good), 2 above

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            var inv = CultureInfo.InvariantCulture;
            _name = P(0).ToUpperInvariant();
            long.TryParse(P(1), NumberStyles.Integer, inv, out _tonnes); long.TryParse(P(2), NumberStyles.Integer, inv, out _price);
            long.TryParse(P(3), NumberStyles.Integer, inv, out _total); long.TryParse(P(4), NumberStyles.Integer, inv, out _mean);
            if (_total <= 0) _total = _tonnes * _price;
            _avgState = 0; _avgLine = "";
            string vs = "";
            if (_mean > 0 && _price > 0)
            {
                int pct = (int)Math.Round((_price - _mean) * 100.0 / _mean);
                _avgState = pct <= 0 ? 1 : 2;
                vs = pct <= 0 ? $"{-pct}% BELOW AVERAGE" : $"{pct}% ABOVE AVERAGE";
                _avgLine = vs.Replace("AVERAGE", "AVG");
            }
            _sub = $"-{_total.ToString("N0", inv)} CR  AT {_price.ToString("N0", inv)}/T" + (vs.Length > 0 ? "  " + vs : "");
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

            double slotA = EaseOut(Phase(t, 0.05, 0.3));
            if (slotA > 0)
            {
                dc.PushOpacity(slotA);
                dc.DrawRoundedRectangle(SlotBrush, SlotPen, new Rect(PaperX - 4, 5, PaperW + 8, 6), 2, 2);
                dc.Pop();
            }

            // The receipt prints downward from the slot: the paper is clipped to the part that has come out.
            double pr = EaseOut(Phase(t, 0.3, 1.8)), shown = PaperH * pr;
            if (shown > 1)
            {
                dc.PushClip(ClipOf(new Rect(PaperX, PaperTop, PaperW, shown)));
                dc.DrawRectangle(Paper, null, new Rect(PaperX, PaperTop, PaperW, PaperH));
                double l = PaperX + 7, r = PaperX + PaperW - 7;
                DrawText(dc, "MARKET RECEIPT", MonoFace, 6.5, Ink, PaperX + PaperW / 2, PaperTop + 4, 1, TextAlignment.Center);
                DrawText(dc, _name, MonoFace, Fit(_name, 6.5, 56), Ink, l, PaperTop + 18, 1, TextAlignment.Left);
                DrawText(dc, _tonnes + " T", MonoFace, 6.5, Ink, r, PaperTop + 18, 1, TextAlignment.Right);
                DrawText(dc, "PRICE/T", MonoFace, 6, Ink, l, PaperTop + 28, 1, TextAlignment.Left);
                DrawText(dc, _price.ToString("N0", CultureInfo.InvariantCulture), MonoFace, 6, Ink, r, PaperTop + 28, 1, TextAlignment.Right);
                dc.DrawLine(Dashed, new Point(l, PaperTop + 38), new Point(r, PaperTop + 38));
                DrawText(dc, "TOTAL", MonoFace, 7.5, Ink, l, PaperTop + 42, 1, TextAlignment.Left);
                double count = EaseOut(Phase(t, 1.6, 0.9));
                string money = Math.Round(_total * count).ToString("N0", CultureInfo.InvariantCulture) + " CR";
                DrawText(dc, money, MonoFace, 7.5, Ink, r, PaperTop + 42, 1, TextAlignment.Right);
                if (_avgState > 0)
                    DrawText(dc, _avgLine, MonoFace, 6, _avgState == 1 ? InkGood : InkBad, PaperX + PaperW / 2, PaperTop + 54, Phase(t, 2.2, 0.3), TextAlignment.Center);
                dc.Pop();
            }

            // The PAID stamp: below the figures so it never covers the total.
            double sp = Phase(t, 2.5, 0.25);
            if (sp > 0)
            {
                const string stamp = "PAID";
                double sz = 9, sca = 1 + (1 - EaseOut(sp)) * 0.9, wBox = stamp.Length * (sz * 0.62) + 14;
                dc.PushTransform(new TranslateTransform(PaperX + PaperW / 2 + 12, PaperTop + 73)); dc.PushTransform(new RotateTransform(-11)); dc.PushTransform(new ScaleTransform(sca, sca));
                dc.PushOpacity(EaseOut(sp));
                dc.DrawRectangle(StampBack, StampPen, new Rect(-wBox / 2, -sz * 0.8, wBox, sz * 1.6));
                DrawSpaced(dc, stamp, MonoFace, sz, AmberB, 0, -sz * 0.6, 0.1, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 3.0, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(10, 103, 220, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "CARGO PURCHASED", MonoFace, 12, AmberB, SceneW / 2, 106, 0.08, pa);
                string line = _name + "  " + _tonnes + " T";
                DrawSpaced(dc, line, MonoFace, Fit(line, 9, 205), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 205), _avgState == 1 ? GoodB : _avgState == 2 ? BadB : Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
