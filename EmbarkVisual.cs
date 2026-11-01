using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Stepping aboard a vehicle: the lit round opening in the hull, a figure walking into the light and fading, the hatch sliding shut over it with an amber progress bar
    // (BOARDING, SEALING HATCH), then BOARDED in cyan while eight pips around the rim light up green one by one (systems online). About 3.6 seconds.
    // Detail = the vehicle ("SHIP", "SRV", "NOMAD").
    public sealed class EmbarkVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b), Pale = Color.FromRgb(0xcf, 0xea, 0xea), Ink = Color.FromRgb(0x0b, 0x14, 0x14), HullColor = Color.FromRgb(0x14, 0x29, 0x2a), Lime = Color.FromRgb(0x55, 0xff, 0x7a);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), HullBrush = Frozen(HullColor), DoorBrush = Frozen(Color.FromRgb(0x1c, 0x3a, 0x3a)), HubBrush = Frozen(Color.FromRgb(0x2a, 0x5a, 0x5a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), TrackBrush = Frozen(Color.FromRgb(0x17, 0x30, 0x2f)), PipOn = Frozen(Lime), PipOff = Frozen(Color.FromRgb(0x17, 0x30, 0x2f)), LitBrush = Frozen(Pale);
        private static readonly Brush[] Figure = MakeLevels(Ink, 255, 8);
        private static readonly Pen HullEdge = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1), PlateEdge = FrozenPen(Color.FromArgb(89, Cyan.R, Cyan.G, Cyan.B), 1), CyanPen = FrozenPen(Cyan, 2), HubPen = FrozenPen(Cyan, 1), RimThick = FrozenPen(HullColor, 14), PipEdge = FrozenPen(Ink, 1);

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
            double bar = Phase(t, 1.0, 1.0), close = Io(Phase(t, 1.0, 0.8));
            bool done = t >= 2.0;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.DrawRoundedRectangle(HullBrush, HullEdge, new Rect(18, 8, 204, 94), 8, 8);

            dc.PushTransform(new TranslateTransform(120, 56));
            dc.PushTransform(new ScaleTransform(0.9, 0.9));
            dc.DrawEllipse(LitBrush, null, new Point(0, 0), 44, 44);

            // A figure walks in toward the middle of the light and fades into it.
            double wp = EaseOut(Phase(t, 0.2, 0.8)), fo = 1 - Phase(t, 0.75, 0.25);
            if (fo > 0.01)
            {
                double s = 0.95 - 0.4 * wp;
                dc.PushTransform(new TranslateTransform(28 * (1 - wp), 12));
                dc.PushTransform(new ScaleTransform(s, s));
                var ink = Figure[(int)Math.Round(fo * 8)];
                dc.DrawEllipse(ink, null, new Point(0, -22), 5, 5);
                dc.DrawRoundedRectangle(ink, null, new Rect(-6, -15, 12, 20), 5, 5);
                dc.DrawRectangle(ink, null, new Rect(-5, 4, 4, 12));
                dc.DrawRectangle(ink, null, new Rect(1, 4, 4, 12));
                dc.Pop(); dc.Pop();
            }

            // The hatch slides back in from the right, behind the rim.
            if (close > 0.001)
            {
                dc.PushClip(new EllipseGeometry(new Point(0, 0), 44, 44));
                dc.PushTransform(new TranslateTransform((1 - close) * 98, 0));
                dc.DrawEllipse(DoorBrush, CyanPen, new Point(0, 0), 44, 44);
                dc.DrawEllipse(HubBrush, HubPen, new Point(0, 0), 8, 8);
                dc.DrawLine(CyanPen, new Point(0, -12), new Point(0, -38));
                dc.DrawLine(CyanPen, new Point(10, 6), new Point(33, 19));
                dc.DrawLine(CyanPen, new Point(-10, 6), new Point(-33, 19));
                dc.Pop(); dc.Pop();
            }

            dc.DrawEllipse(null, RimThick, new Point(0, 0), 44, 44);
            dc.DrawEllipse(null, CyanPen, new Point(0, 0), 51, 51);

            // Systems online: eight pips round the rim light up one by one once the hatch is shut.
            int lit = (int)Math.Floor(Phase(t, 1.8, 0.7) * 9);
            for (int i = 0; i < 8; i++)
            {
                double an = (i * 45 - 90) * Math.PI / 180;
                dc.DrawEllipse(i < lit ? PipOn : PipOff, PipEdge, new Point(Math.Cos(an) * 51, Math.Sin(an) * 51), 4, 4);
            }
            dc.Pop(); dc.Pop();

            // The plate with the progress bar and title.
            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(44, 112, 152, 5), 2.5, 2.5);
            if (bar > 0.01) dc.DrawRoundedRectangle(done ? CyanBrush : AmberBrush, null, new Rect(44, 112, 152 * bar, 5), 2.5, 2.5);
            string title = t < 1.0 ? "BOARDING" : t < 2.0 ? "SEALING HATCH" : "BOARDED";
            DrawSpaced(dc, title, MonoFace, 13, done ? CyanBrush : AmberBrush, SceneW / 2, 118, 0.15, done ? 1 : 0.6 + 0.4 * Math.Sin(t * 14));
            if (done) DrawSpaced(dc, "ABOARD " + _vehicle, MonoFace, 11, MutedBrush, SceneW / 2, 132, 0.1, Phase(t, 2.1, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
