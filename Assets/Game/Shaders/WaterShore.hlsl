#ifndef PUNCHY_WATER_SHORE_INCLUDED
#define PUNCHY_WATER_SHORE_INCLUDED

#include "SkyCommon.hlsl"
#include "WaterSwell.hlsl"

// The sheet runs on under the banks, so a small lift moves the waterline a long way up a gentle slope.
float PunchyShoreLift(float3 restWS, float4 data, float t)
{
    float shallow = 1 - smoothstep(0.05, 0.8, data.z);
    float near = smoothstep(4, 10, PunchyWaterPixelsPerMetre(distance(restWS, GetCameraPositionWS())));
    float weight = shallow * near * (1 - smoothstep(0.5, 1, data.w));
    float lift = 0;
    [branch] if (weight > 0)
    {
        float2 p = restWS.xz;
        float bays = PunchyValueNoise(p * 0.11) * 0.65 + PunchyValueNoise(p * 0.37 + 4.1) * 0.35;
        float lap = sin(t * 0.7 + PunchyValueNoise(p * 0.23 + 9.3) * 6.2831853);
        lift = (bays - 0.5) * 0.5 + lap * 0.03;
    }
    return lift * weight;
}

// T-edge vertices take their edge ends' lift so the sheet never cracks.
float PunchyShorePatchLift(float3 restWS, float4 data, float4 parent, float t)
{
    [branch] if (abs(parent.w) > 1e-4)
    {
        float3 along = parent.w > 0 ? float3(0, 0, 1) : float3(1, 0, 0);
        float3 a = restWS - along * parent.z;
        float3 b = a + along * abs(parent.w);
        return lerp(PunchyShoreLift(a, float4(data.xy, parent.x, data.w), t), PunchyShoreLift(b, float4(data.xy, parent.y, data.w), t),
                    parent.z / abs(parent.w));
    }
    return PunchyShoreLift(restWS, data, t);
}

// The line breaks up even where the mesh is coarse.
half PunchyShoreEdge(float2 p, float thickness, float pixelsPerMetre)
{
    float fine = smoothstep(30, 80, pixelsPerMetre);
    float rag = PunchyValueNoise(p * 0.9 + 2.7) * lerp(1, 0.6, fine) + PunchyValueNoise(p * 3.7) * 0.4 * fine;
    float reach = 0.6 * smoothstep(3, 12, pixelsPerMetre);
    return (half)saturate((thickness - reach * rag) / max(reach * 0.5, 1e-3));
}

// Width and presence of the foam rim along the line, so it is never one continuous white stroke.
float2 PunchyShoreFoamBreak(float2 p, float pixelsPerMetre)
{
    float fine = smoothstep(30, 80, pixelsPerMetre);
    float along = PunchyValueNoise(p * 0.45 + 7.1) * 0.7 + PunchyValueNoise(p * 1.9 + 1.3) * 0.3 * fine + 0.15 * (1 - fine);
    float width = lerp(0.35, 1.5, PunchyValueNoise(p * 0.3 + 3.3));
    return float2(width, smoothstep(0.42, 0.62, along));
}

#endif
