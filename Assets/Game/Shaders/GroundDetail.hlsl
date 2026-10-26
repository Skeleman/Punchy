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
float4 _PunchyPlayerPos;
float2 _PunchyWindDirection;
half   _PunchyWind;

// Fraction of a tile's instances kept at a distance (matches GroundDetailSystem.Thinning).
float DetailThinning(float distance)
{
    return lerp(1.0, 0.12, smoothstep(12.0, _DetailRadius, distance));
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

static const uint BladeRow[15]  = { 0, 0, 1,  0, 1, 1,  1, 1, 2,  1, 2, 2,  2, 2, 3 };
static const float BladeSide[15] = { -1, 1, -1,  1, 1, -1,  -1, 1, -1,  1, 1, -1,  -1, 1, 0 };
static const float BladeT[4] = { 0, 0.4, 0.75, 1 };
static const float BladeWidth[4] = { 1, 0.8, 0.5, 0 };

// World position, normal and height fraction of one blade vertex.
void BuildBladeVertex(Blade blade, uint instance, uint vertex, out float3 positionWS, out float3 normalWS, out float t)
{
    float distance = length(blade.position - GetCameraPositionWS());
    float keep;
    float visibility = DetailVisibility(instance, distance, keep);

    uint row = BladeRow[vertex];
    t = BladeT[row];
    float height = blade.height * visibility;
    // Thinned-out blades grow wider so distant grass keeps its coverage.
    float width = blade.width * BladeWidth[row] * BladeSide[vertex] * min(rsqrt(keep), 2.5);

    float3 across = float3(cos(blade.facing), 0, sin(blade.facing));
    float3 lean = float3(-across.z, 0, across.x);

    // Wind: gusting sway along the wind direction, travelling across the field.
    float3 windDir = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y);
    float phase = _Time.y * (1.3 + _PunchyWind * 1.6) - dot(blade.position.xz, _PunchyWindDirection) * 0.12 + blade.random * 6.28;
    float sway = (sin(phase) * 0.5 + 0.5) * (0.15 + 0.85 * _PunchyWind) + sin(phase * 2.7) * 0.06;
    float3 wind = windDir * sway * height * 0.7;

    // The player pushes blades aside.
    float2 away = blade.position.xz - _PunchyPlayerPos.xz;
    float push = saturate(1 - length(away) / 0.9) * step(abs(blade.position.y - _PunchyPlayerPos.y), 1.5);
    float3 shove = float3(normalize(away + 1e-4).x, 0, normalize(away + 1e-4).y) * push * height * 0.9;

    float3 bend = lean * blade.bend * height + wind + shove;
    positionWS = blade.position + across * width + float3(0, height * t * (1 - 0.35 * push), 0) + bend * (t * t);

    float3 tangent = float3(0, height, 0) + bend * (2 * t);
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
