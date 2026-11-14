using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A binary pair's centre of mass was charted (the journal's ScanBaryCentre): two stars circle their shared barycentre on dotted orbit paths with fading trails, the paths as
    // elliptical as the pair's real eccentricity. The plate gives the system, the orbit size, the period and the eccentricity; several charted together read as a count.
    // Detail = "count|system|body id|eccentricity|semi-major axis (m)|period (s)|widest axis (m)|longest period (s)".
    public sealed class BarycentreVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 7.6, CX = 120, CY = 50, A = 38, Q1 = 0.45, Q2 = 0.55;
        private static readonly Color Cyan = Color.FromRgb(127, 224, 255), Orange = Color.FromRgb(255, 176, 96), Blue = Color.FromRgb(140, 200, 255);
        private static readonly Brush Solid = Frozen(Cyan), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), WhiteB = Frozen(Colors.White);
        private static readonly Brush OrangeGlow = RadialGlow(Orange, 255), BlueGlow = RadialGlow(Blue, 255);
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), CrossPen = FrozenPen(Color.FromRgb(255, 216, 144), 1);
        private static readonly Pen OrangePath = DashedPen(Color.FromArgb(90, Orange.R, Orange.G, Orange.B)), BluePath = DashedPen(Color.FromArgb(90, Blue.R, Blue.G, Blue.B));
        private static readonly Brush[] OrangeTrail = AlphaBrushes(Orange), BlueTrail = AlphaBrushes(Blue);
        private static Pen DashedPen(Color c) { var p = new Pen(Frozen(c), 0.8) { DashStyle = new DashStyle(new double[] { 2.5, 3.5 }, 0) }; p.Freeze(); return p; }

        private int _count = 1;
        private double _ecc;
        private string _title = "BINARY PAIR CHARTED", _name = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            var inv = CultureInfo.InvariantCulture;
            int.TryParse(P(0), NumberStyles.Integer, inv, out _count); if (_count < 1) _count = 1;
            double.TryParse(P(3), NumberStyles.Float, inv, out _ecc); _ecc = Math.Clamp(_ecc, 0, 0.9);
            double.TryParse(P(4), NumberStyles.Float, inv, out var sma); double.TryParse(P(5), NumberStyles.Float, inv, out var period);
            double.TryParse(P(6), NumberStyles.Float, inv, out var maxSma); double.TryParse(P(7), NumberStyles.Float, inv, out var maxPeriod);
            string system = P(1).ToUpperInvariant();
            if (_count > 1)
            {
                _title = _count + " BARYCENTRES CHARTED"; _name = system;
                _sub = $"WIDEST {Distance(maxSma > 0 ? maxSma : sma)}  LONGEST {Span(maxPeriod > 0 ? maxPeriod : period)}";
            }
            else
            {
                _title = "BINARY PAIR CHARTED"; _name = system + (P(2).Length > 0 ? "  BARYCENTRE " + P(2) : "");
                _sub = $"ORBIT {Distance(sma)}  PERIOD {Span(period)}  ECC {_ecc.ToString("0.00", inv)}";
            }
        }

        // The orbit size: light-seconds for a close pair, astronomical units beyond that.
        private static string Distance(double metres)
        {
            if (metres <= 0) return "UNKNOWN";
            double ls = metres / 299792458.0;
            if (ls < 100) return ls.ToString("0.0", CultureInfo.InvariantCulture) + " LS";
            double au = metres / 1.495978707e11;
            return (au < 100 ? au.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(au).ToString("N0", CultureInfo.InvariantCulture)) + " AU";
        }

        // The period: hours, days or years, whichever reads best.
        private static string Span(double seconds)
        {
            if (seconds <= 0) return "UNKNOWN";
            double days = seconds / 86400.0;
            if (days < 2) return Math.Round(seconds / 3600.0).ToString("0", CultureInfo.InvariantCulture) + " HOURS";
            if (days < 730) return days.ToString("0.0", CultureInfo.InvariantCulture) + " DAYS";
            double years = days / 365.25;
            return (years < 100 ? years.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(years).ToString("N0", CultureInfo.InvariantCulture)) + " YEARS";
        }


        // Kepler's equation by a few fixed-point steps, then the position relative to the focus (the barycentre) for an orbit of semi-major axis a.
        private static (double x, double y) Orbit(double m, double e, double a)
        {
            double E = m;
            for (int i = 0; i < 6; i++) E = m + e * Math.Sin(E);
            return (a * (Math.Cos(E) - e), a * Math.Sqrt(1 - e * e) * Math.Sin(E));
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

            double f = EaseOut(Phase(t, 0.1, 0.3));
            dc.PushOpacity(f);

            double e = _ecc, b = Math.Sqrt(1 - e * e);
            // The two dotted orbit paths: each body circles the barycentre (a focus) on a scaled copy of the relative orbit, on opposite sides.
            dc.DrawEllipse(null, OrangePath, new Point(CX + Q2 * A * e, CY), Q2 * A, Q2 * A * b);
            dc.DrawEllipse(null, BluePath, new Point(CX - Q1 * A * e, CY), Q1 * A, Q1 * A * b);
            dc.DrawLine(CrossPen, new Point(CX - 3, CY), new Point(CX + 3, CY));
            dc.DrawLine(CrossPen, new Point(CX, CY - 3), new Point(CX, CY + 3));

            double m = Phase(t, 0.4, 3.0) * Math.PI * 2;
            const int trail = 24;
            for (int i = trail; i >= 1; i--)
            {
                var (tx, ty) = Orbit(m - i * 0.07, e, A);
                int lvl = (int)Math.Round((1 - i / (double)trail) * 0.5 * 8);
                dc.DrawRectangle(OrangeTrail[lvl], null, new Rect(CX - tx * Q2 - 0.7, CY - ty * Q2 - 0.7, 1.4, 1.4));
                dc.DrawRectangle(BlueTrail[lvl], null, new Rect(CX + tx * Q1 - 0.7, CY + ty * Q1 - 0.7, 1.4, 1.4));
            }
            var (rx, ry) = Orbit(m, e, A);
            dc.DrawEllipse(OrangeGlow, null, new Point(CX - rx * Q2, CY - ry * Q2), 12, 12);
            dc.DrawEllipse(BlueGlow, null, new Point(CX + rx * Q1, CY + ry * Q1), 10, 10);
            dc.DrawEllipse(WhiteB, null, new Point(CX - rx * Q2, CY - ry * Q2), 2, 2);
            dc.DrawEllipse(WhiteB, null, new Point(CX + rx * Q1, CY + ry * Q1), 1.7, 1.7);
            if (_count > 1) DrawText(dc, "+" + (_count - 1) + " MORE", MonoFace, 7, WhiteB, CX, 96, 1, TextAlignment.Center);
            dc.Pop();

            double plateAt = 3.4, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(10, 103, 220, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 205), Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 205), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 205), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
