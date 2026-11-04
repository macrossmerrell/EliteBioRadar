using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Interdiction warning: a gravity vortex of red-to-pink streaks spiralling in on the ship (it shudders), alarm bars sweeping down the whole window, red edge flashes, and
    // INTERDICTED in glitching white (red and cyan split, jitter, dark cut-lines through the letters) on a red-tinted plate with EVADE OR THROTTLE DOWN under it.
    // About 3.6 seconds. No detail.
    public sealed class InterdictionVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Red = Color.FromRgb(0xff, 0x46, 0x32);
        private static readonly Brush BarBrush = Frozen(Color.FromArgb(24, Red.R, Red.G, Red.B)), CutBrush = Frozen(Color.FromArgb(217, 40, 6, 4)), HullBrush = Frozen(Color.FromRgb(0xcf, 0xea, 0xea));
        private static readonly Brush RedText = Frozen(Color.FromRgb(0xff, 0x28, 0x28)), CyanText = Frozen(Colors.Cyan), SubBrush = Frozen(Color.FromRgb(0xff, 0xc8, 0xbe));
        private static readonly Pen ShipEdge = FrozenPen(Color.FromArgb(204, Red.R, Red.G, Red.B), 1);
        private static readonly Pen[] Ring = MakeRings();
        private static readonly Pen[,] Streak = MakeStreaks();
        private static readonly Geometry Ship = ShipOutline;

        private static Pen[] MakeRings()
        {
            var a = new Pen[9];
            for (int i = 0; i < 9; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), Red.R, Red.G, Red.B), 1.5);
            return a;
        }

        // Streak colour runs from red-orange at the rim to pink at the centre: four colour steps by nine opacity steps.
        private static Pen[,] MakeStreaks()
        {
            var a = new Pen[4, 9];
            for (int q = 0; q < 4; q++)
                for (int i = 0; i < 9; i++)
                    a[q, i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / 8), 255, (byte)(120 + 100 * q / 3), 140), 1.2);
            return a;
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);

            double ed = LowFuelVisual.Blink(t, 0.3, 10) * Phase(t, 0.3, 0.3);
            if (ed > 0.01) dc.DrawRectangle(LowFuelVisual.Edge[(int)Math.Round(ed * 8)], null, new Rect(0, 0, W, H));

            // Alarm bars: six bands sweeping down the whole window.
            double bh = 10 * k;
            for (int i = 0; i < 6; i++)
            {
                double y = (((t * 70 + i * 28) * k) % (160 * k)) - bh;
                dc.DrawRectangle(BarBrush, null, new Rect(-40, y, W + 80, bh));
            }

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The vortex: streaks spiralling in toward the ship, and rings shrinking onto it.
            for (int i = 0; i < 46; i++)
            {
                double hh = Hash(i), q = (t * 0.45 * (0.6 + hh) + hh) % 1, r = 64 * (1 - q) + 4, an = hh * 6.2832 + q * q * 5;
                double a2 = an - 0.18 * (1 - q * 0.5), r2 = r + 6 * (1 - q);
                var pen = Streak[(int)Math.Min(3, q * 4), (int)Math.Round(0.9 * Math.Min(1, q * 4) * (1 - q * 0.2) * 8)];
                dc.DrawLine(pen, new Point(CentreX + Math.Cos(a2) * r2, CentreY + Math.Sin(a2) * r2 * 0.8), new Point(CentreX + Math.Cos(an) * r, CentreY + Math.Sin(an) * r * 0.8));
            }
            for (int i = 0; i < 3; i++)
            {
                double q = (t * 0.8 + i / 3.0) % 1;
                dc.DrawEllipse(null, Ring[(int)Math.Round(0.35 * (1 - q) * 8)], new Point(CentreX, CentreY), 60 * (1 - q) + 6, 48 * (1 - q) + 5);
            }

            // The ship shudders as it is pulled.
            dc.PushTransform(new TranslateTransform(CentreX + Math.Sin(t * 9) * 1.2 * Phase(t, 0.5, 0.3), CentreY));
            dc.PushTransform(new RotateTransform(Math.Sin(t * 3) * 0.1 * 180 / Math.PI));
            dc.DrawGeometry(HullBrush, ShipEdge, Ship);
            dc.Pop(); dc.Pop();

            // The plate, with the glitching title and the instruction under it.
            dc.DrawRoundedRectangle(LowFuelVisual.CritPlateBrush, LowFuelVisual.CritPlateEdge, new Rect(20, 98, 200, 48), 6, 6);
            double sh = t > 0.2 ? (Hash((int)(t * 30)) - 0.5) * 5 * Math.Max(0, 1 - t / 1.6) : 0, ta = Phase(t, 0.1, 0.2);
            bool jump = Hash((int)(t * 20)) > 0.8 && t < 2;
            const double size = 20, ty = 102;
            DrawSpaced(dc, "INTERDICTED", MonoFace, size, CyanText, CentreX + sh - 2.5, ty, 0.05, 0.55 * ta);
            DrawSpaced(dc, "INTERDICTED", MonoFace, size, RedText, CentreX + sh + 2.5, ty, 0.05, 0.7 * ta);
            DrawSpaced(dc, "INTERDICTED", MonoFace, size, WhiteBrush, CentreX + sh + (jump ? 4 : 0), ty, 0.05, ta);
            if (t < 1.8)
                for (int i = 0; i < 4; i++)
                    if (Hash(i * 7 + (int)(t * 12)) > 0.5)
                        dc.DrawRectangle(CutBrush, null, new Rect(CentreX - 72, ty + 2 + Hash(i + (int)(t * 12)) * size, 144, 2.2));
            DrawSpaced(dc, "EVADE OR THROTTLE DOWN", MonoFace, 11, SubBrush, CentreX, 129, 0.08, Phase(t, 0.9, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
