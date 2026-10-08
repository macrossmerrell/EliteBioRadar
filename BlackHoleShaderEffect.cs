using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Everything BlackHole.fx needs besides time.
    public sealed class BlackHoleLook
    {
        public Color DiskHot, DiskCool;
        public double Seed, Rot, Elev = 0.22, DiskAmt = 1.0, Beam = 0.7, Glow = 1.0;
    }

    // GPU black hole (Shaders\BlackHole.fx -> BlackHole.ps): lensed stars, shadow, photon ring and a Doppler-shaded accretion
    // disk bent over and under the shadow. Same host-rectangle pattern as the other shader scenes and it shares their noise texture.
    public sealed class BlackHoleShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/BlackHole.ps", UriKind.Absolute),
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(BlackHoleShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(BlackHoleShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(BlackHoleShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty     = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty     = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty   = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty   = Reg("Radius", 3, new Point(0.13, 0.13));
        public static readonly DependencyProperty RotProperty      = Reg("Rot", 4, 0.0);
        public static readonly DependencyProperty ElevProperty     = Reg("Elev", 5, 0.22);
        public static readonly DependencyProperty DiskHotProperty  = Reg("DiskHot", 6, new Point4D(1, 0.9, 0.7, 1));
        public static readonly DependencyProperty DiskCoolProperty = Reg("DiskCool", 7, new Point4D(1, 0.4, 0.15, 1));
        public static readonly DependencyProperty DiskAmtProperty  = Reg("DiskAmt", 8, 1.0);
        public static readonly DependencyProperty BeamProperty     = Reg("Beam", 9, 0.7);
        public static readonly DependencyProperty GlowProperty     = Reg("Glow", 10, 1.0);

        public BlackHoleShaderEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, GasGiantShaderEffect.NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, RotProperty, ElevProperty,
                DiskHotProperty, DiskCoolProperty, DiskAmtProperty, BeamProperty, GlowProperty,
            })
                UpdateShaderValue(p);
        }

        public static BlackHoleShaderEffect Create(BlackHoleLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new BlackHoleShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(RotProperty, look.Rot);
            e.SetValue(ElevProperty, look.Elev);
            e.SetValue(DiskHotProperty, C(look.DiskHot));
            e.SetValue(DiskCoolProperty, C(look.DiskCool));
            e.SetValue(DiskAmtProperty, look.DiskAmt);
            e.SetValue(BeamProperty, look.Beam);
            e.SetValue(GlowProperty, look.Glow);
            return e;
        }
    }
}
