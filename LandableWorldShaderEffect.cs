using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Per-body look for the landable world shader - everything the HLSL needs besides time.
    public sealed class LandableWorldLook
    {
        public Color Light, Dark, Accent, Bright, Ice, Hot;
        public double Contrast, CraterAmt, RayAmt, RustAmt, BrightAmt, IceAmt, HotAmt, AtmGlow, Tilt, Pitch, Mottle, Seed, SpecAmt, LineAmt;
        public Vector3D LightDir = new Vector3D(-0.55, 0.55, 0.63);   // view space; more frontal = softer terminator
    }

    // GPU landable rocky world (Shaders\LandableWorld.fx -> LandableWorld.ps). Same host-rectangle
    // pattern as the gas giant / thick-atmosphere shaders, and it shares their noise texture.
    public sealed class LandableWorldShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/LandableWorld.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(LandableWorldShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(LandableWorldShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(LandableWorldShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty      = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty      = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty    = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty    = Reg("Radius", 3, new Point(0.25, 0.25));
        public static readonly DependencyProperty ColLightProperty  = Reg("ColLight", 4, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty ColDarkProperty   = Reg("ColDark", 5, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty ColAccentProperty = Reg("ColAccent", 6, new Point4D(1, 0, 0, 1));
        public static readonly DependencyProperty ColBrightProperty = Reg("ColBright", 7, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty ColIceProperty    = Reg("ColIce", 8, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty ColHotProperty    = Reg("ColHot", 9, new Point4D(1, 0.5, 0.2, 1));
        public static readonly DependencyProperty ContrastProperty  = Reg("Contrast", 10, 1.0);
        public static readonly DependencyProperty CraterAmtProperty = Reg("CraterAmt", 11, 0.6);
        public static readonly DependencyProperty RayAmtProperty    = Reg("RayAmt", 12, 0.3);
        public static readonly DependencyProperty RustAmtProperty   = Reg("RustAmt", 13, 0.3);
        public static readonly DependencyProperty BrightAmtProperty = Reg("BrightAmt", 14, 0.0);
        public static readonly DependencyProperty IceAmtProperty    = Reg("IceAmt", 15, 0.0);
        public static readonly DependencyProperty HotAmtProperty    = Reg("HotAmt", 16, 0.0);
        public static readonly DependencyProperty AtmGlowProperty   = Reg("AtmGlow", 17, 0.0);
        public static readonly DependencyProperty TiltProperty      = Reg("Tilt", 18, 0.0);
        public static readonly DependencyProperty PitchProperty     = Reg("Pitch", 19, 0.0);
        public static readonly DependencyProperty LightDirProperty  = Reg("LightDir", 20, new Vector3D(-0.55, 0.55, 0.63));
        public static readonly DependencyProperty MottleProperty    = Reg("Mottle", 21, 1.0);
        public static readonly DependencyProperty SpecAmtProperty   = Reg("SpecAmt", 22, 0.0);
        public static readonly DependencyProperty LineAmtProperty   = Reg("LineAmt", 23, 0.0);

        public LandableWorldShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, ColLightProperty, ColDarkProperty,
                ColAccentProperty, ColBrightProperty, ColIceProperty, ColHotProperty, ContrastProperty,
                CraterAmtProperty, RayAmtProperty, RustAmtProperty, BrightAmtProperty, IceAmtProperty,
                HotAmtProperty, AtmGlowProperty, TiltProperty, PitchProperty, LightDirProperty, MottleProperty, SpecAmtProperty, LineAmtProperty,
            })
                UpdateShaderValue(p);
        }

        public static LandableWorldShaderEffect Create(LandableWorldLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new LandableWorldShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(ColLightProperty, C(look.Light));
            e.SetValue(ColDarkProperty, C(look.Dark));
            e.SetValue(ColAccentProperty, C(look.Accent));
            e.SetValue(ColBrightProperty, C(look.Bright));
            e.SetValue(ColIceProperty, C(look.Ice));
            e.SetValue(ColHotProperty, C(look.Hot));
            e.SetValue(ContrastProperty, look.Contrast);
            e.SetValue(CraterAmtProperty, look.CraterAmt);
            e.SetValue(RayAmtProperty, look.RayAmt);
            e.SetValue(RustAmtProperty, look.RustAmt);
            e.SetValue(BrightAmtProperty, look.BrightAmt);
            e.SetValue(IceAmtProperty, look.IceAmt);
            e.SetValue(HotAmtProperty, look.HotAmt);
            e.SetValue(AtmGlowProperty, look.AtmGlow);
            e.SetValue(TiltProperty, look.Tilt);
            e.SetValue(PitchProperty, look.Pitch);
            e.SetValue(MottleProperty, look.Mottle);
            e.SetValue(SpecAmtProperty, look.SpecAmt);
            e.SetValue(LineAmtProperty, look.LineAmt);
            e.SetValue(LightDirProperty, look.LightDir);
            return e;
        }
    }
}
