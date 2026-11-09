using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A mission accepted or completed (the journal's MissionAccepted / MissionCompleted, several in a row folded into one): a contract card slides in with the mission and its faction and a stamp
    // thumps onto it, ACCEPTED in cyan or COMPLETED in mint; when completed the reward counts up in gold. Detail = "ACCEPTED|count|name|faction|reward" or "COMPLETED|count|name|faction|reward" (reward 0 = unknown).
    public sealed class MissionVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 3.6, CardX = 65, CardY = 12, CardW = 110, CardH = 66;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush CyanBrush = Frozen(Cyan), MintBrush = Frozen(Mint), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardFill = Frozen(Color.FromArgb(18, Mint.R, Mint.G, Mint.B));
        private static readonly Brush[] CyanLines = AlphaBrushes(Cyan), MintLines = AlphaBrushes(Mint);
        private static readonly Pen CyanEdge = FrozenPen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B), 1.5), MintEdge = FrozenPen(Color.FromArgb(204, Mint.R, Mint.G, Mint.B), 1.5);
        private static readonly Pen CyanStamp = FrozenPen(Cyan, 1.5), MintStamp = FrozenPen(Mint, 1.5);
        private static readonly Pen CyanPlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), MintPlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1);

        private bool _done;
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
            _done = p.Length > 0 && p[0].Contains("COMPLETE", StringComparison.OrdinalIgnoreCase);
            _count = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Max(1, c) : 1;
            _name = p.Length > 2 ? p[2].Trim() : "";
            _faction = p.Length > 3 ? p[3].Trim() : "";
            _reward = p.Length > 4 && long.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Max(0, r) : 0;
            if (_name.Length == 0) _name = "Mission";
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));
        private static string Credits(double n) => Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) + " CR";

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            var edge = _done ? MintEdge : CyanEdge;
            var lines = _done ? MintLines : CyanLines;
            double slide = EaseOut(Phase(t, 0.1, 0.6)), x = CardX + (1 - slide) * 160, cx = x + CardW / 2;
            dc.DrawRectangle(CardFill, edge, new Rect(x, CardY, CardW, CardH));
            string title = _count > 1 ? _count.ToString(CultureInfo.InvariantCulture) + " MISSIONS" : _name;
            string sub = _count > 1 ? _name + "  +" + (_count - 1).ToString(CultureInfo.InvariantCulture) + " more" : _faction;
            DrawSpaced(dc, title, MonoFace, Fit(title, 8, CardW - 12), NameBrush, cx, CardY + 7, 0.04, 1);
            DrawSpaced(dc, sub, MonoFace, Fit(sub, 6, CardW - 12), MutedBrush, cx, CardY + 19, 0.04, 1);
            for (int i = 0; i < 3; i++) dc.DrawRectangle(lines[2], null, new Rect(x + 7, CardY + 31 + i * 6, i == 2 ? 50 : 90, 2));

            if (_done && _reward > 0)
            {
                double n = _reward * EaseOut(Phase(t, 1.1, 0.9));
                DrawSpaced(dc, "+" + Credits(n), MonoFace, 9, GoldBrush, cx, CardY + CardH - 14, 0.04, Phase(t, 1.0, 0.2));
            }

            double st = Phase(t, _done ? 0.7 : 0.9, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.2;
                _stampAt.X = x + 75; _stampAt.Y = 50; _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, _done ? MintStamp : CyanStamp, new Rect(-33, -10, 66, 20));
                DrawSpaced(dc, _done ? "COMPLETED" : "ACCEPTED", MonoFace, 10, _done ? MintBrush : CyanBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 1.8, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, _done ? MintPlateEdge : CyanPlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                string head = (_done ? "MISSION" : "MISSION") + (_count > 1 ? "S " : " ") + (_done ? "COMPLETED" : "ACCEPTED");
                DrawSpaced(dc, head, MonoFace, Fit(head, 12, 180), _done ? MintBrush : CyanBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, title, MonoFace, Fit(title, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string line3 = _done ? (_reward > 0 ? "REWARD  +" + Credits(_reward) : _faction) : (_reward > 0 ? (_faction.Length > 0 ? _faction + "  -  " : "") + "reward " + Credits(_reward) : _faction);
                DrawSpaced(dc, line3, MonoFace, Fit(line3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
