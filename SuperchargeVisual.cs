using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // FSD supercharge from a neutron star or white dwarf jet cone. A ring of 28 segments fills around the star as the drive charges while the multiplier counts up from x1 to the boost,
    // sparks arc off the star, and a ripple goes out when the ring is full. Neutron stars are icy cyan, white dwarfs cream. The plate gives the star type, the boost and, when the
    // app knows the ship's jump range, what the next jump's range becomes.
    // Detail = "NEUTRON|4|62.3" or "WHITE DWARF|1.5" (type, the journal's BoostValue, optionally the current jump range in light-years).
    public sealed class SuperchargeVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.8, SceneW = 240, SceneH = 150, CX = 120, CY = 50;
        private const int Segments = 28;

        private static readonly Color IceColor = Color.FromRgb(140, 225, 255), CreamColor = Color.FromRgb(255, 240, 200);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144));
        private static readonly Brush SegOff = Frozen(Color.FromArgb(20, 255, 255, 255)), WhiteB = Frozen(Colors.White);
        private static readonly Brush IceBrush = Frozen(IceColor), CreamBrush = Frozen(CreamColor);
        private static readonly Brush IceGlow = RadialGlow(IceColor, 255), CreamGlow = RadialGlow(CreamColor, 255);
        private static readonly Pen IcePlate = FrozenPen(Color.FromArgb(153, IceColor.R, IceColor.G, IceColor.B), 1), CreamPlate = FrozenPen(Color.FromArgb(153, CreamColor.R, CreamColor.G, CreamColor.B), 1);
        private static readonly Pen IceRipple = FrozenPen(IceColor, 1.5), CreamRipple = FrozenPen(CreamColor, 1.5);
        private static readonly Pen IceSpark = FrozenPen(Color.FromArgb(130, IceColor.R, IceColor.G, IceColor.B), 0.8), CreamSpark = FrozenPen(Color.FromArgb(130, CreamColor.R, CreamColor.G, CreamColor.B), 0.8);
        private static readonly RotateTransform[] SegRot = MakeSegRot();
        private static RotateTransform[] MakeSegRot()
        {
            var a = new RotateTransform[Segments];
            for (int i = 0; i < Segments; i++) { a[i] = new RotateTransform(-90 + i * 360.0 / Segments); a[i].Freeze(); }
            return a;
        }

        private bool _white;
        private double _boost = 4, _range;
        private string _boostText = "x4", _star = "NEUTRON STAR", _rangeLine = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _white = parts[0].Contains("WHITE", StringComparison.OrdinalIgnoreCase);
            _boost = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b > 1 ? b : (_white ? 1.5 : 4);
            _range = parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : 0;
            _boostText = "x" + _boost.ToString("0.#", CultureInfo.InvariantCulture);
            _star = _white ? "WHITE DWARF" : "NEUTRON STAR";
            _rangeLine = _range > 0
                ? $"JUMP RANGE {Math.Round(_range).ToString("0", CultureInfo.InvariantCulture)} TO {Math.Round(_range * _boost).ToString("0", CultureInfo.InvariantCulture)} LY"
                : "FSD CHARGED FOR ONE BOOSTED JUMP";
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var colour = _white ? CreamBrush : IceBrush;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            double e = EaseOut(Phase(t, 0.1, 0.3));
            dc.PushOpacity(e);

            // The charge ring: segments light clockwise from the top.
            double ch = Phase(t, 0.5, 2.6);
            dc.PushTransform(new TranslateTransform(CX, CY));
            for (int i = 0; i < Segments; i++)
            {
                dc.PushTransform(SegRot[i]);
                dc.DrawRectangle((i + 1.0) / Segments <= ch + 0.001 ? colour : SegOff, null, new Rect(-1.5, -38, 3, 9));
                dc.Pop();
            }
            dc.Pop();

            // The ripple when the ring is full.
            double done = Phase(t, 3.1, 0.5);
            if (done > 0 && done < 1)
            {
                dc.PushOpacity(1 - done);
                dc.DrawEllipse(null, _white ? CreamRipple : IceRipple, new Point(CX, CY), 33 + done * 30, 33 + done * 30);
                dc.Pop();
            }

            // Sparks arcing off the star while it charges.
            if (ch < 1)
                for (int s = 0; s < 3; s++)
                {
                    double ang = ch * 8 + s * 2.1, ex = CX + Math.Cos(ang) * 23 + Math.Sin(t * 20 + s) * 2.2, ey = CY + Math.Sin(ang) * 23 + Math.Cos(t * 17 + s) * 2.2;
                    dc.DrawLine(_white ? CreamSpark : IceSpark, new Point(CX + Math.Cos(ang) * 8, CY + Math.Sin(ang) * 8), new Point(ex, ey));
                }

            double glow = _white ? 16 : 12;
            dc.DrawEllipse(_white ? CreamGlow : IceGlow, null, new Point(CX, CY), glow, glow);

            DrawText(dc, "FSD OUTPUT", MonoFace, 6, colour, CX, CY - 31, 1, TextAlignment.Center);
            double m = 1 + (_boost - 1) * EaseOut(ch);
            string mult = ch >= 1 ? _boostText : "x" + m.ToString("0.0", CultureInfo.InvariantCulture);
            DrawText(dc, mult, MonoFace, 16, WhiteB, CX, CY + 10, 1, TextAlignment.Center);
            dc.Pop();

            // The plate.
            double plateAt = 3.4, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _white ? CreamPlate : IcePlate, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "FSD SUPERCHARGED", MonoFace, 12, colour, SceneW / 2, 106, 0.08, pa);
                string l2 = _star + "  NEXT JUMP " + _boostText;
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _rangeLine, MonoFace, Fit(_rangeLine, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
