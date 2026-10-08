// White dwarf shader (ps_3_0) for WPF ShaderEffect. Compiled with Shaders\compile.ps1 -> WhiteDwarf.ps.
//
// Built from real in-game references: a huge blinding blue-white core with fine glare spikes and a wide bright halo, one thin
// light streak across the whole frame, and two enormous STRAIGHT conical jets - saturated white-hot down the middle with deep
// blue edges, opening steadily and holding their strength over a long distance (far more powerful than a neutron star's
// narrow, wavy plumes). The only motion is a slow outward drift of faint striations inside the cones; everything else is still.

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time    : register(c0);    // seconds
float  Seed    : register(c1);
float2 Center  : register(c2);    // core position in element UV
float2 Radius  : register(c3);    // one "unit" of length, in element UV
float  Tilt    : register(c4);    // jet axis angle from horizontal, radians
float4 CoreCol : register(c5);    // core / spikes / white-hot centre
float4 JetCol  : register(c6);    // cone body
float4 EdgeCol : register(c7);    // cone edges (deep blue)
float4 HaloCol : register(c8);    // sky glow
float  Power   : register(c9);    // 0.6..1.6 how strong the jets are
float  Spin    : register(c10);   // drift speed multiplier

float3 Tex(float2 p)
{
    return tex2D(Noise, frac(p)).rgb;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q  = (uv - Center) / Radius;
    float  r  = length(q);

    float ct = cos(Tilt), st = sin(Tilt);
    float2 ax = float2(ct, -st);
    float2 pe = float2(st, ct);
    float  a  = dot(q, ax);
    float  b  = dot(q, pe);
    float  aa = abs(a);

    float3 col = 0.0;

    // Sky glow: wide, bright, deep blue.
    float vig = pow(saturate(1.0 - length((uv - 0.5) * float2(2.0, 1.78))), 1.4);
    col += HaloCol.rgb * (0.62 * exp(-r * 0.55) + 0.30 * exp(-r * 0.20)) * vig;

    // Core: bigger and hotter than a neutron star's.
    col += CoreCol.rgb * (2.8 / (1.0 + pow(r * 6.0, 2.0)) + 1.4 * exp(-r * r * 22.0));

    // Glare spikes.
    float th = atan2(q.y, q.x);
    float spikes = pow(abs(cos(6.0 * th + 0.35)), 130.0) * exp(-r * 0.62) / (1.0 + r * 4.5);
    col += CoreCol.rgb * spikes * 1.1;

    // One light streak across the frame, faintly violet.
    float streak = exp(-abs(q.y) * 80.0) * (0.34 + 0.66 * exp(-abs(q.x) * 0.30));
    col += float3(0.45, 0.50, 1.0) * streak * 1.2;
    col += float3(0.30, 0.22, 0.85) * exp(-abs(q.y) * 6.5) * exp(-abs(q.x) * 0.4) * 0.07;

    // Jets: straight cones from a thin waist at the star, both poles identical.
    float wc   = 0.06 + 0.30 * aa;                 // cone half-width
    float prof = b / wc;                           // -1..1 across the cone
    float ap   = abs(prof);
    float body = 1.0 - smoothstep(0.62, 1.0, ap);  // fairly crisp edge
    float fall = exp(-aa * 0.15) * smoothstep(0.0, 0.07, aa) * (1.0 - smoothstep(2.3, 3.7, aa));   // ends dissolve well inside the frame

    // Faint striations drifting outward - the only animated element.
    float n = Tex(float2(aa * 0.30 - Time * 0.045 * Spin, prof * 1.2 + Seed * 0.1)).x;
    float stri = 0.84 + 0.26 * n;

    float beam = body * fall * stri * Power;
    float whiteHot = 1.0 - smoothstep(0.0, 0.66, ap);
    col += JetCol.rgb * beam * 1.0;
    col += CoreCol.rgb * whiteHot * beam * 2.0;                              // saturated white-hot middle
    col += EdgeCol.rgb * smoothstep(0.30, 0.9, ap) * body * fall * Power * 1.5;   // deep blue edges
    col += JetCol.rgb * exp(-pow(b / (wc * 1.9), 2.0)) * fall * 0.22 * Power;     // faint outer glow around the cone

    col = 1.0 - exp(-col * 1.6);
    float edge = smoothstep(0.0, 0.16, uv.x) * smoothstep(0.0, 0.16, 1.0 - uv.x)
               * smoothstep(0.0, 0.12, uv.y) * smoothstep(0.0, 0.12, 1.0 - uv.y);
    col *= edge;
    float alpha = saturate(max(col.r, max(col.g, col.b)));
    return float4(min(col, alpha), alpha);
}
