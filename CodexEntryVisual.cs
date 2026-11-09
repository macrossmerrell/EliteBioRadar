using System;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A new codex entry was discovered (the journal's CodexEntry with IsNewEntry): a cyan scanner ring tightens on a faint diamond, its ticks turning slower as it locks; the diamond
    // fills mint with a white ripple, and the plate reads "NEW CODEX ENTRY" over the entry's name and its category. Subject = the entry name, Detail = "CATEGORY|SUB-CATEGORY".
    public sealed class CodexEntryVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.3, CentreX = 120, CentreY = 52, TickCount = 24;

        private static readonly Color Cyan = Color.FromRgb(0x4f, 0xd6, 0xe8), Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), White = Color.FromRgb(255, 255, 255);
        private static readonly Brush MintBrush = Frozen(Mint), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush[] DiamondFill = AlphaBrushes(Mint);
        private static readonly Pen[] RingPen = AlphaPens(Cyan, 1), DiamondPen = AlphaPens(Mint, 1), Flash = AlphaPens(White, 1);
        private static readonly Geometry Diamond = Poly(new[] { (0.0, -17.0), (13, 0), (0, 17), (-13, 0) });

        private string _name = "", _category = "";
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new(), _place = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            _name = (subject ?? "").Trim();
            var parts = (detail ?? "").Split('|');
            string cat = parts.Length > 0 ? parts[0].Trim().ToUpperInvariant() : "", sub = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : "";
            _category = sub.Length > 0 ? sub : cat;   // the sub-category says more ("Bacterial Colonies") than the category ("Biological and Geological")
        }

        // The size that keeps `text` inside the plate's width (about 200 units) with the letter spacing used.
        private static double Fit(string text, double max) => Math.Min(max, 200 / (Math.Max(1, text.Length) * 0.66));

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            double tight = EaseOut(Phase(t, 0.3, 1.9)), fill = EaseOut(Phase(t, 1.6, 1.0)), R = 47.5 - 27.5 * tight;
            var centre = new Point(CentreX, CentreY);
            var ring = RingPen[Math.Clamp((int)Math.Round((0.35 + 0.5 * tight) * 8), 0, 8)];
            dc.DrawEllipse(null, ring, centre, R, R);
            double spin = t * (1 - tight) * 1.5;
            for (int i = 0; i < TickCount; i++)
            {
                double ang = i / TickCount * Math.PI * 2 + spin, c = Math.Cos(ang), s = Math.Sin(ang);
                dc.DrawLine(ring, new Point(CentreX + c * (R + 2), CentreY + s * (R + 2)), new Point(CentreX + c * (R + 6), CentreY + s * (R + 6)));
            }

            _place.X = CentreX; _place.Y = CentreY; dc.PushTransform(_place);
            dc.DrawGeometry(DiamondFill[Math.Clamp((int)Math.Round(0.55 * fill * 8), 0, 8)], DiamondPen[Math.Clamp((int)Math.Round((0.4 + 0.6 * fill) * 8), 0, 8)], Diamond);
            dc.Pop();

            double fl = Phase(t, 2.2, 0.5);
            if (fl > 0 && fl < 1) dc.DrawEllipse(null, Flash[Math.Clamp((int)Math.Round((1 - fl) * 8), 0, 8)], centre, 20 + fl * 30, 20 + fl * 30);

            double pa = Phase(t, 2.7, 0.5), rise = (1 - EaseOut(pa)) * 5;
            if (pa > 0)
            {
                DrawSpaced(dc, "NEW CODEX ENTRY", MonoFace, 11, MintBrush, SceneW / 2, 108 + rise, 0.1, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 10), NameBrush, SceneW / 2, 122 + rise, 0.1, pa);
                DrawSpaced(dc, _category, MonoFace, Fit(_category, 8), MutedBrush, SceneW / 2, 136 + rise, 0.1, pa);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
