using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Taxi and dropship bookings (BookTaxi, BookDropship, CancelTaxi, CancelDropship, DropshipDeploy): a boarding pass slides in with a barcode stub.
    //   TAXI cyan, BOOKED stamp    DROP amber, BOOKED stamp    CANCEL red, the pass tears in half and a REFUNDED stamp lands    DEPLOY amber, DEPLOYED stamp
    // Detail = "KIND|ticket destination|chip name|chip line|fare line|TAXI or DROPSHIP (for CANCEL and DEPLOY)".
    public sealed class TaxiVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen StampPen, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); StampPen = FrozenPen(c, 1.8); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette TaxiPal = new(Color.FromRgb(85, 242, 255)), DropPal = new(Color.FromRgb(255, 180, 80)), CancelPal = new(Color.FromRgb(255, 107, 107)), GreenPal = new(Color.FromRgb(95, 227, 176));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush StampBack = Frozen(Color.FromArgb(235, 8, 13, 13));
        private static readonly Pen Perf = MakePerf();

        private static Pen MakePerf()
        {
            var p = new Pen(Frozen(Color.FromRgb(0x77, 0x77, 0x77)), 0.8) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            p.Freeze();
            return p;
        }

        private Palette _pal = TaxiPal;
        private string _kind = "TAXI", _to = "", _name = "", _sub = "", _fare = "", _tag = "TAXI", _title = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _kind = P(0);
            _to = P(1); _name = P(2); _sub = P(3); _fare = P(4);
            switch (_kind)
            {
                case "DROP": _pal = DropPal; _tag = "DROPSHIP"; _title = "DROPSHIP BOOKED"; break;
                case "CANCEL": _pal = CancelPal; _tag = P(5).Length > 0 ? P(5) : "TAXI"; _title = "BOOKING CANCELLED"; break;
                case "DEPLOY": _pal = DropPal; _tag = "DROPSHIP"; _title = "DROPSHIP DEPLOYED"; break;
                default: _kind = "TAXI"; _pal = TaxiPal; _tag = "TAXI"; _title = "TAXI BOOKED"; break;
            }
            if (_to.Length == 0) _to = _name;
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        private void DrawTicket(DrawingContext dc, double W, double H)
        {
            dc.DrawRectangle(Paper, null, new Rect(-W / 2, -H / 2, W, H));
            dc.DrawRectangle(_pal.Solid, null, new Rect(-W / 2, -H / 2, W, 10));
            DrawText(dc, _tag + " TICKET", MonoFace, 7, BandInk, -W / 2 + 6, -H / 2 + 1.5, 1, TextAlignment.Left);
            string to = "TO " + _to;
            DrawText(dc, to, MonoFace, Fit(to, 9, W - 62), Ink, -W / 2 + 6, -H / 2 + 16, 1, TextAlignment.Left);
            DrawText(dc, _fare, MonoFace, Fit(_fare, 7, W - 62), Ink, -W / 2 + 6, -H / 2 + 31, 1, TextAlignment.Left);
            dc.DrawLine(Perf, new Point(W / 2 - 38, -H / 2 + 10), new Point(W / 2 - 38, H / 2));
            for (int i = 0; i < 9; i++)
                dc.DrawRectangle(Ink, null, new Rect(W / 2 - 33 + i * 3, -H / 2 + 16, i % 3 == 0 ? 1.9 : 1.1, 21));
        }

        private void Stamp(DrawingContext dc, string text, Palette pal, double x, double y, double s, double size)
        {
            double sc = 1 + (1 - EaseOut(s)) * 0.8, wBox = text.Length * (size * 0.62) + 10;
            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new RotateTransform(-11.5)); dc.PushTransform(new ScaleTransform(sc, sc));
            dc.PushOpacity(EaseOut(s));
            // A dark backing so the coloured lettering reads against the cream pass.
            dc.DrawRectangle(StampBack, pal.StampPen, new Rect(-wBox / 2, -size * 0.8, wBox, size * 1.6));
            DrawSpaced(dc, text, MonoFace, size, pal.Solid, 0, -size * 0.6, 0.05, 1);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
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

            const double tw = 150, th = 52, cx = 120, cy = 46;
            double slide = EaseOut(Phase(t, 0.1, 0.6)), tear = _kind == "CANCEL" ? EaseOut(Phase(t, 1.2, 0.7)) : 0;
            if (slide > 0.01)
            {
                // Two halves of the same pass: together until a cancel pulls them apart.
                for (int half = 0; half < 2; half++)
                {
                    double sign = half == 0 ? -1 : 1;
                    dc.PushTransform(new TranslateTransform(cx + sign * tear * 10.5 + (1 - slide) * 135, cy + (half == 0 ? tear * 9 : tear * 13.5)));
                    dc.PushTransform(new RotateTransform(sign * tear * (half == 0 ? 14 : 17)));
                    dc.PushClip(new RectangleGeometry(half == 0 ? new Rect(-tw / 2 - 2, -th / 2 - 2, tw / 2 + 2, th + 4) : new Rect(0, -th / 2 - 2, tw / 2 + 2, th + 4)));
                    DrawTicket(dc, tw, th);
                    dc.Pop(); dc.Pop(); dc.Pop();
                }
            }

            double s = Phase(t, 1.5, 0.25);
            if (s > 0)
            {
                // All three stamps land in the same place, centred on the pass.
                if (_kind == "CANCEL") Stamp(dc, "REFUNDED", CancelPal, cx, cy + 3, s, 12);
                else if (_kind == "DEPLOY") Stamp(dc, "DEPLOYED", GreenPal, cx, cy + 3, s, 12);
                else Stamp(dc, "BOOKED", GreenPal, cx, cy + 3, s, 12);
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
