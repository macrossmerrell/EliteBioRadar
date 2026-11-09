using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The suit is getting too hot or too cold (on foot): a thermometer whose mercury climbs into a red zone or sinks into a blue one, shaking at the extreme, with the suit temperature counting up
    // or down in kelvin beside it. Two levels: the plain hot / cold flag (milder colours, "HEAT WARNING" / "COLD WARNING") and the very hot / very cold flag (red or deep blue, "EXTREME HEAT" /
    // "EXTREME COLD"). Detail = "HOT|2|341" or "COLD|1|240" (kind, level 1 or 2, kelvin).
    public sealed class ThermalVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.6, TubeX = 90, Top = 10, Bottom = 82, Height = Bottom - Top;

        private static readonly Color Orange = Color.FromRgb(0xff, 0x9a, 0x3d), Red = Color.FromRgb(0xff, 0x5a, 0x5a), Ice = Color.FromRgb(0x9f, 0xdc, 0xff), Blue = Color.FromRgb(0x4a, 0x8c, 0xff);
        private static readonly Brush OrangeBrush = Frozen(Orange), RedBrush = Frozen(Red), IceBrush = Frozen(Ice), BlueBrush = Frozen(Blue);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush HotZone = Frozen(Color.FromArgb(38, 255, 90, 90)), ColdZone = Frozen(Color.FromArgb(38, 74, 140, 255));
        private static readonly Pen TubePen = FrozenPen(Color.FromArgb(128, 180, 200, 210), 1.5);
        private static readonly Pen RedPlateEdge = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1), IcePlateEdge = FrozenPen(Color.FromArgb(153, Ice.R, Ice.G, Ice.B), 1);

        private bool _hot = true;
        private int _tier = 2;
        private double _kelvin = 341;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _hot = !parts[0].Contains("COLD", StringComparison.OrdinalIgnoreCase);
            _tier = parts.Length > 1 && parts[1].Trim() == "1" ? 1 : 2;
            _kelvin = parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var k) && k > 0 ? k : (_hot ? 341 : 118);
        }

        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

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

            double reach = EaseOut(Phase(t, 0.2, 1.8));
            double span = _tier == 2 ? 0.45 : 0.25;
            double lvl = _hot ? 0.5 + span * reach : 0.5 - span * reach;
            Brush mercury = _hot ? (_tier == 2 && reach > 0.7 ? RedBrush : OrangeBrush) : (_tier == 2 && reach > 0.7 ? BlueBrush : IceBrush);

            dc.DrawRectangle(HotZone, null, new Rect(TubeX - 7, Top, 14, Height * 0.15));
            dc.DrawRectangle(ColdZone, null, new Rect(TubeX - 7, Bottom - Height * 0.15, 14, Height * 0.15));
            dc.DrawRectangle(null, TubePen, new Rect(TubeX - 7, Top, 14, Height));
            for (int i = 0; i <= 10; i++)
            {
                double y = Bottom - Height * i / 10;
                dc.DrawLine(TubePen, new Point(TubeX + 9, y), new Point(TubeX + (i % 5 == 0 ? 17 : 13), y));
            }

            double jitter = _tier == 2 && reach > 0.9 ? (Hash((int)(t * 40)) - 0.5) * 1.5 : 0;
            double y0 = Bottom - Height * lvl + jitter;
            dc.DrawRectangle(mercury, null, new Rect(TubeX - 4, y0, 8, Bottom - y0 + 4));
            dc.DrawEllipse(mercury, TubePen, new Point(TubeX, Bottom + 6), 10, 10);

            double kel = 295 + (_kelvin - 295) * reach;
            DrawSpaced(dc, Math.Round(kel).ToString("0", CultureInfo.InvariantCulture) + " K", MonoFace, 20, mercury, 165, 34, 0.04, 1);
            DrawSpaced(dc, _hot ? "SUIT HEATING" : "SUIT COOLING", MonoFace, 8, MutedBrush, 165, 58, 0.1, 1);

            double plateAt = 1.6, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, _hot ? RedPlateEdge : IcePlateEdge, new Rect(26, 106, 188, 40), 6, 6);
                dc.Pop();
                string title = _tier == 2 ? (_hot ? "EXTREME HEAT" : "EXTREME COLD") : (_hot ? "HEAT WARNING" : "COLD WARNING");
                DrawSpaced(dc, title, MonoFace, 13, _hot ? RedBrush : IceBrush, SceneW / 2, 112, 0.1, pa);
                DrawSpaced(dc, "SUIT TEMPERATURE  " + Math.Round(_kelvin).ToString("0", CultureInfo.InvariantCulture) + " K", MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, plateAt + 0.3, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
