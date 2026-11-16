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

#endif
