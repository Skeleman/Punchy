// Water.shader's depth pass (vertex DepthVert, fragment DepthFrag), shared with WaterTess.shader.
// The including pass sets its pragmas (and _WATER_FIELD) first.
#include "SharedClock.hlsl"
#include "WaterSwell.hlsl"
#include "WaterField.hlsl"

struct DepthAttributes
{
    float3 positionOS : POSITION;
    float4 data       : TEXCOORD0;   // as WaterAttributes: z the depth below the surface
#ifdef _WATER_FIELD
    float4 parent     : TEXCOORD3;   // as WaterAttributes
    float2 parentY    : TEXCOORD4;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 restXZ     : TEXCOORD0;   // where the point is before the swell moves it
};

DepthVaryings DepthVert(DepthAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    DepthVaryings o;
    float swell;
    float3 rest = TransformObjectToWorld(input.positionOS);
    o.restXZ = rest.xz;
#ifdef _WATER_FIELD
    o.positionCS = TransformWorldToHClip(PunchyWaterPatchVertex(rest, input.data, input.parent, input.parentY, _PunchyClock, swell));
#else
    o.positionCS = TransformWorldToHClip(PunchySwellVertex(rest, input.data.z, _PunchyClock, swell));
#endif
    return o;
}

half4 DepthFrag(DepthVaryings input) : SV_Target
{
#ifndef _WATER_FIELD
    // (The fine sheet stands in for the chunk water near the player; cave water is never under it.)
    clip(PunchyWaterInPatch(input.restXZ) && _UnderRock < 0.5 ? -1 : 1);
#endif
    return 0;
}
