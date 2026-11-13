using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Mining, three moments in one visual (the journal's ProspectedAsteroid, AsteroidCracked and MiningRefined), all amber:
    //   PROSPECT: a stacked composition bar fills with each material's share (named above its slice) and the content grade lights 1-3 dots.
    //   CRACKED:  the rock flashes and splits in two, throwing debris.
    //   REFINED:  an ingot drops into a cargo box.
    // Detail = "PROSPECT|GRADE|MATERIAL:percent;MATERIAL:percent", "CRACKED|" or "REFINED|item|tonnes".
    public sealed class MiningVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, CX = 120, CY = 50;
        private static readonly Color Amber = Color.FromRgb(255, 180, 80);
        private static readonly Color[] SliceColours = { Color.FromRgb(255, 180, 80), Color.FromRgb(85, 242, 255), Color.FromRgb(200, 122, 255), Color.FromRgb(125, 255, 176) };
        private static readonly Brush[] SliceBrushes = Array.ConvertAll(SliceColours, Frozen);
        private static readonly Brush AmberBrush = Frozen(Amber), AmberDim = Frozen(Color.FromArgb(64, 255, 180, 80)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)),
            RockBrush = Frozen(Color.FromRgb(0x3a, 0x34, 0x2e)), CargoText = Frozen(Color.FromRgb(0x8a, 0x80, 0x74)), Flash = Frozen(Colors.White);
        private static readonly Pen AmberEdge = FrozenPen(Color.FromArgb(179, 255, 180, 80), 1.2), PlateEdge = FrozenPen(Color.FromArgb(153, 255, 180, 80), 1), RockEdge = FrozenPen(Color.FromRgb(0x6b, 0x62, 0x58), 1.2),
            BarEdge = FrozenPen(Color.FromArgb(128, 255, 180, 80), 1), CargoEdge = FrozenPen(Color.FromRgb(0x6b, 0x62, 0x58), 1);
        private static readonly Pen[] ShockPens = AlphaPens(Color.FromRgb(255, 120, 80), 1.6);
        private static readonly Brush[] DebrisBrushes = AlphaBrushes(Amber);
        private static readonly Geometry RockWhole = MakeRock(0, 26), RockHalfA = MakeRock(5, 20), RockHalfB = MakeRock(9, 17);

        private static Geometry MakeRock(int seed, double r)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                const int n = 9;
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2 / n, rr = r * (0.78 + Hash(i + 3 + seed) * 0.3);
                    var p = new Point(Math.Cos(a) * rr, Math.Sin(a) * rr);
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, false);
                }
            }
            g.Freeze(); return g;
        }

        private enum Kind { Prospect, Cracked, Refined }
        private Kind _kind;
        private string _grade = "", _item = "";
        private int _tonnes;
        private readonly List<(string name, double pct)> _mats = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string k = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _kind = k == "CRACKED" ? Kind.Cracked : k == "REFINED" ? Kind.Refined : Kind.Prospect;
            _mats.Clear(); _grade = ""; _item = ""; _tonnes = 0;
            if (_kind == Kind.Prospect)
            {
                _grade = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "";
                if (p.Length > 2)
                    foreach (var part in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int c = part.LastIndexOf(':');
                        if (c > 0 && double.TryParse(part[(c + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct > 0 && _mats.Count < 4)
                            _mats.Add((part[..c].Trim().ToUpperInvariant(), pct));
                    }
            }
            else if (_kind == Kind.Refined)
            {
                _item = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "";
                if (p.Length > 2) int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _tonnes);
                if (_tonnes < 1) _tonnes = 1;
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));
        private int GradeDots => _grade.StartsWith("HIGH") ? 3 : _grade.StartsWith("MED") ? 2 : _grade.Length > 0 ? 1 : 0;

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

            string l1, l2, l3;
            if (_kind == Kind.Prospect)
            {
                DrawProspect(dc, t);
                l1 = "ASTEROID PROSPECTED"; l2 = _grade.Length > 0 ? "CONTENT: " + _grade : "COMPOSITION SCANNED"; l3 = string.Join("  ", _mats.ConvertAll(m => m.name + " " + Math.Round(m.pct).ToString(CultureInfo.InvariantCulture) + "%"));
            }
            else if (_kind == Kind.Cracked)
            {
                DrawCracked(dc, t);
                l1 = "ASTEROID CRACKED"; l2 = "CORE EXPOSED"; l3 = "COLLECT THE FRAGMENTS";
            }
            else
            {
                DrawRefined(dc, t);
                l1 = "MINERAL REFINED"; l2 = _item.Length > 0 ? _item : "MINERAL"; l3 = "x" + _tonnes.ToString(CultureInfo.InvariantCulture) + (_tonnes == 1 ? " TONNE" : " TONNES") + " IN HOLD";
            }

            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = l3.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 190), AmberBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 190), AmberBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

        private void DrawProspect(DrawingContext dc, double t)
        {
            const double x0 = 30, bw = 180, y = 34, bh = 12;
            dc.DrawRectangle(null, BarEdge, new Rect(x0, y, bw, bh));
            double acc = 0;
            for (int i = 0; i < _mats.Count; i++)
            {
                double share = Math.Min(100 - acc, _mats[i].pct), p = EaseOut(Phase(t, 0.3 + i * 0.4, 0.6));
                dc.DrawRectangle(SliceBrushes[i % SliceBrushes.Length], null, new Rect(x0 + bw * acc / 100, y, bw * share / 100 * p, bh));
                double lab = Phase(t, 0.8 + i * 0.4, 0.4);
                if (lab > 0) DrawSpaced(dc, _mats[i].name, MonoFace, Fit(_mats[i].name, 7, Math.Max(34, bw * share / 100 + 12)), NameBrush, x0 + bw * (acc + share / 2) / 100, y - 12, 0.03, lab);
                acc += share;
            }
            int dots = GradeDots;
            for (int i = 0; i < 3; i++)
            {
                double q = Phase(t, 1.6 + i * 0.2, 0.3);
                var c = new Point(CX + (i - 1) * 18, 70);
                dc.DrawEllipse(null, AmberEdge, c, 5, 5);
                if (i < dots && q > 0) dc.DrawEllipse(AmberBrush, null, c, 5 * EaseOut(q), 5 * EaseOut(q));
            }
        }

        private void DrawCracked(DrawingContext dc, double t)
        {
            double p = EaseOut(Phase(t, 0.4, 1.2)), flash = Phase(t, 0.4, 0.12) * (1 - Phase(t, 0.5, 0.3));
            double ring = Phase(t, 0.3, 0.6);
            if (p <= 0)
            {
                dc.PushTransform(new TranslateTransform(CX, CY)); dc.PushTransform(new RotateTransform(t * 8));
                dc.DrawGeometry(RockBrush, RockEdge, RockWhole);
                dc.Pop(); dc.Pop();
                if (ring > 0 && ring < 1) dc.DrawEllipse(null, ShockPens[(int)Math.Round((1 - ring) * 8)], new Point(CX, CY), 10 + ring * 44, 10 + ring * 44);
            }
            else
            {
                dc.PushTransform(new TranslateTransform(CX - p * 26, CY - p * 8)); dc.PushTransform(new RotateTransform(-p * 28));
                dc.DrawGeometry(RockBrush, RockEdge, RockHalfA);
                dc.Pop(); dc.Pop();
                dc.PushTransform(new TranslateTransform(CX + p * 26, CY + p * 8)); dc.PushTransform(new RotateTransform(p * 34));
                dc.DrawGeometry(RockBrush, RockEdge, RockHalfB);
                dc.Pop(); dc.Pop();
                for (int i = 0; i < 14; i++)
                {
                    double ang = Hash(i) * Math.PI * 2, d = p * (14 + Hash(i + 9) * 40);
                    dc.DrawRectangle(DebrisBrushes[(int)Math.Round((1 - p * 0.6) * 8)], null, new Rect(CX + Math.Cos(ang) * d, CY + Math.Sin(ang) * d, 2, 2));
                }
            }
            if (flash > 0) { dc.PushOpacity(flash * 0.8); dc.DrawEllipse(Flash, null, new Point(CX, CY), 24, 24); dc.Pop(); }
        }

        private void DrawRefined(DrawingContext dc, double t)
        {
            var box = new Rect(CX - 28, CY + 8, 56, 30);
            double p = EaseOut(Phase(t, 0.3, 0.9));
            bool landed = p > 0.95;
            dc.DrawRectangle(landed ? AmberDim : null, CargoEdge, box);
            DrawSpaced(dc, "CARGO", MonoFace, 7, CargoText, CX, CY + 41, 0.1, 1);
            dc.DrawRectangle(AmberBrush, null, new Rect(CX - 8, CY - 34 + p * 50, 16, 8));
        }
    }
}
