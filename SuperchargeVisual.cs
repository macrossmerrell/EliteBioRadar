using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // FSD supercharge from a neutron star or white dwarf jet cone. The star sits in the middle with a jet reaching out either side; your ship flies in along the left jet while a
    // charge bar fills, then a plate says "FSD SUPERCHARGED" with the star type and the boost. Neutron star jets are long and narrow and icy cyan, white dwarf jets shorter, wider and cream.
    // Subject unused, Detail = "NEUTRON|4" or "WHITE DWARF|1.5" (type, then the journal's BoostValue).
    public sealed class SuperchargeVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.4, SceneW = 240, SceneH = 150, StarX = 120, StarY = 42;

        private static readonly Color IceColor = Color.FromRgb(140, 225, 255), CreamColor = Color.FromRgb(255, 240, 200);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ShipFill = Frozen(Color.FromRgb(0x1b, 0x26, 0x30));
        private static readonly Brush BarTrack = Frozen(Color.FromArgb(51, 120, 150, 170));
        private static readonly Brush IceBrush = Frozen(IceColor), CreamBrush = Frozen(CreamColor);
        private static readonly Brush IceJet = Frozen(Color.FromArgb(33, IceColor.R, IceColor.G, IceColor.B)), CreamJet = Frozen(Color.FromArgb(33, CreamColor.R, CreamColor.G, CreamColor.B));
        private static readonly Brush[] IceDots = AlphaBrushes(IceColor), CreamDots = AlphaBrushes(CreamColor);
        private static readonly Brush IceGlow = RadialGlow(IceColor, 255), CreamGlow = RadialGlow(CreamColor, 255);
        private static readonly Pen IcePlate = FrozenPen(Color.FromArgb(153, IceColor.R, IceColor.G, IceColor.B), 1), CreamPlate = FrozenPen(Color.FromArgb(153, CreamColor.R, CreamColor.G, CreamColor.B), 1);
        private static readonly Pen[] IceShip = AlphaPens(IceColor, 1.2), CreamShip = AlphaPens(CreamColor, 1.2);
        private static readonly Geometry IceJetShape = JetShape(115, 9), CreamJetShape = JetShape(80, 15);
        private static readonly RotateTransform Nose90 = MakeNose90();
        private static RotateTransform MakeNose90() { var r = new RotateTransform(90); r.Freeze(); return r; }

        // One jet pointing right from the star; the left one is the same mirrored.
        private static Geometry JetShape(double length, double halfWidth) => Poly(new (double x, double y)[] { (3, 0), (length, -halfWidth), (length, halfWidth) });

        private bool _white;
        private string _boost = "x4";
        private readonly ScaleTransform _outer = new(), _fit = new(), _shipS = new(), _mirror = new(-1, 1);
        private readonly TranslateTransform _shift = new(), _shipT = new(), _jetT = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var parts = (detail ?? "").Split('|');
            _white = parts[0].Contains("WHITE", StringComparison.OrdinalIgnoreCase);
            double boost = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : (_white ? 1.5 : 4);
            _boost = "x" + boost.ToString("0.#", CultureInfo.InvariantCulture);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var dots = _white ? CreamDots : IceDots;
            var colourBrush = _white ? CreamBrush : IceBrush;
            double jw = _white ? 15 : 9, jl = _white ? 80 : 115;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            // The two jets: right as is, left mirrored about the star.
            var jetFill = _white ? CreamJet : IceJet;
            var jetShape = _white ? CreamJetShape : IceJetShape;
            _jetT.X = StarX; _jetT.Y = StarY;
            dc.PushTransform(_jetT);
            dc.DrawGeometry(jetFill, null, jetShape);
            dc.PushTransform(_mirror); dc.DrawGeometry(jetFill, null, jetShape); dc.Pop();
            dc.Pop();

            // Particles streaming out along both jets.
            for (int d = -1; d <= 1; d += 2)
                for (int j = 0; j < 4; j++)
                {
                    double q = (t * 0.9 + j / 4.0) % 1.0;
                    double dx = StarX + d * (8 + q * (jl - 8)), dy = StarY + Math.Sin(j * 2.1 + t * 3) * jw * q * 0.5;
                    dc.DrawEllipse(dots[(int)Math.Round((1 - q) * 0.5 * 8)], null, new Point(dx, dy), 1.4, 1.4);
                }

            dc.DrawEllipse(_white ? CreamGlow : IceGlow, null, new Point(StarX, StarY), 14, 14);

            // Your ship, nose right, flying in along the left jet.
            double shipX = 10 + EaseOut(Phase(t, 0.2, 2.6)) * (StarX - 10 - 52);
            int lvl = (int)Math.Round((0.7 + 0.3 * Phase(t, 1.0, 1.0)) * 8);
            _shipT.X = shipX; _shipT.Y = StarY; _shipS.ScaleX = _shipS.ScaleY = 0.8;
            dc.PushTransform(_shipT); dc.PushTransform(Nose90); dc.PushTransform(_shipS);
            dc.DrawGeometry(ShipFill, (_white ? CreamShip : IceShip)[Math.Clamp(lvl, 0, 8)], ShipOutline);
            dc.Pop(); dc.Pop(); dc.Pop();

            // The charge bar.
            double ch = Phase(t, 0.9, 2.2);
            dc.DrawRectangle(BarTrack, null, new Rect(30, 88, 180, 4));
            dc.DrawRectangle(colourBrush, null, new Rect(30, 88, 180 * ch, 4));
            DrawSpaced(dc, "CHARGE " + Math.Round(ch * 100).ToString(CultureInfo.InvariantCulture) + "%", MonoFace, 10, MutedBrush, SceneW / 2, 72, 0.1, 1);

            // The plate.
            double pa = Phase(t, 3.3, 0.4);
            if (pa > 0)
            {
                if (pa < 0.999) dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _white ? CreamPlate : IcePlate, new Rect(26, 106, 188, 40), 6, 6);
                if (pa < 0.999) dc.Pop();
            }
            DrawSpaced(dc, "FSD SUPERCHARGED", MonoFace, 13, colourBrush, SceneW / 2, 112, 0.1, pa);
            DrawSpaced(dc, (_white ? "WHITE DWARF" : "NEUTRON STAR") + " - NEXT JUMP " + _boost, MonoFace, 11, MutedBrush, SceneW / 2, 128, 0.1, Phase(t, 3.6, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
