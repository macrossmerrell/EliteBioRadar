using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // An organism sample taken: a rotating DNA helix that lights a third of its length for each sample (1 = Log, 2 and 3 = the Samples). Stage 4 is the Analyse event a few seconds after the
    // third: the whole helix lights, turns gold and spins faster, and a burst marks the genus complete. Subject = the species name, Detail = "1" to "4".
    public sealed class OrganismScanVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, X0 = 40, X1 = 200, Seg = (X1 - X0) / 3, MidY = 52;
        private static readonly Color Green = Color.FromRgb(120, 230, 140), Gold = Color.FromRgb(255, 217, 122);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), GreenBrush = Frozen(Green), GoldBrush = Frozen(Gold), FrontBrush = Frozen(Color.FromArgb(204, 255, 255, 255));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen GreenEdge = FrozenPen(Color.FromArgb(153, Green.R, Green.G, Green.B), 1), GoldEdge = FrozenPen(Color.FromArgb(153, Gold.R, Gold.G, Gold.B), 1);
        // [colour mix 0 (green) to 4 (gold)][0 = not yet lit, 1 = lit]
        private static readonly Brush[][] Dot = MakeBrushes(0.22, 0.95);
        private static readonly Pen[][] Rung = MakeRungs(0.12, 0.52);
        private static readonly Pen[] BurstRing = AlphaPens(Gold, 2);
        private static readonly Brush[] BurstDot = AlphaBrushes(Gold);

        private static Brush[][] MakeBrushes(double off, double on)
        {
            var a = new Brush[5][];
            for (int m = 0; m < 5; m++)
            {
                var c = Mix(Green, Gold, m / 4.0);
                a[m] = new Brush[] { Frozen(Color.FromArgb((byte)Math.Round(255 * off), c.R, c.G, c.B)), Frozen(Color.FromArgb((byte)Math.Round(255 * on), c.R, c.G, c.B)) };
            }
            return a;
        }

        private static Pen[][] MakeRungs(double off, double on)
        {
            var a = new Pen[5][];
            for (int m = 0; m < 5; m++)
            {
                var c = Mix(Green, Gold, m / 4.0);
                a[m] = new Pen[] { FrozenPen(Color.FromArgb((byte)Math.Round(255 * off), c.R, c.G, c.B), 1), FrozenPen(Color.FromArgb((byte)Math.Round(255 * on), c.R, c.G, c.B), 1) };
            }
            return a;
        }

        private int _stage = 1;
        private string _title = "";
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();
        private string _name = "";
        private double _rot, _lastT;
        protected override double Duration => _stage == 4 ? 4.4 : 3.4;

        protected override void OnStart(string subject, string detail)
        {
            _stage = int.TryParse(detail, out var s) ? Math.Clamp(s, 1, 4) : 1;
            _name = (subject ?? "").ToUpperInvariant();
            _title = _stage == 4 ? "GENUS COMPLETE" : $"SAMPLE {_stage} OF 3";
            _rot = 0; _lastT = 0;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            bool done = _stage == 4;
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, done ? 3.8 : 2.8);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            bool gold = done && t > 1.5;
            _rot += Math.Clamp(t - _lastT, 0, 0.1) * (gold ? 6 : 3);
            _lastT = t;
            double lit = done ? X1 + 1 : X0 + (_stage - 1) * Seg + EaseOut(Phase(t, 0.2, 1.2)) * Seg;
            int mix = done ? (int)Math.Round(Phase(t, 1.5, 0.3) * 4) : 0;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            int i = 0;
            for (double x = X0; x <= X1; x += 4, i++)
            {
                double ang = x / 11 + _rot, y1 = MidY + 15 * Math.Sin(ang), y2 = MidY - 15 * Math.Sin(ang);
                int on = x <= lit ? 1 : 0;
                if (i % 2 == 0) dc.DrawLine(Rung[mix][on], new Point(x, y1), new Point(x, y2));
                double r = on == 1 ? 2 : 1.4;
                dc.DrawEllipse(Dot[mix][on], null, new Point(x, y1), r, r);
                dc.DrawEllipse(Dot[mix][on], null, new Point(x, y2), r, r);
            }
            if (!done && lit <= X1) dc.DrawEllipse(FrontBrush, null, new Point(lit, MidY + 15 * Math.Sin(lit / 11 + _rot)), 3, 3);

            if (done)
            {
                double b = Phase(t, 1.6, 1.2);
                if (b > 0 && b < 1)
                {
                    int al = (int)Math.Round((1 - b) * 8);
                    dc.DrawEllipse(null, BurstRing[al], new Point(120, MidY), 10 + b * 70, 10 + b * 70);
                    for (int j = 0; j < 14; j++)
                    {
                        double an = j * 2 * Math.PI / 14 + 0.3, d = 14 + b * 60;
                        dc.DrawEllipse(BurstDot[al], null, new Point(120 + Math.Cos(an) * d, MidY + Math.Sin(an) * d), 1.6, 1.6);
                    }
                }
            }

            dc.DrawRoundedRectangle(PlateBrush, done ? GoldEdge : GreenEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _title, MonoFace, 13, done ? GoldBrush : GreenBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _name, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, done ? 1.6 : 1.2, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
