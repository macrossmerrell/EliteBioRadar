using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The start and the end of a play session (LoadGame, Shutdown): a pilot's licence card scales in with a photo block, the commander's name and four rank bars that fill; a stamp lands on it.
    //   START cyan (Solo), green (Open) or amber (private group), stamped with the mode    END violet, stamped SESSION CLOSED
    // Detail = "KIND|commander|chip line|mode|credits|combat|trade|explore|exobiologist|group name" (START or END; the ranks are the journal's numbers, -1 when unknown).
    public sealed class SessionVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen StampPen, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); StampPen = FrozenPen(c, 1.6); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette CyanPal = new(Color.FromRgb(85, 242, 255)), GreenPal = new(Color.FromRgb(95, 227, 176)), AmberPal = new(Color.FromRgb(255, 180, 80)), VioletPal = new(Color.FromRgb(199, 155, 255));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), PhotoBack = Frozen(Color.FromRgb(0xb9, 0xb4, 0xa6)), PhotoInk = Frozen(Color.FromRgb(0x6a, 0x68, 0x60)), BarBack = Frozen(Color.FromRgb(0xc4, 0xbf, 0xb0));
        private static readonly Brush StampBack = Frozen(Color.FromArgb(235, 8, 13, 13)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Brush BarFill = Frozen(Color.FromRgb(0x2f, 0x8f, 0x88)), BarFillEnd = Frozen(Color.FromRgb(0x8a, 0x6b, 0xc4));
        private static readonly (string Label, string Category)[] Cats = { ("COMBAT", "Combat"), ("TRADE", "Trade"), ("EXPLORE", "Explore"), ("EXOBIO", "Exobiologist") };

        private Palette _pal = CyanPal;
        private bool _end;
        private string _name = "", _sub = "", _title = "", _stamp = "";
        private readonly int[] _ranks = { -1, -1, -1, -1 };

        // Everything is drawn on a stretched clock so the whole card plays at half speed (twice as long to read).
        private const double Slow = 2.0;
        protected override double Duration => (FadeOutAt + 0.6) * Slow;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _end = P(0).ToUpperInvariant() == "END";
            _name = P(1).ToUpperInvariant(); _sub = P(2).ToUpperInvariant();
            string mode = P(3).ToUpperInvariant();
            for (int i = 0; i < 4; i++) if (!int.TryParse(P(5 + i), out _ranks[i])) _ranks[i] = -1;
            if (_end) { _pal = VioletPal; _title = "FLY SAFE, COMMANDER"; _stamp = "SESSION CLOSED"; }
            else
            {
                _title = "WELCOME BACK";
                switch (mode)
                {
                    case "OPEN": _pal = GreenPal; _stamp = "OPEN"; break;
                    case "GROUP": _pal = AmberPal; _stamp = "PRIVATE GROUP"; break;
                    default: _pal = CyanPal; _stamp = "SOLO"; break;
                }
            }
        }


        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt * Slow);
            t /= Slow;
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            const double cw = 166, ch = 88;
            double s = EaseOut(Phase(t, 0.1, 0.55));
            if (s > 0.01)
            {
                double sc = 0.4 + 0.6 * s;
                dc.PushOpacity(s);
                dc.PushTransform(new TranslateTransform(120, 54));
                dc.PushTransform(new ScaleTransform(sc, sc));
                dc.DrawRectangle(Paper, null, new Rect(-cw / 2, -ch / 2, cw, ch));
                dc.DrawRectangle(_pal.Solid, null, new Rect(-cw / 2, -ch / 2, cw, 10));
                DrawText(dc, "PILOTS FEDERATION LICENCE", MonoFace, 6, BandInk, -cw / 2 + 5, -ch / 2 + 2, 1, TextAlignment.Left);
                var photo = new Rect(-cw / 2 + 6, -ch / 2 + 15, 27, 33);
                dc.DrawRectangle(PhotoBack, null, photo);
                dc.PushClip(ClipOf(photo));
                dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 13.5, photo.Y + 11), 6, 7);
                dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 13.5, photo.Y + 32), 12, 11);
                dc.Pop();
                double tx = -cw / 2 + 39, tw = cw - 45;
                DrawText(dc, _name, MonoFace, Fit(_name, 8.5, tw), Ink, tx, -ch / 2 + 14, 1, TextAlignment.Left);
                for (int i = 0; i < 4; i++)
                {
                    double y = -ch / 2 + 27 + i * 11, p = EaseOut(Phase(t, 0.6 + i * 0.15, 0.6));
                    int r = _ranks[i];
                    DrawText(dc, Cats[i].Label, MonoFace, 6, Ink, tx, y, 1, TextAlignment.Left);
                    dc.DrawRectangle(BarBack, null, new Rect(tx + 32, y + 1.5, 42, 4.5));
                    if (r >= 0) dc.DrawRectangle(_end ? BarFillEnd : BarFill, null, new Rect(tx + 32, y + 1.5, 42 * Math.Min(1.0, r / 8.0) * p, 4.5));
                    string rn = r >= 0 ? RankUpVisual.RankName(Cats[i].Category, r).ToUpperInvariant() : "";
                    if (rn.Length > 0) DrawText(dc, rn, MonoFace, Fit(rn, 5.5, tw - 78), Ink, tx + 78, y + 0.5, p, TextAlignment.Left);
                }
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            double sp = Phase(t, 1.6, 0.25);
            if (sp > 0)
            {
                double sz = 9, sca = 1 + (1 - EaseOut(sp)) * 0.8, wBox = _stamp.Length * (sz * 0.62) + 10;
                dc.PushTransform(new TranslateTransform(120, 89)); dc.PushTransform(new RotateTransform(-6)); dc.PushTransform(new ScaleTransform(sca, sca));
                dc.PushOpacity(EaseOut(sp));
                dc.DrawRectangle(StampBack, _pal.StampPen, new Rect(-wBox / 2, -sz * 0.8, wBox, sz * 1.6));
                DrawSpaced(dc, _stamp, MonoFace, sz, _pal.Solid, 0, -sz * 0.6, 0.05, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
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
