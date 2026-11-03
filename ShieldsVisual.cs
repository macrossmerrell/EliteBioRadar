using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Shields switching: a honeycomb dome around the ship. Shields UP: a ring pulses out and the hex cells light from the ship outward, each with a brief flash, the rim closes (cyan).
    // Shields DOWN: the cells go out one by one in a random order, drifting outward, and the rim fades (amber). About 3.6 seconds. Detail = "UP" or "DOWN".
    public sealed class ShieldsVisual : AnimationVisual
    {
        private const double FadeOutAt = 3.0, SceneW = 240, SceneH = 150, DomeR = 48, CellSize = 7.5, CentreX = 120, CentreY = 62;
        protected override double Duration => FadeOutAt + 0.6;

        private static readonly Color Cyan = Color.FromRgb(0x55, 0xf2, 0xff), Amber = Color.FromRgb(0xf2, 0xb8, 0x4b);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(184, 0x05, 0x0b, 0x07)), HullBrush = Frozen(Color.FromRgb(0xcf, 0xea, 0xea));
        private static readonly Brush CyanBrush = Frozen(Cyan), AmberBrush = Frozen(Amber);
        private static readonly Brush[] CyanCell = MakeLevels(Cyan, 32), AmberCell = MakeLevels(Amber, 32);
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(89, Cyan.R, Cyan.G, Cyan.B), 1), CyanShip = FrozenPen(Color.FromArgb(150, Cyan.R, Cyan.G, Cyan.B), 1), AmberShip = FrozenPen(Color.FromArgb(150, Amber.R, Amber.G, Amber.B), 1);
        private static readonly Pen[] CyanRim = MakePens(Cyan, 1.5, 16), AmberRim = MakePens(Amber, 1.5, 16), CyanWave = MakePens(Cyan, 3, 16), CyanGlint = MakePens(Color.FromRgb(255, 255, 255), 2, 16), AmberWave = MakePens(Amber, 1.5, 16);
        private static readonly Geometry Ship = MakeShip();
        private static readonly Geometry HexShape = MakeHex();
        private static readonly Cell[] Cells = MakeCells();

        private readonly struct Cell
        {
            public readonly double X, Y, D, H;
            public Cell(double x, double y, double d, double h) { X = x; Y = y; D = d; H = h; }
        }

        private static Brush[] MakeLevels(Color c, int steps)
        {
            var a = new Brush[steps + 1];
            for (int i = 0; i <= steps; i++) a[i] = Frozen(Color.FromArgb((byte)Math.Round(255.0 * i / steps), c.R, c.G, c.B));
            return a;
        }

        private static Pen[] MakePens(Color c, double width, int steps)
        {
            var a = new Pen[steps + 1];
            for (int i = 0; i <= steps; i++) a[i] = FrozenPen(Color.FromArgb((byte)Math.Round(255.0 * i / steps), c.R, c.G, c.B), width);
            return a;
        }

        private static Geometry MakeShip()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -12), true, true);
                c.LineTo(new Point(11, 12), true, false);
                c.LineTo(new Point(4, 8), true, false);
                c.LineTo(new Point(0, 14), true, false);
                c.LineTo(new Point(-4, 8), true, false);
                c.LineTo(new Point(-11, 12), true, false);
            }
            g.Freeze();
            return g;
        }

        // One pointy hex, a pixel smaller than its cell so a hairline gap shows between neighbours, drawn at the origin and moved into place.
        private static Geometry MakeHex()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = 0; i < 6; i++)
                {
                    double an = Math.PI / 3 * i + Math.PI / 6, r = CellSize - 1;
                    var p = new Point(Math.Cos(an) * r, Math.Sin(an) * r);
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, false);
                }
            g.Freeze();
            return g;
        }

        private static double Hash(int i) { double s = Math.Sin(i * 127.1) * 43758.5453; return s - Math.Floor(s); }

        private static Cell[] MakeCells()
        {
            var list = new List<Cell>();
            double w = CellSize * Math.Sqrt(3);
            int n = 0;
            for (int r = -8; r <= 8; r++)
                for (int q = -8; q <= 8; q++)
                {
                    double x = CentreX + q * w + ((r & 1) != 0 ? w / 2 : 0), y = CentreY + r * CellSize * 1.5, d = Math.Sqrt((x - CentreX) * (x - CentreX) + (y - CentreY) * (y - CentreY)) / DomeR;
                    if (d > 1) continue;
                    n++;
                    list.Add(new Cell(x, y, d, Hash(n)));
                }
            return list.ToArray();
        }

        private bool _up = true;

        protected override void OnStart(string subject, string detail) => _up = !detail.Contains("DOWN", StringComparison.OrdinalIgnoreCase);

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;
            Brush[] cell = _up ? CyanCell : AmberCell;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            dc.PushTransform(new TranslateTransform(CentreX, CentreY));
            dc.DrawGeometry(HullBrush, _up ? CyanShip : AmberShip, Ship);
            dc.Pop();

            double sweep = EaseOut(Phase(t, 0.1, 1.3));
            foreach (var c in Cells)
            {
                double alpha, dx = 0, dy = 0;
                if (_up)
                {
                    alpha = c.D < sweep ? Math.Min(1, (sweep - c.D) * 6) * (0.25 + 0.35 * (1 - c.D)) : 0;
                    double flash = Phase(t, 0.1 + c.D * 1.3, 0.35);
                    if (flash > 0 && flash < 1) alpha += 0.5 * (1 - flash);
                }
                else
                {
                    double gone = Phase(t, 0.1 + c.H * 0.9, 0.5);
                    alpha = (1 - gone) * 0.35;
                    dx = (c.X - CentreX) * gone * 0.25; dy = (c.Y - CentreY) * gone * 0.25;
                }
                if (alpha < 0.012) continue;
                dc.PushTransform(new TranslateTransform(c.X + dx, c.Y + dy));
                dc.DrawGeometry(cell[(int)Math.Round(Math.Min(1, alpha) * 32)], null, HexShape);
                dc.Pop();
            }

            // The rim closes as the dome fills (up), or fades once the cells have gone (down); a pulse ring leaves the ship first on the way up, a glint runs round the rim after.
            double rim = _up ? EaseOut(Phase(t, 0.1, 1.3)) : 1 - Phase(t, 0.8, 0.6);
            if (rim > 0.02) dc.DrawEllipse(null, (_up ? CyanRim : AmberRim)[(int)Math.Round(rim * 16)], new Point(CentreX, CentreY), DomeR, DomeR);
            if (_up)
            {
                double wave = EaseOut(Phase(t, 0.1, 0.9));
                if (wave > 0 && wave < 1)
                {
                    double r = 6 + wave * (DomeR + 10);
                    dc.DrawEllipse(null, CyanWave[(int)Math.Round((1 - wave) * 16)], new Point(CentreX, CentreY), r, r);
                }
                double gl = Phase(t, 1.1, 0.9);
                if (gl > 0 && gl < 1)
                {
                    double a0 = -1.6 + gl * 4, a1 = a0 + 0.6;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(CentreX + Math.Cos(a0) * DomeR, CentreY + Math.Sin(a0) * DomeR), false, false);
                        c.ArcTo(new Point(CentreX + Math.Cos(a1) * DomeR, CentreY + Math.Sin(a1) * DomeR), new Size(DomeR, DomeR), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, CyanGlint[(int)Math.Round(Math.Sin(gl * Math.PI) * 0.7 * 16)], g);
                }
            }
            else
            {
                double pu = Phase(t, 0.9, 0.4);
                if (pu > 0 && pu < 1)
                {
                    double r = 5 + pu * 14;
                    dc.DrawEllipse(null, AmberWave[(int)Math.Round((1 - pu) * 0.6 * 16)], new Point(CentreX, CentreY), r, r);
                }
            }

            // The plate: a dark backing so the words read over anything.
            dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(46, 108, 148, 28), 6, 6);
            DrawSpaced(dc, _up ? "SHIELDS UP" : "SHIELDS DOWN", MonoFace, 13, _up ? CyanBrush : AmberBrush, SceneW / 2, 114, 0.15, Phase(t, _up ? 0.5 : 0.2, 0.4));

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
