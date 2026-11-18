#ifndef PUNCHY_STONE_INCLUDED
#define PUNCHY_STONE_INCLUDED

// R-34 (#332): the one stone surface (rule 33), the base of steep terrain faces and cliff kit pieces (TerrainSurface),
// cave walls (Cave), and boulders and built stone (Flora). Zach picked the wet look as the base rock (11-15). The big
// shapes stay with each caller (R-19's beds, ledges and slabs on cliffs; the cave's strata); this is the surface on
// them. Our own code, written from a list of traits (no reference code):
// - ridged, multiplicative relief: three octaves of the shared detail texture's height, each folded into a ridge and
//   squared, each one's strength scaled by the one below, so the bumps cluster on worn ridges, not even noise;
// - a crevice / edge mask from that relief: dark brown down in the crevices, pale sandy on the edges, mineral specks;
// - darker on downward faces;
// - wet only: two specular lobes (broad and sharp) and a grazing Fresnel, held back in the crevices, and the darkening.
// Dry stone keeps the relief and colouring with no lobes. Wetness is the caller's real wetness (rain, drying over time,
// shores and fall spray, cave damp), so the shine comes and goes with it.
// Every octave, the specks and the colouring fade out by their own on-screen size (footprint: metres one pixel
// covers), never by fixed metres (rule 20), into the caller's own tone, so a far stone keeps the biome's far tone.
// The includer declares _PunchyCaveDetail and sampler_PunchyCaveDetail (rg bump, b height, a glint) before this file.

float _PunchyStoneWetMin;   // debug only (-stonewet): the least wetness stone shows; 0 in play

// The stone's wetness from the caller's real wetness (0..1).
half PunchyStoneWetness(half realWet) { return saturate(max(realWet, _PunchyStoneWetMin)); }

// 0 when a feature of this period (m) is under a few pixels, 1 once it spans eight.
half PunchyStoneFade(float period, float footprint) { return smoothstep(3, 8, period / max(footprint, 1e-5)); }

// One octave: the detail texture read three ways at this scale, blended by the facing weights w. The mip comes from
// the footprint (the texture is 256 px a tile), not from derivatives, so it is safe inside the callers' branches
// (rule 24).
half4 PunchyStoneOctave(float3 p, float3 w, float scale, float footprint)
{
    float3 q = p * scale;
    float mip = log2(max(footprint * scale * 256, 1));
    return SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.zy, mip) * w.x
         + SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xz, mip) * w.y
         + SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xy, mip) * w.z;
}

// A height folded into a ridge and squared (1 on the fold, 0 in the valleys), eased back to its mean as it fades.
half PunchyStoneRidge(half b, half fade) { half r = 1 - abs(b * 2 - 1); return lerp(0.33, r * r, fade); }

// The stone's surface at p: changes albedo and n, and gives the crevice amount (0 on edges, 1 deep down) for the
// lighting below. wet from PunchyStoneWetness; rainWet is the wetness the caller's own wet darkening already applies.
// speckScale (0..1) scales the pale mineral specks: the library passes less on very dark rock (the salt tower's
// basalt), where a fixed pale speck reads as a star; the form below without it keeps every other caller at 1.
void PunchyStoneSurface(float3 p, float footprint, half wet, half rainWet, inout half3 albedo, inout half3 n, out half crevice,
                        half speckScale)
{
    float3 w = pow(abs(n), 4);
    w /= max(w.x + w.y + w.z, 1e-5);
    // Periods about 2.2 m, 0.6 m and 0.18 m; the finest is skipped once it is gone.
    half f1 = PunchyStoneFade(2.2, footprint), f2 = PunchyStoneFade(0.6, footprint), f3 = PunchyStoneFade(0.18, footprint);
    half4 d1 = PunchyStoneOctave(p, w, 0.45, footprint);
    half4 d2 = PunchyStoneOctave(p + 17.3, w, 1.7, footprint);
    half4 d3 = 0.5;
    [branch] if (f3 > 0)
        d3 = PunchyStoneOctave(p - 5.1, w, 5.5, footprint);
    // Ridged multifractal: each octave weighted by the one below it, so fine ridges sit on the big ones' crests.
    half s1 = PunchyStoneRidge(d1.b, f1);
    half w2 = saturate(s1 * 2);
    half s2 = PunchyStoneRidge(d2.b, f2) * w2;
    half w3 = saturate(s2 * 2);
    half s3 = PunchyStoneRidge(d3.b, f3) * w3;
    half h = (s1 + s2 * 0.5 + s3 * 0.25) / 1.75;
    crevice = (1 - smoothstep(0.04, 0.22, h)) * f1;
    half edge = smoothstep(0.4, 0.7, h) * f1;
    // Relief: the octaves' bumps, each by its own fade and by the same weights, the finer ones riding on the ridges.
    half2 bump = (d1.rg - 0.5) * 0.7 * f1 + (d2.rg - 0.5) * 0.6 * f2 * (0.3 + w2) + (d3.rg - 0.5) * 0.45 * f3 * (0.3 + w3);
    n = normalize(n + float3(0, bump.x, bump.y) * w.x + float3(bump.x, 0, bump.y) * w.y + float3(bump.x, bump.y, 0) * w.z);
    // Colour: brown crevices, pale sandy edges, a few mineral specks, darker on downward faces.
    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
    albedo = lerp(albedo, albedo * half3(0.45, 0.36, 0.28), crevice * 0.85);
    albedo = lerp(albedo, lum * half3(1.35, 1.25, 1.05), edge * 0.55);
    float3 cell = floor(p * 30);
    half speck = step(0.965, frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453)) * PunchyStoneFade(0.07, footprint);
    albedo = lerp(albedo, half3(0.86, 0.84, 0.78), speck * 0.6 * speckScale * (1 - crevice));
    albedo *= 1 - 0.35 * saturate(-n.y) * f1;
    // Forced wetness (debug) on dry ground: the darkening rain would have brought (the caller's own covers real wet).
    albedo *= lerp(1, 0.62, saturate(wet - rainWet));
}

