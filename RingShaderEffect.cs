using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // GPU planetary rings (Shaders\Ring.fx -> Ring.ps): up to three class-coloured bands with fine ringlets, soft edges, the planet's
    // shadow and a front sliver pass. Same host-rectangle pattern as the other shader scenes; shares their noise texture.
    public sealed class RingShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/Ring.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(RingShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(RingShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(RingShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty SeedProperty    = Reg("Seed", 0, 1.0);
        public static readonly DependencyProperty CenterProperty  = Reg("Center", 1, new Point(0.5, 0.5));
        public static readonly DependencyProperty ScaleProperty   = Reg("Scale", 2, new Point(0.002, 0.002));
        public static readonly DependencyProperty TiltProperty    = Reg("Tilt", 3, 0.0);
        public static readonly DependencyProperty SquashProperty  = Reg("Squash", 4, 0.3);
        public static readonly DependencyProperty PlanetRProperty = Reg("PlanetR", 5, 100.0);
        public static readonly DependencyProperty ModeProperty    = Reg("Mode", 6, 0.0);
        public static readonly DependencyProperty LightDirProperty = Reg("LightDir", 7, new Vector3D(-0.55, 0.55, 0.63));
        public static readonly DependencyProperty Band0Property   = Reg("Band0", 8, new Point4D(0, 0, 0, 0));
        public static readonly DependencyProperty Band1Property   = Reg("Band1", 9, new Point4D(0, 0, 0, 0));
        public static readonly DependencyProperty Band2Property   = Reg("Band2", 10, new Point4D(0, 0, 0, 0));
        public static readonly DependencyProperty Col0Property    = Reg("Col0", 11, new Point4D(0, 0, 0, 0));
        public static readonly DependencyProperty Col1Property    = Reg("Col1", 12, new Point4D(0, 0, 0, 0));
        public static readonly DependencyProperty Col2Property    = Reg("Col2", 13, new Point4D(0, 0, 0, 0));

        public RingShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                SeedProperty, CenterProperty, ScaleProperty, TiltProperty, SquashProperty, PlanetRProperty, ModeProperty,
                LightDirProperty, Band0Property, Band1Property, Band2Property, Col0Property, Col1Property, Col2Property,
            })
                UpdateShaderValue(p);
        }

        // Per ring class: how solid the band is, how deep its gaps/ringlets are, how grainy it looks.
        private static (double dens, double contrast, double grain) ClassLook(string ringClass) => ringClass switch
        {
            "eRingClass_Icy"       => (0.97, 0.80, 0.30),
            "eRingClass_Rocky"     => (0.88, 0.70, 0.85),
            "eRingClass_MetalRich" => (0.92, 0.55, 0.60),
            "eRingClass_Metalic"   => (0.92, 0.55, 0.60),
            _                      => (0.90, 0.65, 0.60),
        };

        // lightDir is the planet shader's view-space light (x right, y up, z toward viewer); it is rotated into the ring-aligned frame here.
        public static RingShaderEffect Create(BodyScanDetail detail, int sceneW, int sceneH, bool frontPass, Vector3D lightDir, double? planetRadius = null)
        {
            var (cx, cy, sphereR, bands) = PlanetRenderer.GetRingShaderGeometry(detail, sceneW, sceneH);
            var e = new RingShaderEffect();
            double tilt = PlanetRenderer.RingTilt, squash = PlanetRenderer.RingSquash;
            e.SetValue(SeedProperty, (PlanetRenderer.StableHash(detail.BodyName) % 1000) / 1000.0);
            e.SetValue(CenterProperty, new Point(cx / sceneW, cy / sceneH));
            e.SetValue(ScaleProperty, new Point(1.0 / sceneW, 1.0 / sceneH));
            e.SetValue(TiltProperty, tilt);
            e.SetValue(SquashProperty, squash);
            e.SetValue(PlanetRProperty, planetRadius ?? sphereR);
            e.SetValue(ModeProperty, frontPass ? 1.0 : 0.0);

            // Rotate the light by -tilt about the view axis (screen y is down, light y is up).
            double ct = Math.Cos(tilt), st = Math.Sin(tilt);
            double lx = lightDir.X * ct + (-lightDir.Y) * st;
            double lyDown = -lightDir.X * st + (-lightDir.Y) * ct;
            e.SetValue(LightDirProperty, new Vector3D(lx, -lyDown, lightDir.Z));

            var spanProps = new[] { Band0Property, Band1Property, Band2Property };
            var colProps = new[] { Col0Property, Col1Property, Col2Property };
            for (int i = 0; i < 3 && i < bands.Count; i++)
            {
                var b = bands[i];
                var (dens, contrast, grain) = ClassLook(b.RingClass);
                e.SetValue(spanProps[i], new Point4D(b.InnerPx, b.OuterPx, dens, contrast));
                e.SetValue(colProps[i], new Point4D(b.Color.R / 255.0, b.Color.G / 255.0, b.Color.B / 255.0, grain));
            }
            return e;
        }
    }
}
