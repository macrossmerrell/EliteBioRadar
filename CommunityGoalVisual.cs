using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Community goals (CommunityGoalJoin, a higher tier on CommunityGoal, CommunityGoalReward): a five-tick progress bar.
    //   JOIN    green, bar appears nearly empty        TIER    green, bar fills to the tier reached        REWARD  gold, bar fills and a ring flashes
    // Detail = "JOIN|goal|system", "TIER|goal|tier 1-5|contributors" or "REWARD|goal|credits".
    public sealed class CommunityGoalVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private static readonly Brush Green = Frozen(Color.FromRgb(95, 227, 176)), Gold = Frozen(Color.FromRgb(255, 216, 144)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), TrackBrush = Frozen(Color.FromArgb(38, 255, 255, 255)), TickDim = Frozen(Color.FromArgb(89, 255, 255, 255)), GapBrush = Frozen(Color.FromRgb(8, 13, 13));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 255, 216, 144), 1);

        private string _kind = "JOIN", _title = "", _goal = "", _sub = "";
        private int _tier = 1;
        private long _credits;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "JOIN";
            _goal = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : "COMMUNITY GOAL";
            string x = p.Length > 2 ? p[2].Trim() : "";
            switch (_kind)
            {
                case "TIER":
                    _tier = int.TryParse(x, out int t) ? Math.Clamp(t, 1, 5) : 1;
                    _title = $"TIER {_tier} REACHED";
                    _sub = p.Length > 3 && long.TryParse(p[3], out long c) && c > 0 ? c.ToString("N0", CultureInfo.InvariantCulture) + " CONTRIBUTORS" : "";
                    break;
                case "REWARD":
                    _title = "GOAL REWARD";
                    _credits = long.TryParse(x, out long r) ? r : 0;
                    _sub = _credits > 0 ? "+" + _credits.ToString("N0", CultureInfo.InvariantCulture) + " CR" : "";
                    break;
                default:
                    _kind = "JOIN"; _title = "GOAL JOINED"; _sub = x.ToUpperInvariant();
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

            const double x0 = 30, bw = 180, y = 40, bh = 11;
            double fill = _kind == "TIER" ? (_tier - 1) / 5.0 + EaseOut(Phase(t, 0.3, 1.4)) / 5.0 : _kind == "REWARD" ? EaseOut(Phase(t, 0.3, 1.0)) : EaseOut(Phase(t, 0.3, 0.7)) * 0.04;
            var main = _kind == "REWARD" ? Gold : Green;
            double barA = EaseOut(Phase(t, 0.05, 0.3));
            dc.PushOpacity(barA);
            dc.DrawRectangle(TrackBrush, null, new Rect(x0, y, bw, bh));
            dc.DrawRectangle(main, null, new Rect(x0, y, bw * fill, bh));
            for (int i = 1; i < 5; i++) dc.DrawRectangle(GapBrush, null, new Rect(x0 + bw * i / 5 - 0.75, y - 3, 1.5, bh + 6));
            for (int i = 0; i < 5; i++)
                DrawSpaced(dc, "T" + (i + 1), MonoFace, 7, fill >= (i + 1) / 5.0 - 0.001 ? Gold : TickDim, x0 + bw * (i + 0.5) / 5, y + bh + 6, 0.04, 1);
            dc.Pop();

            if (_kind == "REWARD")
            {
                double f = Phase(t, 1.4, 0.8);
                if (f > 0 && f < 1)
                {
                    dc.PushOpacity(1 - f);
                    dc.DrawRectangle(null, FrozenPen(Color.FromRgb(255, 216, 144), 1.5), new Rect(x0 - f * 10, y - f * 10, bw + f * 20, bh + f * 20));
                    dc.Pop();
                }
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), Gold, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _goal, MonoFace, Fit(_goal, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
