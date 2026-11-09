#ifndef PUNCHY_FLORA_INCLUDED
#define PUNCHY_FLORA_INCLUDED

#include "PlayerReveal.hlsl"
#include "SharedClock.hlsl"
#include "WaterSwell.hlsl"

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
// W-107 (FloraShadowOptions): the shadow cache's draws aren't RenderMeshIndirect's, so each gives its visible list's start.
uint _FloraCacheBase;
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
#ifdef FLORA_CACHE_PASS
    uint2 visible = _FloraVisible[_FloraCacheBase + input.instanceID];
#else
    InitIndirectDrawArgs(0);
    uint2 visible = _FloraVisible[GetIndirectInstanceID_Base(input.instanceID)];
#endif
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
            if (side > -1.0 && side < 3.0 && ahead > -0.5 && ahead < 15.0 && abs(v.y) < 2.5)
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

#ifndef FLORA_STATIC
    // (Refactor 3.5: none of this for a model that never moves, FLORA_STATIC: rocks, logs, ruins, kit buildings.)
    // Wind (items 11-12): the whole plant bends from its base with the grass's own gusts (the same noise and scroll as
    // GroundDetail.hlsl, read once at the plant's foot so it moves as one); a slow sway of its own rides on top, and
    // leaves flutter.
    float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
    float2 plantGustUV = plant.position.xz * 0.011 - _PunchyWindDirection * _PunchyClock * (0.025 + 0.06 * _PunchyWind);
    half plantGust = lerp(0.5, smoothstep(0.3, 0.75, SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, plantGustUV, 0).b), _PunchyCaveDetailReady);
    float phase = _PunchyClock * 1.2 - dot(plant.position.xz, _PunchyWindDirection) * 0.04 + plant.random * 6.28;
    float gust = (0.62 + 0.38 * sin(phase) * sin(phase * 0.37 + 1.3)) * (0.08 + 0.92 * _PunchyWind) * (0.35 + 0.8 * plantGust);
    // W-107: far shadows can stand still (the cache always). The cull works out how still each shadow-list plant is,
    // once per plant, and hands it over as its fade above 1 (FloraCull.compute), so no vertex measures a distance.
    float still = 0;
#if defined(FLORA_CACHE_PASS)
    still = 1;
#elif defined(FLORA_SHADOW_PASS)
    float fadeIn = abs(asfloat(visible.y));          // (negative: a solid caster, 3.7 step 3; 3 to 4: a full crown, R-3)
    still = saturate(fadeIn - (fadeIn > 2.5 ? 3 : 1));
