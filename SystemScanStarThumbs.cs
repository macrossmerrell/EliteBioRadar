using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    public partial class SystemScanWindow
    {
        // Live GPU star thumbnail: the same shaders the Star tab uses (fusion stars, neutron stars, white dwarfs, black holes), drawn
        // at thumbnail size with Time frozen at a per-star offset. Bitmap-cached once (MainWindow.Supersample). Returns null on
        // software-only rendering, for stars with real rings, or on any failure, so the caller keeps the old bitmap/swatch.
        private static UIElement? TryBuildStarShaderThumb(BodyScanDetail s, double size)
        {
            if ((RenderCapability.Tier >> 16) == 0) return null;
            bool ringedStar = s.Rings.Exists(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase) && r.OuterRad > 0 && r.InnerRad > 0);
            if (ringedStar && s.StarType is "T" or "Y") return TryBuildRingedBrownDwarfThumb(s, size);
            if (ringedStar) return null;
            try
            {
                int box = (int)Math.Round(size);
                double t = PlanetRenderer.StableHash(s.BodyName) % 900;
                Effect effect;
                if (StarRenderer.IsNeutronStar(s.StarType))
                {
                    double u = size * 0.157;
                    var e = NeutronStarShaderEffect.Create(MainWindow.MakeNeutronLook(s), new Point(0.5, 0.5), new Point(u / box, u / box));
                    e.SetValue(NeutronStarShaderEffect.TimeProperty, t);
                    effect = e;
                }
                else if (StarRenderer.IsWhiteDwarf(s.StarType))
                {
                    double u = size * 0.157;
                    var e = WhiteDwarfShaderEffect.Create(MainWindow.MakeWhiteDwarfLook(s), new Point(0.5, 0.5), new Point(u / box, u / box));
                    e.SetValue(WhiteDwarfShaderEffect.TimeProperty, t);
                    effect = e;
                }
                else if (StarRenderer.IsBlackHole(s.StarType))
                {
                    double u = size * 0.10;
                    var e = BlackHoleShaderEffect.Create(MainWindow.MakeBlackHoleLook(s), new Point(0.5, 0.5), new Point(u / box, u / box));
                    e.SetValue(BlackHoleShaderEffect.TimeProperty, t);
                    effect = e;
                }
                else if (s.StarType is "T" or "Y")
                {
                    var (cx, cy, R) = StarRenderer.GetStarShaderDisc(s, box, box);
                    var e = GasGiantShaderEffect.Create(StarRenderer.GetBrownDwarfGasLook(s), new Point(cx / box, cy / box), new Point(R / box, R / box));
                    e.SetValue(GasGiantShaderEffect.LightDirProperty, new System.Windows.Media.Media3D.Vector3D(-0.12, 0.12, 0.99));
                    e.SetValue(GasGiantShaderEffect.TimeProperty, t);
                    effect = e;
                }
                else if (StarRenderer.IsProceduralStarFamily(s.StarType))
                {
                    var (cx, cy, R) = StarRenderer.GetStarShaderDisc(s, box, box);
                    var e = StarShaderEffect.Create(StarRenderer.GetStarLook(s), new Point(cx / box, cy / box), new Point(R / box, R / box));
                    e.SetValue(StarShaderEffect.TimeProperty, t);
                    effect = e;
                }
                else return null;
                return MainWindow.Supersample(new Rectangle { Width = box, Height = box, Fill = Brushes.Black, Effect = effect, IsHitTestVisible = false });
            }
            catch (Exception ex)
            {
                Log.Write($"Star thumbnail shader failed for {s.BodyName}: {ex.Message}");
                return null;
            }
        }

        // A brown dwarf with real rings: back ring, the magenta banded disc, front ring, built at the 140 px box the CPU ring thumbnail uses (so the
        // ring keeps its proportions) and scaled down to the card's thumb size.
        private static UIElement? TryBuildRingedBrownDwarfThumb(BodyScanDetail s, double size)
        {
            try
            {
                const int box = 140;
                var (ringBack, ringFront) = StarRenderer.GetStarRingLayers(s, box, box);
                var (cx, cy, R) = StarRenderer.GetStarShaderDisc(s, box, box);
                var e = GasGiantShaderEffect.Create(StarRenderer.GetBrownDwarfGasLook(s), new Point(cx / box, cy / box), new Point(R / box, R / box));
                e.SetValue(GasGiantShaderEffect.LightDirProperty, new System.Windows.Media.Media3D.Vector3D(-0.12, 0.12, 0.99));
                e.SetValue(GasGiantShaderEffect.TimeProperty, (double)(PlanetRenderer.StableHash(s.BodyName) % 900));
                var grid = new Grid { Width = box, Height = box, IsHitTestVisible = false };
                grid.Children.Add(new Image { Source = ringBack, Width = box, Height = box });
                grid.Children.Add(MainWindow.Supersample(new Rectangle { Width = box, Height = box, Fill = Brushes.Black, Effect = e, IsHitTestVisible = false }));
                grid.Children.Add(new Image { Source = ringFront, Width = box, Height = box });
                return new Viewbox { Width = size, Height = size, Child = grid, IsHitTestVisible = false };
            }
            catch (Exception ex)
            {
                Log.Write($"Ringed brown dwarf thumbnail failed for {s.BodyName}: {ex.Message}");
                return null;
            }
        }
    }
}
