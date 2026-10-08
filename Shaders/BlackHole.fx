// Black hole shader (ps_3_0) for WPF ShaderEffect. Compiled with Shaders\compile.ps1 -> BlackHole.ps.
//
// A physically-inspired (not simulated) black hole: a perfectly black shadow, a thin photon ring hugging it, a thin accretion
// disk seen at a shallow angle - its near side sweeps IN FRONT of the shadow, its far side is hidden behind it, and gravity
// bends the far side's light into arcs over the top (and a fainter one under the bottom) of the shadow. The side of the disk
// turning toward the viewer is brighter and bluer (relativistic beaming), the receding side dimmer and redder. The disk rotates
// differentially (inner gas faster). Background stars are lensed by a point-mass mapping, so they stretch into arcs around the
// Einstein ring. Units: the shadow radius is 1.

sampler2D Input : register(s0);   // unused (required by WPF)
sampler2D Noise : register(s1);   // tileable value-noise

float  Time     : register(c0);   // seconds
float  Seed     : register(c1);
float2 Center   : register(c2);   // hole centre in element UV
float2 Radius   : register(c3);   // shadow radius in element UV (x and y differ: scene isn't square)
float  Rot      : register(c4);   // disk axis rotation, radians
float  Elev     : register(c5);   // sin of the viewing elevation above the disk plane (small = nearly edge-on)
float4 DiskHot  : register(c6);   // inner disk colour
float4 DiskCool : register(c7);   // outer disk colour
float  DiskAmt  : register(c8);   // overall disk brightness
float  Beam     : register(c9);   // relativistic beaming strength
float  Glow     : register(c10);  // soft halo strength

static const float PI  = 3.14159265;
static const float TAU = 6.28318531;

float3 Tex(float2 p) { return tex2D(Noise, frac(p)).rgb; }
float  H1(float2 p)  { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float3 StarLayer(float2 s, float scale, float density)
{
    float2 g  = s * scale;
    float2 id = floor(g);
    float2 f  = frac(g) - 0.5;
    float  rnd = H1(id + Seed);
    float2 off = float2(H1(id + 7.3), H1(id + 1.9)) - 0.5;
    float  d   = length(f - off * 0.7);
    float  sz  = lerp(0.05, 0.13, H1(id + 3.1));
    float  st  = step(1.0 - density, rnd) * smoothstep(sz, 0.0, d) * (0.45 + 0.55 * H1(id + 9.0));
    float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.92, 0.78), H1(id + 5.5));
    return tint * st;
}

// Brightness profile of the disk at radius r (shadow radii): hot and bright inside, fading out.
float DiskProfile(float r)
{
    const float rin = 1.18, rout = 4.3;
    return smoothstep(rin, rin + 0.10, r) * (1.0 - smoothstep(rout - 1.8, rout, r)) * pow(rin / max(r, rin), 1.25);
}

