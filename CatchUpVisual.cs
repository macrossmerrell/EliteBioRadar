using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // What the journal recorded while the app was not running (see CatchUp.cs): a flight-recorder log whose lines type in one after another - the time, the kind of event, what it was -
    // under a header with how long the commander was away. Plays when the app starts, or when the commander next loads into the game.
    // Detail = "away for|events|more rows|HH:mm~KIND~TEXT~COLOUR;..." with COLOUR one of C (cyan), G (green), A (amber), V (violet), R (red).
    public sealed class CatchUpVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 10.5, RowGap = 15;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Brush CyanB = Frozen(Cyan), GreenB = Frozen(Color.FromRgb(95, 227, 176)), AmberB = Frozen(Color.FromRgb(255, 180, 80)), VioletB = Frozen(Color.FromRgb(199, 155, 255)), RedB = Frozen(Color.FromRgb(255, 110, 100));
        private static readonly Brush PanelBack = Frozen(Color.FromArgb(224, 6, 11, 16)), Stripe = Frozen(Color.FromArgb(14, 255, 255, 255)), Dim = Frozen(Color.FromRgb(0x5d, 0x7f, 0x86)), TextBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff));
        private static readonly Pen PanelEdge = FrozenPen(Color.FromArgb(115, Cyan.R, Cyan.G, Cyan.B), 1);

        private sealed record Row(string Time, string Kind, string Text, Brush Color);
        private readonly List<Row> _rows = new();
        private string _sub = "", _more = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            int.TryParse(P(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int events);
            int.TryParse(P(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int more);
            _sub = $"{P(0).ToUpperInvariant()}  -  {events} EVENT{(events == 1 ? "" : "S")}";
            _more = more > 0 ? $"+ {more} MORE IN THE JOURNAL" : "";
            _rows.Clear();
            foreach (var item in P(3).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var c = item.Split('~');
                if (c.Length < 3) continue;
                Brush col = c.Length > 3 && c[3].Length > 0 ? c[3][0] switch { 'G' => GreenB, 'A' => AmberB, 'V' => VioletB, 'R' => RedB, _ => CyanB } : CyanB;
                _rows.Add(new Row(c[0], c[1].ToUpperInvariant(), c[2].ToUpperInvariant(), col));
                if (_rows.Count >= 6) break;
            }
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

            double e = EaseOut(Phase(t, 0.1, 0.4));
            dc.PushOpacity(e);
            dc.DrawRoundedRectangle(PanelBack, PanelEdge, new Rect(6, 3, 228, 144), 3, 3);
            dc.Pop();
            DrawSpaced(dc, "WHILE YOU WERE AWAY", MonoFace, 9.5, CyanB, SceneW / 2, 9, 0.1, e);
            DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 6.5, 210), Dim, SceneW / 2, 22, 0.08, e);

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                double start = 1.0 + i * 0.9, p = Phase(t, start, 0.35);
                if (p <= 0) continue;
                double y = 38 + i * RowGap;
                if (i % 2 == 0) { dc.PushOpacity(p); dc.DrawRectangle(Stripe, null, new Rect(10, y - 2, 220, RowGap - 1)); dc.Pop(); }
                DrawText(dc, r.Time, MonoFace, 6.5, Dim, 14, y, p, TextAlignment.Left);
                DrawText(dc, r.Kind, MonoFace, 6.5, r.Color, 40, y, p, TextAlignment.Left);
                // The line types in: the text shows one more letter at a time.
                int n = (int)Math.Ceiling(r.Text.Length * Phase(t, start, 0.7));
                if (n > 0) DrawText(dc, r.Text.Substring(0, Math.Min(n, r.Text.Length)), MonoFace, 6.5, TextBrush, 78, y, 1, TextAlignment.Left);
            }
            if (_more.Length > 0) DrawSpaced(dc, _more, MonoFace, 6.5, CyanB, SceneW / 2, 134, 0.06, Phase(t, 1.0 + _rows.Count * 0.9 + 0.3, 0.5));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
