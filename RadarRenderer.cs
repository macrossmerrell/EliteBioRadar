using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    public class RadarRenderer
    {
        private static readonly Color ColGridLine  = Color.FromArgb(0x28, 0x00, 0xb8, 0xb8);
        private static readonly Color ColGridRing  = Color.FromArgb(0x55, 0x00, 0xe5, 0xff);
        private static readonly Color ColNorthLine = Color.FromArgb(0xaa, 0x66, 0xff, 0xff);
        private static readonly Color ColHeading   = Color.FromRgb(0x00, 0xff, 0xcc);
        private static readonly Color ColShip      = Color.FromRgb(0x00, 0xff, 0xcc);
        private static readonly Color ColScan1     = Color.FromRgb(0x00, 0xa3, 0xff);
        private static readonly Color ColScan2     = Color.FromRgb(0x00, 0xff, 0x44);
        private static readonly Color ColScanFull  = Color.FromRgb(0xff, 0xaa, 0x00);

        // Pulse animation — one full sweep inner→outer every 1.25 seconds
        private readonly Stopwatch _pulse = Stopwatch.StartNew();
        private const double PulseCycleSecs = 2.5;

        private readonly Canvas _canvas;

        public RadarRenderer(Canvas canvas) => _canvas = canvas;

        // ---------------------------------------------------------------
        public void Draw(EliteStatus status, List<ScannedOrganism> organisms,
                         double scaleMetres, string? activeGenus = null, bool radarAnimation = true,
                         List<ScannedGeoSite>? geoSites = null)
        {
            _canvas.Children.Clear();

            double w = _canvas.ActualWidth;
            double h = _canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double cx = w / 2.0;
            double cy = h / 2.0;
            double r  = Math.Min(cx, cy) - 20;

            // Background disc
            DrawDisc(cx, cy, r, Color.FromRgb(0x05, 0x10, 0x10), Colors.Transparent);

            // Pulse phase — 0.0 to 1.0 sweeping inner→outer over PulseCycleSecs
            double pulsePhase = _pulse.Elapsed.TotalSeconds % PulseCycleSecs / PulseCycleSecs;
            double discR = radarAnimation ? pulsePhase * r : 0;

            // Expanding disc — only drawn when animation is enabled
            if (radarAnimation && discR > 1)
            {
                int steps = 32;
                for (int s = steps; s >= 1; s--)
                {
                    double stepR    = discR * s / steps;
                    double radial   = (double)s / steps;
                    double gradient = 0.3 + 0.7 * radial;
                    double edgeFade = Math.Max(0, 1.0 - Math.Max(0, (discR / r - 0.92) / 0.08));
                    byte   alpha    = (byte)(2.5 * gradient * edgeFade);
                    if (alpha < 2) continue;
                    DrawDisc(cx, cy, stepR,
                        Color.FromArgb(alpha, 0x00, 0xe5, 0xff),
                        Colors.Transparent);
                }
            }

            // Range rings — peak brightness when wave front is AT the ring, fade after
            for (int i = 1; i <= 4; i++)
            {
                double ringR       = r * i / 4.0;
                double distToFront = discR - ringR;

                double waveIntensity;
                if (distToFront < 0)
                    waveIntensity = Math.Max(0, 1.0 + distToFront / (r * 0.08));
                else
                    waveIntensity = Math.Max(0, 1.0 - distToFront / (r * 0.20));

                double edgeFade   = Math.Max(0, 1.0 - Math.Max(0, (discR / r - 0.92) / 0.08));
                waveIntensity    *= edgeFade;

                byte alpha   = (byte)Math.Min(0xff, 0x55 + waveIntensity * 0x22);
                double thick = 1.0 + waveIntensity * 0.4;
                DrawCircle(cx, cy, ringR, thick,
                    Color.FromArgb(alpha, 0x00, 0xe5, 0xff));

                double labelMetres = scaleMetres * i / 4.0;
                string labelStr = labelMetres >= 1000
                    ? $"{labelMetres / 1000:F1}km" : $"{labelMetres:F0}m";
                DrawText(cx + ringR - 28, cy - 8, labelStr, 9,
                    Color.FromArgb(0x88, 0x00, 0xe5, 0xff));
            }

                        // Crosshairs and diagonals
            DrawLine(cx, cy - r, cx, cy + r, 1, ColGridLine);
            DrawLine(cx - r, cy, cx + r, cy, 1, ColGridLine);
            DrawLine(cx - r * 0.707, cy - r * 0.707, cx + r * 0.707, cy + r * 0.707, 0.5, ColGridLine);
            DrawLine(cx + r * 0.707, cy - r * 0.707, cx - r * 0.707, cy + r * 0.707, 0.5, ColGridLine);

            // Disc border
            DrawCircle(cx, cy, r, 1.5, ColGridRing);

            // North indicator
            DrawText(cx - 5, cy - r - 18, "N", 11, ColNorthLine, bold: true);
            DrawLine(cx, cy - r - 1, cx, cy - r + 8, 1.5, ColNorthLine);

            double pixelsPerMetre = r / scaleMetres;

            // Organisms
            if (status.HasPosition)
            {
                lock (organisms)
                {
                    foreach (var org in organisms)
                        DrawOrganism(org, status, cx, cy, r, pixelsPerMetre, activeGenus);
                }
            }

            // Geological sites
            if (status.HasPosition && geoSites != null && geoSites.Count > 0)
            {
                lock (geoSites)
                {
                    foreach (var site in geoSites)
                        DrawGeoSite(site, status, cx, cy, r, pixelsPerMetre);
                }
            }

            // Heading arrow — 1/10 of radius (1/3 of previous 0.3)
            DrawHeadingArrow(cx, cy, r, status.Heading);

            // Ship marker — dot only, no label
            DrawShipMarker(cx, cy);
        }

        // ---------------------------------------------------------------
        private void DrawOrganism(ScannedOrganism org, EliteStatus status,
                                  double cx, double cy, double r,
                                  double pixelsPerMetre, string? activeGenus)
        {
            double dist    = EliteWatcherService.DistanceMeters(
                status.Latitude, status.Longitude, org.Latitude, org.Longitude, status.PlanetRadius);
            double bearing = EliteWatcherService.BearingDeg(
                status.Latitude, status.Longitude, org.Latitude, org.Longitude);

            double angleRad = (bearing - 90) * Math.PI / 180.0;
            double px = dist * pixelsPerMetre * Math.Cos(angleRad);
            double py = dist * pixelsPerMetre * Math.Sin(angleRad);

            bool offscreen = (px * px + py * py) > r * r;
            if (offscreen)
            {
                double norm = Math.Sqrt(px * px + py * py);
                px = px / norm * (r - 8);
                py = py / norm * (r - 8);
            }

            double sx = cx + px;
            double sy = cy + py;

            bool completed = org.IsComplete;

            // IsComplete = journal confirmed all 3 scans → dim grey-blue
            // ScanCount=3 but not IsComplete = fresh 3rd dot → show orange briefly
            var col = completed
                ? Color.FromArgb(0x66, 0x55, 0x88, 0xaa)
                : org.ScanCount switch { 1 => ColScan1, 2 => ColScan2, _ => ColScanFull };

            // Only highlight active genus for incomplete scans
            bool isActive = !completed && !string.IsNullOrEmpty(activeGenus) &&
                string.Equals(org.Genus, activeGenus, StringComparison.OrdinalIgnoreCase);

            // Colony range ring — very faint for completed, normal for active
            double rangePixels = org.ColonyRange * pixelsPerMetre;
            if (!offscreen && rangePixels > 3)
            {
                if (completed)
                {
                    // Just a very faint dashed ring, no fill
                    DrawDashedCircle(sx, sy, rangePixels, Color.FromArgb(0x28, 0x55, 0x88, 0xaa));
                }
                else
                {
                    // Diagonal hatch fill matching the dot colour
                    DrawHatchedDisc(sx, sy, rangePixels, col);
                    DrawCircle(sx, sy, rangePixels, 1.5, Color.FromArgb(0xcc, col.R, col.G, col.B));
                }
            }

            // Dot marker — smaller and dim for completed, normal/larger for active
            double dotR = offscreen ? 3 : (completed ? 4 : (isActive ? 8 : 6));
            DrawDisc(sx, sy, dotR, Color.FromArgb(completed ? (byte)0x55 : (byte)0xcc, col.R, col.G, col.B), Colors.Transparent);
            if (isActive)
                DrawCircle(sx, sy, dotR + 3, 1, Color.FromArgb(0xaa, col.R, col.G, col.B));

            // Crosshair ticks — skip for completed
            if (!offscreen && !completed)
            {
                DrawLine(sx - 8, sy, sx + 8, sy, 1, col);
                DrawLine(sx, sy - 8, sx, sy + 8, 1, col);
            }

            // Labels — skip for completed off-screen, dim for completed on-screen
            if (!offscreen)
            {
                string label   = org.Genus.Length > 0
                    ? org.Genus.Substring(0, Math.Min(4, org.Genus.Length)).ToUpper() : "?";
                string distStr = dist < 1000 ? $"{dist:F0}m" : $"{dist / 1000:F2}km";
                if (!completed)
                {
                    DrawText(sx + 8, sy - 18, label,   9, col, bold: isActive);
                    DrawText(sx + 8, sy - 7,  distStr, 8, Color.FromArgb(0xcc, col.R, col.G, col.B));
                }
            }
            else if (!completed)
            {
                string distStr = dist < 1000 ? $"{dist:F0}m" : $"{dist / 1000:F2}km";
                DrawText(sx + 6, sy - 5, distStr, 8, Color.FromArgb(0xaa, col.R, col.G, col.B));
            }
        }

        // ---------------------------------------------------------------
        private static readonly Color ColGeo = Color.FromRgb(0xff, 0xaa, 0x00); // matches sidebar "GEO:" text

        private void DrawGeoSite(ScannedGeoSite site, EliteStatus status,
                                 double cx, double cy, double r, double pixelsPerMetre)
        {
            double dist    = EliteWatcherService.DistanceMeters(
                status.Latitude, status.Longitude, site.Latitude, site.Longitude, status.PlanetRadius);
            double bearing = EliteWatcherService.BearingDeg(
                status.Latitude, status.Longitude, site.Latitude, site.Longitude);

            double angleRad = (bearing - 90) * Math.PI / 180.0;
            double px = dist * pixelsPerMetre * Math.Cos(angleRad);
            double py = dist * pixelsPerMetre * Math.Sin(angleRad);

            bool offscreen = (px * px + py * py) > r * r;
            if (offscreen)
            {
                double norm = Math.Sqrt(px * px + py * py);
                px = px / norm * (r - 8);
                py = py / norm * (r - 8);
            }

            double sx = cx + px;
            double sy = cy + py;

            // X arm length scales with zoom; below 4px threshold, collapse to a dot
            // At default 500m scale with a ~200px radius: pixelsPerMetre ≈ 0.4 → arm = 6
            // Zoomed way out pixelsPerMetre drops and arm shrinks naturally
            double arm = Math.Min(6.0, Math.Max(2.0, pixelsPerMetre * 15));
            bool useDot = arm < 4.0 || offscreen;

            if (useDot)
            {
                // Small filled dot — still clearly geo-amber, just compact
                double dotR = offscreen ? 2.5 : 2.5;
                DrawDisc(sx, sy, dotR, Color.FromArgb(0xcc, ColGeo.R, ColGeo.G, ColGeo.B), Colors.Transparent);
            }
            else
            {
                // X mark — two diagonal lines crossing at (sx, sy)
                DrawLine(sx - arm, sy - arm, sx + arm, sy + arm, 1.5, Color.FromArgb(0xdd, ColGeo.R, ColGeo.G, ColGeo.B));
                DrawLine(sx + arm, sy - arm, sx - arm, sy + arm, 1.5, Color.FromArgb(0xdd, ColGeo.R, ColGeo.G, ColGeo.B));
                // Small centre dot so the crossing point is crisp
                DrawDisc(sx, sy, 1.5, Color.FromArgb(0xff, ColGeo.R, ColGeo.G, ColGeo.B), Colors.Transparent);
            }

            // Distance label — only on-screen, skip if too zoomed out (arm collapsed)
            if (!offscreen && !useDot)
            {
                string distStr = dist < 1000 ? $"{dist:F0}m" : $"{dist / 1000:F2}km";
                DrawText(sx + arm + 3, sy - 8, distStr, 8, Color.FromArgb(0xaa, ColGeo.R, ColGeo.G, ColGeo.B));
            }
        }

        // ---------------------------------------------------------------
        private void DrawHeadingArrow(double cx, double cy, double r, double headingDeg)
        {
            double angleRad = (headingDeg - 90) * Math.PI / 180.0;
            // Arrow is 1/10 of radius (was 0.3, now 0.1 = 1/3 of that)
            double arrowLen = r * 0.1;
            double ex = cx + arrowLen * Math.Cos(angleRad);
            double ey = cy + arrowLen * Math.Sin(angleRad);

            // Shaft
            DrawLine(cx, cy, ex, ey, 1.5, ColHeading);

            // Tiny solid filled arrowhead using a Polygon
            double tipSize = 5;
            double perpAngle1 = angleRad + Math.PI / 2;
            double perpAngle2 = angleRad - Math.PI / 2;
            double baseWidth  = 3;

            var tip   = new Point(ex, ey);
            var base1 = new Point(
                cx + (arrowLen - tipSize) * Math.Cos(angleRad) + baseWidth * Math.Cos(perpAngle1),
                cy + (arrowLen - tipSize) * Math.Sin(angleRad) + baseWidth * Math.Sin(perpAngle1));
            var base2 = new Point(
                cx + (arrowLen - tipSize) * Math.Cos(angleRad) + baseWidth * Math.Cos(perpAngle2),
                cy + (arrowLen - tipSize) * Math.Sin(angleRad) + baseWidth * Math.Sin(perpAngle2));

            var poly = new Polygon
            {
                Fill   = new SolidColorBrush(ColHeading),
                Stroke = null,
                Points = new PointCollection { tip, base1, base2 },
            };
            _canvas.Children.Add(poly);

            // Heading label near the tip
            DrawText(ex + 4, ey - 12, $"{headingDeg:F0}°", 8, ColHeading);
        }

        // ---------------------------------------------------------------
        private void DrawShipMarker(double cx, double cy)
        {
            // Outer glow only, no label
            DrawDisc(cx, cy, 8, Color.FromArgb(0x44, 0x00, 0xff, 0xcc), Colors.Transparent);
            DrawDisc(cx, cy, 4, Color.FromArgb(0xff, 0x00, 0xff, 0xcc), Colors.Transparent);
        }

        // ---------------------------------------------------------------
        private void DrawHatchedDisc(double cx, double cy, double radius, Color col)
        {
            // Build a diagonal line tile brush matching the dot colour
            var hatchColor = Color.FromArgb(0xbb, col.R, col.G, col.B);
            var drawing = new DrawingGroup();

            // Tile size — spacing between diagonal lines
            const double tile = 8.0;

            // Two lines per tile to create a clean diagonal pattern
            // Line from top-left to bottom-right of tile
            var lineGeom1 = new LineGeometry(new Point(0, 0), new Point(tile, tile));
            // Repeat shifted so lines are continuous across tiles
            var lineGeom2 = new LineGeometry(new Point(-tile, 0), new Point(0, tile));
            var lineGeom3 = new LineGeometry(new Point(0, -tile), new Point(tile, 0));

            var pen = new Pen(new SolidColorBrush(hatchColor), 1.0);

            drawing.Children.Add(new GeometryDrawing(null, pen, lineGeom1));
            drawing.Children.Add(new GeometryDrawing(null, pen, lineGeom2));
            drawing.Children.Add(new GeometryDrawing(null, pen, lineGeom3));

            var brush = new DrawingBrush
            {
                Drawing    = drawing,
                TileMode   = TileMode.Tile,
                Viewport   = new Rect(0, 0, tile, tile),
                ViewportUnits = BrushMappingMode.Absolute,
            };

            var el = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = brush,
                Stroke = null,
            };
            Canvas.SetLeft(el, cx - radius);
            Canvas.SetTop(el,  cy - radius);
            _canvas.Children.Add(el);
        }

        private void DrawDisc(double cx, double cy, double radius, Color fill, Color stroke)
        {
            var el = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = new SolidColorBrush(fill),
                Stroke = stroke == Colors.Transparent ? null : new SolidColorBrush(stroke),
            };
            Canvas.SetLeft(el, cx - radius);
            Canvas.SetTop(el,  cy - radius);
            _canvas.Children.Add(el);
        }

        private void DrawCircle(double cx, double cy, double radius, double thickness, Color col)
        {
            var el = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(col),
                StrokeThickness = thickness,
                Fill   = null,
            };
            Canvas.SetLeft(el, cx - radius);
            Canvas.SetTop(el,  cy - radius);
            _canvas.Children.Add(el);
        }

        private void DrawDashedCircle(double cx, double cy, double radius, Color col)
        {
            var el = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(0xaa, col.R, col.G, col.B)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                Fill   = null,
            };
            Canvas.SetLeft(el, cx - radius);
            Canvas.SetTop(el,  cy - radius);
            _canvas.Children.Add(el);
        }

        private void DrawLine(double x1, double y1, double x2, double y2,
                              double thickness, Color col)
        {
            var line = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(col),
                StrokeThickness = thickness,
            };
            _canvas.Children.Add(line);
        }

        private void DrawText(double x, double y, string text, double size,
                              Color col, bool bold = false)
        {
            var tb = new TextBlock
            {
                Text       = text,
                Foreground = new SolidColorBrush(col),
                FontFamily = new FontFamily("Consolas"),
                FontSize   = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb,  y);
            _canvas.Children.Add(tb);
        }
    }
}
