using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    // The descent ("deorbit") scene: concept B, "terrain lock dive". The real planet render IS the ground, seen
    // as a big horizon that flattens and rises as the ship comes down; the sky and the haze along the horizon
    // take the colour of the planet's own atmosphere; a neon corridor of gates leads to a glowing landing pad
    // that a lock-on reticle closes in on. Everything is driven by the real descent progress (descentT, 0..1,
    // log-scaled altitude) - see UpdateDeorbitPanel.
    public partial class MainWindow
    {
        private const double DescentW = 620, DescentH = 580, DescentCx = 310;

        // The planet surface persists across frames (the deorbit canvas is rebuilt every tick): rebuilding the shader
        // each frame would restart its animation and cost far too much.
        private FrameworkElement? _descentSurface;
        private string _descentSurfaceBody = "";
        private LandableWorldShaderEffect? _descentEffect;        // set when the GPU shader draws the surface
        private double _descentFrameCx, _descentFrameCy, _descentFrameR0;   // CPU fallback: sphere centre/radius inside its bitmap
        private double _descentGatePhase, _descentGridPhase, _descentSpin;

        private static readonly Color DescentCyan = Color.FromRgb(0x55, 0xf2, 0xff);
        private static readonly Color DescentPink = Color.FromRgb(0xff, 0x6f, 0xd2);

        private static double DLerp(double a, double b, double t) => a + (b - a) * t;
        private static double DSmooth(double a, double b, double x)
        {
            double t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }
        private static Color DMix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }
        private static Color DAlpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

        // Sky and horizon-haze colours for the body's atmosphere (dominant gas), and how much atmosphere there is at all
        // (0 = airless: black sky, no haze). The gas colours follow the app's other planet looks; thick atmospheres
        // (surface pressure) show more of it.
        internal static (Color sky, Color haze, double density) DescentAtmosphere(BodyScanDetail? d)
        {
            string a = d?.AtmosphereType ?? "";
            if (string.IsNullOrEmpty(a) || a.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var comp = d?.AtmosphereComposition;
                if (comp != null && comp.Count > 0) a = comp.OrderByDescending(x => x.Percent).First().Name ?? "";
            }
            bool Has(string s) => a.Contains(s, StringComparison.OrdinalIgnoreCase);
            Color sky, haze;
            if (Has("EarthLike") || Has("Oxygen") && !Has("Ammonia")) { sky = Color.FromRgb(70, 140, 230);  haze = Color.FromRgb(150, 205, 255); }
            else if (Has("SulphurDioxide") || Has("Sulphur")) { sky = Color.FromRgb(150, 130, 40);  haze = Color.FromRgb(238, 216, 92); }
            else if (Has("CarbonDioxide") || Has("Carbon"))    { sky = Color.FromRgb(150, 92, 56);   haze = Color.FromRgb(236, 162, 92); }
            else if (Has("Ammonia"))                           { sky = Color.FromRgb(130, 98, 48);   haze = Color.FromRgb(226, 172, 88); }
            else if (Has("Methane"))                           { sky = Color.FromRgb(58, 140, 120);  haze = Color.FromRgb(132, 218, 186); }
            else if (Has("Water"))                             { sky = Color.FromRgb(110, 160, 215); haze = Color.FromRgb(192, 226, 250); }
            else if (Has("Neon"))                              { sky = Color.FromRgb(170, 92, 150);  haze = Color.FromRgb(242, 164, 222); }
            else if (Has("Argon"))                             { sky = Color.FromRgb(150, 140, 112); haze = Color.FromRgb(232, 224, 192); }
            else if (Has("Helium") || Has("Hydrogen"))         { sky = Color.FromRgb(120, 132, 156); haze = Color.FromRgb(204, 218, 238); }
            else if (Has("Nitrogen"))                          { sky = Color.FromRgb(90, 112, 164);  haze = Color.FromRgb(172, 192, 232); }
            else                                               { sky = Color.FromRgb(100, 120, 140); haze = Color.FromRgb(180, 200, 220); }

            double p = d?.SurfacePressure ?? 0;
            // 100 Pa (thin) -> faint, 1 atm -> full.
            double density = p <= 0 ? 0 : Math.Clamp((Math.Log10(p) - 2.0) / 3.0, 0.15, 1.0);
            return (sky, haze, density);
        }

        // The planet render: the GPU landable-world shader when it can draw this body (animated, and re-aimed every frame
        // by changing its centre/radius, so it stays sharp at any zoom), otherwise the static CPU frame scaled up.
        private void EnsureDescentSurface(BodyScanDetail body, string iconCode)
        {
            if (_descentSurface != null && string.Equals(_descentSurfaceBody, body.BodyName, StringComparison.OrdinalIgnoreCase)) return;
            _descentSurfaceBody = body.BodyName;
            _descentSurface = null;
            _descentEffect = null;
            try
            {
                if (PlanetRenderer.IsLandableWorld(body, iconCode) && (System.Windows.Media.RenderCapability.Tier >> 16) != 0)
                {
                    var look = PlanetRenderer.GetLandableWorldLook(body, iconCode);
                    look.AtmGlow = 0;   // the haze is drawn separately, in the atmosphere's own colour
                    var effect = LandableWorldShaderEffect.Create(look, new Point(0.5, 3), new Point(2, 2));
                    effect.BeginAnimation(LandableWorldShaderEffect.TimeProperty,
                        new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                    _descentEffect = effect;
                    _descentSurface = new Rectangle { Width = DescentW, Height = DescentH, Fill = Brushes.Black, Effect = effect, IsHitTestVisible = false };
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Descent shader setup failed, using CPU render: {ex.Message}");
                _descentEffect = null;
            }
            const int imgSize = 1000;
            var frame = PlanetRenderer.GetTerrainSceneFrame(body, iconCode, imgSize, imgSize, _watcher?.SystemPopulation ?? 0);
            var (fcx, fcy, fr) = PlanetRenderer.GetTerrainGeometry(imgSize, imgSize, body);
            _descentFrameCx = fcx; _descentFrameCy = fcy; _descentFrameR0 = fr;
            _descentSurface = new Image { Width = imgSize, Height = imgSize, Source = frame, IsHitTestVisible = false };
        }

        private static PointCollection DescentHex(double cx, double cy, double rx, double ry, double rot)
        {
            var pts = new PointCollection();
            for (int k = 0; k < 6; k++)
            {
                double a = rot + k * Math.PI / 3;
                pts.Add(new Point(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry));
            }
            return pts;
        }

        private void DrawDescentScene(Canvas c, BodyScanDetail body, string iconCode, double e, double dt, double dev, double sinkRate)
        {
            const double W = DescentW, H = DescentH, CX = DescentCx;
            var (skyC, hazeC, density) = DescentAtmosphere(body);
            double skyAmt = density * DSmooth(0.10, 0.85, e);
            double hazeAmt = density * (0.45 + 0.55 * DSmooth(0.0, 0.6, e));

            // Horizon rises and the planet flattens as the descent proceeds.
            double ease = 1 - Math.Pow(1 - e, 2);
            double hy = DLerp(H * 0.80, H * 0.40, ease);
            double R = W * DLerp(0.95, 1.7, Math.Pow(e, 0.9));
            double cyp = hy + R;

            _descentGatePhase = (_descentGatePhase + dt * (0.10 + 0.28 * e)) % 1.0;
            _descentGridPhase = (_descentGridPhase + dt * DLerp(0.03, 0.11, e)) % 1.0;
            _descentSpin = (_descentSpin + dt * 34) % 360;

            // ---- sky + stars ----
            var skyTop = Color.FromRgb(3, 6, 12);
            c.Children.Add(new Rectangle
            {
                Width = W, Height = H, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(skyTop, 0.0),
                    new GradientStop(DMix(skyTop, DMix(skyC, Colors.Black, 0.55), skyAmt), Math.Clamp((hy - 40) / H, 0.05, 0.9)),
                    new GradientStop(DMix(skyTop, hazeC, skyAmt * 0.80), Math.Clamp(hy / H, 0.1, 0.95)),
                    new GradientStop(skyTop, 1.0),
                }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
            });
            double starAlpha = (1 - skyAmt * 0.92) * 0.85;
            if (starAlpha > 0.03)
            {
                var sg = new StreamGeometry();
                using (var gc = sg.Open())
                {
                    for (int i = 0; i < 60; i++)
                    {
                        double sx = Frac(Math.Sin(i * 12.9898) * 43758.5453) * W;
                        double sy = Frac(Math.Sin(i * 78.233 + 4.1) * 43758.5453) * Math.Max(20, hy - 20);
                        double sz = 0.8 + Frac(Math.Sin(i * 39.346) * 43758.5453) * 1.1;
                        gc.BeginFigure(new Point(sx, sy), true, true);
                        gc.LineTo(new Point(sx + sz, sy), false, false);
                        gc.LineTo(new Point(sx + sz, sy + sz), false, false);
                        gc.LineTo(new Point(sx, sy + sz), false, false);
                    }
                }
                sg.Freeze();
                c.Children.Add(new Path { Data = sg, Fill = new SolidColorBrush(DAlpha(Color.FromRgb(210, 228, 255), starAlpha)), IsHitTestVisible = false });
            }

            // ---- the planet render is the surface ----
            EnsureDescentSurface(body, iconCode);
            if (_descentSurface != null)
            {
                if (_descentSurface.Parent is Panel oldParent) oldParent.Children.Remove(_descentSurface);
                if (_descentEffect != null)
                {
                    _descentEffect.SetValue(LandableWorldShaderEffect.CenterProperty, new Point(0.5, cyp / H));
                    _descentEffect.SetValue(LandableWorldShaderEffect.RadiusProperty, new Point(R / W, R / H));
                }
                else
                {
                    double k = R / Math.Max(1, _descentFrameR0);
                    Canvas.SetLeft(_descentSurface, CX - _descentFrameCx);
                    Canvas.SetTop(_descentSurface, cyp - _descentFrameCy);
                    _descentSurface.RenderTransform = new ScaleTransform(k, k, _descentFrameCx, _descentFrameCy);
                }
                c.Children.Add(_descentSurface);
            }

            // ---- atmosphere: horizon haze, glow and limb in the planet's own colour ----
            if (hazeAmt > 0.01)
            {
                c.Children.Add(new Rectangle
                {
                    Width = W, Height = 300, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(0, hy - 70),
                    Fill = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(DAlpha(hazeC, 0), 0.0),
                        new GradientStop(DAlpha(hazeC, 0.60 * hazeAmt), 70.0 / 300),
                        new GradientStop(DAlpha(hazeC, 0.16 * hazeAmt), 0.55),
                        new GradientStop(DAlpha(hazeC, 0), 1.0),
                    }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
                });
                var glowRect = new Ellipse
                {
                    Width = 1000, Height = 320, IsHitTestVisible = false,
                    Fill = new RadialGradientBrush(DAlpha(hazeC, 0.45 * hazeAmt), DAlpha(hazeC, 0)),
                };
                Canvas.SetLeft(glowRect, CX - 500); Canvas.SetTop(glowRect, hy - 170);
                c.Children.Add(glowRect);
                var limb = LimbGeometry(CX, cyp, R);
                limb.Freeze();
                c.Children.Add(new Path { Data = limb, Stroke = new SolidColorBrush(DAlpha(hazeC, 0.22 * hazeAmt)), StrokeThickness = 16, IsHitTestVisible = false });
                c.Children.Add(new Path { Data = limb, Stroke = new SolidColorBrush(DAlpha(DMix(hazeC, Colors.White, 0.35), 0.9 * Math.Max(hazeAmt, 0.35))), StrokeThickness = 2.4, IsHitTestVisible = false });
            }

            // ---- neon terrain grid, only on the planet's face ----
            var grid = new Canvas { Width = W, Height = H, IsHitTestVisible = false, Clip = new EllipseGeometry(new Point(CX, cyp), R, R) };
            double gridAlpha = 0.10 + 0.22 * DSmooth(0.1, 0.7, e);
            var vg = new StreamGeometry();
            using (var gc = vg.Open())
            {
                for (int i = -10; i <= 10; i++)
                {
                    gc.BeginFigure(new Point(CX + i * 8, hy), false, false);
                    gc.LineTo(new Point(CX + i * 95, H + 4), true, false);
                }
            }
            vg.Freeze();
            grid.Children.Add(new Path { Data = vg, Stroke = new SolidColorBrush(DAlpha(DescentCyan, gridAlpha * 0.7)), StrokeThickness = 1 });
            for (int j = 0; j < 18; j++)
            {
                double u = (j / 18.0 + _descentGridPhase) % 1.0;
                double y = hy + (H - hy) * u * u;
                grid.Children.Add(new Line
                {
                    X1 = 0, X2 = W, Y1 = y, Y2 = y,
                    Stroke = new SolidColorBrush(DAlpha(DescentCyan, (0.05 + 0.30 * u) * (gridAlpha / 0.22))),
                    StrokeThickness = 0.8 + 1.8 * u,
                });
            }
            c.Children.Add(grid);

            // ---- landing pad: beacon, hex pad ----
            double side = (PlanetRenderer.StableHash(body.BodyName) & 1) == 0 ? 1 : -1;   // which side the pad starts on (per body)
            double padX = CX + side * 205 * Math.Pow(1 - DSmooth(0.0, 0.97, e), 1.25) + dev * 8 * e;
            double padY = hy + (H - hy) * DLerp(0.02, 0.40, Math.Pow(e, 1.7));
            double s = DLerp(7, 150, Math.Pow(e, 2.0));
            double pulse = 0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 1000.0 * 4);
            var padGlow = new Ellipse
            {
                Width = s * 3.6, Height = s * 1.4, IsHitTestVisible = false,
                Fill = new RadialGradientBrush(DAlpha(DescentCyan, 0.30), DAlpha(DescentCyan, 0)),
            };
            Canvas.SetLeft(padGlow, padX - s * 1.8); Canvas.SetTop(padGlow, padY - s * 0.7);
            c.Children.Add(padGlow);
            var beam = new Rectangle
            {
                Width = 7, Height = 210, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(DAlpha(DescentCyan, 0), DAlpha(DescentCyan, 0.55 * DLerp(0.5, 1, e)), 90),
            };
            Canvas.SetLeft(beam, padX - 3.5); Canvas.SetTop(beam, padY - 210);
            c.Children.Add(beam);
            c.Children.Add(new Polygon
            {
                Points = DescentHex(padX, padY, s, s * 0.34, 0), IsHitTestVisible = false,
                Fill = new SolidColorBrush(DAlpha(DescentCyan, 0.10 + 0.10 * pulse)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x5f, 0xff, 0xf0)), StrokeThickness = 2,
            });
            c.Children.Add(new Polygon
            {
                Points = DescentHex(padX, padY, s * 0.6, s * 0.2, 0), IsHitTestVisible = false,
                Stroke = new SolidColorBrush(DAlpha(DescentCyan, 0.85)), StrokeThickness = 1.4,
            });

            // ---- corridor gates flying toward the viewer ----
            for (int k = 0; k < 6; k++)
            {
                double z = (k / 6.0 + _descentGatePhase) % 1.0;
                double gx = DLerp(padX, CX, z), gy = DLerp(padY - s * 0.4, H * 0.46, z);
                double gs = 12 + z * z * 300;
                double alpha = Math.Sin(Math.PI * z) * 0.9;
                if (alpha < 0.03) continue;
                c.Children.Add(new Polygon
                {
                    Points = DescentHex(gx, gy, gs, gs * 0.72, Math.PI / 6), IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(DAlpha(DescentCyan, alpha)), StrokeThickness = 1.2 + z * 2.8,
                });
            }

            // ---- lock-on reticle ----
            double rr = s * 1.5 + 22;
            double rcx = padX, rcy = padY - s * 0.25;
            var ring = new Ellipse
            {
                Width = rr * 2, Height = rr * 2, IsHitTestVisible = false,
                Stroke = new SolidColorBrush(DAlpha(DescentPink, 0.8)), StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 6, 4.5 },
                RenderTransform = new RotateTransform(_descentSpin, rr, rr),
            };
            Canvas.SetLeft(ring, rcx - rr); Canvas.SetTop(ring, rcy - rr);
            c.Children.Add(ring);
            double bx = rr * 0.8, bl = 15;
            var brk = new StreamGeometry();
            using (var gc = brk.Open())
            {
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    double x = rcx + sx * bx, y = rcy + sy * bx * 0.8;
                    gc.BeginFigure(new Point(x, y - sy * bl), false, false);
                    gc.LineTo(new Point(x, y), true, false);
                    gc.LineTo(new Point(x - sx * bl, y), true, false);
                }
            }
            brk.Freeze();
            c.Children.Add(new Path { Data = brk, Stroke = new SolidColorBrush(DescentPink), StrokeThickness = 2.2, IsHitTestVisible = false });
            bool lockOn = e > 0.7;
            var lockText = lockOn
                ? MakeCenterLabel("LOCK", rcx, rcy - rr - 28, ((int)(Environment.TickCount64 / 250) % 2 == 0) ? new SolidColorBrush(DescentPink) : Brushes.White, 20)
                : MakeCenterLabel("ACQUIRING", rcx, rcy - rr - 22, new SolidColorBrush(DAlpha(DescentCyan, 0.75)), 13);
            lockText.FontWeight = FontWeights.Bold;
            c.Children.Add(lockText);

            // ---- frame corners, vignette, sink rate ----
            var fr = new StreamGeometry();
            using (var gc = fr.Open())
            {
                const double m = 12, l = 28;
                foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                {
                    double x = sx > 0 ? m : W - m, y = sy > 0 ? m : H - m;
                    gc.BeginFigure(new Point(x, y + sy * l), false, false);
                    gc.LineTo(new Point(x, y), true, false);
                    gc.LineTo(new Point(x + sx * l, y), true, false);
                }
            }
            fr.Freeze();
            c.Children.Add(new Path { Data = fr, Stroke = new SolidColorBrush(DAlpha(DescentCyan, 0.85)), StrokeThickness = 1.8, IsHitTestVisible = false });
            c.Children.Add(new Rectangle
            {
                Width = W, Height = H, IsHitTestVisible = false,
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
                    new GradientStop(Color.FromArgb(150, 0, 0, 0), 1.0),
                }) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.78, RadiusY = 0.78 },
            });
            string vs = Math.Abs(sinkRate) >= 1000 ? $"{sinkRate / 1000:N1} km/s" : $"{sinkRate:N0} m/s";
            c.Children.Add(MakeCenterLabel($"V/S {vs}", CX, 540, InfoBrightValueBrush, 15));
        }


        // The planet's edge over the visible width as a plain polyline (a huge arc segment is drawn with artifacts when its
        // ends are far off-screen).
        private static StreamGeometry LimbGeometry(double cx, double cyp, double R)
        {
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                bool open = false;
                for (int i = 0; i <= 62; i++)
                {
                    double x = -20 + i * (DescentW + 40) / 62.0, dx = x - cx;
                    if (Math.Abs(dx) >= R) { open = false; continue; }
                    double y = cyp - Math.Sqrt(R * R - dx * dx);
                    if (y < -40 || y > DescentH + 40) { open = false; continue; }
                    var pt = new Point(x, y);
                    if (!open) { gc.BeginFigure(pt, false, false); open = true; }
                    else gc.LineTo(pt, true, false);
                }
            }
            geo.Freeze();
            return geo;
        }
        private static double Frac(double x) => x - Math.Floor(x);
    }
}
