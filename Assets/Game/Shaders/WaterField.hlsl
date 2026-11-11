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

// Row 5: the fine sheet over a river fall (WaterPatchSystem.Falls.cs), cut along the drop. Its rectangle (x min, z min,
// x max, z max; empty when off) and which of its metre cells it covers.
float4 _PunchyWaterFallRect;
TEXTURE2D(_PunchyWaterFallMask);
// The lip (xyz) and the drop (w); which way it pours (xy), its half width (z) and how far it has faded in (w); how far
// along the flow from the lip the sheet starts (x), the foot is (y) and the sheet ends (z).
float4 _PunchyWaterFallLip;
float4 _PunchyWaterFallAxis;
float4 _PunchyWaterFallSpan;

// Whether a point (its place before the swell moved it) is under the fine sheet, so the chunk water leaves it out.
bool PunchyWaterInPatch(float2 restXZ)
{
    if (all(restXZ >= _PunchyWaterFallRect.xy) && all(restXZ < _PunchyWaterFallRect.zw) &&
        LOAD_TEXTURE2D(_PunchyWaterFallMask, (int2)floor(restXZ - _PunchyWaterFallRect.xy)).r > 0.5)
        return true;
    if (any(restXZ < _PunchyWaterPatchRect.xy) || any(restXZ >= _PunchyWaterPatchRect.zw))
        return false;
    return LOAD_TEXTURE2D(_PunchyWaterPatchMask, (int2)floor(restXZ - _PunchyWaterPatchRect.xy)).r > 0.5;
}

