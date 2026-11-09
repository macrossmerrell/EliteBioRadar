using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Cargo picked up or thrown out (the journal's CollectCargo / EjectCargo, several in a row of one commodity folded into one): a shipping manifest card slides in with a (deliberately faint) crate, the
    // commodity and the tonnes counting up, a stamp thumps down, LOADED in mint or DUMPED in amber, and a hold bar moves from the old fill to the new one. Detail = "COLLECT|name|tonnes|hold used|hold capacity"
    // or "DUMP|name|tonnes|hold used|hold capacity" (used / capacity -1 = unknown).
    public sealed class CargoVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.2, CardX = 60, CardY = 8, CardW = 120, CardH = 88;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xff, 0xb8, 0x4d), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Gold = Color.FromRgb(0xff, 0xd3, 0x6b);
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber), MintBrush = Frozen(Mint), GoldBrush = Frozen(Gold), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CyanFill = Frozen(Color.FromArgb(20, Cyan.R, Cyan.G, Cyan.B)), AmberFill = Frozen(Color.FromArgb(20, Amber.R, Amber.G, Amber.B));
        private static readonly Brush TrackBrush = Frozen(Color.FromArgb(64, 120, 140, 150)), HoldBrush = Frozen(Color.FromArgb(220, Cyan.R, Cyan.G, Cyan.B));
        private static readonly Pen CyanEdge = FrozenPen(Color.FromArgb(204, Cyan.R, Cyan.G, Cyan.B), 1.5), AmberEdge = FrozenPen(Color.FromArgb(204, Amber.R, Amber.G, Amber.B), 1.5);
        private static readonly Pen CyanPlateEdge = FrozenPen(Color.FromArgb(153, Cyan.R, Cyan.G, Cyan.B), 1), AmberPlateEdge = FrozenPen(Color.FromArgb(153, Amber.R, Amber.G, Amber.B), 1);
        private static readonly Pen MintStamp = FrozenPen(Mint, 1.5), AmberStamp = FrozenPen(Amber, 1.5);
        // The crate stays faint so the stamp reads over it: a thin outline and a barely-there cross.
        private static readonly Pen CrateEdge = FrozenPen(Color.FromArgb(85, 160, 200, 205), 0.8), CrateCross = FrozenPen(Color.FromArgb(38, 160, 200, 205), 0.6);

        private bool _dump;
        private string _name = "";
        private int _tonnes = 1, _used = -1, _cap = -1;
        private readonly ScaleTransform _outer = new(), _fit = new(), _stampScale = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _stampAt = new();
        private readonly RotateTransform _stampRot = new(-11.5);

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _dump = p.Length > 0 && p[0].Contains("DUMP", StringComparison.OrdinalIgnoreCase);
            _name = p.Length > 1 ? p[1].Trim() : "";
            if (_name.Length == 0) _name = "Cargo";
            int I(int i, int d) => p.Length > i && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : d;
            _tonnes = Math.Max(1, I(2, 1)); _used = I(3, -1); _cap = I(4, -1);
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

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
            var accent = _dump ? AmberBrush : CyanBrush;
            dc.DrawRectangle(_dump ? AmberFill : CyanFill, _dump ? AmberEdge : CyanEdge, new Rect(x, CardY, CardW, CardH));
            DrawSpaced(dc, "CARGO MANIFEST", MonoFace, 8, accent, cx, CardY + 5, 0.1, 1);

            // The faint crate under the stamp.
            double cy = CardY + 33, s = 11;
            dc.DrawRectangle(null, CrateEdge, new Rect(cx - s, cy - s, s * 2, s * 2));
            dc.DrawLine(CrateCross, new Point(cx - s, cy - s), new Point(cx + s, cy + s));
            dc.DrawLine(CrateCross, new Point(cx + s, cy - s), new Point(cx - s, cy + s));

            DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, CardW - 12), NameBrush, cx, CardY + 54, 0.04, 1);
            double n = _tonnes * EaseOut(Phase(t, 1.2, 0.8));
            DrawSpaced(dc, (_dump ? "-" : "+") + Math.Round(n).ToString(CultureInfo.InvariantCulture) + " T", MonoFace, 8, GoldBrush, cx, CardY + 66, 0.06, Phase(t, 1.1, 0.2));

            if (_used >= 0 && _cap > 0)
            {
                double before = Math.Clamp((_dump ? _used + _tonnes : _used - _tonnes) / (double)_cap, 0, 1), after = Math.Clamp(_used / (double)_cap, 0, 1);
                double fill = Lerp(before, after, EaseOut(Phase(t, 1.4, 0.8))), bw = CardW - 20;
                dc.DrawRectangle(TrackBrush, null, new Rect(x + 10, CardY + CardH - 9, bw, 3));
                dc.DrawRectangle(HoldBrush, null, new Rect(x + 10, CardY + CardH - 9, bw * fill, 3));
            }

            double st = Phase(t, 0.9, 0.25);
            if (st > 0)
            {
                double sc = 1 + (1 - EaseOut(st)) * 1.3;
                _stampAt.X = cx; _stampAt.Y = cy; _stampScale.ScaleX = _stampScale.ScaleY = sc;
                dc.PushOpacity(EaseOut(st));
                dc.PushTransform(_stampAt); dc.PushTransform(_stampRot); dc.PushTransform(_stampScale);
                dc.DrawRectangle(null, _dump ? AmberStamp : MintStamp, new Rect(-33, -10, 66, 20));
                DrawSpaced(dc, _dump ? "DUMPED" : "LOADED", MonoFace, 10, _dump ? AmberBrush : MintBrush, 0, -7, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.2, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, _dump ? AmberPlateEdge : CyanPlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                string head = _dump ? "CARGO JETTISONED" : "CARGO COLLECTED";
                DrawSpaced(dc, head, MonoFace, Fit(head, 12, 180), accent, SceneW / 2, 106, 0.08, pa);
                string line2 = (_dump ? "-" : "+") + _tonnes.ToString(CultureInfo.InvariantCulture) + " t  " + _name;
                DrawSpaced(dc, line2, MonoFace, Fit(line2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string line3 = _used >= 0 && _cap > 0 ? "hold " + _used.ToString(CultureInfo.InvariantCulture) + " / " + _cap.ToString(CultureInfo.InvariantCulture) + " t" : (_dump ? "thrown out of the hold" : "taken aboard");
                DrawSpaced(dc, line3, MonoFace, Fit(line3, 8, 180), MutedBrush, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
