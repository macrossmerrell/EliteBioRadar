using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Both visuals below are laid out in "local units" like the mock-ups they came from: a scene about 340 x 210 that is scaled uniformly to fit the
    // area (taking the width and height of the area into account), so local W and H vary a little with the window's shape.

    // The Landed moment (a planet touchdown): a cockpit view along the ground at the star's low light. The bump of contact, a dust haze that rolls past and clears, the
    // title "LANDED", the body's name and a short stats line (gravity, temperature, atmosphere). About 6 seconds. Starts at the touchdown itself.
    // Subject = body name, detail = "LABEL=value|LABEL=value".
    public class LandedVisual : AnimationVisual
    {
        private const double FadeOutAt = 5.8;
        // Overridden by the lift-off, which is the same cockpit view run the other way: the ground drops away and the dust blows outward and thins.
        protected virtual bool Rising => false;
        protected virtual string Title => "LANDED";
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Brush Frame = Frozen(Color.FromRgb(0x07, 0x0b, 0x0e));
        private static readonly Pen FrameEdge = FrozenPen(Color.FromArgb(128, 85, 242, 255), 1);

        // Depends only on the size of the area, so they are rebuilt when that changes, not every frame.
        private Brush? _sky;
        private double _skyHz;
        private Geometry? _leftPillar, _rightPillar;
        private double _pillarW, _pillarH;

        // Rocks and dust clouds are laid out once: fractions of the width and of the depth of the ground.
        private static readonly (double x, double d, double w, double h)[] Rocks = MakeRocks();
        private static readonly (double x, double d, double rx, double ry, double speed, double alpha)[] Clouds = MakeClouds();

        private string _subject = "";
        private (string label, string value)[] _stats = Array.Empty<(string, string)>();

        private static (double, double, double, double)[] MakeRocks()
        {
            var r = new Random(11);
            var a = new (double, double, double, double)[16];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble(), 2 + r.NextDouble() * 6, 1 + r.NextDouble() * 3);
            return a;
        }

        private static (double, double, double, double, double, double)[] MakeClouds()
        {
            var r = new Random(23);
            var a = new (double, double, double, double, double, double)[9];
            for (int i = 0; i < a.Length; i++) a[i] = (r.NextDouble(), r.NextDouble(), 50 + r.NextDouble() * 70, 12 + r.NextDouble() * 18, 6 + r.NextDouble() * 14, 0.5 + r.NextDouble() * 0.5);
            return a;
        }

        // The look of this landing, fixed at Start from the hidden entries of the detail: "~STAR" (kelvin of the lighting star), "~FLUX" (its light at the body,
        // 1 = the star's full-brightness level) and "~GROUND" (RRGGBB, the planet's own ground colour). Without them the scene keeps its warm default.
        private Brush _ground = Frozen(Color.FromRgb(0x1d, 0x12, 0x10)), _disc = Frozen(Color.FromRgb(0xff, 0xe0, 0xb0)), _glow = null!;
        private readonly Brush[] _dustLevels = new Brush[17];   // the dust at 0/16 .. 16/16 of its strength: opacity layers are costly, so the fade steps between ready-made brushes
        private Pen _horizon = FrozenPen(Color.FromRgb(0x3a, 0x24, 0x20), 1.5), _lines = FrozenPen(Color.FromArgb(64, 120, 80, 64), 0.8);
        private Color _skyHorizon = Color.FromRgb(90, 58, 50);
        private double _discR = 9, _glowAmt = 1;

        // The default look is the one drawn for a star of this temperature; other stars shift it by the ratio of their colour to this one's.
        private const double ReferenceK = 4800;

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _stats = ParseStats(detail);
            var x = ParseExtras(detail);

            double starK = x.TryGetValue("STAR", out var sk) && double.TryParse(sk, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var kv) ? kv : 0;
            double flux = x.TryGetValue("FLUX", out var fl) && double.TryParse(fl, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fv) ? fv : 0;
            (double r, double g, double b) rel = (1, 1, 1);
            if (starK > 0)
            {
                var k = PlanetRenderer.StarLightTint(starK); var r0 = PlanetRenderer.StarLightTint(ReferenceK);
                rel = (Math.Clamp(k.r / r0.r, 0.35, 1.8), Math.Clamp(k.g / r0.g, 0.35, 1.8), Math.Clamp(k.b / r0.b, 0.35, 1.8));
            }
            Color Tint(Color c, double mul = 1) => Color.FromRgb(
                (byte)Math.Clamp(c.R * rel.r * mul, 0, 255), (byte)Math.Clamp(c.G * rel.g * mul, 0, 255), (byte)Math.Clamp(c.B * rel.b * mul, 0, 255));

            // A bright star is a bigger, stronger disc and glow, a faint one a small ember (the lit fraction is a square root of the flux, kept within reason).
            double lit = flux > 0 ? Math.Clamp(Math.Pow(flux, 0.25), 0.6, 1.35) : 1;
            _discR = 9 * lit; _glowAmt = Math.Clamp(lit, 0.6, 1.0);
            _disc = Frozen(Tint(Color.FromRgb(0xff, 0xe0, 0xb0)));
            _skyHorizon = Tint(Color.FromRgb(90, 58, 50), lit);

            Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(WithAlpha(Tint(Color.FromRgb(255, 200, 130)), (byte)(230 * _glowAmt)), 0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(Tint(Color.FromRgb(255, 150, 90)), (byte)(102 * _glowAmt)), 0.25));
            glow.GradientStops.Add(new GradientStop(WithAlpha(Tint(Color.FromRgb(255, 120, 80)), 0), 1));
            glow.Freeze(); _glow = glow;

            // The ground: the planet's own colour, darkened for a low-light view and blended with the default so a wrong guess never takes over the whole screen.
            Color groundDefault = Color.FromRgb(0x1d, 0x12, 0x10), dustDefault = Color.FromRgb(176, 112, 88);
            Color ground = groundDefault, dust = Tint(dustDefault);
            if (x.TryGetValue("GROUND", out var gh) && gh.Length == 6 && int.TryParse(gh, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                var pc = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                Color Mix(Color a, Color b, double f) => Color.FromRgb((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
                ground = Mix(groundDefault, Color.FromRgb((byte)(pc.R * 0.30), (byte)(pc.G * 0.30), (byte)(pc.B * 0.30)), 0.7);
                dust = Mix(Tint(dustDefault), Color.FromRgb((byte)Math.Min(255, pc.R * 1.25), (byte)Math.Min(255, pc.G * 1.15), (byte)Math.Min(255, pc.B * 1.05)), 0.6);
            }
            _ground = Frozen(ground);
            _horizon = FrozenPen(Color.FromRgb((byte)Math.Min(255, ground.R * 2 + 6), (byte)Math.Min(255, ground.G * 2 + 6), (byte)Math.Min(255, ground.B * 2 + 6)), 1.5);
            _lines = FrozenPen(Color.FromArgb(64, (byte)Math.Min(255, ground.R * 4), (byte)Math.Min(255, ground.G * 4), (byte)Math.Min(255, ground.B * 4)), 0.8);
            for (int i = 0; i < _dustLevels.Length; i++)
            {
                var db = new RadialGradientBrush(Color.FromArgb((byte)(255 * i / 16), dust.R, dust.G, dust.B), Color.FromArgb(0, dust.R, dust.G, dust.B));
                db.Freeze(); _dustLevels[i] = db;
            }
            _sky = null;   // rebuilt with this landing's horizon colour
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double hz0 = H * 0.585;
            // Rising: the nose lifts and the horizon sinks away, slowly at first. `gone` is how far the ground has dropped (0..1).
            double gone = Rising ? Math.Pow(Smooth(0.6, 3.8, t), 1.3) : 0;
            double hz = hz0 + H * 0.45 * gone;   // far enough to leave the bottom of the screen

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);

            // Landed: the bump of contact, then the engines' vibration dying away. Lift-off: the engines' rumble, strongest before the skids leave the ground.
            double shake = Rising
                ? 1.8 * Math.Exp(-t * 0.55) * Math.Sin(t * 47) * (1 - 0.7 * Smooth(0.6, 1.4, t))
                : 5 * Math.Exp(-t * 5) * Math.Sin(t * 30) + 0.7 * Math.Exp(-t * 1.1) * Math.Sin(t * 53);
            dc.PushTransform(new TranslateTransform(0, shake));

            if (_sky == null || _skyHz != hz0)
            {
                var sky = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, hz0) };
                sky.GradientStops.Add(new GradientStop(Color.FromRgb(4, 7, 12), 0));
                sky.GradientStops.Add(new GradientStop(Color.FromRgb(26, 20, 32), 0.6));
                sky.GradientStops.Add(new GradientStop(_skyHorizon, 1));
                sky.Freeze(); _sky = sky; _skyHz = hz0;
            }
            // The sky is one gradient fixed to the original horizon and slid down with it, so the haze stays on the horizon as the ground falls away.
            dc.PushTransform(new TranslateTransform(0, hz - hz0));
            dc.DrawRectangle(_sky, null, new Rect(-40, -H - 40, W + 80, hz0 + H + 40));
            dc.Pop();

            // The star, low on the horizon, climbing a little (lift-off: it stays put and the horizon sinks below it).
            double sx = W * 0.69, sy = Rising ? hz0 - 4 : hz + 2 - 22 * Smooth(0, 5, t);
            dc.PushClip(new RectangleGeometry(new Rect(-40, -40, W + 80, hz + 40)));
            dc.DrawEllipse(_glow, null, new Point(sx, sy), 86, 86);
            dc.DrawEllipse(_disc, null, new Point(sx, sy), _discR, _discR);
            dc.Pop();

            dc.DrawRectangle(_ground, null, new Rect(-40, hz, W + 80, H - hz + 80));
            dc.DrawLine(_horizon, new Point(-40, hz), new Point(W + 40, hz));
            double reach = 300, slope = 56.0 / (210 * 0.415);
            for (int i = -8; i <= 8; i++)
                dc.DrawLine(_lines, new Point(W / 2 + i * 4, hz), new Point(W / 2 + i * 4 + i * slope * reach, hz + reach));
            foreach (var r in Rocks)
            {
                double y = hz + 8 + r.d * Math.Max(20, H - hz - 40), s = (0.6 + (y - hz) / 60) * (1 - 0.55 * gone);
                dc.DrawEllipse(Frame, null, new Point(r.x * W, y), r.w * s, r.h * s);
            }

            // Dust. Landed: thick at contact, drifting past and thinning out. Lift-off: thick under the engines, blown outward and gone within a few seconds.
            double da = Rising ? 0.9 * Smooth(0, 0.4, t) * (1 - Smooth(1.2, 4.2, t)) : 0.62 * Smooth(0, 0.3, t) * (1 - Smooth(1.0, 4.8, t));
            if (da > 0.01)
            {
                double span = W + 160;
                // Lift-off throws a second, bigger layer of clouds (different spots, wider) on top of the first.
                for (int pass = 0; pass < (Rising ? 2 : 1); pass++)
                    foreach (var q in Clouds)
                    {
                        double qx = pass == 0 ? q.x : (q.x * 1.7 + 0.31) % 1, qd = pass == 0 ? q.d : 1 - q.d, grow = pass == 0 ? 1 : 1.4;
                        double x = Rising
                            ? W / 2 + (qx - 0.5) * W * (1 + 0.7 * t)
                            : (((qx * W + t * q.speed * 1.6) % span) + span) % span - 80;
                        double y = hz + 10 + qd * Math.Max(20, H - hz - 50) - (Rising ? 0 : 14 * Smooth(0.5, 4.8, t));
                        dc.DrawEllipse(_dustLevels[(int)Math.Round(Clamp01(da * q.alpha) * 16)], null, new Point(x, y), q.rx * grow, q.ry * grow);
                    }
            }
            dc.Pop();   // shake

            // Cockpit frame: side pillars and the dash, with a ground readout.
            if (_leftPillar == null || _pillarW != W || _pillarH != H)
            {
                var left = new StreamGeometry();
                using (var g = left.Open()) { g.BeginFigure(new Point(0, 0), true, true); g.LineTo(new Point(34, 0), true, false); g.LineTo(new Point(8, H), true, false); g.LineTo(new Point(0, H), true, false); }
                var right = new StreamGeometry();
                using (var g = right.Open()) { g.BeginFigure(new Point(W, 0), true, true); g.LineTo(new Point(W - 34, 0), true, false); g.LineTo(new Point(W - 8, H), true, false); g.LineTo(new Point(W, H), true, false); }
                left.Freeze(); right.Freeze();
                _leftPillar = left; _rightPillar = right; _pillarW = W; _pillarH = H;
            }
            dc.DrawGeometry(Frame, null, _leftPillar); dc.DrawGeometry(Frame, null, _rightPillar);
            dc.DrawLine(FrameEdge, new Point(34, 0), new Point(8, H)); dc.DrawLine(FrameEdge, new Point(W - 34, 0), new Point(W - 8, H));
            dc.DrawRectangle(Frame, null, new Rect(0, H - 30, W, 30));
            dc.DrawRectangle(Frame, null, new Rect(8, H - 30, W - 16, 1));
            dc.DrawLine(FrameEdge, new Point(8, H - 30), new Point(W - 8, H - 30));
            dc.Pop();   // backdrop opacity

            // Text, drawn at full strength over the scene.
            DrawText(dc, "ALT", MonoFace, 10, DimBrush, 30, H - 22, env * 0.9, TextAlignment.Left);
            double alt = Rising ? 1240 * Math.Pow(Smooth(0.5, 4.2, t), 2) : 0;
            DrawText(dc, $"{alt:N0} m", MonoFace, 13, WhiteBrush, 52, H - 24, env, TextAlignment.Left);
            DrawText(dc, Rising ? (alt > 0 ? "CLIMBING" : "LIFTING") : "ON GROUND", MonoFace, 11, GreenBrush, W - 30, H - 22, env, TextAlignment.Right);

            double pt = Phase(t, 1.5, 0.8);
            if (pt > 0)
            {
                double ease = EaseOut(pt), titleY = H * 0.28;
                dc.PushOpacity(ease * fadeOut);
                dc.DrawRectangle(StripBrush, null, new Rect(-40, titleY - 14, W + 80, 86));
                dc.Pop();
                DrawSpaced(dc, Title, MonoFace, 22, TitleBrush, W / 2, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
                double ps = Phase(t, 2.1, 0.5);
                DrawText(dc, _subject, MonoFace, 12, SubjectBrush, W / 2, titleY + 34, ps * fadeOut);
                DrawStats(dc, _stats, MonoFace, W / 2, titleY + 56, W - 80, t, 2.5, fadeOut);
            }
            dc.Pop();   // scale
        }
    }

    // The Lift-off moment: the Landed view run the other way. The engines' dust is thrown outward and thins, the horizon sinks as the ship climbs, the altitude counts up, the
    // title is "LIFTOFF". Same detail string as Landed (body stats and the hidden star / ground entries).
    public sealed class LiftoffVisual : LandedVisual
    {
        protected override bool Rising => true;
        protected override string Title => "LIFTOFF";
    }

    // The Docked moment (a station, outpost, settlement or fleet carrier): a top-down pad lock. Four brackets close on the ship, a pink ring turns, ripples
    // spread at contact and the brackets go green, then the title "DOCKED", the station's name and its stats appear. About 5.5 seconds.
    // Subject = station name, detail = "LABEL=value|LABEL=value".
    public sealed class DockedVisual : AnimationVisual
    {
        private const double ContactAt = 1.1, FadeOutAt = 5.0;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Brush Backdrop = Frozen(Color.FromRgb(0x05, 0x09, 0x0b));
        private static readonly Pen RingPen = MakeRingPen();
        private static readonly Pen RipplePen = FrozenPen(Color.FromRgb(125, 255, 176), 2);   // fades by opacity when drawn
        private static readonly Brush FlashBrush = Frozen(Color.FromRgb(255, 255, 255));
        private static readonly Geometry Hull = MakeHull();

        private static Pen MakeRingPen()
        {
            var p = new Pen(Frozen(Color.FromArgb(191, 255, 111, 210)), 1.3) { DashStyle = new DashStyle(new[] { 4.0, 3.2 }, 0) };
            p.Freeze(); return p;
        }

        private static Geometry MakeHull()
        {
            var hull = new StreamGeometry();
            using (var g = hull.Open())
            {
                g.BeginFigure(new Point(0, -24), true, true);
                foreach (var p in new[] { new Point(17, 15), new Point(6, 11), new Point(0, 18), new Point(-6, 11), new Point(-17, 15) }) g.LineTo(p, true, false);
            }
            hull.Freeze(); return hull;
        }
        private static readonly Brush Cyan = Frozen(Color.FromRgb(0x55, 0xf2, 0xff));
        private static readonly Brush ShipFill = Frozen(Color.FromRgb(0x1b, 0x2a, 0x33));
        private static readonly Brush Shadow = Frozen(Color.FromArgb(90, 0, 0, 0));
        private static readonly Brush Canopy = Frozen(Color.FromArgb(153, 85, 242, 255));
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x0e, 0x2a, 0x30), 0.7);
        private static readonly Pen ShipPen = FrozenPen(Color.FromRgb(0x55, 0xf2, 0xff), 1.2);
        private static readonly Pen CyanPen = FrozenPen(Color.FromRgb(0x55, 0xf2, 0xff), 2.2);
        private static readonly Pen GreenPen = FrozenPen(Color.FromRgb(0x7d, 0xff, 0xb0), 2.2);

        private string _subject = "";
        private (string label, string value)[] _stats = Array.Empty<(string, string)>();

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _stats = ParseStats(detail);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double cx = W / 2, cy = H * 0.46;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.PushOpacity(BackdropOpacity * env);
            dc.DrawRectangle(Backdrop, null, new Rect(-40, -40, W + 80, H + 80));
            dc.Pop();

            dc.PushOpacity(env);
            for (double x = 10; x < W; x += 20) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, H));
            for (double y = 5; y < H; y += 20) dc.DrawLine(GridPen, new Point(0, y), new Point(W, y));

            double e = Smooth(0, ContactAt, t);
            bool contact = t >= ContactAt;
            const double s = 38;
            double d = s + 78 * Math.Pow(1 - e, 2);
            double sc = Lerp(1.7, 1, Math.Pow(e, 1.4));

            dc.PushTransform(new TranslateTransform(cx, cy + (1 - e) * 6));
            dc.PushTransform(new ScaleTransform(sc, sc));
            dc.DrawEllipse(Shadow, null, new Point(5 * (1 - e) + 2, 6), 20, 24);
            dc.DrawGeometry(ShipFill, ShipPen, Hull);
            dc.DrawRectangle(Canopy, null, new Rect(-2, -12, 4, 8));
            dc.Pop(); dc.Pop();

            // The ring turns by rotating the dashed circle (15 pen-widths of dash a second along its edge), not by animating a new pen each frame.
            double ringR = s + 22 + (1 - e) * 30;
            dc.PushTransform(new RotateTransform(-t * 15 * 1.3 / ringR * 180 / Math.PI, cx, cy));
            dc.DrawEllipse(null, RingPen, new Point(cx, cy), ringR, ringR);
            dc.Pop();

            var pen = contact ? GreenPen : CyanPen;
            const double L = 13;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                double x = cx + sx * d, y = cy + sy * d;
                dc.DrawLine(pen, new Point(x, y - sy * L), new Point(x, y));
                dc.DrawLine(pen, new Point(x, y), new Point(x - sx * L, y));
            }

            if (!contact) DrawText(dc, "ALIGNING", MonoFace, 11, Cyan, cx, cy + 66, 1);
            else
            {
                double k = t - ContactAt;
                foreach (double o in new[] { 0.0, 0.25, 0.5 })
                {
                    double v = k - o;
                    if (v < 0) continue;
                    dc.PushOpacity(0.7 * Math.Exp(-v * 1.5));
                    dc.DrawEllipse(null, RipplePen, new Point(cx, cy), 10 + v * 120, 10 + v * 120);
                    dc.Pop();
                }
                if (k < 0.35)
                {
                    dc.PushOpacity(0.5 * (1 - k / 0.35));
                    dc.DrawEllipse(FlashBrush, null, new Point(cx, cy), 60, 60);
                    dc.Pop();
                }
                if (((int)(t * 4)) % 2 == 0 || t > ContactAt + 1.2) DrawText(dc, "SECURE", MonoFace, 11, GreenBrush, cx, cy + 66, 1);
            }
            dc.Pop();   // env

            double pt = Phase(t, 1.4, 0.7);
            if (pt > 0)
            {
                double ease = EaseOut(pt), titleY = H * 0.06;
                dc.PushOpacity(ease * fadeOut);
                dc.DrawRectangle(StripBrush, null, new Rect(-40, titleY - 12, W + 80, 66));
                dc.Pop();
                DrawSpaced(dc, "DOCKED", MonoFace, 20, TitleBrush, cx, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
                DrawText(dc, _subject, MonoFace, 12, SubjectBrush, cx, titleY + 28, Phase(t, 1.9, 0.5) * fadeOut);
            }
            DrawStats(dc, _stats, MonoFace, cx, cy + 88, W - 40, t, 2.0, fadeOut);
            dc.Pop();   // scale
        }
    }
}
