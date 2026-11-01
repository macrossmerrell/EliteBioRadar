using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Stepping out of a vehicle: a round hull hatch with an amber progress bar under it (CYCLING AIRLOCK), then the hatch slides aside into the hull and a figure appears in
    // the lit opening; the title turns to DISEMBARKED with the vehicle it left. About 3.6 seconds. Detail = the vehicle ("SHIP", "SRV", "NOMAD").
    public sealed class DisembarkVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b), Pale = Color.FromRgb(0xcf, 0xea, 0xea), Ink = Color.FromRgb(0x0b, 0x14, 0x14), HullColor = Color.FromRgb(0x14, 0x29, 0x2a);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), HullBrush = Frozen(HullColor), DoorBrush = Frozen(Color.FromRgb(0x1c, 0x3a, 0x3a)), HubBrush = Frozen(Color.FromRgb(0x2a, 0x5a, 0x5a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), TrackBrush = Frozen(Color.FromRgb(0x17, 0x30, 0x2f));
        private static readonly Brush[] Lit = MakeLevels(Pale, 255, 32), Figure = MakeLevels(Ink, 255, 8);
        private static readonly Pen HullEdge = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1), PlateEdge = FrozenPen(Color.FromArgb(89, Cyan.R, Cyan.G, Cyan.B), 1), CyanPen = FrozenPen(Cyan, 2), HubPen = FrozenPen(Cyan, 1), RimThick = FrozenPen(HullColor, 14);

        private static Brush[] MakeLevels(Color c, int max, int steps)
        {
            var a = new Brush[steps + 1];
            for (int i = 0; i <= steps; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round((double)max * i / steps), c.R, c.G, c.B));
            return a;
        }

        private static double Io(double x) => x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2;

        private string _vehicle = "SHIP";

        protected override void OnStart(string subject, string detail) => _vehicle = string.IsNullOrWhiteSpace(detail) ? "SHIP" : detail.Trim().ToUpperInvariant();

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double bar = Phase(t, 0.2, 0.9), open = Io(Phase(t, 1.1, 1.0));
            bool cycling = t < 1.1;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.DrawRoundedRectangle(HullBrush, HullEdge, new Rect(18, 8, 204, 94), 8, 8);

            // The opening, lit from inside; the hatch slides off to the right behind the rim, and a figure steps into the light.
            dc.PushTransform(new TranslateTransform(120, 56));
            dc.PushTransform(new ScaleTransform(0.9, 0.9));
            dc.DrawEllipse(Lit[(int)Math.Round((0.2 + 0.8 * open) * 32)], null, new Point(0, 0), 44, 44);
            double fp = Phase(t, 1.6, 0.5);
            if (fp > 0.01)
            {
                double s = 0.95 * (0.6 + 0.4 * EaseOut(Phase(t, 1.6, 0.9)));
                dc.PushTransform(new TranslateTransform(0, 12));
                dc.PushTransform(new ScaleTransform(s, s));
                var ink = Figure[(int)Math.Round(fp * 8)];
                dc.DrawEllipse(ink, null, new Point(0, -22), 5, 5);
                dc.DrawRoundedRectangle(ink, null, new Rect(-6, -15, 12, 20), 5, 5);
                dc.DrawRectangle(ink, null, new Rect(-5, 4, 4, 12));
                dc.DrawRectangle(ink, null, new Rect(1, 4, 4, 12));
                dc.Pop(); dc.Pop();
            }

            if (open < 0.999)
            {
                dc.PushClip(new EllipseGeometry(new Point(0, 0), 44, 44));
                dc.PushTransform(new TranslateTransform(open * 98, 0));
                dc.DrawEllipse(DoorBrush, CyanPen, new Point(0, 0), 44, 44);
                dc.DrawEllipse(HubBrush, HubPen, new Point(0, 0), 8, 8);
                dc.DrawLine(CyanPen, new Point(0, -12), new Point(0, -38));
                dc.DrawLine(CyanPen, new Point(10, 6), new Point(33, 19));
                dc.DrawLine(CyanPen, new Point(-10, 6), new Point(-33, 19));
                dc.Pop(); dc.Pop();
            }

            dc.DrawEllipse(null, RimThick, new Point(0, 0), 44, 44);
            dc.DrawEllipse(null, CyanPen, new Point(0, 0), 51, 51);
            dc.Pop(); dc.Pop();

            // The plate: a dark backing so the words read over anything, with the progress bar and title.
            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(44, 112, 152, 5), 2.5, 2.5);
            if (bar > 0.01) dc.DrawRoundedRectangle(cycling ? AmberBrush : CyanBrush, null, new Rect(44, 112, 152 * bar, 5), 2.5, 2.5);
            DrawSpaced(dc, cycling ? "CYCLING AIRLOCK" : "DISEMBARKED", MonoFace, 13, cycling ? AmberBrush : CyanBrush, SceneW / 2, 118, 0.15, cycling ? 0.6 + 0.4 * Math.Sin(t * 14) : 1);
            if (!cycling) DrawSpaced(dc, "FROM " + _vehicle, MonoFace, 11, MutedBrush, SceneW / 2, 132, 0.1, Phase(t, 1.2, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
