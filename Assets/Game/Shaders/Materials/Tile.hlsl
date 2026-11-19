#ifndef PUNCHY_TILE_INCLUDED
#define PUNCHY_TILE_INCLUDED

// W-173: the one roof-cover look (MaterialFamily.Tile): wood shingles (Wood.hlsl's), clay pantiles, slate and stone
// slabs, laid in courses on the roof's plane (Wood.hlsl's face coordinates). Units are too small for geometry, so each
// course's lap is drawn: a shadow under the course above and the unit's edges leaning the normal. Details are centred on
// 1 and fade with the pixels (rules 20, 25). Needs Sky.hlsl (the noise) included first.
#include "Wood.hlsl"

#define PUNCHY_ROOF_SHINGLE 0
#define PUNCHY_ROOF_PANTILE 1
#define PUNCHY_ROOF_SLATE 2
#define PUNCHY_ROOF_SLAB 3

/// Lichen on a roof: pale grey-green and orange rosettes, more on stone than on fired clay.
half3 PunchyRoofLichen(half3 c, float2 uv, float footprint, half amount)
{
    half spot = smoothstep(0.72, 0.86, PunchyValueNoise(uv / 0.07 + 41.3)) * smoothstep(0.4, 0.7, PunchyValueNoise(uv / 0.8 + 7.1));
    half orange = step(0.5, PunchyValueNoise(uv / 0.5 + 19.9));
    return lerp(c, lerp(half3(1.05, 1.12, 0.92), half3(1.35, 0.95, 0.5), orange), spot * amount * PunchyWoodFade(0.07, footprint));
}

/// A roof cover's albedo multiplier (kind PUNCHY_ROOF_*: MaterialLibrary.RoofKind) at pattern point p on a face of normal
/// n (leaned in place), seen along v; r is the piece's random. Shingles take `albedo` (Wood.hlsl colours by kind), so
/// this returns the colour itself, not a multiplier; woodKind is the shingles' WoodKind.
half3 PunchyRoofCover(half3 albedo, float3 p, inout half3 n, float footprint, half r, int kind, int woodKind)
{
    float across, row;
    PunchyWoodFaceCoords(p + r * 23, n, across, row);
    if (kind == PUNCHY_ROOF_SHINGLE)
        return PunchyWoodShingles(albedo, across, row, footprint, woodKind, -1);
    float2 alongXZ = normalize(float2(-n.z, n.x) + float2(1e-4, 0));
    float3 t = float3(alongXZ.x, 0, alongXZ.y);
    float3 bt = normalize(float3(0, 1, 0) - n * n.y + 1e-5);
    float course = kind == PUNCHY_ROOF_PANTILE ? 0.26 : kind == PUNCHY_ROOF_SLATE ? 0.2 : 0.3;
    float rr = row / course;
    float c = floor(rr);
    half fr = frac(rr);
    half hc = PunchyCellHash(int2((int)c, 13));
    float width = kind == PUNCHY_ROOF_PANTILE ? 0.24 : (kind == PUNCHY_ROOF_SLATE ? 0.25 : 0.5) * (0.8 + 0.4 * hc);
    float u = across / width + (kind == PUNCHY_ROOF_PANTILE ? 0 : frac(c * 0.5 + hc * 0.3));
    half fu = frac(u);
    half h = PunchyCellHash(int2((int)c, (int)floor(u) + 401));
    // Fades on whole periods and a few pixels each: the wood fade on half a unit lost the rolls by 6 m.
    half fCourse = smoothstep(2, 5, course / max(footprint, 1e-4));
    half fUnit = smoothstep(2, 5, width / max(footprint, 1e-4));
    half fSeam = smoothstep(2, 5, width * 0.25 / max(footprint, 1e-4));
    half tail = kind == PUNCHY_ROOF_PANTILE ? 0 : (PunchyValueNoise(float2(across / 0.03, c * 7.7)) - 0.5) * 0.08;
    half under = smoothstep(0.66 + tail, 1.0, fr) * 0.42;
    half lip = (1 - smoothstep(0.0, 0.07, fr + tail)) * 0.12;
    half3 m;
    if (kind == PUNCHY_ROOF_PANTILE)
    {
        half s = sin(fu * 6.2832);
        half roll = smoothstep(0.55, 0.9, fu) * (1 - smoothstep(0.9, 1.0, fu));
        n = normalize(n + t * (cos(fu * 6.2832) * 0.45 * fUnit));
        half3 fired = lerp(half3(0.82, 0.86, 0.92), half3(1.14, 1.06, 0.98), h);
        half seam = 1 - smoothstep(0.0, 0.08, min(fu, 1 - fu));
        m = fired * (1 + (s * 0.16 + roll * 0.22) * fUnit - seam * 0.35 * fSeam) * half3(1.08, 0.96, 0.9);
    }
    else
    {
        half joint = (1 - smoothstep(0.012, 0.03, min(fu, 1 - fu) * width)) * fSeam;
        half sheen = (PunchyValueNoise(float2(across, row) / 0.06 + 3.1) - 0.5) * (kind == PUNCHY_ROOF_SLATE ? 0.12 : 0.22);
        half3 tone = kind == PUNCHY_ROOF_SLATE ? lerp(half3(0.85, 0.88, 0.95), half3(1.1, 1.08, 1.05), h)
                                               : lerp(half3(0.86, 0.85, 0.82), half3(1.12, 1.08, 1.0), h);
        n = normalize(n + bt * (lip - under) * 0.6 * fCourse);
        m = tone * (1 + sheen * PunchyWoodFade(0.06, footprint)) * (1 - joint * 0.4);
    }
    m = lerp(1, m, fUnit) * (1 - (under - lip) * fCourse);
    return albedo * PunchyRoofLichen(m, float2(across, row), footprint, kind == PUNCHY_ROOF_PANTILE ? 0.25 : 0.55);
}

#endif
