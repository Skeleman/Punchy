#ifndef PUNCHY_WATER_FIELD_INCLUDED
#define PUNCHY_WATER_FIELD_INCLUDED

// R-9: the water deforms under what swims, wades or lands in it. Near the player, WaterPatchSystem.cs draws the water
// again as a fine sheet (12.5 cm quads over a 32 m square) and steps a wave field over the same square
// (WaterField.compute): the fine sheet's vertices rise and fall by it, and its normals follow. The chunk water clips
// itself out of the square (where the fine sheet stands in for it), and further off or on Low the swimmer ripples
// (SwimmerWakes) carry on as before. Cosmetic and per machine.

// The field: heights (m) in r, the step before in g; clamped, bilinear.
TEXTURE2D(_PunchyWaterField);
SAMPLER(sampler_PunchyWaterField);
// x, z of the field's corner, texels per metre, texels across.
float4 _PunchyWaterFieldAt;
// The fine sheet's square (x min, z min, x max, z max): empty (min > max) when it's off.
float4 _PunchyWaterPatchRect;
// Which of the square's metre cells the fine sheet covers (1): only the chunk water's own wet cells, so other water in
// this material over dry ground there (a fountain's basin) is left alone.
TEXTURE2D(_PunchyWaterPatchMask);

// Whether a point (its place before the swell moved it) is under the fine sheet, so the chunk water leaves it out.
bool PunchyWaterInPatch(float2 restXZ)
{
    if (any(restXZ < _PunchyWaterPatchRect.xy) || any(restXZ >= _PunchyWaterPatchRect.zw))
        return false;
    return LOAD_TEXTURE2D(_PunchyWaterPatchMask, (int2)floor(restXZ - _PunchyWaterPatchRect.xy)).r > 0.5;
}

// How much the field shows here: 1 inside, easing to 0 over the square's outer 2.5 m (where the field soaks its waves
// up anyway), so the fine sheet meets the chunk water with no step.
float PunchyWaterFieldWeight(float2 restXZ)
{
    float2 inside = min(restXZ - _PunchyWaterPatchRect.xy, _PunchyWaterPatchRect.zw - restXZ);
    return saturate(min(inside.x, inside.y) / 2.5 - 0.1);
}

float2 PunchyWaterFieldUV(float2 xz)
{
    return (xz - _PunchyWaterFieldAt.xy) * _PunchyWaterFieldAt.z / _PunchyWaterFieldAt.w;
}

float PunchyWaterFieldHeight(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_PunchyWaterField, sampler_PunchyWaterField, PunchyWaterFieldUV(xz), 0).r;
}

// The field's slope (dh/dx, dh/dz), from its texels either side.
float2 PunchyWaterFieldSlope(float2 xz)
{
    float2 uv = PunchyWaterFieldUV(xz);
    float du = 1 / _PunchyWaterFieldAt.w;
    float hx0 = SAMPLE_TEXTURE2D_LOD(_PunchyWaterField, sampler_PunchyWaterField, uv - float2(du, 0), 0).r;
    float hx1 = SAMPLE_TEXTURE2D_LOD(_PunchyWaterField, sampler_PunchyWaterField, uv + float2(du, 0), 0).r;
    float hz0 = SAMPLE_TEXTURE2D_LOD(_PunchyWaterField, sampler_PunchyWaterField, uv - float2(0, du), 0).r;
    float hz1 = SAMPLE_TEXTURE2D_LOD(_PunchyWaterField, sampler_PunchyWaterField, uv + float2(0, du), 0).r;
    return float2(hx1 - hx0, hz1 - hz0) * _PunchyWaterFieldAt.z * 0.5;
}

// A fine sheet vertex: where it stands once the swell and the field have moved it. parent: on the square's edge, the
// depths at the two corners of the chunk water's edge the vertex sits on, how far along it is (m), and which way that
// edge runs (1 along z, 2 along x; 0 inside the square). The chunk water there is straight from corner to corner, so an
// edge vertex goes where that straight line is (the swell's moved corners, mixed), not where the swell itself is,
// leaving no crack between the two sheets.
float3 PunchyWaterPatchVertex(float3 restWS, float depth, float4 parent, float t, out float swell)
{
    float3 positionWS;
    [branch] if (parent.w > 0.5)
    {
        float3 along = parent.w < 1.5 ? float3(0, 0, 1) : float3(1, 0, 0);
        float3 a = restWS - along * parent.z;
        float swellA, swellB;
        float3 movedA = PunchySwellVertex(a, parent.x, t, swellA);
        float3 movedB = PunchySwellVertex(a + along, parent.y, t, swellB);
        positionWS = lerp(movedA, movedB, parent.z);
        swell = lerp(swellA, swellB, parent.z);
    }
    else
        positionWS = PunchySwellVertex(restWS, depth, t, swell);
    positionWS.y += PunchyWaterFieldHeight(restWS.xz) * PunchyWaterFieldWeight(restWS.xz);
    return positionWS;
}

#endif
