#ifndef PUNCHY_WATER_FIELD_INCLUDED
#define PUNCHY_WATER_FIELD_INCLUDED

// R-9: the water deforms under what swims, wades or lands in it. Near the player, WaterPatchSystem.cs draws the water
// again as a fine sheet (quads down to 12.5 cm, by screen size, a few metres round them in a 32 m square) and steps a wave field over the same square
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
// How much the field shows at each of the square's metre corners (bilinear between): 0 where the fine sheet meets the
// chunk water (the quads it leaves whole, or the square's edge), rising over the metres further in.
TEXTURE2D(_PunchyWaterPatchWeight);
SAMPLER(sampler_PunchyWaterPatchWeight);

// Whether a point (its place before the swell moved it) is under the fine sheet, so the chunk water leaves it out.
bool PunchyWaterInPatch(float2 restXZ)
{
    if (any(restXZ < _PunchyWaterPatchRect.xy) || any(restXZ >= _PunchyWaterPatchRect.zw))
        return false;
    return LOAD_TEXTURE2D(_PunchyWaterPatchMask, (int2)floor(restXZ - _PunchyWaterPatchRect.xy)).r > 0.5;
}

// How much the field shows here: 1 well inside the fine sheet, easing to 0 at its edge, so it meets the chunk water
// with no step.
float PunchyWaterFieldWeight(float2 restXZ)
{
    float row = _PunchyWaterPatchRect.z - _PunchyWaterPatchRect.x + 1;
    float2 uv = (restXZ - _PunchyWaterPatchRect.xy + 0.5) / row;
    return SAMPLE_TEXTURE2D_LOD(_PunchyWaterPatchWeight, sampler_PunchyWaterPatchWeight, uv, 0).r;
}

// Zach 11-11: how much the field's slope shades the water at xz: 1 over the field's square, fading out over its outer
// 3 m (where the step soaks waves up anyway), 0 outside it or with no field (WaterPatchSystem sets w = 0 then).
float PunchyWaterFieldShade(float2 xz)
{
    if (_PunchyWaterFieldAt.w <= 0)
        return 0;
    float side = _PunchyWaterFieldAt.w / _PunchyWaterFieldAt.z;
    float2 local = xz - _PunchyWaterFieldAt.xy;
    float edge = min(min(local.x, local.y), min(side - local.x, side - local.y));
    return saturate(edge / 3.0);
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

// A point of the fine sheet moved by the swell and the field.
float3 PunchyWaterPatchPoint(float3 restWS, float depth, float t, out float swell)
{
    float3 positionWS = PunchySwellVertex(restWS, depth, t, swell);
    positionWS.y += PunchyWaterFieldHeight(restWS.xz) * PunchyWaterFieldWeight(restWS.xz);
    return positionWS;
}

// A fine sheet vertex: where it stands once the swell and the field have moved it. parent: on an edge shared with a
// coarser neighbour (a coarser cut quad of the sheet, or the chunk water past the square's edge), the depths at the two
// ends of the neighbour's segment the vertex sits on, how far along that it is (m), and the segment's length (m), signed
// by which way it runs (+ along z, - along x; 0 elsewhere). The neighbour is straight from end to end, so the vertex
// goes where that straight line is (the moved ends, mixed), not where the swell and field would put it, leaving no
// crack between them. (Where the sheet meets the chunk water the field weighs nothing, so that is the chunk water's own line.)
float3 PunchyWaterPatchVertex(float3 restWS, float depth, float4 parent, float t, out float swell)
{
    [branch] if (abs(parent.w) > 1e-4)
    {
        float3 along = parent.w > 0 ? float3(0, 0, 1) : float3(1, 0, 0);
        float span = abs(parent.w);
        float3 a = restWS - along * parent.z;
        float swellA, swellB;
        float3 movedA = PunchyWaterPatchPoint(a, parent.x, t, swellA);
        float3 movedB = PunchyWaterPatchPoint(a + along * span, parent.y, t, swellB);
        float k = parent.z / span;
        swell = lerp(swellA, swellB, k);
        return lerp(movedA, movedB, k);
    }
    return PunchyWaterPatchPoint(restWS, depth, t, swell);
}

#endif
