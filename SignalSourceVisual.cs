using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A signal source found (the journal's FSSSignalDiscovered after a honk or an FSS sweep, and USSDrop): noise on a waveform settles into a clean signal while the name decodes out of scrambled letters.
    // Colour follows the kind: resource extraction amber, conflict zone and pirate activity red, unidentified signal source violet, nav beacon cyan, anything else grey.
    // Detail = "KIND|NAME|DETAIL|MORE|THREAT" with KIND RES, CZ, PIRATE, USS, NAV or OTHER, MORE the number of other signals in the same batch and THREAT 0-5 (pips).
    public sealed class SignalSourceVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private const string Scramble = "ABCDEFGHJKLMNPQRSTUVWXYZ#%";

        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Wave, WaveFaint, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Wave = FrozenPen(c, 1.5); WaveFaint = FrozenPen(Color.FromArgb(90, c.R, c.G, c.B), 1.2); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette ResPal = new(Color.FromRgb(255, 180, 80)), CzPal = new(Color.FromRgb(255, 106, 90)), UssPal = new(Color.FromRgb(200, 122, 255)),
            NavPal = new(Color.FromRgb(85, 242, 255)), OtherPal = new(Color.FromRgb(165, 175, 195));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));

        private Palette _pal = OtherPal;
        private string _kind = "", _name = "", _detail = "", _title = "SIGNAL SOURCE FOUND";
        private int _more, _threat;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _name = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "SIGNAL";
            _detail = p.Length > 2 ? p[2].Trim().ToUpperInvariant() : "";
            _more = p.Length > 3 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? Math.Max(0, m) : 0;
            _threat = p.Length > 4 && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var th) ? Math.Clamp(th, 0, 5) : 0;
            _pal = _kind switch { "RES" => ResPal, "CZ" or "PIRATE" => CzPal, "USS" => UssPal, "NAV" => NavPal, _ => OtherPal };
            _title = _kind == "USS" ? "UNIDENTIFIED SIGNAL SOURCE" : "SIGNAL SOURCE FOUND";
        }


        private static Geometry Wave(double x0, double w, double y, double t, double settle, double amp, double phase)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                int frame = (int)Math.Floor(t * 30);
                for (int x = 0; x <= (int)w; x += 2)
                {
                    double noise = (Hash(frame * 7 + x + (int)(phase * 100)) - 0.5) * 36 * (1 - settle), sig = Math.Sin(x * 0.17 + phase) * amp * settle;
                    var pt = new Point(x0 + x, y + noise + sig);
                    if (x == 0) c.BeginFigure(pt, false, false); else c.LineTo(pt, true, false);
                }
            }
            g.Freeze(); return g;
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

            double settleP = Phase(t, 0.2, 1.6), rev = Phase(t, 1.4, 0.9);
            dc.DrawGeometry(null, _pal.WaveFaint, Wave(20, 200, 56, t, settleP, 12, 1.3));
            dc.DrawGeometry(null, _pal.Wave, Wave(20, 200, 56, t, settleP, 16, 0));

            // The name: letters settle left to right from scrambled ones.
            var sb = new StringBuilder(_name.Length);
            int frame = (int)Math.Floor(t * 20);
            for (int i = 0; i < _name.Length; i++)
                sb.Append(_name[i] == ' ' || (double)i / _name.Length < rev ? _name[i] : Scramble[(int)(Hash(i + frame * 13) * Scramble.Length) % Scramble.Length]);
            string shown = sb.ToString();
            DrawSpaced(dc, shown, MonoFace, Fit(_name, 11, 220), NameBrush, SceneW / 2, 14, 0.04, Phase(t, 0.15, 0.3));

            for (int i = 0; i < _threat; i++)
            {
                double q = Phase(t, 1.9 + i * 0.2, 0.3);
                if (q > 0) { dc.PushOpacity(q); dc.DrawEllipse(_pal.Solid, null, new Point(SceneW / 2 + (i - (_threat - 1) / 2.0) * 14, 88), 4, 4); dc.Pop(); }
            }

            string l3 = _detail + (_more > 0 ? (_detail.Length > 0 ? "  -  " : "") + "+" + _more.ToString(CultureInfo.InvariantCulture) + " MORE" : "");
            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = l3.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(26, 103, 188, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 11, 180), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), _pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
