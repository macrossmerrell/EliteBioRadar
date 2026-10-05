using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Everything Star.fx needs besides time.
    public sealed class StarLook
    {
        public Color Core, Mid, Edge, Hot;
        public double Seed, Activity, Contrast, SpotAmt, LoopAmt, FlareAmt, HaloAmt, Tilt, PatchCover = 1.0;
    }

    // GPU star scene (Shaders\Star.fx -> Star.ps): photosphere, sunspots, halo, coronal loops and
    // mass ejections, all animated from Time on the GPU. Same host-rectangle pattern as
    // GasGiantShaderEffect and it shares that class's noise texture.
    public sealed class StarShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/Star.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(StarShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(StarShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(StarShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty     = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty     = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty   = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty   = Reg("Radius", 3, new Point(0.25, 0.25));
        public static readonly DependencyProperty CoreColProperty  = Reg("CoreCol", 4, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty MidColProperty   = Reg("MidCol", 5, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty EdgeColProperty  = Reg("EdgeCol", 6, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty HotColProperty   = Reg("HotCol", 7, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty ActivityProperty = Reg("Activity", 8, 0.5);
        public static readonly DependencyProperty ContrastProperty = Reg("Contrast", 9, 1.0);
        public static readonly DependencyProperty SpotAmtProperty  = Reg("SpotAmt", 10, 0.5);
        public static readonly DependencyProperty LoopAmtProperty  = Reg("LoopAmt", 11, 0.5);
        public static readonly DependencyProperty FlareAmtProperty = Reg("FlareAmt", 12, 0.5);
        public static readonly DependencyProperty HaloAmtProperty  = Reg("HaloAmt", 13, 0.45);
        public static readonly DependencyProperty TiltProperty     = Reg("Tilt", 14, 0.0);
        public static readonly DependencyProperty PatchCoverProperty = Reg("PatchCover", 15, 1.0);

        public StarShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, CoreColProperty, MidColProperty,
                EdgeColProperty, HotColProperty, ActivityProperty, ContrastProperty, SpotAmtProperty,
                LoopAmtProperty, FlareAmtProperty, HaloAmtProperty, TiltProperty, PatchCoverProperty,
            })
                UpdateShaderValue(p);
        }

        public static StarShaderEffect Create(StarLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new StarShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(CoreColProperty, C(look.Core));
            e.SetValue(MidColProperty, C(look.Mid));
            e.SetValue(EdgeColProperty, C(look.Edge));
            e.SetValue(HotColProperty, C(look.Hot));
            e.SetValue(ActivityProperty, look.Activity);
            e.SetValue(ContrastProperty, look.Contrast);
            e.SetValue(SpotAmtProperty, look.SpotAmt);
            e.SetValue(LoopAmtProperty, look.LoopAmt);
            e.SetValue(FlareAmtProperty, look.FlareAmt);
            e.SetValue(HaloAmtProperty, look.HaloAmt);
            e.SetValue(TiltProperty, look.Tilt);
            e.SetValue(PatchCoverProperty, look.PatchCover);
            return e;
        }
    }
}
