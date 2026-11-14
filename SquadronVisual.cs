using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Squadron events (invite, application, joined, founded, promotion, demotion, left, removed, disbanded, trophy): an ID card swings in on a lanyard.
    //   INVITE / APPLY cyan, PENDING    JOIN / CREATE green, MEMBER    PROMOTE gold, rank pips fill    DEMOTE amber    LEAVE / KICK / DISBAND red, crossed out then dropped    TROPHY gold star
    // Detail = "KIND|squadron name|chip line|old rank|new rank" (ranks are the journal's numbers, shown as given).
    public sealed class SquadronVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.2;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Edge, PlateEdge;
            public Palette(Color c) { Solid = Frozen(c); Edge = FrozenPen(c, 1.4); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette CyanPal = new(Color.FromRgb(85, 242, 255)), GreenPal = new(Color.FromRgb(95, 227, 176)), GoldPal = new(Color.FromRgb(255, 216, 100)), AmberPal = new(Color.FromRgb(255, 180, 80)), RedPal = new(Color.FromRgb(255, 107, 107));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), PhotoBack = Frozen(Color.FromRgb(0xb9, 0xb4, 0xa6)), PhotoInk = Frozen(Color.FromRgb(0x6a, 0x68, 0x60)), PipOff = Frozen(Color.FromRgb(0xc4, 0xbf, 0xb0));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), StampBack = Frozen(Color.FromArgb(235, 8, 13, 13));
        private static readonly Pen Lanyard = FrozenPen(Color.FromRgb(0x4a, 0x7a, 0x8a), 2.2), Clip = FrozenPen(Color.FromRgb(0xaa, 0xb4, 0xb8), 2.6);

        private Palette _pal = CyanPal;
        private string _kind = "JOIN", _squad = "", _chip = "", _title = "", _status = "", _name = "";
        private int _oldRank, _newRank;

        protected override double Duration => FadeOutAt + 0.6;

        private static string Fit1(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _kind = P(0);
            _squad = P(1); _chip = P(2);
            int.TryParse(P(3), out _oldRank); int.TryParse(P(4), out _newRank);
            _name = _squad;
            switch (_kind)
            {
                case "INVITE": _pal = CyanPal; _title = "SQUADRON INVITE"; _status = "PENDING"; break;
                case "APPLY": _pal = CyanPal; _title = "SQUADRON APPLICATION"; _status = "PENDING"; break;
                case "CREATE": _pal = GreenPal; _title = "SQUADRON FOUNDED"; _status = "MEMBER"; break;
                case "PROMOTE": _pal = GoldPal; _title = "SQUADRON PROMOTION"; _status = "PROMOTED"; break;
                case "DEMOTE": _pal = AmberPal; _title = "SQUADRON DEMOTION"; _status = "DEMOTED"; break;
                case "LEAVE": _pal = RedPal; _title = "LEFT SQUADRON"; _status = "REVOKED"; break;
                case "KICK": _pal = RedPal; _title = "REMOVED FROM SQUADRON"; _status = "REVOKED"; break;
                case "DISBAND": _pal = RedPal; _title = "SQUADRON DISBANDED"; _status = "REVOKED"; break;
                case "TROPHY": _pal = GoldPal; _title = "SQUADRON TROPHY"; _status = "TROPHY"; break;
                default: _kind = "JOIN"; _pal = GreenPal; _title = "SQUADRON JOINED"; _status = "MEMBER"; break;
            }
        }

        private Geometry? _star;   // the trophy star, built once for its position
        private Point _starAt;
        private bool Ends => _kind == "LEAVE" || _kind == "KICK" || _kind == "DISBAND";

        private void DrawCard(DrawingContext dc, double W, double H, double t)
        {
            dc.DrawRectangle(Paper, null, new Rect(-W / 2, -H / 2, W, H));
            dc.DrawRectangle(_pal.Solid, null, new Rect(-W / 2, -H / 2, W, 11));
            DrawText(dc, "SQUADRON ID", MonoFace, 7, BandInk, -W / 2 + 6, -H / 2 + 2, 1, TextAlignment.Left);
            // Photo block with a head-and-shoulders silhouette.
            var photo = new Rect(-W / 2 + 7, -H / 2 + 17, 34, 40);
            dc.DrawRectangle(PhotoBack, null, photo);
            dc.PushClip(ClipOf(photo));
            dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 17, photo.Y + 15), 7, 8);
            dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 17, photo.Y + 42), 15, 15);
            dc.Pop();
            double tx = -W / 2 + 47, tw = W - 54;
            DrawText(dc, _squad, MonoFace, Fit(_squad, 9, tw), Ink, tx, -H / 2 + 18, 1, TextAlignment.Left);
            DrawText(dc, _status, MonoFace, Fit(_status, 8, tw), _pal.Solid == GoldPal.Solid || _pal.Solid == CyanPal.Solid ? Ink : Ink, tx, -H / 2 + 32, 1, TextAlignment.Left);
            if (_kind == "PROMOTE" || _kind == "DEMOTE")
            {
                int fill = Math.Clamp(_newRank, 0, 5), from = Math.Clamp(_oldRank, 0, 5);
                double grow = Phase(t, 1.2, 0.6);
                for (int i = 0; i < 5; i++)
                {
                    bool on = i < from || (i < fill && grow * 5 > i - from + 0.01) || (_kind == "DEMOTE" && i < fill);
                    if (_kind == "DEMOTE") on = i < from - (int)Math.Round(grow * Math.Max(0, from - fill));
                    dc.DrawEllipse(on ? _pal.Solid : PipOff, null, new Point(tx + 4 + i * 11, -H / 2 + 49), 3.6, 3.6);
                }
            }
            else if (_kind == "TROPHY")
            {
                double cx = tx + 8, cy = -H / 2 + 49;
                if (_star == null || _starAt != new Point(cx, cy))
                {
                    var star = new StreamGeometry();
                    using (var g = star.Open())
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            double ang = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 7 : 3;
                            var pt = new Point(cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r);
                            if (i == 0) g.BeginFigure(pt, true, true); else g.LineTo(pt, true, false);
                        }
                    }
                    star.Freeze();
                    _star = star; _starAt = new Point(cx, cy);
                }
                dc.PushOpacity(Phase(t, 1.2, 0.3));
                dc.DrawGeometry(_pal.Solid, null, _star);
                dc.Pop();
            }
            dc.DrawRectangle(null, _pal.Edge, new Rect(-W / 2, -H / 2, W, H));
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

            const double cw = 130, ch = 66, hang = 24, pivotX = 120, pivotY = 6;
            double drop = EaseOut(Phase(t, 0.1, 0.55)), sw = Math.Max(0, t - 0.45);
            double angle = sw > 0 ? 15 * Math.Exp(-sw * 1.1) * Math.Sin(sw * 5.2) : 0;
            double rad = angle * Math.PI / 180, dy = -(1 - drop) * 110;
            bool cut = Ends && t > 2.1;
            double fall = Ends ? Phase(t, 2.1, 0.55) : 0;

            if (drop > 0.01)
            {
                // Card hangs from the clip; once removed it is cut loose and drops away.
                double cpx = pivotX - Math.Sin(rad) * hang, cpy = pivotY + dy + Math.Cos(rad) * hang;
                if (!cut)
                {
                    dc.DrawLine(Lanyard, new Point(pivotX - 26, -22), new Point(cpx, cpy));
                    dc.DrawLine(Lanyard, new Point(pivotX + 26, -22), new Point(cpx, cpy));
                    dc.DrawLine(Clip, new Point(cpx - 5, cpy), new Point(cpx + 5, cpy));
                }
                else
                {
                    dc.DrawLine(Lanyard, new Point(pivotX - 26, -22), new Point(pivotX - 4, 2 + 4 * fall));
                    dc.DrawLine(Lanyard, new Point(pivotX + 26, -22), new Point(pivotX + 4, 2 + 4 * fall));
                }
                double fy = fall * fall * 110, fr = fall * 28;
                if (fall > 0) dc.PushOpacity(1 - Clamp01((fall - 0.55) / 0.45) * 0.9);
                dc.PushTransform(new TranslateTransform(pivotX, pivotY + dy + fy));
                dc.PushTransform(new RotateTransform(angle + fr));
                dc.PushTransform(new TranslateTransform(0, hang + ch / 2));
                DrawCard(dc, cw, ch, t);
                if (Ends)
                {
                    // Red cross drawn corner to corner.
                    double x1 = EaseOut(Phase(t, 1.4, 0.3)), x2 = EaseOut(Phase(t, 1.65, 0.3));
                    var pen = FrozenPen(Color.FromRgb(255, 107, 107), 3);
                    if (x1 > 0) dc.DrawLine(pen, new Point(-cw / 2 + 4, -ch / 2 + 4), new Point(-cw / 2 + 4 + (cw - 8) * x1, -ch / 2 + 4 + (ch - 8) * x1));
                    if (x2 > 0) dc.DrawLine(pen, new Point(cw / 2 - 4, -ch / 2 + 4), new Point(cw / 2 - 4 - (cw - 8) * x2, -ch / 2 + 4 + (ch - 8) * x2));
                }
                dc.Pop(); dc.Pop(); dc.Pop();
                if (fall > 0) dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                string sub = _kind == "PROMOTE" || _kind == "DEMOTE" ? (_chip.Length > 0 ? _chip : $"RANK {_oldRank} TO {_newRank}") : _chip;
                bool has3 = sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, sub, MonoFace, Fit(sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
