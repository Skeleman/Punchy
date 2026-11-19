#ifndef PUNCHY_THATCH_INCLUDED
#define PUNCHY_THATCH_INCLUDED

// W-166 row 3: the one thatch look. Every thatched roof, hay blob and bale (MaterialFamily.Thatch) draws through here.
// Each detail is centred on 1 and fades out with the pixels, so far thatch keeps its vertex colour (rule 25). It lays
// its straw on the face's plane the way wood lays boards, so it shares Wood.hlsl's face coordinates and fade.
// Needs Sky.hlsl (the noise) included first.
#include "Wood.hlsl"

/// W-99: thatch (look 3: StructureKit.KitSurface.Thatch), straw strands down the slope in ragged layers, clumps
/// lighter and darker, a shadow under each layer, on the face's plane (Wood.hlsl's PunchyWoodFaceCoords). Each
/// scale drops out as it gets too fine for the pixels. Returns an albedo multiplier.
half PunchyThatch(float3 p, float3 nOS, float footprint)
{
    float across, row;
    PunchyWoodFaceCoords(p, nOS, across, row);
    float rr = row / 0.42 + (PunchyValueNoise(float2(across * 1.3, 3.7)) - 0.5) * 0.4;
    half strand = PunchyValueNoise(float2(across * 38, row * 1.2)) * 0.6 + PunchyValueNoise(float2(across * 90 + 3.1, row * 2.5)) * 0.4;
    half clump = PunchyValueNoise(float2(across * 4.0, row * 1.5 + 11.1));
    half layer = smoothstep(0.62, 1.0, frac(rr)) * 0.32 * PunchyWoodFade(0.42, footprint);
    return (1 + (strand - 0.5) * 0.6 * PunchyWoodFade(0.02, footprint) + (clump - 0.5) * 0.3 * PunchyWoodFade(0.25, footprint)) * (1 - layer);
}

#define PUNCHY_THATCH_STRAW 0
#define PUNCHY_THATCH_REED 1
#define PUNCHY_THATCH_HEATHER 2
#define PUNCHY_THATCH_PALM 3

/// Water reed (ThatchKind.Reed): dressed flat, so the face is a stipple of cut stem ends in even 0.3 m courses, with a
/// faint comb line along each course.
half3 PunchyThatchReed(float across, float row, float footprint)
{
    float rr = row / 0.3;
    float2 cell = float2(across, row) / 0.012;
    cell.x += floor(cell.y) * 0.5;
    int2 id = int2(floor(cell));
    half stem = PunchyCellHash(id);
    half2 f = frac(cell) - 0.5 - (half2(stem, PunchyCellHash(id + int2(7, 19))) - 0.5) * 0.35;
    half end = 1 - smoothstep(0.15 + stem * 0.15, 0.3 + stem * 0.2, length(f));
    half stipple = ((stem - 0.5) * 0.35 - end * 0.18) * PunchyWoodFade(0.012, footprint);
    half comb = (PunchyValueNoise(float2(across * 22, rr * 3)) - 0.5) * 0.12 * PunchyWoodFade(0.05, footprint);
    half course = smoothstep(0.85, 1.0, frac(rr)) * 0.16 * PunchyWoodFade(0.3, footprint);
    half patch = (PunchyValueNoise(float2(across, row) / 1.6 + 5.3) - 0.5) * 0.14 * PunchyWoodFade(1.6, footprint);
    return (1 + stipple + comb + patch) * (1 - course);
}

/// Heather (ThatchKind.Heather): coarse wiry twigs in lumpy uneven layers, dark crevices between clumps, and green moss
/// in the hollows where it holds the wet.
half3 PunchyThatchHeather(float across, float row, float footprint)
{
    float rr = row / 0.32 + (PunchyValueNoise(float2(across * 2.1, 8.3)) - 0.5) * 0.9;
    half twig = abs(PunchyValueNoise(float2(across * 46 + row * 9, row * 30)) - 0.5) * 2;
    half wire = (1 - smoothstep(0.0, 0.18, twig)) * 0.3 * PunchyWoodFade(0.015, footprint);
    half clump = PunchyValueNoise(float2(across, row) / 0.14 + 2.7);
    half hollow = (1 - smoothstep(0.25, 0.5, clump)) * PunchyWoodFade(0.14, footprint);
    half layer = smoothstep(0.55, 1.0, frac(rr)) * 0.38 * PunchyWoodFade(0.32, footprint);
    half moss = smoothstep(0.55, 0.8, PunchyValueNoise(float2(across, row) / 0.9 + 17.9)) * hollow * PunchyWoodFade(0.9, footprint);
    half3 c = (1 + wire - hollow * 0.3 + (clump - 0.5) * 0.25 * PunchyWoodFade(0.14, footprint)) * (1 - layer);
    return lerp(c, half3(0.7, 0.95, 0.45), moss * 0.6);
}

/// Palm fronds (ThatchKind.Palm): broad leaflets slanting off a rib in lapped 0.35 m courses, a hard shadow under each
/// lap and pale split edges.
half3 PunchyThatchPalm(float across, float row, float footprint)
{
    float rr = row / 0.35;
    float c = floor(rr);
    half fr = frac(rr);
    float frond = across / 0.9 + c * 0.37;
    float d = (frac(frond) - 0.5) * 0.9;
    float slant = (fr * 0.35 + abs(d) * 0.75) / 0.045 + floor(frond) * 0.31;
    half leaf = frac(slant);
    half split = (1 - smoothstep(0.0, 0.12, min(leaf, 1 - leaf))) * 0.22 * PunchyWoodFade(0.045, footprint);
    half rib = (1 - smoothstep(0.0, 0.025, abs(d))) * 0.15 * PunchyWoodFade(0.06, footprint);
    half tone = (PunchyCellHash(int2((int)c * 131 + (int)floor(frond), (int)floor(slant) + (d > 0 ? 977 : 0))) - 0.5) * 0.24 * PunchyWoodFade(0.045, footprint);
    half under = smoothstep(0.62, 1.0, fr) * 0.42 * PunchyWoodFade(0.35, footprint);
    return (1 + tone + split + rib) * (1 - under);
}

/// The thatch look of a ThatchKind (MaterialLibrary.ThatchKind): straw is PunchyThatch. Returns an albedo multiplier;
/// the kind's colour comes from the piece (MaterialLibrary.ThatchTint).
half3 PunchyThatchKind(float3 p, float3 nOS, float footprint, int kind)
{
    if (kind == PUNCHY_THATCH_STRAW)
        return PunchyThatch(p, nOS, footprint);
    float across, row;
    PunchyWoodFaceCoords(p, nOS, across, row);
    if (kind == PUNCHY_THATCH_REED)
        return PunchyThatchReed(across, row, footprint);
    if (kind == PUNCHY_THATCH_HEATHER)
        return PunchyThatchHeather(across, row, footprint);
    return PunchyThatchPalm(across, row, footprint);
}

#endif
