using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A planet's detailed surface scan finished (the journal's SAAScanComplete): a survey card slides in with a faint planet that gets contour lines drawn over it, a MAPPED stamp thumps down, and a row of
    // pips fills one per probe used, with the efficiency target marked in gold; finishing within the target earns the BONUS line. Detail = "probes used|efficiency target" (subject = the body's short name).
    public sealed class MappedVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.8, CardX = 62, CardY = 6, CardW = 116, CardH = 92, DiscR = 17;

        private static readonly Color Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Gold = Color.FromRgb(0xff, 0xd3, 0x6b), Teal = Color.FromRgb(0x2f, 0x7f, 0x86);
        private static readonly Brush MintBrush = Frozen(Mint), CyanBrush = Frozen(Cyan), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardFill = Frozen(Color.FromArgb(18, Mint.R, Mint.G, Mint.B)), DiscFill = Frozen(Color.FromArgb(95, Teal.R, Teal.G, Teal.B));
        private static readonly Brush PipOff = Frozen(Color.FromArgb(77, 111, 168, 154)), PipOn = Frozen(Cyan), PipBonus = Frozen(Color.FromArgb(128, Gold.R, Gold.G, Gold.B));
        private static readonly Pen CardEdge = FrozenPen(Color.FromArgb(204, Mint.R, Mint.G, Mint.B), 1.5), PlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), MintStamp = FrozenPen(Mint, 1.5);
        private static readonly Pen DiscEdge = FrozenPen(Color.FromArgb(110, Cyan.R, Cyan.G, Cyan.B), 0.8), TargetPen = FrozenPen(Gold, 1);
        private static readonly Pen[] ContourPens = AlphaPens(Mint, 0.9);
        private static readonly Geometry DiscClip = MakeDisc();
        private static readonly Geometry[] Contours = MakeContours();

        private static Geometry MakeDisc() { var g = new EllipseGeometry(new Point(0, 0), DiscR, DiscR); g.Freeze(); return g; }
        private static Geometry[] MakeContours()
        {
            var list = new Geometry[4];
            for (int k = 0; k < 4; k++)
            {
                var sg = new StreamGeometry();
                using (var c = sg.Open())
                {
                    for (double a = -DiscR; a <= DiscR; a += 2)
                    {
                        var p = new Point(a, (k - 1.5) * 8 + Math.Sin(a * 0.2 + k) * 3 + Math.Sin(a * 0.45 + k * 2) * 1.2);
                        if (a == -DiscR) c.BeginFigure(p, false, false); else c.LineTo(p, true, false);
                    }
                }
                sg.Freeze(); list[k] = sg;
            }
            return list;
        }

        private string _name = "";
        private int _probes = 1, _target = 1;
        private readonly ScaleTransform _outer = new(), _fit = new(), _stampScale = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _stampAt = new(), _discAt = new();
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _name = (subject ?? "").Trim();
            if (_name.Length == 0) _name = "Planet";
            _probes = p.Length > 0 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? Math.Max(0, u) : 1;
            _target = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? Math.Max(0, g) : 0;
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            bool bonus = _target > 0 && _probes <= _target;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double slide = EaseOut(Phase(t, 0.1, 0.6)), x = CardX + (1 - slide) * 170, cx = x + CardW / 2;
            dc.DrawRectangle(CardFill, CardEdge, new Rect(x, CardY, CardW, CardH));
            DrawSpaced(dc, "SURFACE SURVEY", MonoFace, 8, MintBrush, cx, CardY + 5, 0.1, 1);

            // The faint planet with contour lines drawn over it, one after another.
            double cy = CardY + 36;
            _discAt.X = cx; _discAt.Y = cy; dc.PushTransform(_discAt);
            dc.DrawGeometry(DiscFill, DiscEdge, DiscClip);
            dc.PushClip(DiscClip);
            for (int c = 0; c < Contours.Length; c++)
            {
                double p = Phase(t, 0.7 + c * 0.2, 0.4);
                if (p > 0) dc.DrawGeometry(null, ContourPens[Math.Clamp((int)Math.Round(p * 5), 0, 8)], Contours[c]);
            }
            dc.Pop(); dc.Pop();

            DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, CardW - 12), NameBrush, cx, CardY + 58, 0.04, 1);

            // One pip per probe, the efficiency target marked in gold.
            int n = Math.Clamp(Math.Max(_target, _probes), 1, 10);
            double gap = 3, pw = Math.Min(14, (CardW - 20 - (n - 1) * gap) / n), rowW = n * pw + (n - 1) * gap, px0 = cx - rowW / 2, py0 = CardY + CardH - 22;
            for (int i = 0; i < n; i++)
            {
                bool on = i < _probes;
                double p = on ? Phase(t, 1.1 + i * 0.2, 0.2) : 0;
                var r = new Rect(px0 + i * (pw + gap), py0, pw, 6);
                dc.DrawRectangle(p > 0.5 ? PipOn : PipOff, null, r);
                if (_target > 0 && i == _target - 1) dc.DrawRectangle(null, TargetPen, r);
            }
            string tally = _probes.ToString(CultureInfo.InvariantCulture) + (_target > 0 ? " / " + _target.ToString(CultureInfo.InvariantCulture) : "") + (bonus ? "  -  BONUS" : "  PROBES");
            DrawSpaced(dc, tally, MonoFace, 7, bonus ? GoldBrush : MutedBrush, cx, CardY + CardH - 12, 0.06, Phase(t, 1.9, 0.3));

            double st = Phase(t, 1.9, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.3;
                _stampAt.X = cx; _stampAt.Y = cy; _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, MintStamp, new Rect(-33, -10, 66, 20));
                DrawSpaced(dc, "MAPPED", MonoFace, 10, MintBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.8, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "PLANET MAPPED", MonoFace, 12, MintBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string line3 = _probes.ToString(CultureInfo.InvariantCulture) + (_probes == 1 ? " probe" : " probes") + (_target > 0 ? "  -  efficiency target " + _target.ToString(CultureInfo.InvariantCulture) : "") + (bonus ? "  -  BONUS" : "");
                DrawSpaced(dc, line3, MonoFace, Fit(line3, 8, 180), bonus ? GoldBrush : MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
