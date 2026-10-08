// Neutron star shader (ps_3_0) for WPF ShaderEffect. Compiled with Shaders\compile.ps1 -> NeutronStar.ps.
//
// Built from real in-game references: a blinding blue-white core with a soft halo and fine diffraction spikes, one thin
// light streak running the whole width of the frame, and two relativistic jets - a razor-thin, steady, white-blue neck
// near the star that opens into long turbulent lavender plumes which bend in a lazy S and curl at their far ends. Only the
// plumes move: strands twirl along the jet axis and the turbulence drifts outward; the core, neck, spikes and streak
// stay still. Noise comes from the shared tileable texture (sampler 1).

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time    : register(c0);    // seconds
float  Seed    : register(c1);
float2 Center  : register(c2);    // core position in element UV
float2 Radius  : register(c3);    // one "unit" of length, in element UV (x and y differ: scene isn't square)
float  Tilt    : register(c4);    // jet axis angle from horizontal, radians
float4 CoreCol : register(c5);    // core / spikes
float4 JetCol  : register(c6);    // jet neck
float4 TailCol : register(c7);    // plume
float4 HaloCol : register(c8);    // sky glow
float  TailAmt : register(c9);    // 0..1.5 plume strength
float  Spin    : register(c10);   // twirl speed multiplier

float3 Tex(float2 p)
{
    return tex2D(Noise, frac(p)).rgb;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q  = (uv - Center) / Radius;           // pixel-correct units around the core
    float  r  = length(q);

    float ct = cos(Tilt), st = sin(Tilt);
    float2 ax = float2(ct, -st);                  // jet axis (screen y points down, so +a runs up-right)
    float2 pe = float2(st, ct);
    float  a  = dot(q, ax);
    float  b  = dot(q, pe);
    float  aa = abs(a);
    float  sgn = a >= 0.0 ? 1.0 : -1.0;

    float3 col = 0.0;

    // Sky glow: a deep blue bloom around the star, wide and soft.
    float vig = pow(saturate(1.0 - length((uv - 0.5) * float2(2.0, 1.78))), 1.5);   // fades to nothing well inside the frame
    col += HaloCol.rgb * (0.50 * exp(-r * 0.70) + 0.22 * exp(-r * 0.24)) * vig;

    // Core: tiny, blinding, with a hot tight centre.
    col += CoreCol.rgb * (1.9 / (1.0 + pow(r * 8.0, 2.0)) + 0.9 * exp(-r * r * 36.0));

    // Fine diffraction spikes (short, thin) - the star's own glare, not a separate starburst element.
    float th = atan2(q.y, q.x);
    float spikes = pow(abs(cos(6.0 * th + 0.35)), 150.0) * exp(-r * 0.9) / (1.0 + r * 6.0);
    col += CoreCol.rgb * spikes * 0.9;

    // One light streak across the whole frame, faintly violet at its heart.
    float streak = exp(-abs(q.y) * 85.0) * (0.34 + 0.66 * exp(-abs(q.x) * 0.30));
    col += float3(0.45, 0.52, 1.0) * streak * 1.25;
    col += float3(0.30, 0.22, 0.85) * exp(-abs(q.y) * 7.0) * exp(-abs(q.x) * 0.45) * 0.06;

    // Jets: both poles identical, point-symmetric; the S-bend flips sign across the star.
    float bend = 0.17 * sin(aa * 0.85 + Seed) * smoothstep(0.7, 3.4, aa) * sgn;
    float bb   = b - bend;

    // Steady neck: razor thin and bright, fading with distance. No motion.
    float wn   = 0.012 + 0.028 * aa;
    float neck = exp(-pow(bb / wn, 2.0) * 1.3) * exp(-aa * 0.50) * smoothstep(0.0, 0.06, aa);
    col += lerp(CoreCol.rgb, JetCol.rgb, saturate(aa * 0.45)) * neck * 1.7;
    // Soft sheath hugging the neck.
    col += JetCol.rgb * exp(-pow(bb / (0.07 + 0.10 * aa), 2.0)) * exp(-aa * 0.9) * 0.22;

    // Plume: opens out beyond the neck, lavender, turbulent - and this is the only thing that animates.
    float env = smoothstep(0.65, 1.7, aa) * (1.0 - smoothstep(3.1, 4.7, aa));
    float wp  = 0.13 + 0.27 * max(aa - 0.6, 0.0);
    float body = exp(-pow(bb / wp, 2.0) * 1.15);

    float tw = Time * 0.85 * Spin;
    // Curl near the far end: noise coordinates swirl progressively.
    float vr = smoothstep(2.2, 3.8, aa) * (aa - 2.2) * 0.45 + sin(Time * 0.35 * Spin) * 0.18 * smoothstep(2.0, 3.4, aa);
    float cv = cos(vr), sv = sin(vr);
    float2 np = float2(aa * 0.75 - Time * 0.06 * Spin, bb / wp * 0.55);
    np = float2(np.x * cv - np.y * sv, np.x * sv + np.y * cv);
    float n1 = Tex(np * 0.9 + Seed * 0.07).x;
    float n2 = Tex(np * 2.1 + 0.37).y;
    float turb = 0.22 + 0.95 * pow(n1 * 0.6 + n2 * 0.4, 1.4);

    // Three intertwined strands twirling along the axis (the helical wisps in the references).
    float sw = 0.04 + 0.022 * aa;
    float amp = wp * 0.48;
    float s1 = exp(-pow((bb - amp * sin(aa * 2.6 - tw)) / sw, 2.0));
    float s2 = exp(-pow((bb - amp * sin(aa * 2.6 - tw + 2.094)) / sw, 2.0));
    float s3 = exp(-pow((bb - amp * sin(aa * 2.6 - tw + 4.189)) / sw, 2.0));
    float strands = (s1 + s2 + s3) * 0.30;

    float plume = env * body * (turb * 0.95 + strands) * TailAmt;
    col += TailCol.rgb * plume * 0.95;
    // A paler core running down the plume, so it reads luminous rather than flat.
    col += lerp(TailCol.rgb, float3(1.0, 1.0, 1.0), 0.45) * env * exp(-pow(bb / (wp * 0.35), 2.0)) * exp(-aa * 0.30) * 0.30 * TailAmt;

    // Soft tonemap, then premultiplied output with the frame edges faded to nothing.
    col = 1.0 - exp(-col * 1.15);
    float edge = smoothstep(0.0, 0.16, uv.x) * smoothstep(0.0, 0.16, 1.0 - uv.x)
               * smoothstep(0.0, 0.12, uv.y) * smoothstep(0.0, 0.12, 1.0 - uv.y);
    col *= edge;
    float alpha = saturate(max(col.r, max(col.g, col.b)));
    return float4(min(col, alpha), alpha);
}
