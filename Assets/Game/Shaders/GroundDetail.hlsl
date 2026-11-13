#ifndef PUNCHY_GROUND_DETAIL_INCLUDED
#define PUNCHY_GROUND_DETAIL_INCLUDED

// Procedural geometry for ground detail drawn by GroundDetailSystem: grass blades and small stones. Each instance is
// read from a structured buffer by SV_InstanceID and its triangles built from SV_VertexID, so no meshes are needed.
// Must match GroundDetailSystem.Blade / Pebble.

struct Blade
{
    float3 position;
    float facing;
    float height;
    float width;
    float bend;
    float random;
    float4 color;
};

struct Pebble
{
    float3 position;
    float size;
    float random;
    float tilt;
    float2 pad;
    float4 color;
};

uint   _InstanceCount;
float  _DetailRadius;
float  _DetailFull;     // every instance is kept within this distance (GroundDetailSystem.FullDensityRadius)
float4 _PunchyPlayerPos;
// Other players' feet (w = 1 where there is one), set by GroundDetailSystem.
float4 _PunchyOthersPos[4];
float2 _PunchyWindDirection;
half   _PunchyWind;
// The shared tiling noise (CaveSystem makes it; b is a smooth height): gusts roll across fields as it scrolls downwind.
TEXTURE2D(_PunchyCaveDetail);
SAMPLER(sampler_PunchyCaveDetail);
float _PunchyCaveDetailReady;
#include "SharedClock.hlsl"
#include "ChemistryOverlay.hlsl"

// Fraction of a tile's instances kept at a distance (matches GroundDetailSystem.Thinning): all of them within _DetailFull.
float DetailThinning(float distance)
{
    return lerp(1.0, 0.12, smoothstep(_DetailFull, _DetailRadius, distance));
}

// 0..1 visibility of instance i at a distance: instances beyond the kept fraction shrink smoothly away, and
// everything fades out approaching the radius.
float DetailVisibility(uint instance, float distance, out float keep)
{
    keep = DetailThinning(distance);
    float order = (instance + 0.5) / max(_InstanceCount, 1);
    return saturate((keep - order) / (0.2 * keep)) * (1 - smoothstep(_DetailRadius * 0.85, _DetailRadius, distance));
}

// ------------------------------------------------------------------------------------------------ grass

// Grass clumps (Zach, 11-08: single blades scattered evenly read as stubble, never a field): each instance is a tuft of
// _GrassClump blades fanned out from one root, leaning outwards, of uneven heights (1: a lone blade, as before;
// GroundDetailSystem sets it, -grassclumps 0 for the old grass). Each blade is 15 vertices; vertex / 15 picks the blade.
float _GrassClump;   // (a float: Unity's global ints are floats)

