// Landable rocky world shader (ps_3_0) for WPF ShaderEffect - landable High Metal Content bodies
// first. Compiled with Shaders\compile.ps1 -> LandableWorld.ps.
//
// Built from real in-game references of airless / very thin-atmosphere bodies: sharp-edged
// highland and basin patches, fine regolith grain, rust-coloured streaks, pale frost/regolith
// patches, impact craters (some with bright radial rays), glowing orange flecks on magma-volcanic
// bodies, hard terminator and a barely-there limb haze. Noise comes from the shared tileable
// texture (sampler 1).

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time      : register(c0);   // seconds
float  Seed      : register(c1);
float2 Center    : register(c2);   // sphere centre in element UV
float2 Radius    : register(c3);   // sphere radius in element UV
float4 ColLight  : register(c4);   // highland tone, sRGB 0..1
float4 ColDark   : register(c5);   // basin tone
float4 ColAccent : register(c6);   // rust streaks
float4 ColBright : register(c7);   // pale regolith patches
float4 ColIce    : register(c8);   // frost
float4 ColHot    : register(c9);   // glowing flecks
float  Contrast  : register(c10);  // 0.4..1.4 highland/basin contrast
float  CraterAmt : register(c11);  // 0..1 how many craters
float  RayAmt    : register(c12);  // 0..1 share of craters with bright rays
float  RustAmt   : register(c13);  // 0..1
float  BrightAmt : register(c14);  // 0..1 pale patches (0 = none)
float  IceAmt    : register(c15);  // 0..1 frost (0 = none)
float  HotAmt    : register(c16);  // 0..1 glowing flecks (0 = none)
float  AtmGlow   : register(c17);  // 0..1 thin-atmosphere rim (0 = airless)
float  Tilt      : register(c18);
float  Pitch     : register(c19);
float3 LightDir  : register(c20);  // normalized, view space
float  Mottle    : register(c21);  // pattern scale multiplier
float  SpecAmt   : register(c22);  // 0..1 glossy sun-glint (icy surfaces are shiny, bare rock is not)
float  LineAmt   : register(c23);  // 0..1 long thin fracture lines drawn in ColIce (icy crevasses; 0 = none)

static const float PI  = 3.14159265;
static const float TAU = 6.28318531;

