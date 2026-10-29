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
// Doors something has opened (FloraDoors): xyz where, w how far open; how many are set.
float4 _PunchyDoorOpen[16];
int _PunchyDoorCount;
half _PunchyDoorDay;   // 1 by day, 0 at night (FloraDoors, from the sky's night)
// The shared tiling noise CaveSystem makes (rg a normal, b a smooth height): grain on rock and bark up close
// (Flora.shader), and the gusts that roll across the land (cloth here, grass in GroundDetail.hlsl).
TEXTURE2D(_PunchyCaveDetail);
SAMPLER(sampler_PunchyCaveDetail);
float _PunchyCaveDetailReady;
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
    float3 positionOS = input.positionOS;
    float3 normalOS = input.normalOS;
    // Door leaves (part 12) swing outwards about their hinge: x is the distance from the hinge along the leaf, z the
    // door's facing (FloraMeshBuilder.Hinge). A few doors stand ajar by day, all shut at night, and any that something
    // has opened (FloraDoors, matched by the hinge within 2 m) open as far as it says.
    if ((int)round(input.data.y) == 12)
    {
        float yaw = input.data.z;
        float3 across = float3(cos(yaw), 0, -sin(yaw));
        float3 outwards = float3(sin(yaw), 0, cos(yaw));
        float3 hingeOS = positionOS - across * input.data.x;
        float3 hs = hingeOS * plant.scale;
        float3 hingeWS = plant.position + float3(hs.x * c - hs.z * s, hs.y, hs.x * s + hs.z * c);
        float open = _PunchyDoorDay * step(0.75, frac(plant.random * 13.7)) * 0.3;
        // A set door matches a leaf if it stands out in front of it (a Door anchor is out on the plot, up to several
        // metres from a small house's door), across no further than the doorway, and at about its height. Doors of
        // the houses opposite face the other way, so they never match.
        float3 acrossWS = float3(across.x * c - across.z * s, 0, across.x * s + across.z * c);
        float3 outWS = float3(outwards.x * c - outwards.z * s, 0, outwards.x * s + outwards.z * c);
        for (int i = 0; i < _PunchyDoorCount; i++)
        {
            // Height against the building's base, not each vertex's own: a tall leaf must match as one piece.
            float3 v = _PunchyDoorOpen[i].xyz - float3(hingeWS.x, plant.position.y, hingeWS.z);
            float side = dot(v.xz, acrossWS.xz), ahead = dot(v.xz, outWS.xz);
            if (side > -1.0 && side < 3.0 && ahead > -0.5 && ahead < 7.0 && abs(v.y) < 2.5)
                open = max(open, _PunchyDoorOpen[i].w);
        }
        float phi = open * 1.65;
        float3 rel = positionOS - hingeOS;
        float u = dot(rel, across), d = dot(rel, outwards);
        float3 a2 = across * cos(phi) + outwards * sin(phi);
        float3 f2 = outwards * cos(phi) - across * sin(phi);
        positionOS = hingeOS + a2 * u + f2 * d + (rel - across * u - outwards * d);
        normalOS = a2 * dot(normalOS, across) + f2 * dot(normalOS, outwards) + float3(0, normalOS.y, 0);
    }
    float3 p = positionOS * plant.scale;
    p = float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
    float3 n = float3(normalOS.x * c - normalOS.z * s, normalOS.y, normalOS.x * s + normalOS.z * c);

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
    // (A door leaf's x is its distance from the hinge, not how much it bends.)
    float sway = ((int)round(input.data.y) == 12 ? 0 : input.data.x) * (0.05 + 0.045 * p.y);
    p += windDir * sway * gust * 2.2;
    p.y -= sway * gust * sway * 0.6;   // bending, not stretching
    float flutter = ((int)round(input.data.y) == 12 ? 0 : max(input.data.z, 0)) * (0.15 + _PunchyWind) * 0.022 * plant.scale;
    p += n * sin(_Time.y * 7.3 + dot(input.positionOS, float3(3.1, 2.3, 4.7)) + plant.random * 20) * flutter;
    // Motions carried in the flutter channel as negatives (FloraMeshBuilder.Motion, Sway), on the shared clock: a slow
    // breath along the normal (mushroom caps), below -2 a sudden small jerk now and then (cocoons), and below -10 a
    // slow pendulum sway (vines), stronger in wind.
    if (input.data.z < -99.5)
    {
        // Floating debris: the whole cluster turns slowly round the plant's centre, each piece bobbing on its own beat.
        float turn = _PunchyClock * 0.12 + plant.random * 6.28;
        float ct = cos(turn), st = sin(turn);
        p.xz = float2(p.x * ct - p.z * st, p.x * st + p.z * ct);
        n.xz = float2(n.x * ct - n.z * st, n.x * st + n.z * ct);
        p.y += sin(_PunchyClock * 0.8 + dot(input.positionOS, float3(1.7, 0.3, 2.3))) * (-input.data.z - 100) * plant.scale;
    }
    else if (input.data.z < -9.5)
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

    // Cloth (part 10; how free each vertex is, 0 at its pole or bar to 1 at the free edge, rides on the part as a
    // fraction x 0.4): a wave running out to the free edge with a quicker flutter there, leaning downwind, hanging
    // nearly limp in calm. Its gusts are the grass's own (the same noise scrolled downwind as in GroundDetail.hlsl),
    // on the shared clock, so a gust passes through a field and the banners together and every player sees it.
    // Flora.shader lights it with normals from the waved surface.
    if ((int)round(input.data.y) == 10)
    {
        float free = saturate((input.data.y - 10) / 0.4);
        float3 at = plant.position + p;
        float2 gustUV = at.xz * 0.011 - _PunchyWindDirection * _PunchyClock * (0.025 + 0.06 * _PunchyWind);
        half gustNoise = SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, gustUV, 0).b;
        half gust = lerp(0.5, smoothstep(0.3, 0.75, gustNoise), _PunchyCaveDetailReady);
        float strength = (0.08 + 0.92 * _PunchyWind) * (0.3 + 0.9 * gust);
        float t = _PunchyClock * (1.6 + 4.5 * strength) + plant.random * 6.28;
        float along = (input.positionOS.x + input.positionOS.z) * 1.3;
        float wave = sin(t - free * 4.2 + along);
        float flutter = sin(t * 2.6 - free * 9.0 + input.positionOS.y * 4.1) * free;
        float amount = free * (0.06 + 0.4 * strength) * plant.scale;
        float3 windDir3 = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
        p += n * (wave * 0.8 + flutter * 0.5) * amount;
        // Blown out from where it hangs: turned downwind by an angle that rises quickly with the wind (about 35
        // degrees on a breezy day, 65 in a storm, a few in calm), so it streams out and lifts as well.
        float angle = 1.15 * pow(saturate((strength - 0.05) / 0.8), 0.6) * (0.85 + 0.15 * wave);
        float reach = free * 1.5 * plant.scale;
        p += windDir3 * reach * sin(angle);
        p.y += reach * (1 - cos(angle));
    }

    // Electric arcs (part 11) jump about: each vertex jerks to a new spot a dozen times a second.
    if ((int)round(input.data.y) == 11)
    {
        float tick = floor(_PunchyClock * 13 + plant.random * 50);
        float3 h = frac(sin(float3(tick, tick + 1.3, tick + 2.7) * 12.9898 + dot(input.positionOS, float3(78.2, 37.7, 11.3))) * 43758.5453);
        p += (h - 0.5) * 0.18 * plant.scale;
    }

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
