using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The ship is destroyed, then respawned (the journal's Died and Resurrect). The ship is drawn in dots along its outline. DESTROYED: the cyan outline fades, the dots turn red and scatter into dust, and the
    // plate says "SHIP DESTROYED" over the ship's name and what killed it. RESTORED: mint dots gather from the dust, the outline draws back in with a ripple, and the plate says "SHIP RESTORED" over the ship
    // and the rebuy. Subject = (not used), Detail = "DESTROYED|ship|killer" or "RESTORED|ship|rebuy text".
    public sealed class ShipLostVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.3, CentreX = 120, CentreY = 52, Scale = 1.8;
        private const int PerEdge = 11, EdgeCount = 6, PointCount = PerEdge * EdgeCount;

        private static readonly Color Cyan = Color.FromRgb(0x4f, 0xd6, 0xe8), Red = Color.FromRgb(0xff, 0x5a, 0x5a), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0);
        private static readonly Brush RedBrush = Frozen(Red), MintBrush = Frozen(Mint), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush[] CyanDot = AlphaBrushes(Cyan), RedDot = AlphaBrushes(Red), MintDot = AlphaBrushes(Mint), CyanFill = AlphaBrushes(Cyan), MintFill = AlphaBrushes(Mint);
        private static readonly Pen[] CyanLine = AlphaPens(Cyan, 0.6), MintLine = AlphaPens(Mint, 0.6), MintRipple = AlphaPens(Mint, 1);

        // Where each dot sits on the hull and where it drifts to as dust (all fixed, so the animation is the same every time).
        private static readonly double[] HomeX = new double[PointCount], HomeY = new double[PointCount], DustX = new double[PointCount], DustY = new double[PointCount], Delay = new double[PointCount];
        static ShipLostVisual()
        {
            var hull = new (double x, double y)[] { (0, -14), (11, 12), (4, 8), (0, 14), (-4, 8), (-11, 12) };
            for (int e = 0; e < EdgeCount; e++)
            {
                var p = hull[e]; var q = hull[(e + 1) % EdgeCount];
                for (int k = 0; k < PerEdge; k++)
                {
                    int i = e * PerEdge + k;
                    double f = k / (double)PerEdge;
                    HomeX[i] = CentreX + (p.x + (q.x - p.x) * f) * Scale;
                    HomeY[i] = CentreY + (p.y + (q.y - p.y) * f) * Scale;
                    double ang = Hash(i) * Math.PI * 2, dist = 30 + Hash(i + 5) * 55;
                    DustX[i] = HomeX[i] + Math.Cos(ang) * dist;
                    DustY[i] = HomeY[i] + Math.Sin(ang) * dist * 0.7 - 10;
                    Delay[i] = Hash(i + 2);
                }
            }
        }

        private bool _restored;
        private string _ship = "", _line3 = "";
        private readonly ScaleTransform _outer = new(), _fit = new(), _hullScale = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _hullPlace = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _restored = parts.Length > 0 && parts[0].Trim().Equals("RESTORED", StringComparison.OrdinalIgnoreCase);
            _ship = parts.Length > 1 ? parts[1].Trim() : "";
            _line3 = parts.Length > 2 ? parts[2].Trim().ToUpperInvariant() : "";
            if (_ship.Length == 0) _ship = "Your ship";
        }

        private static double Fit(string text, double max) => Math.Min(max, 200 / (Math.Max(1, text.Length) * 0.66));

        private static int Lv(double a) => Math.Clamp((int)Math.Round(a * 8), 0, 8);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            for (int i = 0; i < PointCount; i++)
            {
                double x, y, al;
                Brush[] set;
                if (!_restored)
                {
                    double start = 0.5 + Delay[i] * 0.5, e = EaseOut(Phase(t, start, 2.8 - start));
                    x = HomeX[i] + (DustX[i] - HomeX[i]) * e; y = HomeY[i] + (DustY[i] - HomeY[i]) * e;
                    al = 1 - Phase(t, 2.2, 1.2);
                    set = t < 0.6 ? CyanDot : RedDot;
                }
                else
                {
                    double start = 0.2 + Delay[i] * 0.4, e = 1 - EaseOut(Phase(t, start, 2.0 - start));
                    x = HomeX[i] + (DustX[i] - HomeX[i]) * e; y = HomeY[i] + (DustY[i] - HomeY[i]) * e;
                    al = Phase(t, 0, 0.6);
                    set = MintDot;
                }
                if (al > 0.01) dc.DrawEllipse(set[Lv(al)], null, new Point(x, y), 1.3, 1.3);
            }

            // The outline: the ship's own cyan outline fading as it breaks up, or the mint one drawing back in once the dots have gathered.
            double outline = !_restored ? 1 - Phase(t, 0.3, 0.3) : Phase(t, 2.0, 0.6) * (1 - Phase(t, 3.4, 0.5));
            if (outline > 0.01)
            {
                _hullPlace.X = CentreX; _hullPlace.Y = CentreY; dc.PushTransform(_hullPlace);
                _hullScale.ScaleX = _hullScale.ScaleY = Scale; dc.PushTransform(_hullScale);
                dc.DrawGeometry((_restored ? MintFill : CyanFill)[Lv(_restored ? 0.15 * outline : 0)], (_restored ? MintLine : CyanLine)[Lv(outline)], ShipOutline);
                dc.Pop(); dc.Pop();
            }
            if (_restored)
            {
                double r = Phase(t, 2.0, 1.0);
                if (r > 0 && r < 1) dc.DrawEllipse(null, MintRipple[Lv((1 - r) * 0.8)], new Point(CentreX, CentreY), 7.5 + r * 35, 7.5 + r * 35);
            }

            double pa = Phase(t, _restored ? 1.9 : 1.7, 0.5), rise = (1 - EaseOut(pa)) * 4;
            if (pa > 0)
            {
                DrawSpaced(dc, _restored ? "SHIP RESTORED" : "SHIP DESTROYED", MonoFace, 11, _restored ? MintBrush : RedBrush, SceneW / 2, 107 + rise, 0.1, pa);
                DrawSpaced(dc, _ship, MonoFace, Fit(_ship, 10), NameBrush, SceneW / 2, 121 + rise, 0.1, pa);
                DrawSpaced(dc, _line3, MonoFace, Fit(_line3, 8), MutedBrush, SceneW / 2, 134 + rise, 0.1, pa);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
