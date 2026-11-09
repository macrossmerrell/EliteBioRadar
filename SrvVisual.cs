using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The SRV leaving or returning to the ship's bay, drawn as the SRV itself. DEPLOYED: the bay doors slide open, the status lights go from amber to green and the SRV rolls out TOWARD the camera,
    // seen from the front (white headlights, cyan-rimmed wheels, the two arms with their pods), growing as it comes. RECOVERED: it is seen from behind (red tail lights) and shrinks away into the bay
    // before the doors close. The SRV is always drawn in front of the bay and the floor line; the floor line is simply not drawn across its width while the two overlap.
    // Subject = the SRV's name (for example "SRV Scarab"), Detail = "DEPLOYED" or "RECOVERED". About 4.9 seconds.
    public sealed class SrvVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.3;
        private const double BayX = 80, BayY = 14, BayW = 80, BayH = 62, GroundY = BayY + BayH, CentreX = 120;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(125, 255, 196), Amber = Color.FromRgb(255, 200, 90);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), MintBrush = Frozen(Mint), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush BayFill = Frozen(Color.FromRgb(0x0a, 0x11, 0x18)), DoorFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30));
        private static readonly Brush BodyFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30)), WheelFill = Frozen(Color.FromRgb(0x10, 0x16, 0x1c)), PodFill = Frozen(Color.FromRgb(0x2a, 0x36, 0x40));
        private static readonly Brush RoofLight = Frozen(Color.FromArgb(179, 255, 255, 255));
        private static readonly Brush HeadGlow = RadialGlow(Color.FromRgb(255, 255, 255), 166), TailGlow = RadialGlow(Color.FromRgb(255, 70, 50), 242);
        private static readonly Pen MintPlate = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1);
        private static readonly Pen BayEdge = FrozenPen(Color.FromArgb(179, Cyan.R, Cyan.G, Cyan.B), 1.3), DoorEdge = FrozenPen(Color.FromArgb(128, Cyan.R, Cyan.G, Cyan.B), 1), GroundPen = FrozenPen(Color.FromArgb(102, Cyan.R, Cyan.G, Cyan.B), 1);
        private static readonly Pen BodyEdge = FrozenPen(Color.FromArgb(230, Cyan.R, Cyan.G, Cyan.B), 0.9), ArmPen = FrozenPen(Color.FromRgb(0x4a, 0x5a, 0x66), 1.4);
        private static readonly Pen BigRim = FrozenPen(Color.FromArgb(242, Cyan.R, Cyan.G, Cyan.B), 1.6), SmallRim = FrozenPen(Color.FromArgb(128, Cyan.R, Cyan.G, Cyan.B), 1.28);
        // Nine alpha levels each.
        private static readonly Brush[] AmberDot = AlphaBrushes(Amber), MintDot = AlphaBrushes(Mint);

        private bool _deployed = true;
        private string _line2 = "";
        private readonly ScaleTransform _outer = new(), _fit = new(), _srvS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _srvT = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            _deployed = !(detail ?? "").Contains("RECOVER", StringComparison.OrdinalIgnoreCase);
            _line2 = string.IsNullOrWhiteSpace(subject) ? "SRV" : subject.ToUpperInvariant();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double p = _deployed ? EaseOut(Phase(t, 1.0, 2.6)) : 1 - EaseOut(Phase(t, 0.9, 2.4)), s = 0.28 + p * 0.9, cy = GroundY - 6 + p * 26;
            double open = _deployed ? Phase(t, 0.2, 0.8) : Math.Min(Phase(t, 0.1, 0.6), 1 - Phase(t, 3.0, 0.8)), door = BayW / 2 * (1 - open);
            bool srvShown = open > 0.5;

            // The bay: its back wall and the two doors, outlined without their bottom edges (the floor line is drawn on its own below).
            dc.DrawRectangle(BayFill, null, new Rect(BayX, BayY, BayW, BayH));
            if (door > 0)
            {
                dc.DrawRectangle(DoorFill, null, new Rect(BayX, BayY, door, BayH));
                dc.DrawRectangle(DoorFill, null, new Rect(BayX + BayW - door, BayY, door, BayH));
            }
            dc.DrawLine(BayEdge, new Point(BayX, GroundY), new Point(BayX, BayY));
            dc.DrawLine(BayEdge, new Point(BayX, BayY), new Point(BayX + BayW, BayY));
            dc.DrawLine(BayEdge, new Point(BayX + BayW, BayY), new Point(BayX + BayW, GroundY));
            if (door > 0)
            {
                dc.DrawLine(DoorEdge, new Point(BayX + door, GroundY), new Point(BayX + door, BayY));
                dc.DrawLine(DoorEdge, new Point(BayX + BayW - door, GroundY), new Point(BayX + BayW - door, BayY));
            }

            // The floor line stops either side of the SRV while it crosses it.
            bool crossing = srvShown && cy - 22 * s <= GroundY + 1 && cy + 19 * s >= GroundY - 1;
            double half = crossing ? 31 * s + 1 : 0;
            dc.DrawLine(GroundPen, new Point(10, GroundY), new Point(CentreX - half, GroundY));
            dc.DrawLine(GroundPen, new Point(CentreX + half, GroundY), new Point(230, GroundY));

            // Four status lights above the bay: amber, then mint once the doors are open.
            for (int i = 0; i < 4; i++)
            {
                double lit = Phase(t, 0.1 + i * 0.15, 0.2);
                var set = open > 0.85 ? MintDot : AmberDot;
                dc.DrawEllipse(set[Math.Clamp((int)Math.Round((0.3 + 0.7 * lit) * 8), 0, 8)], null, new Point(BayX + 10 + i * 20, BayY - 6), 2.2, 2.2);
            }

            if (srvShown) DrawSrv(dc, s, cy);

            double pa = Phase(t, 2.2, 0.4);
            if (pa > 0)
            {
                if (pa < 0.999) dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, MintPlate, new Rect(26, 106, 188, 40), 6, 6);
                if (pa < 0.999) dc.Pop();
            }
            DrawSpaced(dc, _deployed ? "SRV DEPLOYED" : "SRV RECOVERED", MonoFace, 13, MintBrush, SceneW / 2, 112, 0.1, pa);
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 2.5, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

        // The SRV in its own units (about 62 wide, 40 tall, origin at its middle), scaled and placed by the caller.
        private void DrawSrv(DrawingContext dc, double s, double cy)
        {
            _srvT.X = CentreX; _srvT.Y = cy; _srvS.ScaleX = _srvS.ScaleY = s;
            dc.PushTransform(_srvT); dc.PushTransform(_srvS);

            // The far wheels, then the body, the roof bar, the arms with their pods, the near wheels.
            dc.DrawEllipse(WheelFill, SmallRim, new Point(-23, 6), 6.2, 8);
            dc.DrawEllipse(WheelFill, SmallRim, new Point(23, 6), 6.2, 8);
            dc.DrawRoundedRectangle(BodyFill, BodyEdge, new Rect(-13, -11, 26, 18), 3, 3);
            dc.DrawRoundedRectangle(BodyFill, BodyEdge, new Rect(-9, -16, 18, 5), 1.5, 1.5);
            for (int d = -1; d <= 1; d += 2)
            {
                dc.DrawLine(ArmPen, new Point(d * 13, -6), new Point(d * 20, -15));
                dc.DrawRoundedRectangle(PodFill, null, new Rect(d * 20 - 3.5, -21, 7, 8), 2, 2);
                dc.DrawLine(ArmPen, new Point(d * 13, 2), new Point(d * 27, 8));
            }
            dc.DrawEllipse(WheelFill, BigRim, new Point(-30, 9), 7.8, 10);
            dc.DrawEllipse(WheelFill, BigRim, new Point(30, 9), 7.8, 10);

            // Headlights (solid, middle-bright) from the front; red tail lights from behind.
            if (_deployed)
            {
                dc.DrawEllipse(HeadGlow, null, new Point(-8, 2), 8, 8);
                dc.DrawEllipse(HeadGlow, null, new Point(8, 2), 8, 8);
                dc.DrawEllipse(RoofLight, null, new Point(-9, -14), 1.2, 1.2);
                dc.DrawEllipse(RoofLight, null, new Point(9, -14), 1.2, 1.2);
            }
            else
            {
                dc.DrawEllipse(TailGlow, null, new Point(-8, 2), 7, 7);
                dc.DrawEllipse(TailGlow, null, new Point(8, 2), 7, 7);
            }
            dc.Pop(); dc.Pop();
        }
    }
}
