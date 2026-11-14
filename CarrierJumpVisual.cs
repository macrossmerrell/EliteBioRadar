using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // A fleet carrier's jump. SCHEDULED (the journal's CarrierJumpRequest): a ring clock counts down to the departure with the destination on the plate. CANCELLED (CarrierJumpCancelled): the clock
    // freezes, turns red and is struck out. ARRIVED (CarrierJump): the clock races to zero and flashes, the whole clock bursts outward and fades, and the carrier fades in at full size on the cleared stage.
    // Detail = "SCHEDULED|system|body|departure time (round-trip UTC, or PREVIEW)", "CANCELLED|system||seconds that were left" or "ARRIVED|system".
    public sealed class CarrierJumpVisual : AnimationVisual
    {
        private const double FadeOutAt = 4.2, SceneW = 240, SceneH = 150, CentreX = 120, CentreY = 46, RingR = 32, FullClock = 900, ZeroAt = 0.62, FlashAt = 0.78;
        protected override double Duration => FadeOutAt + 0.6;

        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen[] Thin, Arc;
            public Palette(Color c) { Solid = Frozen(c); Thin = AlphaPens(c, 1); Arc = AlphaPens(c, 3); }
        }
        private static readonly Color GoldColor = Color.FromRgb(255, 200, 90), RedColor = Color.FromRgb(255, 90, 70);
        private static readonly Palette Gold = new(GoldColor), Red = new(RedColor);
        private static readonly Pen[] BurstRing = AlphaPens(GoldColor, 1.6), StrikePen = AlphaPens(RedColor, 2.4);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 18, 12, 4)), NameBrush = Frozen(Color.FromRgb(0xff, 0xf3, 0xdc)), HullFill = Frozen(Color.FromRgb(0x2b, 0x2a, 0x26)), FlashBrush = Frozen(Color.FromRgb(255, 250, 235));
        private static readonly Brush CarrierGlow = MakeGlow();
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, GoldColor.R, GoldColor.G, GoldColor.B), 1), HullEdge = FrozenPen(GoldColor, 1.2), SpineEdge = FrozenPen(Color.FromArgb(153, GoldColor.R, GoldColor.G, GoldColor.B), 1.2);
        private static readonly Geometry HullShape = Poly(new[] { (-34.0, -5.0), (26.0, -7.0), (38.0, 0.0), (26.0, 7.0), (-34.0, 5.0) });
        private static readonly Rect[] Towers = { new(-24, -12, 8, 6), new(-4, -12, 10, 6), new(-24, 6, 8, 6), new(-4, 6, 10, 6) };

        private static Brush MakeGlow()
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(GoldColor, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, GoldColor.R, GoldColor.G, GoldColor.B), 1));
            b.Freeze(); return b;
        }

        private readonly Dictionary<int, Geometry> _arcs = new();
        private string _kind = "SCHEDULED", _system = "", _body = "";
        private DateTime _departure = DateTime.UtcNow.AddSeconds(898);
        private double _leftAtCancel = 898;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _kind = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "SCHEDULED";
            _system = p.Length > 1 ? p[1].Trim() : "";
            _body = p.Length > 2 ? p[2].Trim() : "";
            _departure = DateTime.UtcNow.AddSeconds(898);
            if (_kind == "SCHEDULED" && p.Length > 3 && DateTime.TryParse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dep)) _departure = dep.ToUniversalTime();
            if (_kind == "CANCELLED" && p.Length > 3 && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var left)) _leftAtCancel = Math.Max(0, left);
            else _leftAtCancel = 898;
        }

        private static string Clock(double secs) { int s = (int)Math.Max(0, Math.Floor(secs)); return (s / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture); }

        // An arc of 0..100 % round the clock, clockwise from the top, cached per percent.
        private Geometry Arc(int pct)
        {
            if (_arcs.TryGetValue(pct, out var g)) return g;
            double f = pct / 100.0, a0 = -Math.PI / 2, a1 = a0 + Math.PI * 2 * f;
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(new Point(Math.Cos(a0) * RingR, Math.Sin(a0) * RingR), false, false);
                if (pct >= 100) { c.ArcTo(new Point(Math.Cos(a0 + Math.PI) * RingR, Math.Sin(a0 + Math.PI) * RingR), new Size(RingR, RingR), 0, false, SweepDirection.Clockwise, true, false); c.ArcTo(new Point(Math.Cos(a0) * RingR, Math.Sin(a0) * RingR), new Size(RingR, RingR), 0, false, SweepDirection.Clockwise, true, false); }
                else if (pct > 0) c.ArcTo(new Point(Math.Cos(a1) * RingR, Math.Sin(a1) * RingR), new Size(RingR, RingR), 0, f > 0.5, SweepDirection.Clockwise, true, false);
            }
            sg.Freeze(); _arcs[pct] = sg; return sg;
        }

        private void DrawCarrier(DrawingContext dc, double alpha, double glow)
        {
            if (alpha <= 0) return;
            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            if (glow > 0.01) { dc.PushOpacity(Clamp01(glow)); dc.DrawEllipse(CarrierGlow, null, new Point(0, 0), 44, 44); dc.Pop(); }
            dc.PushOpacity(Clamp01(alpha));
            dc.DrawGeometry(HullFill, HullEdge, HullShape);
            foreach (var r in Towers) dc.DrawRectangle(HullFill, HullEdge, r);
            dc.DrawLine(SpineEdge, new Point(-34, 0), new Point(34, 0));
            dc.Pop(); dc.Pop();
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.25) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            bool arrived = _kind == "ARRIVED", cancelled = _kind == "CANCELLED";
            var pal = cancelled ? Red : Gold;

            // What the clock shows: the time left (live) while scheduled, the time that was left once cancelled, a fast count to zero on arrival.
            double secs = arrived ? Math.Max(0, 8 - t * 8 * (1 + t)) : cancelled ? _leftAtCancel : Math.Max(0, (_departure - DateTime.UtcNow).TotalSeconds);
            double frac = arrived ? Clamp01(secs / 8) : Math.Clamp(secs / FullClock, 0.03, 1);

            // On arrival the clock clears away (digits, ticks and ring burst outward) after the flash, leaving the stage to the carrier.
            double face = 1, ringA = 1, ringR = RingR;
            if (arrived && t > FlashAt) { double q = Phase(t, FlashAt, 0.3); face = 1 - q; ringA = 1 - q; ringR = RingR + q * 40; }

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            for (int i = 0; i < 40; i++) dc.DrawRectangle(Level(Whites, 0.15 + 0.25 * Hash(i + 3)), null, new Rect(Hash(i) * SceneW, Hash(i + 9) * 92, 1, 1));

            if (ringA > 0.01)
            {
                dc.DrawEllipse(null, pal.Thin[2], new Point(CentreX, CentreY), ringR, ringR);
                for (int i = 0; i < 60; i++)
                {
                    double an = i / 60.0 * Math.PI * 2 - Math.PI / 2, l = i % 5 == 0 ? 5 : 2.5, al = (i % 5 == 0 ? 0.8 : 0.4) * ringA;
                    int lvl = (int)Math.Round(al * 8);
                    if (lvl > 0) dc.DrawLine(pal.Thin[lvl], new Point(CentreX + Math.Cos(an) * (ringR + 2), CentreY + Math.Sin(an) * (ringR + 2)), new Point(CentreX + Math.Cos(an) * (ringR + 2 + l), CentreY + Math.Sin(an) * (ringR + 2 + l)));
                }
                int pct = (int)Math.Round(frac * 100);
                if (pct > 0)
                {
                    double s = ringR / RingR;
                    dc.PushTransform(new TranslateTransform(CentreX, CentreY)); dc.PushTransform(new ScaleTransform(s, s));
                    dc.DrawGeometry(null, pal.Arc[(int)Math.Round(ringA * 8)], Arc(pct));
                    dc.Pop(); dc.Pop();
                }
            }
            if (face > 0.01)
            {
                double blink = cancelled && t < 0.6 ? ((int)Math.Floor(t * 14) % 2 == 1 ? 0.4 : 1) : 1;
                DrawSpaced(dc, Clock(secs), MonoFace, 19, pal.Solid, CentreX, CentreY - 11, 0.04, blink * face);
                DrawSpaced(dc, "DEPARTURE", MonoFace, 6, pal.Solid, CentreX, CentreY + 11, 0.2, 0.7 * face);
            }
            if (cancelled)
            {
                double sa = Phase(t, 0.5, 0.3);
                if (sa > 0) dc.DrawLine(StrikePen[(int)Math.Round(sa * 8)], new Point(CentreX - 30, CentreY + 22), new Point(CentreX - 30 + 60 * sa, CentreY - 22));
            }

            if (arrived)
            {
                double show = EaseOut(Phase(t, FlashAt + 0.1, 0.45));
                DrawCarrier(dc, show, 0.35 * show * (1 - Phase(t, 1.5, 0.8)));
                for (int i = 0; i < 3; i++)
                {
                    double q = Phase(t, FlashAt + 0.05 + i * 0.12, 0.9);
                    if (q > 0 && q < 1) dc.DrawEllipse(null, BurstRing[(int)Math.Round((1 - q) * 8)], new Point(CentreX, CentreY), RingR + q * 60, RingR + q * 60);
                }
                double fl = Phase(t, FlashAt, 0.08) * (1 - Phase(t, FlashAt + 0.08, 0.4));
                if (fl > 0) { dc.PushOpacity(Clamp01(fl * 0.7)); dc.DrawRectangle(FlashBrush, null, new Rect(-40, -40, W + 80, H + 80)); dc.Pop(); }
            }

            // The plate: what is happening and where.
            string l1 = arrived ? "CARRIER ARRIVED" : cancelled ? "CARRIER JUMP CANCELLED" : "CARRIER JUMP SCHEDULED";
            string l2 = _system.Length > 0 ? _system : "Fleet carrier";
            string l3 = arrived ? "JUMP COMPLETE" : cancelled ? "" : "DEPARTS IN " + Clock(secs) + (_body.Length > 0 ? "  -  " + _body : "");
            double plateAt = arrived ? 1.6 : 1.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);   // as tall as its lines
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 180), pal.Solid, SceneW / 2, 106, 0.1, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.75);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
