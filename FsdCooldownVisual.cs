using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The FSD cooling down after a jump, and ready again: the mass-lock ring of 24 ticks round the ship, heated. COOLING: the ticks ignite orange one after another and flicker outwards.
    // READY: they cool back to cyan in turn and a pulse goes out. Detail = "COOLING" or "READY". About 4.4 seconds.
    public sealed class FsdCooldownVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.8, CentreX = 120, CentreY = 54, Radius = 34;
        private const int Ticks = 24;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Hot = Color.FromRgb(255, 150, 60);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), HotBrush = Frozen(Hot), CyanBrush = Frozen(Cyan), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30));
        private static readonly Pen ShipEdge = FrozenPen(Cyan, 1.1), HotPlateEdge = FrozenPen(Color.FromArgb(153, Hot.R, Hot.G, Hot.B), 1), CyanPlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1);
        // Tick pens from cool (0) to hot (8): the colour mixes from cyan to orange and the pen thickens a little.
        private static readonly Pen[] TickPen = MakeTicks();
        private static readonly Pen[] ReadyPulse = AlphaPens(Cyan, 1.8);
        private static readonly double[] Cos = MakeTrig(Math.Cos), Sin = MakeTrig(Math.Sin);

        private static Pen[] MakeTicks()
        {
            var a = new Pen[9];
            for (int i = 0; i <= 8; i++) a[i] = FrozenPen(Mix(Cyan, Hot, i / 8.0), 2.2 + 0.8 * i / 8);
            return a;
        }

        private static double[] MakeTrig(Func<double, double> f)
        {
            var a = new double[Ticks];
            for (int i = 0; i < Ticks; i++) a[i] = f(i * 2 * Math.PI / Ticks - Math.PI / 2);
            return a;
        }

        private bool _cooling = true;
        private string _line1 = "", _line2 = "";
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _shipT = new();

        protected override void OnStart(string subject, string detail)
        {
            _cooling = !(detail ?? "").Contains("READY", StringComparison.OrdinalIgnoreCase);
            _line1 = _cooling ? "FSD COOLING" : "FSD READY";
            _line2 = _cooling ? "CHARGE UNAVAILABLE" : "JUMP WHEN READY";
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

            _shipT.X = CentreX; _shipT.Y = CentreY; _shipS.ScaleX = _shipS.ScaleY = 1.1;
            dc.PushTransform(_shipT); dc.PushTransform(_shipS);
            dc.DrawGeometry(ShipFill, ShipEdge, ShipOutline);
            dc.Pop(); dc.Pop();

            for (int i = 0; i < Ticks; i++)
            {
                double heat = _cooling ? EaseOut(Phase(t, 0.2 + i * 0.03, 0.3)) : 1 - EaseOut(Phase(t, 0.3 + i * 0.04, 0.4));
                double reach = Radius + 3 + heat * 3 * (0.6 + 0.4 * Math.Sin(t * 10 + i));   // hot ticks flicker outwards
                dc.DrawLine(TickPen[(int)Math.Round(heat * 8)], new Point(CentreX + Cos[i] * (Radius - 3), CentreY + Sin[i] * (Radius - 3)), new Point(CentreX + Cos[i] * reach, CentreY + Sin[i] * reach));
            }

            if (!_cooling)
            {
                double fl = Phase(t, 1.5, 0.7);
                if (fl > 0 && fl < 1) dc.DrawEllipse(null, ReadyPulse[(int)Math.Round((1 - fl) * 8)], new Point(CentreX, CentreY), Radius + 4 + fl * 24, Radius + 4 + fl * 24);
            }
            DrawSpaced(dc, "FSD", MonoFace, 10, _cooling ? HotBrush : CyanBrush, SceneW / 2, CentreY + Radius + 6, 0.1, 0.9);

            dc.DrawRoundedRectangle(PlateBrush, _cooling ? HotPlateEdge : CyanPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _line1, MonoFace, 13, _cooling ? HotBrush : CyanBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