float ClumpHash(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

Blade ClumpMember(Blade blade, uint member)
{
    if (_GrassClump <= 1)
        return blade;
    float seed = blade.random * 97.3 + member * 7.13;
    float angle = 6.2832 * (member + ClumpHash(seed)) / _GrassClump + blade.random * 6.2832;
    float radius = (member == 0 ? 0.0 : 0.05 + 0.13 * ClumpHash(seed + 1.7)) * (0.6 + 0.6 * blade.height);
    float2 out2 = float2(cos(angle), sin(angle));
    blade.position.xz += out2 * radius;
    // Lean away from the root: the blade's lean is its facing turned a quarter, so face across the outward line.
    blade.facing = angle + 1.5708 + (ClumpHash(seed + 3.1) - 0.5) * 0.6;
    blade.bend = abs(blade.bend) * 0.6 + 0.15 + 0.25 * ClumpHash(seed + 4.3) * (member == 0 ? 0.3 : 1.0);
    blade.height *= 0.7 + 0.5 * ClumpHash(seed + 5.9);
    blade.width *= 0.85 + 0.3 * ClumpHash(seed + 6.7);
    blade.random = frac(blade.random + member * 0.618);
    return blade;
}

static const uint BladeRow[15]  = { 0, 0, 1,  0, 1, 1,  1, 1, 2,  1, 2, 2,  2, 2, 3 };
static const float BladeSide[15] = { -1, 1, -1,  1, 1, -1,  -1, 1, -1,  1, 1, -1,  -1, 1, 0 };
static const float BladeT[4] = { 0, 0.4, 0.75, 1 };
static const float BladeWidth[4] = { 1, 0.8, 0.5, 0 };

// World position, normal and height fraction of one blade vertex.
void BuildBladeVertex(Blade blade, uint instance, uint vertex, out float3 positionWS, out float3 normalWS, out float t)
{
    blade = ClumpMember(blade, vertex / 15);
    vertex %= 15;
    float distance = length(blade.position - GetCameraPositionWS());
    float keep;
    float visibility = DetailVisibility(instance, distance, keep);

    uint row = BladeRow[vertex];
    t = BladeT[row];
    float height = blade.height * visibility;
    // Spell chemistry (ChemistryOverlay.hlsl): burnt grass is short stubble and burning grass shrivels (x 1 with no bridge).
    half4 chem = PunchyChemAt(blade.position.xz);
    height *= 1 - 0.85 * max(chem.g, chem.r * 0.5);
    // Thinned-out blades grow wider so distant grass keeps its coverage.
    float width = blade.width * BladeWidth[row] * BladeSide[vertex] * min(rsqrt(keep), 2.5);

    float3 across = float3(cos(blade.facing), 0, sin(blade.facing));
    float3 lean = float3(-across.z, 0, across.x);

    // Wind: patchy gusts rolling across the field. A tiling noise scrolled downwind sets how hard each blade is
    // pushed and turns it a little off the wind; a small, quick flutter rides on top. On the shared clock, so every
    // player sees the same gusts.
    float time = _PunchyClock;
    float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
    float2 gustUV = blade.position.xz * 0.011 - _PunchyWindDirection * time * (0.025 + 0.06 * _PunchyWind);
    half4 gustNoise = SAMPLE_TEXTURE2D_LOD(_PunchyCaveDetail, sampler_PunchyCaveDetail, gustUV, 0);
    half gust = lerp(0.5, smoothstep(0.3, 0.75, gustNoise.b), _PunchyCaveDetailReady);
    float3 gustDir = normalize(windDir + float3(gustNoise.r - 0.5, 0, gustNoise.g - 0.5) * 0.8 * _PunchyCaveDetailReady + 1e-4);
    float flutter = sin(time * (6.5 + blade.random * 3) + blade.random * 6.28) * (0.03 + 0.05 * gust);
    float sway = (0.1 + 0.75 * _PunchyWind) * (0.2 + 0.9 * gust) + flutter;
    float3 wind = gustDir * sway * height * 0.7;

    // The player, and anyone else nearby, pushes blades aside.
    float2 away = blade.position.xz - _PunchyPlayerPos.xz;
    float push = saturate(1 - length(away) / 0.9) * step(abs(blade.position.y - _PunchyPlayerPos.y), 1.5);
    [unroll] for (int o = 0; o < 4; o++)
    {
        float2 awayOther = blade.position.xz - _PunchyOthersPos[o].xz;
        float pushOther = saturate(1 - length(awayOther) / 0.9) * step(abs(blade.position.y - _PunchyOthersPos[o].y), 1.5) * _PunchyOthersPos[o].w;
        if (pushOther > push)
        {
            push = pushOther;
            away = awayOther;
        }
    }
    float3 shove = float3(normalize(away + 1e-4).x, 0, normalize(away + 1e-4).y) * push * height * 0.9;

    float3 bend = lean * blade.bend * height + wind + shove;
    positionWS = blade.position + across * width + float3(0, height * t * (1 - 0.35 * push), 0) + bend * (t * t);

    // (R-18, nan-safe: a blade faded to nothing has height 0 and no bend, so its tip vertex - side 0 - would normalize
    // a zero vector into a NaN normal, drawn as a NaN pixel that bloom spreads into a white circle. The floor keeps it
    // upright; any visible blade is far taller than 1e-4.)
    float3 tangent = float3(0, max(height, 1e-4), 0) + bend * (2 * t);
    normalWS = normalize(cross(across, tangent) + across * BladeSide[vertex] * 0.35);
}

// ------------------------------------------------------------------------------------------------ pebbles

static const float3 PebbleAxis[6] = { float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1) };
// 8 faces of an octahedron, by axis index (top four, then bottom four).
static const uint PebbleCorner[24] = { 2,0,4,  2,4,1,  2,1,5,  2,5,0,  3,4,0,  3,1,4,  3,5,1,  3,0,5 };

float3 PebbleCornerLocal(Pebble pebble, uint axis)
{
    float jitter = 0.7 + 0.4 * frac(sin((pebble.random * 97.1 + axis * 13.7)) * 43758.5453);
    float3 p = PebbleAxis[axis] * jitter;
    p.y *= 0.55;   // pebbles are flattish
    return p;
}

float3 PebbleToWorld(Pebble pebble, float3 local, float scale)
{
    float yaw = pebble.random * 6.2832;
    float tilt = (pebble.tilt - 0.5) * 0.6;
    float cy = cos(yaw), sy = sin(yaw), ct = cos(tilt), st = sin(tilt);
    local = float3(local.x, local.y * ct - local.z * st, local.y * st + local.z * ct);
    local = float3(local.x * cy - local.z * sy, local.y, local.x * sy + local.z * cy);
    return pebble.position + local * pebble.size * scale;
}

void BuildPebbleVertex(Pebble pebble, uint instance, uint vertex, out float3 positionWS, out float3 normalWS)
{
    float distance = length(pebble.position - GetCameraPositionWS());
    float keep;
    float scale = DetailVisibility(instance, distance, keep);

    uint face = vertex / 3;
    float3 a = PebbleToWorld(pebble, PebbleCornerLocal(pebble, PebbleCorner[face * 3]), scale);
    float3 b = PebbleToWorld(pebble, PebbleCornerLocal(pebble, PebbleCorner[face * 3 + 1]), scale);
    float3 c = PebbleToWorld(pebble, PebbleCornerLocal(pebble, PebbleCorner[face * 3 + 2]), scale);
    float3 n = normalize(cross(b - a, c - a));
    if (dot(n, (a + b + c) / 3 - pebble.position) < 0)
        n = -n;
    normalWS = n;
    positionWS = PebbleToWorld(pebble, PebbleCornerLocal(pebble, PebbleCorner[vertex]), scale);
}

#endif
