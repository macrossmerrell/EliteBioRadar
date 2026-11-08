using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The FSD becoming mass locked or the lock clearing: a ring of 24 ticks around the ship. LOCKED: the ticks go dark and red one after another while a red slash strikes through the "FSD"
    // label. CLEARED: the slash breaks away, the ticks relight in mint round the ring and a pulse goes out. Detail = "LOCKED" or "CLEARED". About 4.4 seconds.
    public sealed class MassLockVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.8, CentreX = 120, CentreY = 54, Radius = 34;
        private const int Ticks = 24;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Red = Color.FromRgb(255, 100, 80), Mint = Color.FromRgb(125, 255, 196);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), RedBrush = Frozen(Red), MintBrush = Frozen(Mint), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30));
        private static readonly Pen ShipEdge = FrozenPen(Cyan, 1.1), RedPlateEdge = FrozenPen(Color.FromArgb(153, Red.R, Red.G, Red.B), 1), MintPlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1);
        private static readonly Pen CyanTick = FrozenPen(Color.FromArgb(230, Cyan.R, Cyan.G, Cyan.B), 2.2), RedTick = FrozenPen(Color.FromArgb(89, Red.R, Red.G, Red.B), 2.2);
        private static readonly Pen[] MintTick = MakeMintTicks(), RedSlash = AlphaPens(Red, 2.6), MintPulse = AlphaPens(Mint, 1.8);
        private static readonly double[] Cos = MakeTrig(Math.Cos), Sin = MakeTrig(Math.Sin);

        // Mint ticks at nine levels from dim (alpha 0.25) to bright (0.95).
        private static Pen[] MakeMintTicks()
        {
            var a = new Pen[9];
            for (int i = 0; i <= 8; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255 * (0.25 + 0.7 * i / 8)), Mint.R, Mint.G, Mint.B), 2.2);
            return a;
        }

        private static double[] MakeTrig(Func<double, double> f)
        {
            var a = new double[Ticks];
            for (int i = 0; i < Ticks; i++) a[i] = f(i * 2 * Math.PI / Ticks - Math.PI / 2);
            return a;
        }

        private bool _locked = true;
        private string _line1 = "", _line2 = "";
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _shipT = new();

        protected override void OnStart(string subject, string detail)
        {
            _locked = !(detail ?? "").Contains("CLEAR", StringComparison.OrdinalIgnoreCase);
            _line1 = _locked ? "MASS LOCKED" : "MASS LOCK CLEARED";
            _line2 = _locked ? "FSD UNAVAILABLE" : "FSD READY";
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
                Pen pen;
                if (_locked) pen = 1 - EaseOut(Phase(t, 0.2 + i * 0.025, 0.25)) > 0.5 ? CyanTick : RedTick;
                else pen = MintTick[(int)Math.Round(EaseOut(Phase(t, 0.35 + i * 0.04, 0.25)) * 8)];
                dc.DrawLine(pen, new Point(CentreX + Cos[i] * (Radius - 3), CentreY + Sin[i] * (Radius - 3)), new Point(CentreX + Cos[i] * (Radius + 3), CentreY + Sin[i] * (Radius + 3)));
            }

            if (_locked)
            {
                double s = EaseOut(Phase(t, 0.9, 0.4)), l = (Radius + 8) * s * 0.8;
                if (s > 0) dc.DrawLine(RedSlash[(int)Math.Round(0.95 * s * 8)], new Point(CentreX - l, CentreY + l), new Point(CentreX + l, CentreY - l));
            }
            else
            {
                // The old slash drifts off and fades, then a mint pulse goes out.
                double sp = Phase(t, 0.1, 0.5);
                if (sp > 0 && sp < 1)
                {
                    double d = sp * 14;
                    dc.DrawLine(RedSlash[(int)Math.Round(0.9 * (1 - sp) * 8)], new Point(CentreX - 27 + d, CentreY + 27 + d), new Point(CentreX + 27 + d, CentreY - 27 + d));
                }
                double b = Phase(t, 1.4, 0.7);
                if (b > 0 && b < 1) dc.DrawEllipse(null, MintPulse[(int)Math.Round((1 - b) * 8)], new Point(CentreX, CentreY), Radius + 4 + b * 24, Radius + 4 + b * 24);
            }
            DrawSpaced(dc, "FSD", MonoFace, 10, _locked ? RedBrush : MintBrush, SceneW / 2, CentreY + Radius + 6, 0.1, 0.9);

            dc.DrawRoundedRectangle(PlateBrush, _locked ? RedPlateEdge : MintPlateEdge, new Rect(26, 106, 188, 40), 6, 6);
            DrawSpaced(dc, _line1, MonoFace, 13, _locked ? RedBrush : MintBrush, SceneW / 2, 112, 0.1, Phase(t, 0.1, 0.3));
            DrawSpaced(dc, _line2, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 1.0, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
