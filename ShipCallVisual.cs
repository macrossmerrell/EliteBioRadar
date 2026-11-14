using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The ship dismissed or recalled from the SRV (the journal's Liftoff and Touchdown with PlayerControlled false): a glowing star hangs in the sky and the ship flies to it, turning into a silhouette
    // across the star's disc (dismissed, amber), or comes out of the star's glare and descends to the pad (recalled, cyan). Targeting corners follow the ship and an altitude readout counts up or down;
    // the figure is a visual, the journal does not report one. Detail = "DISMISS" or "RECALL".
    public sealed class ShipCallVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, PadX = 112, PadY = 78, StarX = 169, StarY = 30, MaxAlt = 120;
        private static readonly Color Amber = Color.FromRgb(255, 180, 80), Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Brush AmberBrush = Frozen(Amber), CyanBrush = Frozen(Cyan), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)),
            GroundBrush = Frozen(Color.FromRgb(0x1c, 0x24, 0x30)), StarCore = Frozen(Color.FromRgb(0xff, 0xe8, 0xb0)), ShipLight = Frozen(Color.FromRgb(0xa8, 0xb8, 0xc8)), ShipDark = Frozen(Color.FromRgb(0x0a, 0x0f, 0x14));
        private static readonly Pen AmberPen = FrozenPen(Amber, 1.2), CyanPen = FrozenPen(Cyan, 1.2), AmberEdge = FrozenPen(Color.FromArgb(153, 255, 180, 80), 1), CyanEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1),
            ShipOutline = FrozenPen(Color.FromRgb(0x6b, 0x7a, 0x88), 0.8), FlarePen = FrozenPen(Color.FromRgb(255, 255, 255), 1);
        private static readonly Pen[] FlarePens = AlphaPens(Color.FromRgb(255, 255, 255), 1);
        private static readonly Brush StarGlow = MakeGlow();
        private static readonly Geometry ShipShape = Poly(new[] { (0.0, -12.0), (10.0, 8.0), (0.0, 4.0), (-10.0, 8.0) });
        private static readonly double[] StarXs = MakeStars(true), StarYs = MakeStars(false);

        private static Brush MakeGlow()
        {
            var b = new RadialGradientBrush(Color.FromArgb(150, 255, 216, 144), Color.FromArgb(0, 255, 216, 144));
            b.Freeze(); return b;
        }
        private static double[] MakeStars(bool x) { var a = new double[34]; for (int i = 0; i < a.Length; i++) a[i] = x ? Hash(i + 3) * SceneW : Hash(i + 9) * 78; return a; }

        private bool _recall;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _recall = p.Length > 0 && p[0].Trim().ToUpperInvariant() == "RECALL";
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            Brush col = _recall ? CyanBrush : AmberBrush; Pen colPen = _recall ? CyanPen : AmberPen, edge = _recall ? CyanEdge : AmberEdge;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // Sky, ground, star.
            for (int i = 0; i < StarXs.Length; i++)
                dc.DrawRectangle(Level(Whites, 0.2 + Hash(i) * 0.5), null, new Rect(StarXs[i], StarYs[i], 1.2, 1.2));
            dc.DrawEllipse(GroundBrush, null, new Point(SceneW / 2, 154), 202, 79);
            dc.DrawEllipse(StarGlow, null, new Point(StarX, StarY), 46, 46);
            dc.DrawEllipse(StarCore, null, new Point(StarX, StarY), 8, 8);

            // The ship's flight.
            double p = Phase(t, 0.3, 2.0), q = EaseOut(p);
            double x, y, s;
            if (!_recall) { x = PadX + (StarX - PadX) * q; y = PadY + (StarY - PadY) * q; s = 1 - q * 0.75; }
            else { x = StarX + (PadX - StarX) * q; y = StarY + (PadY - StarY) * q; s = 0.25 + q * 0.75; }
            double alt = (_recall ? 1 - q : q) * MaxAlt;
            double near = Math.Max(0, 1 - Math.Sqrt((x - StarX) * (x - StarX) + (y - StarY) * (y - StarY)) / 12);

            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new RotateTransform((_recall ? -2.4 : 0.7) * 180 / Math.PI)); dc.PushTransform(new ScaleTransform(s * 0.85, s * 0.85));
            dc.DrawGeometry(near > 0.15 ? ShipDark : ShipLight, ShipOutline, ShipShape);
            dc.Pop(); dc.Pop(); dc.Pop();
            if (near > 0) dc.DrawLine(FlarePens[(int)Math.Round(near * 8)], new Point(StarX - 26, StarY), new Point(StarX + 26, StarY));

            // Targeting corners follow the ship.
            double b = 10 * s + 5, c = Phase(t, 0.1, 0.3);
            if (c > 0)
            {
                dc.PushOpacity(c);
                const double L = 4;
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    dc.DrawLine(colPen, new Point(x + sx * b, y + sy * (b - L)), new Point(x + sx * b, y + sy * b));
                    dc.DrawLine(colPen, new Point(x + sx * b, y + sy * b), new Point(x + sx * (b - L), y + sy * b));
                }
                dc.Pop();
            }

            // Altitude readout, and the word that closes it.
            DrawText(dc, alt.ToString("0.0", CultureInfo.InvariantCulture) + " KM", MonoFace, 12, NameBrush, 10, 8, 1, TextAlignment.Left);
            DrawText(dc, _recall ? "ALTITUDE (DESCENDING)" : "ALTITUDE (CLIMBING)", MonoFace, 7, col, 10, 24, 0.9, TextAlignment.Left);
            double end = Phase(t, 2.3, 0.3);
            if (end > 0) DrawSpaced(dc, _recall ? "TOUCHDOWN" : "ORBIT ACHIEVED", MonoFace, 9, col, SceneW / 2, 62, 0.08, end);

            string l1 = _recall ? "SHIP RECALLED" : "SHIP DISMISSED", l2 = _recall ? "INBOUND TO YOUR POSITION" : "DEPARTING FOR ORBIT", l3 = _recall ? "STAND CLEAR OF THE PAD" : "AWAITING YOUR CALL";
            double plateAt = 2.6, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, edge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 180), col, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), col, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
