using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The hull has fallen through a damage threshold: a segmented integrity bar drains from the old hull level to the new one, the lost segments breaking off and falling away, with the
    // percentage counting down. DAMAGED is amber; CRITICAL is red and pulses. Detail = "DAMAGED|from|to" or "CRITICAL|from|to" with the hull as 0..1 (the Play preview sends just
    // "DAMAGED" or "CRITICAL" and gets sample levels). About 4.4 seconds.
    public sealed class HullDamageVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.8, BarX = 36, BarW = 168, BarY = 40, BarH = 16;
        private const int Segs = 20;
        private const double SegStep = BarW / Segs, SegW = SegStep - 2;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Amber = Color.FromRgb(255, 184, 77), Red = Color.FromRgb(255, 84, 84);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), AmberBrush = Frozen(Amber), RedBrush = Frozen(Red), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen AmberEdge = FrozenPen(Color.FromArgb(166, Amber.R, Amber.G, Amber.B), 1), RedEdge = FrozenPen(Color.FromArgb(166, Red.R, Red.G, Red.B), 1);
        private static readonly Pen AmberSlot = FrozenPen(Color.FromArgb(64, Amber.R, Amber.G, Amber.B), 1), RedSlot = FrozenPen(Color.FromArgb(64, Red.R, Red.G, Red.B), 1), EmptySlot = FrozenPen(Color.FromArgb(64, 120, 140, 160), 1);
        private static readonly Brush[] AmberFill = AlphaBrushes(Amber), RedFill = AlphaBrushes(Red);
        private static readonly double[] Scatter = StarValues(Segs, 1, 1);

        private bool _crit;
        private double _from = 1, _to = 0.62;
        private int _pct = -1;
        private string _pctText = "", _integrity = "";
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _crit = parts[0].Contains("CRIT", StringComparison.OrdinalIgnoreCase);
            if (parts.Length >= 3 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            { _from = Math.Clamp(f, 0, 1); _to = Math.Clamp(n, 0, 1); }
            else if (_crit) { _from = 0.62; _to = 0.18; }
            else { _from = 1; _to = 0.62; }
            _pct = -1;
            _integrity = Math.Round(_to * 100) + " % INTEGRITY";
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            double pulse = _crit ? 0.55 + 0.45 * Math.Sin(t * 9) : 1;
            int pl = (int)Math.Round((_crit ? pulse : 0.9) * 8);
            double cur = _from + (_to - _from) * EaseOut(Phase(t, 0.3, 1.2));
            var fill = _crit ? RedFill : AmberFill;
            var slot = _crit ? RedSlot : AmberSlot;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            for (int i = 0; i < Segs; i++)
            {
                double f0 = (double)i / Segs, f1 = (double)(i + 1) / Segs, x = BarX + i * SegStep;
                bool alive = f1 <= cur + 0.001, lost = !alive && f1 <= _from + 0.001;
                if (alive) dc.DrawRectangle(fill[pl], null, new Rect(x, BarY, SegW, BarH));
                else if (lost)
                {
                    // The segment breaks off (the top of the bar first) and falls, fading.
                    double d = Clamp01((t - 0.3 - (1 - f0) * 0.2) / 0.8);
                    if (d < 1) dc.DrawRectangle(fill[(int)Math.Round(0.9 * (1 - d) * 8)], null, new Rect(x + (Scatter[i] - 0.5) * 8 * d, BarY + d * d * 40, SegW, BarH));
                    dc.DrawRectangle(null, slot, new Rect(x + 0.5, BarY + 0.5, SegW - 1, BarH - 1));
                }
                else dc.DrawRectangle(null, EmptySlot, new Rect(x + 0.5, BarY + 0.5, SegW - 1, BarH - 1));
            }

            int pct = (int)Math.Round(cur * 100);
            if (pct != _pct) { _pct = pct; _pctText = pct + " %"; }
            DrawSpaced(dc, _pctText, MonoFace, 20, _crit ? RedBrush : AmberBrush, SceneW / 2, 66, 0.1, pulse);

            dc.DrawRoundedRectangle(PlateBrush, _crit ? RedEdge : AmberEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _crit ? "HULL CRITICAL" : "HULL DAMAGED", MonoFace, 13, _crit ? RedBrush : AmberBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _integrity, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
