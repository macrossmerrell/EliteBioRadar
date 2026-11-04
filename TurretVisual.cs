using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // SRV turret deploying / retracting: a front view of the SRV (studded tyres, headlamps). DEPLOYED (cyan): the post lifts out of the roof, the crossbar of twin guns opens out,
    // swings left and right once to scan and settles forward. RETRACTED (amber): the guns fold down and the post sinks into the roof.
    // SRV TURRET sits at the top of the scene. About 3.6 seconds. Detail = "ON" (deployed) or "OFF" (retracted).
    public sealed class TurretVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 66;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), TyreBrush = Frozen(Color.FromRgb(34, 46, 46)), BodyBrush = Frozen(Color.FromRgb(42, 52, 56));
        private static readonly Brush GlassBrush = Frozen(Color.FromRgb(22, 52, 60)), HeadBrush = Frozen(Color.FromRgb(110, 140, 140)), TipBrush = Frozen(Color.FromRgb(136, 170, 170));
        private static readonly Brush LampBrush = Frozen(Color.FromArgb(230, 255, 225, 150));
        private static readonly Look CyanLook = new(Cyan), AmberLook = new(Amber);
        private static readonly Geometry BodyShape = Poly(new[] { (-26.0, -6.0), (-18, -16), (18, -16), (26, -6), (28, 14), (-28, 14) });

        // The pens and stud brush one state (cyan or amber) draws the SRV with.
        private sealed class Look
        {
            public readonly Pen Edge, Arm, Bar, PlateEdge;
            public readonly Brush Stud;
            public readonly Brush[] Flash = new Brush[9];
            public Look(Color c)
            {
                Edge = FrozenPen(Color.FromArgb(230, c.R, c.G, c.B), 1);
                Arm = FrozenPen(Color.FromArgb(179, c.R, c.G, c.B), 2);
                Bar = FrozenPen(Color.FromArgb(242, c.R, c.G, c.B), 2);
                PlateEdge = FrozenPen(Color.FromArgb(89, c.R, c.G, c.B), 1);
                Stud = Frozen(Color.FromArgb(115, c.R, c.G, c.B));
                for (int i = 0; i < 9; i++) Flash[i] = Frozen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), 255, 240, 200));
            }
        }

        private bool _on = true;

        protected override void OnStart(string subject, string detail) => _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);

        private static void DrawTyre(DrawingContext dc, Look l, double x, double y)
        {
            dc.DrawRoundedRectangle(TyreBrush, l.Edge, new Rect(x - 8, y - 13, 16, 26), 5, 5);
            for (int i = -9; i <= 9; i += 6)
            {
                dc.DrawRectangle(l.Stud, null, new Rect(x - 8, y + i, 3, 2));
                dc.DrawRectangle(l.Stud, null, new Rect(x + 5, y + i, 3, 2));
            }
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var look = _on ? CyanLook : AmberLook;
            double p = EaseOut(Phase(t, 0.2, 0.8)), e = _on ? p : 1 - p;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            for (int s = -1; s <= 1; s += 2)
            {
                DrawTyre(dc, look, s * 40, 16);
                DrawTyre(dc, look, s * 52, 12);
                dc.DrawLine(look.Arm, new Point(s * 14, 8), new Point(s * 40, 14));
            }
            dc.DrawGeometry(BodyBrush, look.Edge, BodyShape);
            dc.DrawRoundedRectangle(GlassBrush, look.Edge, new Rect(-16, -12, 32, 12), 3, 3);
            dc.DrawEllipse(LampBrush, null, new Point(-22, 8), 2.2, 2.2);
            dc.DrawEllipse(LampBrush, null, new Point(22, 8), 2.2, 2.2);

            // The post, the gun head on top of it and the crossbar of twin guns (folded down while it retracts, swinging once to scan after it deploys).
            double rise = e * 18, sw = 0;
            if (_on) { double q = Phase(t, 1.0, 1.2); sw = Math.Sin(q * Math.PI * 2) * 0.5 * (1 - q * 0.6); }
            double fold = (1 - e) * 1.3, reach = (4 + e * 9) * Math.Cos(sw);
            dc.DrawLine(look.Bar, new Point(0, -16), new Point(0, -16 - rise));
            dc.PushTransform(new TranslateTransform(0, -16 - rise));
            dc.DrawRoundedRectangle(HeadBrush, look.Edge, new Rect(-4, -3, 8, 5), 1, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                dc.PushTransform(new RotateTransform(s * fold * 180 / Math.PI, 0, -2));
                dc.DrawLine(look.Bar, new Point(0, -2), new Point(s * reach, -2));
                dc.DrawRectangle(TipBrush, null, new Rect(s * reach - 1, -3.5, 2, 3));
                dc.Pop();
            }
            dc.Pop();

            if (_on && t > 0.9 && t < 1.2)
            {
                var flash = look.Flash[(int)Math.Round((1 - Phase(t, 0.9, 0.3)) * 8)];
                dc.DrawEllipse(flash, null, new Point(13, -36), 2.5, 2.5);
                dc.DrawEllipse(flash, null, new Point(-13, -36), 2.5, 2.5);
            }
            dc.Pop();

            DrawSpaced(dc, "SRV TURRET", MonoFace, 12, _on ? CyanBrush : AmberBrush, SceneW / 2, 4, 0.25, 0.85 * Phase(t, 0.1, 0.3));

            dc.DrawRoundedRectangle(PlateBrush, look.PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _on ? "DEPLOYED" : "RETRACTED", MonoFace, 13, _on ? CyanBrush : AmberBrush, SceneW / 2, 112, 0.1, 1);
            DrawSpaced(dc, _on ? "WEAPONS READY" : "WEAPONS STOWED", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
