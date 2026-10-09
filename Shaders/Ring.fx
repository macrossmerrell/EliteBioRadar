// Planetary ring shader (ps_3_0) for WPF ShaderEffect. Compiled with Shaders\compile.ps1 -> Ring.ps.
//
// Draws up to three concentric ring bands (each with its own real ring-class colour) as a thin tilted disk seen at a shallow angle:
// fine radial ringlets and gaps from the shared noise texture, soft edges, a faint grain, the planet's shadow falling across the
// ring, and a dimmer look on the face turned away from the light. Two passes use the same shader: Mode 0 draws the whole ring
// (placed behind the planet); Mode 1 draws only the near half where it overlaps the planet disk (the sliver that passes in front).

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise

float  Seed    : register(c0);
float2 Center  : register(c1);    // planet centre in element UV
float2 Scale   : register(c2);    // one pixel in element UV
float  Tilt    : register(c3);    // ring plane rotation on screen, radians
float  Squash  : register(c4);    // minor/major axis ratio (= sin of the viewing elevation)
float  PlanetR : register(c5);    // planet radius in pixels
float  Mode    : register(c6);    // 0 = whole ring, 1 = front sliver over the planet
float3 LightDir: register(c7);    // in the ring-aligned view frame (x right, y up, z toward viewer)
float4 Band0   : register(c8);    // inner px, outer px, density, contrast
float4 Band1   : register(c9);
float4 Band2   : register(c10);
float4 Col0    : register(c11);   // rgb colour, a = grain amount
float4 Col1    : register(c12);
float4 Col2    : register(c13);

float3 Tex(float2 p) { return tex2D(Noise, frac(p)).rgb; }

// x = coverage (0..1, how much ring material), y = brightness variation
float2 BandSample(float4 band, float rpx, float s, out float edge)
{
    float width = max(band.y - band.x, 1.0);
    float t = (rpx - band.x) / width;
    edge = smoothstep(0.0, 0.035, t) * (1.0 - smoothstep(0.965, 1.0, t));
    float rn = rpx / max(PlanetR, 1.0);
    float a = Tex(float2(rn * 0.55 + s,        0.21)).x;
    float b = Tex(float2(rn * 2.3  + s * 1.7,  0.57)).y;
    float f = Tex(float2(rn * 11.0 + s * 2.3,  0.83)).z;
    float g = Tex(float2(rn * 40.0 + s * 3.1,  0.37)).x;
    float d = 0.42 * a + 0.30 * b + 0.20 * f + 0.08 * g;
    float cov = saturate((d - (0.50 - band.w * 0.20)) * (3.2 + band.w * 4.0) + 0.50);
    cov = lerp(1.0, cov, saturate(0.60 + band.w * 0.40));
    // A couple of clean divisions, like a real ring system's dark gaps (only in bands wide enough to hold them).
    float wide = smoothstep(22.0, 55.0, width);
    float g1 = 0.28 + 0.14 * frac(s * 7.31);
    float g2 = 0.62 + 0.18 * frac(s * 3.17);
    float gap = exp(-pow((t - g1) / 0.024, 2.0)) + 0.8 * exp(-pow((t - g2) / 0.016, 2.0));
    cov *= 1.0 - 0.92 * saturate(gap) * wide;
    // Density rises toward the inner part of a band, like the dense B ring.
    cov *= lerp(1.0, 0.72 + 0.28 * (1.0 - t), wide);
    float shade = 0.52 + 0.80 * (0.35 * a + 0.35 * b + 0.30 * f);
    return float2(cov * band.z, shade);
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 qp = (uv - Center) / Scale;                        // px from planet centre, y down
    float ct = cos(Tilt), st = sin(Tilt);
    float2 pl = float2(qp.x * ct + qp.y * st, -qp.x * st + qp.y * ct);   // ring-aligned screen frame
    float u = pl.x, v = pl.y / Squash;                        // position in the ring plane (v > 0 = near side)
    float rpx = sqrt(u * u + v * v);

    // Mask for the two passes.
    float planetDisk = 1.0 - smoothstep(PlanetR - 1.0, PlanetR + 0.5, length(qp));
    float passMask = lerp(1.0, planetDisk * smoothstep(-0.5, 1.5, v * Squash), Mode);

    // Combine the bands (they don't overlap; the last one covering a pixel wins).
    float3 col = 0.0;
    float  alpha = 0.0;
    float  grain = 0.0;

    float e0, e1, e2;
    float2 s0 = BandSample(Band0, rpx, Seed,        e0);
    float2 s1 = BandSample(Band1, rpx, Seed + 3.7,  e1);
    float2 s2 = BandSample(Band2, rpx, Seed + 7.9,  e2);
    float in0 = step(Band0.x, rpx) * step(rpx, Band0.y) * step(1.0, Band0.y);
    float in1 = step(Band1.x, rpx) * step(rpx, Band1.y) * step(1.0, Band1.y);
    float in2 = step(Band2.x, rpx) * step(rpx, Band2.y) * step(1.0, Band2.y);
    float a0 = s0.x * e0 * in0, a1 = s1.x * e1 * in1, a2 = s2.x * e2 * in2;
    float3 c0 = Col0.rgb * s0.y, c1 = Col1.rgb * s1.y, c2 = Col2.rgb * s2.y;
    alpha = max(a0, max(a1, a2));
    col = (c0 * a0 + c1 * a1 + c2 * a2) / max(a0 + a1 + a2, 1e-4);
    grain = (Col0.a * a0 + Col1.a * a1 + Col2.a * a2) / max(a0 + a1 + a2, 1e-4);

    // Fine particulate grain.
    float gr = Tex(float2(u * 0.021 + 0.3, v * 0.021 + 0.7)).z;
    col *= 1.0 + (gr - 0.5) * 0.55 * grain;

    // Lighting: face turned away from the light is dim; the planet's shadow falls on the ring.
    float sinE = Squash, cosE = sqrt(max(1.0 - Squash * Squash, 0.0));
    float3 P = float3(u, -v * sinE, v * cosE) / max(PlanetR, 1.0);
    float3 L = normalize(LightDir);
    float3 N = float3(0.0, cosE, sinE);
    float lit = lerp(0.38, 1.0, smoothstep(-0.12, 0.22, dot(L, N)));
    float b = dot(P, L);
    float disc = b * b - (dot(P, P) - 1.0);
    float shadow = smoothstep(0.0, 0.22, disc) * (1.0 - smoothstep(-0.04, 0.10, b));
    col *= lit * lerp(1.0, 0.16, shadow);

    alpha *= passMask;
    col *= alpha;
    return float4(col, alpha);
}
