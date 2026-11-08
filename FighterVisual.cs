using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A ship-launched fighter going out or coming home, drawn side-on with the mothership's nose pointing right. LAUNCH: a door at the nose glows, the fighter slips out and flies off ahead
    // while a dashed tether stretches out behind it. RECOVER: the ship sits further forward and the fighter comes in straight from the rear along a shortening tether, the tail door
    // glows, the fighter shrinks into it and a lock ring pulses. Detail = "LAUNCH|YOU", "LAUNCH|CREW" (who is flying it) or "RECOVER".
    public sealed class FighterVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.8, MidY = 52, LaunchX = 96, RecoverX = 150, ShipScale = 1.9, FighterScale = 1.5;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(255, 184, 77);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), CyanBrush = Frozen(Cyan), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30)), FighterFill = Frozen(Color.FromRgb(0x2a, 0x2f, 0x26)), GlintBrush = Frozen(Color.FromArgb(230, 255, 230, 180));
        private static readonly Pen CyanEdge = FrozenPen(Cyan, 1.1), AmberEdge = FrozenPen(Amber, 1), PlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1);
        private static readonly Pen TetherPen = FrozenPen(Color.FromArgb(166, Cyan.R, Cyan.G, Cyan.B), 1.2), TickPen = FrozenPen(Color.FromArgb(115, Cyan.R, Cyan.G, Cyan.B), 1);
        private static readonly Pen[] AmberRing = AlphaPens(Amber, 1.4), CyanRing = AlphaPens(Cyan, 1.6), AmberDoorEdge = AlphaPens(Amber, 1);
        private static readonly Brush[] AmberDoor = AlphaBrushes(Amber);
        private static readonly Geometry Mothership = ShipOutline, Dart = Poly(new[] { (0.0, -8.0), (5, 6), (0, 3), (-5, 6) });
        private static readonly RotateTransform Nose90 = MakeNose90();
        private static RotateTransform MakeNose90() { var r = new RotateTransform(90); r.Freeze(); return r; }

        private bool _launch = true;
        private string _control = "", _line1 = "", _line2 = "";
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new(), _dartS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _shipT = new(), _dartT = new();

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _launch = !parts[0].Contains("RECOVER", StringComparison.OrdinalIgnoreCase);
            _control = parts.Length > 1 && parts[1].Contains("CREW", StringComparison.OrdinalIgnoreCase) ? "CONTROL: CREW" : "CONTROL: YOU";
            _line1 = _launch ? "FIGHTER LAUNCHED" : "FIGHTER RECOVERED";
            _line2 = _launch ? _control : "SAFE IN HANGAR";
        }

        private void DrawShip(DrawingContext dc, double mx)
        {
            _shipT.X = mx; _shipT.Y = MidY; _shipS.ScaleX = _shipS.ScaleY = ShipScale;
            dc.PushTransform(_shipT); dc.PushTransform(Nose90); dc.PushTransform(_shipS);
            dc.DrawGeometry(ShipFill, CyanEdge, Mothership);
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        private void DrawFighter(DrawingContext dc, double x, double s)
        {
            _dartT.X = x; _dartT.Y = MidY; _dartS.ScaleX = _dartS.ScaleY = s;
            dc.PushTransform(_dartT); dc.PushTransform(Nose90); dc.PushTransform(_dartS);
            dc.DrawGeometry(FighterFill, AmberEdge, Dart);
            dc.Pop(); dc.Pop(); dc.Pop();
            dc.DrawEllipse(GlintBrush, null, new Point(x - 8 * s, MidY), 1.5, 1.5);
        }

        private static void DrawDoor(DrawingContext dc, double x, double a)
        {
            a = Clamp01(a);
            dc.DrawRectangle(AmberDoor[(int)Math.Round((0.15 + 0.6 * a) * 8)], null, new Rect(x - 2, MidY - 7, 4, 14));
            dc.DrawRectangle(null, AmberDoorEdge[(int)Math.Round((0.4 + 0.5 * a) * 8)], new Rect(x - 1.5, MidY - 6.5, 3, 13));
        }

        // The dashed tether from a to b with tick marks; the dashes run along it (outwards when `outward`).
        private static void DrawTether(DrawingContext dc, double a, double b, int ticks, double t, bool outward)
        {
            if (b - a <= 2) return;
            double shift = (t * 20) % 6;
            if (outward) shift = 6 - shift;
            for (double x = a - 6 + shift; x < b; x += 6)
            {
                double x0 = Math.Max(a, x), x1 = Math.Min(b, x + 3);
                if (x1 > x0) dc.DrawLine(TetherPen, new Point(x0, MidY), new Point(x1, MidY));
            }
            for (int i = 1; i <= ticks; i++)
            {
                double tx = a + (b - a) * i / (ticks + 1);
                dc.DrawLine(TickPen, new Point(tx, MidY - 4), new Point(tx, MidY + 4));
            }
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

            if (_launch)
            {
                double noseX = LaunchX + 27, p = EaseOut(Phase(t, 0.5, 2.0)), fx = noseX + 4 + p * (205 - noseX - 4);
                DrawShip(dc, LaunchX);
                DrawDoor(dc, noseX - 1, Phase(t, 0.1, 0.3) * (1 - Phase(t, 0.9, 0.5)));
                DrawFighter(dc, fx, FighterScale);
                DrawTether(dc, noseX + 6, fx - 10, 5, t, true);
                double rr = Phase(t, 0.5, 0.8);
                if (rr < 1) dc.DrawEllipse(null, AmberRing[(int)Math.Round((1 - rr) * 8)], new Point(noseX, MidY), 6 + rr * 22, 6 + rr * 22);
            }
            else
            {
                double tailX = RecoverX - 27, q = Smooth(0, 1, Phase(t, 0.4, 2.0)), fx = 14 + q * (tailX + 2 - 14);
                DrawShip(dc, RecoverX);
                DrawDoor(dc, tailX + 1, Phase(q, 0.6, 0.3) * (1 - Phase(t, 2.8, 0.4)));
                double sc = q > 0.85 ? FighterScale * (1 - (q - 0.85) / 0.15) : FighterScale;
                if (sc > 0.12) DrawFighter(dc, fx, sc);
                if (q < 0.9) DrawTether(dc, fx + 10, RecoverX - 14, 4, t, false);
                double lk = Phase(t, 2.2, 0.7);
                if (lk > 0 && lk < 1) dc.DrawEllipse(null, CyanRing[(int)Math.Round((1 - lk) * 8)], new Point(RecoverX, MidY), 28 + lk * 18, 28 + lk * 18);
            }

            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _line1, MonoFace, 13, CyanBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, _launch ? 1.0 : 2.4, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
