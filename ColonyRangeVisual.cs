using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The commander walks out of, or back into, the colony range of an unfinished organism sample (top-down like the radar). LEFT: the amber hatched ring drops to a faint mint dashed ring with a ripple
    // as the arrow walks out through its edge; the plate says "COLONY RANGE CLEARED" and which sample is ready. ENTERED: the arrow walks back in, the ring locks hatched amber with a ripple, and the
    // plate says "INSIDE COLONY RANGE" with the distance to keep. Detail = "LEFT|name|range metres|next sample number" or "ENTERED|name|range metres|next sample number".
    public sealed class ColonyRangeVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.6, SampleX = 100, SampleY = 50, Radius = 35;

        private static readonly Color Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Amber = Color.FromRgb(0xff, 0xb8, 0x4d);
        private static readonly Brush MintBrush = Frozen(Mint), AmberBrush = Frozen(Amber), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ArrowBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0));
        private static readonly Pen MintPlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), AmberPlateEdge = FrozenPen(Color.FromArgb(153, Amber.R, Amber.G, Amber.B), 1);
        private static readonly Pen AmberRing = FrozenPen(Color.FromArgb(230, Amber.R, Amber.G, Amber.B), 1.5), HatchPen = FrozenPen(Color.FromArgb(90, Amber.R, Amber.G, Amber.B), 0.8);
        private static readonly Pen DashedRing = MakeDashedRing();
        private static readonly Pen[] MintRipple = AlphaPens(Mint, 1.5), AmberRipple = AlphaPens(Amber, 1.5);
        private static readonly Geometry Disc = MakeDisc(), Arrow = Poly(new[] { (5.0, 0.0), (-4, -4), (-2, 0), (-4, 4) });
        private static Pen MakeDashedRing() { var p = new Pen(Frozen(Color.FromArgb(128, Mint.R, Mint.G, Mint.B)), 1) { DashStyle = new DashStyle(new[] { 3.0, 4.0 }, 0) }; p.Freeze(); return p; }
        private static Geometry MakeDisc() { var g = new EllipseGeometry(new Point(SampleX, SampleY), Radius, Radius); g.Freeze(); return g; }

        private bool _left = true;
        private string _name = "", _line3 = "";
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _arrowAt = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _left = !parts[0].Contains("ENTER", StringComparison.OrdinalIgnoreCase);
            _name = parts.Length > 1 ? parts[1].Trim() : "";
            if (_name.Length == 0) _name = "Organism sample";
            string range = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r > 0 ? r.ToString(CultureInfo.InvariantCulture) : "";
            int next = parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 2, 3) : 2;
            _line3 = _left ? $"SAMPLE {next} OF 3 READY" : (range.Length > 0 ? $"TOO CLOSE  -  KEEP {range} M" : "TOO CLOSE TO SAMPLE");
        }

        private static double Fit(string text, double max) => Math.Min(max, 190 / (Math.Max(1, text.Length) * 0.66));

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

            // The arrow walks out through the ring's edge (LEFT) or back in (ENTERED).
            double m = EaseOut(Phase(t, 0.3, 2.4));
            double x = _left ? SampleX + 15 + (Radius + 35) * m : SampleX + Radius + 35 - (Radius + 20) * m, y = SampleY + (_left ? -3 : 3);
            bool inside = Math.Abs(x - SampleX) < Radius;

            if (inside)
            {
                dc.PushClip(Disc);
                for (double i = -Radius * 2; i < Radius * 2; i += 5)
                    dc.DrawLine(HatchPen, new Point(SampleX + i - Radius, SampleY + Radius), new Point(SampleX + i + Radius, SampleY - Radius));
                dc.Pop();
                dc.DrawEllipse(null, AmberRing, new Point(SampleX, SampleY), Radius, Radius);
            }
            else dc.DrawEllipse(null, DashedRing, new Point(SampleX, SampleY), Radius, Radius);

            double cr = Phase(t, _left ? 1.1 : 1.0, 0.8);
            if (cr > 0 && cr < 1)
            {
                double rr = Radius + cr * 14;
                dc.DrawEllipse(null, (_left ? MintRipple : AmberRipple)[Math.Clamp((int)Math.Round((1 - cr) * 0.8 * 8), 0, 8)], new Point(SampleX, SampleY), rr, rr);
            }

            dc.DrawEllipse(AmberBrush, null, new Point(SampleX, SampleY), 4, 4);
            _arrowAt.X = x; _arrowAt.Y = y; dc.PushTransform(_arrowAt);
            dc.DrawGeometry(ArrowBrush, null, Arrow);
            dc.Pop();

            double plateAt = 1.6, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, _left ? MintPlateEdge : AmberPlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _left ? "COLONY RANGE CLEARED" : "INSIDE COLONY RANGE", MonoFace, 12, _left ? MintBrush : AmberBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 10), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _line3, MonoFace, 8, MutedBrush, SceneW / 2, 134, 0.1, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
