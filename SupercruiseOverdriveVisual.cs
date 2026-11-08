using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Supercruise overdrive switching, on the supercruise look: the ship from behind with the stars streaming away from it. ON: the stars stretch into long streaks and shift from cyan to
    // violet, a ring pulses out, the engines flare, and the three engines flare together.
    // OFF: a ring draws back in to the ship, the streaks shorten and go back to cyan and the engines settle. About 3.6 seconds. Detail = "ON" or "OFF".
    public sealed class SupercruiseOverdriveVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50;
        private const int Streaks = 60;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Violet = Color.FromRgb(196, 160, 255), Amber = Color.FromRgb(255, 217, 160), Lavender = Color.FromRgb(235, 225, 255);
        private static readonly Brush CyanBrush = Frozen(Cyan), VioletBrush = Frozen(Violet), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(204, 5, 10, 20)), HullBrush = Frozen(Color.FromRgb(0x2a, 0x3a, 0x46)), CoreBrush = Frozen(Color.FromRgb(255, 248, 235));
        private static readonly Pen CyanPlateEdge = FrozenPen(Color.FromArgb(140, Cyan.R, Cyan.G, Cyan.B), 1), VioletPlateEdge = FrozenPen(Color.FromArgb(140, Violet.R, Violet.G, Violet.B), 1);
        // Everything that changes colour with the overdrive comes in nine levels, from cyan (0) to violet (8).
        private static readonly Pen[] HullEdge = MakeEdges(0.87), WhiteStreak = AlphaPens(Lavender, 1.6), RingOut = AlphaPens(Violet, 1.8), RingIn = AlphaPens(Cyan, 1.8);
        private static readonly Brush[] HullGlow = MakeGlows(i => Mix(Cyan, Violet, i / 8.0), 77, 140), NozzleGlow = MakeGlows(i => Mix(Amber, Lavender, i / 8.0), 110, 190);
        // The ship seen from behind (the same drawing as the supercruise screen's).
        private static readonly double[] Angle = StarValues(Streaks, 0, 2 * Math.PI), Start = StarValues(Streaks, 1, 1), Length = StarValues(Streaks, 2, 1), Offset = StarValues(Streaks, 3, 1);

        private static Pen[] MakeEdges(double width)
        {
            var a = new Pen[9];
            for (int i = 0; i <= 8; i++) a[i] = FrozenPen(Mix(Cyan, Violet, i / 8.0), width);
            return a;
        }

        // Nine radial glows: the colour comes from `colour(level)`, the alpha rises from `from` to `to`.
        private static Brush[] MakeGlows(Func<int, Color> colour, int from, int to)
        {
            var a = new Brush[9];
            for (int i = 0; i <= 8; i++)
            {
                var c = colour(i);
                var b = new RadialGradientBrush();
                b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(from + (to - from) * i / 8.0), c.R, c.G, c.B), 0.1));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                b.Freeze();
                a[i] = b;
            }
            return a;
        }

        // Streak pens by colour level then alpha level, built once on first use (the alpha-only sets above cover the white and the plain cyan ones).
        private static readonly Pen[][] ColourStreak = MakeColourStreaks();
        private static Pen[][] MakeColourStreaks()
        {
            var a = new Pen[9][];
            for (int c = 0; c <= 8; c++)
            {
                a[c] = new Pen[9];
                var col = Mix(Cyan, Violet, c / 8.0);
                for (int i = 0; i <= 8; i++) a[c][i] = FrozenPen(Color.FromArgb((byte)Math.Round(235.0 * i / 8), col.R, col.G, col.B), 1.2);
            }
            return a;
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
            double p = EaseOut(Phase(t, 0.2, 1.5)), L = _on ? p : 1 - p;   // 0 = plain supercruise, 1 = overdrive
            int lv = (int)Math.Round(L * 8);
            double accel = 0.55 + 0.45 * L, stretch = 1 + 2.2 * L;
            _flow += Math.Clamp(t - _lastT, 0, 0.1) * (0.25 + 1.5 * accel);
            _lastT = t;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The stars: longer the further into overdrive, from cyan towards violet.
            for (int i = 0; i < Streaks; i++)
            {
                double z = (_flow + Offset[i]) % 1.0, z2 = z * z;
                double d0 = 8 + Start[i] * 14 + z2 * 92, d1 = d0 + 2 + accel * stretch * (0.35 + Length[i]) * (6 + z2 * 70);
                double cs = Math.Cos(Angle[i]), sn = Math.Sin(Angle[i]);
                int al = (int)Math.Round(Math.Min(1, z * 4) * Math.Min(1, 0.35 + accel) * 8);
                dc.DrawLine(i % 4 == 0 ? WhiteStreak[al] : ColourStreak[lv][al], new Point(CentreX + cs * d0, CentreY + sn * d0), new Point(CentreX + cs * d1, CentreY + sn * d1));
            }

            // The ring: out from the ship as the overdrive engages (violet), drawn back in to it as it lets go (cyan).
            if (_on)
            {
                double rp = Phase(t, 0.3, 0.9);
                if (rp > 0 && rp < 1) dc.DrawEllipse(null, RingOut[(int)Math.Round((1 - rp) * 8)], new Point(CentreX, CentreY), 14 + rp * 90, 14 + rp * 90);
            }
            else
            {
                double rp = Phase(t, 0.2, 0.9);
                if (rp > 0 && rp < 1) dc.DrawEllipse(null, RingIn[(int)Math.Round(Math.Sin(rp * Math.PI) * 8)], new Point(CentreX, CentreY), 90 - rp * 76, 90 - rp * 76);
            }

            // The ship from behind.
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.PushTransform(new ScaleTransform(1.15, 1.15));
            dc.DrawEllipse(HullGlow[lv], null, new Point(0, 2), 22 + accel * 10 + L * 8, 22 + accel * 10 + L * 8);
            dc.DrawGeometry(HullBrush, HullEdge[lv], RearHull);
            dc.DrawGeometry(HullBrush, HullEdge[lv], RearWingL);
            dc.DrawGeometry(HullBrush, HullEdge[lv], RearWingR);
            dc.DrawGeometry(HullBrush, HullEdge[lv], RearFin);
            // Three identical engines. All the glows are drawn first and the cores and rings after, so no engine's glow washes over another.
            double gr = 5 + accel * 7 + L * 2, core = 1.8 + accel * 0.9 + L * 0.3;
            for (int e = -1; e <= 1; e++) dc.DrawEllipse(NozzleGlow[lv], null, new Point(e * 5.5, 3), gr, gr);
            for (int e = -1; e <= 1; e++)
            {
                dc.DrawEllipse(CoreBrush, null, new Point(e * 5.5, 3), core, core);
                dc.DrawEllipse(null, HullEdge[lv], new Point(e * 5.5, 3), 3.2, 3.2);
            }
            dc.Pop(); dc.Pop();

            dc.DrawRoundedRectangle(PlateBrush, _on ? VioletPlateEdge : CyanPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, "OVERDRIVE", MonoFace, 13, _on ? VioletBrush : CyanBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _on ? "ENGAGED" : "DISENGAGED", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
