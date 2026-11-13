using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The first time a material is ever found (MaterialDiscovered, which the game writes with the first pickup): a codex card flips from its back (a "?") to its front, which shows the category,
    // the material's glyph and its discovery number, and a FIRST FIND stamp lands on it.
    //   Manufactured amber (chip glyph)    Encoded violet (bit grid)    Raw cyan (chemical symbol)
    // Detail = "name|category|internal name|discovery number".
    public sealed class MaterialDiscoveredVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Edge, StampPen, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); Edge = FrozenPen(c, 1.6); StampPen = FrozenPen(c, 1.6); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette AmberPal = new(Color.FromRgb(255, 180, 80)), VioletPal = new(Color.FromRgb(199, 155, 255)), CyanPal = new(Color.FromRgb(85, 242, 255));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x3a, 0x3a, 0x3a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), BackFill = Frozen(Color.FromRgb(0x1a, 0x3a, 0x3a));
        private static readonly Brush StampBack = Frozen(Color.FromArgb(235, 8, 13, 13)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen InkPen = FrozenPen(Color.FromRgb(0x3a, 0x3a, 0x3a), 1.3);

        private Palette _pal = AmberPal;
        private int _kind = 1, _number;   // 0 raw, 1 manufactured, 2 encoded
        private string _name = "", _category = "", _symbol = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _name = P(0).ToUpperInvariant(); string cat = P(1);
            int.TryParse(P(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out _number);
            _kind = cat.StartsWith("Manu", StringComparison.OrdinalIgnoreCase) ? 1 : cat.StartsWith("Enc", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            _pal = _kind == 1 ? AmberPal : _kind == 2 ? VioletPal : CyanPal;
            _category = _kind == 1 ? "MANUFACTURED" : _kind == 2 ? "ENCODED" : "RAW";
            if (_kind == 0)
            {
                if (!MaterialVisual.TryElementSymbol(P(2), out _symbol))
                    _symbol = new string(_name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).Take(2).ToArray());
            }
            _sub = _number > 0 ? $"{_category}  DISCOVERY No. {_number}" : _category;
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        private void DrawGlyph(DrawingContext dc, double cx, double cy)
        {
            if (_kind == 0)
            {
                DrawText(dc, _symbol, MonoFace, _symbol.Length > 2 ? 19 : 24, Ink, cx, cy - 13, 1, TextAlignment.Center);
            }
            else if (_kind == 1)
            {
                dc.DrawRectangle(null, InkPen, new Rect(cx - 9, cy - 9, 18, 18));
                dc.DrawRectangle(Ink, null, new Rect(cx - 4, cy - 4, 8, 8));
                for (int i = 0; i < 4; i++)
                {
                    double o = -6 + i * 4;
                    dc.DrawRectangle(Ink, null, new Rect(cx - 13, cy + o - 0.7, 4, 1.4)); dc.DrawRectangle(Ink, null, new Rect(cx + 9, cy + o - 0.7, 4, 1.4));
                    dc.DrawRectangle(Ink, null, new Rect(cx + o - 0.7, cy - 13, 1.4, 4)); dc.DrawRectangle(Ink, null, new Rect(cx + o - 0.7, cy + 9, 1.4, 4));
                }
            }
            else
            {
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 5; c++)
                        if ((r * 7 + c * 3 + 3) % 3 != 0) dc.DrawRectangle(Ink, null, new Rect(cx - 12 + c * 5, cy - 12 + r * 5, 3.4, 3.4));
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

            const double cw = 76, ch = 84;
            double s = EaseOut(Phase(t, 0.1, 0.4)), flip = Phase(t, 0.6, 0.7);
            if (s > 0.01)
            {
                double sx = Math.Max(0.03, Math.Abs(Math.Cos(flip * Math.PI)));
                bool front = flip > 0.5;
                dc.PushOpacity(s);
                dc.PushTransform(new TranslateTransform(120, 47));
                dc.PushTransform(new ScaleTransform(s * sx, s));
                dc.DrawRectangle(front ? Paper : BackFill, null, new Rect(-cw / 2, -ch / 2, cw, ch));
                dc.DrawRectangle(null, _pal.Edge, new Rect(-cw / 2 + 1.5, -ch / 2 + 1.5, cw - 3, ch - 3));
                if (front)
                {
                    dc.DrawRectangle(_pal.Solid, null, new Rect(-cw / 2, -ch / 2, cw, 10));
                    DrawText(dc, _category, MonoFace, Fit(_category, 6, cw - 8), BandInk, 0, -ch / 2 + 1.8, 1, TextAlignment.Center);
                    DrawGlyph(dc, 0, -3);
                    DrawText(dc, "No. " + _number, MonoFace, 8, Ink, 0, ch / 2 - 15, 1, TextAlignment.Center);
                }
                else DrawText(dc, "?", MonoFace, 34, _pal.Solid, 0, -20, 1, TextAlignment.Center);
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            double sp = Phase(t, 1.4, 0.25);
            if (sp > 0)
            {
                const string stamp = "FIRST FIND";
                double sz = 9, sca = 1 + (1 - EaseOut(sp)) * 0.8, wBox = stamp.Length * (sz * 0.62) + 10;
                dc.PushTransform(new TranslateTransform(153, 74)); dc.PushTransform(new RotateTransform(-14)); dc.PushTransform(new ScaleTransform(sca, sca));
                dc.PushOpacity(EaseOut(sp));
                dc.DrawRectangle(StampBack, _pal.StampPen, new Rect(-wBox / 2, -sz * 0.8, wBox, sz * 1.6));
                DrawSpaced(dc, stamp, MonoFace, sz, _pal.Solid, 0, -sz * 0.6, 0.05, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "NEW MATERIAL FOUND", MonoFace, 12, _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
