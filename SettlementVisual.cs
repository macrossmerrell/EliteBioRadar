using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Approaching a settlement (the journal's ApproachSettlement, for places with a market): a data card types in the owning faction, government and
    // economy, then one tile per service pops in underneath (M market, O outfitting, Y shipyard, E engineer, F refuel, R repair, A rearm, P missions, S search and rescue).
    // Detail = "NAME|faction|government|economy|body|services" with the services as a string of those letters.
    public sealed class SettlementVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0;
        private static readonly Brush Cyan = Frozen(Color.FromRgb(85, 242, 255)), Gold = Frozen(Color.FromRgb(255, 216, 144)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), LabelBrush = Frozen(Color.FromArgb(102, 255, 255, 255)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), TileBrush = Frozen(Color.FromRgb(0x1b, 0x22, 0x30));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 85, 242, 255), 1), TileEdge = FrozenPen(Color.FromRgb(85, 242, 255), 1.2);

        private string _name = "", _faction = "", _gov = "", _eco = "", _services = "", _sub = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim().ToUpperInvariant() : "";
            _name = P(0).Length > 0 ? P(0) : "SETTLEMENT";
            _faction = P(1); _gov = P(2); _eco = P(3); _services = P(5);
            _sub = string.Join(" - ", new[] { _eco, _gov }.Where(s => s.Length > 0));
        }


        private void DrawRow(DrawingContext dc, string label, string value, double y, double start, double t)
        {
            if (value.Length == 0) return;
            double p = Phase(t, start, 0.45);
            if (p <= 0) return;
            DrawText(dc, label, MonoFace, 6.5, LabelBrush, 24, y + 1.5, Math.Min(1, p * 3), TextAlignment.Left);
            string shown = value.Substring(0, (int)Math.Floor(value.Length * p));
            if (p < 1) shown += "_";
            DrawText(dc, shown, MonoFace, Math.Min(8, 118 / (Math.Max(1, value.Length) * 0.62)), NameBrush, 82, y, 1, TextAlignment.Left);
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

            DrawRow(dc, "FACTION", _faction, 14, 0.2, t);
            DrawRow(dc, "GOVERNMENT", _gov, 28, 0.65, t);
            DrawRow(dc, "ECONOMY", _eco, 42, 1.1, t);

            int n = Math.Min(6, _services.Length);
            const double tw = 22, th = 16, gap = 5;
            double x0 = SceneW / 2 - (n * tw + (n - 1) * gap) / 2;
            for (int i = 0; i < n; i++)
            {
                double p = EaseOut(Phase(t, 1.6 + i * 0.15, 0.3));
                if (p <= 0) continue;
                dc.PushTransform(new TranslateTransform(x0 + i * (tw + gap) + tw / 2, 72)); dc.PushTransform(new ScaleTransform(0.6 + 0.4 * p, 0.6 + 0.4 * p));
                dc.PushOpacity(p);
                dc.DrawRoundedRectangle(TileBrush, TileEdge, new Rect(-tw / 2, -th / 2, tw, th), 3, 3);
                DrawText(dc, _services[i].ToString(), MonoFace, 9, Cyan, 0, -5.5, 1, TextAlignment.Center);
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.6, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "APPROACHING SETTLEMENT", MonoFace, Fit("APPROACHING SETTLEMENT", 10, 190), Cyan, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
