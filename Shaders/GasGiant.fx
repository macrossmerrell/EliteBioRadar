// Gas giant surface shader (ps_3_0) for WPF ShaderEffect.
// Compiled with Shaders\compile.ps1 -> GasGiant.ps (checked in; the build never runs fxc).
//
// Renders a lit sphere: latitude bands with turbulent, domain-warped boundaries, per-latitude
// differential rotation (jets alternate direction, which is what makes real gas giant bands
// shear and swirl at their edges), seeded storm ovals that ride their own latitude's wind,
// diffuse lighting with a soft terminator, limb darkening, and a thin atmospheric rim.
// Noise comes from a small tileable texture (sampler 1) instead of per-pixel hashing to
// stay well inside ps_3_0 instruction limits.

sampler2D Input : register(s0);   // unused (required by WPF); the host element is a plain fill
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time      : register(c0);  // seconds
float  Seed      : register(c1);  // body-derived
float2 Center    : register(c2);  // sphere center, in element UV (0..1)
float2 Radius    : register(c3);  // sphere radius in element UV (x and y differ: scene isn't square)
float4 Col0      : register(c4);  // palette, sRGB 0..1 (dark band)
float4 Col1      : register(c5);
float4 Col2      : register(c6);  // light band
float4 Col3      : register(c7);  // accent band
float4 Col4      : register(c8);  // dark streak
float4 GlowCol   : register(c9);  // atmosphere rim
float  BandCount : register(c10); // roughly how many bands pole to pole
float  Turb      : register(c11); // 0..1.5 turbulence / swirl strength
float  StormAmt  : register(c12); // 0..1
float3 LightDir  : register(c13); // normalized, view space (x right, y up, z toward viewer)
float  Contrast  : register(c14); // 0..1.2 band contrast (cloudless classes are low)
float  BigStorm  : register(c15); // 0 = none, 1 = one large swirling Great-Red-Spot style storm
float  Tilt      : register(c16); // radians; real giants' bands run visibly diagonal
float  Pitch     : register(c17); // radians about the horizontal axis; ~1.4 = viewed nearly pole-on

static const float PI  = 3.14159265;
static const float TAU = 6.28318531;

float3 Tex(float2 p)
{
    return tex2D(Noise, frac(p)).rgb;
}

