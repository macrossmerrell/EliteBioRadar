using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Three combat alerts. ATTACK (the journal's UnderAttack, red): a hex shield dome around the ship ripples at each hit and the window edges flash. LOCK (ShipTargetted, amber): a spinning double reticle
    // collapses onto the target and a readout ticks in beside it. SCAN (Scanned, cyan): glitching scan lines sweep down your ship and a bar fills as the data is pulled.
    // Detail = "ATTACK|who" (You / Fighter ...), "LOCK|ship|pilot|rank|faction|legal status|shield %|hull %" (anything after the ship may be empty) or "SCAN|scan type" (Cargo / Crime / Cabin / Data).
    public sealed class CombatAlertVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.7, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 46, DomeR = 32;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color RedColor = Color.FromRgb(255, 70, 60), AmberColor = Color.FromRgb(255, 180, 60), CyanColor = Color.FromRgb(85, 242, 255);
        private static readonly Brush RedBrush = Frozen(RedColor), AmberBrush = Frozen(AmberColor), CyanBrush = Frozen(CyanColor), PlateBrush = Frozen(Color.FromArgb(214, 10, 8, 12)), TextBrush = Frozen(Color.FromRgb(0xf0, 0xf4, 0xf8)), HullFill = Frozen(Color.FromArgb(230, 200, 225, 230)), SparkBrush = Frozen(Color.FromRgb(255, 200, 180));
        private static readonly Pen[] RedThin = AlphaPens(RedColor, 1), RedRing = AlphaPens(RedColor, 1.4), RedRipple = AlphaPens(RedColor, 1.6), AmberThin = AlphaPens(AmberColor, 1);
        private static readonly Pen GreyEdge = FrozenPen(Color.FromArgb(204, 170, 190, 200), 0.8), AmberEdge = FrozenPen(Color.FromArgb(179, AmberColor.R, AmberColor.G, AmberColor.B), 0.8);
        private static readonly Pen RedPlateEdge = FrozenPen(Color.FromArgb(153, RedColor.R, RedColor.G, RedColor.B), 1), AmberPlateEdge = FrozenPen(Color.FromArgb(153, AmberColor.R, AmberColor.G, AmberColor.B), 1), CyanPlateEdge = FrozenPen(Color.FromArgb(153, CyanColor.R, CyanColor.G, CyanColor.B), 1);
        private static readonly double[] Hits = { 0.25, 0.6, 1.0, 1.35 };

        private string _mode = "ATTACK", _who = "You", _ship = "", _pilot = "", _faction = "", _legal = "", _shield = "", _hull = "", _scan = "";

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string Part(int i) => p.Length > i ? p[i].Trim() : "";
            _mode = Part(0).ToUpperInvariant();
            if (_mode != "LOCK" && _mode != "SCAN") _mode = "ATTACK";
            _who = Part(1); _scan = Part(1);
            _ship = Part(1); _pilot = Part(2); _faction = Part(4); _legal = Part(5); _shield = Part(6); _hull = Part(7);
            if (_pilot.StartsWith("$")) _pilot = "";
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));
        private static double Hit(double t) { double a = 0; foreach (var h in Hits) { double q = t - h; if (q >= 0 && q < 0.35) a = Math.Max(a, 1 - q / 0.35); } return a; }

        private void DrawShip(DrawingContext dc, double scale, Pen edge)
        {
            dc.PushTransform(new TranslateTransform(CentreX, CentreY)); dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(HullFill, edge, ShipOutline);
            dc.Pop(); dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            bool attack = _mode == "ATTACK", lockOn = _mode == "LOCK";
            Brush col = attack ? RedBrush : lockOn ? AmberBrush : CyanBrush;
            Pen plateEdge = attack ? RedPlateEdge : lockOn ? AmberPlateEdge : CyanPlateEdge;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);

            double hit = attack ? Hit(t) : 0;
            if (hit > 0.02) dc.DrawRectangle(LowFuelVisual.Edge[(int)Math.Round(hit * 8)], null, new Rect(0, 0, W, H));

            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            for (int i = 0; i < 40; i++) dc.DrawRectangle(Level(Whites, 0.12 + 0.2 * Hash(i + 3)), null, new Rect(Hash(i) * SceneW, Hash(i + 9) * 92, 1, 1));

            if (attack)
            {
                DrawShip(dc, 1.4, GreyEdge);
                dc.DrawEllipse(null, RedRing[(int)Math.Round(Clamp01(0.35 + 0.5 * hit) * 8)], new Point(CentreX, CentreY), DomeR, DomeR);
                int lv = (int)Math.Round(Clamp01(0.3 + 0.4 * hit) * 8), lv2 = (int)Math.Round(Clamp01(0.12 + 0.16 * hit) * 8);
                for (int i = 0; i < 6; i++)
                {
                    double an = i / 6.0 * Math.PI * 2 + 0.5, an2 = an + Math.PI / 3;
                    dc.DrawLine(RedThin[lv], new Point(CentreX + Math.Cos(an) * DomeR, CentreY + Math.Sin(an) * DomeR), new Point(CentreX + Math.Cos(an2) * DomeR, CentreY + Math.Sin(an2) * DomeR));
                    if (lv2 > 0) dc.DrawLine(RedThin[lv2], new Point(CentreX, CentreY), new Point(CentreX + Math.Cos(an) * DomeR, CentreY + Math.Sin(an) * DomeR));
                }
                for (int i = 0; i < Hits.Length; i++)
                {
                    double q = Phase(t, Hits[i], 0.45);
                    if (q <= 0 || q >= 1) continue;
                    double an = -0.6 + i * 0.9, hx = CentreX + Math.Cos(an) * DomeR, hy = CentreY + Math.Sin(an) * DomeR;
                    dc.DrawEllipse(null, RedRipple[(int)Math.Round((1 - q) * 8)], new Point(hx, hy), 4 + q * 22, 4 + q * 22);
                    dc.PushOpacity(1 - q); dc.DrawEllipse(SparkBrush, null, new Point(hx, hy), 3 * (1 - q) + 1, 3 * (1 - q) + 1); dc.Pop();
                }
            }
            else if (lockOn)
            {
                DrawShip(dc, 1.4, AmberEdge);
                double close = EaseOut(Phase(t, 0, 0.8)), r0 = 60 * (1 - close) + 20;
                for (int n = 0; n < 2; n++)
                {
                    double rr = r0 + n * 8, circ = Math.PI * 2 * rr, dash = 0.4 * rr, period = circ / 8.0;
                    var pen = new Pen(AmberBrush, 1.4) { DashStyle = new DashStyle(new[] { dash / 1.4, (period - dash) / 1.4 }, (n == 0 ? 1 : -1) * t * 3 * rr / 1.4) };
                    if (n == 1) dc.PushOpacity(0.5);
                    dc.DrawEllipse(null, pen, new Point(CentreX, CentreY), rr, rr);
                    if (n == 1) dc.Pop();
                }
                dc.DrawLine(AmberThin[6], new Point(CentreX - 30, CentreY), new Point(CentreX - 10, CentreY)); dc.DrawLine(AmberThin[6], new Point(CentreX + 10, CentreY), new Point(CentreX + 30, CentreY));
                dc.DrawLine(AmberThin[6], new Point(CentreX, CentreY - 30), new Point(CentreX, CentreY - 10)); dc.DrawLine(AmberThin[6], new Point(CentreX, CentreY + 10), new Point(CentreX, CentreY + 30));
                var rows = new List<string>();
                if (_ship.Length > 0) rows.Add(_ship.ToUpperInvariant());
                if (_shield.Length > 0) rows.Add("SHD " + _shield + "%");
                if (_hull.Length > 0) rows.Add("HULL " + _hull + "%");
                for (int i = 0; i < rows.Count && i < 3; i++)
                    DrawText(dc, rows[i].Length > 12 ? rows[i].Substring(0, 12) : rows[i], MonoFace, 7, AmberBrush, CentreX + 42, CentreY - 18 + i * 10, Phase(t, 0.9 + i * 0.25, 0.2), TextAlignment.Left);
            }
            else
            {
                DrawShip(dc, 1.4, GreyEdge);
                double sy = CentreY - 32 + Phase(t, 0.2, 1.0) * 64;
                for (int i = 0; i < 9; i++)
                {
                    double y = CentreY - 32 + i * 8; bool on = sy > y;
                    double jx = on && Hash(i + (int)(t * 12)) > 0.8 ? 4 : 0;
                    dc.PushOpacity(0.1 + (on ? 0.25 : 0)); dc.DrawRectangle(CyanBrush, null, new Rect(CentreX - 44 + jx, y, 88, 1)); dc.Pop();
                }
                if (t > 0.2 && t < 1.3) dc.DrawRectangle(CyanBrush, null, new Rect(CentreX - 44, sy, 88, 1.5));
                double p = Phase(t, 0.3, 1.1);
                dc.PushOpacity(0.2); dc.DrawRectangle(CyanBrush, null, new Rect(CentreX - 40, CentreY + 38, 80, 4)); dc.Pop();
                dc.DrawRectangle(CyanBrush, null, new Rect(CentreX - 40, CentreY + 38, 80 * p, 4));
                DrawSpaced(dc, "DATA " + ((int)Math.Round(p * 100)).ToString(CultureInfo.InvariantCulture) + "%", MonoFace, 6, CyanBrush, CentreX, CentreY + 44, 0.15, 0.9);
            }

            // The plate.
            string l1, l2, l3 = "";
            if (attack)
            {
                l1 = "UNDER ATTACK";
                l2 = _who.Equals("Fighter", StringComparison.OrdinalIgnoreCase) ? "Your fighter is taking fire" : _who.Length == 0 || _who.Equals("You", StringComparison.OrdinalIgnoreCase) ? "Your ship is taking fire" : "Taking fire";
            }
            else if (lockOn)
            {
                l1 = "TARGET LOCKED";
                l2 = _ship.Length > 0 ? _ship : "Hostile ship";
                var parts = new List<string>();
                if (_pilot.Length > 0) parts.Add(_pilot);
                if (_faction.Length > 0) parts.Add(_faction);
                if (_legal.Length > 0) parts.Add(_legal.ToUpperInvariant());
                l3 = string.Join("  -  ", parts);
            }
            else
            {
                l1 = "SCANNED";
                l2 = _scan.Length > 0 && !_scan.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? _scan + " scan" : "Scanned by another ship";
            }
            double plateAt = lockOn ? 1.4 : 1.1, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, plateEdge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 180), col, SceneW / 2, 106, 0.1, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), TextBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), col, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.5, 0.4) * 0.8);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