// How much the field shows here: 1 well inside the fine sheet, easing to 0 at its edge, so it meets the chunk water
// with no step.
float PunchyWaterFieldWeight(float2 restXZ)
{
    // (The fall's sheet shares the vertex code; outside the player's square the field weighs nothing.)
    if (any(restXZ < _PunchyWaterPatchRect.xy) || any(restXZ > _PunchyWaterPatchRect.zw))
        return 0;
    float row = _PunchyWaterPatchRect.z - _PunchyWaterPatchRect.x + 1;
    float2 uv = (restXZ - _PunchyWaterPatchRect.xy + 0.5) / row;
    return SAMPLE_TEXTURE2D_LOD(_PunchyWaterPatchWeight, sampler_PunchyWaterPatchWeight, uv, 0).r;
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

// How far a fall's water moves along its flow, which the wave field (a height field) can't do on an upright sheet: the
// lip bulges and curls out, ripples run down the curtain, and the pool churns at the foot. Fades to nothing at the
// sheet's edges, across its ends, and with how big the fall looks (Axis.w), so it meets the chunk water and fades in
// with no step. churn: how much the foot is churning here (for foam); curtain: how much this is the falling curtain
// (the shader keeps it shaded as a steep face even where a ripple tilts a triangle back).
float3 PunchyWaterFallOffset(float3 restWS, float t, out float churn, out float curtain)
{
    churn = 0;
    curtain = 0;
    float fade = _PunchyWaterFallAxis.w;
    [branch] if (fade <= 0 || any(restWS.xz < _PunchyWaterFallRect.xy) || any(restWS.xz > _PunchyWaterFallRect.zw))
        return 0;
    float2 o = _PunchyWaterFallAxis.xy;
    float2 rel = restWS.xz - _PunchyWaterFallLip.xz;
    float s = dot(rel, o);
    float c = dot(rel, float2(-o.y, o.x));
    float down = _PunchyWaterFallLip.y - restWS.y;
    float drop = _PunchyWaterFallLip.w;
    float halfWidth = _PunchyWaterFallAxis.z;
    float2 edge = min(restWS.xz - _PunchyWaterFallRect.xy, _PunchyWaterFallRect.zw - restWS.xz);
    float w = fade * (1 - smoothstep(halfWidth - 3, halfWidth, abs(c))) *
        smoothstep(_PunchyWaterFallSpan.x, _PunchyWaterFallSpan.x + 3, s) *
        (1 - smoothstep(_PunchyWaterFallSpan.z - 4, _PunchyWaterFallSpan.z, s)) *
        saturate(min(edge.x, edge.y) * 0.5 - 0.25);
    [branch] if (w <= 0)
        return 0;
    // (A phase that wanders across the fall, so the curtain isn't one sheet moving in step.)
    float ph = c * 0.9 + sin(c * 0.37) * 2.1 + sin(c * 1.3 + 1.7) * 0.6;
    float3 outward = float3(o.x, 0, o.y);
    float3 offset = 0;
    // The lip: the run-in bulges up as it reaches the edge, then curls out over it.
    offset.y += 0.12 * smoothstep(-4, -0.8, s) * (1 - smoothstep(0, 0.8, down));
    offset += outward * 0.35 * exp(-((down - 0.9) / 0.7) * ((down - 0.9) / 0.7));
    // The curtain: ripples running down it, uneven across.
    float face = smoothstep(0.5, 1.5, down) * (1 - smoothstep(drop - 1.5, drop - 0.3, down));
    offset += outward * 0.11 * face * (0.6 + 0.4 * sin(c * 0.71 + ph)) *
        (0.65 * sin(down * 3.6 - t * 7 + ph) + 0.35 * sin(down * 7.3 - t * 12 + 1.7 * ph));
    curtain = smoothstep(0.3, 1.0, down) * (1 - smoothstep(drop - 0.6, drop, down)) * w;
    // The foot: the pool heaves where the curtain lands, dying away downstream.
    float foot = _PunchyWaterFallSpan.y;
    float pool = smoothstep(drop - 1.2, drop - 0.2, down);
    float band = smoothstep(foot - 1.5, foot, s) * (1 - smoothstep(foot + 1, foot + 7, s));
    offset.y += 0.14 * pool * band * (0.6 * sin(t * 5.3 + c * 1.7 + s * 2.3) + 0.4 * sin(t * 8.1 - c * 2.9 + s * 1.1 + ph));
    churn = pool * band * w;
    return offset * w;
}

// A point of the fine sheet moved by the swell, the field and a fall.
float3 PunchyWaterPatchPoint(float3 restWS, float depth, float t, out float swell)
{
    float3 positionWS = PunchySwellVertex(restWS, depth, t, swell);
    positionWS.y += PunchyWaterFieldHeight(restWS.xz) * PunchyWaterFieldWeight(restWS.xz);
    float churn, curtain;
    positionWS += PunchyWaterFallOffset(restWS, t, churn, curtain);
    return positionWS;
}

// A fine sheet vertex: where it stands once the swell and the field have moved it. parent: on an edge shared with a
// coarser neighbour (a coarser cut quad of the sheet, or the chunk water past the square's edge), the depths at the two
// ends of the neighbour's segment the vertex sits on, how far along that it is (m), and the segment's length (m), signed
// by which way it runs (+ along z, - along x; 0 elsewhere). The neighbour is straight from end to end, so the vertex
// goes where that straight line is (the moved ends, mixed), not where the swell and field would put it, leaving no
// crack between them. (Where the sheet meets the chunk water the field weighs nothing, so that is the chunk water's own line.)
// parentY: the heights of the segment's two ends less the vertex's own (a fall's segments slope steeply).
float3 PunchyWaterPatchVertex(float3 restWS, float depth, float4 parent, float2 parentY, float t, out float swell)
{
    [branch] if (abs(parent.w) > 1e-4)
    {
        float3 along = parent.w > 0 ? float3(0, 0, 1) : float3(1, 0, 0);
        float span = abs(parent.w);
        float3 a = restWS - along * parent.z;
        float3 b = a + along * span;
        a.y = restWS.y + parentY.x;
        b.y = restWS.y + parentY.y;
        float swellA, swellB;
        float3 movedA = PunchyWaterPatchPoint(a, parent.x, t, swellA);
        float3 movedB = PunchyWaterPatchPoint(b, parent.y, t, swellB);
        float k = parent.z / span;
        swell = lerp(swellA, swellB, k);
        return lerp(movedA, movedB, k);
    }
    return PunchyWaterPatchPoint(restWS, depth, t, swell);
}

#endif
