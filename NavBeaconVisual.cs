using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A nav beacon scanned (the journal's NavBeaconScan, which hands over the system's body list): a cyan grid draws in, the star sits at the left, and the bodies appear one by one each with its orbit ring.
    // The plate gives the body count and the system name. Detail = "bodyCount|system".
    public sealed class NavBeaconVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, CX = 120, CY = 48, StarX = 52;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Pen GridPen = FrozenPen(Color.FromArgb(46, 85, 242, 255), 1);
        private static readonly Pen[] OrbitPens = AlphaPens(Cyan, 1);
        private static readonly Brush[] BodyBrushes = AlphaBrushes(Color.FromRgb(95, 227, 176));
        private static readonly Brush StarBrush = Frozen(Color.FromRgb(0xff, 0xd8, 0x90)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CyanBrush = Frozen(Cyan);
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1);

        private int _count;
        private string _system = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _count = p.Length > 0 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, n) : 0;
            _system = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "";
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The grid draws outward from the middle.
            double gp = Phase(t, 0.1, 0.9);
            for (int i = -5; i <= 5; i++) dc.DrawLine(GridPen, new Point(CX + i * 18, CY - 36 * gp), new Point(CX + i * 18, CY + 36 * gp));
            for (int j = -2; j <= 2; j++) dc.DrawLine(GridPen, new Point(CX - 90 * gp, CY + j * 15), new Point(CX + 90 * gp, CY + j * 15));
            dc.DrawEllipse(StarBrush, null, new Point(StarX, CY), 4, 4);

            int shown = Math.Min(_count, 40);
            for (int i = 0; i < shown; i++)
            {
                double p = Phase(t, 0.8 + i * (1.2 / Math.Max(1, shown)), 0.3);
                if (p <= 0) continue;
                double e = EaseOut(p), x = StarX + 16 + i * (130.0 / Math.Max(shown, 1)), y = CY + Math.Sin(i * 1.7) * 20;
                dc.DrawEllipse(null, OrbitPens[(int)Math.Round(0.25 * e * 8)], new Point(StarX, CY), x - StarX, Math.Abs(y - CY) + 6);
                dc.DrawEllipse(BodyBrushes[(int)Math.Round(e * 8)], null, new Point(x, y), 1.5 + e * 1.5, 1.5 + e * 1.5);
            }

            string title = "NAV BEACON DATA", l2 = (_count == 1 ? "1 BODY" : _count.ToString(CultureInfo.InvariantCulture) + " BODIES") + " CHARTED";
            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool hasName = _system.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, hasName ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, title, MonoFace, Fit(title, 12, 180), CyanBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (hasName) DrawSpaced(dc, _system, MonoFace, Fit(_system, 8, 180), CyanBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
