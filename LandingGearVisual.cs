using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The landing gear moving: a hazard band with chevrons streaming down (gear coming down) or up (gear coming in) that brake to a stop, a thud as it locks, and the plate's
    // title turning from DEPLOYING / RETRACTING to GEAR DEPLOYED / GEAR RETRACTED in green with a LOCKED stamp. About 4 seconds. Detail = "DOWN" or "UP".
    public sealed class LandingGearVisual : AnimationVisual
    {
        private const double LockAt = 2.0, FadeOutAt = 3.4;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Brush[] BandFill = MakeBand();
        private static readonly Brush Working = Frozen(Color.FromRgb(0xcf, 0xea, 0xea));
        private static readonly Pen[] Edge = MakeEdge();
        private static readonly Pen[] Down = MakeChevrons(Color.FromRgb(0x55, 0xf2, 0xff)), Up = MakeChevrons(Color.FromRgb(0xf2, 0xb8, 0x4b));
        private static readonly double[] Columns = { 0.053, 0.14, 0.86, 0.947 };

        private static Brush[] MakeBand() { var a = new Brush[33]; for (int i = 0; i < a.Length; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round(255.0 * i / 32), 0x1a, 0x16, 0x08)); return a; }
        private static Pen[] MakeEdge() { var a = new Pen[9]; for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)(i * 32 - (i == 8 ? 1 : 0)), 0xf2, 0xb8, 0x4b), 1.5); return a; }
        private static Pen[] MakeChevrons(Color c)
        {
            var a = new Pen[6];
            for (int i = 0; i < a.Length; i++) { var p = new Pen(Frozen(Color.FromArgb((byte)(i * 51), c.R, c.G, c.B)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); a[i] = p; }
            return a;
        }

        private bool _down = true;

        protected override void OnStart(string subject, string detail) => _down = !detail.Contains("UP", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double cx = W / 2, cy = H / 2, a = Clamp01(t / 0.2) * fadeOut, halfH = 23, halfW = Math.Min(W * 0.27, 85);
            bool locked = t >= LockAt;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            // The thud when the gear locks: the whole band jolts and settles.
            double th = locked ? 3 * Math.Exp(-(t - LockAt) * 9) * Math.Cos((t - LockAt) * 40) : 0;
            dc.PushTransform(new TranslateTransform(0, th));

            var band = new Rect(-40, cy - halfH, W + 80, halfH * 2);
            dc.DrawRectangle(Level(BandFill, 0.9 * a), null, band);

            // Chevrons rush in, then brake (150 px over 1.8 s, easing out) and fade as the gear locks.
            double dir = _down ? 1 : -1, shift = dir * 150 * EaseOut(Phase(t, 0.2, 1.8)), o = Phase(t, 0, 0.2) * (1 - Phase(t, LockAt, 0.3)) * fadeOut;
            if (o > 0.01)
            {
                var pens = _down ? Down : Up;
                dc.PushClip(new RectangleGeometry(band));
                foreach (double col in Columns)
                    for (int k = 0; k < 3; k++)
                    {
                        double off = (((k * 16 + shift) % 48) + 48) % 48, y = cy - halfH + off, f = Math.Sin(Math.PI * off / 48) * 0.9 * o;
                        int lvl = (int)Math.Round(Clamp01(f) * 5);
                        if (lvl == 0) continue;
                        double x = col * W, tip = y + 4 * dir, wing = y - 4 * dir;
                        var pen = pens[lvl];
                        dc.DrawLine(pen, new Point(x - 8, wing), new Point(x, tip));
                        dc.DrawLine(pen, new Point(x, tip), new Point(x + 8, wing));
                    }
                dc.Pop();
            }

            // The plate, with edge lines across the whole width. The edges stay amber through the thud; only the title turns green.
            var plate = new Rect(cx - halfW, cy - halfH - 2, halfW * 2, halfH * 2 + 4);
            dc.DrawRectangle(Level(Backdrops, a), null, plate);
            var edge = Fade(Edge, a);
            dc.DrawRectangle(null, edge, plate);
            dc.DrawLine(edge, new Point(-40, cy - halfH), new Point(W + 80, cy - halfH));
            dc.DrawLine(edge, new Point(-40, cy + halfH), new Point(W + 80, cy + halfH));

            string title = locked ? (_down ? "GEAR DEPLOYED" : "GEAR RETRACTED") : (_down ? "DEPLOYING" : "RETRACTING");
            DrawSpaced(dc, title, MonoFace, 13, locked ? GreenBrush : Working, cx, cy - 17, 0.15, a);
            if (locked) DrawSpaced(dc, "LOCKED", MonoFace, 10, GreenBrush, cx, cy + 4, 0.3, a);
            dc.Pop();   // thud
            dc.Pop();   // scale
        }

        private static Pen Fade(Pen[] set, double alpha) => set[(int)Math.Round(Clamp01(alpha) * 8)];
    }
}
