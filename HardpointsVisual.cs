using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The hardpoints going out or coming in: a hazard-striped guard flips up, the switch slides from RETRACTED to DEPLOYED (red-orange) and the title flickers on; coming in,
    // the switch slides back (cyan) and the guard drops shut. About 3.4 seconds. Detail = "OUT" (deployed) or "IN" (retracted).
    public sealed class HardpointsVisual : AnimationVisual
    {
        private const double FadeOutAt = 2.8;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Red = Color.FromRgb(0xff, 0x6a, 0x3d), Cyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Brush RedBrush = Frozen(Red), CyanBrush = Frozen(Cyan), MutedBrush = Frozen(Color.FromRgb(0x44, 0x55, 0x66));
        private static readonly Brush RedFill = Frozen(Color.FromArgb(204, Red.R, Red.G, Red.B)), CyanFill = Frozen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B));
        private static readonly Pen RedPen = FrozenPen(Red, 2.5), CyanPen = FrozenPen(Cyan, 2.5);
        private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x17, 0x30, 0x2f)), KnobBrush = Frozen(Color.FromRgb(0x0a, 0x12, 0x12));
        private static readonly Brush GuardShut = Frozen(Color.FromArgb(224, 0xf2, 0xb8, 0x4b)), GuardOpen = Frozen(Color.FromArgb(64, 0xf2, 0xb8, 0x4b)), GuardText = Frozen(Color.FromRgb(0x1a, 0x16, 0x08));
        private static readonly Pen GuardEdge = FrozenPen(Color.FromRgb(0x0a, 0x12, 0x12), 1);

        private bool _out = true;

        protected override void OnStart(string subject, string detail) => _out = !detail.Contains("IN", StringComparison.Ordinal) || detail.Contains("OUT", StringComparison.Ordinal);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, cx = W / 2, oy = H / 2 - 95;
            double trackW = Math.Min(200, W - 60), left = cx - trackW / 2, right = cx + trackW / 2;
            Brush hot = _out ? RedBrush : CyanBrush;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            // The guard flips up first (deploying) or drops shut last (retracting); the knob slides between.
            double open = _out ? EaseOut(Phase(t, 0.2, 0.4)) : 1 - EaseOut(Phase(t, 1.2, 0.4));
            double k = _out ? EaseOut(Phase(t, 0.6, 0.9)) : 1 - EaseOut(Phase(t, 0.2, 0.9));
            double x = left + 12 + (trackW - 24) * k;

            var track = new Rect(left, oy + 70, trackW, 16);
            dc.DrawRoundedRectangle(TrackBrush, null, track, 8, 8);
            dc.DrawRoundedRectangle(_out ? RedFill : CyanFill, null, new Rect(left, oy + 70, Math.Max(16, x + 12 - left), 16), 8, 8);
            dc.DrawEllipse(KnobBrush, _out ? RedPen : CyanPen, new Point(x, oy + 78), 12, 12);

            double gh = 32 * (1 - open) + 2, gw = Math.Min(92, trackW * 0.46), gy = oy + 94 - gh;
            dc.DrawRoundedRectangle(open > 0.95 ? GuardOpen : GuardShut, GuardEdge, new Rect(cx - gw / 2, gy, gw, gh), 3, 3);
            if (open < 0.4) DrawText(dc, "GUARD", MonoFace, 9, GuardText, cx, gy + gh / 2 - 6, a);

            DrawText(dc, "RETRACTED", MonoFace, 11, k < 0.5 ? CyanBrush : MutedBrush, left + 32, oy + 100, a);
            DrawText(dc, "DEPLOYED", MonoFace, 11, k >= 0.5 ? RedBrush : MutedBrush, right - 28, oy + 100, a);

            double flick = _out && t > 1.5 && t < 2.0 ? ((int)(t * 14) % 2 == 1 ? 0.35 : 1) : 1;
            DrawSpaced(dc, _out ? "HARDPOINTS DEPLOYED" : "HARDPOINTS RETRACTED", MonoFace, 13, hot, cx, oy + 134, 0.15, a * Phase(t, _out ? 1.5 : 1.4, 0.3) * flick);
            dc.Pop();
        }
    }
}
