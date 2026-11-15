using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A planet the commander has held as the nav target: the planet with a scan line sweeping down it, then gravity / temperature / pressure bars filling, and a plate with
    // the class and atmosphere; the bio and geo signals, landable and first-discovery chips land last.
    // Detail = "name|class|gravity g|temperature K|pressure atm|atmosphere|mapped 1/0|bio|geo|landable 1/0|first discovery 1/0".
    public sealed class PlanetTargetVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 6.6;
        private static readonly Color Cyan = Color.FromRgb(85, 242, 255);
        private static readonly Brush CyanB = Frozen(Cyan), AmberB = Frozen(Color.FromRgb(255, 180, 80)), VioletB = Frozen(Color.FromRgb(199, 155, 255)), GreenB = Frozen(Color.FromRgb(95, 227, 176));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Dim = Frozen(Color.FromRgb(0x5d, 0x7f, 0x86)), Track = Frozen(Color.FromArgb(26, 255, 255, 255));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), ChipBack = Frozen(Color.FromArgb(230, 6, 11, 16)), SweepGlow = Frozen(Color.FromArgb(40, Cyan.R, Cyan.G, Cyan.B));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(128, Cyan.R, Cyan.G, Cyan.B), 1);

        private string _name = "", _class = "", _sub = "", _atmos = "";
        private double _g, _k, _atm;
        private bool _mapped;
        private int _bio, _geo;
        private Brush _planet = Brushes.Gray;
        private readonly System.Collections.Generic.List<(string Text, Brush Color, Pen Edge)> _chips = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            var inv = CultureInfo.InvariantCulture;
            _name = P(0).ToUpperInvariant(); _class = P(1).ToUpperInvariant();
            double.TryParse(P(2), NumberStyles.Float, inv, out _g); double.TryParse(P(3), NumberStyles.Float, inv, out _k); double.TryParse(P(4), NumberStyles.Float, inv, out _atm);
            _atmos = P(5).ToUpperInvariant(); _mapped = P(6) == "1";
            int.TryParse(P(7), NumberStyles.Integer, inv, out _bio); int.TryParse(P(8), NumberStyles.Integer, inv, out _geo);
            bool landable = P(9) == "1", first = P(10) == "1";
            _sub = (_atmos.Length > 0 ? _atmos : "NO ATMOSPHERE") + "  -  " + (_mapped ? "MAPPED" : "NOT YET MAPPED");
            _chips.Clear();
            if (_bio > 0) _chips.Add(($"BIO {_bio}", VioletB, FrozenPen(Color.FromRgb(199, 155, 255), 0.8)));
            if (_geo > 0) _chips.Add(($"GEO {_geo}", AmberB, FrozenPen(Color.FromRgb(255, 180, 80), 0.8)));
            if (landable) _chips.Add(("LANDABLE", GreenB, FrozenPen(Color.FromRgb(95, 227, 176), 0.8)));
            if (first) _chips.Add(("FIRST", AmberB, FrozenPen(Color.FromRgb(255, 180, 80), 0.8)));

            // The planet's tint follows its class.
            string c = _class;
            Color hi, mid, lo;
            if (c.Contains("ICY")) { hi = Color.FromRgb(0xe6, 0xf2, 0xff); mid = Color.FromRgb(0x8f, 0xb4, 0xd4); lo = Color.FromRgb(0x1f, 0x2d, 0x3d); }
            else if (c.Contains("WATER") || c.Contains("EARTH") || c.Contains("AMMONIA")) { hi = Color.FromRgb(0x9f, 0xd0, 0xf0); mid = Color.FromRgb(0x2f, 0x6a, 0x9a); lo = Color.FromRgb(0x0b, 0x1a, 0x2a); }
            else if (c.Contains("GAS GIANT")) { hi = Color.FromRgb(0xe8, 0xcf, 0xa0); mid = Color.FromRgb(0xa8, 0x7e, 0x58); lo = Color.FromRgb(0x2a, 0x1d, 0x14); }
            else if (c.Contains("METAL")) { hi = Color.FromRgb(0xd9, 0xb3, 0x8a); mid = Color.FromRgb(0x8a, 0x62, 0x40); lo = Color.FromRgb(0x2a, 0x1d, 0x14); }
            else { hi = Color.FromRgb(0xc4, 0xbc, 0xb0); mid = Color.FromRgb(0x7a, 0x72, 0x68); lo = Color.FromRgb(0x20, 0x1d, 0x1a); }
            var rb = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.4, 0.4), RadiusX = 0.75, RadiusY = 0.75 };
            rb.GradientStops.Add(new GradientStop(hi, 0)); rb.GradientStops.Add(new GradientStop(mid, 0.6)); rb.GradientStops.Add(new GradientStop(lo, 1));
            rb.Freeze(); _planet = rb;
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

            // The planet, with the scan line sweeping down it.
            const double cx = 48, cy = 42, r = 26;
            double pe = EaseOut(Phase(t, 0.1, 0.4));
            dc.PushOpacity(pe);
            dc.DrawEllipse(_planet, null, new Point(cx, cy), r, r);
            dc.Pop();
            double sw = Phase(t, 0.5, 1.6);
            if (sw > 0 && sw < 1)
            {
                double y = cy - r - 4 + sw * (2 * r + 8), vis = Math.Min(Clamp01(sw / 0.15), Clamp01((1 - sw) / 0.15));
                dc.PushOpacity(vis);
                dc.DrawRectangle(SweepGlow, null, new Rect(cx - r - 4, y - 8, 2 * r + 8, 8));
                dc.DrawRectangle(CyanB, null, new Rect(cx - r - 4, y - 1, 2 * r + 8, 2));
                dc.Pop();
            }

            DrawText(dc, "SURFACE SCAN", MonoFace, 6.5, CyanB, 96, 14, Phase(t, 2.0, 0.45), TextAlignment.Left);
            DrawText(dc, _name, MonoFace, Fit(_name, 9.5, 134), NameBrush, 96, 26, Phase(t, 2.1, 0.45), TextAlignment.Left);

            // Gravity, temperature and pressure fill in turn.
            double gf = Clamp01(_g / 2.5), tf = Clamp01(_k / 800), pf = _atm <= 0 ? 0 : Clamp01(Math.Log10(1 + _atm * 10) / 2);
            string gt = _g > 0 ? _g.ToString("0.00", CultureInfo.InvariantCulture) + " G" : "-", tt = _k > 0 ? Math.Round(_k).ToString("0", CultureInfo.InvariantCulture) + " K" : "-";
            string pt = _atm <= 0 ? "NONE" : _atm < 0.01 ? "<0.01 ATM" : _atm.ToString(_atm < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " ATM";
            var rows = new (string L, string V, double F, Brush B)[] { ("GRAVITY", gt, gf, CyanB), ("TEMP", tt, tf, AmberB), ("PRESSURE", pt, pf, VioletB) };
            for (int i = 0; i < rows.Length; i++)
            {
                double y = 56 + i * 17, st = 2.4 + i * 0.35, o = Phase(t, st, 0.4);
                if (o <= 0) continue;
                DrawText(dc, rows[i].L, MonoFace, 6, Dim, 96, y - 12, o, TextAlignment.Left);
                DrawText(dc, rows[i].V, MonoFace, 6, NameBrush, 230, y - 12, o, TextAlignment.Right);
                dc.PushOpacity(o); dc.DrawRectangle(Track, null, new Rect(96, y, 134, 4)); dc.Pop();
                dc.DrawRectangle(rows[i].B, null, new Rect(96, y, 134 * rows[i].F * EaseOut(Phase(t, st + 0.2, 0.8)), 4));
            }

            // The plate: class, atmosphere and whether it is mapped, then the chips.
            double plateAt = 3.6, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(14, 98, 212, _chips.Count > 0 ? 46 : 34), 5, 5);
                dc.Pop();
                DrawSpaced(dc, _class, MonoFace, Fit(_class, 8, 200), CyanB, SceneW / 2, 103, 0.08, Phase(t, plateAt + 0.1, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 6.5, 200), AmberB, SceneW / 2, 115, 0.08, Phase(t, plateAt + 0.3, 0.4));
            }
            if (_chips.Count > 0)
            {
                double total = -6;
                foreach (var c in _chips) total += c.Text.Length * 4.3 + 12 + 6;
                double x = (SceneW - total) / 2;
                for (int i = 0; i < _chips.Count; i++)
                {
                    var c = _chips[i];
                    double cw = c.Text.Length * 4.3 + 12, pp = EaseOut(Phase(t, 4.3 + i * 0.2, 0.35));
                    if (pp > 0)
                    {
                        dc.PushOpacity(pp);
                        dc.PushTransform(new TranslateTransform(x + cw / 2, 133)); dc.PushTransform(new ScaleTransform(0.5 + 0.5 * pp, 0.5 + 0.5 * pp));
                        dc.DrawRoundedRectangle(ChipBack, c.Edge, new Rect(-cw / 2, -6, cw, 12), 2, 2);
                        DrawText(dc, c.Text, MonoFace, 6.5, c.Color, 0, -4.5, 1, TextAlignment.Center);
                        dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    x += cw + 6;
                }
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
