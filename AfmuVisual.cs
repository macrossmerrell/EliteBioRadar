using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Auto field maintenance unit repairs (AfmuRepairs, batched into one run): an "AFMU REPAIR RECEIPT" prints in from the top, one line per module with its health counting up.
    //   green when a module is fully restored, amber when it could only be partly repaired
    // Detail = "total modules|partly repaired|name~health percent;name~health percent;..." (up to five lines; the rest show as "+ N MORE").
    public sealed class AfmuVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private static readonly Color GreenC = Color.FromRgb(95, 227, 176), AmberC = Color.FromRgb(255, 180, 80);
        private static readonly Brush GreenBrush = Frozen(GreenC), AmberBrush = Frozen(AmberC), GreenInk = Frozen(Color.FromRgb(0x2f, 0x8f, 0x6f)), AmberInk = Frozen(Color.FromRgb(0xb8, 0x74, 0x1a));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), DimInk = Frozen(Color.FromRgb(0x77, 0x73, 0x68));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen GreenEdge = FrozenPen(Color.FromArgb(153, GreenC.R, GreenC.G, GreenC.B), 1), AmberEdge = FrozenPen(Color.FromArgb(153, AmberC.R, AmberC.G, AmberC.B), 1);
        private static readonly Pen Perf = MakePerf();

        private static Pen MakePerf()
        {
            var p = new Pen(Frozen(Color.FromRgb(0x77, 0x77, 0x77)), 0.7) { DashStyle = new DashStyle(new double[] { 2.5, 2.5 }, 0) };
            p.Freeze();
            return p;
        }

        private readonly List<(string Name, int Health)> _rows = new();
        private int _total, _partial;
        private bool _amber;
        private string _title = "", _name = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _rows.Clear();
            int.TryParse(p.Length > 0 ? p[0] : "", out _total); int.TryParse(p.Length > 1 ? p[1] : "", out _partial);
            if (p.Length > 2)
                foreach (var part in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var q = part.Split('~');
                    int hp = 100;
                    if (q.Length > 1) int.TryParse(q[1], out hp);
                    _rows.Add((q[0].Trim().ToUpperInvariant(), Math.Clamp(hp, 0, 100)));
                }
            if (_total < _rows.Count) _total = _rows.Count;
            bool single = _total <= 1;
            _amber = single && _partial > 0;
            _title = single ? (_amber ? "MODULE PARTLY REPAIRED" : "MODULE REPAIRED") : "MODULES REPAIRED";
            _name = single ? (_rows.Count > 0 ? _rows[0].Name : "MODULE") : _total + " MODULES";
            if (single) _sub = _amber ? $"HEALTH {(_rows.Count > 0 ? _rows[0].Health : 0)}%  CANNOT FULLY RESTORE" : "AFMU  FULLY RESTORED";
            else _sub = _partial == 0 ? "AFMU  ALL FULLY RESTORED" : $"AFMU  {_total - _partial} RESTORED  {_partial} PARTLY";
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

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

            int shown = Math.Min(_rows.Count, 5);
            int more = Math.Max(0, _total - shown);
            const double rw = 150, rx = 45, top = 8;
            double rh = 16 + shown * 11 + (more > 0 ? 11 : 0) + 8;
            double slide = EaseOut(Phase(t, 0.1, 0.5));
            if (slide > 0.01)
            {
                var accent = _amber ? AmberBrush : GreenBrush;
                dc.PushTransform(new TranslateTransform(0, -(1 - slide) * (rh + top + 10)));
                dc.DrawRectangle(Paper, null, new Rect(rx, top, rw, rh));
                dc.DrawRectangle(accent, null, new Rect(rx, top, rw, 10));
                DrawText(dc, "AFMU REPAIR RECEIPT", MonoFace, 6.5, BandInk, rx + 5, top + 1.5, 1, TextAlignment.Left);
                dc.DrawLine(Perf, new Point(rx + 4, top + 13), new Point(rx + rw - 4, top + 13));
                for (int i = 0; i < shown; i++)
                {
                    double y = top + 17 + i * 11, p = EaseOut(Phase(t, 0.5 + i * 0.35, 0.35));
                    var row = _rows[i];
                    double hp = row.Health * p;
                    DrawText(dc, row.Name, MonoFace, Fit(row.Name, 7, rw - 46), Ink, rx + 6, y, p, TextAlignment.Left);
                    DrawText(dc, Math.Round(hp) + "%", MonoFace, 7, row.Health >= 100 ? GreenInk : AmberInk, rx + rw - 6, y, p, TextAlignment.Right);
                }
                if (more > 0)
                    DrawText(dc, $"+ {more} MORE", MonoFace, 6.5, DimInk, rx + 6, top + 17 + shown * 11, Phase(t, 0.5 + shown * 0.35, 0.35), TextAlignment.Left);
                dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                var edge = _amber ? AmberEdge : GreenEdge;
                var solid = _amber ? AmberBrush : GreenBrush;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, edge, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
