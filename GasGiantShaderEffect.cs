using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace EliteBioRadar
{
    // Per-body look for the gas giant shader — everything the HLSL needs besides time.
    public sealed class GasGiantLook
    {
        public Color C0, C1, C2, C3, Band4, Glow;
        public double BandCount, Turb, StormAmt, BigStorm, Contrast, Tilt, Pitch, Seed;
    }

    // GPU gas giant surface (Shaders\GasGiant.fx, compiled to GasGiant.ps). Applied to a plain
    // Rectangle: the shader ignores that element's own pixels and draws the whole lit, rotating
    // sphere itself. Time is a normal dependency property, so a linear DoubleAnimation on it
    // drives the rotation entirely on the GPU - no timer redraw loop.
    public sealed class GasGiantShaderEffect : ShaderEffect
    {
        private static readonly PixelShader _shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/Shaders/GasGiant.ps", UriKind.Absolute),
        };

        private static ImageBrush? _noiseBrush;

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(GasGiantShaderEffect), 0);
        public static readonly DependencyProperty NoiseProperty =
            RegisterPixelShaderSamplerProperty("Noise", typeof(GasGiantShaderEffect), 1);

        private static DependencyProperty Reg<T>(string name, int register, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(GasGiantShaderEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty TimeProperty      = Reg("Time", 0, 0.0);
        public static readonly DependencyProperty SeedProperty      = Reg("Seed", 1, 1.0);
        public static readonly DependencyProperty CenterProperty    = Reg("Center", 2, new Point(0.5, 0.5));
        public static readonly DependencyProperty RadiusProperty    = Reg("Radius", 3, new Point(0.25, 0.25));
        public static readonly DependencyProperty Col0Property      = Reg("Col0", 4, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty Col1Property      = Reg("Col1", 5, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty Col2Property      = Reg("Col2", 6, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty Col3Property      = Reg("Col3", 7, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty Col4Property      = Reg("Col4", 8, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty GlowColProperty   = Reg("GlowCol", 9, new Point4D(0, 0, 0, 1));
        public static readonly DependencyProperty BandCountProperty = Reg("BandCount", 10, 10.0);
        public static readonly DependencyProperty TurbProperty      = Reg("Turb", 11, 1.0);
        public static readonly DependencyProperty StormAmtProperty  = Reg("StormAmt", 12, 0.5);
        public static readonly DependencyProperty LightDirProperty  = Reg("LightDir", 13, new Vector3D(-0.55, 0.55, 0.63));
        public static readonly DependencyProperty ContrastProperty  = Reg("Contrast", 14, 1.0);
        public static readonly DependencyProperty BigStormProperty  = Reg("BigStorm", 15, 0.0);
        public static readonly DependencyProperty TiltProperty      = Reg("Tilt", 16, 0.0);
        public static readonly DependencyProperty PitchProperty     = Reg("Pitch", 17, 0.0);

        public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

        public GasGiantShaderEffect()
        {
            PixelShader = _shader;
            // The Input sampler is required by WPF but unused by the shader.
            UpdateShaderValue(InputProperty);
            SetValue(NoiseProperty, NoiseBrush);
            UpdateShaderValue(NoiseProperty);
            foreach (var p in new[]
            {
                TimeProperty, SeedProperty, CenterProperty, RadiusProperty, Col0Property, Col1Property,
                Col2Property, Col3Property, Col4Property, GlowColProperty, BandCountProperty, TurbProperty,
                StormAmtProperty, LightDirProperty, ContrastProperty, BigStormProperty, TiltProperty, PitchProperty,
            })
                UpdateShaderValue(p);
        }

        public static GasGiantShaderEffect Create(GasGiantLook look, Point center, Point radius)
        {
            static Point4D C(Color c) => new Point4D(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1.0);
            var e = new GasGiantShaderEffect();
            e.SetValue(SeedProperty, look.Seed);
            e.SetValue(CenterProperty, center);
            e.SetValue(RadiusProperty, radius);
            e.SetValue(Col0Property, C(look.C0));
            e.SetValue(Col1Property, C(look.C1));
            e.SetValue(Col2Property, C(look.C2));
            e.SetValue(Col3Property, C(look.C3));
            e.SetValue(Col4Property, C(look.Band4));
            e.SetValue(GlowColProperty, C(look.Glow));
            e.SetValue(BandCountProperty, look.BandCount);
            e.SetValue(TurbProperty, look.Turb);
            e.SetValue(StormAmtProperty, look.StormAmt);
            e.SetValue(ContrastProperty, look.Contrast);
            e.SetValue(BigStormProperty, look.BigStorm);
            e.SetValue(TiltProperty, look.Tilt);
            e.SetValue(PitchProperty, look.Pitch);
            return e;
        }

        // ---- shared tileable noise texture ----
        internal static ImageBrush NoiseBrush
        {
            get
            {
                if (_noiseBrush == null)
                {
                    _noiseBrush = new ImageBrush(BuildNoiseTexture(256)) { Stretch = Stretch.Fill };
                    _noiseBrush.Freeze();
                }
                return _noiseBrush;
            }
        }

        private static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        // Value noise on a G x G lattice that wraps, so the texture tiles seamlessly.
        private static double TileNoise(double x, double y, int g, int seed)
        {
            double fx = x * g, fy = y * g;
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            double tx = Fade(fx - ix), ty = Fade(fy - iy);
            int x0 = ((ix % g) + g) % g, x1 = (x0 + 1) % g;
            int y0 = ((iy % g) + g) % g, y1 = (y0 + 1) % g;
            double a = Hash(x0, y0, seed), b = Hash(x1, y0, seed);
            double c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
        }

        private static BitmapSource BuildNoiseTexture(int size)
        {
            // Three independent fbm fields (R,G,B), alpha pinned to 255 so WPF's premultiply
            // step can't alter the data. Each channel is stretched to the full 0..1 range.
            var data = new double[size * size * 3];
            int[] baseG = { 4, 6, 8 };
            int[] octaves = { 4, 5, 5 };
            for (int ch = 0; ch < 3; ch++)
            {
                double min = double.MaxValue, max = double.MinValue;
                for (int py = 0; py < size; py++)
                    for (int px = 0; px < size; px++)
                    {
                        double x = px / (double)size, y = py / (double)size;
                        double v = 0, amp = 0.5, norm = 0;
                        int g = baseG[ch];
                        for (int o = 0; o < octaves[ch]; o++)
                        {
                            v += TileNoise(x, y, g, 17 + ch * 31 + o * 7) * amp;
                            norm += amp; amp *= 0.5; g *= 2;
                        }
                        v /= norm;
                        data[(py * size + px) * 3 + ch] = v;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                double range = Math.Max(1e-6, max - min);
                for (int i = ch; i < data.Length; i += 3) data[i] = (data[i] - min) / range;
            }

            var pixels = new byte[size * size * 4];
            for (int i = 0; i < size * size; i++)
            {
                pixels[i * 4 + 0] = (byte)(data[i * 3 + 2] * 255); // B
                pixels[i * 4 + 1] = (byte)(data[i * 3 + 1] * 255); // G
                pixels[i * 4 + 2] = (byte)(data[i * 3 + 0] * 255); // R
                pixels[i * 4 + 3] = 255;
            }
            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
