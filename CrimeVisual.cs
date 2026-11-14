using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A crime or its cost (the journal's CommitCrime, PayFines, PayBounties): a wanted poster drops in with a silhouette, the crime and the amount, and a stamp thumps onto it.
    // FINED in amber (a fine), WANTED in red (a bounty on you), PAID in mint (fines or bounties cleared); the plate gives the crime, the faction and the amount.
    // Detail = "FINE|crime|faction|amount", "WANTED|crime|faction|amount" or "PAID|crime or label|faction|amount|plate title".
    public sealed class CrimeVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private enum Kind { Fine, Wanted, Paid }

        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Poster, Stamp, PlateEdge;
            public Palette(Color c)
            {
                Solid = Frozen(c);
                Poster = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1.5); Stamp = FrozenPen(c, 1.5); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
            }
        }
        private static readonly Palette FinePal = new(Color.FromRgb(255, 184, 77)), WantedPal = new(Color.FromRgb(255, 107, 94)), PaidPal = new(Color.FromRgb(95, 227, 176));
        private static readonly Brush PaperFill = Frozen(Color.FromArgb(20, 232, 220, 190)), SilFill = Frozen(Color.FromArgb(64, 165, 175, 195)), SilHead = Frozen(Color.FromArgb(128, 165, 175, 195)),
            NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));

        private Kind _kind;
        private string _crime = "", _faction = "", _title = "";
        private long _amount;
        private readonly ScaleTransform _stampScale = new();
        private readonly TranslateTransform _stampAt = new(120, 44);
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string k = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _kind = k == "WANTED" ? Kind.Wanted : k == "PAID" ? Kind.Paid : Kind.Fine;
            _crime = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "";
            _faction = p.Length > 2 ? p[2].Trim().ToUpperInvariant() : "";
            _amount = p.Length > 3 && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? Math.Max(0, a) : 0;
            _title = p.Length > 4 ? p[4].Trim().ToUpperInvariant() : "";
            if (_crime.Length == 0) _crime = _kind == Kind.Paid ? "ALL CLEARED" : "CRIME";
            if (_title.Length == 0) _title = _kind switch { Kind.Wanted => "BOUNTY ON YOUR HEAD", Kind.Paid => "FINES PAID", _ => "CRIME COMMITTED" };
        }

        private static string Credits(double n) => Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) + " CR";

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            var pal = _kind switch { Kind.Wanted => WantedPal, Kind.Paid => PaidPal, _ => FinePal };
            string head = _kind switch { Kind.Wanted => "WANTED", Kind.Paid => "CLEARED", _ => "CITED" }, word = _kind switch { Kind.Wanted => "WANTED", Kind.Paid => "PAID", _ => "FINED" };

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            double drop = EaseOut(Phase(t, 0.1, 0.5));
            dc.PushOpacity(drop);
            dc.PushTransform(new TranslateTransform(0, (1 - drop) * -40));
            dc.DrawRectangle(PaperFill, pal.Poster, new Rect(75, 6, 90, 90));
            DrawSpaced(dc, head, MonoFace, 9, pal.Solid, 120, 10, 0.1, 1);
            dc.DrawRectangle(SilFill, null, new Rect(92, 24, 56, 36));
            dc.DrawEllipse(SilHead, null, new Point(120, 38), 7, 7);
            dc.DrawRectangle(SilHead, null, new Rect(110, 46, 20, 14));
            DrawSpaced(dc, _crime, MonoFace, Fit(_crime, 8, 84), NameBrush, 120, 64, 0.06, 1);
            if (_amount > 0) DrawSpaced(dc, Credits(_amount), MonoFace, 8, pal.Solid, 120, 76, 0.06, Phase(t, 0.9, 0.3));
            dc.Pop(); dc.Pop();

            double st = Phase(t, 0.9, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.2;
                _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(PlateBrush, pal.Stamp, new Rect(-32, -10, 64, 20));
                DrawSpaced(dc, word, MonoFace, 10, pal.Solid, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            string l2 = _faction.Length > 0 ? _crime + "  -  " + _faction : _crime;
            string l3 = _amount > 0 ? (_kind switch { Kind.Wanted => "BOUNTY  ", Kind.Paid => "PAID  ", _ => "FINE  " }) + Credits(_amount) : "";
            double plateAt = 1.7, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, pal.PlateEdge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 180), pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
