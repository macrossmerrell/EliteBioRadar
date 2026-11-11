using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // How an interdiction ended (the journal's EscapeInterdiction / Interdicted): the red vortex winds down behind the ship while an interdiction card appears and a stamp thumps down,
    // EVADED in cyan, SUBMITTED in amber or PULLED OUT in red (the interdictor won). Detail = "EVADED|interdictor", "SUBMITTED|interdictor" or "PULLED|interdictor"; the name may be empty.
    public sealed class InterdictionOutcomeVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.3, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 50;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Red = Color.FromRgb(0xff, 0x46, 0x32), Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xff, 0xbe, 0x46);
        private static readonly Brush HullBrush = Frozen(Color.FromRgb(0xcf, 0xea, 0xea)), HeadBrush = Frozen(Color.FromRgb(0xc9, 0xa8, 0xa2)), SubBrush = Frozen(Color.FromRgb(0xff, 0xd0, 0xc8));
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), RedBrush = Frozen(Red);
        private static readonly Pen[] Streak = AlphaPens(Red, 1.2);
        private static readonly Pen CyanEdge = FrozenPen(Color.FromArgb(140, Cyan.R, Cyan.G, Cyan.B), 1), AmberEdge = FrozenPen(Color.FromArgb(140, Amber.R, Amber.G, Amber.B), 1), RedEdge = FrozenPen(Color.FromArgb(140, Red.R, Red.G, Red.B), 1);
        private static readonly Pen CyanStamp = FrozenPen(Cyan, 1.6), AmberStamp = FrozenPen(Amber, 1.6), RedStamp = FrozenPen(Red, 1.6);
        private static readonly Pen CyanShip = FrozenPen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B), 1), AmberShip = FrozenPen(Color.FromArgb(204, Amber.R, Amber.G, Amber.B), 1), RedShip = FrozenPen(Color.FromArgb(204, Red.R, Red.G, Red.B), 1);

        private int _outcome;   // 0 evaded, 1 submitted, 2 pulled out
        private string _name = "";
        private double _phase, _lastT;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _outcome = kind.StartsWith("EVAD") ? 0 : kind.StartsWith("SUB") ? 1 : 2;
            _name = p.Length > 1 ? p[1].Trim() : "";
            _phase = 0.37 * _outcome; _lastT = 0;
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));
        private static double Smooth(double x) => x * x * (3 - 2 * x);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            int o = _outcome;
            Brush colBrush = o == 0 ? CyanBrush : o == 1 ? AmberBrush : RedBrush;
            Pen edge = o == 0 ? CyanEdge : o == 1 ? AmberEdge : RedEdge, stamp = o == 0 ? CyanStamp : o == 1 ? AmberStamp : RedStamp, shipPen = o == 0 ? CyanShip : o == 1 ? AmberShip : RedShip;
            string title = o == 0 ? "EVADED" : o == 1 ? "SUBMITTED" : "PULLED OUT";
            string sub = o == 0 ? "INTERDICTION ESCAPED" : o == 1 ? "DROPPING OUT OF SUPERCRUISE" : "DRAGGED OUT BY THE INTERDICTOR";

            // The vortex winds down: it spins in more slowly and fades, so the card has the stage.
            _phase += Math.Clamp(t - _lastT, 0, 0.1) * 0.45 * (1 - Phase(t, 0, 0.8) * 0.85);
            _lastT = t;
            double va = 0.35 * (1 - Phase(t, 1.5, 1.4));

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            if (va > 0.01)
                for (int i = 0; i < 46; i++)
                {
                    double hh = Hash(i), q = ((_phase * (0.6 + hh) + hh) % 1 + 1) % 1, r = 64 * (1 - q) + 4, an = hh * 6.2832 + q * q * 5;
                    double a2 = an - 0.18 * (1 - q * 0.5), r2 = r + 6 * (1 - q);
                    int lvl = (int)Math.Round(0.9 * Math.Min(1, q * 4) * (1 - q * 0.2) * va * 8);
                    if (lvl > 0) dc.DrawLine(Streak[lvl], new Point(CentreX + Math.Cos(a2) * r2, CentreY + Math.Sin(a2) * r2 * 0.8), new Point(CentreX + Math.Cos(an) * r, CentreY + Math.Sin(an) * r * 0.8));
                }

            // The ship shudders for a moment (hard if it was pulled out), then a small lurch: forward if it got away.
            double sx = CentreX, sy = CentreY, rot = 0;
            if (t < 0.3) { sx += Math.Sin(t * (o == 2 ? 55 : 60)) * 1.2; rot = Math.Sin(t * 20) * 0.1 * 180 / Math.PI; }
            if (o == 0) sy -= 4 * Smooth(Phase(t, 0.5, 0.5)) * (1 - Phase(t, 1.4, 1.2));
            if (o == 2 && t < 0.5) sx += Math.Sin(t * 55) * 1.4;
            dc.PushTransform(new TranslateTransform(sx, sy));
            dc.PushTransform(new RotateTransform(rot));
            dc.DrawGeometry(HullBrush, shipPen, ShipOutline);
            dc.Pop(); dc.Pop();

            // The card, the stamp thumping down on it, and what happened under it.
            dc.DrawRoundedRectangle(LowFuelVisual.CritPlateBrush, edge, new Rect(20, 90, 200, 56), 6, 6);
            string head = _name.Length > 0 ? "INTERDICTED BY " + _name.ToUpperInvariant() : "INTERDICTION";
            DrawSpaced(dc, head, MonoFace, Fit(head, 9, 184), HeadBrush, CentreX, 95, 0.1, Phase(t, 0, 0.2));

            double sa = Phase(t, 0.3, 0.12);
            if (sa > 0)
            {
                double sc = 1 + 0.8 * (1 - EaseOut(Phase(t, 0.3, 0.2))), bw = title.Length * 12 + 10;
                dc.PushOpacity(sa);
                dc.PushTransform(new TranslateTransform(CentreX, 117));
                dc.PushTransform(new RotateTransform(-11.5));
                dc.PushTransform(new ScaleTransform(sc, sc));
                dc.DrawRoundedRectangle(null, stamp, new Rect(-bw / 2, -14, bw, 28), 3, 3);
                DrawSpaced(dc, title, MonoFace, 20, colBrush, 0, -12, 0.05, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }
            DrawSpaced(dc, sub, MonoFace, Fit(sub, 9, 184), SubBrush, CentreX, 134, 0.08, Phase(t, 0.7, 0.3));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
