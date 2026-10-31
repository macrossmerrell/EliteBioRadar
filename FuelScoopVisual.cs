using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // What the scoop screen needs to know, shared by every window showing it (set from the watcher's Status.json reads, see MainWindow.InitAnimations).
    public static class FuelScoopState
    {
        public static double Total, Capacity;   // tonnes in the tanks now / the main tank's size (0 = unknown)
        public static DateTime LastStart = DateTime.MinValue;
        public static DateTime LastEnd = DateTime.MinValue;
        public static event Action? EndRequested;
        public static void RequestEnd() { LastEnd = DateTime.UtcNow; EndRequested?.Invoke(); }
    }

    // Skimming a star: a glowing plasma ribbon in the star's own colour rolls across the screen, surging with a heartbeat, over the tank gauge that fills as the scoop works.
    // Open-ended: runs for as long as the game reports the scoop working, then fades. Subject = star name, detail = "TYPE=<star type>|~STAR=<kelvin>".
    public sealed class FuelScoopVisual : AnimationVisual
    {
        private const double EndSeconds = 0.8, GiveUpSeconds = 300;
        private static readonly Pen GaugeFrame = FrozenPen(Color.FromRgb(0x2f, 0x4f, 0x55), 1);
        private static readonly double[] HashA = MakeHash(12.9898, 43758.5453), HashB = MakeHash(3.7, 9999), HashC = MakeHash(7.1, 9999), HashD = MakeHash(9.3, 9999), HashE = MakeHash(1.7, 9999);
        private static double[] MakeHash(double k, double m) { var a = new double[80]; for (int i = 0; i < a.Length; i++) { double v = Math.Sin(i * k) * m; a[i] = v - Math.Floor(v); } return a; }

        private string _subject = "", _type = "";
        private double _endAt = -1;
        private bool _preview, _dead;
        private Brush[] _glows = null!, _dotsBright = null!, _dotsWhite = null!;
        private Brush _vignette = null!, _gaugeFill = null!, _coreBrush = null!;
        private Pen[] _strands = null!;
        private Pen _halo1 = null!, _halo2 = null!;

        public FuelScoopVisual()
        {
            Loaded += (_, _) => FuelScoopState.EndRequested += OnEndRequested;
            Unloaded += (_, _) => FuelScoopState.EndRequested -= OnEndRequested;
        }
        private void OnEndRequested() { if (_endAt < 0) _endAt = Elapsed; }

        protected override double Duration => _dead ? 0 : _endAt >= 0 ? _endAt + EndSeconds : Elapsed > GiveUpSeconds ? 0 : double.MaxValue;

        protected override void OnStart(string subject, string detail)
        {
            _subject = subject.ToUpperInvariant();
            _endAt = -1;
            _preview = detail.Contains("PREVIEW");
            _dead = !_preview && FuelScoopState.LastEnd >= FuelScoopState.LastStart;
            var stats = ParseStats(detail);
            _type = stats.Length > 0 ? stats[0].value : "";
            var tint = ReadSurfaceTint(ParseExtras(detail));
            Color Sc(int r, int g, int b) => Scale(Color.FromRgb((byte)r, (byte)g, (byte)b), tint.Star);
            Color deep = Sc(255, 110, 40), mid = Sc(255, 170, 80), bright = Sc(255, 232, 190);
            _coreBrush = Frozen(bright);

            // The centre glow at 9 strengths (it swells with each surge), and the sparks in two colours at 4 strengths each, all built once.
            _glows = new Brush[9];
            for (int i = 0; i < 9; i++)
            {
                byte a = (byte)(70 + i * 16);
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(Color.FromArgb(a, mid.R, mid.G, mid.B), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 3), deep.R, deep.G, deep.B), 0.5));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, deep.R, deep.G, deep.B), 1));
                g.Freeze(); _glows[i] = g;
            }
            _dotsBright = new Brush[4]; _dotsWhite = new Brush[4];
            for (int i = 0; i < 4; i++)
            {
                byte a = (byte)(90 + i * 45);
                _dotsBright[i] = Frozen(Color.FromArgb(a, bright.R, bright.G, bright.B));
                _dotsWhite[i] = Frozen(Color.FromArgb(a, 255, 255, 255));
            }
            var vg = new RadialGradientBrush();
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 3, 6, 11), 0.4));
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(190, 3, 6, 11), 1));
            vg.Freeze(); _vignette = vg;

            // Strands 1..6 sit behind the bright core (strand 0): the nearest one white, the rest fainter and in the star's colour.
            double[] w = { 0, 3.4, 2.8, 2.2, 1.6, 1.1, 0.8 };
            _strands = new Pen[7];
            for (int k = 1; k < 7; k++)
                _strands[k] = k == 1 ? FrozenPen(Color.FromArgb(115, 255, 255, 255), w[k]) : FrozenPen(Color.FromArgb((byte)(46 + 46 * (6 - k) / 2.0), mid.R, mid.G, mid.B), w[k]);
            _halo1 = FrozenPen(Color.FromArgb(46, mid.R, mid.G, mid.B), 16);
            _halo2 = FrozenPen(Color.FromArgb(80, mid.R, mid.G, mid.B), 8);
            _gaugeFill = Frozen(Color.FromRgb(0x7d, 0xff, 0xb0));
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            if (_preview && t >= 7.5 && _endAt < 0) _endAt = t;
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            double W = w / u, H = h / u, cx = W / 2, cy = H * 0.47;
            double env = Math.Min(1, t / 0.4) * (_endAt >= 0 ? 1 - Clamp01((t - _endAt) / EndSeconds) : 1);
            if (env <= 0) return;

            double beat = Math.Pow(Math.Max(0, Math.Sin(t * 2.6)), 6), amp = 44 * (1 + 0.3 * beat);
            double Y(double x, int k)
            {
                double ph = x * 0.011 - t * 1.7;
                return cy + Math.Sin(ph) * amp + Math.Sin(ph * 2.4 + k * 0.8 + t * 0.6) * 12 * (k == 0 ? 0.5 : 1) + (k - 3) * 6 * Math.Sin(t * 1.1 + k);
            }

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            dc.DrawEllipse(_glows[(int)Math.Round(Clamp01(0.35 + 0.65 * beat) * 8)], null, new Point(cx, cy), Math.Max(W * 0.6, 320), Math.Max(H * 0.9, 190));

            // The ribbon: seven strands, the fainter ones first; the core strand gets two wide faint halos under it (a cheap glow) and swells on each surge.
            var sgs = new StreamGeometry[7];
            for (int k = 0; k < 7; k++)
            {
                sgs[k] = new StreamGeometry();
                using (var c = sgs[k].Open())
                {
                    c.BeginFigure(new Point(-10, Y(-10, k)), false, false);
                    for (double x = -4; x <= W + 10; x += 6) c.LineTo(new Point(x, Y(x, k)), true, true);
                }
                sgs[k].Freeze();
            }
            for (int k = 6; k >= 1; k--) dc.DrawGeometry(null, _strands[k], sgs[k]);
            dc.DrawGeometry(null, _halo1, sgs[0]);
            dc.DrawGeometry(null, _halo2, sgs[0]);
            dc.DrawGeometry(null, new Pen(_coreBrush, 4.5 + 2.5 * beat) { LineJoin = PenLineJoin.Round }, sgs[0]);

            // Sparks riding the ribbon.
            double span = W + 80;
            for (int i = 0; i < 70; i++)
            {
                double x = ((HashA[i] * span - t * (50 + HashB[i] * 70)) % span + span) % span - 30, y = Y(x, (int)(HashC[i] * 5)) + (HashD[i] - 0.5) * 34, r = 0.8 + HashE[i] * 1.6;
                var set = i % 4 == 0 ? _dotsWhite : _dotsBright;
                dc.DrawEllipse(set[(int)(HashB[(i + 11) % 80] * 3.99)], null, new Point(x, y), r, r);
            }

            dc.DrawRectangle(_vignette, null, new Rect(0, 0, W, H));

            double a = env * Phase(t, 0.3, 0.6);
            DrawSpaced(dc, "FUEL SCOOPING", MonoFace, 15, TitleBrush, cx, 12, 0.5, a);
            string who = _subject + (_type.Length > 0 ? (_subject.Length > 0 ? "  ·  " : "") + _type : "");
            if (who.Length > 0) DrawText(dc, who, MonoFace, 11, SubjectBrush, cx, 32, a);

            double total, cap;
            if (_preview) { cap = 32; total = 12 + Math.Min(20, t * 3.2); } else { cap = FuelScoopState.Capacity; total = FuelScoopState.Total; }
            double gw = Math.Min(W * 0.44, 300), gx = cx - gw / 2, gy = H * 0.76;
            if (cap > 0)
            {
                dc.DrawRectangle(Level(Backdrops, 0.6 * a), GaugeFrame, new Rect(gx, gy, gw, 12));
                dc.DrawRectangle(_gaugeFill, null, new Rect(gx + 1, gy + 1, (gw - 2) * Clamp01(total / cap), 10));
                DrawText(dc, $"{total:F1} / {cap:F1} t", MonoFace, 12, GreenBrush, cx, gy + 17, a);
            }
            else DrawText(dc, $"{total:F1} t", MonoFace, 12, GreenBrush, cx, gy + 4, a);
            dc.Pop();
        }
    }
}