// East-west wind speed at a latitude (radians). Alternating jets + a gentle equatorial
// speed-up, in radians per second of planet rotation.
float WindSpeed(float lat)
{
    float jet = sin(lat * BandCount + Seed * 6.0);
    float s = cos(lat);
    return 0.030 + 0.016 * jet + 0.010 * s * s;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q = (uv - Center) / Radius;       // unit disc, y down
    float r2 = dot(q, q);
    float r  = sqrt(r2);
    float z  = sqrt(saturate(1.0 - r2));
    float3 P = float3(q.x, -q.y, z);         // point on the sphere, y up

    // Axial tilt (seeded by the host) so the bands run diagonal like the in-game giants.
    float ct = cos(Tilt), st = sin(Tilt);
    float3 Pz = float3(P.x * ct - P.y * st, P.x * st + P.y * ct, P.z);
    float cp = cos(Pitch), sp = sin(Pitch);
    float3 Pt = float3(Pz.x, Pz.y * cp - Pz.z * sp, Pz.y * sp + Pz.z * cp);

    float lat = asin(clamp(Pt.y, -0.999, 0.999));
    float lon = atan2(Pt.x, Pt.z);

    // Differential rotation: features sit at fixed world longitude and the planet turns,
    // each latitude at its own wind speed.
    float lonW = lon - Time * WindSpeed(lat);
    float u = lonW / TAU;
    float v = lat / PI + 0.5;

    // Domain warp of the latitude coordinate -> wavy, uneven band edges.
    float3 nA = Tex(float2(u * 1.4, v * 3.0) + Seed * 0.173);   // low horizontal frequency: edges undulate gently, no zig-zag
    float warp = (nA.x - 0.5) * 0.22 * Turb + (nA.y - 0.5) * 0.08 * Turb;

    float bandArg = (lat + warp * 0.5) * BandCount + Seed * 6.0;
    float band    = sin(bandArg) * 0.65 + sin(bandArg * 2.3 + 1.7) * 0.35;   // -1..1

    // Swirls concentrate where the wind shear is strongest (band edges).
    float shear   = pow(abs(cos(bandArg)), 2.0);
    float3 nB     = Tex(float2(u * 6.0, v * 24.0) + 0.37);
    float swirl   = (nB.y - 0.5) * shear * Turb * 0.045;   // was 0.10: dragged the streaks into long strings

    // Each belt/zone gets its own random brightness, with a fairly sharp edge between them
    // (real giants have distinct dark belts next to pale zones, not a smooth sine ramp).
    float bandIdx = bandArg / PI;
    float bid = floor(bandIdx);
    float bf  = frac(bandIdx);
    float r0 = frac(sin((bid - 1.0) * 12.9898 + Seed) * 43758.5453);
    float r1 = frac(sin(bid * 12.9898 + Seed) * 43758.5453);
    float bandRnd = lerp(r0, r1, smoothstep(0.0, 0.12, bf));

    // East-west sheared streaks (low horizontal frequency, moderate vertical) and blotches.
    float3 nC = Tex(float2((u + swirl) * 2.5, v * 12.0) + 0.71);
    float3 nD = Tex(float2((u + swirl * 2.0) * 1.5, v * 10.0) + 0.13);
    float streak  = nC.z;
    float blotch  = nD.x;

    float t = 0.5 + band * 0.30 * Contrast
                  + (bandRnd - 0.5) * 0.60 * Contrast
                  + (blotch - 0.5) * 0.30 * (0.4 + Turb * 0.6)
                  + (streak - 0.5) * 0.14;
    t = saturate(t);

    // Palette ramp: dark -> mid -> light -> accent.
    float3 lowRamp  = lerp(Col0.rgb, Col1.rgb, saturate(t * 2.0));
    float3 highRamp = lerp(Col2.rgb, Col3.rgb, saturate((t - 0.6) * 2.5));
    float3 col = lerp(lowRamp, highRamp, smoothstep(0.30, 0.70, t));

    // Dark streaks between bands.
    float darkMask = saturate((nC.y - 0.60) * 2.5) * 0.45 * Contrast * shear;
    col = lerp(col, Col4.rgb, darkMask);

    // Storm ovals: each rides the wind speed of its own latitude.
    // Many small ovals (some dark, some pale, often in rows along a band) plus, optionally,
    // one big swirling storm - both read clearly in the in-game Class I / IV references.
    for (int i = 0; i < 8; i++)
    {
        float3 h = frac(sin(float3(Seed * 13.7 + i * 17.1, Seed * 7.3 + i * 31.9, Seed * 3.1 + i * 11.3))
                        * float3(43758.5, 22578.1, 31415.9));
        float sLat  = (h.x - 0.5) * 1.25;
        float sLon0 = h.y * TAU;
        float big   = (i == 0) ? 1.0 : 0.0;
        float size  = big > 0.5 ? 0.30 : (0.055 + h.z * 0.11);
        float ax = size, ay = size * (big > 0.5 ? 0.55 : 0.85);
        float exists = big > 0.5 ? BigStorm : step(0.80 - 0.62 * StormAmt, frac(h.z * 7.3));

        float d = (lon - Time * WindSpeed(sLat)) - sLon0;
        d = (frac(d / TAU + 0.5) - 0.5) * TAU;                 // wrap to -pi..pi
        float dx = d * cos(sLat) / ax;
        float dy = (lat - sLat) / ay;
        float rr = sqrt(dx * dx + dy * dy);

        // Wider footprint than the core so spiral arms trail out past the oval.
        float reach = smoothstep(1.55, 0.50, rr);
        float inner = smoothstep(1.0, 0.45, rr);
        float rim   = smoothstep(0.55, 0.85, rr) * smoothstep(1.12, 0.92, rr);
        float dark  = step(0.5, frac(h.y * 5.7));
        float3 tone = dark > 0.5 ? Col4.rgb : lerp(Col3.rgb, float3(1.0, 1.0, 1.0), 0.55);

        // Cyclonic swirl: the twist grows toward the eye, direction flips with hemisphere,
        // and the whole pattern turns slowly with time.
        float hemi  = sLat >= 0.0 ? 1.0 : -1.0;
        float twist = (1.6 - rr) * (3.2 + h.z * 2.0) * hemi;
        float spin  = Time * 0.22 * hemi;
        float ang   = atan2(dy, dx);
        float arms  = big > 0.5 ? 2.0 : 3.0;
        float spiral = 0.5 + 0.5 * sin(arms * (ang + twist - spin));
        // Noise sampled in the twisted frame gives wispy, curved cloud streaks.
        float cs = cos(twist - spin), sn = sin(twist - spin);
        float2 rot = float2(dx * cs - dy * sn, dx * sn + dy * cs);
        float wisp = Tex(rot * 0.9 + i * 0.37 + Seed).x;
        float swirlTex = saturate(spiral * 0.65 + wisp * 0.55);

        float amt = exists;
        float body = inner * lerp(0.55, 1.0, swirlTex);
        col = lerp(col, tone, body * amt * 0.80);
        // Dark spiral arms trailing into the surrounding clouds.
        col = lerp(col, Col4.rgb, reach * (1.0 - inner) * spiral * wisp * amt * 0.45);
        col *= 1.0 - rim * amt * 0.22;
    }

    // Lighting: wrapped diffuse so the night side stays readable, plus limb darkening.
    float ndl  = dot(P, normalize(LightDir));
    float lit  = lerp(0.16, 1.0, smoothstep(-0.30, 0.85, ndl));
    float limb = 0.55 + 0.45 * pow(z, 0.55);
    col *= lit * limb;

    // Thin atmospheric rim, brighter on the lit side.
    float rimGlow = pow(1.0 - z, 3.0) * saturate(ndl * 0.8 + 0.5) * 0.45;
    col += GlowCol.rgb * rimGlow;

    float edge = 1.0 - smoothstep(0.985, 1.0, r);
    return float4(saturate(col) * edge, edge);                  // premultiplied alpha
}
