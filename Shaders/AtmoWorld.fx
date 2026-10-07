// Thick-atmosphere rocky world shader (ps_3_0) for WPF ShaderEffect - non-landable High Metal
// Content bodies first. Compiled with Shaders\compile.ps1 -> AtmoWorld.ps.
//
// Built from real in-game references: a tinted surface (ochre / rust / brown / olive / dark navy,
// with mottled highlands and darker basins) seen THROUGH a haze whose opacity follows the body's
// surface pressure (very dense CO2 is a smooth pale blue-grey; thinner atmospheres show the surface),
// streaky wind-sheared cloud bands, white cyclones with dark eyes, cream ice caps that grow with
// cold, and a bright blue-white limb. Noise comes from the shared tileable texture (sampler 1).

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time         : register(c0);   // seconds
float  Seed         : register(c1);
float2 Center       : register(c2);   // sphere centre in element UV
float2 Radius       : register(c3);   // sphere radius in element UV
float4 Surf0        : register(c4);   // dark surface tone, sRGB 0..1
float4 Surf1        : register(c5);   // light surface tone
float4 HazeCol      : register(c6);   // dense-atmosphere haze tint
float4 CloudCol     : register(c7);   // cloud white
float4 CapCol       : register(c8);   // ice cap
float4 GlowCol      : register(c9);   // limb glow
float  HazeAmt      : register(c10);  // 0..1 how much the atmosphere hides the surface
float  SurfContrast : register(c11);  // 0..1.5 surface albedo contrast
float  CycloneAmt   : register(c12);  // 0..1
float  CloudAmt     : register(c13);  // 0..1
float  CapAmt       : register(c14);  // 0..1 ice cap size (0 = none)
float  Tilt         : register(c15);  // axial tilt about the view axis, radians
float  Pitch        : register(c16);  // radians about the horizontal axis (exposes a pole)
float3 LightDir     : register(c17);  // normalized, view space
float  CrackAmt     : register(c18);  // 0..1 fine fracture lines in an ice shell (0 = none)
float  FleckAmt     : register(c19);  // 0..1 clusters of small dark flecks on the ice (0 = none)
float  HotAmt       : register(c20);  // 0..1 glowing red-hot lava patches (very hot bodies; 0 = none)
float  LandAmt      : register(c21);  // 0..1 continent coverage over an ocean (Earth-like worlds; 0 = none)
float4 LandCol0     : register(c22);  // lowland (green)
float4 LandCol1     : register(c23);  // coast / dry highland (tan)
float  GlintAmt     : register(c24);  // 0..1 sun glint on the ocean

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
    float lon = atan2(Pt.x, Pt.z);

    // ---------------- surface (rotates slowly) ----------------
    float2 ps = float2((lon - Time * 0.010) / PI, lat / PI * 2.0);
    float big  = Tex(ps * 0.9 + Seed * 0.13).x;
    float mid  = Tex(ps * 2.3 + 0.31).y;
    float fine = Tex(ps * 7.0 + 0.7).z;
    float alb  = big * 0.55 + mid * 0.30 + fine * 0.15;
    float polar = smoothstep(1.20, 1.45, abs(lat));   // fade detail at the poles (lat/lon pinches there)
    alb = saturate((alb - 0.5) * (1.6 * SurfContrast + 0.4) + 0.5);
    alb = lerp(alb, 0.5, polar);

    float3 surf = lerp(Surf0.rgb, Surf1.rgb, alb);
    float basin = smoothstep(0.62, 0.34, Tex(ps * 1.4 + 0.77 + Seed * 0.05).z);
    surf = lerp(surf, Surf0.rgb * 0.70, basin * 0.50 * SurfContrast);
    surf *= 0.94 + 0.12 * fine;

    // ---------------- continents (Earth-like worlds) ----------------
    // Integer x-scales keep the noise seamless around the longitude wrap; the warp breaks up grid alignment.
    float2 cw = ps + (Tex(ps * float2(1.0, 1.0) + 0.41).xy - 0.5) * 0.30;
    float cn = Tex(cw * float2(1.0, 1.0) + Seed * 0.11).x * 0.44 + Tex(cw * float2(2.0, 2.0) + 0.23).y * 0.28
             + Tex(cw * float2(4.0, 4.0) + 0.61).z * 0.18 + Tex(cw * float2(8.0, 8.0) + 0.37).x * 0.10;
    float thr  = 0.5 + (0.40 - LandAmt) * 0.35;
    float hasL = step(0.001, LandAmt);
    float land = smoothstep(thr - 0.010, thr + 0.010, cn) * hasL;
    float shallow = smoothstep(thr - 0.080, thr - 0.004, cn) * (1.0 - land) * hasL;
    surf = lerp(surf, Surf1.rgb * 1.25 + float3(0.0, 0.03, 0.03), shallow * 0.40);
    float lt  = Tex(cw * float2(2.0, 2.0) + 0.80).y * 0.6 + Tex(cw * float2(8.0, 8.0) + 0.30).z * 0.4;
    float3 lcol = lerp(LandCol0.rgb, LandCol1.rgb, smoothstep(0.58, 0.85, lt) * 0.55);
    float coastB = land * (1.0 - smoothstep(thr + 0.004, thr + 0.050, cn));
    lcol = lerp(lcol, LandCol1.rgb * 1.08, coastB * 0.55);
    lcol = lerp(lcol, LandCol0.rgb * 0.72, smoothstep(thr + 0.07, thr + 0.17, cn) * 0.45);
    lcol *= 0.86 + 0.28 * Tex(cw * float2(16.0, 16.0) + 0.5).z;
    surf = lerp(surf, lcol, land * (1.0 - polar * 0.5));

    // Dense atmosphere: contrast washes out toward the mean tone, then the haze tint takes over.
    float3 meanSurf = 0.5 * (Surf0.rgb + Surf1.rgb);
    float3 col = lerp(surf, meanSurf, HazeAmt * 0.65);
    col = lerp(col, HazeCol.rgb * (0.88 + 0.22 * alb), HazeAmt * 0.88);

    // ---------------- fine cracks (ice shell fractures) ----------------
    float cz  = Tex(ps * 4.0 + 0.27 + (mid - 0.5) * 0.4).z;
    float cz2 = Tex(ps * 9.0 + 0.60).x;
    float crack = smoothstep(0.945, 0.985, 1.0 - abs(cz * 2.0 - 1.0)) + 0.5 * smoothstep(0.96, 0.99, 1.0 - abs(cz2 * 2.0 - 1.0));
    col *= 1.0 - saturate(crack) * CrackAmt * (1.0 - polar) * 0.14;

    // ---------------- dark fleck clusters ----------------
    float2 wps = ps + (float2(mid, big) - 0.5) * 0.45;      // warp so the flecks do not line up with the texture tiling
    float fMask = smoothstep(0.42, 0.62, Tex(wps * 1.8 + 0.45).z);
    float fDot  = smoothstep(0.72, 0.86, Tex(wps * 9.0 + 0.12).x * 0.55 + Tex(wps * 17.0 + 0.80).y * 0.45);
    col = lerp(col, Surf0.rgb * 0.40, saturate(fMask * fDot * FleckAmt * 3.0) * (1.0 - polar) * (1.0 - HazeAmt));

    // ---------------- ice caps ----------------
    float edgeN = (Tex(ps * 3.0 + 0.2).x - 0.5) * 0.30 + (Tex(ps * 8.0 + 0.9).y - 0.5) * 0.10;
    float capEdge = 1.0 - CapAmt * 0.55;
    float cap = smoothstep(0.0, 0.05, abs(Pt.y) - capEdge + edgeN) * step(0.001, CapAmt);
    cap *= 0.80 + 0.20 * Tex(ps * 6.0 + 0.4).x;
    col = lerp(col, CapCol.rgb * (0.92 + 0.10 * fine), cap * 0.95);

    // ---------------- clouds (own differential wind) ----------------
    float wind = Time * 0.020 * (1.0 + 0.25 * sin(lat * 4.0 + Seed * 3.0));
    float lonC = lon - wind;
    float2 pc  = float2(lonC / PI, lat / PI * 2.0);
    float2 warp = Tex(pc * 0.8 + Seed * 0.07).xy - 0.5;
    float streakA = Tex(float2(pc.x * 3.2 + warp.x * 0.50, pc.y * 3.5 + warp.y * 0.45) + 0.50).x;
    float puff    = Tex(float2(pc.x * 4.0, pc.y * 4.5) + warp * 0.7 + 0.20).y;
    float cloud   = smoothstep(0.50, 0.88, streakA * 0.40 + puff * 0.60);
    // Heavier haze = smoother flow, fewer distinct puffs.
    cloud *= CloudAmt * (1.0 - 0.55 * HazeAmt) * (1.0 - polar);

    // Cyclones: white spiral storms with a dark eye, riding the same wind.
    float cyc = 0.0, eye = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float3 h = frac(sin(float3(Seed * 11.7 + i * 17.3, Seed * 5.9 + i * 29.1, Seed * 2.3 + i * 13.7))
                        * float3(43758.5, 22578.1, 31415.9));
        float exists = step(1.0 - 1.05 * CycloneAmt, frac(h.z * 7.3 + 0.17));
        float sLat = (h.x - 0.5) * 1.5;
        float size = 0.075 + h.z * 0.075;   // modest storms; the earlier 0.12-0.24 made a lone storm look like a planet-sized eye
        float d = (lon - Time * 0.020 * (1.0 + 0.25 * sin(sLat * 4.0 + Seed * 3.0))) - (float(i) + 0.6 * h.y) / 12.0 * TAU;   // spread around the globe
        d = (frac(d / TAU + 0.5) - 0.5) * TAU;
        float dx = d * cos(sLat) / size;
        float dy = (lat - sLat) / size;
        float rr = sqrt(dx * dx + dy * dy);

        float hemi = sLat >= 0.0 ? 1.0 : -1.0;
        float twist = (1.8 - rr) * 3.6 * hemi;
        float a = atan2(dy, dx);
        float spiral = 0.5 + 0.5 * sin(2.0 * (a + twist - Time * 0.25 * hemi));
        float cs = cos(twist), sn = sin(twist);
        float wisp = Tex(float2(dx * cs - dy * sn, dx * sn + dy * cs) * 0.8 + i * 0.37 + Seed).x;
        float body = smoothstep(1.55, 0.25, rr) * (0.50 + 0.50 * saturate(spiral * 0.6 + wisp * 0.55));
        cyc += body * exists;
        eye += smoothstep(0.22, 0.0, rr) * exists;
    }
    cyc = saturate(cyc);

    float3 cloudCol = CloudCol.rgb * (0.88 + 0.12 * Tex(pc * 5.0 + 0.6).x);
    float cloudTotal = saturate(cloud + cyc);
    col = lerp(col, cloudCol, cloudTotal * 0.92);
    col *= 1.0 - saturate(eye) * 0.45;

    // ---------------- lighting ----------------
    float ndl  = dot(P, normalize(LightDir));
    float lit  = lerp(0.10, 1.0, smoothstep(-0.28, 0.80, ndl));
    float limb = 0.70 + 0.30 * pow(z, 0.5);
    col *= lit * limb;

    // Sun glint on open water: a broad soft sheen plus a tighter core, hidden by land and cloud.
    float3 Hh = normalize(normalize(LightDir) + float3(0.0, 0.0, 1.0));
    float nh = saturate(dot(P, Hh));
    float glint = (pow(nh, 12.0) * 0.28 + pow(nh, 80.0) * 0.50) * GlintAmt * (1.0 - land) * (1.0 - saturate(cloudTotal * 1.2)) * (1.0 - cap);
    col += float3(0.82, 0.90, 1.0) * glint * (0.85 + 0.30 * fine);

    // Bright blue-white limb haze, stronger the denser the atmosphere.
    float rimGlow = pow(1.0 - z, 2.4) * saturate(ndl * 0.8 + 0.45) * (0.45 + 0.55 * HazeAmt);
    col += GlowCol.rgb * rimGlow;

    // Glowing lava patches: emissive, so they survive the shading and show on the night side too.
    float hotN = Tex(ps * 2.2 + 0.2).x * 0.55 + Tex(ps * 7.0 + 0.5).y * 0.45;
    float hotM = smoothstep(0.74, 0.86, hotN) * HotAmt * (1.0 - polar);
    col += float3(1.0, 0.16, 0.07) * hotM * (0.65 + 0.35 * sin(Time * 0.9 + fine * 20.0)) * (0.6 + 0.4 * Tex(ps * 14.0).z);

    // Thin atmosphere halo just outside the disc, brighter on the lit side.
    float2 dirOut = q / max(r, 1e-3);
    float side = saturate(dot(float2(dirOut.x, -dirOut.y), normalize(LightDir.xy + 1e-4)) * 0.5 + 0.6);
    float halo = exp(-max(r - 1.0, 0.0) * 24.0) * 0.50 * side * (0.55 + 0.45 * HazeAmt);

    float edge = 1.0 - smoothstep(0.985, 1.0, r);
    float3 rgb = saturate(col) * edge + GlowCol.rgb * halo * (1.0 - edge);
    float alpha = saturate(edge + halo * (1.0 - edge));
    return float4(min(rgb, alpha + 0.0001), alpha);          // premultiplied alpha
}
