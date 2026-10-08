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
        // GPU black hole on the Star tab: see Shaders\BlackHole.fx. Returns false (caller falls back to the old drawing) on
        // software-only rendering or any setup failure.
        private bool TryAddBlackHoleShaderScene(BodyScanDetail detail, int sceneW, int sceneH, int sceneX, int sceneY)
        {
            if ((RenderCapability.Tier >> 16) == 0) return false;
            try
            {
                int hash = PlanetRenderer.StableHash(detail.BodyName);
                bool super = string.Equals(detail.StarType, "SupermassiveBlackHole", StringComparison.OrdinalIgnoreCase);
                var look = new BlackHoleLook
                {
                    Seed = (hash % 5000) / 100.0 + 1.0,
                    Rot = (((hash >> 12) & 1) == 0 ? -1 : 1) * (0.26 + ((hash >> 3) % 26) / 100.0),   // disk axis: always clearly tilted (15-30 degrees), leaning either way
                    Elev = 0.17 + ((hash >> 9) % 14) / 100.0,                                 // how far it is tipped toward us
                    // Sagittarius A* and its kin are starved: a dimmer, redder, cooler disk than a stellar-mass hole's bright white-gold one.
                    DiskHot = super ? Color.FromRgb(255, 176, 96) : Color.FromRgb(255, 236, 196),
                    DiskCool = super ? Color.FromRgb(168, 56, 22) : Color.FromRgb(255, 116, 36),
                    DiskAmt = super ? 1.25 : 1.6,
                    Beam = 0.55,
                    Glow = super ? 0.8 : 1.0,
                };
                const double unit = 50;   // px per shadow radius
                var effect = BlackHoleShaderEffect.Create(look, new Point(0.5, 0.5), new Point(unit / sceneW, unit / sceneH));
                effect.BeginAnimation(BlackHoleShaderEffect.TimeProperty,
                    new DoubleAnimation(0, 36000, TimeSpan.FromSeconds(36000)));
                var surface = new Rectangle { Width = sceneW, Height = sceneH, Fill = Brushes.Black, Effect = effect, IsHitTestVisible = false };
                Canvas.SetLeft(surface, sceneX); Canvas.SetTop(surface, sceneY);
                starPanelCanvas.Children.Add(surface);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Black hole shader setup failed, using the old drawing: {ex.Message}");
                return false;
            }
        }
    }
}
