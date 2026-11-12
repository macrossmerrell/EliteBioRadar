using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The full system scan finished (every body found): a small star with five orbit rings, and the system's bodies light up one by one, grey dots turning cyan with a little ring as each is found.
    // When the last one is lit they all turn mint and a mint ripple goes out; a COMPLETE stamp thumps onto the star and the plate says "SYSTEM SCANNED", the number of bodies and the system name. The dots are decorative (the real orbits are not drawn);
    // up to 14 are shown however many bodies there are. Subject = the system name, Detail = the body count.
    public sealed class FssCompleteVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.3, CentreX = 120, CentreY = 50;
        private const int MaxDots = 14;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(125, 255, 196), Grey = Color.FromRgb(120, 140, 160), StarColour = Color.FromRgb(255, 220, 150);
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x88, 0x99, 0xaa)), MintBrush = Frozen(Mint), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), StarGlow = RadialGlow(StarColour, 255);
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff));
        private static readonly Pen StampPen = FrozenPen(Mint, 1.5);
        private static readonly Pen MintPlate = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), OrbitPen = FrozenPen(Color.FromArgb(36, Cyan.R, Cyan.G, Cyan.B), 0.8);
        // Nine alpha levels each.
        private static readonly Brush[] CyanDot = AlphaBrushes(Cyan), MintDot = AlphaBrushes(Mint), GreyDot = AlphaBrushes(Grey);
        private static readonly Pen[] FoundRing = AlphaPens(Cyan, 1), Ripple = AlphaPens(Mint, 2);
        private static readonly double[] Orbit = { 14, 22, 30, 38, 46 };
        private static readonly double[] Start = MakeStart();
        private static double[] MakeStart() { var a = new double[MaxDots]; for (int i = 0; i < MaxDots; i++) a[i] = i * 2.4 + 0.5; return a; }

        private int _count = 14, _dots = 14;
        private string _line2 = "", _system = "";
        private readonly ScaleTransform _stampScale = new();
        private readonly TranslateTransform _stampAt = new(CentreX, CentreY);
        private readonly RotateTransform _stampRot = new(-11.5);
        private double _step = 0.16, _done = 2.63;   // the gap between bodies lighting, and when the last is lit
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            _system = (subject ?? "").Trim().ToUpperInvariant();
            _count = int.TryParse((detail ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 14;
            _dots = Math.Clamp(_count, 1, MaxDots);
            _step = Math.Min(0.16, 2.2 / _dots);
            _done = 0.3 + (_dots - 1) * _step + 0.25;
            _line2 = _count == 1 ? "1 BODY FOUND" : _count.ToString(CultureInfo.InvariantCulture) + " BODIES FOUND";
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

            foreach (double r in Orbit) dc.DrawEllipse(null, OrbitPen, new Point(CentreX, CentreY), r * 1.5, r * 0.8);
            dc.DrawEllipse(StarGlow, null, new Point(CentreX, CentreY), 9, 9);

            bool allFound = t >= _done;
            for (int i = 0; i < _dots; i++)
            {
                int ring = i % 5;
                double ang = Start[i] + t * (0.25 - ring * 0.03), on = Phase(t, 0.3 + i * _step, 0.25), r = Orbit[ring];
                double x = CentreX + Math.Cos(ang) * r * 1.5, y = CentreY + Math.Sin(ang) * r * 0.8;
                Brush fill = on <= 0 ? GreyDot[4] : (allFound ? MintDot : CyanDot)[Math.Clamp((int)Math.Round((0.35 + 0.65 * on) * 8), 0, 8)];
                double rad = (ring == 3 ? 3.6 : 2.4) * (0.6 + 0.4 * on);
                dc.DrawEllipse(fill, null, new Point(x, y), rad, rad);
                if (on > 0 && on < 1) dc.DrawEllipse(null, FoundRing[Math.Clamp((int)Math.Round((1 - on) * 8), 0, 8)], new Point(x, y), 3 + on * 8, 3 + on * 8);
            }

            double b = Phase(t, _done + 0.1, 0.9);
            if (b > 0 && b < 1) dc.DrawEllipse(null, Ripple[Math.Clamp((int)Math.Round((1 - b) * 0.8 * 8), 0, 8)], new Point(CentreX, CentreY), 30 + b * 60, 16 + b * 32);

            // A COMPLETE stamp thumps onto the star when the last body is lit.
            double st = Phase(t, _done, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.2;
                _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(PlateBrush, StampPen, new Rect(-36, -10, 72, 20));
                DrawSpaced(dc, "COMPLETE", MonoFace, 10, MintBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = _done - 0.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                if (pa < 0.999) dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, MintPlate, new Rect(26, 103, 188, _system.Length > 0 ? 45 : 34), 6, 6);   // as tall as its lines
                if (pa < 0.999) dc.Pop();
            }
            DrawSpaced(dc, "SYSTEM SCANNED", MonoFace, 12, MintBrush, SceneW / 2, 106, 0.08, pa);
            DrawSpaced(dc, _line2, MonoFace, 10, NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
            if (_system.Length > 0) DrawSpaced(dc, _system, MonoFace, Math.Min(8, 180 / (_system.Length * 0.66)), MintBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
