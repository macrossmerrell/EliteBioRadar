using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Per-body look for the thick-atmosphere world shader - everything the HLSL needs besides time.
    public sealed class AtmoWorldLook
    {
        public Color Surf0, Surf1, Haze, Cloud, Cap, Glow;
        public double HazeAmt, SurfContrast, CycloneAmt, CloudAmt, CapAmt, Tilt, Pitch, Seed, CrackAmt, FleckAmt, HotAmt;
        public Vector3D LightDir = new Vector3D(-0.55, 0.55, 0.63);   // view space; more frontal = less terminator
    }

    // GPU thick-atmosphere rocky world (Shaders\AtmoWorld.fx -> AtmoWorld.ps). Same host-rectangle
    // pattern as GasGiantShaderEffect: the shader draws the whole lit sphere and animates itself
    // from Time, and it shares that class's noise texture.
    public sealed class AtmoWorldShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/AtmoWorld.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(AtmoWorldShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(AtmoWorldShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(AtmoWorldShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty         = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty         = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty       = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty       = Reg("Radius", 3, new Point(0.25, 0.25));
        public static readonly DependencyProperty Surf0Property        = Reg("Surf0", 4, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty Surf1Property        = Reg("Surf1", 5, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty HazeColProperty      = Reg("HazeCol", 6, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty CloudColProperty     = Reg("CloudCol", 7, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty CapColProperty       = Reg("CapCol", 8, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty GlowColProperty      = Reg("GlowCol", 9, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty HazeAmtProperty      = Reg("HazeAmt", 10, 0.3);
        public static readonly DependencyProperty SurfContrastProperty = Reg("SurfContrast", 11, 1.0);
        public static readonly DependencyProperty CycloneAmtProperty   = Reg("CycloneAmt", 12, 0.7);
        public static readonly DependencyProperty CloudAmtProperty     = Reg("CloudAmt", 13, 0.7);
        public static readonly DependencyProperty CapAmtProperty       = Reg("CapAmt", 14, 0.0);
        public static readonly DependencyProperty TiltProperty         = Reg("Tilt", 15, 0.0);
        public static readonly DependencyProperty PitchProperty        = Reg("Pitch", 16, 0.0);
        public static readonly DependencyProperty LightDirProperty     = Reg("LightDir", 17, new Vector3D(-0.55, 0.55, 0.63));
        public static readonly DependencyProperty CrackAmtProperty     = Reg("CrackAmt", 18, 0.0);
        public static readonly DependencyProperty FleckAmtProperty     = Reg("FleckAmt", 19, 0.0);
        public static readonly DependencyProperty HotAmtProperty       = Reg("HotAmt", 20, 0.0);

        public AtmoWorldShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, Surf0Property, Surf1Property,
                HazeColProperty, CloudColProperty, CapColProperty, GlowColProperty, HazeAmtProperty,
                SurfContrastProperty, CycloneAmtProperty, CloudAmtProperty, CapAmtProperty, TiltProperty,
                PitchProperty, LightDirProperty, CrackAmtProperty, FleckAmtProperty, HotAmtProperty,
            })
                UpdateShaderValue(p);
        }

        public static AtmoWorldShaderEffect Create(AtmoWorldLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new AtmoWorldShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(Surf0Property, C(look.Surf0));
            e.SetValue(Surf1Property, C(look.Surf1));
            e.SetValue(HazeColProperty, C(look.Haze));
            e.SetValue(CloudColProperty, C(look.Cloud));
            e.SetValue(CapColProperty, C(look.Cap));
            e.SetValue(GlowColProperty, C(look.Glow));
            e.SetValue(HazeAmtProperty, look.HazeAmt);
            e.SetValue(SurfContrastProperty, look.SurfContrast);
            e.SetValue(CycloneAmtProperty, look.CycloneAmt);
            e.SetValue(CloudAmtProperty, look.CloudAmt);
            e.SetValue(CapAmtProperty, look.CapAmt);
            e.SetValue(TiltProperty, look.Tilt);
            e.SetValue(PitchProperty, look.Pitch);
            e.SetValue(CrackAmtProperty, look.CrackAmt);
            e.SetValue(FleckAmtProperty, look.FleckAmt);
            e.SetValue(HotAmtProperty, look.HotAmt);
            e.SetValue(LightDirProperty, look.LightDir);
            return e;
        }
    }
}
