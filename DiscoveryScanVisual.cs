using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The discovery scan (the honk, the journal's FSSDiscoveryScan): three cyan rings expand from the star, and each body in the system lights up as a ring passes it. The plate gives the body count and the system name.
    // Detail = "bodyCount|system".
    public sealed class DiscoveryScanVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, CX = 120, CY = 52, MaxR = 66, RingLen = 1.6;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Pen[] RingPens = AlphaPens(Cyan, 1.6);
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

            for (int r = 0; r < 3; r++)
            {
                double p = Phase(t, 0.2 + r * 0.55, RingLen);
                if (p <= 0 || p >= 1) continue;
                dc.DrawEllipse(null, RingPens[(int)Math.Round((1 - p) * 0.85 * 8)], new Point(CX, CY), 6 + EaseOut(p) * MaxR, (6 + EaseOut(p) * MaxR) * 0.55);
            }

            int shownBodies = Math.Min(_count, 40);
            for (int i = 0; i < shownBodies; i++)
            {
                double ang = Hash(i) * Math.PI * 2, r = 20 + Hash(i + 50) * 40, reach = Clamp01((r - 6) / MaxR);
                double p = Phase(t, 0.2 + EaseInverse(reach) * RingLen, 0.4);
                if (p <= 0) continue;
                double pop = EaseOut(p);
                dc.DrawEllipse(BodyBrushes[(int)Math.Round(pop * 8)], null, new Point(CX + Math.Cos(ang) * r, CY + Math.Sin(ang) * r * 0.55), 1.5 + (1 - p) * 3, 1.5 + (1 - p) * 3);
            }
            dc.DrawEllipse(StarBrush, null, new Point(CX, CY), 5, 5);

            string title = "DISCOVERY SCAN", l2 = _count == 1 ? "1 BODY DETECTED" : _count.ToString(CultureInfo.InvariantCulture) + " BODIES DETECTED";
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

        // The ring radius follows EaseOut(p); this finds the p at which the ring reaches a given fraction of its full radius, so a body lights as the ring crosses it.
        private static double EaseInverse(double reach) => 1 - Math.Pow(1 - Clamp01(reach), 1.0 / 3.0);
    }
}
