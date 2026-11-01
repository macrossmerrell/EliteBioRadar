using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The cargo scoop going out or coming in: a platform lowers straight down from the hull on two rails inside a growing shaft of light, lands on the ground with a ring
    // pulse (amber), and rises again on retract (cyan title). About 3.4 seconds. Detail = "OUT" (deployed) or "IN" (retracted).
    public sealed class CargoScoopVisual : AnimationVisual
    {
        private const double FadeOutAt = 2.8;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Amber = Color.FromRgb(0xf2, 0xb8, 0x4b), Cyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Brush AmberBrush = Frozen(Amber), CyanBrush = Frozen(Cyan), HullBrush = Frozen(Color.FromRgb(0x1c, 0x3a, 0x3a)), ShaftBrush = Frozen(Color.FromArgb(56, Amber.R, Amber.G, Amber.B));
        private static readonly Pen EdgePen = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1.5);
        private static readonly Pen AmberRail = FrozenPen(Amber, 2.5), CyanRail = FrozenPen(Cyan, 2.5), AmberPlat = FrozenPen(Amber, 2), CyanPlat = FrozenPen(Cyan, 2);
        private static readonly Pen[] Ring = MakeRing(Amber);

        private static Pen[] MakeRing(Color c) { var a = new Pen[9]; for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(230.0 * i / 8), c.R, c.G, c.B), 1.5); return a; }
        private static double OverShoot(double x) { x = Clamp01(x); const double c = 1.3; return 1 + (c + 1) * Math.Pow(x - 1, 3) + c * Math.Pow(x - 1, 2); }

        private bool _out = true;

        protected override void OnStart(string subject, string detail) => _out = !detail.Contains("IN", StringComparison.Ordinal);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, cx = W / 2, oy = H / 2 - 85;
            Brush hot = _out ? AmberBrush : CyanBrush;
            Pen rail = _out ? AmberRail : CyanRail, plat = _out ? AmberPlat : CyanPlat;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            // The hull's underside above, the ground below.
            dc.DrawRoundedRectangle(HullBrush, EdgePen, new Rect(cx - 60, oy + 20, 120, 30), 5, 5);
            double gl = Math.Min(110, W / 2 - 10);
            dc.DrawLine(EdgePen, new Point(cx - gl, oy + 112), new Point(cx + gl, oy + 112));

            // The platform: lowers with a small overshoot as it lands, rises smoothly on retract.
            double e = _out ? OverShoot(Phase(t, 0.3, 1.2)) : 1 - EaseOut(Phase(t, 0.3, 1.1));
            double py = oy + 50 + 54 * Math.Min(1.05, e);
            if (py > oy + 51) dc.DrawRectangle(ShaftBrush, null, new Rect(cx - 32, oy + 50, 64, py - oy - 50));
            dc.DrawLine(rail, new Point(cx - 32, oy + 50), new Point(cx - 32, py));
            dc.DrawLine(rail, new Point(cx + 32, oy + 50), new Point(cx + 32, py));
            dc.DrawRoundedRectangle(HullBrush, plat, new Rect(cx - 44, py, 88, 7), 2, 2);

            // Touching down: a ring pulses out across the ground.
            double r = _out ? Phase(t, 1.4, 0.6) : 0;
            if (r > 0 && r < 1)
            {
                double k = EaseOut(r);
                dc.DrawEllipse(null, Ring[(int)Math.Round((1 - r) * 8)], new Point(cx, oy + 112), 10 + 34 * k, 3 + 6 * k);
            }

            DrawSpaced(dc, _out ? "CARGO SCOOP DEPLOYED" : "CARGO SCOOP RETRACTED", MonoFace, 13, hot, cx, oy + 136, 0.15, a * Phase(t, 1.5, 0.4));
            dc.Pop();
        }
    }
}
