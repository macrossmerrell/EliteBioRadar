using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace EliteBioRadar
{
    // The one place that turns a planet into its GPU layers (ring back pass, planet shader, ring front sliver, glow). Shared by the
    // Planet tab (animated, scene-sized) and the System Scan thumbnails (frozen time, small), so a look or geometry change is made once.
    internal static class PlanetShaderScenes
    {

        // The shader clocks run from 0 whenever a scene is built, so any redraw of the same body (a scan completing, a refresh) used to restart the rotation at its
        // starting point and the planet visibly jumped. The first time a body is drawn its start time is remembered, and later builds begin the clock at the elapsed
        // time instead, so the rotation carries on from where it was.
        private static readonly Dictionary<string, DateTime> _clockStarts = new(StringComparer.OrdinalIgnoreCase);
        internal static double ClockOffset(string key)
        {
            lock (_clockStarts)
            {
                if (!_clockStarts.TryGetValue(key, out var t)) { _clockStarts[key] = DateTime.UtcNow; return 0; }
                return Math.Min((DateTime.UtcNow - t).TotalSeconds, 30000.0);
            }
        }
        internal sealed class Scene
        {
            public readonly List<FrameworkElement> Layers = new();
            public double Cx, Cy;   // planet centre in the layers' own pixel space
        }

        // The gas giant shader's default light; the rings use the same one so planet and ring agree.
        private static readonly Vector3D Light = new(-0.55, 0.55, 0.63);

        // frozenTime null = animate (Time runs forever); a value = still frame at that time. Returns null when the planet isn't covered by a
        // shader, on software-only rendering, or on any failure - the caller then keeps the CPU render (rings included).
        internal static Scene? Build(BodyScanDetail d, string iconCode, int w, int h, double? frozenTime, long systemPopulation, double starK = 0, double starDistM = 0, double starFlux = 0)
        {
            if ((RenderCapability.Tier >> 16) == 0) return null;
            bool gas = PlanetRenderer.IsGasGiantFamily(iconCode);
            bool atmo = !gas && PlanetRenderer.IsAtmoWorld(d, iconCode);
            // Any rocky/metal/icy terrain body that is not an atmosphere world draws with the landable-world shader, landable or not (a ringed non-landable
            // Metal-rich body used to get the old CPU planet inside the new rings).
            bool landable = !gas && !atmo && PlanetRenderer.HasLandableShader(iconCode);
            bool ringed = d.Rings.Any(r => r.OuterRad > 0 && r.InnerRad > 0);
            (double, double, double)? ringTint = starK >= PlanetRenderer.RingTintMinK ? PlanetRenderer.StarLightTint(starK) : null;
            // Rings darken with the starlight a little faster than the planet: brightness is the planet's factor squared (floor 0.15), so a ring far from a weak star is dark.
            double ringDim = starFlux > 0 && !gas ? Math.Clamp(starFlux, 0.15, 1.0) : 1.0;   // not for gas giants: they are not dimmed with distance, so their rings stay as they were
            // Terrain planets without a shader of their own still get the ring shader when ringed.
            if (!gas && !atmo && !landable && !(ringed && PlanetRenderer.IsTerrainFamily(iconCode))) return null;
            try
            {
                var scene = new Scene();
                FrameworkElement Host(Effect e) => MainWindow.Supersample(new Rectangle { Width = w, Height = h, Fill = Brushes.Black, Effect = e, IsHitTestVisible = false });
                void Clock(Effect e, DependencyProperty time)
                {
                    if (frozenTime.HasValue) e.SetValue(time, frozenTime.Value);
                    else { double off = ClockOffset(d.BodyName); e.BeginAnimation(time, new DoubleAnimation(off, 36000, TimeSpan.FromSeconds(36000 - off))); }
                }

                BitmapSource? ringBackImage = null, glow = null;
                FrameworkElement? planet;
                double planetR;
                if (gas)
                {
                    (ringBackImage, glow) = PlanetRenderer.GetGasGiantShaderStaticLayers(d, iconCode, w, h, shaderRings: ringed);
                    (scene.Cx, scene.Cy, planetR) = PlanetRenderer.GetGasGiantSphere(d, w, h);
                    var e = GasGiantShaderEffect.Create(PlanetRenderer.GetGasGiantLook(d, iconCode),
                        new Point(scene.Cx / w, scene.Cy / h), new Point(planetR / w, planetR / h));
                    Clock(e, GasGiantShaderEffect.TimeProperty);
                    planet = Host(e);
                }
                else
                {
                    (scene.Cx, scene.Cy, planetR) = PlanetRenderer.GetTerrainGeometry(w, h, d);
                    var centre = new Point(scene.Cx / w, scene.Cy / h);
                    var radius = new Point(planetR / w, planetR / h);
                    if (atmo)
                    {
                        var atmoLook = PlanetRenderer.GetAtmoWorldLook(d, iconCode);
                        PlanetRenderer.ApplyAtmoStarTint(atmoLook, starK);
                        PlanetRenderer.ApplyAtmoStarFlux(atmoLook, starFlux);
                        var e = AtmoWorldShaderEffect.Create(atmoLook, centre, radius);
                        Clock(e, AtmoWorldShaderEffect.TimeProperty);
                        planet = Host(e);
                    }
                    else if (landable)
                    {
                        var e = LandableWorldShaderEffect.Create(PlanetRenderer.GetLandableWorldLook(d, iconCode, starK, starDistM, starFlux), centre, radius);
                        Clock(e, LandableWorldShaderEffect.TimeProperty);
                        planet = Host(e);
                    }
                    else
                        planet = new Image { Width = w, Height = h, Source = PlanetRenderer.GetTerrainSceneFrame(d, iconCode, w, h, systemPopulation, true) };
                }

                if (ringed)
                {
                    // Built before anything is returned: if the ring shader can't be set up the whole scene is abandoned (caller falls back to the
                    // CPU render with its own rings) rather than showing a ringless planet.
                    double? terrainR = gas ? null : planetR;
                    scene.Layers.Add(Host(RingShaderEffect.Create(d, w, h, false, Light, terrainR, ringTint, ringDim)));
                    scene.Layers.Add(planet);
                    scene.Layers.Add(Host(RingShaderEffect.Create(d, w, h, true, Light, terrainR, ringTint, ringDim)));
                }
                else
                {
                    if (ringBackImage != null) scene.Layers.Add(new Image { Width = w, Height = h, Source = ringBackImage, IsHitTestVisible = false });
                    scene.Layers.Add(planet);
                }
                if (glow != null) scene.Layers.Add(new Image { Width = w, Height = h, Source = glow, IsHitTestVisible = false });
                return scene;
            }
            catch (Exception ex)
            {
                Log.Write($"Planet shader scene failed for {d.BodyName}, using CPU renderer: {ex.Message}");
                return null;
            }
        }
    }
}
