using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Signals found on a body (the journal's FSSBodySignals and SAASignalsFound): one chip per signal type, each with a small icon and a count that ticks up. Biological in green, geological orange, human blue,
    // Guardian cyan, Thargoid violet, surface mining sites (the journal's Planetary Mining Location, the same violet as the planet panel) with a pick icon, anything else grey. The plate names the body; when a Guardian or Thargoid signal is there it says NON-HUMAN SIGNALS.
    // Detail = "body|TYPE:count;TYPE:count" with TYPE one of BIOLOGICAL, GEOLOGICAL, HUMAN, GUARDIAN, THARGOID or any other word (shown grey).
    public sealed class BodySignalsVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private enum Icon { Bio, Geo, Human, Guardian, Thargoid, Mining, Other }

        private sealed class Palette
        {
            public readonly Brush Solid, Fill;
            public readonly Pen Edge, IconPen, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c); Fill = Frozen(Color.FromArgb(26, c.R, c.G, c.B));
                Edge = FrozenPen(Color.FromArgb(179, c.R, c.G, c.B), 1.3); IconPen = FrozenPen(c, 1.5); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette BioPal = new(Color.FromRgb(95, 227, 176)), GeoPal = new(Color.FromRgb(255, 150, 80)), HumanPal = new(Color.FromRgb(120, 190, 255)),
            GuardianPal = new(Color.FromRgb(85, 242, 255)), ThargoidPal = new(Color.FromRgb(190, 120, 255)), MiningPal = new(Color.FromRgb(0xb9, 0x78, 0xff)), OtherPal = new(Color.FromRgb(165, 175, 195));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Geometry GeoTri = Poly(new[] { (-7.0, 6.0), (0.0, -8.0), (7.0, 6.0) }), GuardianDiamond = Poly(new[] { (0.0, -8.0), (6.0, 0.0), (0.0, 8.0), (-6.0, 0.0) }),
            ThargoidHex = MakeHex(), ThargoidEye = MakeEye(), PickHead = MakePickHead();

        // A pick: the curved head (stroked) over a diagonal handle.
        private static Geometry MakePickHead()
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(-7, -3), false, false); c.QuadraticBezierTo(new Point(0, -10), new Point(8, -1), true, false); }
            g.Freeze(); return g;
        }

        private static Geometry MakeHex() { var pts = new (double, double)[6]; for (int i = 0; i < 6; i++) pts[i] = (Math.Cos(i * Math.PI / 3) * 7, Math.Sin(i * Math.PI / 3) * 7); return Poly(pts); }
        private static Geometry MakeEye()
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(-3, 0), true, true); c.QuadraticBezierTo(new Point(0, -4), new Point(3, 0), true, false); c.QuadraticBezierTo(new Point(0, 4), new Point(-3, 0), true, false); }
            g.Freeze(); return g;
        }

        private readonly List<(Icon icon, string label, int count)> _chips = new();
        private string _body = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _body = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _chips.Clear();
            if (p.Length > 1)
                foreach (var part in p[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0) continue;
                    string label = kv[0].Trim().ToUpperInvariant();
                    if (label.Contains("MINING")) label = "MINING SITES";   // the journal's "Planetary Mining Location"
                    Icon icon = label switch { "BIOLOGICAL" => Icon.Bio, "GEOLOGICAL" => Icon.Geo, "HUMAN" => Icon.Human, "GUARDIAN" => Icon.Guardian, "THARGOID" => Icon.Thargoid, "MINING SITES" => Icon.Mining, _ => Icon.Other };
                    if (icon == Icon.Other && label.Length == 0) label = "OTHER";
                    if (_chips.Count < 4) _chips.Add((icon, label, n));
                }
            if (_chips.Count == 0) _chips.Add((Icon.Other, "OTHER", 1));
        }

        private static Palette PalOf(Icon i) => i switch { Icon.Bio => BioPal, Icon.Geo => GeoPal, Icon.Human => HumanPal, Icon.Guardian => GuardianPal, Icon.Thargoid => ThargoidPal, Icon.Mining => MiningPal, _ => OtherPal };

        private static void DrawIcon(DrawingContext dc, Icon icon, Palette pal, double x, double y)
        {
            dc.PushTransform(new TranslateTransform(x, y));
            switch (icon)
            {
                case Icon.Bio: dc.DrawEllipse(pal.Solid, null, new Point(0, 0), 6, 6); break;
                case Icon.Geo: dc.DrawGeometry(pal.Solid, null, GeoTri); break;
                case Icon.Human: dc.DrawEllipse(pal.Solid, null, new Point(0, -3), 3, 3); dc.DrawRectangle(pal.Solid, null, new Rect(-4, 1, 8, 6)); break;
                case Icon.Guardian: dc.DrawGeometry(null, pal.IconPen, GuardianDiamond); dc.DrawEllipse(pal.Solid, null, new Point(0, 0), 2, 2); break;
                case Icon.Thargoid: dc.DrawGeometry(null, pal.IconPen, ThargoidHex); dc.DrawGeometry(pal.Solid, null, ThargoidEye); break;
                case Icon.Mining: dc.DrawLine(pal.IconPen, new Point(-6, 7), new Point(4, -3)); dc.DrawGeometry(null, pal.IconPen, PickHead); break;
                default: dc.DrawEllipse(null, pal.IconPen, new Point(0, 0), 5, 5); break;
            }
            dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            int n = _chips.Count;
            double cw = Math.Min(62, (SceneW - 40 - (n - 1) * 8) / n);
            var main = PalOf(_chips[0].icon);
            bool nonHuman = _chips.Exists(c => c.icon is Icon.Guardian or Icon.Thargoid);

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            for (int i = 0; i < n; i++)
            {
                var (icon, label, count) = _chips[i];
                var pal = PalOf(icon);
                double x = SceneW / 2 + (i - (n - 1) / 2.0) * (cw + 10), kk = EaseOut(Phase(t, 0.2 + i * 0.3, 0.4));
                dc.PushOpacity(kk);
                dc.PushTransform(new TranslateTransform(0, (1 - kk) * 12));
                dc.DrawRectangle(pal.Fill, pal.Edge, new Rect(x - cw / 2, 14, cw, 66));
                DrawIcon(dc, icon, pal, x, 29);
                int shown = (int)Math.Round(count * EaseOut(Phase(t, 0.5 + i * 0.3, 0.6)));
                DrawSpaced(dc, shown.ToString(CultureInfo.InvariantCulture), MonoFace, 20, NameBrush, x, 44, 0.04, 1);
                DrawSpaced(dc, label, MonoFace, Fit(label, 7, cw - 4), pal.Solid, x, 68, 0.05, 1);
                dc.Pop(); dc.Pop();
            }

            string title = nonHuman ? "NON-HUMAN SIGNALS" : "SURFACE SIGNALS", l2 = string.Join("  -  ", _chips.ConvertAll(c => c.count.ToString(CultureInfo.InvariantCulture) + " " + c.label)), l3 = _body.Length > 0 ? "BODY " + _body : "";
            double plateAt = 1.9, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, main.PlateEdge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, title, MonoFace, Fit(title, 12, 180), main.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), main.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
