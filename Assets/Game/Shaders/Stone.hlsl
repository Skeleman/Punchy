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
void PunchyStoneSurface(float3 p, float footprint, half wet, half rainWet, inout half3 albedo, inout half3 n, out half crevice)
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
    albedo = lerp(albedo, half3(0.86, 0.84, 0.78), speck * 0.6 * (1 - crevice));
    albedo *= 1 - 0.35 * saturate(-n.y) * f1;
    // Forced wetness (debug) on dry ground: the darkening rain would have brought (the caller's own covers real wet).
    albedo *= lerp(1, 0.62, saturate(wet - rainWet));
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

#endif
