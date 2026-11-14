using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Synthesis (the journal's Synthesis event): a tile for each material slides in from the sides and they merge into one result tile, then grade pips light.
    // Detail = "RECIPE NAME|grade 1-3|mat:count;mat:count" (Basic 1, Standard 2, Premium 3). The recipe type picks the colour.
    public sealed class SynthesisVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardBrush = Frozen(Color.FromRgb(0x1b, 0x22, 0x30)), GoldBrush = Frozen(Color.FromRgb(255, 216, 144)), PipOff = Frozen(Color.FromArgb(46, 255, 255, 255));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 255, 216, 144), 1);

        private sealed record Mat(string Name, string Symbol, int Count, Brush Fill, Pen Edge);
        private readonly List<Mat> _mats = new();
        private string _name = "", _line = "";
        private int _grade = 1;
        private Brush _typeBrush = Frozen(Color.FromRgb(85, 242, 255));
        private Pen _typeEdge = FrozenPen(Color.FromRgb(85, 242, 255), 2);

        protected override double Duration => FadeOutAt + 0.6;

        private static Color TypeColour(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("fuel")) return Color.FromRgb(85, 242, 255);
            if (n.Contains("repair")) return Color.FromRgb(95, 227, 176);
            if (n.Contains("ammo")) return Color.FromRgb(255, 180, 80);
            if (n.Contains("limpet")) return Color.FromRgb(200, 122, 255);
            if (n.Contains("life")) return Color.FromRgb(106, 168, 255);
            return Color.FromRgb(232, 242, 255);
        }

        // A steady colour per material name, so the same element always looks the same.
        private static Color MatColour(string name)
        {
            int h = 0;
            foreach (char c in name) h = (h * 31 + c) % 360;
            double hue = h / 60.0, s = 0.55, v = 0.95, f = hue - Math.Floor(hue), p = v * (1 - s), q = v * (1 - s * f), t2 = v * (1 - s * (1 - f));
            (double r, double g, double b) = ((int)hue % 6) switch { 0 => (v, t2, p), 1 => (q, v, p), 2 => (p, v, t2), 3 => (p, q, v), 4 => (t2, p, v), _ => (v, p, q) };
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _name = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "SYNTHESIS";
            _grade = p.Length > 1 && int.TryParse(p[1], out int gr) ? Math.Clamp(gr, 1, 3) : 1;
            var tc = TypeColour(_name);
            _typeBrush = Frozen(tc); _typeEdge = FrozenPen(tc, 2);
            _mats.Clear();
            var parts = new List<string>();
            if (p.Length > 2)
                foreach (var m in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var q = m.Split(':');
                    string nm = q[0].Trim().ToUpperInvariant();
                    if (nm.Length == 0 || _mats.Count >= 6) continue;
                    int cnt = q.Length > 1 && int.TryParse(q[1], out int c) ? c : 1;
                    var col = MatColour(nm);
                    string sym = nm.Length >= 2 ? char.ToUpperInvariant(nm[0]) + nm.Substring(1, 1).ToLowerInvariant() : nm;
                    _mats.Add(new Mat(nm, sym, cnt, Frozen(col), FrozenPen(col, 1.8)));
                    parts.Add($"{nm} X{cnt}");
                }
            _line = string.Join(" - ", parts);
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

            int n = _mats.Count;
            double size = n > 2 ? 30 : 36, move = EaseOut(Phase(t, 0.4, 1.0)), gone = 1 - Phase(t, 1.4, 0.2);
            int rows = (n + 1) / 2;
            for (int i = 0; i < n; i++)
            {
                var m = _mats[i];
                double appear = EaseOut(Phase(t, 0.1 + i * 0.2, 0.3));
                if (appear <= 0 || gone <= 0) continue;
                bool right = i % 2 == 1;
                double sx = right ? 200 : 40, sy = 40 + ((i / 2) - (rows - 1) / 2.0) * (size + 4), x = sx + (120 - sx) * move, y = sy + (40 - sy) * move, sc = 1 - 0.45 * move;
                dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(sc, sc)); dc.PushOpacity(appear * gone);
                dc.DrawRoundedRectangle(CardBrush, m.Edge, new Rect(-size / 2, -size / 2, size, size), 4, 4);
                DrawText(dc, m.Symbol, MonoFace, size * 0.45, m.Fill, 0, -size * 0.36, 1, TextAlignment.Center);
                DrawText(dc, "x" + m.Count, MonoFace, 7, m.Fill, 0, size * 0.18, 1, TextAlignment.Center);
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            // The result tile pops in where they merged.
            double rp = EaseOut(Phase(t, 1.4, 0.4));
            if (rp > 0.01)
            {
                dc.PushTransform(new TranslateTransform(120, 40)); dc.PushTransform(new ScaleTransform(rp * 1.1, rp * 1.1));
                dc.PushOpacity(rp);
                dc.DrawRoundedRectangle(CardBrush, _typeEdge, new Rect(-19.5, -19.5, 39, 39), 5, 5);
                dc.DrawEllipse(_typeBrush, null, new Point(0, 0), 7, 7);
                dc.Pop(); dc.Pop(); dc.Pop();
                bool lit = t > 1.9;
                for (int i = 0; i < 3; i++) dc.DrawEllipse(lit && i < _grade ? _typeBrush : PipOff, null, new Point(120 - 10 + i * 10, 74), 3.2, 3.2);
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, _line.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "SYNTHESISED", MonoFace, 12, GoldBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (_line.Length > 0) DrawSpaced(dc, _line, MonoFace, Fit(_line, 8, 190), GoldBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