void PunchyStoneSurface(float3 p, float footprint, half wet, half rainWet, inout half3 albedo, inout half3 n, out half crevice)
{
    PunchyStoneSurface(p, footprint, wet, rainWet, albedo, n, crevice, 1);
}

// The wet stone's two specular lobes from one light (times the light's colour and shadow): a broad one on the relief's
// normal, a tight one on that normal bent further out from the face (n0: the face's own normal), so the tight glints
// sit on the ridges' flanks. Nothing when dry.
half PunchyStoneSpec(half3 n, half3 n0, half3 v, half3 l, half crevice, half wet)
{
    half3 h = SafeNormalize(l + v);
    half3 bent = normalize(n + (n - n0) * 0.8);
    half broad = pow(saturate(dot(n, h)), 12) * 0.22;
    half sharp = pow(saturate(dot(bent, h)), 90) * 1.1;
    return (broad + sharp) * saturate(dot(n, l) * 4) * (1 - 0.8 * crevice) * wet;
}

// How much of the sky the wet stone reflects, strongly at grazing angles (times the sky's light in the reflected
// direction). Nothing when dry.
half PunchyStoneFresnel(half3 n, half3 v, half crevice, half wet)
{
    return (0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5)) * 0.5 * (1 - 0.8 * crevice) * wet;
}

// W-166 row 3: built stone (MaterialFamily.Stone), the one look for cut and rubble stone laid on top of the surface
// above. Built pieces call PunchyStoneBlocks before PunchyStoneSurface, so the blocks carry the same wet relief,
// crevices and shine as the rock they were cut from.
// - cut: level courses of squared blocks, each course its own block length, with tight, slightly sunk joints and
//   softened arrises (fountains, plinths, steps, hearths, seats);
// - rubble: irregular stones packed wider than tall in pale lime, each one domed (ruined walls).
// Joints, tints and doming fade by their own on-screen size (rule 20), so a far wall keeps its tone (rule 25).
// Self-contained: no other include needed beyond the surface's own.

float PunchyStoneHash(float2 c) { return frac(sin(dot(c, float2(127.1, 311.7))) * 43758.5453); }

// The face's frame at p: along and up a wall, or across a top (x and z on anything facing mostly up or down).
void PunchyStoneFrame(float3 p, half3 n, out float2 uv, out float3 t, out float3 bt)
{
    if (abs(n.y) > 0.7)
    {
        t = float3(1, 0, 0);
        bt = float3(0, 0, 1);
        uv = p.xz;
        return;
    }
    float2 a = normalize(float2(-n.z, n.x) + 1e-4);
    t = float3(a.x, 0, a.y);
    bt = normalize(float3(0, 1, 0) - n * n.y + 1e-5);
    uv = float2(dot(p.xz, a), p.y);
}

