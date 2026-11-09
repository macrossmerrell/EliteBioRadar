using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A bounty or combat bond paid out (the journal's Bounty / FactionKillBond, several in a row folded into one): a wanted card slides in showing the target ship and its name, a stamp thumps down,
    // CLAIMED in mint for a bounty or AWARDED in cyan for a combat bond, and the reward counts up in gold. Detail = "BOUNTY|count|target|victim faction|reward|awarding faction" or "BOND|count|victim faction|victim faction|reward|awarding faction".
    public sealed class BountyVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.2, CardX = 65, CardY = 8, CardW = 110, CardH = 80;

        private static readonly Color Amber = Color.FromRgb(0xff, 0xb8, 0x4d), Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Red = Color.FromRgb(0xff, 0x6b, 0x5e), Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush AmberBrush = Frozen(Amber), CyanBrush = Frozen(Cyan), MintBrush = Frozen(Mint), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), AmberFill = Frozen(Color.FromArgb(20, Amber.R, Amber.G, Amber.B)), CyanFill = Frozen(Color.FromArgb(20, Cyan.R, Cyan.G, Cyan.B)), ShipFill = Frozen(Color.FromArgb(55, Red.R, Red.G, Red.B));
        private static readonly Pen AmberEdge = FrozenPen(Color.FromArgb(204, Amber.R, Amber.G, Amber.B), 1.5), CyanEdge = FrozenPen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B), 1.5), ShipPen = FrozenPen(Color.FromArgb(230, Red.R, Red.G, Red.B), 1.2);
        private static readonly Pen MintStamp = FrozenPen(Mint, 1.5), CyanStamp = FrozenPen(Cyan, 1.5);
        private static readonly Pen AmberPlateEdge = FrozenPen(Color.FromArgb(153, Amber.R, Amber.G, Amber.B), 1), CyanPlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1);

        private bool _bond;
        private int _count = 1;
        private string _name = "", _victim = "", _awarder = "";
        private long _reward;
        private readonly ScaleTransform _outer = new(), _fit = new(), _stampScale = new(), _shipScale = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _stampAt = new(), _shipAt = new();
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _bond = p.Length > 0 && p[0].Contains("BOND", StringComparison.OrdinalIgnoreCase);
            _count = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Max(1, c) : 1;
            _name = p.Length > 2 ? p[2].Trim() : "";
            _victim = p.Length > 3 ? p[3].Trim() : "";
            _reward = p.Length > 4 && long.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Max(0, r) : 0;
            _awarder = p.Length > 5 ? p[5].Trim() : "";
            if (_name.Length == 0) _name = _bond ? "Combat bond" : "Hostile ship";
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

            double slide = EaseOut(Phase(t, 0.1, 0.6)), x = CardX + (1 - slide) * 170, cx = x + CardW / 2;
            dc.DrawRectangle(_bond ? CyanFill : AmberFill, _bond ? CyanEdge : AmberEdge, new Rect(x, CardY, CardW, CardH));
            DrawSpaced(dc, _bond ? "COMBAT BOND" : "WANTED", MonoFace, 9, _bond ? CyanBrush : AmberBrush, cx, CardY + 5, 0.1, 1);

            _shipAt.X = cx; _shipAt.Y = CardY + 33; _shipScale.ScaleX = _shipScale.ScaleY = 0.95;
            dc.PushTransform(_shipAt); dc.PushTransform(_shipScale);
            dc.DrawGeometry(ShipFill, ShipPen, ShipOutline);
            dc.Pop(); dc.Pop();

            string title = _count > 1 ? _count.ToString(CultureInfo.InvariantCulture) + (_bond ? " BONDS" : " TARGETS") : _name;
            DrawSpaced(dc, title, MonoFace, Fit(title, 8, CardW - 12), NameBrush, cx, CardY + 53, 0.04, 1);

            if (_reward > 0)
            {
                double n = _reward * EaseOut(Phase(t, 1.5, 0.9));
                DrawSpaced(dc, "+" + Credits(n), MonoFace, 8, GoldBrush, cx, CardY + CardH - 14, 0.04, Phase(t, 1.4, 0.2));
            }

            double st = Phase(t, 1.0, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.3;
                _stampAt.X = x + 55; _stampAt.Y = CardY + 36; _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, _bond ? CyanStamp : MintStamp, new Rect(-33, -10, 66, 20));
                DrawSpaced(dc, _bond ? "AWARDED" : "CLAIMED", MonoFace, 10, _bond ? CyanBrush : MintBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.2, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, _bond ? CyanPlateEdge : AmberPlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                string head = _bond ? (_count > 1 ? "COMBAT BONDS" : "COMBAT BOND") : (_count > 1 ? "BOUNTIES CLAIMED" : "BOUNTY CLAIMED");
                DrawSpaced(dc, head, MonoFace, Fit(head, 12, 180), _bond ? CyanBrush : AmberBrush, SceneW / 2, 106, 0.08, pa);
                string line2 = _reward > 0 ? "+" + Credits(_reward) : title;
                DrawSpaced(dc, line2, MonoFace, Fit(line2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string line3 = _bond ? (_awarder.Length > 0 ? "awarded by " + _awarder : _victim) : (_count > 1 ? _name + "  +" + (_count - 1).ToString(CultureInfo.InvariantCulture) + " more" : (_victim.Length > 0 ? _name + "  -  " + _victim : _name));
                DrawSpaced(dc, line3, MonoFace, Fit(line3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