#endif
    gust *= 1 - still;
    // (A door leaf's x is its distance from the hinge, not how much it bends.)
    float sway = (int)round(input.data.y) == 12 ? 0 : input.data.x;
    // A rotation about the base, by an angle set by height alone (short plants bend further): vertices at one height
    // turn together, so a crown tips as a whole and a stem keeps its length. Moving each vertex and rescaling it to its
    // old length pulled a crown in and out, which read as inflating (item 12).
    float bend = sway * gust * (0.16 + 0.18 / max(abs(p.y), 0.5));
    float3 bendAxis = float3(windDir.z, 0, -windDir.x);
    p = p * cos(bend) + cross(bendAxis, p) * sin(bend) + bendAxis * dot(bendAxis, p) * (1 - cos(bend));
    // Flutter along one direction, not the normal: along the normal a round leaf clump swelled and shrank.
    float flutter = ((int)round(input.data.y) == 12 ? 0 : max(input.data.z, 0)) * (0.15 + _PunchyWind) * 0.022 * plant.scale * (1 - still);
    p += float3(-windDir.z * 0.82, 0.57, windDir.x * 0.82) * sin(_Time.y * 7.3 + dot(input.positionOS, float3(3.1, 2.3, 4.7)) + plant.random * 20) * flutter;
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
    // Breaths and twitches settle beyond 60-100 m, where they're too small to see.
    else if (input.data.z < -1.5)
    {
        float size = (-input.data.z - 2) * plant.scale * (1 - smoothstep(60, 100, distance(plant.position, GetCameraPositionWS())));
        float beat = frac(_PunchyClock * 0.23 + plant.random * 7.1 + input.positionOS.x * 0.37);
        float jerk = beat < 0.06 ? sin(beat / 0.06 * 18.85) : 0;
        p += float3(sin(plant.random * 40 + input.positionOS.z), 0, cos(plant.random * 40 + input.positionOS.x)) * jerk * size;
    }
    else if (input.data.z < 0)
        p += n * (-input.data.z) * plant.scale * (0.5 + 0.5 * sin(_PunchyClock * 1.571 + plant.random * 6.28))   // a 4 s breath
             * (1 - smoothstep(60, 100, distance(plant.position, GetCameraPositionWS())));

    // Cloth (part 10; how free each vertex is, 0 at its pole or bar to 1 at the free edge, rides on the part as a
    // fraction x 0.4): a wave running out to the free edge with a quicker flutter there, leaning downwind, hanging
    // nearly limp in calm. Its gusts are the grass's own (the same noise scrolled downwind as in GroundDetail.hlsl),
    // on the shared clock, so a gust passes through a field and the banners together and every player sees it.
    // Flora.shader lights it with normals from the waved surface.
    // Taut cloth (an awning held at both bars; carried below 10) ripples slowly and gently and isn't blown out (F-42).
    if ((int)round(input.data.y) == 10)
    {
        bool taut = input.data.y < 10;
        // Hide (Cloth + 0.45, W-113) is held still: past free cloth's 0.4, so it never shares a triangle with free cloth.
        float free = input.data.y > 10.42 ? 0 : saturate(abs(input.data.y - 10) / 0.4);
        float3 at = plant.position + p;
        // W-43: one gust per banner (read at its root, so the cloth can't tear between vertices), scrolled at a fixed
        // rate and heading. The grass's scroll (clock x wind x direction) jumps by whole noise cells whenever the wind's
        // strength or heading drifts, because the clock is hours in; a banner showed each jump as a snap.
        float2 gustUV = plant.position.xz * 0.011 - float2(0.8, 0.6) * _PunchyClock * 0.055;
        half gustNoise = SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, gustUV, 0).b;
        half gust = lerp(0.5, smoothstep(0.3, 0.75, gustNoise), _PunchyCaveDetailReady);
        float strength = (0.08 + 0.92 * _PunchyWind) * (0.3 + 0.9 * gust);
        // Banners roll slowly, a big wave about every three seconds in a breeze, never a buzz (Z-33: they shook far
        // too fast). The rate is fixed: wind and gusts change only how far it moves. W-43: the rate rode on the gust,
        // so the phase (clock x rate, the clock hours in) leapt by radians whenever a gust or the wind changed, and
        // each vertex read its own gust, so the cloth tore and flapped erratically.
        float t = _PunchyClock * (taut ? 1.0 : 1.9) + plant.random * 6.28;
        // Its own phase per piece along the building (the hall's two banners stand 4.4 m apart: x 1.3 put them 5.7
        // radians apart, nearly in step; x 0.9 puts them ~4 apart).
        float along = (input.positionOS.x + input.positionOS.z) * 0.9;
        float wave = sin(t - free * 4.2 + along);
        // The quicker flutter at the free edge only up close: far off it's a shimmer the eye reads as noise (Z-33).
        float flutterNear = 1 - smoothstep(30, 80, distance(plant.position, GetCameraPositionWS()));
        float flutter = taut ? 0 : sin(t * 1.8 - free * 5.5 + input.positionOS.y * 2.6) * free * flutterNear;
        // Taut cloth stirs only in wind, and gently (G-19): nothing in calm air.
        float amount = free * (taut ? 0.08 * smoothstep(0.2, 0.9, _PunchyWind) * (0.5 + 0.5 * gust) : 0.06 + 0.4 * strength) * plant.scale;
        float3 windDir3 = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
        p += n * (wave * 0.8 + flutter * 0.35) * amount;
        // Blown out from where it hangs: turned downwind by an angle that rises quickly with the wind (about 35
        // degrees on a breezy day, 65 in a storm, a few in calm), so it streams out and lifts as well.
        float angle = 1.15 * pow(saturate((strength - 0.05) / 0.8), 0.6) * (0.85 + 0.15 * wave);
        float reach = taut ? 0 : free * 1.5 * plant.scale;
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

#endif
#ifndef FLORA_CACHE_PASS
    // Floating things (lily pads, sea folk buoys and rafts) ride the sea's swell, lifted and tipped with the surface
    // under their middle (playtest 11-08 item 20). FloraPlacement marks them with their water's depth as a negative
    // snow (nothing floats under snow). Not in the still shadow cache.
    [branch] if (plant.snow < 0)
    {
        float3 ride = PunchySwellFloat(plant.position, -plant.snow);
        p.y += ride.y + dot(ride.xz, p.xz);
    }
#endif
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

// Moss sheets (FloraPart.Moss = 13, W-64 #24): x the cover from the vertex colour's alpha (1 in the sheet's middle,
// 0 at its rim), or 2 on any other part; yzw the point in the plant's own frame. Every pass carries it and calls
// FloraMossClip, so the holes and torn edge cut the same in the colour, shadows and depth.
float4 FloraMossData(FloraAttributes input)
{
    return float4(abs(input.data.y - 13) < 0.5 ? input.color.a : 2, input.positionOS);
}

float FloraMossHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float FloraMossNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(FloraMossHash(i), FloraMossHash(i + float3(1, 0, 0)), f.x),
                     lerp(FloraMossHash(i + float3(0, 1, 0)), FloraMossHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(FloraMossHash(i + float3(0, 0, 1)), FloraMossHash(i + float3(1, 0, 1)), f.x),
                     lerp(FloraMossHash(i + float3(0, 1, 1)), FloraMossHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// Clumps (coarse noise) and a frayed fringe (fine noise): the thinner the cover, the more of the sheet is cut away.
half FloraMossClump(float3 p)
{
    return FloraMossNoise(p * 9) * 0.6 + FloraMossNoise(p * 31 + 3.1) * 0.4;
}

void FloraMossClip(float4 moss)
{
    if (moss.x > 1.5)
        return;
    // W-101 (#80): a fine ragged hem on top of the clumps, so the rim frays into tufts instead of a smooth cut.
    clip(moss.x * 1.4 + FloraMossClump(moss.yzw) + (FloraMossNoise(moss.yzw * 70 + 1.3) - 0.5) * 0.4 - 1.0);
}

#endif
