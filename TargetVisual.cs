using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A finished ship scan (ShipTargeted at scan stage 3): a target dossier card scales in with the ship's silhouette, the pilot, rank, faction and shield / hull, and the legal status is stamped on it.
    //   CLEAN green    LAWLESS amber    WANTED red (the bounty is on the card and in the chip)
    // Detail = "LEGAL|ship|pilot|rank|faction|shield %|hull %|bounty".
    public sealed class TargetVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen StampPen, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); StampPen = FrozenPen(c, 1.6); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette GreenPal = new(Color.FromRgb(95, 227, 176)), AmberPal = new(Color.FromRgb(255, 180, 80)), RedPal = new(Color.FromRgb(255, 107, 107)), CyanPal = new(Color.FromRgb(85, 242, 255));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), PhotoBack = Frozen(Color.FromRgb(0xb9, 0xb4, 0xa6)), ShipInk = Frozen(Color.FromRgb(0x4a, 0x4a, 0x42));
        private static readonly Brush StampBack = Frozen(Color.FromArgb(235, 8, 13, 13)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Geometry ShipShape = Poly(new[] { (0.0, -20.0), (7, -4), (24, 10), (22, 14), (6, 8), (4, 18), (0, 14), (-4, 18), (-6, 8), (-22, 14), (-24, 10), (-7, -4) });

        private Palette _pal = CyanPal;
        private string _legal = "", _ship = "", _pilot = "", _rank = "", _faction = "", _title = "", _sub = "";
        private double _shield = 100, _hull = 100;
        private long _bounty;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _legal = P(0).ToUpperInvariant(); _ship = P(1).ToUpperInvariant(); _pilot = P(2).ToUpperInvariant(); _rank = P(3).ToUpperInvariant(); _faction = P(4).ToUpperInvariant();
            if (!double.TryParse(P(5), NumberStyles.Float, CultureInfo.InvariantCulture, out _shield)) _shield = 100;
            if (!double.TryParse(P(6), NumberStyles.Float, CultureInfo.InvariantCulture, out _hull)) _hull = 100;
            long.TryParse(P(7), out _bounty);
            _pal = _legal switch { "CLEAN" => GreenPal, "LAWLESS" => AmberPal, "WANTED" => RedPal, _ => CyanPal };
            _title = _legal.Length > 0 ? _legal + " TARGET" : "TARGET SCANNED";
            if (_bounty > 0) _sub = $"{_ship}  BOUNTY {_bounty.ToString("N0", CultureInfo.InvariantCulture)} CR";
            else if (_shield < 100 || _hull < 100) _sub = $"{_ship}  SHIELDS {Math.Round(_shield)}%  HULL {Math.Round(_hull)}%";
            else _sub = (_ship + "  " + _rank).Trim();
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

            const double cw = 180, ch = 84;
            double s = EaseOut(Phase(t, 0.1, 0.5));
            if (s > 0.01)
            {
                double sc = 0.5 + 0.5 * s;
                dc.PushOpacity(s);
                dc.PushTransform(new TranslateTransform(120, 50));
                dc.PushTransform(new ScaleTransform(sc, sc));
                dc.DrawRectangle(Paper, null, new Rect(-cw / 2, -ch / 2, cw, ch));
                dc.DrawRectangle(_pal.Solid, null, new Rect(-cw / 2, -ch / 2, cw, 9));
                DrawText(dc, "TARGET DOSSIER", MonoFace, 6, BandInk, -cw / 2 + 5, -ch / 2 + 1.5, 1, TextAlignment.Left);
                var photo = new Rect(-cw / 2 + 6, -ch / 2 + 14, 48, 38);
                dc.DrawRectangle(PhotoBack, null, photo);
                dc.PushTransform(new TranslateTransform(photo.X + 24, photo.Y + 19)); dc.PushTransform(new ScaleTransform(0.7, 0.7));
                dc.DrawGeometry(ShipInk, null, ShipShape);
                dc.Pop(); dc.Pop();
                double tx = -cw / 2 + 60, tw = cw - 66;
                DrawText(dc, _pilot, MonoFace, Fit(_pilot, 8.5, tw), Ink, tx, -ch / 2 + 13, 1, TextAlignment.Left);
                DrawText(dc, _ship, MonoFace, Fit(_ship, 6.5, tw), Ink, tx, -ch / 2 + 24, 1, TextAlignment.Left);
                if (_rank.Length > 0) DrawText(dc, "RANK  " + _rank, MonoFace, Fit("RANK  " + _rank, 6.5, tw), Ink, tx, -ch / 2 + 33, 1, TextAlignment.Left);
                if (_faction.Length > 0) DrawText(dc, _faction, MonoFace, Fit(_faction, 6.5, tw), Ink, tx, -ch / 2 + 42, 1, TextAlignment.Left);
                string hs = $"SHIELD {Math.Round(_shield)}%   HULL {Math.Round(_hull)}%";
                DrawText(dc, hs, MonoFace, Fit(hs, 6.5, tw), Ink, tx, -ch / 2 + 51, 1, TextAlignment.Left);
                string bounty = _bounty > 0 ? "BOUNTY  " + _bounty.ToString("N0", CultureInfo.InvariantCulture) + " CR" : "NO BOUNTY";
                DrawText(dc, bounty, MonoFace, Fit(bounty, 6.5, cw - 12), Ink, -cw / 2 + 6, ch / 2 - 13, 1, TextAlignment.Left);
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            double sp = Phase(t, 1.4, 0.25);
            if (sp > 0 && _legal.Length > 0)
            {
                double sz = 10, sca = 1 + (1 - EaseOut(sp)) * 0.8, wBox = _legal.Length * (sz * 0.62) + 10;
                dc.PushTransform(new TranslateTransform(166, 80)); dc.PushTransform(new RotateTransform(-11.5)); dc.PushTransform(new ScaleTransform(sca, sca));
                dc.PushOpacity(EaseOut(sp));
                dc.DrawRectangle(StampBack, _pal.StampPen, new Rect(-wBox / 2, -sz * 0.8, wBox, sz * 1.6));
                DrawSpaced(dc, _legal, MonoFace, sz, _pal.Solid, 0, -sz * 0.6, 0.05, 1);
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
                DrawSpaced(dc, _pilot, MonoFace, Fit(_pilot, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
