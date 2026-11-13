using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Shipyard moments (the journal's ShipyardBuy, ShipyardNew, ShipyardSwap and ShipyardTransfer): a ship registration card flips in with a small ship and its name, then a tilted stamp lands.
    //   BUY  gold,   stamp PAID IN FULL, plate "-price CR"        NEW   green,  stamp DELIVERED, plate "READY IN YOUR HANGAR"
    //   SWAP cyan,   stamp ACTIVE,       plate "OLD SHIP STORED"   XFER  violet, stamp IN TRANSIT, plate "distance - price - time"
    // Detail = "BUY|ship|price", "NEW|ship", "SWAP|ship|old ship" or "XFER|ship|system|distanceLY|price|seconds".
    public sealed class ShipyardVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Edge, StampPen, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); Edge = FrozenPen(c, 1.2); StampPen = FrozenPen(c, 1.8); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette BuyPal = new(Color.FromRgb(255, 216, 144)), NewPal = new(Color.FromRgb(95, 227, 176)), SwapPal = new(Color.FromRgb(85, 242, 255)), XferPal = new(Color.FromRgb(200, 122, 255));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardBrush = Frozen(Color.FromRgb(0x1b, 0x22, 0x30)),
            FaintBrush = Frozen(Color.FromArgb(77, 255, 255, 255)), HullBrush = Frozen(Color.FromRgb(0xa8, 0xb8, 0xc8));
        private static readonly Pen HullPen = FrozenPen(Color.FromRgb(0x6b, 0x7a, 0x88), 1);
        private static readonly Geometry Hull = Poly(new[] { (0.0, -26.0), (9.0, -6.0), (30.0, 14.0), (30.0, 20.0), (10.0, 14.0), (0.0, 22.0), (-10.0, 14.0), (-30.0, 20.0), (-30.0, 14.0), (-9.0, -6.0) });

        private Palette _pal = BuyPal;
        private string _title = "", _ship = "", _line3 = "", _stamp = "";
        private string _shipLine = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _ship = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "SHIP";
            string Part(int i) => p.Length > i ? p[i].Trim() : "";
            _shipLine = _ship;
            switch (kind)
            {
                case "NEW": _pal = NewPal; _title = "SHIP DELIVERED"; _stamp = "DELIVERED"; _line3 = "READY IN YOUR HANGAR"; break;
                case "SWAP": _pal = SwapPal; _title = "SHIP SWAPPED"; _stamp = "ACTIVE"; _line3 = Part(2).Length > 0 ? Part(2).ToUpperInvariant() + " STORED" : "NOW IN COMMAND"; break;
                case "XFER":
                {
                    _pal = XferPal; _title = "SHIP TRANSFER ORDERED"; _stamp = "IN TRANSIT";
                    string system = Part(2).ToUpperInvariant();
                    if (system.Length > 0) _shipLine = _ship + " FROM " + system;
                    var parts = new System.Collections.Generic.List<string>();
                    if (double.TryParse(Part(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var ly) && ly > 0) parts.Add(ly.ToString("0.0", CultureInfo.InvariantCulture) + " LY");
                    if (long.TryParse(Part(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var price) && price > 0) parts.Add(price.ToString("N0", CultureInfo.InvariantCulture) + " CR");
                    if (int.TryParse(Part(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs) && secs > 0)
                        parts.Add(secs >= 3600 ? (secs / 3600).ToString(CultureInfo.InvariantCulture) + "H " + ((secs % 3600 + 30) / 60).ToString(CultureInfo.InvariantCulture) + "M" : secs >= 60 ? ((secs + 30) / 60).ToString(CultureInfo.InvariantCulture) + "M" : secs.ToString(CultureInfo.InvariantCulture) + "S");
                    _line3 = string.Join(" - ", parts);
                    break;
                }
                default:
                    _pal = BuyPal; _title = "SHIP PURCHASED"; _stamp = "PAID IN FULL";
                    _line3 = long.TryParse(Part(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cost) && cost > 0 ? "-" + cost.ToString("N0", CultureInfo.InvariantCulture) + " CR" : "";
                    break;
            }
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

            // The card flips in.
            double p = EaseOut(Phase(t, 0.1, 0.6));
            if (p > 0.01)
            {
                dc.PushTransform(new TranslateTransform(SceneW / 2, 47)); dc.PushTransform(new RotateTransform(-0.4 * (1 - p) * 180 / Math.PI)); dc.PushTransform(new ScaleTransform(p, p));
                dc.PushOpacity(p);
                dc.DrawRoundedRectangle(CardBrush, _pal.Edge, new Rect(-69, -34.5, 138, 69), 4, 4);
                dc.PushTransform(new TranslateTransform(-39, 0)); dc.PushTransform(new ScaleTransform(0.65, 0.65));
                dc.DrawGeometry(HullBrush, HullPen, Hull);
                dc.Pop(); dc.Pop();
                DrawText(dc, "SHIP REGISTRATION", MonoFace, 8, _pal.Solid, -10, -27, 1, TextAlignment.Left);
                dc.DrawRectangle(FaintBrush, null, new Rect(-10, -14, 67, 2));
                dc.DrawRectangle(FaintBrush, null, new Rect(-10, -8, 52, 2));
                dc.DrawRectangle(FaintBrush, null, new Rect(-10, -2, 60, 2));
                DrawText(dc, _ship, MonoFace, Fit(_ship, 8, 62), NameBrush, -10, 12, 1, TextAlignment.Left);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            // The stamp.
            double s = Phase(t, 1.6, 0.25);
            if (s > 0)
            {
                double sc = 1 + (1 - EaseOut(s)) * 0.8, wBox = _stamp.Length * 7.4 + 12;
                dc.PushTransform(new TranslateTransform(142, 66)); dc.PushTransform(new RotateTransform(-11.5)); dc.PushTransform(new ScaleTransform(sc, sc));
                dc.PushOpacity(EaseOut(s));
                dc.DrawRectangle(null, _pal.StampPen, new Rect(-wBox / 2, -9, wBox, 18));
                DrawSpaced(dc, _stamp, MonoFace, 10, _pal.Solid, 0, -6.5, 0.05, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.4, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _line3.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 11, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _shipLine, MonoFace, Fit(_shipLine, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _line3, MonoFace, Fit(_line3, 8, 190), _pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