float3 Tex(float2 p)
{
    return tex2D(Noise, frac(p)).rgb;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q = (uv - Center) / Radius;
    float r2 = dot(q, q);
    float r  = sqrt(r2);
    float z  = sqrt(saturate(1.0 - r2));
    float3 P = float3(q.x, -q.y, z);

    float ct = cos(Tilt), st = sin(Tilt);
    float3 Pz = float3(P.x * ct - P.y * st, P.x * st + P.y * ct, P.z);
    float cp = cos(Pitch), sp = sin(Pitch);
    float3 Pt = float3(Pz.x, Pz.y * cp - Pz.z * sp, Pz.y * sp + Pz.z * cp);

    float lat = asin(clamp(Pt.y, -0.999, 0.999));
    float lonW = atan2(Pt.x, Pt.z) - Time * 0.006;
    float2 ps = float2(lonW / PI, lat / PI * 2.0);
    float polar = smoothstep(1.20, 1.45, abs(lat));       // hide the lat/lon pinch at the poles

    // ---------------- terrain ----------------
    float big   = Tex(ps * 0.8 * Mottle + Seed * 0.13).x;
    float mid   = Tex(ps * 2.2 * Mottle + 0.31).y;
    float fine  = Tex(ps * 9.0 + 0.7).z;
    float fine2 = Tex(ps * 17.0 + 0.2).x;
    float reg   = lerp(big * 0.6 + mid * 0.4, 0.5, polar);

    // Sharp-edged highlands vs basins.
    float edgeW = 0.04 + 0.10 / (0.5 + Contrast);
    float hl = smoothstep(0.50 - edgeW, 0.50 + edgeW, reg);
    float3 base = lerp(ColDark.rgb, ColLight.rgb, hl);
    base = lerp(lerp(ColDark.rgb, ColLight.rgb, 0.5), base, saturate(Contrast));
    base *= lerp(1.0, 0.86 + 0.28 * mid, saturate(Contrast + 0.25));   // low-contrast looks stay smooth
    base *= 0.92 + 0.16 * fine2;

    // Rust streaks: contour lines of a warped field.
    float rz = Tex(ps * 3.0 * Mottle + float2(0.5, 0.1) + (mid - 0.5) * 0.6).z;
    float ridge = 1.0 - abs(rz * 2.0 - 1.0);
    float rust = smoothstep(0.86, 0.96, ridge) * RustAmt * (1.0 - hl * 0.4) * (1.0 - polar);
    base = lerp(base, ColAccent.rgb, rust * 0.85);

    // Long fracture lines (icy crevasses): arcs of random great circles, wobbled by noise, broken into
    // segments and varying in width, drawn in ColIce. Rotates with the planet.
    float spin = Time * 0.006;
    float3 Pr = float3(Pt.x * cos(spin) + Pt.z * sin(spin), Pt.y, -Pt.x * sin(spin) + Pt.z * cos(spin));
    float lines = 0.0;
    for (int ci = 0; ci < 7; ci++)
    {
        float3 hn = frac(sin(float3(Seed * 3.7 + ci * 12.9, Seed * 8.1 + ci * 7.3, Seed * 1.3 + ci * 21.7))
                         * float3(43758.5, 22578.1, 31415.9));
        float3 N = normalize(hn * 2.0 - 1.0 + 1e-3);
        float wob = (Tex(ps * 1.6 + ci * 0.37).x - 0.5) * 0.12 + (Tex(ps * 4.5 + ci * 0.19).y - 0.5) * 0.02;
        float dcirc = abs(dot(Pr, N) + wob);
        float w = 0.014 + 0.012 * Tex(ps * 2.0 + ci * 0.51).z;
        float seg = smoothstep(0.40, 0.62, Tex(ps * 2.2 + float2(ci * 0.31, 0.7)).y);
        lines += smoothstep(w, w * 0.35, dcirc) * seg;
    }
    base = lerp(base, ColIce.rgb, saturate(lines) * LineAmt * (1.0 - polar) * 0.92);

    // Pale regolith patches, concentrated in the lighter ground.
    float bp = Tex(ps * 1.6 * Mottle + 0.9).x * 0.6 + Tex(ps * 5.5 + 0.4).y * 0.4;
    float bthr = 0.78 - 0.20 * BrightAmt;
    float bmask = smoothstep(bthr, bthr + 0.07, bp) * step(0.001, BrightAmt) * (0.55 + 0.45 * hl);
    base = lerp(base, ColBright.rgb * (0.90 + 0.10 * fine2), bmask * 0.90);

    // Frost: favours the poles and low ground, ragged edges.
    float iceN = (Tex(ps * 3.0 + 0.6).x - 0.5) * 0.8 + (Tex(ps * 9.0 + 0.1).y - 0.5) * 0.25;
    float ice = smoothstep(0.0, 0.10, IceAmt * (0.45 + abs(Pt.y) * 0.95) - 0.55 + iceN - hl * 0.10) * step(0.001, IceAmt);
    base = lerp(base, ColIce.rgb * (0.92 + 0.10 * fine2), ice * 0.92);

    // ---------------- craters ----------------
    float shade = 1.0;
    float rays = 0.0;
    for (int i = 0; i < 9; i++)
    {
        float3 h = frac(sin(float3(Seed * 9.1 + i * 13.3, Seed * 4.7 + i * 27.9, Seed * 1.9 + i * 19.1))
                        * float3(43758.5, 22578.1, 31415.9));
        float exists = step(1.0 - 0.95 * CraterAmt, frac(h.z * 5.1 + 0.3));
        float cLat = (h.x - 0.5) * 2.1;
        float dLon = lonW - (h.y * TAU - PI);
        dLon = (frac(dLon / TAU + 0.5) - 0.5) * TAU;
        float dx = dLon * cos(cLat);
        float dy = lat - cLat;
        float R0 = 0.07 + pow(h.z, 1.8) * 0.30;
        float rr = sqrt(dx * dx + dy * dy) / R0;

        float bowl = smoothstep(1.0, 0.45, rr);
        float rim  = smoothstep(0.78, 1.0, rr) * smoothstep(1.28, 1.0, rr);
        shade *= 1.0 - bowl * 0.38 * exists;
        shade += rim * 0.18 * exists;

        // Bright radial rays on a share of the craters.
        float ang = atan2(dy, dx);
        float darkRays = step(RayAmt, 0.0);   // negative RayAmt = thin dark spokes (real HMC craters) instead of soft bright rays
        float rayN = Tex(float2(ang / TAU * lerp(7.0, 26.0, darkRays) + h.x * 3.0, 0.31 + h.y)).x;
        float rayOn = step(1.0 - abs(RayAmt) * 0.55, frac(h.y * 3.7 + 0.11));   // few craters carry rays
        rays += smoothstep(lerp(0.52, 0.66, darkRays), lerp(0.90, 0.78, darkRays), rayN) * smoothstep(lerp(8.0, 2.6, darkRays), 1.2, rr) * smoothstep(0.9, 1.3, rr) * rayOn * exists;
    }
    base *= shade;
    base = (RayAmt < 0.0) ? lerp(base, base * 0.78, saturate(rays) * 0.55)
                          : lerp(base, base * 1.55 + 0.06, saturate(rays) * 0.85);

    // ---------------- relief (cheap bump shading from the mid-scale noise) ----------------
    float e = 0.012;
    float mx = Tex((ps + float2(e, 0.0)) * 2.2 * Mottle + 0.31).y;
    float my = Tex((ps + float2(0.0, e)) * 2.2 * Mottle + 0.31).y;
    float2 grad = float2(mx - mid, my - mid) / e;
    float2 L2 = normalize(LightDir.xy + 1e-4);
    float relief = saturate(0.5 + 0.5 * dot(grad * float2(1.0, -1.0), L2) * 0.06) * 2.0 - 1.0;
    base *= 1.0 + relief * (0.10 + 0.20 * saturate(Contrast));

    // ---------------- lighting ----------------
    float ndl  = dot(P, normalize(LightDir));
    float lit  = lerp(0.05, 1.0, smoothstep(-0.18, 0.72, ndl));
    float limb = 0.80 + 0.20 * pow(z, 0.5);
    float3 col = base * lit * limb;

    // Glossy sun-glint on icy surfaces: a sparkly highlight where the surface mirrors the light back.
    float3 Hh = normalize(normalize(LightDir) + float3(0.0, 0.0, 1.0));
    float spec = pow(saturate(dot(P, Hh)), 40.0) * SpecAmt * (0.35 + 1.1 * fine2) * smoothstep(0.0, 0.25, ndl);
    col += float3(1.0, 1.0, 0.96) * spec * 0.85;

    // Glowing flecks on magma-volcanic bodies (emissive, so they survive the shading).
    float hotN = fine2 * 0.35 + Tex(ps * 4.0 + 0.3).y * 0.65;
    float hotMask = HotAmt * smoothstep(0.72, 0.84, hotN) * (1.0 - hl * 0.6) * (1.0 - polar);
    col += ColHot.rgb * hotMask * (0.55 + 0.45 * sin(Time * 1.3 + fine * 20.0)) * (0.45 + 0.55 * saturate(1.0 - lit + 0.3));

    // Faint haze rim for bodies with a trace atmosphere.
    float rimGlow = pow(1.0 - z, 3.0) * AtmGlow * saturate(ndl * 0.8 + 0.4);
    col += float3(0.42, 0.58, 0.50) * rimGlow * 0.55;

    float2 dirOut = q / max(r, 1e-3);
    float side = saturate(dot(float2(dirOut.x, -dirOut.y), normalize(LightDir.xy + 1e-4)) * 0.5 + 0.6);
    float halo = exp(-max(r - 1.0, 0.0) * 38.0) * 0.30 * side * AtmGlow;

    float edge = 1.0 - smoothstep(0.985, 1.0, r);
    float3 rgb = saturate(col) * edge + float3(0.42, 0.58, 0.50) * halo * (1.0 - edge);
    float alpha = saturate(edge + halo * (1.0 - edge));
    return float4(min(rgb, alpha + 0.0001), alpha);          // premultiplied alpha
}
