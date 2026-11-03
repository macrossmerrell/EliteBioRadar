using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // SRV drive assist switching: a top-down six-wheeled SRV between two guide lines. ON (cyan): the wobbling lines straighten and lock, the sway stops and a pulse ring goes out.
    // OFF (amber): the lines break up and the SRV drifts and sways. About 3.6 seconds. Detail = "ON" or "OFF".
    public sealed class DriveAssistVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), BodyBrush = Frozen(Color.FromRgb(176, 206, 206)), GlassBrush = Frozen(Color.FromRgb(22, 52, 60));
        private static readonly Brush WheelBrush = Frozen(Color.FromRgb(34, 46, 46)), EngineBrush = Frozen(Color.FromRgb(110, 140, 140)), LampBrush = Frozen(Color.FromRgb(255, 240, 200));
        private static readonly Pen GlintPen = FrozenPen(Color.FromArgb(115, 255, 255, 255), 1);
        private static readonly Look CyanLook = new(Cyan), AmberLook = new(Amber);
        private static readonly Geometry BodyShape = Poly(new[] { (-5.0, -23.0), (5, -23), (9, -13), (10, 10), (8, 23), (-8, 23), (-10, 10), (-9, -13) });
        private static readonly Geometry CanopyShape = Poly(new[] { (-4.0, -18.0), (4, -18), (6.5, -7), (-6.5, -7) });
        private static readonly (double s, double y)[] WheelSpots = { (-1, -14), (1, -14), (-1, 0), (1, 0), (-1, 14), (1, 14) };

        // The pens one state (cyan or amber) draws the SRV and guide lines with.
        private sealed class Look
        {
            public readonly Pen Arm, Edge, Tread, Lane, Vent, Turret, PlateEdge;
            public readonly Pen[] Pulse = new Pen[9];
            public Look(Color c)
            {
                Arm = FrozenPen(Color.FromArgb(230, c.R, c.G, c.B), 2);
                Edge = FrozenPen(Color.FromArgb(242, c.R, c.G, c.B), 1);
                Tread = FrozenPen(Color.FromArgb(140, c.R, c.G, c.B), 1);
                Lane = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1.5);
                Vent = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
                Turret = FrozenPen(Color.FromArgb(230, c.R, c.G, c.B), 1.5);
                PlateEdge = FrozenPen(Color.FromArgb(89, c.R, c.G, c.B), 1);
                for (int i = 0; i < 9; i++) Pulse[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), c.R, c.G, c.B), 2);
            }
        }

        private static Geometry Poly((double x, double y)[] pts)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = new Point(pts[i].x, pts[i].y);
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, false);
                }
            g.Freeze();
            return g;
        }

        private static double Hash(int i) { double s = Math.Sin(i * 127.1) * 43758.5453; return s - Math.Floor(s); }

        private bool _on = true;

        protected override void OnStart(string subject, string detail) => _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);

        private static void DrawSrv(DrawingContext dc, Look l)
        {
            foreach (var (s, y) in WheelSpots)
            {
                double x = s * 19;
                dc.DrawLine(l.Arm, new Point(s * 9, y), new Point(s * 15, y));
                dc.DrawRoundedRectangle(WheelBrush, l.Edge, new Rect(x - 4.5, y - 7, 9, 14), 2, 2);
                for (int k = -5; k <= 5; k += 3) dc.DrawLine(l.Tread, new Point(x - 4.5, y + k), new Point(x + 4.5, y + k));
            }
            dc.DrawGeometry(BodyBrush, l.Edge, BodyShape);
            dc.DrawGeometry(GlassBrush, l.Edge, CanopyShape);
            dc.DrawLine(GlintPen, new Point(-2.5, -16), new Point(-4, -9));
            dc.DrawRoundedRectangle(EngineBrush, l.Edge, new Rect(-6, 10, 12, 10), 2, 2);
            foreach (double vy in new[] { 13.0, 16, 19 }) dc.DrawLine(l.Vent, new Point(-4.5, vy), new Point(4.5, vy));
            dc.DrawEllipse(EngineBrush, l.Edge, new Point(0, -1), 4.2, 4.2);
            dc.DrawLine(l.Turret, new Point(-1.5, -1), new Point(-1.5, -8));
            dc.DrawLine(l.Turret, new Point(1.5, -1), new Point(1.5, -8));
            dc.DrawRectangle(LampBrush, null, new Rect(-4.5, -23, 2.5, 1.6));
            dc.DrawRectangle(LampBrush, null, new Rect(2, -23, 2.5, 1.6));
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var look = _on ? CyanLook : AmberLook;
            double p = EaseOut(Phase(t, 0.2, 1.2));

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The guide lines: wobbling at first and settling straight (on), or breaking up (off).
            for (int side = -1; side <= 1; side += 2)
            {
                var lane = new StreamGeometry();
                using (var c = lane.Open())
                    for (int y = 6; y <= 98; y += 3)
                    {
                        double wob = _on ? (1 - p) * Math.Sin(y * 0.2 + t * 9) * 8 : p * Math.Sin(y * 0.12 + t * 4 + side) * 10 * (0.4 + 0.6 * Hash(y / 12));
                        var pt = new Point(CentreX + side * 38 + wob, y);
                        if (y == 6) c.BeginFigure(pt, false, false); else c.LineTo(pt, true, false);
                    }
                lane.Freeze();
                dc.DrawGeometry(null, look.Lane, lane);
            }

            double sway = _on ? (1 - p) * Math.Sin(t * 6) * 0.2 : p * Math.Sin(t * 3.3) * 0.28, drift = _on ? 0 : p * Math.Sin(t * 2.1) * 10;
            dc.PushTransform(new TranslateTransform(CentreX + drift, CentreY));
            dc.PushTransform(new RotateTransform(sway * 180 / Math.PI));
            DrawSrv(dc, look);
            dc.Pop(); dc.Pop();

            if (_on && p >= 1)
            {
                double q = Phase(t, 1.4, 0.6), r = 28 + q * 28;
                if (q < 1) dc.DrawEllipse(null, look.Pulse[(int)Math.Round((1 - q) * 8)], new Point(CentreX, CentreY), r, r);
            }

            dc.DrawRoundedRectangle(PlateBrush, look.PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _on ? "DRIVE ASSIST ON" : "DRIVE ASSIST OFF", MonoFace, 13, _on ? CyanBrush : AmberBrush, SceneW / 2, 112, 0.1, _on ? 1 : 0.6 + 0.4 * Math.Sin(t * 14));
            DrawSpaced(dc, _on ? "SRV STABILISED" : "SRV MANUAL", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.2, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

    }
}
