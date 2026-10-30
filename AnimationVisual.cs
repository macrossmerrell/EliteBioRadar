using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Common base of the full-area animations (First discovery, Landed, Docked): one clock, one frame loop, a Finished event, and a few drawing helpers. A visual draws
    // itself in OnRender each frame while it is running and is invisible otherwise; the animation surfaces (see AnimationRouter) host one of each and start the right one.
    // Start takes the animation's own text: a subject (system, body or station name) and a detail whose meaning belongs to the subclass.
    public abstract class AnimationVisual : FrameworkElement
    {
        private readonly Stopwatch _clock = new();
        private bool _running;

        public event Action? Finished;

        // How much of what lies underneath is hidden while it plays (1 = fully covered); set from the animation's catalog entry.
        public double BackdropOpacity { get; set; } = 1.0;

        // Seconds from Start to the end of the final fade.
        protected abstract double Duration { get; }
        // The look the full-area animations share.
        protected static readonly Typeface MonoFace = new(AppFonts.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        protected static readonly Brush TitleBrush = Frozen(Color.FromRgb(0xf2, 0xb8, 0x4b));
        protected static readonly Brush SubjectBrush = Frozen(Color.FromRgb(0xff, 0xd9, 0xa0));
        protected static readonly Brush DimBrush = Frozen(Color.FromRgb(0x5a, 0xa8, 0xb0));
        protected static readonly Brush WhiteBrush = Frozen(Color.FromRgb(0xff, 0xff, 0xff));
        protected static readonly Brush GreenBrush = Frozen(Color.FromRgb(0x7d, 0xff, 0xb0));
        protected static readonly Brush StripBrush = Frozen(Color.FromArgb(0xd9, 0x08, 0x0d, 0x0d));

        // Timing and layout shared by the scenes drawn on a ~300 x 210 local canvas scaled to fit: the final fade, the opening fade-in, the scale u and the local size.
        protected readonly record struct Scene(double FadeOut, double Env, double U, double W, double H);
        protected Scene LayoutScene(double w, double h, double t, double fadeOutAt)
        {
            double fadeOut = t > fadeOutAt ? Clamp01(1 - (t - fadeOutAt) / (Duration - fadeOutAt)) : 1;
            double u = Math.Max(0.8, Math.Min(h / 210.0, w / 300.0));
            return new Scene(fadeOut, Math.Min(1, t / 0.35) * fadeOut, u, w / u, h / u);
        }


        protected double Elapsed => _clock.Elapsed.TotalSeconds;

        protected AnimationVisual()
        {
            IsHitTestVisible = false; ClipToBounds = true;
            // CompositionTarget.Rendering is static, so a visual whose window closes (or that gets hidden) mid-play would be kept alive and redrawn forever,
            // and its lane would never be released. Treat losing the screen as the end of the animation.
            Unloaded += (_, _) => Abandon();
            IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) Abandon(); };
        }

        private void Abandon()
        {
            if (!_running) return;
            Stop();
            Finished?.Invoke();
        }

        protected virtual void OnStart(string subject, string detail) { }

        public void Start(string subject, string detail)
        {
            OnStart(subject, detail);
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
            if (Elapsed >= Duration) { Stop(); Finished?.Invoke(); return; }
            InvalidateVisual();
        }

        protected sealed override void OnRender(DrawingContext dc)
        {
            if (!_running) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            Render(dc, w, h, Elapsed);
        }

        // Draw one frame; `real` is the seconds since Start.
        protected abstract void Render(DrawingContext dc, double w, double h, double real);

        protected static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        protected static Pen FrozenPen(Color c, double w) { var p = new Pen(Frozen(c), w); p.Freeze(); return p; }

        protected static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        protected static double EaseOut(double x) { x = Clamp01(x); return 1 - Math.Pow(1 - x, 3); }
        // Fraction (0..1) of an animation that starts at `start` and lasts `len`.
        protected static double Phase(double t, double start, double len) => Clamp01((t - start) / len);
        protected static double Smooth(double a, double b, double x) { double t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        protected static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Centred text with an explicit gap (in em) between the letters; FormattedText has no letter-spacing of its own.
        protected void DrawSpaced(DrawingContext dc, string text, Typeface face, double size, Brush brush, double centreX, double y, double spacingEm, double opacity)
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

        // One run of text, left / centre / right aligned on x, y being the top of the line.
        protected void DrawText(DrawingContext dc, string text, Typeface face, double size, Brush brush, double x, double y, double opacity, TextAlignment align = TextAlignment.Center)
        {
            if (opacity <= 0 || text.Length == 0) return;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double left = align == TextAlignment.Center ? x - ft.Width / 2 : align == TextAlignment.Right ? x - ft.Width : x;
            dc.PushOpacity(opacity);
            dc.DrawText(ft, new Point(left, y));
            dc.Pop();
        }

        // Up to three label-over-value stats in a row centred on centreX, each slot as wide as its text (plus a gap), so long values never run into their
        // neighbours; if the row is still wider than maxWidth the whole row is drawn smaller. They fade in one after another from `start`.
        protected void DrawStats(DrawingContext dc, (string label, string value)[] stats, Typeface face, double centreX, double y, double maxWidth, double t, double start, double fadeOut)
        {
            int n = Math.Min(stats.Length, 3);
            if (n == 0) return;
            const double gap = 26, labelSize = 9, valueSize = 12;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var widths = new double[n];
            double total = gap * (n - 1);
            for (int i = 0; i < n; i++)
            {
                double lw = new FormattedText(stats[i].label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, labelSize, Brushes.White, dpi).Width;
                double vw = new FormattedText(stats[i].value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, valueSize, Brushes.White, dpi).Width;
                widths[i] = Math.Max(lw, vw);
                total += widths[i];
            }
            double k = total > maxWidth ? maxWidth / total : 1;
            double x = centreX - total * k / 2;
            for (int i = 0; i < n; i++)
            {
                double a = Phase(t, start + i * 0.3, 0.5) * fadeOut;
                double slot = widths[i] * k, mid = x + slot / 2;
                dc.PushTransform(new ScaleTransform(k, k, mid, y));
                DrawText(dc, stats[i].label, face, labelSize, DimBrush, mid, y, a * 0.85);
                DrawText(dc, stats[i].value, face, valueSize, Brushes.White, mid, y + 12, a);
                dc.Pop();
                x += slot + gap * k;
            }
        }


        // "LABEL=value|LABEL=value": the stats lines the animations show under their title. Entries without a value are dropped.
        protected static (string label, string value)[] ParseStats(string detail)
        {
            var list = new System.Collections.Generic.List<(string, string)>();
            foreach (var part in detail.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1) continue;
                list.Add((part[..eq].Trim().ToUpperInvariant(), part[(eq + 1)..].Trim()));
            }
            return list.ToArray();
        }
    }
}
