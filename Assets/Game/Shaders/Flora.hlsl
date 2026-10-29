#ifndef PUNCHY_FLORA_INCLUDED
#define PUNCHY_FLORA_INCLUDED

#include "PlayerReveal.hlsl"
#include "SharedClock.hlsl"

// Plants drawn by FloraSystem with indirect instanced draws. Each draw is one variant at one LOD; its instances
// are the plants FloraCull.compute found visible, stored from the draw's startInstance in _FloraVisible as (plant index, dither
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
    float2 tilt;   // the ground normal's x and z: rocks lie on the slope
    uint ground;   // RGB8 ground colour round a built thing's footprint and, in alpha, the dirt band's height (cm; 0: none)
};

StructuredBuffer<FloraInstance> _FloraInstances;
StructuredBuffer<uint2> _FloraVisible;
// Each draw's region of _FloraVisible starts at its indirect arguments' startInstance (FloraSystem and CaveSystem
// draw with RenderMeshIndirect), so draws share one material with no per-draw property block.
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
float _FloraNoBatcher;   // see Flora.shader's Properties
float _FloraInCave;      // 1 for cave plants (CaveSystem's copy of the material)
float2 _PunchyWindDirection;
half _PunchyWind;
// 0 above ground, 1 with the camera deep in a cave; the player's lantern (xyz position, w strength) and its colour
// (rgb) and range (w). Set by CaveSystem.
float _PunchyUnderground;
float4 _PunchyLantern;
float4 _PunchyLanternColor;

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
    InitIndirectDrawArgs(0);
    uint2 visible = _FloraVisible[GetIndirectInstanceID_Base(input.instanceID)];
    FloraInstance plant = _FloraInstances[visible.x];

    float c = cos(plant.yaw), s = sin(plant.yaw);
    float3 p = input.positionOS * plant.scale;
    p = float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
    float3 n = float3(input.normalOS.x * c - input.normalOS.z * s, input.normalOS.y, input.normalOS.x * s + input.normalOS.z * c);

    // Ground tilt (rocks): turn the plant's up onto the ground normal, about the horizontal axis between them.
    if (any(plant.tilt != 0))
    {
        float3 up = float3(plant.tilt.x, sqrt(saturate(1 - dot(plant.tilt, plant.tilt))), plant.tilt.y);
        float3 axis = normalize(float3(up.z, 0, -up.x));
        float cosA = up.y, sinA = sqrt(saturate(1 - cosA * cosA));
        // Rodrigues' rotation of p and n about axis by the angle between (0, 1, 0) and up.
        p = p * cosA + cross(axis, p) * sinA + axis * dot(axis, p) * (1 - cosA);
        n = n * cosA + cross(axis, n) * sinA + axis * dot(axis, n) * (1 - cosA);
    }

    // Wind: the whole plant leans and sways with gusts that travel across the land; leaves also flutter.
    float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
    float phase = _Time.y * (0.7 + _PunchyWind * 1.1) - dot(plant.position.xz, _PunchyWindDirection) * 0.04 + plant.random * 6.28;
    float gust = (0.6 + 0.4 * sin(phase) * sin(phase * 0.37 + 1.3)) * (0.08 + 0.92 * _PunchyWind);
    float sway = input.data.x * (0.05 + 0.045 * p.y);
    p += windDir * sway * gust * 2.2;
    p.y -= sway * gust * sway * 0.6;   // bending, not stretching
    float flutter = max(input.data.z, 0) * (0.15 + _PunchyWind) * 0.022 * plant.scale;
    p += n * sin(_Time.y * 7.3 + dot(input.positionOS, float3(3.1, 2.3, 4.7)) + plant.random * 20) * flutter;
    // Motions carried in the flutter channel as negatives (FloraMeshBuilder.Motion, Sway), on the shared clock: a slow
    // breath along the normal (mushroom caps), below -2 a sudden small jerk now and then (cocoons), and below -10 a
    // slow pendulum sway (vines), stronger in wind.
    if (input.data.z < -9.5)
    {
        float swing = (-input.data.z - 10) * plant.scale * (0.6 + _PunchyWind * 0.8);
        float t = _PunchyClock * 1.1 + plant.random * 6.28 + (input.positionOS.x + input.positionOS.z) * 0.35;
        float2 side = float2(-_PunchyWindDirection.y, _PunchyWindDirection.x);
        float2 move = _PunchyWindDirection * (sin(t) * 0.7 + _PunchyWind * 0.5) + side * sin(t * 0.63 + 1.7) * 0.5;
        p.xz += move * swing;
    }
    else if (input.data.z < -1.5)
    {
        float size = (-input.data.z - 2) * plant.scale;
        float beat = frac(_PunchyClock * 0.23 + plant.random * 7.1 + input.positionOS.x * 0.37);
        float jerk = beat < 0.06 ? sin(beat / 0.06 * 18.85) : 0;
        p += float3(sin(plant.random * 40 + input.positionOS.z), 0, cos(plant.random * 40 + input.positionOS.x)) * jerk * size;
    }
    else if (input.data.z < 0)
        p += n * (-input.data.z) * plant.scale * (0.5 + 0.5 * sin(_PunchyClock * 1.571 + plant.random * 6.28));   // a 4 s breath

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
