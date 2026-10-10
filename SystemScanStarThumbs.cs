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
            if (s.Rings.Exists(r => !r.Name.Contains("Belt", StringComparison.OrdinalIgnoreCase) && r.OuterRad > 0 && r.InnerRad > 0)) return null;
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
    }
}
