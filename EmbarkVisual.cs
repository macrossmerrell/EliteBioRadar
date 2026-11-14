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
        protected override double Duration => (_vehicle == "SRV" ? SrvFadeOut : FadeOutAt) + 0.6;

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
            if (_vehicle == "SRV") { RenderSrv(dc, w, h, t); return; }
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
        // ---- SRV: the cockpit view powers up (the SRV HUD: pillars, notched top bar, dash with radar, wireframe, power bars, fuel and the checklist, over a settling horizon) ----
        private const double SrvFadeOut = 7.4;
        private static readonly Color Blue = Color.FromRgb(0x3a, 0xa0, 0xff), Green = Color.FromRgb(0x7d, 0xff, 0xb0);
        private static readonly Brush BlueB = Frozen(Blue), GreenB = Frozen(Green), SkyB = Frozen(Color.FromRgb(0x0a, 0x16, 0x12)), GroundB = Frozen(Color.FromRgb(0x1a, 0x12, 0x10)), FrameB = Frozen(Color.FromRgb(0x0a, 0x12, 0x14)), DashB = Frozen(Color.FromRgb(0x0c, 0x16, 0x18)), BackB = Frozen(Color.FromRgb(0x06, 0x0b, 0x10)), BarBack = Frozen(Color.FromRgb(0x17, 0x30, 0x2f)), DimB = Frozen(Color.FromRgb(0x5d, 0x7f, 0x86)), PlateB = Frozen(Color.FromArgb(199, 5, 11, 7));
        private static readonly Pen BluePen = FrozenPen(Blue, 1), BlueThick = FrozenPen(Blue, 1.5), EdgePen = FrozenPen(Color.FromRgb(0x2a, 0x4a, 0x4a), 1), HillPen = FrozenPen(Color.FromRgb(0x4a, 0x2f, 0x20), 1), CyanThin = FrozenPen(Cyan, 1), GreenPen = FrozenPen(Green, 1), BoxBorder = FrozenPen(Color.FromArgb(89, Cyan.R, Cyan.G, Cyan.B), 1);
        private static readonly Geometry PillarL = Poly(new[] { (0.0, 0.0), (72, 0), (56, 64), (40, 126), (34, 200), (0, 200) }), PillarR = Poly(new[] { (320.0, 0.0), (248, 0), (264, 64), (280, 126), (286, 200), (320, 200) });
        private static readonly Geometry TopBar = Poly(new[] { (96.0, 0.0), (224, 0), (212, 12), (196, 15), (124, 15), (108, 12) }), Dash = Poly(new[] { (64.0, 200.0), (84, 172), (126, 152), (194, 152), (236, 172), (256, 200) });
        private static readonly Geometry Ground = Poly(new[] { (0.0, 0.0), (60, -8), (110, -3), (170, -12), (230, -4), (290, -14), (320, -6), (320, 260), (0, 260) });
        private static readonly double[][] Wire = { new[] { -8.0, -8, 16, 16 }, new[] { -14.0, -9, 4, 5 }, new[] { -14.0, -2, 4, 5 }, new[] { -14.0, 5, 4, 5 }, new[] { 10.0, -9, 4, 5 }, new[] { 10.0, -2, 4, 5 }, new[] { 10.0, 5, 4, 5 } };
        private static readonly string[] Checks = { "BOARD SHIP", "HAND BRAKE", "CARGO SCOOP" };

        private void SrvText(DrawingContext dc, string s, double size, Brush b, double x, double yMid, double a, TextAlignment al) => DrawText(dc, s, MonoFace, size, b, x, yMid - size * 0.6, a, al);

        private void RenderSrv(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, SrvFadeOut);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k * 0.75, k * 0.75));   // drawn on a 320 x 200 canvas
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 320, 200)));
            dc.DrawRectangle(BackB, null, new Rect(0, 0, 320, 200));

            // The view bobs as the suspension settles, then the horizon furniture draws in.
            double bob = Math.Sin(t * 9) * 5 * Math.Max(0, 1 - Phase(t, 0.2, 1.8)) * Clamp01(t / 0.2), hy = 104 + bob;
            dc.DrawRectangle(SkyB, null, new Rect(0, -30, 320, hy + 30));
            dc.PushTransform(new TranslateTransform(0, hy));
            dc.DrawGeometry(GroundB, HillPen, Ground);
            double b = Phase(t, 0.8, 0.7);
            if (b > 0)
            {
                dc.PushOpacity(b);
                for (int i = 0; i < 5; i++)
                {
                    double yy = -12 - i * 14, lw = i % 2 == 1 ? 14 : 22;
                    dc.DrawLine(BluePen, new Point(160 - lw, yy), new Point(160 + lw, yy));
                    SrvText(dc, (5 + i * 5).ToString(), 7, BlueB, 160 + lw + 5, yy, 1, TextAlignment.Left);
                }
                dc.DrawLine(BluePen, new Point(148, 0), new Point(172, 0));
                dc.DrawLine(BlueThick, new Point(152, 10), new Point(160, 5)); dc.DrawLine(BlueThick, new Point(160, 5), new Point(168, 10));
                dc.Pop();
            }
            double br = Phase(t, 1.2, 0.5);
            if (br > 0)
            {
                dc.PushOpacity(br);
                dc.DrawLine(BluePen, new Point(98, -38), new Point(98, 12)); dc.DrawLine(BluePen, new Point(98, -38), new Point(104, -38));
                dc.DrawLine(BluePen, new Point(222, -38), new Point(222, 12)); dc.DrawLine(BluePen, new Point(222, -38), new Point(216, -38));
                SrvText(dc, "SECONDARY A", 7, BlueB, 94, -38, 1, TextAlignment.Right); SrvText(dc, "PRIMARY A", 7, BlueB, 226, -38, 1, TextAlignment.Left);
                dc.Pop();
            }
            dc.Pop();

            // The frame closes in: pillars from the sides, the notched bar from above, the dash from below.
            double o = 1 - EaseOut(Phase(t, 0.05, 0.9));
            dc.PushTransform(new TranslateTransform(-o * 90, 0)); dc.DrawGeometry(FrameB, EdgePen, PillarL); dc.Pop();
            dc.PushTransform(new TranslateTransform(o * 90, 0)); dc.DrawGeometry(FrameB, EdgePen, PillarR); dc.Pop();
            dc.PushTransform(new TranslateTransform(0, -o * 30)); dc.DrawGeometry(FrameB, EdgePen, TopBar); dc.Pop();
            dc.PushTransform(new TranslateTransform(0, o * 40)); dc.DrawGeometry(DashB, EdgePen, Dash); dc.Pop();

            // The dash boots: radar, wireframe SRV, power bars, fuel and the checklist.
            const double d0 = 2.4;
            double d = Phase(t, d0, 0.5);
            if (d > 0)
            {
                dc.PushOpacity(d);
                dc.DrawEllipse(null, BluePen, new Point(112, 176), 12, 12);
                dc.DrawEllipse(null, BluePen, new Point(160, 178), 30, 13);
                double sw = (t * 2.4) % (Math.PI * 2);
                dc.PushOpacity(0.7); dc.DrawLine(CyanThin, new Point(112, 176), new Point(112 + Math.Cos(sw) * 12, 176 + Math.Sin(sw) * 12)); dc.Pop();
                SrvText(dc, Math.Round(100 * EaseOut(Phase(t, d0 + 0.3, 1.2))).ToString("0") + "%", 7.5, BlueB, 204, 168, 1, TextAlignment.Left);
                SrvText(dc, "0", 7.5, BlueB, 138, 166, 1, TextAlignment.Center);
                dc.Pop();
            }
            double wf = Phase(t, d0 + 0.4, 1.4) * Wire.Length;
            for (int i = 0; i < Wire.Length; i++)
            {
                if (wf <= i) continue;
                double al = Clamp01(wf - i); var r = Wire[i];
                dc.PushOpacity(al); dc.DrawRectangle(null, CyanThin, new Rect(160 + r[0] * 0.9, 178 + r[1] * 0.9, r[2] * 0.9, r[3] * 0.9)); dc.Pop();
            }
            double pb = Phase(t, d0 + 0.8, 1.2);
            if (pb > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    double hh = 16 * EaseOut(Clamp01(pb * 1.4 - i * 0.2));
                    dc.DrawRectangle(BarBack, null, new Rect(222 + i * 7, 170, 5, 16));
                    dc.DrawRectangle(BlueB, null, new Rect(222 + i * 7, 186 - hh, 5, hh));
                }
                SrvText(dc, "SYS ENG WEP", 6.5, DimB, 233, 192, pb, TextAlignment.Center);
            }
            double fu = Phase(t, d0 + 1.0, 1.0);
            if (fu > 0)
            {
                dc.DrawRectangle(BarBack, null, new Rect(250, 168, 48, 3));
                dc.DrawRectangle(AmberBrush, null, new Rect(250, 168, 48 * fu, 3));
                SrvText(dc, "FUEL", 7, DimB, 250, 162, fu, TextAlignment.Left);
            }
            for (int i = 0; i < 3; i++)
            {
                double cp = Phase(t, d0 + 1.4 + i * 0.3, 0.3);
                if (cp <= 0) continue;
                SrvText(dc, Checks[i], 6.5, DimB, 262, 176 + i * 8, cp, TextAlignment.Left);
                dc.DrawRectangle(null, GreenPen, new Rect(252, 174 + i * 8, 4, 4));
            }

            // BOOTING UP while it comes alive, then the plate.
            SrvText(dc, "BOOTING UP", 10.5, AmberBrush, 160, 26, Phase(t, 0.3, 0.3) * (1 - Phase(t, 4.8, 0.3)), TextAlignment.Center);
            double pa = Phase(t, 5.0, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa); dc.DrawRectangle(PlateB, BoxBorder, new Rect(70, 60, 180, 42)); dc.Pop();
                SrvText(dc, "SRV ONLINE", 13, GreenB, 160, 75, pa, TextAlignment.Center);
                SrvText(dc, "ALL SYSTEMS NOMINAL", 9, DimB, 160, 91, Phase(t, 5.3, 0.4), TextAlignment.Center);
            }
            dc.Pop();
            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

    }
}
