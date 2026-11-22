using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The unmanned ship touching down after a recall (the journal's Touchdown with PlayerControlled false, which lands well after the recall itself starts): a ship seen from behind
    // settles onto the ground with its legs coming down, dust blooming under it, then a plate and four status chips appear. The gear, engines and hatch chips are scene dressing
    // (the game does not report them); the shields chip is real: the ship's shield state as last seen while the commander was flying it. Detail = "ON" or "OFF".
    // Drawn on a 720 x 450 canvas inside the usual 240 x 150 scene (scaled by a third).
    public sealed class ShipLandedVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 5.6, Ground = 300;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255), Green = Color.FromRgb(125, 255, 176), Amber = Color.FromRgb(255, 180, 80);
        private static readonly Brush CyanBrush = Frozen(Cyan), OkBrush = Frozen(Green), AmberBrush = Frozen(Amber), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)),
            PlateBrush = Frozen(Color.FromArgb(224, 5, 10, 20)), SkyBrush = Frozen(Color.FromRgb(0x0a, 0x08, 0x14)), Hull = Frozen(Color.FromRgb(0x1a, 0x11, 0x14)), HullDark = Frozen(Color.FromRgb(0x15, 0x0d, 0x10)),
            RingBrush = Frozen(Color.FromRgb(0x12, 0x0c, 0x0e)), Bore = Frozen(Color.FromRgb(5, 3, 4)), Pad = Frozen(Color.FromRgb(0x2a, 0x1e, 0x24)), LightBrush = Frozen(Color.FromRgb(0xea, 0xf2, 0xff)),
            DustBrush = Frozen(Color.FromRgb(0xb0, 0x80, 0x70)), Black = Frozen(Color.FromRgb(0, 0, 0)), WingLight = Frozen(Color.FromRgb(0xf0, 0xe0, 0x60)), WingGlow = Frozen(Color.FromArgb(90, 0xf0, 0xe0, 0x60));
        private static readonly Pen Edge = FrozenPen(Color.FromRgb(0x5a, 0x46, 0x50), 1.5), Accent = FrozenPen(Color.FromRgb(0x1d, 0x6f, 0xb5), 2), VentLine = FrozenPen(Color.FromRgb(0x3a, 0x2a, 0x30), 1),
            LegPen = MakeLegPen(), CyanEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1.5), GreenEdge = FrozenPen(Green, 1.5), AmberEdge = FrozenPen(Amber, 1.5);
        private static readonly Brush GroundBrush = MakeGround();
        private static readonly Geometry Mountains = Poly(new[] { (0.0, 200.0), (180.0, 170.0), (320.0, 190.0), (470.0, 160.0), (620.0, 185.0), (720.0, 180.0), (720.0, 450.0), (0.0, 450.0) });
        private static readonly Geometry WingMain = Poly(new[] { (18.0, -12.0), (130.0, 10.0), (118.0, 16.0), (22.0, 6.0) });
        private static readonly Geometry WingBlade = Poly(new[] { (20.0, -18.0), (112.0, -34.0), (118.0, -30.0), (40.0, -8.0) });
        private static readonly Geometry Body = Poly(new[] { (-24.0, -26.0), (24.0, -26.0), (30.0, 8.0), (0.0, 16.0), (-30.0, 8.0) });

        private static Pen MakeLegPen() { var p = new Pen(Frozen(Color.FromRgb(0x3a, 0x2c, 0x32)), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }
        private static Brush MakeGround()
        {
            var b = new LinearGradientBrush(Color.FromRgb(0x5a, 0x2a, 0x26), Color.FromRgb(0x2a, 0x15, 0x14), new Point(0, 0), new Point(0, 1)); b.Freeze(); return b;
        }

        private bool _shieldsUp = true;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            _shieldsUp = !string.Equals((detail ?? "").Trim(), "OFF", StringComparison.OrdinalIgnoreCase);
        }

        private static double FootOffset(double leg) => 10 + leg * 26 + 27;

        private static void Ring(DrawingContext dc, double cx, double cy, double r)
        {
            dc.DrawEllipse(RingBrush, Edge, new Point(cx, cy), r, r);
            dc.DrawEllipse(Bore, null, new Point(cx, cy), r * 0.62, r * 0.62);
        }

        private static void DrawShip(DrawingContext dc, double cx, double cy, double s, double leg)
        {
            dc.PushTransform(new TranslateTransform(cx, cy)); dc.PushTransform(new ScaleTransform(s, s));
            foreach (double m in new[] { -1.0, 1.0 })
            {
                dc.PushTransform(new ScaleTransform(m, 1));
                dc.DrawGeometry(Hull, Edge, WingMain);
                dc.DrawGeometry(HullDark, Edge, WingBlade);
                dc.DrawEllipse(WingGlow, null, new Point(88, 11.5), 16, 7);
                dc.DrawRectangle(WingLight, null, new Rect(78, 8, 20, 7));
                dc.DrawLine(Accent, new Point(40, -4), new Point(58, -1));
                Ring(dc, 66, -4, 12);
                dc.DrawRectangle(Hull, Edge, new Rect(56, 8, 20, 24));
                for (int i = 0; i < 4; i++) dc.DrawLine(VentLine, new Point(56, 13 + i * 6), new Point(76, 13 + i * 6));
                double ly = 10 + leg * 26, lx = 26 + leg * 14;
                dc.DrawLine(LegPen, new Point(20, 14), new Point(lx - 4, 12 + leg * 12));
                dc.DrawLine(LegPen, new Point(lx - 4, 12 + leg * 12), new Point(lx, ly + 22));
                dc.DrawRectangle(Pad, Edge, new Rect(lx - 8, ly + 22, 18, 5));
                dc.Pop();
            }
            dc.DrawGeometry(Hull, Edge, Body);
            Ring(dc, 0, -14, 15); Ring(dc, -20, -4, 11); Ring(dc, 20, -4, 11); Ring(dc, 0, 8, 13);
            dc.DrawLine(Accent, new Point(-14, -30), new Point(14, -30));
            dc.Pop(); dc.Pop();
        }

        private void Chip(DrawingContext dc, double t, int i, string label, string value, Brush valueBrush, Pen edge)
        {
            double a = Phase(t, 2.5 + i * 0.3, 0.3);
            if (a <= 0) return;
            double bx = 64 + i * 150, by = 345;
            dc.PushOpacity(a);
            dc.DrawRoundedRectangle(PlateBrush, edge, new Rect(bx, by, 136, 60), 8, 8);
            dc.Pop();
            DrawSpaced(dc, label, MonoFace, 17, NameBrush, bx + 68, by + 9, 0.08, a);
            DrawSpaced(dc, value, MonoFace, 17, valueBrush, bx + 68, by + 33, 0.08, a);
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
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, SceneW, SceneH)));
            dc.PushTransform(new ScaleTransform(SceneW / 720.0, SceneW / 720.0));   // from here on, the 720 x 450 mock-up space

            dc.DrawRectangle(SkyBrush, null, new Rect(0, 0, 720, 450));
            for (int i = 0; i < 40; i++) dc.DrawRectangle(Level(Whites, 0.2 + ((i * 37) % 50) / 100.0), null, new Rect((i * 83) % 720, (i * 53) % 190, 2, 2));
            dc.DrawGeometry(GroundBrush, null, Mountains);

            double p = EaseOut(Phase(t, 0.2, 2.0)), leg = Phase(t, 0.8, 0.9), sc = 1.2;
            double feet = 120 + p * (Ground - 120), cy = feet - FootOffset(leg) * sc;

            // Shadow, landing-light pads, ship.
            dc.PushOpacity(0.1 + p * 0.5); dc.DrawEllipse(Black, null, new Point(360, Ground), 40 + p * 170, 4 + p * 14); dc.Pop();
            if (p > 0.6)
            {
                dc.PushOpacity((p - 0.6) / 0.4 * 0.8);
                foreach (double m in new[] { -1.0, 1.0 }) dc.DrawEllipse(LightBrush, null, new Point(360 + m * (26 + leg * 14) * sc, Ground + 2), 34, 6);
                dc.Pop();
            }
            DrawShip(dc, 360, cy, sc, leg);

            // Dust blooming outward once the feet are down.
            if (p > 0.97)
            {
                double d = Phase(t, 2.2, 1.4);
                dc.PushOpacity((1 - d) * 0.45);
                for (int i = 0; i < 10; i++)
                {
                    double m = i % 2 == 1 ? 1 : -1, r = 60 + d * 180;
                    dc.DrawEllipse(DustBrush, null, new Point(360 + m * (r - i * 8), Ground + 4 - d * 14 * (i % 3)), 14 + d * 18, 6 + d * 5);
                }
                dc.Pop();
            }

            // Altitude readout, then the plate and chips.
            double plate = Phase(t, 3.8, 0.4);
            DrawSpaced(dc, ((1 - p) * 120).ToString("0.0", CultureInfo.InvariantCulture) + " KM", MonoFace, 28, NameBrush, 120, 14, 0.08, 1 - plate);
            DrawSpaced(dc, "ALTITUDE (DESCENDING)", MonoFace, 12, CyanBrush, 150, 46, 0.08, 0.9 * (1 - plate));
            Chip(dc, t, 0, "GEAR", "LOCKED", OkBrush, GreenEdge);
            Chip(dc, t, 1, "ENGINES", "SAFE", OkBrush, GreenEdge);
            Chip(dc, t, 2, "HATCH", "READY", OkBrush, GreenEdge);
            Chip(dc, t, 3, "SHIELDS", _shieldsUp ? "ONLINE" : "OFFLINE", _shieldsUp ? OkBrush : AmberBrush, _shieldsUp ? GreenEdge : AmberEdge);
            if (plate > 0)
            {
                dc.PushOpacity(plate);
                dc.DrawRoundedRectangle(PlateBrush, CyanEdge, new Rect(160, 40, 400, 70), 10, 10);
                dc.Pop();
                DrawSpaced(dc, "SHIP LANDED", MonoFace, 28, CyanBrush, 360, 48, 0.08, plate);
                DrawSpaced(dc, _shieldsUp ? "ALL SYSTEMS SAFE" : "SECURE ON THE GROUND", MonoFace, 17, NameBrush, 360, 82, 0.08, plate);
            }

            dc.Pop(); dc.Pop();   // the mock-up scale, the clip
            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
