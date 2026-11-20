#ifndef PUNCHY_GLINT_INCLUDED
#define PUNCHY_GLINT_INCLUDED

// Rule 33: the one sun sparkle (snow glitter, frost on glacier ice). A grid of tiny dots, each a facet leaned its own
// way, that flashes when it turns the sun to the eye. Callers fade it by their own on-screen size (rule 20) and add it
// with PunchyGlintLight. Needs Lighting.hlsl (Light) first.

/// -0.5..0.5 per cell of p (an integer hash: far from the origin a sin hash breaks up).
half PunchyCellHash(float2 p)
{
    uint2 cell = (uint2)(int2)floor(p);
    uint h = cell.x * 73856093u ^ cell.y * 19349663u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return (h >> 22) / 1023.0 - 0.5;
}

/// gp: position in cells. seeds: hash offsets (x keep pick, yzw facet lean); tilt: lean per axis; stray: how far a dot
/// wanders from its cell's middle (by the lean's xy); dotR: dot edge radii in cells; keep: share of cells that glint.
half PunchyGlint(float2 gp, float4 seeds, half3 tilt, half stray, half2 dotR, half keep, float3 normalWS, float3 viewWS, float3 sunWS)
{
    float2 gc = floor(gp);
    half3 lean = half3(PunchyCellHash(gc + seeds.y), PunchyCellHash(gc + seeds.z), PunchyCellHash(gc + seeds.w));
    half dotOn = 1 - smoothstep(dotR.x, dotR.y, length(gp - gc - 0.5 - lean.xy * stray));
    float3 facet = normalize(normalWS + lean * tilt);
    return (PunchyCellHash(gc + seeds.x) >= 0.5 - keep) * dotOn * pow(saturate(dot(facet, normalize(viewWS + sunWS))), 120);
}

half3 PunchyGlintLight(Light sun, half glint)
{
    return sun.color * sun.shadowAttenuation * glint * 1.5;
}

#endif
