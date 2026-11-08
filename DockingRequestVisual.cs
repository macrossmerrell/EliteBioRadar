using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The station's answer to a docking request. REQUESTED: your ship (nose right) sends dots across to the station. GRANTED: a landing pad with four corner lights that come on green one by one and
    // the pad number in the middle. DENIED: the pad lights flicker red, the pad dims and a cross strikes through it, with the reason on the plate.
    // Subject = the station name, Detail = "REQUESTED", "GRANTED|pad" or "DENIED|reason".
    public sealed class DockingRequestVisual : AnimationVisual
    {
        private enum Stage { Requested, Granted, Denied }
        private const double SceneW = 240, SceneH = 150, ShipX = 44, ShipY = 54, StationX = 190, StationY = 50, PadX = 120, PadY = 52, PadHalf = 28, PadFontSize = 26;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(125, 255, 196), Red = Color.FromRgb(255, 100, 80);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), CyanBrush = Frozen(Cyan), MintBrush = Frozen(Mint), RedBrush = Frozen(Red);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30)), StationFill = Frozen(Color.FromRgb(0x14, 0x1c, 0x24));
        private static readonly Pen ShipEdge = FrozenPen(Cyan, 1.1), StationEdge = FrozenPen(Color.FromArgb(217, Cyan.R, Cyan.G, Cyan.B), 1.4), StationRing = FrozenPen(Color.FromArgb(102, Cyan.R, Cyan.G, Cyan.B), 1.4);
        private static readonly Pen CyanPlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), MintPlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), RedPlateEdge = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1);
        // Nine alpha levels each.
        private static readonly Brush[] CyanFill = AlphaBrushes(Cyan), MintFill = AlphaBrushes(Mint), RedFill = AlphaBrushes(Red);
        private static readonly Pen[] MintPad = AlphaPens(Mint, 1.4), RedPad = AlphaPens(Red, 1.4), RedCross = AlphaPens(Red, 2.6);
        private static readonly Brush StationSlot = Frozen(Color.FromArgb(51, Cyan.R, Cyan.G, Cyan.B));
        private static readonly Geometry Octagon = MakeOctagon();
        private static readonly RotateTransform Nose90 = MakeNose90();
        private static RotateTransform MakeNose90() { var r = new RotateTransform(90); r.Freeze(); return r; }

        private static Geometry MakeOctagon()
        {
            var pts = new (double x, double y)[8];
            for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4 + Math.PI / 8; pts[i] = (StationX + Math.Cos(a) * 22, StationY + Math.Sin(a) * 22); }
            return Poly(pts);
        }

        private Stage _stage = Stage.Requested;
        private string _line1 = "", _line2 = "", _pad = "";
        private double _padTop = PadY - 13;   // the top of the pad-number line that puts the digits' middle on the pad centre
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _shipT = new();

        // The request is a quick "working on it" while the answer is on its way; the answers stay up a little longer.
        protected override double Duration => _stage == Stage.Requested ? 2.6 : 4.4;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            string extra = parts.Length > 1 ? parts[1] : "";
            _stage = parts[0].Contains("GRANT", StringComparison.OrdinalIgnoreCase) ? Stage.Granted : parts[0].Contains("DENIED", StringComparison.OrdinalIgnoreCase) ? Stage.Denied : Stage.Requested;
            switch (_stage)
            {
                case Stage.Granted:
                    _pad = extra;
                    var probe = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, PadFontSize, MintBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    _padTop = PadY - (probe.Baseline - 0.32 * PadFontSize);   // digits are about 0.64 em tall, so their middle is 0.32 em above the baseline
                    _line1 = "DOCKING GRANTED"; _line2 = "LANDING PAD " + extra;
                    break;
                case Stage.Denied:
                    _line1 = "DOCKING DENIED"; _line2 = extra.ToUpperInvariant();
                    break;
                default:
                    _line1 = "DOCKING REQUESTED"; _line2 = (subject ?? "").ToUpperInvariant();
                    break;
            }
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, Duration - 0.6);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            if (_stage == Stage.Requested) DrawRequest(dc, t); else DrawPad(dc, t);

            var edge = _stage == Stage.Granted ? MintPlateEdge : _stage == Stage.Denied ? RedPlateEdge : CyanPlateEdge;
            var head = _stage == Stage.Granted ? MintBrush : _stage == Stage.Denied ? RedBrush : CyanBrush;
            dc.DrawRoundedRectangle(PlateBrush, edge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _line1, MonoFace, 13, head, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, _stage == Stage.Requested ? 0.6 : 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

        // Your ship and the station, with dots running across the gap.
        private void DrawRequest(DrawingContext dc, double t)
        {
            _shipT.X = ShipX; _shipT.Y = ShipY; _shipS.ScaleX = _shipS.ScaleY = 1.1;
            dc.PushTransform(_shipT); dc.PushTransform(Nose90); dc.PushTransform(_shipS);
            dc.DrawGeometry(ShipFill, ShipEdge, ShipOutline);
            dc.Pop(); dc.Pop(); dc.Pop();

            dc.DrawGeometry(StationFill, StationEdge, Octagon);
            dc.DrawEllipse(null, StationRing, new Point(StationX, StationY), 11, 11);
            dc.DrawRectangle(StationSlot, null, new Rect(StationX - 4, StationY - 4, 8, 8));

            double from = ShipX + 16, to = StationX - 22;
            for (int j = 0; j < 3; j++)
            {
                double q = (t * 0.9 + j / 3.0) % 1;
                int al = (int)Math.Round(0.9 * (1 - Math.Abs(q - 0.5) * 1.2) * 8);
                dc.DrawEllipse(CyanFill[Math.Clamp(al, 0, 8)], null, new Point(from + (to - from) * q, StationY + 4), 2, 2);
            }
        }

        // The landing pad: GRANTED lights it green with the pad number, DENIED flickers it red and crosses it out.
        private void DrawPad(DrawingContext dc, double t)
        {
            bool granted = _stage == Stage.Granted;
            double fade = granted ? 1 : 1 - 0.6 * EaseOut(Phase(t, 1.0, 0.5));
            var pen = granted ? MintPad : RedPad;
            int lvl = (int)Math.Round(0.5 * fade * 8);
            dc.DrawRoundedRectangle(null, pen[lvl], new Rect(PadX - PadHalf, PadY - PadHalf, PadHalf * 2, PadHalf * 2), 5, 5);
            dc.DrawEllipse(null, pen[lvl], new Point(PadX, PadY), PadHalf * 0.62, PadHalf * 0.62);

            for (int i = 0; i < 4; i++)
            {
                double on, al;
                if (granted) { on = EaseOut(Phase(t, 0.3 + i * 0.2, 0.3)); al = 0.25 + 0.75 * on; }
                else { on = Math.Sin(t * 22 + i * 1.7) > 0 ? 1 : 0.25; al = on * (1 - Phase(t, 1.2, 0.5) * 0.7); }
                double cx = PadX + (i == 0 || i == 3 ? -PadHalf : PadHalf), cy = PadY + (i < 2 ? -PadHalf : PadHalf);
                dc.DrawEllipse((granted ? MintFill : RedFill)[(int)Math.Round(al * 8)], null, new Point(cx, cy), 3.2, 3.2);
            }

            if (granted) DrawSpaced(dc, _pad, MonoFace, PadFontSize, MintBrush, SceneW / 2, _padTop, 0.1, EaseOut(Phase(t, 1.1, 0.5)));
            else
            {
                double c = EaseOut(Phase(t, 0.5, 0.4)), r = 13;
                int cl = (int)Math.Round(c * 8);
                dc.DrawLine(RedCross[cl], new Point(PadX - r, PadY - r), new Point(PadX + r, PadY + r));
                dc.DrawLine(RedCross[cl], new Point(PadX + r, PadY - r), new Point(PadX - r, PadY + r));
            }
        }
    }
}