// Colour, texture and Doppler shading of a disk sample at radius r and azimuth cosine cphi (+1 = side receding... see below).
float3 DiskColor(float r, float phi, float cphi)
{
    float t = saturate((r - 1.18) / 2.9);
    float3 base = lerp(DiskHot.rgb, DiskCool.rgb, pow(t, 0.8));
    // Differential rotation: inner gas laps much faster than outer.
    float omega = 0.30 * pow(max(r, 1.0), -1.5);
    float az = phi / TAU * 6.0 + Time * omega * 4.0;
    float n1 = Tex(float2(az, r * 2.2) + Seed * 0.03).x;
    float n2 = Tex(float2(az * 2.3 + 0.37, r * 5.0)).y;
    float streak = 0.55 + 0.75 * (n1 * 0.65 + n2 * 0.35);
    float rings  = 0.88 + 0.12 * sin(r * 34.0 + n1 * 5.0);
    // Beaming: the left side (cphi = -1) turns toward us.
    float dop = pow(max(1.0 + Beam * (-cphi), 0.05), 1.6);
    float3 shift = lerp(float3(0.78, 0.90, 1.30), float3(1.30, 0.78, 0.52), 0.5 + 0.5 * cphi);
    return base * shift * streak * rings * dop;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 q  = (uv - Center) / Radius;                 // shadow radii, y down
    float  r  = length(q);

    float cr = cos(Rot), sr = sin(Rot);
    float2 qr = float2(q.x * cr + q.y * sr, -q.x * sr + q.y * cr);   // disk-aligned frame
    float  phi = atan2(qr.y, qr.x);
    float  cphi = qr.x / max(r, 1e-3);

    float3 col = 0.0;

    // Lensed background stars (point-mass mapping about an Einstein radius just outside the shadow).
    float thE2 = 1.42;
    float2 beta = q - thE2 * q / max(dot(q, q), 0.05);
    float mag = 1.0 / max(abs(1.0 - thE2 * thE2 / max(dot(q, q) * dot(q, q), 0.05)), 0.25);
    col += (StarLayer(beta * 0.55 + 3.1, 24.0, 0.08) + StarLayer(beta * 0.35 + 7.7, 11.0, 0.10)) * min(mag, 2.2) * smoothstep(0.98, 1.06, r);

    // Warm halo.
    col += lerp(DiskCool.rgb, DiskHot.rgb, 0.4) * Glow * exp(-(r - 1.0) * 1.1) * smoothstep(0.95, 1.1, r) * 0.20;

    // ---- Primary disk image (tilted plane) ----
    float yd  = qr.y / max(Elev, 0.05);                 // + = near side (in front), - = far side
    float rd  = sqrt(qr.x * qr.x + yd * yd);
    float pd  = DiskProfile(rd) * DiskAmt;
    float3 dc = DiskColor(rd, atan2(yd, qr.x), qr.x / max(rd, 1e-3)) * pd;
    float nearSide = smoothstep(-0.02, 0.06, qr.y);
    float farVis   = (1.0 - nearSide) * smoothstep(0.97, 1.04, r);       // far side only outside the shadow
    float3 primary = dc * (nearSide + farVis);

    // ---- Lensed secondary image: far-side light bent over the top (and, fainter, under the bottom) of the shadow ----
    float w   = max(r - 1.0, 0.0);
    float r2  = 1.18 + w * 5.2;
    float sec = DiskProfile(r2) * DiskAmt * smoothstep(0.995, 1.012, r) * (1.0 - smoothstep(0.55, 0.7, w));
    float topW = lerp(1.0, 0.42, smoothstep(-0.2, 0.3, qr.y));            // brighter above the axis, fainter below
    float3 secondary = DiskColor(r2, phi, cphi) * sec * topW * 2.0;

    // ---- Photon ring ----
    float ring = exp(-pow((r - 1.03) / 0.016, 2.0));
    float ringDop = pow(max(1.0 + Beam * 0.8 * (-cphi), 0.1), 1.8);
    float3 photon = float3(1.0, 0.95, 0.82) * ring * (0.9 + 0.8 * ringDop) * 1.1 * DiskAmt;
    photon += float3(1.0, 0.85, 0.6) * exp(-pow((r - 1.075) / 0.05, 2.0)) * 0.20 * DiskAmt;

    col += primary + secondary + photon;

    // Shadow: black inside the horizon; only the near side of the disk passes in front.
    float inside = 1.0 - smoothstep(0.985, 1.0, r);
    float3 inCol = dc * nearSide;
    col = lerp(col, inCol, inside);

    col = 1.0 - exp(-col * 1.25);
    float edge = smoothstep(0.0, 0.14, uv.x) * smoothstep(0.0, 0.14, 1.0 - uv.x)
               * smoothstep(0.0, 0.10, uv.y) * smoothstep(0.0, 0.10, 1.0 - uv.y);
    col *= lerp(edge, 1.0, inside);
    float alpha = saturate(max(max(col.r, max(col.g, col.b)), inside));
    return float4(min(col, alpha), alpha);
}
