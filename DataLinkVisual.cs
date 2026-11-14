using System;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Data link scans (DataScanned, and the DatalinkVoucher that closes an intel package): the target's icon on the left, a download bar filling beside it with scrolling hex underneath, "COMPLETE" at the end.
    //   LOG cyan (abandoned data log)    POINT green (data point)    WRECK amber (wrecked ship)    UPLINK violet (ship uplink)    INTEL gold (intel package, "COMPILING PACKAGE")
    // Detail = "KIND|target name|chip line|payout (INTEL only)".
    public sealed class DataLinkVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.4;
        private sealed class Palette
        {
            public readonly Brush Solid, Wash, Dim;
            public readonly Pen Icon, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Wash = Frozen(Color.FromArgb(51, c.R, c.G, c.B)); Dim = Frozen(Color.FromArgb(178, c.R, c.G, c.B));
                Icon = FrozenPen(c, 1.6); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette CyanPal = new(Color.FromRgb(85, 242, 255)), GreenPal = new(Color.FromRgb(95, 227, 176)), AmberPal = new(Color.FromRgb(255, 180, 80)), VioletPal = new(Color.FromRgb(199, 155, 255)), GoldPal = new(Color.FromRgb(255, 216, 100));
        private static readonly Brush TrackBrush = Frozen(Color.FromArgb(28, 255, 255, 255)), LabelBrush = Frozen(Color.FromRgb(0xcf, 0xe8, 0xe8));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen Outline = FrozenPen(Color.FromArgb(90, 255, 255, 255), 1);

        // Icons in local coordinates, centred on (0, 0), about 36 units tall.
        private static readonly Geometry LogBody = new RectangleGeometry(new Rect(-7, -11, 14, 22), 2, 2);
        private static readonly Geometry PointShape = Poly(new[] { (0.0, -12.0), (7, 0), (0, 12), (-7, 0) });
        private static readonly Geometry WreckA = Poly(new[] { (-12.0, 4.0), (-3, -6), (1, 3) }), WreckB = Poly(new[] { (4.0, -3.0), (12, 6), (3, 7) });
        private static readonly Geometry DishArc = MakeArc(), DishStand = Poly(new[] { (-0.7, 3.0), (0.7, 3), (0.7, 11), (-0.7, 11) }), DishBase = Poly(new[] { (-6.0, 11.0), (6, 11), (6, 12.4), (-6, 12.4) });
        private static readonly Geometry Envelope = new RectangleGeometry(new Rect(-11, -8, 22, 16), 1.5, 1.5), EnvelopeFlap = Poly(new[] { (-11.0, -8.0), (0, 1), (11, -8) });

        private static Geometry MakeArc()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(-9.5, 0), false, false);
                c.ArcTo(new Point(9.5, 0), new Size(9.5, 9.5), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private Palette _pal = CyanPal;
        private string _kind = "LOG", _name = "", _title = "DATA SCANNED", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _kind = P(0); _name = P(1); _sub = P(2);
            _title = "DATA SCANNED";
            switch (_kind)
            {
                case "POINT": _pal = GreenPal; break;
                case "WRECK": _pal = AmberPal; break;
                case "UPLINK": _pal = VioletPal; break;
                case "INTEL": _pal = GoldPal; _title = "INTEL PACKAGE"; break;
                default: _kind = "LOG"; _pal = CyanPal; break;
            }
            if (P(2).Length > 0 && _kind == "INTEL" && P(3).Length > 0) _sub = P(2);
        }


        private void DrawIcon(DrawingContext dc, double x, double y, double s)
        {
            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(s, s));
            switch (_kind)
            {
                case "POINT": dc.DrawGeometry(_pal.Wash, _pal.Icon, PointShape); break;
                case "WRECK": dc.DrawGeometry(_pal.Wash, _pal.Icon, WreckA); dc.DrawGeometry(_pal.Wash, _pal.Icon, WreckB); break;
                case "UPLINK":
                    dc.PushTransform(new TranslateTransform(0, 4)); dc.DrawGeometry(null, _pal.Icon, DishArc); dc.Pop();
                    dc.DrawGeometry(_pal.Solid, null, DishStand); dc.DrawGeometry(_pal.Solid, null, DishBase);
                    dc.DrawEllipse(_pal.Solid, null, new Point(0, -7), 1.8, 1.8);
                    break;
                case "INTEL": dc.DrawGeometry(_pal.Wash, _pal.Icon, Envelope); dc.DrawGeometry(null, _pal.Icon, EnvelopeFlap); break;
                default:
                    dc.DrawGeometry(_pal.Wash, _pal.Icon, LogBody);
                    for (int i = 0; i < 3; i++) dc.DrawRectangle(_pal.Solid, null, new Rect(-4.5, -6 + i * 6, 9, 1));
                    break;
            }
            dc.Pop(); dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            double d = EaseOut(Phase(t, 0.1, 0.4));
            if (d > 0)
            {
                dc.PushOpacity(d);
                DrawIcon(dc, 50, 50, 1.45);
                double p = EaseOut(Phase(t, 0.4, 1.4)), bx = 92, bw = 128, by = 44, bh = 9;
                dc.DrawRectangle(TrackBrush, null, new Rect(bx, by, bw, bh));
                dc.DrawRectangle(_pal.Solid, null, new Rect(bx, by, bw * p, bh));
                dc.DrawRectangle(null, Outline, new Rect(bx, by, bw, bh));
                DrawText(dc, _kind == "INTEL" ? "COMPILING PACKAGE" : "DOWNLOADING", MonoFace, 7, LabelBrush, bx, by - 13, 1, TextAlignment.Left);
                DrawText(dc, Math.Round(p * 100) + "%", MonoFace, 7, LabelBrush, bx + bw, by - 13, 1, TextAlignment.Right);
                for (int i = 0; i < 3; i++)
                {
                    var sb = new StringBuilder();
                    for (int j = 0; j < 17; j++) sb.Append("0123456789ABCDEF"[(int)((t * 13 + i * 5 + j * 3.7) % 16)]).Append(j % 2 == 1 ? " " : "");
                    DrawText(dc, sb.ToString(), MonoFace, 6, _pal.Dim, bx, by + 14 + i * 9, p < 1 ? 1 : 0.4, TextAlignment.Left);
                }
                if (p >= 1) DrawText(dc, _kind == "INTEL" && _sub.Length > 0 ? "COMPLETE" : "COMPLETE", MonoFace, 8, _pal.Solid, bx, by + 44, 1, TextAlignment.Left);
                dc.Pop();
            }

            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
