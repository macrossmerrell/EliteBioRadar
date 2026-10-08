using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Everything WhiteDwarf.fx needs besides time.
    public sealed class WhiteDwarfLook
    {
        public Color Core, Jet, Edge, Halo;
        public double Seed, Tilt, Power = 1.0, Spin = 1.0;
    }

    // GPU white dwarf (ShadersWhiteDwarf.fx -> WhiteDwarf.ps): core, glare, light streak and two straight conical jets with drifting
    // striations. Same host-rectangle pattern as the other shader scenes and it shares their noise texture.
    public sealed class WhiteDwarfShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/WhiteDwarf.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(WhiteDwarfShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(WhiteDwarfShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(WhiteDwarfShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty    = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty    = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty  = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty  = Reg("Radius", 3, new Point(0.2, 0.2));
        public static readonly DependencyProperty TiltProperty    = Reg("Tilt", 4, 1.08);
        public static readonly DependencyProperty CoreColProperty = Reg("CoreCol", 5, new Point4D(1, 1, 1, 1));
        public static readonly DependencyProperty JetColProperty  = Reg("JetCol", 6, new Point4D(0.5, 0.7, 1, 1));
        public static readonly DependencyProperty EdgeColProperty = Reg("EdgeCol", 7, new Point4D(0.25, 0.35, 1, 1));
        public static readonly DependencyProperty HaloColProperty = Reg("HaloCol", 8, new Point4D(0.1, 0.2, 0.8, 1));
        public static readonly DependencyProperty PowerProperty = Reg("Power", 9, 1.0);
        public static readonly DependencyProperty SpinProperty    = Reg("Spin", 10, 1.0);

        public WhiteDwarfShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, TiltProperty, CoreColProperty,
                JetColProperty, EdgeColProperty, HaloColProperty, PowerProperty, SpinProperty,
            })
                UpdateShaderValue(p);
        }

        public static WhiteDwarfShaderEffect Create(WhiteDwarfLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new WhiteDwarfShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(TiltProperty, look.Tilt);
            e.SetValue(CoreColProperty, C(look.Core));
            e.SetValue(JetColProperty, C(look.Jet));
            e.SetValue(EdgeColProperty, C(look.Edge));
            e.SetValue(HaloColProperty, C(look.Halo));
            e.SetValue(PowerProperty, look.Power);
            e.SetValue(SpinProperty, look.Spin);
            return e;
        }
    }
}
