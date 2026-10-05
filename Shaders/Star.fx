// Star shader (ps_3_0) for WPF ShaderEffect. Compiled with Shaders\compile.ps1 -> Star.ps.
//
// Draws a whole star scene inside the host rectangle: a rotating granulated photosphere
// (soft hot blobs, bright plasma patches that dissolve and re-form, limb reddening), drifting
// sunspot pairs, a fuzzy soft halo, glowing coronal loops that grow/hold/fade around the limb,
// and mass ejections that lift off from some loop apexes. Noise comes from the shared tileable
// value-noise texture (sampler 1), same as GasGiant.fx.

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise, R/G/B = three independent fields

float  Time     : register(c0);   // seconds
float  Seed     : register(c1);
float2 Center   : register(c2);   // disc center in element UV
float2 Radius   : register(c3);   // disc radius in element UV (x/y differ)
float4 CoreCol  : register(c4);   // sRGB 0..1 palette
float4 MidCol   : register(c5);
float4 EdgeCol  : register(c6);
float4 HotCol   : register(c7);   // bright plasma patches
float  Activity : register(c8);   // 0..1
float  Contrast : register(c9);   // granulation contrast
float  SpotAmt  : register(c10);  // 0..1 sunspot frequency
float  LoopAmt  : register(c11);  // 0..1 coronal loop count/strength
float  FlareAmt : register(c12);  // 0..1 chance a loop launches an ejection
float  HaloAmt  : register(c13);  // halo strength
float  Tilt     : register(c14);  // axial tilt, radians
float  PatchCover : register(c15); // 0..1 how much of the disc the bright plasma patches cover (cool dwarfs are smoother)

static const float PI  = 3.14159265;
static const float TAU = 6.28318531;

float3 Tex(float2 p)
{
    return tex2D(Noise, frac(p)).rgb;
}

