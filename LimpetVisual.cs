using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Limpet drones (LaunchDrone, RepairDrone): a hull plate with the limpet hovering over it.
    //   L (launched) the limpet sweeps along above the plate, the type under it    R (repair result) the limpet rides the plate's edge as the bar fills with welding sparks and "+N HULL" counts up
    //   Repair green, Collection amber, Prospector violet, Hatch breaker red, the rest cyan
    // Detail = "L or R|limpet type|chip name|chip line|hull repaired".
    public sealed class LimpetVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.4;
        private sealed class Palette
        {
            public readonly Brush Solid, Wash;
            public readonly Pen Arm, Dotted, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Wash = Frozen(Color.FromArgb(77, c.R, c.G, c.B));
                Arm = FrozenPen(c, 1.3); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
                var d = new Pen(Frozen(Color.FromArgb(128, c.R, c.G, c.B)), 0.8) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                d.Freeze(); Dotted = d;
            }
        }
        private static readonly Palette GreenPal = new(Color.FromRgb(95, 227, 176)), AmberPal = new(Color.FromRgb(255, 180, 80)), VioletPal = new(Color.FromRgb(199, 155, 255)), RedPal = new(Color.FromRgb(255, 107, 107)), CyanPal = new(Color.FromRgb(85, 242, 255));
        private static readonly Brush PlateFill = Frozen(Color.FromArgb(56, 120, 160, 170)), TextBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Spark = Frozen(Color.FromRgb(255, 216, 144));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen PlateOutline = FrozenPen(Color.FromRgb(0x7f, 0xa0, 0xaa), 1.6), SparkPen = FrozenPen(Color.FromRgb(255, 216, 144), 1.2);

        private Palette _pal = GreenPal;
        private bool _result;
        private string _type = "", _name = "", _sub = "", _title = "";
        private double _amount;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _result = P(0) == "R";
            _type = P(1); _name = P(2); _sub = P(3);
            double.TryParse(p.Length > 4 ? p[4] : "", NumberStyles.Float, CultureInfo.InvariantCulture, out _amount);
            _pal = _type switch { "REPAIR" => GreenPal, "COLLECTION" => AmberPal, "PROSPECTOR" => VioletPal, "HATCH BREAKER" => RedPal, _ => CyanPal };
            _title = _result ? "LIMPET REPAIR DONE" : "LIMPET LAUNCHED";
        }


        private void DrawLimpet(DrawingContext dc, double x, double y, double s, double rot)
        {
            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new RotateTransform(rot)); dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawEllipse(_pal.Wash, _pal.Arm, new Point(0, 0), 4.5, 4.5);
            for (int i = 0; i < 4; i++)
            {
                double ang = i * Math.PI / 2;
                dc.DrawLine(_pal.Arm, new Point(Math.Cos(ang) * 4.5, Math.Sin(ang) * 4.5), new Point(Math.Cos(ang) * 8.5, Math.Sin(ang) * 8.5));
            }
            dc.Pop(); dc.Pop(); dc.Pop();
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

            const double bx = 35, by = 52, bw = 170, bh = 20;
            double d = EaseOut(Phase(t, 0.1, 0.3));
            if (d > 0)
            {
                dc.PushOpacity(d);
                dc.DrawRectangle(PlateFill, PlateOutline, new Rect(bx, by, bw, bh));
                double lx, ly = 34 + Math.Sin(t * 3) * 1.5;
                if (_result)
                {
                    double hp = Phase(t, 0.3, 1.5);
                    dc.DrawRectangle(_pal.Solid, null, new Rect(bx, by, bw * hp, bh));
                    lx = bx + bw * hp;
                    if (hp > 0 && hp < 1)
                        for (int i = 0; i < 6; i++)
                        {
                            double sx = lx - Math.Abs(Math.Sin(t * 9 + i * 1.7)) * 40;
                            if (Math.Sin(t * 14 + i) > 0.2 && sx > bx) dc.DrawLine(SparkPen, new Point(sx, by), new Point(sx + 3, by - 6));
                        }
                    DrawText(dc, "+" + Math.Round(_amount * hp).ToString("0", CultureInfo.InvariantCulture) + " HULL", MonoFace, 11, TextBrush, SceneW / 2, 80, 1, TextAlignment.Center);
                }
                else
                {
                    lx = bx + bw / 2 + Math.Sin(t * 1.3) * 60;
                    DrawText(dc, _type, MonoFace, 11, TextBrush, SceneW / 2, 80, 1, TextAlignment.Center);
                }
                dc.DrawLine(_pal.Dotted, new Point(lx, ly + 9), new Point(lx, by));
                DrawLimpet(dc, lx, ly, 1.3, t * 5 * 57.3);
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
