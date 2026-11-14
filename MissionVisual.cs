using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A mission's moment (the journal's MissionAccepted / MissionCompleted / MissionFailed / MissionAbandoned / MissionRedirected, several in a row folded into one): a contract card slides in with the mission
    // and its faction and a stamp thumps onto it. ACCEPTED in cyan; COMPLETED in mint with the reward counting up in gold; FAILED in red with the fine counting up; ABANDONED in amber on a dimmed card with the penalty;
    // REDIRECTED in violet with an arrow, the plate naming the new destination.
    // Detail = "ACCEPTED|count|name|faction|reward", "COMPLETED|...", "FAILED|count|name||fine", "ABANDONED|count|name||fine" or "REDIRECTED|count|name|new destination|0" (reward / fine 0 = unknown).
    public sealed class MissionVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.6, CardX = 65, CardY = 12, CardW = 110, CardH = 66;
        private enum Kind { Accepted, Completed, Failed, Abandoned, Redirected }

        // Everything that takes a kind's colour, built once per colour.
        private sealed class Palette
        {
            public readonly Brush Solid, Fill;
            public readonly Brush[] Lines;
            public readonly Pen Edge, Stamp, PlateEdge;
            public Palette(Color c, Color? card = null, Color? fill = null)
            {
                Solid = Frozen(c);
                var cc = card ?? c;
                Edge = FrozenPen(Color.FromArgb(204, cc.R, cc.G, cc.B), 1.5);
                Lines = AlphaBrushes(cc);
                Stamp = FrozenPen(c, 1.5);
                PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
                var f = fill ?? c; Fill = Frozen(Color.FromArgb(18, f.R, f.G, f.B));
            }
        }

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Gold = Color.FromRgb(0xff, 0xd3, 0x6b), Red = Color.FromRgb(0xff, 0x6b, 0x5e), Amber = Color.FromRgb(0xff, 0xb8, 0x4d), Violet = Color.FromRgb(175, 145, 255), Grey = Color.FromRgb(150, 150, 150);
        private static readonly Palette AcceptedPal = new(Cyan, null, Mint), CompletedPal = new(Mint), FailedPal = new(Red), AbandonedPal = new(Amber, Grey), RedirectedPal = new(Violet);
        private static readonly Brush GoldBrush = Frozen(Gold), RedBrush = Frozen(Red), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20));
        private static readonly Pen ArrowPen = FrozenPen(Violet, 2);
        private static readonly Geometry ArrowHead = Poly(new[] { (80.0, -5.0), (88.0, 0.0), (80.0, 5.0) });

        private Kind _kind;
        private int _count = 1;
        private string _name = "", _faction = "";
        private long _reward;
        private readonly ScaleTransform _outer = new(), _fit = new(), _stampScale = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _stampAt = new();
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string k = p.Length > 0 ? p[0] : "";
            _kind = k.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase) ? Kind.Completed : k.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? Kind.Failed
                  : k.Contains("ABANDON", StringComparison.OrdinalIgnoreCase) ? Kind.Abandoned : k.Contains("REDIRECT", StringComparison.OrdinalIgnoreCase) ? Kind.Redirected : Kind.Accepted;
            _count = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Max(1, c) : 1;
            _name = p.Length > 2 ? p[2].Trim() : "";
            _faction = p.Length > 3 ? p[3].Trim() : "";
            _reward = p.Length > 4 && long.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Max(0, r) : 0;
            if (_name.Length == 0) _name = "Mission";
        }

        private static string Credits(double n) => Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) + " CR";

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            var pal = _kind switch { Kind.Completed => CompletedPal, Kind.Failed => FailedPal, Kind.Abandoned => AbandonedPal, Kind.Redirected => RedirectedPal, _ => AcceptedPal };
            string word = _kind switch { Kind.Completed => "COMPLETED", Kind.Failed => "FAILED", Kind.Abandoned => "ABANDONED", Kind.Redirected => "REDIRECTED", _ => "ACCEPTED" };

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double slide = EaseOut(Phase(t, 0.1, 0.6)), x = CardX + (1 - slide) * 160, cx = x + CardW / 2;
            bool dim = _kind == Kind.Abandoned;
            if (dim) dc.PushOpacity(1 - 0.4 * Phase(t, 1.0, 0.5));   // an abandoned card goes dull
            dc.DrawRectangle(pal.Fill, pal.Edge, new Rect(x, CardY, CardW, CardH));
            string title = _count > 1 ? _count.ToString(CultureInfo.InvariantCulture) + " MISSIONS" : _name;
            string sub = _count > 1 ? _name + "  +" + (_count - 1).ToString(CultureInfo.InvariantCulture) + " more" : _kind == Kind.Redirected ? "" : _faction;
            DrawSpaced(dc, title, MonoFace, Fit(title, 8, CardW - 12), NameBrush, cx, CardY + 7, 0.04, 1);
            DrawSpaced(dc, sub, MonoFace, Fit(sub, 6, CardW - 12), MutedBrush, cx, CardY + 19, 0.04, 1);
            for (int i = 0; i < 3; i++) dc.DrawRectangle(pal.Lines[2], null, new Rect(x + 7, CardY + 31 + i * 6, i == 2 ? 50 : 90, 2));
            if (dim) dc.Pop();

            // What it cost or paid, under the stamp.
            if (_kind == Kind.Completed && _reward > 0)
            {
                double n = _reward * EaseOut(Phase(t, 1.1, 0.9));
                DrawSpaced(dc, "+" + Credits(n), MonoFace, 9, GoldBrush, cx, CardY + CardH - 14, 0.04, Phase(t, 1.0, 0.2));
            }
            else if (_kind == Kind.Failed && _reward > 0)
            {
                double n = _reward * EaseOut(Phase(t, 1.1, 0.9));
                DrawSpaced(dc, "-" + Credits(n), MonoFace, 9, RedBrush, cx, CardY + CardH - 14, 0.04, Phase(t, 1.0, 0.2));
            }
            else if (_kind == Kind.Redirected)
            {
                double ar = Phase(t, 1.2, 0.5);
                if (ar > 0)
                {
                    dc.PushOpacity(ar);
                    dc.PushTransform(new TranslateTransform(x, CardY + CardH - 9));
                    dc.DrawLine(ArrowPen, new Point(30, 0), new Point(80, 0));
                    dc.DrawGeometry(RedirectedPal.Solid, null, ArrowHead);
                    dc.Pop(); dc.Pop();
                }
            }

            double stampAt = _kind == Kind.Completed ? 0.7 : 0.9, st = Phase(t, stampAt, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.2, size = word.Length > 9 ? 9 : 10, bw = Math.Max(66, word.Length * (size * 0.68) + 10);
                _stampAt.X = cx; _stampAt.Y = 50;   // centred on the card, so the wider words (ABANDONED, REDIRECTED) stay on it _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, pal.Stamp, new Rect(-bw / 2, -10, bw, 20));
                DrawSpaced(dc, word, MonoFace, size, pal.Solid, 0, size == 10 ? -7 : -6.5, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            // The plate: the word, the mission, and the money or the destination.
            string head = "MISSION" + (_count > 1 ? "S " : " ") + word;
            string line3 = _kind switch
            {
                Kind.Completed => _reward > 0 ? "REWARD  +" + Credits(_reward) : _faction,
                Kind.Failed => _reward > 0 ? "FINE  -" + Credits(_reward) : "",
                Kind.Abandoned => _reward > 0 ? "PENALTY  -" + Credits(_reward) : "",
                Kind.Redirected => _faction.Length > 0 ? "RETURN TO  " + _faction : "",
                _ => _reward > 0 ? (_faction.Length > 0 ? _faction + "  -  " : "") + "reward " + Credits(_reward) : _faction
            };
            double plateAt = 1.8, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, pal.PlateEdge, new Rect(26, 103, 188, line3.Length > 0 ? 45 : 34), 6, 6);   // as tall as its lines
                dc.Pop();
                DrawSpaced(dc, head, MonoFace, Fit(head, 12, 180), pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, title, MonoFace, Fit(title, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (line3.Length > 0) DrawSpaced(dc, line3, MonoFace, Fit(line3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
