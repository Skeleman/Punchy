#ifndef PUNCHY_ROCK_WALL_INCLUDED
#define PUNCHY_ROCK_WALL_INCLUDED

// W-162 (#327): the one rock wall (rule 33). Cliff faces (TerrainSurface: steep ground, scarp skins, cliff kit) and cave
// walls (Cave) draw their rock's big shapes from here, then the shared stone surface (Stone.hlsl) on top, and each adds
// its own layers (mesa strata and slabs on cliffs; blotches, floors, moss and finishes in caves).
// - Beds: a thick and a thin set by height, wandering slowly along the face so they aren't contour lines, each bed's top
//   a ledge (the normal tips up where the beds step out) and its lip's underside in shade.
// - Slabs: patches of lighter and darker stone, wide along the beds, short across them.
// - The colour from the country rock round it: the caller passes it in (the biome's steep rock; in a cave, baked per
//   vertex from the ground round it and faded to the style's own colour with depth).
// Every part fades by its own on-screen size (pxm: pixels a metre spans; DetailPolicy.ScreenSize, rule 20), never by
// fixed metres. Value noise only, no texture reads.
// The includer declares _PunchyCaveDetail and sampler_PunchyCaveDetail before this file (for Stone.hlsl).

#include "Stone.hlsl"

// R-19's beds at period scale m (1: 6 m and 1.4 m sets). y is the height; xz the ground position (the wander). two:
// both sets and both wanders (else the thick set and the broad wander only). face: how much the normal may tip.
// band: the bed's light or dark shade (about -0.5..0.5, scaled by its fade); ledge: the lip underside's shade (0..1).
// wy: the wandered height, for the slabs.
void PunchyRockBeds(float2 xz, float y, float m, float pxm, bool two, half face, inout float3 n, out half band, out half ledge, out float wy)
{
    half thickF = saturate((6.0 * m * pxm - 2) / 2), thinF = saturate((1.4 * m * pxm - 2) / 2);
    y += (PunchyValueNoise(xz * 0.013 + 5.1) - 0.5) * 8 * m;
    [branch] if (two)
        y += (PunchyValueNoise(xz * 0.07 + 1.7) - 0.5) * 1.6 * m;
    wy = y;
    const float ey = 0.2;
    float yt = y / (6.0 * m), ye = (y + ey) / (6.0 * m);
    float2 bed = (float2(PunchyValueNoise(float2(yt, 19.7)), PunchyValueNoise(float2(ye, 19.7))) - 0.5) * thickF;
    [branch] if (two)
        bed += (float2(PunchyValueNoise(float2(y / (1.4 * m), 23.1)), PunchyValueNoise(float2((y + ey) / (1.4 * m), 23.1))) - 0.5) * 0.5 * thinF;
    band = bed.x;
    half rise = (bed.y - bed.x) / ey;
    n = normalize(n + float3(0, saturate(rise) * 0.5 * face, 0));
    ledge = saturate(-rise * 0.5);
}

// R-19 1c's slabs: soft-thresholded value noise on a warped domain (no cells, grid or straight seams), read along x and
// along z with the bed height wy, so a face of any heading gets them. They show once a 2.5 m slab spans slabPx pixels,
// full at twice that, and soften to mottling far off. two: three reads across two headings (else one read, one warp).
half PunchyRockSlabs(float2 xz, float wy, float pxm, bool two, half slabPx)
{
    half blockF = saturate((2.5 * pxm - slabPx) / slabPx);
    half block = 0;
    [branch] if (blockF > 0.001)
    {
        half s;
        [branch] if (two)
        {
            float y = wy + (PunchyValueNoise(xz * 0.21 + 2.9) - 0.5) * 1.2;
            half sA = PunchyValueNoise(float2(xz.x * 0.22 + PunchyValueNoise(xz * 0.09) * 2.5, y * 0.75));
            half sB = PunchyValueNoise(float2(xz.y * 0.22 + PunchyValueNoise(xz * 0.09 + 4.1) * 2.5, y * 0.75 + 31.7));
            half sC = PunchyValueNoise(float2((xz.x + xz.y) * 0.5, y * 1.6 + 9.3));
            s = (sA + sB) * 0.35 + sC * 0.3;
        }
        else
            s = PunchyValueNoise(float2((xz.x + xz.y) * 0.22 + PunchyValueNoise(xz * 0.09) * 2.5, wy * 0.75));
        half far = lerp(0.45, 1, saturate((1.0 * pxm - 2) / 3));
        block = (smoothstep(0.36, 0.64, s) - 0.5) * 0.9 * blockF * far;
    }
    return block;
}

// The beds, slabs and ledge shade on a rock colour; amount scales the beds and slabs (less where the caller's own
// banding already carries the colour, as mesa strata).
half3 PunchyRockShade(half3 rock, half band, half block, half ledge, half amount)
{
    return rock * (1 + band * amount) * (1 + block * 0.7 * amount) * (1 - ledge * 0.45);
}

// The rock's two tones from the country rock: the pale one is the country colour, the dark one the same stone in its
// hollows, at the style's own dark-to-pale ratio. share: how much of the country rock shows (1 at a cave mouth, fading
// to the style's own colours with depth).
void PunchyRockCountry(half3 country, half share, inout half3 pale, inout half3 dark)
{
    half ratio = saturate(dot(dark, 1) / max(dot(pale, 1), 1e-3));
    pale = lerp(pale, country, share);
    dark = lerp(dark, country * ratio, share);
}

#endif
