using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    // The two ascent screens, sharing one look: the planet (the real render) sinks away to a thin glowing curve under a
    // sky that drains from the atmosphere's colour to black, with stars coming in.
    //  - Jump from atmosphere (FSD hyperspace charge while still near a body): the next system's marker slides to the
    //    centre, locks, the drive charges, and the screen ends in a hyperspace flash. Timed by the real charge.
    //  - Climb (normal ascent into supercruise): the same screen without a target - just the descent's gates leading up
    //    out of the centre, fading as the atmosphere thins. Driven by the real altitude.
    public partial class MainWindow
    {
        private double _ascentP, _ascentGatePhase;
        private DateTime _ascentLastTick = DateTime.MinValue;
        private DateTime _jumpChargeStart = DateTime.MinValue, _jumpLastCharging = DateTime.MinValue;

        // Expected seconds an atmospheric jump charge takes; the scene's timeline is stretched to it (and holds at "charge
        // complete" if the real charge runs longer).
        // A hyperspace charge takes about 15 s. The first ~2.5 s are the align-and-lock beat; the charge ring then fills across the rest
        // of the real spool-up and holds just short of full until the drive actually fires.
        private const double JumpChargeSeconds = 15.0, JumpLockSeconds = 2.5, JumpFlashSeconds = 1.6;

        private void ResetAscentState()
        {
            _ascentP = 0;
            _ascentLastTick = DateTime.MinValue;
        }

        // True while the jump screen should show: a hyperspace charge near a body, then - once the drive fires (the journal's
        // StartJump) - the flash and the hyperspace tunnel, until arrival ends the charge state. Idempotent within a tick
        // (called from several places).
        private bool JumpSceneActive(EliteStatus status)
        {
            var now = DateTime.UtcNow;
            bool charging = _watcher != null && _watcher.IsChargingJump && status.HasPosition;
            if (charging)
            {
                if (_jumpLastCharging == DateTime.MinValue || (now - _jumpLastCharging).TotalSeconds > 3)
                    _jumpChargeStart = now;
                _jumpLastCharging = now;
                return true;
            }
            // Keep the screen through the jump itself even if position data drops out during hyperspace.
            return _watcher != null && _watcher.IsChargingJump && _jumpLastCharging != DateTime.MinValue
                && _watcher.HyperspaceJumpStartedAt > _jumpChargeStart
                && (now - _watcher.HyperspaceJumpStartedAt).TotalSeconds < 90;
        }

        // Sky, stars, the planet and its atmosphere. p = 0 on the ground .. 1 clear of the atmosphere.
        // sinkFrac: how far the planet has sunk, 0 = on the ground .. 1 = fully below the frame.
        private void DrawAscentBase(Canvas c, BodyScanDetail? body, double p, double t, double sinkFrac = -1)
        {
            const double W = DescentW, H = DescentH, CX = DescentCx;
            if (sinkFrac < 0) sinkFrac = Math.Pow(DSmooth(0, 0.8, p), 0.8);   // jump screen: sinks within the short charge
            var (skyC, hazeC, density) = body != null ? DescentAtmosphere(body) : (Color.FromRgb(100, 120, 140), Color.FromRgb(180, 200, 220), 0.4);
            double ap = DSmooth(0.05, 0.7, p);
            double skyAmt = density * (1 - ap);

            var near = Color.FromRgb(2, 4, 10);
            c.Children.Add(new Rectangle
            {
                Width = W, Height = H, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(DMix(near, skyC, skyAmt * 0.30), 0.0),
                    new GradientStop(DMix(near, hazeC, skyAmt * 0.78), 1.0),
                }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
            });

            // Stars hang almost still; they only trace a very slow arc around a point below the planet (an orbit-trace sweep
            // of a few degrees that eases back and forth), and brighten as the air thins.
            double starAlpha = DSmooth(0.05, 0.5, p) * (1 - skyAmt * 0.6) * 0.85;
            if (starAlpha > 0.02)
            {
                var sg = new StreamGeometry();
                double ang = 0.06 * Math.Sin(t * 0.05), ca = Math.Cos(ang), sa = Math.Sin(ang);
                const double pvx = CX, pvy = H * 1.3;
                using (var gc = sg.Open())
                {
                    for (int i = 0; i < 130; i++)
                    {
                        double bx = -W * 0.3 + Frac(Math.Sin(i * 12.9898) * 43758.5453) * W * 1.6;
                        double by = -H * 0.3 + Frac(Math.Sin(i * 78.233 + 4.1) * 43758.5453) * H * 1.3;
                        double sx = pvx + (bx - pvx) * ca - (by - pvy) * sa;
                        double sy = pvy + (bx - pvx) * sa + (by - pvy) * ca;
                        double sz = 0.8 + Frac(Math.Sin(i * 39.346) * 43758.5453) * 1.2;
                        gc.BeginFigure(new Point(sx, sy), true, true);
                        gc.LineTo(new Point(sx + sz, sy), false, false);
                        gc.LineTo(new Point(sx + sz, sy + sz), false, false);
                        gc.LineTo(new Point(sx, sy + sz), false, false);
                    }
                }
                sg.Freeze();
                c.Children.Add(new Path { Data = sg, Fill = new SolidColorBrush(DAlpha(Color.FromRgb(210, 228, 255), starAlpha)), IsHitTestVisible = false });
            }

            // The planet sinks to a thin curve at the bottom.
            double hy = DLerp(H * 0.58, H * 1.16, sinkFrac);
            double R = W * 2.2, cyp = hy + R;
            string? iconCode = body != null ? MapPlanetClassToIconCode(body.PlanetClass) : null;
            bool real = body != null && iconCode != null && PlanetRenderer.IsTerrainFamily(iconCode);
            if (real) EnsureDescentSurface(body!, iconCode!);
            if (real && _descentSurface != null)
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
            else
            {
                c.Children.Add(new Path
                {
                    Data = new EllipseGeometry(new Point(CX, cyp), R, R), IsHitTestVisible = false,
                    Fill = new SolidColorBrush(DMix(Color.FromRgb(30, 34, 40), hazeC, 0.18)),
                });
            }

            // Atmosphere: haze band and glow in the planet's colour, bright limb.
            double hazeAmt = density * (1 - 0.6 * p);
            if (hazeAmt > 0.01)
            {
                c.Children.Add(new Rectangle
                {
                    Width = W, Height = 300, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(0, hy - 90),
                    Fill = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(DAlpha(hazeC, 0), 0.0),
                        new GradientStop(DAlpha(hazeC, 0.55 * hazeAmt), 90.0 / 300),
                        new GradientStop(DAlpha(hazeC, 0.12 * hazeAmt), 0.6),
                        new GradientStop(DAlpha(hazeC, 0), 1.0),
                    }) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) },
                });
                var glowRect = new Ellipse
                {
                    Width = 1000, Height = 320, IsHitTestVisible = false,
                    Fill = new RadialGradientBrush(DAlpha(hazeC, 0.40 * hazeAmt), DAlpha(hazeC, 0)),
                };
                Canvas.SetLeft(glowRect, CX - 500); Canvas.SetTop(glowRect, hy - 170);
                c.Children.Add(glowRect);
            }
            var limb = LimbGeometry(CX, cyp, R);
            limb.Freeze();
            double limbA = Math.Max(Math.Max(density, 0.3), 0.3);
            c.Children.Add(new Path { Data = limb, Stroke = new SolidColorBrush(DAlpha(hazeC, 0.22 * limbA)), StrokeThickness = 16, IsHitTestVisible = false });
            c.Children.Add(new Path { Data = limb, Stroke = new SolidColorBrush(DAlpha(DMix(hazeC, Colors.White, 0.4), 0.9 * limbA)), StrokeThickness = 2.4, IsHitTestVisible = false });
        }

        private static void DrawAscentFrame(Canvas c)
        {
            const double W = DescentW, H = DescentH;
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
        }

        // Normal climb: the descent's gates lead up out of the centre and fade as the air thins. No target.
        private void DrawClimbScene(Canvas c, BodyScanDetail? body, double altitudeM, double climbRateMps, double dt)
        {
            const double H = DescentH, CX = DescentCx;
            // A climb in supercruise spans the ground to roughly 1,700 km (where position data first appears on the way down), so
            // progress is log-scaled over that range: ~1 km = 0.09, 10 km = 0.32, 100 km = 0.62, 400 km = 0.81.
            double target = Math.Clamp(Math.Log10(1 + Math.Max(altitudeM, 0) / 1000.0) / Math.Log10(1 + 1700.0), 0, 1);
            if (dt > 0)
            {
                if (target > _ascentP) _ascentP += (target - _ascentP) * (1 - Math.Exp(-1.2 * dt));
            }
            double p = _ascentP;
            // Gates drift slowly, paced by the real climb rate (about one new gate every 2-6 seconds).
            double rate = Math.Clamp(Math.Log10(1 + Math.Max(climbRateMps, 0)) / 5.0, 0, 1);
            _ascentGatePhase = (_ascentGatePhase + dt * (0.03 + 0.07 * rate)) % 1.0;
            double t = Environment.TickCount64 / 1000.0;

            // The planet sinks steadily and is only fully gone at the top of the climb (orbital cruise height).
            DrawAscentBase(c, body, p, t, 0.724 * p);

            double fx = CX, fy = H * 0.40, gA = 1 - DSmooth(0.35, 0.75, p);
            if (gA > 0.02)
            {
                for (int k = 0; k < 6; k++)
                {
                    double z = (k / 6.0 + _ascentGatePhase) % 1.0;
                    double gs = 6 + z * z * 330;
                    double a = DSmooth(0, 0.12, z) * (1 - DSmooth(0.55, 1.0, z)) * 0.9 * gA;   // appear small and far, fade as they near the screen
                    if (a < 0.06) continue;
                    c.Children.Add(new Polygon
                    {
                        Points = DescentHex(fx, fy + z * 18, gs, gs * 0.72, Math.PI / 6), IsHitTestVisible = false,
                        Stroke = new SolidColorBrush(DAlpha(DescentCyan, a)), StrokeThickness = 0.8 + z * 1.8,
                    });
                }
                var gl = new Ellipse { Width = 150, Height = 150, IsHitTestVisible = false, Fill = new RadialGradientBrush(DAlpha(DescentCyan, 0.22 * gA), DAlpha(DescentCyan, 0)) };
                Canvas.SetLeft(gl, fx - 75); Canvas.SetTop(gl, fy - 75);
                c.Children.Add(gl);
            }
            DrawAscentFrame(c);
        }

        // The hyperspace tunnel shown from the end of the flash until arrival: streaks pouring out of the centre.
        private void DrawHyperspaceTunnel(Canvas c, double t, string next)
        {
            const double W = DescentW, H = DescentH, CX = DescentCx;
            double fx = CX, fy = H * 0.40;
            c.Children.Add(new Rectangle { Width = W, Height = H, IsHitTestVisible = false, Fill = new SolidColorBrush(Color.FromRgb(2, 4, 10)) });
            var core = new Ellipse { Width = 460, Height = 460, IsHitTestVisible = false, Fill = new RadialGradientBrush(DAlpha(Color.FromRgb(150, 190, 255), 0.38), DAlpha(Color.FromRgb(150, 190, 255), 0)) };
            Canvas.SetLeft(core, fx - 230); Canvas.SetTop(core, fy - 230);
            c.Children.Add(core);
            var cols = new[] { Color.FromRgb(255, 111, 210), Color.FromRgb(95, 255, 240), Colors.White };
            for (int ci = 0; ci < 3; ci++)
            {
                var sg = new StreamGeometry();
                using (var gc = sg.Open())
                {
                    for (int i = ci; i < 150; i += 3)
                    {
                        double a = Frac(Math.Sin(i * 12.9898) * 43758.5453) * Math.PI * 2;
                        double u = (t * 0.55 + Frac(Math.Sin(i * 7.1) * 9999)) % 1.0;
                        double d0 = 14 + u * u * W * 0.60;
                        double d1 = d0 + 6 + u * u * W * 0.38;
                        gc.BeginFigure(new Point(fx + Math.Cos(a) * d0, fy + Math.Sin(a) * d0), false, false);
                        gc.LineTo(new Point(fx + Math.Cos(a) * d1, fy + Math.Sin(a) * d1), true, false);
                    }
                }
                sg.Freeze();
                c.Children.Add(new Path { Data = sg, Stroke = new SolidColorBrush(DAlpha(cols[ci], 0.8)), StrokeThickness = 1.2 + ci * 0.7, IsHitTestVisible = false });
            }
            var lbl = MakeCenterLabel("IN HYPERSPACE", fx, fy + 150, Brushes.White, 18);
            lbl.FontWeight = FontWeights.Bold;
            c.Children.Add(lbl);
            if (!string.IsNullOrEmpty(next)) c.Children.Add(MakeCenterLabel(next, fx, fy + 176, new SolidColorBrush(DAlpha(Colors.White, 0.8)), 13));
            DrawAscentFrame(c);
        }

        // Jump from atmosphere. Returns the short status text for the HUD plate.
        private string DrawJumpScene(Canvas c, BodyScanDetail? body, EliteStatus status, double altitudeM, double dt)
        {
            const double W = DescentW, H = DescentH, CX = DescentCx;
            var now = DateTime.UtcNow;
            double t = Environment.TickCount64 / 1000.0;
            double p;
            // The drive has fired once the journal's StartJump arrives (it lands as the charge ends): flash, then the tunnel.
            bool jumped = _watcher != null && _watcher.HyperspaceJumpStartedAt > _jumpChargeStart;
            double tj = jumped ? (now - _watcher!.HyperspaceJumpStartedAt).TotalSeconds : -1;
            if (jumped && tj >= JumpFlashSeconds)
            {
                DrawHyperspaceTunnel(c, t, _watcher?.CurrentDestination?.NextSystem ?? "");
                return "HYPERSPACE";
            }
            if (jumped)
                p = 0.9 + 0.1 * Math.Clamp(tj / JumpFlashSeconds, 0, 1);
            else
            {
                double el = (now - _jumpChargeStart).TotalSeconds;
                p = el < JumpLockSeconds
                    ? el / JumpLockSeconds * 0.66
                    : Math.Min(0.895, 0.66 + 0.24 * Math.Clamp((el - JumpLockSeconds) / (JumpChargeSeconds - JumpLockSeconds), 0, 1));
            }

            // Sky, stars and planet follow the real altitude (the same scale as the climb): a jump charged on or near the ground
            // keeps the planet in frame, and it only sinks away if you actually climb.
            double tgt = Math.Clamp(Math.Log10(1 + Math.Max(altitudeM, 0) / 1000.0) / Math.Log10(1 + 1700.0), 0, 1);
            if (dt > 0 && tgt > _ascentP) _ascentP += (tgt - _ascentP) * (1 - Math.Exp(-1.2 * dt));
            DrawAscentBase(c, body, _ascentP, t, 0.724 * _ascentP);

            double fx = CX, fy = H * 0.40;
            double al = 1 - DSmooth(0.12, 0.55, p);
            double tx = fx + 136 * al, ty = fy - 77 * al;
            double tsz = p < 0.12 ? 0.4 + 2.0 * DSmooth(0, 0.12, p) : 1.0;
            double ta = Math.Clamp(p / 0.08, 0, 1);
            bool locked = p > 0.55;
            var lockCol = Color.FromRgb(0x7d, 0xff, 0xb0);

            // Alignment rings and crosshair.
            foreach (double r in new[] { 41.0, 77, 113, 150 })
                c.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Stroke = new SolidColorBrush(DAlpha(DescentCyan, 0.30)), StrokeThickness = 1, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(fx - r, fy - r) });
            var cross = new StreamGeometry();
            using (var gc = cross.Open())
            {
                gc.BeginFigure(new Point(fx - 172, fy), false, false); gc.LineTo(new Point(fx + 172, fy), true, false);
                gc.BeginFigure(new Point(fx, fy - 172), false, false); gc.LineTo(new Point(fx, fy + 172), true, false);
            }
            cross.Freeze();
            c.Children.Add(new Path { Data = cross, Stroke = new SolidColorBrush(DAlpha(DescentCyan, 0.30)), StrokeThickness = 1, IsHitTestVisible = false });
            c.Children.Add(new Ellipse { Width = 48, Height = 48, Stroke = new SolidColorBrush(locked ? lockCol : DescentPink), StrokeThickness = 2.4, IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(fx - 24, fy - 24) });

            // Line to the target while it is still off-centre.
            if (al > 0.02 && ta > 0.05)
                c.Children.Add(new Line { X1 = fx, Y1 = fy, X2 = tx, Y2 = ty, Stroke = new SolidColorBrush(DAlpha(DescentPink, 0.6 * Math.Clamp(p / 0.12, 0, 1))), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 5, 6 }, IsHitTestVisible = false });

            // The next system: a flare that swells as the drive charges.
            double flare = 1 + 2 * DSmooth(0.66, 0.9, p);
            var star = new Ellipse { Width = 100 * tsz * flare, Height = 100 * tsz * flare, IsHitTestVisible = false, Fill = new RadialGradientBrush(DAlpha(Color.FromRgb(255, 240, 200), 0.55 * ta), DAlpha(Color.FromRgb(255, 240, 200), 0)) };
            Canvas.SetLeft(star, tx - 50 * tsz * flare); Canvas.SetTop(star, ty - 50 * tsz * flare);
            c.Children.Add(star);
            double arm = 15 * tsz * flare;
            var starCross = new StreamGeometry();
            using (var gc = starCross.Open())
            {
                gc.BeginFigure(new Point(tx - arm, ty), false, false); gc.LineTo(new Point(tx + arm, ty), true, false);
                gc.BeginFigure(new Point(tx, ty - arm), false, false); gc.LineTo(new Point(tx, ty + arm), true, false);
            }
            starCross.Freeze();
            c.Children.Add(new Path { Data = starCross, Stroke = new SolidColorBrush(DAlpha(Colors.White, ta)), StrokeThickness = 1.6, IsHitTestVisible = false });
            c.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(DAlpha(Colors.White, ta)), IsHitTestVisible = false, RenderTransform = new TranslateTransform(tx - 3, ty - 3) });
            if (ta > 0.5)
            {
                string next = _watcher?.CurrentDestination?.NextSystem ?? "";
                var n1 = new TextBlock { Text = string.IsNullOrEmpty(next) ? "NEXT JUMP" : next, Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
                Canvas.SetLeft(n1, tx + 22); Canvas.SetTop(n1, ty - 22);
                var n2 = new TextBlock { Text = "NEXT JUMP", Foreground = InfoOrangeBrush, FontFamily = new FontFamily("Consolas"), FontSize = 10 };
                Canvas.SetLeft(n2, tx + 22); Canvas.SetTop(n2, ty - 7);
                if (!string.IsNullOrEmpty(next)) c.Children.Add(n2);
                c.Children.Add(n1);
            }

            // Charge ring and the lock / align text.
            string phase;
            if (p > 0.66 && p < 0.9)
            {
                double f = DSmooth(0.66, 0.9, p);
                var arc = new StreamGeometry();
                using (var gc = arc.Open())
                {
                    const double rr = 42;
                    double a1 = -Math.PI / 2 + Math.Min(f, 0.999) * Math.PI * 2;
                    gc.BeginFigure(new Point(fx, fy - rr), false, false);
                    gc.ArcTo(new Point(fx + rr * Math.Cos(a1), fy + rr * Math.Sin(a1)), new Size(rr, rr), 0, f > 0.5, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                c.Children.Add(new Path { Data = arc, Stroke = new SolidColorBrush(lockCol), StrokeThickness = 4.5, IsHitTestVisible = false });
                c.Children.Add(MakeCenterLabel($"FSD CHARGING {Math.Min(99, Math.Round(f * 100))} %", fx, fy + 78, new SolidColorBrush(lockCol), 15));
                phase = "FSD CHARGING";
            }
            else if (p <= 0.66)
            {
                c.Children.Add(MakeCenterLabel(locked ? "LOCKED" : $"ALIGN {al * 34:F1}°", fx, fy + 78, new SolidColorBrush(locked ? lockCol : Color.FromRgb(0xff, 0x9a, 0xd8)), 15));
                phase = locked ? "TARGET LOCKED" : "ALIGNING";
            }
            else phase = "JUMPING";
            if (locked && p < 0.9 && (p < 0.66 || (int)(Environment.TickCount64 / 250) % 2 == 0))
            {
                var tl = MakeCenterLabel("TARGET LOCKED", fx, fy - 62, new SolidColorBrush(lockCol), 18);
                tl.FontWeight = FontWeights.Bold;
                c.Children.Add(tl);
            }

            DrawAscentFrame(c);

            // Hyperspace: streaks from the centre, then a white flash.
            if (p > 0.9)
            {
                double f = (p - 0.9) / 0.1;
                var cols = new[] { Color.FromRgb(255, 111, 210), Color.FromRgb(95, 255, 240), Colors.White };
                for (int ci = 0; ci < 3; ci++)
                {
                    var sg = new StreamGeometry();
                    using (var gc = sg.Open())
                    {
                        for (int i = ci; i < 120; i += 3)
                        {
                            double a = Frac(Math.Sin(i * 12.9898) * 43758.5453) * Math.PI * 2;
                            double d0 = 18 + Frac(Math.Sin(i * 3.7) * 9999) * 55;
                            double d1 = d0 + f * f * W * 0.5 * (0.4 + Frac(Math.Sin(i * 7.1) * 9999));
                            gc.BeginFigure(new Point(fx + Math.Cos(a) * d0, fy + Math.Sin(a) * d0), false, false);
                            gc.LineTo(new Point(fx + Math.Cos(a) * d1, fy + Math.Sin(a) * d1), true, false);
                        }
                    }
                    sg.Freeze();
                    c.Children.Add(new Path { Data = sg, Stroke = new SolidColorBrush(DAlpha(cols[ci], 0.8)), StrokeThickness = 1.6 + ci * 0.6, IsHitTestVisible = false });
                }
                double flashA = 0.9 * Math.Exp(-Math.Pow((f - 0.12) / 0.12, 2)) + 0.85 * DSmooth(0.75, 1, f);
                c.Children.Add(new Rectangle { Width = W, Height = H, IsHitTestVisible = false, Fill = new SolidColorBrush(DAlpha(Colors.White, Math.Clamp(flashA, 0, 1))) });
                if (f < 0.8)
                {
                    var jt = MakeCenterLabel("JUMP", CX, H * 0.5 - 14, new SolidColorBrush(Color.FromRgb(2, 6, 11)), 34);
                    jt.FontWeight = FontWeights.Bold;
                    c.Children.Add(jt);
                }
            }
            return phase;
        }
    }
}
