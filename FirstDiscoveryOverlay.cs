using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The First Discovery moment: plays over the STAR tab on arrival in a system nobody had discovered. A generic system map (orbits
    // drawing in, bodies circling, drifting and slowly zooming) with rings pulsing out of the star, corner brackets locking on, then
    // the title spreading in over the system name. About 4 seconds, then it fades and the tab underneath is back. Drawn frame by
    // frame from one clock, so the title's letter-spacing animation (which a TextBlock cannot do) stays in step with the rest.
    public sealed class FirstDiscoveryVisual : FrameworkElement
    {
        // The build-up runs 20% slower than first drawn (Slow), then holds for four more seconds before the final fade.
        private const double Slow = 1.2;
        private const double FadeOutAt = 3.3 * Slow + 4.0;           // seconds
        private const double Duration = FadeOutAt + 0.6 * Slow;      // seconds, including the final fade

        private static readonly Brush Backdrop   = Frozen(Color.FromRgb(0x08, 0x0d, 0x0d));
        private static readonly Brush TitleStrip = Frozen(Color.FromArgb(0xd9, 0x08, 0x0d, 0x0d));
        private static readonly Brush Amber      = Frozen(Color.FromRgb(0xf2, 0xb8, 0x4b));
        private static readonly Brush Pale       = Frozen(Color.FromRgb(0xc9, 0xd6, 0xdc));
        private static readonly Brush Body       = Frozen(Color.FromRgb(0x9f, 0xd8, 0xd4));
        private static readonly Brush StarFill   = Frozen(Color.FromRgb(0xe8, 0xf4, 0xf2));
        private static readonly Pen GridPen   = FrozenPen(Color.FromRgb(0x11, 0x20, 0x26), 1);
        private static readonly Pen OrbitPen  = FrozenPen(Color.FromRgb(0x2f, 0x6a, 0x70), 1.4);
        private static readonly Pen RingPen   = FrozenPen(Color.FromRgb(0x3f, 0xd0, 0xc9), 1.6);
        private static readonly Pen CrossPen  = FrozenPen(Color.FromRgb(0x3f, 0xd0, 0xc9), 1);
        private static readonly Pen BracketPen = FrozenPen(Color.FromRgb(0xf2, 0xb8, 0x4b), 2.2);

        // Orbit radius (fraction of the map size) and angular speed of the body riding it.
        private static readonly (double r, double speed)[] Orbits =
            { (.12, .9), (.22, .7), (.36, .55), (.52, .4), (.72, .3), (.95, .22) };

        private static readonly Typeface TitleFace = new(AppFonts.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface SubFace   = new(AppFonts.Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private readonly Stopwatch _clock = new();
        private bool _running;
        private string _subtitle = "";

        public event Action? Finished;

        public FirstDiscoveryVisual() { IsHitTestVisible = false; }

        public void Start(string systemName, string starClass)
        {
            _subtitle = starClass.Length > 0 ? $"{systemName}  ·  {starClass}" : systemName;
            _subtitle = _subtitle.ToUpperInvariant();
            _clock.Restart();
            if (!_running) { _running = true; CompositionTarget.Rendering += OnFrame; }
            InvalidateVisual();
        }

        public void Stop()
        {
            if (_running) { CompositionTarget.Rendering -= OnFrame; _running = false; }
            _clock.Stop();
            InvalidateVisual();
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            if (_clock.Elapsed.TotalSeconds >= Duration) { Stop(); Finished?.Invoke(); return; }
            InvalidateVisual();
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double w) { var p = new Pen(Frozen(c), w); p.Freeze(); return p; }

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        private static double EaseOut(double x) { x = Clamp01(x); return 1 - Math.Pow(1 - x, 3); }
        // Fraction (0..1) of an animation that starts at `start` and lasts `len`.
        private static double Phase(double t, double start, double len) => Clamp01((t - start) / len);

        protected override void OnRender(DrawingContext dc)
        {
            if (!_running) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            double real = _clock.Elapsed.TotalSeconds;
            double fadeOut = real > FadeOutAt ? Clamp01(1 - (real - FadeOutAt) / (Duration - FadeOutAt)) : 1;
            double t = real / Slow;   // animation time: every phase below is written in the original timings
            double env = Math.Min(1, t / 0.5) * fadeOut;
            if (env <= 0) return;
            double k = Math.Max(0.8, Math.Min(w, h) / 300);   // sizes below are written for a ~300 px panel

            dc.PushOpacity(env);
            dc.DrawRectangle(Backdrop, null, new Rect(0, 0, w, h));
            dc.Pop();

            // Faint grid.
            dc.PushOpacity(env);
            double step = 34 * k;
            for (double x = 0; x < w; x += step) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            for (double y = 0; y < h; y += step) dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            dc.Pop();

            DrawMap(dc, w, h, k, t, env);
            DrawRings(dc, w, h, t);
            DrawBrackets(dc, w, h, k, t, fadeOut);
            DrawTitle(dc, w, h, k, t, fadeOut);
        }

        private void DrawMap(DrawingContext dc, double w, double h, double k, double t, double env)
        {
            double zoom = 0.8 + 0.3 * (t / 4);
            double cx = w / 2 - 14 * k * t, cy = h / 2;
            double baseR = Math.Min(w, h * 1.4) * 0.72;
            const double tilt = -0.3, flat = 0.42;
            double ct = Math.Cos(tilt), st = Math.Sin(tilt);
            Point At(double r, double a)
            {
                double x = Math.Cos(a) * r * zoom, y = Math.Sin(a) * r * zoom * flat;
                return new Point(cx + x * ct - y * st, cy + x * st + y * ct);
            }

            dc.PushOpacity(env);
            for (int i = 0; i < Orbits.Length; i++)
            {
                double r = Orbits[i].r * baseR;
                double p = Phase(t, 0.1 + i * 0.12, 1.1);
                if (p <= 0) continue;
                int n = Math.Max(2, (int)(120 * p));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(At(r, 0), false, false);
                    for (int s = 1; s <= n; s++) ctx.LineTo(At(r, Math.PI * 2 * p * s / n), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, OrbitPen, g);
                if (p >= 1)
                {
                    double a = t * Orbits[i].speed * 0.9 + i * 1.7;
                    double br = (i % 2 == 1 ? 2.6 : 3.4) * k;
                    dc.DrawEllipse(i == 3 ? Amber : Body, null, At(r, a), br, br);
                }
            }
            var c = new Point(cx, cy);
            dc.DrawEllipse(StarFill, null, c, 4 * k, 4 * k);
            dc.DrawLine(CrossPen, new Point(cx - 10 * k, cy), new Point(cx - 6 * k, cy));
            dc.DrawLine(CrossPen, new Point(cx + 6 * k, cy), new Point(cx + 10 * k, cy));
            dc.DrawLine(CrossPen, new Point(cx, cy - 10 * k), new Point(cx, cy - 6 * k));
            dc.DrawLine(CrossPen, new Point(cx, cy + 6 * k), new Point(cx, cy + 10 * k));
            dc.Pop();
        }

        private static void DrawRings(DrawingContext dc, double w, double h, double t)
        {
            double maxR = Math.Min(w, h) * 0.65;
            foreach (double start in new[] { 0.1, 0.4 })
            {
                double p = Phase(t, start, 1.4);
                if (p <= 0 || p >= 1) continue;
                double r = 10 + (maxR - 10) * EaseOut(p);
                dc.PushOpacity(0.9 * (1 - EaseOut(p)));
                dc.DrawEllipse(null, RingPen, new Point(w / 2, h / 2), r, r);
                dc.Pop();
            }
        }

        private static void DrawBrackets(DrawingContext dc, double w, double h, double k, double t, double fadeOut)
        {
            double p = Phase(t, 0.5, 0.7);
            if (p <= 0) return;
            double e = EaseOut(p);
            double scale = 2.2 - 1.2 * e;
            double size = 22 * k, inset = 16 * k, half = size / 2 * scale;
            dc.PushOpacity(e * fadeOut);
            // (box centre, horizontal direction, vertical direction) per corner: the L opens toward the panel's centre.
            foreach (var (bx, by, dx, dy) in new[]
            {
                (inset + size / 2, inset + size / 2, 1.0, 1.0), (w - inset - size / 2, inset + size / 2, -1.0, 1.0),
                (inset + size / 2, h - inset - size / 2, 1.0, -1.0), (w - inset - size / 2, h - inset - size / 2, -1.0, -1.0),
            })
            {
                var corner = new Point(bx - dx * half, by - dy * half);
                dc.DrawLine(BracketPen, corner, new Point(corner.X + dx * 2 * half, corner.Y));
                dc.DrawLine(BracketPen, corner, new Point(corner.X, corner.Y + dy * 2 * half));
            }
            dc.Pop();
        }

        private void DrawTitle(DrawingContext dc, double w, double h, double k, double t, double fadeOut)
        {
            double pt = Phase(t, 1.1, 0.9), ps = Phase(t, 1.7, 0.6);
            if (pt <= 0) return;
            double ease = EaseOut(pt);
            double titleSize = 17 * k, subSize = 12 * k;
            double titleY = h * 0.40, subY = titleY + titleSize + 18 * k;

            dc.PushOpacity(ease * fadeOut);
            dc.DrawRectangle(TitleStrip, null, new Rect(0, titleY - 14 * k, w, titleSize + subSize + 52 * k));
            dc.Pop();

            DrawSpaced(dc, "FIRST DISCOVERY", TitleFace, titleSize, Amber, w / 2, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
            if (ps > 0) DrawSpaced(dc, _subtitle, SubFace, subSize, Pale, w / 2, subY, 0.14, ps * fadeOut);
        }

        // Centred text with an explicit gap (in em) between the letters; FormattedText has no letter-spacing of its own.
        private void DrawSpaced(DrawingContext dc, string text, Typeface face, double size, Brush brush, double centreX, double y, double spacingEm, double opacity)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var glyphs = new FormattedText[text.Length];
            double total = 0;
            for (int i = 0; i < text.Length; i++)
            {
                glyphs[i] = new FormattedText(text[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, dpi);
                total += glyphs[i].WidthIncludingTrailingWhitespace + (i < text.Length - 1 ? spacingEm * size : 0);
            }
            double x = centreX - total / 2;
            dc.PushOpacity(opacity);
            for (int i = 0; i < text.Length; i++)
            {
                dc.DrawText(glyphs[i], new Point(x, y));
                x += glyphs[i].WidthIncludingTrailingWhitespace + spacingEm * size;
            }
            dc.Pop();
        }
    }

    public partial class MainWindow
    {
        private void InitFirstDiscovery(EliteWatcherService svc)
        {
            firstDiscoveryVisual.Finished += () => firstDiscoveryLayer.Visibility = Visibility.Collapsed;
            // Raised on the journal thread, after the arrival star scan has already queued its tab refresh.
            svc.SystemFirstDiscovered += (system, starType) => Dispatcher.InvokeAsync(() => PlayFirstDiscovery(system, starType));
        }

        // Plays over the STAR tab, which the arrival scan has just brought up. If another tab is showing (or the window is hidden), nobody
        // would see it, so it is skipped rather than queued.
        private void PlayFirstDiscovery(string system, string starType)
        {
            if (starPanelViewbox.Visibility != Visibility.Visible || !IsVisible || WindowState == WindowState.Minimized)
            {
                Log.Write($"First discovery of '{system}' not shown (star tab not showing)");
                return;
            }
            firstDiscoveryLayer.Visibility = Visibility.Visible;
            firstDiscoveryVisual.Start(system, starType.Length > 0 ? StarClassNames.GetDisplayName(starType) : "");
        }
    }
}