// Cut stone: distance (m) to the nearest joint (to a head joint, to a bed joint) at face point uv, which way each
// lies (-1 or +1), and the block's own index. Each course draws its own block length and offset.
float2 PunchyStoneCourse(float2 uv, float seed, out float2 side, out float2 cell)
{
    const float course = 0.34;
    float row = floor(uv.y / course);
    float len = lerp(0.5, 0.9, PunchyStoneHash(float2(row, seed)));
    float u = uv.x / len + PunchyStoneHash(float2(row, seed + 3.1));
    cell = float2(floor(u), row);
    float2 f = float2(frac(u) * len, frac(uv.y / course) * course);
    side = float2(f.x < len * 0.5 ? -1 : 1, f.y < course * 0.5 ? -1 : 1);
    return float2(min(f.x, len - f.x), min(f.y, course - f.y));
}

// Rubble: the stone nearest face point uv (cells 0.34 x 0.22 m, so the stones lie flat), as the offset from the point
// to its middle (m) and its index; returns roughly how far (m) the point is from the stone's edge.
float PunchyStoneRubble(float2 uv, float seed, out float2 toMiddle, out float2 cell)
{
    const float2 size = float2(0.34, 0.22);
    float2 q = uv / size;
    float2 b = floor(q), f = q - b;
    float d1 = 8, d2 = 8;
    toMiddle = 0;
    cell = b;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        float2 o = float2(i, j), id = b + o;
        float2 c = o + 0.15 + 0.7 * float2(PunchyStoneHash(id + seed), PunchyStoneHash(id + seed + 17.7));
        float2 dv = (c - f) * size;
        float d = dot(dv, dv);
        if (d < d1) { d2 = d1; d1 = d; toMiddle = dv; cell = id; }
        else if (d < d2) d2 = d;
    }
    return (sqrt(d2) - sqrt(d1)) * 0.5;
}

/// The built stone's albedo multiplier at world point p on a face with normal n (leaned in place for the joints and
/// the doming); rubble picks the packed look over the cut courses; r is the piece's own random (0..1).
half3 PunchyStoneBlocks(float3 p, inout half3 n, float footprint, bool rubble, half r)
{
    half period = rubble ? 0.22 : 0.34;
    half fade = smoothstep(1.5, 4, period / max(footprint, 1e-5));
    half fine = PunchyStoneFade(period, footprint);
    if (fade <= 0)
        return 1;
    float seed = r * 53;
    float2 uv;
    float3 t, bt;
    PunchyStoneFrame(p + r * 41, n, uv, t, bt);
    half mortar = rubble ? 0.011 : 0.006;   // half a joint's width (m)
    // Joints thinner than a pixel draw a pixel wide, as dark as the share of it they cover.
    half drawn = max(mortar, footprint * 0.3);
    half cover = sqrt(mortar / drawn);
    half joint, h;
    float edge;
    half2 lean;
    if (rubble)
    {
        float2 toMiddle, cell;
        edge = PunchyStoneRubble(uv, seed, toMiddle, cell);
        joint = 1 - smoothstep(drawn - footprint * 0.5, drawn + footprint * 0.5, edge);
        h = PunchyStoneHash(cell + seed + 5.3);
        // Domed: the face leans away from each stone's middle, most near its edge.
        lean = -toMiddle / 0.17 * 0.5 * (1 - joint);
    }
    else
    {
        float2 side, cell;
        float2 d = PunchyStoneCourse(uv, seed, side, cell);
        edge = min(d.x, d.y);
        joint = 1 - smoothstep(drawn - footprint * 0.5, drawn + footprint * 0.5, edge);
        h = PunchyStoneHash(cell + seed + 5.3);
        // The softened arrises lean their light into the joints; each block's face pitched a touch its own way.
        lean = (1 - smoothstep(mortar, mortar + 0.025, d)) * step(mortar, d) * side * 0.45
             + (float2(h, PunchyStoneHash(cell + seed + 9.1)) - 0.5) * 0.08;
    }
    joint *= fade * cover;
    lean *= fine;
    n = normalize(n + t * lean.x + bt * lean.y);
    // Each stone its own shade (rubble more, gathered from many beds), a little warmer or cooler.
    half3 tint = rubble ? lerp(half3(0.72, 0.74, 0.79), half3(1.2, 1.16, 1.08), h) : lerp(half3(0.84, 0.85, 0.88), half3(1.12, 1.1, 1.05), h);
    // Joints: cut stone's are thin shadow lines, rubble's are pale lime, darker where it sits back.
    half3 grout = rubble ? half3(1.12, 1.12, 1.1) * lerp(0.8, 1, saturate((mortar - edge) / mortar)) : half3(0.48, 0.48, 0.5);
    return lerp(lerp(1, tint, fade), grout, joint);
}

#endif