float3 Hash3(float a, float b, float c)
{
    return frac(sin(float3(a * 13.7 + b * 17.1, a * 7.3 + c * 31.9, a * 3.1 + b * 11.3 + c))
                * float3(43758.5, 22578.1, 31415.9));
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q = (uv - Center) / Radius;     // disc = unit circle, y down
    float r2 = dot(q, q);
    float r  = sqrt(r2);
    float ang = atan2(q.y, q.x);

    // Fuzzy limb: the edge radius wobbles with angle + time, like a real photosphere edge.
    // Fuzzy limb: a small, fine-grained wobble of the edge radius (many tiny bumps, +-0.4%) so the edge is soft
    // but stays round. The earlier slow +-1.5% wobble had only ~7 bumps round the star and read as flat spots.
    float fringeN = Tex(float2(ang / TAU * 38.0, 0.31 + Time * 0.012)).x * 0.6 + Tex(float2(ang / TAU * 71.0, 0.77)).y * 0.4;
    float rN = r - (fringeN - 0.5) * 0.008;
    float edge = 1.0 - smoothstep(0.975, 1.012, rN);

    float z = sqrt(saturate(1.0 - r2));
    float3 P = float3(q.x, -q.y, z);
    float ct = cos(Tilt), st = sin(Tilt);
    float3 Pz = float3(P.x * ct - P.y * st, P.x * st + P.y * ct, P.z);
    float lat = asin(clamp(Pz.y, -0.999, 0.999));
    float lon = atan2(Pz.x, Pz.z);

    // Slow solid rotation of the whole photosphere.
    float lonW = lon - Time * 0.014;
    float2 p = float2(lonW / PI, lat / PI * 2.0);   // ~isotropic at the equator

    // ---------------- photosphere ----------------
    float big  = Tex(p * 0.75 + Seed * 0.11).x;           // soft large hot/cool blobs
    float big2 = Tex(p * 1.30 + 0.43 + Seed * 0.07).z;

    // Plasma patches: two advected copies of a fine field, dissolved into each other by a
    // slowly varying per-region phase, so patches boil instead of just sliding sideways.
    float f1 = Tex(p * 6.0 + float2( Time * 0.0045, 0.0) + 0.17).y;
    float f2 = Tex(p * 6.0 + float2(-Time * 0.0040, Time * 0.0030) + 0.61).y;
    float mixT = 0.5 + 0.5 * sin(Time * 0.30 + big2 * 9.0);
    float f = lerp(f1, f2, mixT);
    float fine = Tex(p * 13.0 + 0.9).x;
    float field = f * 0.60 + fine * 0.40;
    // Cool dwarfs: patches come in a few big clumps (the rest of the disc stays smooth), made of
    // larger chunks rather than a fine even scatter.
    float clump = smoothstep(0.36, 0.64, Tex(p * 1.05 + 0.27 + Seed * 0.05).z);
    float lowF  = Tex(p * 2.4 + 0.5).y;
    field = lerp(field, lowF * 0.55 + field * 0.45, 1.0 - PatchCover);

    // More patches in the mid-latitude "active" belts and away from the soft bright blobs.
    float belt = 1.0 - smoothstep(0.35, 1.0, abs(p.y));
    float thr  = 0.62 - 0.10 * Activity - 0.06 * belt + 0.12 * smoothstep(0.55, 0.8, big) + (1.0 - PatchCover) * (0.10 + 0.22 * (1.0 - clump));
    float patch = smoothstep(thr, thr + 0.15, field);
    float white = smoothstep(thr + 0.17, thr + 0.25, field);

    float3 body = lerp(MidCol.rgb, CoreCol.rgb, 0.15 + 0.35 * big);
    // Soft bright sheen blobs (the pale cloudy hot spots in the references).
    body = lerp(body, lerp(CoreCol.rgb, float3(1, 1, 1), 0.15), smoothstep(0.58, 0.86, big) * 0.30);
    // Darker cooler mottling between patches.
    body = lerp(body, EdgeCol.rgb, smoothstep(0.55, 0.20, big2) * 0.30);
    // Large soft darker-orange blotches (the in-game "cool patches" - diffuse, no hard edge).
    body = lerp(body, lerp(MidCol.rgb, EdgeCol.rgb, 0.55), smoothstep(0.42, 0.20, big) * 0.40);

    float3 col = lerp(body, HotCol.rgb, patch * saturate(0.55 + 0.5 * Contrast));
    col = lerp(col, float3(1.0, 0.93, 0.72), white * 0.45 * Contrast);

    // ---------------- sunspots (drift with rotation, in pairs) ----------------
    for (int j = 0; j < 5; j++)
    {
        float3 h = Hash3(Seed + j * 5.3, j * 2.1, Seed * 0.3);
        float exists = step(frac(h.z * 3.3), 0.10 + SpotAmt * 0.50);
        // Scatter groups over a wide latitude range (either hemisphere) and across all longitudes.
        float sLat = (frac(h.x * 3.7 + j * 0.31) - 0.5) * 1.5;
        float sLon = frac(h.y * 2.3 + j * 0.211) * TAU;
        float size = 0.075 + frac(h.z * 9.1) * 0.085;
        // Companion: random direction and distance, so groups never line up.
        float cAng = frac(h.x * 5.9) * TAU;
        float cDist = size * (1.8 + frac(h.y * 4.3) * 2.2);

        for (int k = 0; k < 2; k++)
        {
            float kk = (float)k;
            float2 off = kk * cDist * float2(cos(cAng), sin(cAng));
            float cLat = sLat + off.y;
            float dLon = (lon - Time * 0.014) - (sLon + off.x / max(cos(sLat), 0.3));
            dLon = (frac(dLon / TAU + 0.5) - 0.5) * TAU;
            float sz = size * (1.0 - 0.35 * kk);
            float dx = dLon * cos(cLat) / sz;
            float dy = (lat - cLat) / sz;
            float rr = sqrt(dx * dx + dy * dy);
            rr += (Tex(float2(dx, dy) * 0.35 + j * 0.31 + kk * 0.13).x - 0.5) * 0.55;   // ragged outline
            float pen = smoothstep(1.7, 0.35, rr);
            float umb = smoothstep(1.0, 0.15, rr);
            col *= 1.0 - pen * exists * 0.16;
            col = lerp(col, EdgeCol.rgb * 0.85, umb * exists * 0.45);
        }
    }

    // Limb: slightly redder/darker toward the edge, brighter overall in the middle.
    float limb = pow(saturate(1.0 - z), 1.8);
    col = lerp(col, EdgeCol.rgb * 0.95, limb * 0.55);
    col *= 0.90 + 0.18 * pow(z, 0.8);

    // ---------------- halo ----------------
    float fringe = 0.70 + 0.60 * Tex(float2(ang / TAU * 5.0 + 0.2, 0.67 - Time * 0.008)).y;
    float breathe = 1.0 + 0.08 * sin(Time * 0.55 + Seed);
    float rOut = max(r, 1.0) - 1.0;
    float halo = exp(-rOut * 3.4) * HaloAmt * fringe * breathe;
    halo *= smoothstep(2.1, 0.9, max(r, 1.0));

    // ---------------- coronal loops ----------------
    // Smooth magnetic arches of glowing plasma anchored on the limb (feet sink slightly into the
    // disc). Six slots, each re-rolling its limb position, span and height every cycle on
    // staggered clocks, so several loops of different heights are alive around the star at once.
    float3 loopSum = float3(0, 0, 0);    // drawn BEHIND the disc
    float3 flareSum = float3(0, 0, 0);   // drawn in front
    float lAng[6], lSpan[6], lEnv[6];    // per-slot loop footprint, so flares can steer clear
    float3 loopCol  = lerp(EdgeCol.rgb, MidCol.rgb, 0.35) * 1.30;
    float3 loopCore = lerp(MidCol.rgb, HotCol.rgb, 0.25);

    for (int m = 0; m < 6; m++)
    {
        float3 h0 = Hash3(Seed * 0.7 + m * 19.1, m * 7.7, Seed * 0.2 + m);
        float period = 13.0 + h0.z * 11.0;
        float cyc    = Time / period + h0.x * 5.0;
        float life   = frac(cyc);
        float cycI   = floor(cyc);
        float3 h = Hash3(Seed * 0.7 + m * 19.1 + cycI * 7.31, m * 7.7 + cycI * 1.3, Seed * 0.2 + m + cycI * 2.9);

        // 30% fewer new arches than before.
        float exists = step(frac(h.y * 7.7), (0.40 + 0.55 * LoopAmt) * 0.70);
        // Position: usually anywhere around the limb, but often within one of two "active region"
        // clumps (their centers drift every ~45 s, fixed for a given arch from birth to death).
        float birthT = (cycI - h0.x * 5.0) * period;
        float3 hc    = Hash3(Seed * 0.9 + floor(birthT / 45.0) * 3.3, 5.5, 2.2);
        float pick   = frac(h.x * 17.3);
        float scat   = (frac(h.z * 9.7) - 0.5) * 0.9;
        float a      = pick < 0.28 ? hc.x * TAU + scat
                     : pick < 0.50 ? hc.y * TAU + scat
                     : h.x * TAU;
        float delta  = (0.10 + frac(h.z * 3.7) * 0.26) * 0.85;   // half-span, radians along the limb
        // Always taller than wide (height 1.0x-1.9x the half-span) so even small loops read as arches.
        float Hh     = sin(delta) * (1.00 + 0.90 * frac(h.y * 5.3)) * (0.80 + 0.30 * LoopAmt);
        float env    = smoothstep(0.0, 0.36, life) * smoothstep(1.0, 0.60, life) * exists;     // rise 30% slower
        float Hc     = Hh * (0.55 + 0.45 * smoothstep(0.0, 0.64, life));                       // growth 30% slower
        lAng[m] = a; lSpan[m] = delta; lEnv[m] = env;

        float2 n  = float2(cos(a), sin(a));
        float2 tg = float2(-n.y, n.x);
        // Chord sits a little inside the limb so the feet overlap the star's edge.
        float cd = cos(delta) - 0.025, w = sin(delta);
        float x = dot(q, tg);
        float y = dot(q, n) - cd;

        float L = 0.0;
        for (int k = 0; k < 3; k++)
        {
            float kk = (float)k;
            float Hk = Hc * (1.0 - 0.10 * kk);
            float wk = w * (1.0 - 0.07 * kk);
            float fx = x / wk, fy = y / Hk;
            // Superellipse with exponent ~2: a rounded crown that is taller than wide (1.6 was too pointed, 2.5 too U-shaped).
            float ax = abs(fx), ay = abs(fy);
            float f  = pow(ax, 2.1) + pow(ay, 2.1);
            float gl = 2.1 * length(float2(pow(ax, 1.1) / wk, pow(ay, 1.1) / Hk));
            float dd = abs(f - 1.0) / max(gl, 1e-3);
            // k == 0: broad smooth body; k > 0: slightly tighter strands inside it.
            // Thick at the feet, tapering to a thin crest.
            float tUp = saturate(y / Hk);
            float taperW = 1.05 - 0.75 * pow(tUp, 0.8);
            float wd = ((k == 0) ? 0.080 : (0.030 + 0.010 * kk)) * taperW;
            float body = exp(-(dd * dd) / (wd * wd));
            float haze = exp(-dd / (0.12 * taperW)) * 0.09;
            float yMask = smoothstep(-0.07, -0.005, y);                  // continue just into the disc
            float amp = ((k == 0) ? 0.34 : 0.15) * (1.0 - 0.40 * tUp);
            L += (body * amp + (k == 0 ? haze : 0.0)) * yMask;
        }
        float bright = 0.55 + 0.45 * frac(h.z * 13.1);                  // each loop its own intensity
        loopSum += lerp(loopCol, loopCore, saturate(L * 0.6)) * L * env * bright;
    }

    // ---------------- flares / mass ejections ----------------
    // Independent of the loops: a shredded, fibrous cloud of bright plasma lifts off the limb,
    // billows outward and breaks into scattered fragments. Re-rolls its position each cycle.
    for (int e = 0; e < 2; e++)
    {
        float3 h0 = Hash3(Seed * 1.9 + e * 29.7, e * 5.1 + 3.0, Seed + e * 2.3);
        float period = 60.0 + h0.z * 40.0;                  // half as often; each event keeps its old length
        float cyc  = Time / period + h0.y * 7.0;
        float life = frac(cyc);
        float cycI = floor(cyc);
        float3 h = Hash3(Seed * 1.9 + e * 29.7 + cycI * 4.7, e * 5.1 + 3.0 + cycI * 1.9, Seed + e * 2.3 + cycI * 3.1);
        float launch = (e < 1) ? 1.0 : step(frac(h.y * 11.3), FlareAmt);
        float a = h.x * TAU;
        float u = saturate(life / 0.31);
        float gate = launch * smoothstep(0.0, 0.10, u) * smoothstep(1.0, 0.50, u) * step(life, 0.31);
        // Stay clear of any loop that is up around the same part of the limb.
        for (int bm = 0; bm < 6; bm++)
        {
            float dA = abs(frac((a - lAng[bm]) / TAU + 0.5) - 0.5) * TAU;
            gate *= 1.0 - lEnv[bm] * (1.0 - smoothstep(lSpan[bm] + 0.15, lSpan[bm] + 0.45, dA));
        }
        if (gate > 0.001)
        {
            float2 n  = float2(cos(a), sin(a));
            float2 tg = float2(-n.y, n.x);
            float rho = 0.1125 + 0.1125 * u;                         // 75% of the previous size (37.5% of the original)
            float2 c  = n * (1.0 + 0.0375 + 0.1425 * u) + tg * sin(h.z * 6.0) * 0.03;
            float2 dq = q - c;
            float2 lq = float2(dot(dq, tg), dot(dq, n));          // cloud-local, n = outward

            // Domain-warped fibrous field: contour lines of fbm read as glowing threads.
            // (Frequencies doubled along with the size cut so the detail stays the same.)
            float2 wv = Tex(lq * 1.6 + h.xy + Time * 0.010).xy - 0.5;
            float s1 = Tex((lq + wv * 0.34) * 2.0 + h.zx + float2(0.0, -Time * 0.010)).y;
            float s2 = Tex((lq + wv * 0.225) * 4.0 + h.yz).x;
            float thread = smoothstep(0.78, 0.93, 1.0 - abs(s1 * 2.0 - 1.0));
            float thread2 = smoothstep(0.84, 0.95, 1.0 - abs(s2 * 2.0 - 1.0));

            float mCloud = smoothstep(1.0, 0.15, length(lq / float2(rho * 1.1, rho * 0.85)));
            float mWide  = smoothstep(1.7, 0.4, length(lq / float2(rho * 1.1, rho * 0.85)));
            float haze   = smoothstep(0.25, 0.70, s1) * mCloud * 0.62;
            float frag = smoothstep(0.76, 0.84, Tex(lq * 4.8 + h.xy + u).x) * mWide * (1.0 - mCloud * 0.5) * 0.7;

            float I = (haze + thread * mCloud * 0.95 + thread2 * mCloud * 0.55 + frag * 0.70) * gate;
            float3 tone = lerp(loopCol, float3(1.0, 0.68, 0.34), saturate(thread * 0.80 + frag * 0.45));
            flareSum += tone * I;

            // Bright eruption flash at the footpoint early on.
            float flash = exp(-dot(q - n * 0.98, q - n * 0.98) / 0.005) * smoothstep(0.35, 0.0, u) * gate;
            flareSum += float3(1.0, 0.85, 0.55) * flash * 0.7;
        }
    }

    // Fade anything (loops, ejecta) that would otherwise be cut off by the scene rectangle.
    float2 uvEdge = min(uv, 1.0 - uv);
    float edgeFade = smoothstep(0.0, 0.10, min(uvEdge.x, uvEdge.y));
    loopSum *= edgeFade; flareSum *= edgeFade;

    float3 haloRGB = MidCol.rgb * halo;
    // Loops sit behind the star (its edge hides the feet); flares lift off in front of it.
    float3 behind = haloRGB + loopSum * 1.15;
    float3 rgb = col * edge + behind * (1.0 - edge) + flareSum * 1.15;
    float loopMax = max(loopSum.r, max(loopSum.g, loopSum.b)) * 1.15;   // additive: no darkening alpha
    float flareMax = max(flareSum.r, max(flareSum.g, flareSum.b)) * 1.15;
    float alpha = saturate(edge + (halo + loopMax) * (1.0 - edge) + flareMax);
    return float4(min(rgb, alpha + 0.0001), alpha);          // premultiplied alpha
}
