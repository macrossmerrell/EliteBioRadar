using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // On-foot kit changes. Loadouts: the Artemis-style suit stands in the middle and lines run out to the two weapons, lighting up one after the other
    // (a deleted loadout fades with a strike line). Consumables: an energy cell charges a battery bar, a medkit pulses a green cross, anything else shows a canister.
    // Detail = "KIND|loadout or item name|second line|primary weapon|sidearm", KIND = SW / NEW / DEL / CELL / KIT / USE.
    public sealed class SuitLoadoutVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private static readonly Color Green = Color.FromRgb(159, 240, 160), Cyan = Color.FromRgb(85, 242, 255), Red = Color.FromRgb(255, 122, 122), Yellow = Color.FromRgb(255, 210, 63), Med = Color.FromRgb(125, 255, 154);
        private static Brush B(Color c) => Frozen(c);
        private static readonly Brush White = B(Color.FromRgb(0xe9, 0xee, 0xf0)), Orange = B(Color.FromRgb(0xe0, 0x8a, 0x3c)), Grey = B(Color.FromRgb(0x8d, 0x97, 0x9b)), Visor = B(Color.FromRgb(0x0b, 0x0f, 0x12));
        private static readonly Pen Outline = FrozenPen(Color.FromRgb(0x5a, 0x66, 0x6a), 0.8);
        private static readonly Brush NameBrush = B(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = B(Color.FromRgb(255, 216, 144)), PlateBrush = B(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush SegOff = B(Color.FromArgb(20, 255, 255, 255)), CardBrush = B(Color.FromRgb(0xe8, 0xee, 0xee)), CrossBrush = B(Color.FromRgb(0x1f, 0x9a, 0x4a)), DarkText = B(Color.FromRgb(8, 13, 13));
        private static readonly Pen CasePen = FrozenPen(Color.FromRgb(0xcf, 0xe8, 0xe8), 1.5);

        // The suit's torso and chest band never change, so they are built once.
        private static readonly Geometry Torso = Poly(new[] { (-13.0, -24.0), (13, -24), (9, -2), (-9, -2) }), ChestBand = Poly(new[] { (-12.0, -21.0), (12, -21), (11, -16), (0, -13), (-11, -16) });

        private Color _col = Green;
        private Brush _solid = B(Green);
        private Pen _linePen = FrozenPen(Green, 0.8), _ringPen = FrozenPen(Green, 1.5), _platePen = FrozenPen(Color.FromArgb(153, 159, 240, 160), 1), _strikePen = FrozenPen(Red, 2.2);
        private string _kind = "SW", _title = "", _name = "", _sub = "", _w1 = "", _w2 = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _kind = P(0).ToUpperInvariant(); _name = P(1).ToUpperInvariant(); _sub = P(2).ToUpperInvariant(); _w1 = P(3).ToUpperInvariant(); _w2 = P(4).ToUpperInvariant();
            switch (_kind)
            {
                case "NEW": _col = Cyan; _title = "LOADOUT SAVED"; break;
                case "DEL": _col = Red; _title = "LOADOUT DELETED"; break;
                case "CELL": _col = Yellow; _title = "ENERGY CELL USED"; _name = "SUIT BATTERY RECHARGED"; _sub = "CONSUMABLE"; break;
                case "KIT": _col = Med; _title = "MEDKIT USED"; _name = "HEALTH RESTORED"; _sub = "CONSUMABLE"; break;
                case "USE": _col = Cyan; _title = (_name.Length > 0 ? _name : "ITEM") + " USED"; _name = "ITEM CONSUMED"; _sub = "CONSUMABLE"; break;
                default: _kind = "SW"; _col = Green; _title = "LOADOUT EQUIPPED"; break;
            }
            _solid = B(_col); _linePen = FrozenPen(_col, 0.8); _ringPen = FrozenPen(_col, 1.5);
            _platePen = FrozenPen(Color.FromArgb(153, _col.R, _col.G, _col.B), 1);
        }


        private static void RR(DrawingContext dc, Brush fill, Pen? pen, double x, double y, double w, double h, double r) => dc.DrawRoundedRectangle(fill, pen, new Rect(x, y, w, h), r, r);

        // The Artemis-style suit: white armour with orange bands, a dark visor, pauldrons, a belt and a pack on the right hip. Drawn about (0,0), roughly 90 units tall.
        private void DrawSuit(DrawingContext dc)
        {
            RR(dc, White, Outline, -9, 3, 8, 21, 3); RR(dc, White, Outline, 1, 3, 8, 21, 3);
            RR(dc, Orange, null, -8.5, 20, 7, 5, 2); RR(dc, Orange, null, 1.5, 20, 7, 5, 2);
            RR(dc, White, Outline, -8, 25, 7, 16, 2.5); RR(dc, White, Outline, 1, 25, 7, 16, 2.5);
            RR(dc, Grey, Outline, -10, 40, 10, 6, 2.5); RR(dc, Grey, Outline, 0, 40, 10, 6, 2.5);
            RR(dc, White, Outline, -24, -15, 7, 16, 3); RR(dc, White, Outline, 17, -15, 7, 16, 3);
            RR(dc, White, Outline, -25, 0, 7, 14, 3); RR(dc, White, Outline, 18, 0, 7, 14, 3);
            RR(dc, Grey, null, -25, 13, 7, 5, 2); RR(dc, Grey, null, 18, 13, 7, 5, 2);
            dc.DrawGeometry(White, Outline, Torso);
            dc.DrawGeometry(Orange, null, ChestBand);
            RR(dc, White, Outline, -21, -26, 12, 9, 4); RR(dc, White, Outline, 9, -26, 12, 9, 4);
            dc.DrawRectangle(Orange, null, new Rect(-19, -22, 8, 2)); dc.DrawRectangle(Orange, null, new Rect(11, -22, 8, 2));
            dc.DrawRectangle(Orange, null, new Rect(-10, -3, 20, 4));
            RR(dc, White, Outline, 10, 1, 10, 14, 4.5);
            RR(dc, Grey, null, -5, -28, 10, 5, 1);
            dc.DrawEllipse(White, Outline, new Point(0, -35), 8, 9.5);
            dc.DrawEllipse(Visor, null, new Point(0, -34), 6, 5.6);
            dc.PushOpacity(0.5); dc.DrawEllipse(_solid, null, new Point(-2, -36), 2.4, 1.4); dc.Pop();
        }

        private void DrawConsumable(DrawingContext dc, double t)
        {
            double s = EaseOut(Phase(t, 0.1, 0.4));
            if (s <= 0) return;
            dc.PushOpacity(s);
            double cx = 120, cy = 47;
            if (_kind == "CELL")
            {
                RR(dc, null!, CasePen, 70, 30, 90, 32, 5);
                dc.DrawRectangle(CasePen.Brush, null, new Rect(160, 40, 5, 12));
                double fill = EaseOut(Phase(t, 0.7, 1.2));
                for (int i = 0; i < 6; i++) dc.DrawRectangle(fill * 6 > i + 0.2 ? _solid : SegOff, null, new Rect(74 + i * 14.2, 34, 12, 24));
                double cp = EaseOut(Phase(t, 0.1, 0.6)), drop = 6 + cp * 22 * (1 - Phase(t, 0.7, 0.3));
                RR(dc, _solid, null, 108, drop, 15, 22, 3);
                DrawText(dc, "+", MonoFace, 11, DarkText, 115.5, drop + 1, 1, TextAlignment.Center);
            }
            else if (_kind == "KIT")
            {
                double r = Phase(t, 0.7, 1.2);
                if (r < 1) { dc.PushOpacity(1 - r); dc.DrawEllipse(null, _ringPen, new Point(cx, cy), 15 + r * 38, 15 + r * 38); dc.Pop(); }
                RR(dc, CardBrush, null, cx - 23, cy - 17, 46, 34, 5);
                double pl = 1 + (t > 0.7 ? 0.1 * Math.Sin(t * 8) : 0);
                dc.PushTransform(new TranslateTransform(cx, cy)); dc.PushTransform(new ScaleTransform(pl, pl));
                dc.DrawRectangle(CrossBrush, null, new Rect(-3, -11, 6, 22)); dc.DrawRectangle(CrossBrush, null, new Rect(-11, -3, 22, 6));
                dc.Pop(); dc.Pop();
            }
            else
            {
                double r = Phase(t, 0.7, 1.2);
                if (r < 1) { dc.PushOpacity(1 - r); dc.DrawEllipse(null, _ringPen, new Point(cx, cy), 15 + r * 38, 15 + r * 38); dc.Pop(); }
                RR(dc, CardBrush, null, cx - 12, cy - 20, 24, 40, 6);
                dc.DrawRectangle(_solid, null, new Rect(cx - 12, cy - 6, 24, 10));
            }
            dc.Pop();
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

            if (_kind == "CELL" || _kind == "KIT" || _kind == "USE") DrawConsumable(dc, t);
            else
            {
                bool del = _kind == "DEL";
                double s = EaseOut(Phase(t, 0.1, 0.3));
                double suitA = del ? s * (1 - EaseOut(Phase(t, 1.4, 0.6))) : s;
                const double cx = 120, cy = 54, sc = 0.9;
                if (suitA > 0.01)
                {
                    dc.PushOpacity(suitA);
                    dc.PushTransform(new TranslateTransform(cx, cy)); dc.PushTransform(new ScaleTransform(sc, sc));
                    DrawSuit(dc);
                    dc.Pop(); dc.Pop(); dc.Pop();
                }
                if (!del)
                {
                    double a1 = EaseOut(Phase(t, 0.6, 0.4)), a2 = EaseOut(Phase(t, 1.0, 0.4));
                    if (_w1.Length > 0 && a1 > 0)
                    {
                        double y = cy - 13;
                        dc.DrawLine(_linePen, new Point(cx - 22, y), new Point(cx - 22 - 30 * a1, y));
                        dc.PushOpacity(a1); dc.DrawEllipse(_solid, null, new Point(cx - 52, y), 2.2, 2.2); dc.Pop();
                        DrawText(dc, _w1, MonoFace, Fit(_w1, 7, 62), _solid, cx - 58, y - 4.5, a1, TextAlignment.Right);
                    }
                    if (_w2.Length > 0 && a2 > 0)
                    {
                        double y = cy + 8;
                        dc.DrawLine(_linePen, new Point(cx + 22, y), new Point(cx + 22 + 30 * a2, y));
                        dc.PushOpacity(a2); dc.DrawEllipse(_solid, null, new Point(cx + 52, y), 2.2, 2.2); dc.Pop();
                        DrawText(dc, _w2, MonoFace, Fit(_w2, 7, 62), _solid, cx + 58, y - 4.5, a2, TextAlignment.Left);
                    }
                }
                else
                {
                    double sp = EaseOut(Phase(t, 1.3, 0.4));
                    if (sp > 0) dc.DrawLine(_strikePen, new Point(70, 12), new Point(70 + 100 * sp, 12 + 80 * sp));
                }
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _platePen, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
