using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The headlights changing: two lamps throw one merged amber beam downward toward a three-notch switch (OFF / ON / HIGH). The beam grows to a medium length for ON and
    // longer and brighter for HIGH (same colour); the knob slides between notches. About 3.2 seconds. Detail = "OFF", "ON" or "HIGH".
    public sealed class HeadlightsVisual : AnimationVisual
    {
        private const double FadeOutAt = 2.6;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Amber = Color.FromRgb(0xff, 0xd9, 0x8a), Dim = Color.FromRgb(0x44, 0x55, 0x66), Cyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Brush[] BeamFill = MakeBeam(), LampFill = MakeLamps();
        private static readonly Brush HousingBrush = Frozen(Color.FromRgb(0x1c, 0x3a, 0x3a)), TrackBrush = Frozen(Color.FromRgb(0x17, 0x30, 0x2f)), KnobBrush = Frozen(Color.FromRgb(0x0a, 0x12, 0x12));
        private static readonly Brush TrackFill = Frozen(Color.FromArgb(204, Amber.R, Amber.G, Amber.B)), AmberBrush = Frozen(Amber), OffTitleBrush = Frozen(Dim), LabelBrush = Frozen(Color.FromRgb(0x66, 0x77, 0x88));
        private static readonly Pen HousingEdge = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1.5), KnobOn = FrozenPen(Amber, 2.5), KnobOff = FrozenPen(Cyan, 2.5);

        private static Brush[] MakeBeam() { var a = new Brush[33]; for (int i = 0; i < a.Length; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round(107.0 * i / 32), Amber.R, Amber.G, Amber.B)); return a; }
        private static Brush[] MakeLamps()
        {
            var a = new Brush[9];
            for (int i = 0; i < a.Length; i++) { double f = i / 8.0; a[i] = Frozen(Color.FromRgb(Lerp8(Dim.R, Amber.R, f), Lerp8(Dim.G, Amber.G, f), Lerp8(Dim.B, Amber.B, f))); }
            return a;
        }
        private static byte Lerp8(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);

        // The level the lights were last left at (0 off, 1 on, 2 high), so a change animates from where it really was.
        private static double _last;
        private double _from, _to;

        protected override void OnStart(string subject, string detail)
        {
            _to = detail.Contains("HIGH", StringComparison.OrdinalIgnoreCase) ? 2 : detail.Contains("OFF", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            _from = _last;
            if (_from == _to) _from = _to == 1 ? 0 : 1;
            _last = _to;
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, cx = W / 2, oy = H / 2 - 90;
            double lvl = _from + (_to - _from) * EaseOut(Phase(t, 0.2, 1.0));
            double on = Math.Min(1, lvl), hi = Math.Max(0, lvl - 1);

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));

            // One merged beam shape, so where the two cones cross the light stays an even colour.
            double len = lvl <= 1 ? 52 * lvl : 52 + 38 * hi, bw = len * 0.3, lampX = 26, top = oy + 22;
            if (len > 1)
            {
                var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
                using (var g = geo.Open())
                    foreach (double sx in new[] { cx - lampX, cx + lampX })
                    {
                        g.BeginFigure(new Point(sx - 4, top), true, true);
                        g.LineTo(new Point(sx + 4, top), true, false);
                        g.LineTo(new Point(sx + 4 + bw, top + len), true, false);
                        g.LineTo(new Point(sx - 4 - bw, top + len), true, false);
                    }
                geo.Freeze();
                dc.DrawGeometry(Level(BeamFill, (on * 0.28 + hi * 0.14) / 0.42 * a), null, geo);
            }

            dc.DrawRoundedRectangle(HousingBrush, HousingEdge, new Rect(cx - 44, oy, 88, 22), 8, 8);
            var lamp = LampFill[(int)Math.Round(on * 8)];
            double lr = 4 + hi * 1.5;
            dc.DrawEllipse(lamp, null, new Point(cx - lampX, oy + 18), lr, lr);
            dc.DrawEllipse(lamp, null, new Point(cx + lampX, oy + 18), lr, lr);

            // The switch: the knob sits at OFF, ON or HIGH.
            double trackW = Math.Min(160, W - 60), left = cx - trackW / 2, half = (trackW - 14) / 2, kx = cx - half + half * lvl, ty = oy + 124;
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(left, ty, trackW, 14), 7, 7);
            if (lvl > 0.02) dc.DrawRoundedRectangle(TrackFill, null, new Rect(left, ty, Math.Max(14, kx + 7 - left), 14), 7, 7);
            dc.DrawEllipse(KnobBrush, lvl > 0.5 ? KnobOn : KnobOff, new Point(kx, ty + 7), 10, 10);
            DrawText(dc, "OFF", MonoFace, 11, LabelBrush, cx - half, ty + 24, a);
            DrawText(dc, "ON", MonoFace, 11, LabelBrush, cx, ty + 24, a);
            DrawText(dc, "HIGH", MonoFace, 11, LabelBrush, cx + half, ty + 24, a);

            string title = _to == 2 ? "HIGH BEAM" : _to == 1 ? "HEADLIGHTS ON" : "HEADLIGHTS OFF";
            DrawSpaced(dc, title, MonoFace, 13, _to > 0 ? AmberBrush : OffTitleBrush, cx, ty + 48, 0.15, a * Phase(t, 1.0, 0.4));
            dc.Pop();
        }
    }
}
