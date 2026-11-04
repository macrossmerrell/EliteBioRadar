using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // SRV handbrake switching: a "P" badge inside a ring of six arcs, one per wheel. ON (amber): the arcs lock in one by one and a pulse goes out, HANDBRAKE ON with WHEELS LOCKED under it.
    // OFF (cyan): the arcs release one by one, HANDBRAKE OFF with WHEELS FREE under it. About 3.6 seconds. Detail = "ON" or "OFF".
    public sealed class HandbrakeVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b), Idle = Color.FromRgb(60, 80, 110);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Pen CyanPlateEdge = FrozenPen(Color.FromArgb(89, Cyan.R, Cyan.G, Cyan.B), 1), AmberPlateEdge = FrozenPen(Color.FromArgb(89, Amber.R, Amber.G, Amber.B), 1);
        private static readonly Pen CyanRing = FrozenPen(Cyan, 2), AmberRing = FrozenPen(Amber, 2), IdleArc = FrozenPen(Color.FromArgb(204, Idle.R, Idle.G, Idle.B), 3);
        private static readonly Pen CyanArc = FrozenPen(Color.FromArgb(242, Cyan.R, Cyan.G, Cyan.B), 4), AmberArc = FrozenPen(Color.FromArgb(242, Amber.R, Amber.G, Amber.B), 4);
        private static readonly Pen[] CyanPulse = MakePulse(Cyan), AmberPulse = MakePulse(Amber);

        private static Pen[] MakePulse(Color c)
        {
            var a = new Pen[9];
            for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), c.R, c.G, c.B), 2);
            return a;
        }

        private static Geometry Arc(double r, double a0, double a1)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(Math.Cos(a0) * r, Math.Sin(a0) * r), false, false);
                c.ArcTo(new Point(Math.Cos(a1) * r, Math.Sin(a1) * r), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        // Each of the six arcs at its locked radius (34) and at its open radius (40).
        private static readonly Geometry[] Near = MakeArcs(34), Far = MakeArcs(40);

        private static Geometry[] MakeArcs(double r)
        {
            var a = new Geometry[6];
            for (int i = 0; i < 6; i++)
            {
                double a0 = -Math.PI / 2 + i / 6.0 * Math.PI * 2 + 0.12;
                a[i] = Arc(r, a0, a0 + Math.PI * 2 / 6 - 0.24);
            }
            return a;
        }

        private bool _on = true;

        protected override void OnStart(string subject, string detail) => _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var ring = _on ? AmberRing : CyanRing;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // Six wheel arcs: each snaps in (locked, bright, at the locked radius) or lets go (idle, out at the open radius) in turn.
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            for (int i = 0; i < 6; i++)
            {
                double kk = Phase(t, 0.2 + i * 0.12, 0.3);
                bool locked = _on ? kk >= 0.5 : kk < 0.5;
                dc.DrawGeometry(null, locked ? (_on ? AmberArc : CyanArc) : IdleArc, (_on ? kk <= 0 : kk >= 0.5) ? Far[i] : Near[i]);
            }
            dc.DrawEllipse(null, ring, new Point(0, 0), 22, 22);
            dc.Pop();

            DrawSpaced(dc, "P", MonoFace, 28, _on ? AmberBrush : CyanBrush, CentreX, CentreY - 17, 0, 1);

            if (_on)
            {
                double q = Phase(t, 1.0, 0.7);
                if (q > 0 && q < 1) dc.DrawEllipse(null, AmberPulse[(int)Math.Round((1 - q) * 8)], new Point(CentreX, CentreY), 38 + q * 22, 38 + q * 22);
            }

            dc.DrawRoundedRectangle(PlateBrush, _on ? AmberPlateEdge : CyanPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _on ? "HANDBRAKE ON" : "HANDBRAKE OFF", MonoFace, 13, _on ? AmberBrush : CyanBrush, SceneW / 2, 112, 0.1, 1);
            DrawSpaced(dc, _on ? "WHEELS LOCKED" : "WHEELS FREE", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
