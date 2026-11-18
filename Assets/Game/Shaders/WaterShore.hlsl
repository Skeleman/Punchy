#ifndef PUNCHY_WATER_SHORE_INCLUDED
#define PUNCHY_WATER_SHORE_INCLUDED

#include "SkyCommon.hlsl"
#include "WaterSwell.hlsl"

// W-160: the waterline wanders on shallow banks. The sheet runs on under the banks, so a small lift moves the line
// a long way up a gentle slope; it is held to the shallows, so deep water and fall curtains never move.
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
        lift = (bays - 0.5) * 0.2 + lap * 0.02;
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

// The pixel side: the water thins to nothing over a ragged band of the depth difference, so the line breaks up
// even where the mesh is coarse. The fine ragging fades in by screen size.
half PunchyShoreEdge(float2 p, float thickness, float pixelsPerMetre)
{
    float fine = smoothstep(30, 80, pixelsPerMetre);
    float rag = PunchyValueNoise(p * 0.9 + 2.7) * lerp(1, 0.6, fine) + PunchyValueNoise(p * 3.7) * 0.4 * fine;
    float reach = 0.25 * smoothstep(3, 12, pixelsPerMetre);
    return (half)saturate((thickness - reach * rag) / max(reach * 0.5, 1e-3));
}

#endif
