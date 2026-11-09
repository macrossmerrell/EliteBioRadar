using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The suit's oxygen or the commander's health has fallen low (on foot): a ring gauge drains from cyan through amber to red with a white mark at 25%, then pulses a red ring; the percentage sits
    // in the middle and the plate says "LOW OXYGEN" or "LOW HEALTH". Detail = "OXYGEN|0.18" or "HEALTH|0.24" (the level 0..1).
    public sealed class VitalsVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.6, CentreX = 120, CentreY = 50, Radius = 34, StartLevel = 0.55;
        private const int Steps = 24;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(255, 184, 77), Red = Color.FromRgb(0xff, 0x5a, 0x5a);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), RedBrush = Frozen(Red), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen TrackPen = FrozenPen(Color.FromArgb(77, 120, 140, 150), 6), MarkPen = FrozenPen(Color.FromRgb(232, 246, 240), 1.2), PlateEdge = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1);
        private static readonly Pen[] PulseRing = AlphaPens(Red, 1.6);
        // The gauge colour for each level from StartLevel down to 0.15, ready-made (pens for the arc, brushes for the number).
        private static readonly Pen[] ArcPens = MakeArcPens();
        private static readonly Brush[] NumBrushes = MakeNumBrushes();

        private static Color Severity(double v)
        {
            if (v > 0.4) return Cyan;
            if (v > 0.25) return Mix(Cyan, Amber, (0.4 - v) / 0.15);
            return Mix(Amber, Red, Math.Clamp((0.25 - v) / 0.07, 0, 1));
        }
        private static Color Mix(Color a, Color b, double k) => Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
        private static double LevelOf(int i) => StartLevel - (StartLevel - 0.15) * i / Steps;
        private static int IndexOf(double v) => Math.Clamp((int)Math.Round((StartLevel - v) / (StartLevel - 0.15) * Steps), 0, Steps);
        private static Pen[] MakeArcPens() { var a = new Pen[Steps + 1]; for (int i = 0; i <= Steps; i++) a[i] = new Pen(Frozen(Severity(LevelOf(i))), 6) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; foreach (var p in a) p.Freeze(); return a; }
        private static Brush[] MakeNumBrushes() { var a = new Brush[Steps + 1]; for (int i = 0; i <= Steps; i++) a[i] = Frozen(Severity(LevelOf(i))); return a; }

        private bool _health;
        private double _level = 0.18, _from = 0.55;
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _health = parts[0].Contains("HEALTH", StringComparison.OrdinalIgnoreCase);
            _level = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0.01, 0.5) : (_health ? 0.24 : 0.18);
            _from = Math.Min(1, Math.Max(StartLevel, _level + 0.25));
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double v = _from - (_from - _level) * EaseOut(Phase(t, 0.2, 1.4));
            int idx = IndexOf(v);
            var centre = new Point(CentreX, CentreY);
            dc.DrawEllipse(null, TrackPen, centre, Radius, Radius);

            double sweep = v * Math.PI * 2;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(CentreX, CentreY - Radius), false, false);
                c.ArcTo(new Point(CentreX + Radius * Math.Sin(sweep), CentreY - Radius * Math.Cos(sweep)), new Size(Radius, Radius), 0, v > 0.5, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, ArcPens[idx], geo);
            dc.DrawLine(MarkPen, new Point(CentreX + Radius - 7, CentreY), new Point(CentreX + Radius + 7, CentreY));   // the 25% mark

            if (v < 0.25)
            {
                double p = 0.5 + 0.5 * Math.Sin(t * 9);
                double rr = Radius + 8 + p * 3;
                dc.DrawEllipse(null, PulseRing[Math.Clamp((int)Math.Round((0.15 + 0.45 * p) * 8), 0, 8)], centre, rr, rr);
            }

            DrawSpaced(dc, Math.Round(v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", MonoFace, 17, NumBrushes[idx], CentreX, CentreY - 9, 0.05, 1);
            DrawSpaced(dc, _health ? "HEALTH" : "OXYGEN", MonoFace, 8, MutedBrush, CentreX, CentreY + 11, 0.12, 1);

            double plateAt = 1.6, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _health ? "LOW HEALTH" : "LOW OXYGEN", MonoFace, 13, RedBrush, SceneW / 2, 112, 0.1, pa);
                DrawSpaced(dc, (_health ? "VITALS  " : "SUIT RESERVE  ") + Math.Round(_level * 100).ToString("0", CultureInfo.InvariantCulture) + "%", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, plateAt + 0.3, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
