using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    public partial class MainWindow
    {
        // GPU neutron star on the Star tab: see Shaders\NeutronStar.fx. Returns false (caller falls back to the CPU render) on
        // software-only rendering or any setup failure.
        private bool TryAddNeutronShaderScene(BodyScanDetail detail, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                int hash = PlanetRenderer.StableHash(detail.BodyName);
                var look = new NeutronStarLook
                {
                    Core = Color.FromRgb(238, 246, 255), Jet = Color.FromRgb(120, 170, 255), Tail = Color.FromRgb(176, 136, 255), Halo = Color.FromRgb(34, 64, 215),
                    Seed = (hash % 5000) / 100.0 + 1.0,
                    Tilt = (62 + (((hash >> 3) % 21) - 10)) * Math.PI / 180.0,   // jet axis around the usual 62 degrees, varied a little per star
                    TailAmt = 1.0, Spin = 1.0,
                };
                const double unit = 58;   // px per shader length unit
                var effect = NeutronStarShaderEffect.Create(look, new Point(0.5, 0.5), new Point(unit / sceneW, unit / sceneH));
                effect.BeginAnimation(NeutronStarShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));

                // A sprinkling of distant stars under the glow.
                var sg = new StreamGeometry();
                using (var gc = sg.Open())
                {
                    for (int i = 0; i < 70; i++)
                    {
                        double sx = sceneX + Frac(Math.Sin(i * 12.9898) * 43758.5453) * sceneW;
                        double sy = sceneY + Frac(Math.Sin(i * 78.233 + 4.1) * 43758.5453) * sceneH;
                        double sz = 0.8 + Frac(Math.Sin(i * 39.346) * 43758.5453) * 1.2;
                        gc.BeginFigure(new Point(sx, sy), true, true);
                        gc.LineTo(new Point(sx + sz, sy), false, false);
                        gc.LineTo(new Point(sx + sz, sy + sz), false, false);
                        gc.LineTo(new Point(sx, sy + sz), false, false);
                    }
                }
                sg.Freeze();
                starPanelCanvas.Children.Add(new Path { Data = sg, Fill = new SolidColorBrush(Color.FromArgb(0x99, 205, 222, 255)), IsHitTestVisible = false });

                var surface = new Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect, IsHitTestVisible = false };
                Canvas.SetLeft(surface, sceneX); Canvas.SetTop(surface, sceneY);
                starPanelCanvas.Children.Add(surface);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Neutron star shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        // GPU white dwarf on the Star tab: see Shaders\WhiteDwarf.fx. Same fallback behaviour as the neutron star scene.
        private bool TryAddWhiteDwarfShaderScene(BodyScanDetail detail, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                int hash = PlanetRenderer.StableHash(detail.BodyName);
                var look = new WhiteDwarfLook
                {
                    Core = Color.FromRgb(242, 249, 255), Jet = Color.FromRgb(160, 190, 255), Edge = Color.FromRgb(58, 84, 255), Halo = Color.FromRgb(30, 58, 215),
                    Seed = (hash % 5000) / 100.0 + 1.0,
                    Tilt = (62 + (((hash >> 3) % 21) - 10)) * Math.PI / 180.0,
                    Power = 0.9 + ((hash >> 7) % 25) / 100.0,
                    Spin = 1.0,
                };
                const double unit = 58;
                var effect = WhiteDwarfShaderEffect.Create(look, new Point(0.5, 0.5), new Point(unit / sceneW, unit / sceneH));
                effect.BeginAnimation(WhiteDwarfShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                AddDistantStars(sceneW, sceneH, sceneX, sceneY);
                var surface = new Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect, IsHitTestVisible = false };
                Canvas.SetLeft(surface, sceneX); Canvas.SetTop(surface, sceneY);
                starPanelCanvas.Children.Add(surface);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"White dwarf shader setup failed, using CPU renderer: {ex.Message}");
                return false;
            }
        }

        private void AddDistantStars(int sceneW, int sceneH, int sceneX, int sceneY)
        {
            var sg = new StreamGeometry();
            using (var gc = sg.Open())
            {
                for (int i = 0; i < 70; i++)
                {
                    double sx = sceneX + Frac(Math.Sin(i * 12.9898) * 43758.5453) * sceneW;
                    double sy = sceneY + Frac(Math.Sin(i * 78.233 + 4.1) * 43758.5453) * sceneH;
                    double sz = 0.8 + Frac(Math.Sin(i * 39.346) * 43758.5453) * 1.2;
                    gc.BeginFigure(new Point(sx, sy), true, true);
                    gc.LineTo(new Point(sx + sz, sy), false, false);
                    gc.LineTo(new Point(sx + sz, sy + sz), false, false);
                    gc.LineTo(new Point(sx, sy + sz), false, false);
                }
            }
            sg.Freeze();
            starPanelCanvas.Children.Add(new Path { Data = sg, Fill = new SolidColorBrush(Color.FromArgb(0x99, 205, 222, 255)), IsHitTestVisible = false });
        }

        // Developer aid: set the environment variable BIORADAR_STARPREVIEW to a star class ("N", "DA", "H", ...) before starting the
        // app and the Star tab shows that kind of star regardless of where you are - for checking looks of stars you will rarely meet.
        private BodyScanDetail? _previewStar;
        private static string? PreviewStarType()
        {
            var v = Environment.GetEnvironmentVariable("BIORADAR_STARPREVIEW");
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        private BodyScanDetail GetPreviewStar(string type)
        {
            if (_previewStar == null || !string.Equals(_previewStar.StarType, type, StringComparison.OrdinalIgnoreCase))
                _previewStar = new BodyScanDetail { IsStar = true, StarType = type, BodyName = "Preview " + type, StellarMass = 1.4, Radius = 12000, SurfaceTemperature = 600000 };
            return _previewStar;
        }
    }
}
