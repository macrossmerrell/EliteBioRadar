using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    public partial class MainWindow
    {
        // Thargoid sensor shown for a Nonhuman Signature target (see RenderSignalTargetPanel), as a cyan hologram: a round head
        // seated on a spinal column of four segments, each a cluster of three tubes (one ringed, one slotted, one hidden on the
        // far side, hinted dashed) around a joint with an amber notch; only the top third of the head shows the honeycomb of
        // glowing cells. Authored in a 1000 x 640 space; cx/cy is where the panel's centre lands, scale multiplies it. The cells
        // and slot lights pulse, a scan line sweeps down, and the whole thing sways slowly - all plain WPF animation.
        private static Canvas MakeThargoidSensorIllustration(double cx, double cy, double scale)
        {
            const double W = 1000, H = 640;
            var cyan = new SolidColorBrush(Color.FromArgb(0xf2, 95, 230, 255));
            var cyanDim = new SolidColorBrush(Color.FromArgb(0x73, 95, 230, 255));
            var fill = new SolidColorBrush(Color.FromArgb(0x10, 0, 200, 255));
            var slotFill = new SolidColorBrush(Color.FromArgb(0x99, 0, 30, 50));
            double HX = 770, HY = 296, HR = 150, NX = 0.5, NY = -0.86;
            double Rnd(double i) { double x = Math.Sin(i * 127.1 + 311.7) * 43758.5453; return x - Math.Floor(x); }
            double Lerp(double a, double b, double t) => a + (b - a) * t;

            var host = new Canvas { Width = W, Height = H, IsHitTestVisible = false, Clip = new RectangleGeometry(new Rect(0, 0, W, H)) };
            host.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(cx - W * scale / 2, cy - H * scale / 2) } };

            // Panel: dark glass with a faint grid and a cyan border.
            host.Children.Add(new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromRgb(3, 18, 28)), Stroke = new SolidColorBrush(Color.FromArgb(0x88, 95, 230, 255)), StrokeThickness = 3 });
            var grid = new StreamGeometry();
            using (var gc = grid.Open())
            {
                for (int x = 40; x < W; x += 40) { gc.BeginFigure(new Point(x, 0), false, false); gc.LineTo(new Point(x, H), true, false); }
                for (int y = 40; y < H; y += 40) { gc.BeginFigure(new Point(0, y), false, false); gc.LineTo(new Point(W, y), true, false); }
            }
            grid.Freeze();
            host.Children.Add(new Path { Data = grid, Stroke = new SolidColorBrush(Color.FromArgb(0x16, 95, 230, 255)), StrokeThickness = 1 });

            // Everything below sways together.
            var body = new Canvas { Width = W, Height = H };
            var sway = new TranslateTransform();
            body.RenderTransform = sway;
            sway.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(6.3))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            host.Children.Add(body);

            Canvas Group(Canvas parent, double tx, double ty, double rotDeg = 0, double sc = 1)
            {
                var g = new Canvas { Width = 0, Height = 0 };
                g.RenderTransform = new TransformGroup { Children = { new ScaleTransform(sc, sc), new RotateTransform(rotDeg), new TranslateTransform(tx, ty) } };
                parent.Children.Add(g);
                return g;
            }
            Rectangle RRect(Canvas p, double x, double y, double w, double h, double r, Brush stroke, double thick, Brush? fillBrush = null, bool dashed = false)
            {
                var rc = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Stroke = stroke, StrokeThickness = thick, Fill = fillBrush ?? Brushes.Transparent };
                if (dashed) rc.StrokeDashArray = new DoubleCollection { 5, 5 };
                Canvas.SetLeft(rc, x); Canvas.SetTop(rc, y);
                p.Children.Add(rc);
                return rc;
            }
            Ellipse Ell(Canvas p, double ecx, double ecy, double rx, double ry, Brush stroke, double thick, Brush? fillBrush = null)
            {
                var e = new Ellipse { Width = rx * 2, Height = ry * 2, Stroke = stroke, StrokeThickness = thick, Fill = fillBrush ?? Brushes.Transparent };
                Canvas.SetLeft(e, ecx - rx); Canvas.SetTop(e, ecy - ry);
                p.Children.Add(e);
                return e;
            }
            void Pulse(UIElement el, double from, double seconds, double phase)
            {
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, 1.0, TimeSpan.FromSeconds(seconds))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-phase), EasingFunction = new SineEase() });
            }
            void Slot(Canvas p, double x, double y, double w, double h, double seed)
            {
                RRect(p, x, y, w, h, 6, cyan, 1.2, slotFill);
                var dots = new Canvas { Width = 0, Height = 0 };
                int n = Math.Max(2, (int)(w / 15));
                double cs = h / 2.6;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < 2; j++)
                        Ell(dots, x + w * (i + 0.5) / n, y + h * (0.3 + 0.4 * j), cs * 0.5, cs * 0.5, null!, 0, cyan);
                p.Children.Add(dots);
                Pulse(dots, 0.45, 1.5, seed);
            }
            void Barrel(Canvas parent, double len, double wid, double angDeg, bool ringed, double seed, bool dashedHint)
            {
                var g = Group(parent, 0, 0, angDeg);
                if (dashedHint) { RRect(g, 0, -wid / 2, len, wid, wid * 0.38, cyanDim, 1.3, null, true); return; }
                RRect(g, 0, -wid / 2, len, wid, wid * 0.38, cyan, 1.6, fill);
                foreach (double fx in new[] { 0.18, 0.5, 0.8 })
                {
                    double x = len * fx;
                    g.Children.Add(new Path { Data = Geometry.Parse($"M{x:F1},{-wid / 2:F1} Q{x + 8:F1},0 {x:F1},{wid / 2:F1}"), Stroke = cyanDim, StrokeThickness = 1.2 });
                }
                Slot(g, len * 0.2, -wid * 0.2, len * 0.56, wid * 0.4, seed);
                if (ringed)
                {
                    Ell(g, len - 4, 0, wid * 0.22, wid * 0.5, cyan, 1.6, fill);
                    foreach (double k in new[] { 0.7, 0.4 }) Ell(g, len - 4, 0, wid * 0.22 * k, wid * 0.5 * k, cyan, 1.4);
                }
                else Ell(g, len - 4, 0, wid * 0.2, wid * 0.46, cyanDim, 1.3);
            }

            // ---- Spine: ribbed necks between segments, then the four segments ----
            var V = new[] { (258.0, 440.0, 0.88), (372.0, 408.0, 0.92), (486.0, 376.0, 0.96), (604.0, 342.0, 1.0) };
            for (int i = 0; i < 3; i++)
            {
                double dx = V[i + 1].Item1 - V[i].Item1, dy = V[i + 1].Item2 - V[i].Item2, L = Math.Sqrt(dx * dx + dy * dy);
                var g = Group(body, V[i].Item1, V[i].Item2, Math.Atan2(dy, dx) * 180 / Math.PI);
                RRect(g, 0, -26, L, 52, 24, cyan, 1.6, fill).Opacity = 0.9;
                for (int k = 1; k < 5; k++) Ell(g, L * k / 5, 0, 6, 26, cyanDim, 1.1);
            }
            for (int i = 0; i < 4; i++)
            {
                var g = Group(body, V[i].Item1, V[i].Item2, 0, V[i].Item3);
                Barrel(g, 120, 66, -28.6, false, i + 40, true);
                Barrel(g, 150, 78, 111.7, false, i + 20, false);
                Barrel(g, 158, 84, -146.1, true, i, false);
                Ell(g, 0, 0, 30, 34, cyan, 1.6, fill);
                Ell(g, 0, 0, 15, 17, cyan, 1.4);
                var notch = new Polygon
                {
                    Points = PointCollection.Parse("-24,-30 2,-44 10,-16"), Stroke = new SolidColorBrush(Color.FromRgb(255, 200, 100)), StrokeThickness = 2.4,
                    Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 100), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 },
                };
                g.Children.Add(notch);
                Pulse(notch, 0.55, 1.1, i * 0.4);
            }

            // ---- Head: round, ridged shell; only the top third shows the honeycomb cap ----
            var head = new Canvas { Width = 0, Height = 0 };
            body.Children.Add(head);
            Ell(head, HX, HY, HR, HR, cyan, 1.6, fill);
            var shell = new Canvas { Width = 0, Height = 0, Clip = new EllipseGeometry(new Point(HX, HY), HR, HR) };
            for (int k = 0; k < 7; k++)
            {
                var sg = new StreamGeometry();
                double rx = HR * 1.05 - k * 14, ry = HR * 0.55 - k * 6, ccx = HX - 30 + k * 8, ccy = HY + 60 + k * 10, rot = 0.5;
                using (var gc = sg.Open())
                {
                    for (int s = 0; s <= 24; s++)
                    {
                        double a = 0.3 + (2.6 - 0.3) * s / 24.0;
                        double ex = Math.Cos(a) * rx, ey = Math.Sin(a) * ry;
                        var pt = new Point(ccx + ex * Math.Cos(rot) - ey * Math.Sin(rot), ccy + ex * Math.Sin(rot) + ey * Math.Cos(rot));
                        if (s == 0) gc.BeginFigure(pt, false, false); else gc.LineTo(pt, true, false);
                    }
                }
                sg.Freeze();
                shell.Children.Add(new Path { Data = sg, Stroke = cyanDim, StrokeThickness = 1.2 });
            }
            var cap = new Ellipse
            {
                Width = HR * 1.56, Height = HR * 1.12, Stroke = cyan, StrokeThickness = 1.8,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0.52 * 180 / Math.PI),
            };
            Canvas.SetLeft(cap, HX + NX * HR * 0.34 + NX * 10 - HR * 0.78); Canvas.SetTop(cap, HY + NY * HR * 0.34 + NY * 10 - HR * 0.56);
            shell.Children.Add(cap);
            head.Children.Add(shell);

            // Honeycomb cells, packed inside the cap, bigger toward its centre.
            var cells = new List<(double x, double y, double r)>();
            {
                double ccx = HX + NX * 62, ccy = HY + NY * 62, rx = 104, ry = 82, rot = 0.52;
                for (int tries = 1; cells.Count < 46 && tries < 4000; tries++)
                {
                    double a = Rnd(tries) * 6.28, d = Math.Sqrt(Rnd(tries + 9000)), px = Math.Cos(a) * d, py = Math.Sin(a) * d;
                    double r = Lerp(24, 7, Math.Pow(d, 1.2));
                    double x = ccx + px * rx * Math.Cos(rot) - py * ry * Math.Sin(rot), y = ccy + px * rx * Math.Sin(rot) + py * ry * Math.Cos(rot);
                    if (Math.Sqrt((x - HX) * (x - HX) + (y - HY) * (y - HY)) > HR - r - 4) continue;
                    bool ok = true;
                    foreach (var c in cells) if (Math.Sqrt((c.x - x) * (c.x - x) + (c.y - y) * (c.y - y)) < c.r + r + 2) { ok = false; break; }
                    if (ok) cells.Add((x, y, r));
                }
            }
            int ci = 0;
            foreach (var c in cells)
            {
                var brush = new RadialGradientBrush(Color.FromArgb(0x90, 190, 255, 255), Color.FromArgb(0x20, 0, 160, 220));
                var cellEl = Ell(head, c.x, c.y, c.r, c.r, cyan, 1.2, brush);
                Pulse(cellEl, 0.5, 1.9, Rnd(ci * 3 + 7) * 6);
                ci++;
            }

            // Fins where the head meets the spine.
            var fins = new Canvas { Width = 0, Height = 0 };
            Ell(fins, HX - HR * 0.96, HY + HR * 0.28, 26, 62, cyan, 1.6, fill).RenderTransformOrigin = new Point(0.5, 0.5);
            for (int k = 0; k < 6; k++)
                fins.Children.Add(new Line { X1 = HX - HR * 0.98 - 14, Y1 = HY + HR * 0.28 - 48 + k * 18, X2 = HX - HR * 0.98 + 10, Y2 = HY + HR * 0.28 - 40 + k * 16, Stroke = cyan, StrokeThickness = 1.6 });
            head.Children.Add(fins);

            // Scan line sweeping down over everything.
            var scan = new Rectangle
            {
                Width = W, Height = 80,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 95, 230, 255), 0),
                    new GradientStop(Color.FromArgb(0x38, 95, 230, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 95, 230, 255), 1),
                }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
            };
            var scanT = new TranslateTransform(0, -80);
            scan.RenderTransform = scanT;
            scanT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-80, H, TimeSpan.FromSeconds(3.7)) { RepeatBehavior = RepeatBehavior.Forever });
            host.Children.Add(scan);
            return host;
        }

        // Mining location signal: a scanned patch of terrain as a violet hologram wireframe, with a numbered beacon for every
        // mining site on the body (the journal gives only the COUNT, so the beacons use a fixed pattern for that many, not real
        // positions). Pulsing rings, a scan line sweeping down. Same card layout as the sensor: 1000 x 640 space, scaled.
        private static Canvas MakeMiningSiteScanIllustration(double cx, double cy, double scale, int siteCount)
        {
            const double W = 1000, H = 640;
            int count = Math.Clamp(siteCount <= 0 ? 8 : siteCount, 1, 16);
            var violet = Color.FromRgb(190, 130, 255);
            var line = new SolidColorBrush(Color.FromArgb(0x80, violet.R, violet.G, violet.B));
            var bright = new SolidColorBrush(Color.FromArgb(0xf2, violet.R, violet.G, violet.B));
            double Rnd(double i) { double x = Math.Sin(i * 127.1 + 311.7) * 43758.5453; return x - Math.Floor(x); }

            var host = new Canvas { Width = W, Height = H, IsHitTestVisible = false, Clip = new RectangleGeometry(new Rect(0, 0, W, H)) };
            host.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(cx - W * scale / 2, cy - H * scale / 2) } };
            host.Children.Add(new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromRgb(5, 9, 16)), Stroke = new SolidColorBrush(Color.FromArgb(0x88, violet.R, violet.G, violet.B)), StrokeThickness = 3 });
            var gridGeo = new StreamGeometry();
            using (var gc = gridGeo.Open())
            {
                for (int x = 40; x < W; x += 40) { gc.BeginFigure(new Point(x, 0), false, false); gc.LineTo(new Point(x, H), true, false); }
                for (int y = 40; y < H; y += 40) { gc.BeginFigure(new Point(0, y), false, false); gc.LineTo(new Point(W, y), true, false); }
            }
            gridGeo.Freeze();
            host.Children.Add(new Path { Data = gridGeo, Stroke = new SolidColorBrush(Color.FromArgb(0x10, violet.R, violet.G, violet.B)), StrokeThickness = 1 });
            var glowEl = new Ellipse { Width = 760, Height = 760, Fill = new RadialGradientBrush(Color.FromArgb(0x2a, 150, 80, 255), Color.FromArgb(0, 150, 80, 255)) };
            Canvas.SetLeft(glowEl, 120); Canvas.SetTop(glowEl, 60);
            host.Children.Add(glowEl);

            // Terrain mesh in a simple perspective.
            Point Proj(double x, double y, double z) => new Point(500 + (x - 0.5) * (300 + y * 620), 250 + y * 330 - z);
            double Hgt(double x, double y) => 26 * Math.Sin(x * 9 + 1) * Math.Cos(y * 7) + 14 * Math.Sin(x * 21 + y * 13);
            const int NX = 26, NY = 16;
            for (int j = 0; j <= NY; j++)
            {
                var sg = new StreamGeometry();
                using (var gc = sg.Open())
                    for (int i = 0; i <= NX; i++)
                    {
                        var p = Proj(i / (double)NX, j / (double)NY, Hgt(i / (double)NX, j / (double)NY));
                        if (i == 0) gc.BeginFigure(p, false, false); else gc.LineTo(p, true, false);
                    }
                sg.Freeze();
                host.Children.Add(new Path { Data = sg, Stroke = line, StrokeThickness = 1.2, Opacity = 0.25 + 0.6 * j / NY });
            }
            var vg = new StreamGeometry();
            using (var gc = vg.Open())
                for (int i = 0; i <= NX; i++)
                    for (int j = 0; j <= NY; j++)
                    {
                        var p = Proj(i / (double)NX, j / (double)NY, Hgt(i / (double)NX, j / (double)NY));
                        if (j == 0) gc.BeginFigure(p, false, false); else gc.LineTo(p, true, false);
                    }
            vg.Freeze();
            host.Children.Add(new Path { Data = vg, Stroke = line, StrokeThickness = 1.2, Opacity = 0.5 });

            // Site beacons: back to front.
            var pattern = new[] { (0.18, 0.30), (0.42, 0.20), (0.70, 0.28), (0.86, 0.50), (0.58, 0.50), (0.30, 0.58), (0.14, 0.82), (0.62, 0.84) };
            var sites = new (double x, double y, int n)[count];
            for (int i = 0; i < count; i++)
                sites[i] = i < pattern.Length ? (pattern[i].Item1, pattern[i].Item2, i + 1) : (0.1 + 0.8 * Rnd(i * 3), 0.18 + 0.7 * Rnd(i * 3 + 1), i + 1);
            Array.Sort(sites, (a, b) => a.y.CompareTo(b.y));
            foreach (var s in sites)
            {
                var b0 = Proj(s.x, s.y, Hgt(s.x, s.y));
                double hh = 150 + 60 * s.y;
                var beam = new Rectangle
                {
                    Width = 8, Height = hh,
                    Fill = new LinearGradientBrush(Color.FromArgb(0, 210, 160, 255), Color.FromArgb(0xb0, 210, 160, 255), 90),
                };
                Canvas.SetLeft(beam, b0.X - 4); Canvas.SetTop(beam, b0.Y - hh);
                host.Children.Add(beam);
                beam.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1.0, TimeSpan.FromSeconds(1.6))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-s.n * 0.5), EasingFunction = new SineEase() });
                for (int k = 0; k < 2; k++)
                {
                    var ring = new Ellipse
                    {
                        Width = 112, Height = 28, Stroke = bright, StrokeThickness = 1.8,
                        RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.2, 0.2),
                    };
                    Canvas.SetLeft(ring, b0.X - 56); Canvas.SetTop(ring, b0.Y - 14);
                    host.Children.Add(ring);
                    var begin = TimeSpan.FromSeconds(-(k * 1.0 + s.n * 0.27));
                    var dur = TimeSpan.FromSeconds(2.0);
                    ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.2, 1.0, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
                    ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1.0, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
                    ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, 0.0, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
                }
                var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0xf0, 0xdc, 0xff)) };
                Canvas.SetLeft(dot, b0.X - 6); Canvas.SetTop(dot, b0.Y - hh - 6);
                host.Children.Add(dot);
                var num = new TextBlock { Text = s.n.ToString(), Width = 40, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 24, Foreground = new SolidColorBrush(Color.FromRgb(0xeb, 0xd2, 0xff)) };
                Canvas.SetLeft(num, b0.X - 20); Canvas.SetTop(num, b0.Y - hh - 38);
                host.Children.Add(num);
            }

            host.Children.Add(new TextBlock
            {
                Text = count == 1 ? "1 MINING SITE" : $"{count} MINING SITES", FontFamily = new FontFamily("Consolas"), FontSize = 30,
                Foreground = new SolidColorBrush(Color.FromRgb(0xeb, 0xd2, 0xff)),
                RenderTransform = new TranslateTransform(40, 30),
            });

            var scan = new Rectangle
            {
                Width = W, Height = 80,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, violet.R, violet.G, violet.B), 0),
                    new GradientStop(Color.FromArgb(0x34, violet.R, violet.G, violet.B), 0.5),
                    new GradientStop(Color.FromArgb(0, violet.R, violet.G, violet.B), 1),
                }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
            };
            var scanT = new TranslateTransform(0, -80);
            scan.RenderTransform = scanT;
            scanT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-80, H, TimeSpan.FromSeconds(3.3)) { RepeatBehavior = RepeatBehavior.Forever });
            host.Children.Add(scan);
            return host;
        }
    }
}
