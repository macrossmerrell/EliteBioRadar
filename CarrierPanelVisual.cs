using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Fleet carrier management events other than the jump (tritium, services, bank, trade orders, docking access, decommission): a six-row control panel
    // with the matching row lit up and animated.
    //   FUEL cyan, the bar fills and the tonnage counts up    SVC green (amber when off), a switch flips    BANK gold, credits count up    TRADE violet, the order quantity counts up
    //   DOCK cyan, the access setting types in with a lit dot    DECOM red, blinking countdown    CANCEL green, the countdown row clears
    // Detail = "ROW|carrier|chip title|chip line|prefix|number|suffix|bar from|bar to|switch state (1 on, 0 off)".
    public sealed class CarrierPanelVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.4;
        private sealed class Palette
        {
            public readonly Brush Solid, Wash;
            public readonly Pen Edge, PlateEdge;
            public readonly Color C;
            public Palette(Color c)
            {
                C = c; Solid = Frozen(c); Wash = Frozen(Color.FromArgb(46, c.R, c.G, c.B));
                Edge = FrozenPen(c, 1.2); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette CyanPal = new(Color.FromRgb(85, 242, 255)), GreenPal = new(Color.FromRgb(95, 227, 176)), AmberPal = new(Color.FromRgb(255, 180, 80)), GoldPal = new(Color.FromRgb(255, 216, 100)), VioletPal = new(Color.FromRgb(199, 155, 255)), RedPal = new(Color.FromRgb(255, 107, 107));
        private static readonly Brush RowBack = Frozen(Color.FromArgb(10, 255, 255, 255)), DimLabel = Frozen(Color.FromRgb(0x6f, 0xa0, 0xa0)), DimBar = Frozen(Color.FromArgb(34, 255, 255, 255)), White = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen PanelEdge = FrozenPen(Color.FromArgb(110, 111, 160, 160), 1);
        private static readonly string[] RowNames = { "FUEL", "SERVICES", "BANK", "ORDERS", "ACCESS", "STATUS" };

        private Palette _pal = CyanPal;
        private int _row;
        private string _carrier = "", _title = "", _sub = "", _prefix = "", _suffix = "";
        private double _number, _barFrom, _barTo;
        private bool _hasNumber, _on = true;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            string row = P(0).ToUpperInvariant();
            _carrier = P(1).ToUpperInvariant(); _title = P(2).ToUpperInvariant(); _sub = P(3).ToUpperInvariant();
            _prefix = P(4).ToUpperInvariant(); _suffix = P(6).ToUpperInvariant();
            _hasNumber = double.TryParse(P(5), NumberStyles.Float, CultureInfo.InvariantCulture, out _number);
            if (!double.TryParse(P(7), NumberStyles.Float, CultureInfo.InvariantCulture, out _barFrom)) _barFrom = 0.4;
            if (!double.TryParse(P(8), NumberStyles.Float, CultureInfo.InvariantCulture, out _barTo)) _barTo = 0.6;
            _on = P(9) != "0";
            switch (row)
            {
                case "SVC": _row = 1; _pal = _on ? GreenPal : AmberPal; break;
                case "BANK": _row = 2; _pal = _prefix.StartsWith("-") ? AmberPal : GoldPal; break;
                case "TRADE": _row = 3; _pal = VioletPal; break;
                case "DOCK": _row = 4; _pal = CyanPal; break;
                case "DECOM": _row = 5; _pal = RedPal; break;
                case "CANCEL": _row = 5; _pal = GreenPal; break;
                default: _row = 0; _pal = CyanPal; break;
            }
            if (_row == 5 && row == "CANCEL") _prefix = "SCRAP CANCELLED";
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

            double d = EaseOut(Phase(t, 0.1, 0.4));
            if (d > 0)
            {
                dc.PushOpacity(d);
                dc.DrawRectangle(null, PanelEdge, new Rect(14, 8, 212, 90));
                double p = EaseOut(Phase(t, 0.4, 1.0));
                for (int i = 0; i < 6; i++)
                {
                    double y = 12 + i * 14.3;
                    bool act = i == _row;
                    dc.DrawRectangle(act ? _pal.Wash : RowBack, null, new Rect(17, y, 206, 12.5));
                    DrawText(dc, RowNames[i], MonoFace, 7, act ? _pal.Solid : DimLabel, 22, y + 2.5, 1, TextAlignment.Left);
                    if (!act) { dc.DrawRectangle(DimBar, null, new Rect(80, y + 5, 50, 2.5)); continue; }
                    switch (_row)
                    {
                        case 0:
                        {
                            dc.DrawRectangle(DimBar, null, new Rect(80, y + 3.5, 90, 5.5));
                            dc.DrawRectangle(_pal.Solid, null, new Rect(80, y + 3.5, 90 * (_barFrom + (_barTo - _barFrom) * p), 5.5));
                            DrawText(dc, Count(p), MonoFace, 7, White, 176, y + 2.5, 1, TextAlignment.Left);
                            break;
                        }
                        case 1:
                        {
                            double knob = _on ? p : 1 - p;
                            dc.DrawRoundedRectangle(DimBar, null, new Rect(80, y + 2.5, 22, 7.5), 3.7, 3.7);
                            dc.DrawEllipse(_pal.Solid, null, new Point(84.5 + 13 * knob, y + 6.25), 4.2, 4.2);
                            DrawText(dc, _sub.Length > 0 ? Short(_sub, 18) : "SERVICE", MonoFace, 7, White, 110, y + 2.5, p, TextAlignment.Left);
                            break;
                        }
                        case 5:
                        {
                            double blink = _prefix == "SCRAP CANCELLED" ? 1 : 0.45 + 0.55 * Math.Abs(Math.Sin(t * 6));
                            DrawText(dc, _prefix.Length > 0 ? _prefix : "SCRAP SCHEDULED", MonoFace, 7, _pal.Solid, 80, y + 2.5, blink, TextAlignment.Left);
                            break;
                        }
                        case 4:
                        {
                            string s = _prefix; int n = (int)(p * s.Length);
                            DrawText(dc, s.Substring(0, Math.Min(n, s.Length)), MonoFace, 7, White, 80, y + 2.5, 1, TextAlignment.Left);
                            dc.PushOpacity(p);
                            dc.DrawEllipse(_pal.Solid, null, new Point(214, y + 6.25), 2.8, 2.8);
                            dc.Pop();
                            break;
                        }
                        default:
                            DrawText(dc, Count(p), MonoFace, 7, _pal.Solid, 80, y + 2.5, 1, TextAlignment.Left);
                            break;
                    }
                }
                dc.Pop();
            }

            double plateAt = 2.2, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _carrier, MonoFace, Fit(_carrier, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }

        private static string Short(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

        // Prefix + the number counted up to its value + suffix.
        private string Count(double p) => _hasNumber
            ? _prefix + (_prefix.Length > 0 && _prefix != "+" && _prefix != "-" ? " " : "") + Math.Round(_number * p).ToString("N0", CultureInfo.InvariantCulture) + (_suffix.Length > 0 ? " " + _suffix : "")
            : (_prefix + " " + _suffix).Trim();
    }
}
