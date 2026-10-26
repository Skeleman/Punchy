#ifndef PUNCHY_FLORA_INCLUDED
#define PUNCHY_FLORA_INCLUDED

#include "PlayerReveal.hlsl"

// Plants drawn by FloraSystem with indirect instanced draws. Each draw is one variant at one LOD; its instances
// are the plants FloraCull.compute found visible, stored from _RegionStart in _FloraVisible as (plant index, dither
// fade). Plant meshes carry albedo in the vertex colour and, in UV0: x wind sway, y part (0 bark, 1 foliage,
// 2 petal, 3 cactus), z leaf flutter, w ambient occlusion. Must match FloraSystem.FloraInstance.

struct FloraInstance
{
    float3 position;
    float yaw;
    float scale;
    int model;
    float random;
    float snow;
};

StructuredBuffer<FloraInstance> _FloraInstances;
StructuredBuffer<uint2> _FloraVisible;
uint _RegionStart;
float2 _PunchyWindDirection;
half _PunchyWind;

struct FloraAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;
    float4 data       : TEXCOORD0;
    uint instanceID   : SV_InstanceID;
};

struct FloraVertex
{
    float3 positionWS;
    float3 normalWS;
    float fade;
    FloraInstance plant;
};

FloraVertex FloraTransform(FloraAttributes input)
{
    uint2 visible = _FloraVisible[_RegionStart + input.instanceID];
    FloraInstance plant = _FloraInstances[visible.x];

    float c = cos(plant.yaw), s = sin(plant.yaw);
    float3 p = input.positionOS * plant.scale;
    p = float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
    float3 n = float3(input.normalOS.x * c - input.normalOS.z * s, input.normalOS.y, input.normalOS.x * s + input.normalOS.z * c);

    // Wind: the whole plant leans and sways with gusts that travel across the land; leaves also flutter.
    float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
    float phase = _Time.y * (0.7 + _PunchyWind * 1.1) - dot(plant.position.xz, _PunchyWindDirection) * 0.04 + plant.random * 6.28;
    float gust = (0.6 + 0.4 * sin(phase) * sin(phase * 0.37 + 1.3)) * (0.08 + 0.92 * _PunchyWind);
    float sway = input.data.x * (0.05 + 0.045 * p.y);
    p += windDir * sway * gust * 2.2;
    p.y -= sway * gust * sway * 0.6;   // bending, not stretching
    float flutter = input.data.z * (0.15 + _PunchyWind) * 0.022 * plant.scale;
    p += n * sin(_Time.y * 7.3 + dot(input.positionOS, float3(3.1, 2.3, 4.7)) + plant.random * 20) * flutter;

    FloraVertex o;
    o.positionWS = plant.position + p;
    o.normalWS = n;
    o.fade = asfloat(visible.y);
    o.plant = plant;
    return o;
}

// Screen-space dither (interleaved gradient noise) for LOD crossfades.
float FloraDitherValue(float4 positionCS)
{
    return frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
}

void FloraDither(float4 positionCS, float fade)
{
    float dither = FloraDitherValue(positionCS);
    clip(fade >= 0 ? fade - dither : dither - (-fade));
}

// As above, and also dissolves whatever is right in front of the camera (when it pushes into a bush or canopy) or
// stands between the camera and the player.
void FloraDither(float4 positionCS, float fade, float3 positionWS)
{
    FloraDither(positionCS, fade);
    float dither = FloraDitherValue(positionCS);
    clip(saturate((length(positionWS - GetCameraPositionWS()) - 0.3) / 0.9) - dither);
    PunchyPlayerRevealClip(positionWS, dither);
}

#endif
