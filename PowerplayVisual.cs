using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Powerplay events (pledged, merits earned, rank up, defected, left, salary): a five-segment merit bar with the rank above it.
    //   JOIN cyan, the bar draws in    MERIT gold, the fill grows and "+N" floats up    RANK violet, the bar fills and flashes
    //   DEFECT amber, the bar turns cyan    LEAVE red, the bar drains    PAY green, "+N CR" floats up
    // The bar is a visual only (the journal gives no rank thresholds); the real numbers are in the labels and the chip.
    // Detail = "KIND|power|chip line|old rank|new rank|merits gained or credits|total merits" (ranks -1 when unknown).
    public sealed class PowerplayVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.2;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette CyanPal = new(Color.FromRgb(85, 242, 255)), GoldPal = new(Color.FromRgb(255, 216, 100)), VioletPal = new(Color.FromRgb(199, 155, 255)), AmberPal = new(Color.FromRgb(255, 180, 80)), RedPal = new(Color.FromRgb(255, 107, 107)), GreenPal = new(Color.FromRgb(95, 227, 176));
        private static readonly Brush TrackBrush = Frozen(Color.FromArgb(28, 255, 255, 255)), White = Frozen(Color.FromRgb(255, 255, 255)), LabelBrush = Frozen(Color.FromRgb(0xcf, 0xe8, 0xe8));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen Outline = FrozenPen(Color.FromArgb(90, 255, 255, 255), 1), Tick = FrozenPen(Color.FromArgb(90, 255, 255, 255), 1);

        private Palette _pal = CyanPal;
        private string _kind = "JOIN", _power = "", _sub = "", _title = "", _float = "";
        private int _oldRank = -1, _newRank = -1;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _kind = P(0); _power = P(1); _sub = P(2); _float = "";
            if (!int.TryParse(P(3), out _oldRank)) _oldRank = -1;
            if (!int.TryParse(P(4), out _newRank)) _newRank = -1;
            string gain = P(5);
            switch (_kind)
            {
                case "MERIT": _pal = GoldPal; _title = "MERITS EARNED"; _float = gain.Length > 0 ? "+" + gain : ""; break;
                case "RANK": _pal = VioletPal; _title = "POWER RANK UP"; break;
                case "DEFECT": _pal = AmberPal; _title = "POWER DEFECTION"; break;
                case "LEAVE": _pal = RedPal; _title = "LEFT POWER"; break;
                case "PAY": _pal = GreenPal; _title = "POWER SALARY"; _float = gain.Length > 0 ? "+" + gain + " CR" : ""; break;
                default: _kind = "JOIN"; _pal = CyanPal; _title = "POWER PLEDGED"; break;
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

            const double bx = 20, by = 54, bw = 200, bh = 14;
            double drop = EaseOut(Phase(t, 0.1, 0.4));
            Palette pal = _kind == "DEFECT" && t > 1.5 ? CyanPal : _pal;
            double f = 0.5;
            switch (_kind)
            {
                case "JOIN": f = EaseOut(Phase(t, 0.5, 1.0)) * 0.15; break;
                case "MERIT": f = 0.5 + 0.18 * EaseOut(Phase(t, 0.8, 1.0)); break;
                case "RANK": f = 0.9 + 0.1 * EaseOut(Phase(t, 0.6, 0.6)); break;
                case "DEFECT": f = 0.6; break;
                case "LEAVE": f = 0.6 * (1 - EaseOut(Phase(t, 1.2, 0.9))); break;
                case "PAY": f = 0.4 + 0.2 * EaseOut(Phase(t, 0.8, 1.0)); break;
            }

            if (drop > 0)
            {
                dc.PushOpacity(drop);
                dc.DrawRectangle(TrackBrush, null, new Rect(bx, by, bw, bh));
                if (f > 0.001) dc.DrawRectangle(pal.Solid, null, new Rect(bx, by, bw * f, bh));
                dc.DrawRectangle(null, Outline, new Rect(bx, by, bw, bh));
                for (int i = 1; i < 5; i++) dc.DrawLine(Tick, new Point(bx + bw * i / 5, by), new Point(bx + bw * i / 5, by + bh));
                int rank = _kind == "RANK" && t > 1.3 ? _newRank : (_kind == "RANK" ? _oldRank : _newRank >= 0 ? _newRank : _oldRank);
                string label = (rank >= 0 && _kind != "DEFECT" ? "RANK " + rank + "  " : "") + _power;
                if (_kind == "DEFECT") label = _power;
                DrawText(dc, label, MonoFace, Fit(label, 9, bw), LabelBrush, bx, by - 14, 1, TextAlignment.Left);
                dc.Pop();

                if (_float.Length > 0 && t > 0.8)
                {
                    double rise = Phase(t, 0.8, 1.0), fo = 1 - Phase(t, 1.9, 0.4);
                    DrawText(dc, _float, MonoFace, 14, pal.Solid, bx + bw * Math.Min(f, 0.95), by - 34 - rise * 10, drop * fo, TextAlignment.Center);
                }
                if (_kind == "RANK" && t > 1.3 && t < 1.9)
                {
                    dc.PushOpacity(1 - Phase(t, 1.3, 0.6));
                    dc.DrawRectangle(White, null, new Rect(bx, by, bw, bh));
                    dc.Pop();
                }
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _power, MonoFace, Fit(_power, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
