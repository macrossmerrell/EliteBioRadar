using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Supercruise assist switching, on the supercruise look: the ship from behind with the stars streaming away. ON: a mint targeting reticle spins in and snaps onto a
    // bright point above the ship, then pings. OFF: the reticle opens up, turns away and fades. About 4.4 seconds. Detail = "ON" or "OFF".
    public sealed class SupercruiseAssistVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.8, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50, TargetX = 120, TargetY = 15, Accel = 0.55;
        private const int Streaks = 60;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(125, 255, 196), Amber = Color.FromRgb(255, 217, 160), Lavender = Color.FromRgb(235, 225, 255);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), MintBrush = Frozen(Mint);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(204, 5, 10, 20)), HullBrush = Frozen(Color.FromRgb(0x2a, 0x3a, 0x46)), CoreBrush = Frozen(Color.FromRgb(255, 248, 235));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(140, Mint.R, Mint.G, Mint.B), 1), HullEdge = FrozenPen(Cyan, 0.87);
        // Nine alpha levels each.
        private static readonly Pen[] CyanStreak = MakeAlphaPens(Cyan, 1.2), WhiteStreak = MakeAlphaPens(Lavender, 1.6), MintBracket = MakeAlphaPens(Mint, 1.6), MintPing = MakeAlphaPens(Mint, 1.4);
        private static readonly Brush[] MintFill = MakeFills(Mint);
        private static readonly Brush HullGlow = MakeGlow(Cyan, 90), NozzleGlow = MakeGlow(Amber, 160);
        private static readonly Geometry Hull = Poly(new[] { (-6.0, -9.0), (6, -9), (9, 6), (-9, 6) }), WingL = Poly(new[] { (-9.0, -1.0), (-24, 8), (-24, 11), (-9, 7) }), WingR = Poly(new[] { (9.0, -1.0), (24, 8), (24, 11), (9, 7) }), Fin = Poly(new[] { (0.0, -9.0), (0, -16), (3, -9) });
        private static readonly Geometry Corner = MakeCorner();
        private static readonly double[] Angle = MakeValues(0, 2 * Math.PI), Start = MakeValues(1, 1), Length = MakeValues(2, 1), Offset = MakeValues(3, 1);

        private static double[] MakeValues(int k, double scale)
        {
            var a = new double[Streaks];
            for (int i = 0; i < Streaks; i++) a[i] = Hash(i * 4 + k) * scale;
            return a;
        }

        private static Pen[] MakeAlphaPens(Color c, double width)
        {
            var a = new Pen[9];
            for (int i = 0; i <= 8; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(235.0 * i / 8), c.R, c.G, c.B), width);
            return a;
        }

        private static Brush[] MakeFills(Color c)
        {
            var a = new Brush[9];
            for (int i = 0; i <= 8; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), c.R, c.G, c.B));
            return a;
        }

        private static Brush MakeGlow(Color c, byte alpha)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0.1));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }

        // One bracket corner, drawn at the origin with its arms pointing back towards the reticle's centre; moved out to (r, r) and turned for the other three.
        private static Geometry MakeCorner()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -5), false, false);
                c.LineTo(new Point(0, 0), true, false);
                c.LineTo(new Point(-5, 0), true, false);
            }
            g.Freeze();
            return g;
        }

        private bool _on = true;
        private double _flow, _lastT;

        protected override void OnStart(string subject, string detail)
        {
            _on = !detail.Contains("OFF", StringComparison.OrdinalIgnoreCase);
            _flow = 0; _lastT = 0;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double p = _on ? EaseOut(Phase(t, 0.3, 1.0)) : 1 - EaseOut(Phase(t, 0.2, 0.9));   // how locked on the reticle is
            _flow += Math.Clamp(t - _lastT, 0, 0.1) * (0.25 + 1.5 * Accel);
            _lastT = t;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            for (int i = 0; i < Streaks; i++)
            {
                double z = (_flow + Offset[i]) % 1.0, z2 = z * z;
                double d0 = 8 + Start[i] * 14 + z2 * 92, d1 = d0 + 2 + Accel * (0.35 + Length[i]) * (6 + z2 * 70);
                double cs = Math.Cos(Angle[i]), sn = Math.Sin(Angle[i]);
                int al = (int)Math.Round(Math.Min(1, z * 4) * 0.8 * 8);
                dc.DrawLine(i % 4 == 0 ? WhiteStreak[al] : CyanStreak[al], new Point(CentreX + cs * d0, CentreY + sn * d0), new Point(CentreX + cs * d1, CentreY + sn * d1));
            }

            // The ship from behind.
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.PushTransform(new ScaleTransform(1.15, 1.15));
            dc.DrawEllipse(HullGlow, null, new Point(0, 2), 30, 30);
            dc.DrawGeometry(HullBrush, HullEdge, Hull);
            dc.DrawGeometry(HullBrush, HullEdge, WingL);
            dc.DrawGeometry(HullBrush, HullEdge, WingR);
            dc.DrawGeometry(HullBrush, HullEdge, Fin);
            double gr = 5 + Accel * 7, core = 1.8 + Accel * 0.9;
            for (int e = -1; e <= 1; e++) dc.DrawEllipse(NozzleGlow, null, new Point(e * 5.5, 3), gr, gr);
            for (int e = -1; e <= 1; e++)
            {
                dc.DrawEllipse(CoreBrush, null, new Point(e * 5.5, 3), core, core);
                dc.DrawEllipse(null, HullEdge, new Point(e * 5.5, 3), 3.2, 3.2);
            }
            dc.Pop(); dc.Pop();

            // The reticle: closes in on the point, turning as it comes (ON), or opens out and turns away (OFF).
            if (p > 0.01)
            {
                int pl = (int)Math.Round(p * 8);
                double r = 10 + (1 - p) * 26, rot = (1 - p) * 0.8 * 180 / Math.PI;
                dc.PushTransform(new TranslateTransform(TargetX, TargetY));
                dc.PushTransform(new RotateTransform(rot));
                for (int c = 0; c < 4; c++)
                {
                    dc.PushTransform(new RotateTransform(c * 90));
                    dc.PushTransform(new TranslateTransform(r, r));
                    dc.DrawGeometry(null, MintBracket[pl], Corner);
                    dc.Pop(); dc.Pop();
                }
                dc.Pop(); dc.Pop();
                double pulse = 1 + 0.25 * Math.Sin(t * 8);
                dc.DrawEllipse(MintFill[pl], null, new Point(TargetX, TargetY), 2.4 * pulse, 2.4 * pulse);
                if (_on)
                {
                    double ping = Phase(t, 1.3, 0.7);
                    if (ping > 0 && ping < 1) dc.DrawEllipse(null, MintPing[(int)Math.Round((1 - ping) * 8)], new Point(TargetX, TargetY), 6 + ping * 30, 6 + ping * 30);
                }
            }

            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, "SUPERCRUISE ASSIST", MonoFace, 13, MintBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _on ? "ENGAGED" : "DISENGAGED", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
