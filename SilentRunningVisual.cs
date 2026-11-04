using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Silent running switching: a ring of eight system lights round the ship. ON: the lights go out one by one, the ship's glow dies and it fades to a dim silhouette, the view
    // darkens a little (blue). OFF: the lights come back one by one in cyan, the glow returns, the view clears. About 3.6 seconds. Detail = "ON" or "OFF".
    public sealed class SilentRunningVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50, RingR = 36;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Blue = Color.FromRgb(0x78, 0x96, 0xff), Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Pale = Color.FromRgb(0xcf, 0xea, 0xea);
        private static readonly Brush BlueBrush = Frozen(Blue), CyanBrush = Frozen(Cyan), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(204, 5, 10, 20)), PipOff = Frozen(Color.FromRgb(40, 60, 90));
        private static readonly Pen BluePlateEdge = FrozenPen(Color.FromArgb(128, Blue.R, Blue.G, Blue.B), 1), CyanPlateEdge = FrozenPen(Color.FromArgb(128, Cyan.R, Cyan.G, Cyan.B), 1);
        private static readonly Brush[] HullLevel = MakeLevels(Pale, 255, 16), Dim = MakeLevels(Color.FromRgb(2, 4, 14), 115, 9), Glow = MakeGlow();
        private static readonly Pen[] BlueEdge = MakePens(Blue), CyanEdge = MakePens(Cyan);
        private static readonly Geometry Ship = ShipOutline;

        private static Brush[] MakeLevels(Color c, int max, int steps)
        {
            var a = new Brush[steps + 1];
            for (int i = 0; i <= steps; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round((double)max * i / steps), c.R, c.G, c.B));
            return a;
        }

        private static Pen[] MakePens(Color c)
        {
            var a = new Pen[17];
            for (int i = 0; i <= 16; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(230.0 * i / 16), c.R, c.G, c.B), 1);
            return a;
        }

        private static Brush[] MakeGlow()
        {
            var a = new Brush[9];
            for (int i = 0; i <= 8; i++)
            {
                var b = new RadialGradientBrush();
                b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(90.0 * i / 8), Cyan.R, Cyan.G, Cyan.B), 0.1));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), 1));
                b.Freeze();
                a[i] = b;
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
            double p = EaseOut(Phase(t, 0.2, 1.6)), lvl = _on ? 1 - p : p;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The ship's glow, fading with the power.
            dc.DrawEllipse(Glow[(int)Math.Round(lvl * 8)], null, new Point(CentreX, CentreY), 34, 34);

            // Eight system lights: each goes out (or comes back) in turn.
            for (int i = 0; i < 8; i++)
            {
                double an = -Math.PI / 2 + i / 8.0 * Math.PI * 2, kk = Phase(t, 0.2 + i * 0.18, 0.25);
                bool lit = _on ? kk < 0.5 : kk >= 0.5;
                dc.DrawEllipse(lit ? CyanBrush : PipOff, null, new Point(CentreX + Math.Cos(an) * RingR, CentreY + Math.Sin(an) * RingR), 3.5, 3.5);
            }

            double shipAlpha = 0.25 + 0.75 * lvl;
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.DrawGeometry(HullLevel[(int)Math.Round(shipAlpha * 16)], (_on ? BlueEdge : CyanEdge)[(int)Math.Round(shipAlpha * 16)], Ship);
            dc.Pop();
            dc.Pop(); dc.Pop();

            // The view darkens as the systems go (and clears as they return).
            double dim = _on ? p : 1 - p;
            if (dim > 0.02) dc.DrawRectangle(Dim[(int)Math.Round(dim * 9)], null, new Rect(0, 0, W, H));

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRoundedRectangle(PlateBrush, _on ? BluePlateEdge : CyanPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _on ? "SILENT RUNNING" : "SILENT RUNNING OFF", MonoFace, 13, _on ? BlueBrush : CyanBrush, SceneW / 2, 112, 0.1, 1);
            DrawSpaced(dc, _on ? "SYSTEMS DARK" : "SYSTEMS ONLINE", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.2, 0.4));
            dc.Pop(); dc.Pop();

            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
