using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Flight Assist switching: a gyro ring of four arcs around the ship. Assist ON: the arcs drift together and lock into one ring (cyan, ticks at the compass points, a lock
    // pulse) and the ship settles. Assist OFF: the ring shatters, the arcs drift apart and spin separately (amber) and the ship wanders. About 3.6 seconds.
    // Detail = "ON" or "OFF".
    public sealed class FlightAssistVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, Radius = 38;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), HullBrush = Frozen(Color.FromRgb(0x1c, 0x3a, 0x3a));
        private static readonly Pen CyanArc = RoundPen(Cyan, 3), AmberArc = RoundPen(Amber, 3), CyanShip = FrozenPen(Cyan, 1.5), AmberShip = FrozenPen(Amber, 1.5), CyanTick = FrozenPen(Cyan, 2);
        private static readonly Pen[] Pulse = MakePulse();
        private static readonly Geometry Ship = MakeShip();

        private static Pen RoundPen(Color c, double w) { var p = new Pen(Frozen(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }
        private static Pen[] MakePulse() { var a = new Pen[9]; for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(230.0 * i / 8), Cyan.R, Cyan.G, Cyan.B), 2); return a; }

        private static Geometry MakeShip()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -13), true, true);
                c.LineTo(new Point(10, 11), true, false);
                c.LineTo(new Point(0, 5), true, false);
                c.LineTo(new Point(-10, 11), true, false);
            }
            g.Freeze();
            return g;
        }

        private bool _on = true;

        protected override void OnStart(string subject, string detail) => _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, cx = W / 2, cy = H / 2 - 12;
            const double lockAt = 1.4;

            // How broken the ring is: coming on, it closes up and locks; going off, it shatters fast then keeps drifting.
            double loose = _on ? 1 - EaseOut(Phase(t, 0.2, 1.2)) : EaseOut(Phase(t, 0.15, 0.7));
            bool locked = _on && t >= lockAt;
            Pen arcPen = _on ? CyanArc : AmberArc, shipPen = _on ? CyanShip : AmberShip;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);

            // The four arcs: a gap that opens as the ring loosens, a spin that grows with it, and each arc breathing in and out on its own.
            var arcs = new StreamGeometry();
            using (var c = arcs.Open())
                for (int i = 0; i < 4; i++)
                {
                    double r = Radius + loose * 8 * Math.Sin(t * 1.1 + i * 1.7), spin = loose * t * (i % 2 == 0 ? -0.9 : 0.9), gap = 0.06 + loose * 0.35;
                    double a0 = i * Math.PI / 2 + spin + gap / 2, a1 = (i + 1) * Math.PI / 2 + spin - gap / 2;
                    c.BeginFigure(new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r), false, false);
                    c.ArcTo(new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
            arcs.Freeze();
            dc.DrawGeometry(null, arcPen, arcs);

            if (locked)
            {
                var ticks = new StreamGeometry();
                using (var c = ticks.Open())
                    for (int i = 0; i < 4; i++)
                    {
                        double an = i * Math.PI / 2;
                        c.BeginFigure(new Point(cx + Math.Cos(an) * 44, cy + Math.Sin(an) * 44), false, false);
                        c.LineTo(new Point(cx + Math.Cos(an) * 50, cy + Math.Sin(an) * 50), true, false);
                    }
                ticks.Freeze();
                dc.DrawGeometry(null, CyanTick, ticks);

                double p = Phase(t, lockAt, 0.7);
                if (p > 0 && p < 1) dc.DrawEllipse(null, Pulse[(int)Math.Round((1 - p) * 8)], new Point(cx, cy), Radius + 34 * EaseOut(p), Radius + 34 * EaseOut(p));
            }

            // The ship: still when the assist holds it, wandering and rocking when it doesn't.
            dc.PushTransform(new TranslateTransform(cx + loose * 14 * Math.Sin(t * 0.8), cy + loose * 5 * Math.Sin(t * 1.3)));
            dc.PushTransform(new RotateTransform(loose * 16 * Math.Sin(t * 0.9)));
            dc.DrawGeometry(HullBrush, shipPen, Ship);
            dc.Pop(); dc.Pop();

            DrawSpaced(dc, _on ? "FLIGHT ASSIST ON" : "FLIGHT ASSIST OFF", MonoFace, 13, _on ? CyanBrush : AmberBrush, cx, cy + 62, 0.15, Phase(t, _on ? 1.2 : 0.8, 0.4));
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
