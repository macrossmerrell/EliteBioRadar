using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The plotted route as a little constellation: eight stars joined by a line, each coloured by its star class, the last one ringed as the destination.
    // PLOTTED: the line draws out star by star and a pulse runs down it. CANCELLED: the line breaks from the fourth star on, goes red and dashed, and a red cross strikes the break.
    // COMPLETE: your ship marker flies along the line, lighting every star it passes, and a mint burst goes out from the destination.
    // Detail = "PLOTTED|jumps|ly|classes", "CANCELLED|jumps|ly|classes" or "COMPLETE|jumps|ly|classes"; classes = eight star-class letters sampled along the route.
    public sealed class RouteVisual : AnimationVisual
    {
        private enum Stage { Plotted, Cancelled, Complete }
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.3;
        private const int N = 8;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(125, 255, 196), Red = Color.FromRgb(255, 100, 80), Grey = Color.FromRgb(120, 130, 140);
        private static readonly Color[] StarColours =
        {
            Color.FromRgb(170, 200, 255), Color.FromRgb(255, 245, 230), Color.FromRgb(255, 235, 150),
            Color.FromRgb(255, 170, 90), Color.FromRgb(255, 120, 100), Color.FromRgb(200, 220, 255),
        };
        private static readonly double[] NodeX = MakeX(), NodeY = { 30, 64, 36, 58, 26, 62, 40, 48 };
        private static double[] MakeX() { var a = new double[N]; for (int i = 0; i < N; i++) a[i] = 24 + i * 27; return a; }

        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), CyanBrush = Frozen(Cyan), MintBrush = Frozen(Mint), RedBrush = Frozen(Red);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30));
        private static readonly Pen CyanPlate = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), MintPlate = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), RedPlate = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1);
        private static readonly Pen CyanShip = FrozenPen(Cyan, 2), MintShip = FrozenPen(Mint, 2), RedShip = FrozenPen(Red, 2);
        // Nine alpha levels each.
        private static readonly Pen[] CyanLine = AlphaPens(Cyan, 1.4), MintLine = AlphaPens(Mint, 1.4), RedLine = AlphaPens(Red, 1.4);
        private static readonly Pen[] CyanRing = AlphaPens(Cyan, 1.2), MintRing = AlphaPens(Mint, 1.2), RedRing = AlphaPens(Red, 1.2), MintBurst = AlphaPens(Mint, 2), RedCross = AlphaPens(Red, 2.4);
        private static readonly Pen[] DeadLine = MakeDashed();
        private static readonly Brush[] CyanDot = AlphaBrushes(Cyan), MintDot = AlphaBrushes(Mint), RedDot = AlphaBrushes(Red), GreyDot = AlphaBrushes(Grey);
        private static readonly Brush[][] StarDot = MakeStarDots();
        private static readonly RotateTransform Nose90 = MakeNose90();
        private static RotateTransform MakeNose90() { var r = new RotateTransform(90); r.Freeze(); return r; }

        private static Pen[] MakeDashed()
        {
            var a = new Pen[9];
            for (int i = 0; i < 9; i++)
            {
                var p = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255.0 * i / 8), Red.R, Red.G, Red.B)), 1.4) { DashStyle = new DashStyle(new[] { 1.4, 2.9 }, 0) };
                p.Brush.Freeze(); p.DashStyle.Freeze(); p.Freeze();
                a[i] = p;
            }
            return a;
        }

        private static Brush[][] MakeStarDots()
        {
            var a = new Brush[StarColours.Length][];
            for (int i = 0; i < a.Length; i++) a[i] = AlphaBrushes(StarColours[i]);
            return a;
        }

        private static int ClassIndex(char c) => char.ToUpperInvariant(c) switch { 'O' or 'B' => 0, 'A' or 'F' => 1, 'G' => 2, 'K' => 3, 'M' => 4, _ => 5 };

        private Stage _stage = Stage.Plotted;
        private string _line1 = "", _line2 = "";
        private readonly int[] _class = new int[N];
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _shipT = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _stage = parts[0].Contains("CANCEL", StringComparison.OrdinalIgnoreCase) ? Stage.Cancelled : parts[0].Contains("COMPLETE", StringComparison.OrdinalIgnoreCase) ? Stage.Complete : Stage.Plotted;
            string jumps = parts.Length > 1 ? parts[1] : "", ly = parts.Length > 2 ? parts[2] : "", cls = parts.Length > 3 ? parts[3] : "";
            for (int i = 0; i < N; i++) _class[i] = ClassIndex(cls.Length > 0 ? cls[i % cls.Length] : "KMFKMAFK"[i]);
            string jumpText = jumps == "1" ? "1 JUMP" : jumps + " JUMPS";
            switch (_stage)
            {
                case Stage.Cancelled:
                    _line1 = "ROUTE CANCELLED"; _line2 = "NAV ROUTE CLEARED";
                    break;
                case Stage.Complete:
                    _line1 = "ROUTE COMPLETE"; _line2 = jumps.Length > 0 ? jumpText + " - ARRIVED" : "ARRIVED";
                    break;
                default:
                    _line1 = "ROUTE PLOTTED"; _line2 = jumps.Length > 0 ? jumpText + (ly.Length > 0 ? " - " + ly + " LY" : "") : "";
                    break;
            }
        }

        private static (double x, double y) ShipAt(double u)
        {
            double f = u * (N - 1);
            int i = Math.Min(N - 2, (int)Math.Floor(f));
            double q = f - i;
            return (NodeX[i] + (NodeX[i + 1] - NodeX[i]) * q, NodeY[i] + (NodeY[i + 1] - NodeY[i]) * q);
        }

        private static int Lvl(double a) => Math.Clamp((int)Math.Round(a * 8), 0, 8);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            bool plotted = _stage == Stage.Plotted, cancelled = _stage == Stage.Cancelled, complete = _stage == Stage.Complete;
            var line = cancelled ? RedLine : complete ? MintLine : CyanLine;
            var ring = cancelled ? RedRing : complete ? MintRing : CyanRing;
            var routeDot = cancelled ? RedDot : complete ? MintDot : CyanDot;
            var shipPen = cancelled ? RedShip : complete ? MintShip : CyanShip;
            var head = cancelled ? RedBrush : complete ? MintBrush : CyanBrush;
            var plateEdge = cancelled ? RedPlate : complete ? MintPlate : CyanPlate;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double draw = plotted ? Phase(t, 0.2, 1.8) : 1, cut = cancelled ? Phase(t, 0.6, 0.5) : 0, shipU = complete ? Phase(t, 0.2, 2.4) : 0;
            double shipPos = shipU * (N - 1);

            for (int i = 0; i < N - 1; i++)
            {
                double seg = Clamp01(draw * (N - 1) - i);
                if (seg <= 0) continue;
                bool dead = cancelled && i >= 2 && cut > 0;
                var pen = dead ? DeadLine[Lvl(0.25 * (1 - cut) + 0.1)] : line[Lvl(complete && shipPos > i + 1 ? 0.9 : 0.4)];
                dc.DrawLine(pen, new Point(NodeX[i], NodeY[i]), new Point(NodeX[i] + (NodeX[i + 1] - NodeX[i]) * seg, NodeY[i] + (NodeY[i + 1] - NodeY[i]) * seg));
            }

            for (int i = 0; i < N; i++)
            {
                double on = plotted ? Phase(draw * (N - 1), i - 0.2, 0.3) : 1;
                if (on <= 0) continue;
                bool last = i == N - 1, dead = cancelled && i >= 2, visited = complete && shipPos >= i - 0.05;
                double r = (last ? 5 : 3) * (last ? 1 : 1 + (i % 3) * 0.3) * (0.5 + 0.5 * on);
                double al = (dead ? 0.35 * (1 - cut) + 0.2 : visited || last ? 1 : 0.7) * on;
                Brush[] set = dead ? GreyDot : (!visited && !cancelled) ? StarDot[_class[i]] : routeDot;
                dc.DrawEllipse(set[Lvl(al)], null, new Point(NodeX[i], NodeY[i]), r, r);
                if (last) dc.DrawEllipse(null, ring[Lvl(0.8 * on)], new Point(NodeX[i], NodeY[i]), 9 * on, 9 * on);
            }

            if (cancelled && cut > 0)
            {
                double mx = NodeX[3], my = NodeY[3], c8 = 16 * cut;
                var cp = RedCross[Lvl(cut)];
                dc.DrawLine(cp, new Point(mx - 8, my - 8), new Point(mx - 8 + c8, my - 8 + c8));
                dc.DrawLine(cp, new Point(mx + 8, my - 8), new Point(mx + 8 - c8, my - 8 + c8));
            }

            // Your ship marker rides the line (flies along it on COMPLETE).
            var (sx, sy) = ShipAt(shipU);
            _shipT.X = sx; _shipT.Y = sy - 10; _shipS.ScaleX = _shipS.ScaleY = 0.5;
            dc.PushTransform(_shipT); dc.PushTransform(Nose90); dc.PushTransform(_shipS);
            dc.DrawGeometry(ShipFill, shipPen, ShipOutline);
            dc.Pop(); dc.Pop(); dc.Pop();

            if (complete)
            {
                double b = Phase(t, 2.6, 0.9);
                if (b > 0 && b < 1) dc.DrawEllipse(null, MintBurst[Lvl((1 - b) * 0.9)], new Point(NodeX[N - 1], NodeY[N - 1]), 8 + b * 34, 8 + b * 34);
            }
            if (plotted)
            {
                double q = Phase(t, 2.0, 1.4);
                if (q > 0 && q < 1)
                {
                    var (qx, qy) = ShipAt(q);
                    dc.DrawEllipse(CyanDot[Lvl((1 - q) * 0.9)], null, new Point(qx, qy), 2.4, 2.4);
                }
            }

            double pt = plotted ? 2.0 : complete ? 2.8 : 1.2, pa = Phase(t, pt, 0.4);
            if (pa > 0)
            {
                if (pa < 0.999) dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, plateEdge, new Rect(26, 106, 188, 40), 6, 6);
                if (pa < 0.999) dc.Pop();
            }
            DrawSpaced(dc, _line1, MonoFace, 13, head, SceneW / 2, 112, 0.1, pa);
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, pt + 0.3, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
