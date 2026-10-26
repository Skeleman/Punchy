#ifndef PUNCHY_LANDMARK_EFFECTS_INCLUDED
#define PUNCHY_LANDMARK_EFFECTS_INCLUDED

// Landmark effects (LandmarkEffectSystem). Each emitter is one effect; every puff and streak is animated from time
// alone. Must match LandmarkEffectSystem.Emitter.

struct LandmarkEmitter
{
    float3 position;
    float seed;
    float4 color;    // rgb, glow
    float4 shape;    // height (plume rise / waterfall reach), width, size, lifetime
    float4 timing;   // period, duty, opacity, particles
    float2 outDir;   // waterfall: horizontal direction the water travels
    float drop;      // waterfall: height of the fall
    float pad;
};

StructuredBuffer<LandmarkEmitter> _LandmarkEmitters;
uint _MaxParticles;
float4 _WaterfallGrid;   // columns, rows
float2 _PunchyWindDirection;
half _PunchyWind;

static const float2 EffectCorner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

float EffectHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

/// 0..1: how strongly an emitter is erupting at a time (always 1 for continuous ones).
float Eruption(LandmarkEmitter e, float time)
{
    float period = e.timing.x;
    if (period <= 0)
        return 1;
    float phase = frac(time / period + e.seed);
    float duty = e.timing.y;
    return smoothstep(0, 0.04, phase) * (1 - smoothstep(duty * 0.6, duty, phase));
}

struct PlumeVertex
{
    float3 positionWS;
    float2 uv;
    float alpha;
    float age;
};

/// A puff: rises (fast, then slowing) to the emitter's height, spreading out, growing and drifting downwind.
/// Puffs born outside an eruption stay hidden, so a geyser spouts and then falls quiet.
PlumeVertex BuildPlume(uint vertex, uint instance)
{
    uint index = instance / _MaxParticles;
    uint k = instance % _MaxParticles;
    LandmarkEmitter e = _LandmarkEmitters[index];
    float r = EffectHash(k * 1.731 + e.seed * 91.0);
    float life = e.shape.w;
    float age = frac(_Time.y / life + r);
    float born = _Time.y - age * life;

    float rise = 1 - (1 - age) * (1 - age);
    float angle = EffectHash(r * 5.3) * 6.2832;
    float2 out2 = float2(cos(angle), sin(angle)) * e.shape.y * (0.15 + age) * sqrt(EffectHash(r * 9.1));
    float2 drift = _PunchyWindDirection * (0.3 + _PunchyWind) * age * life * 0.8;
    float3 centre = e.position + float3(out2.x + drift.x, e.shape.x * rise * lerp(0.6, 1.0, EffectHash(r * 2.9)), out2.y + drift.y);

    float size = e.shape.z * (0.35 + 1.1 * age) * lerp(0.7, 1.3, EffectHash(r * 4.7));
    float alpha = e.timing.z * Eruption(e, born) * smoothstep(0, 0.12, age) * (1 - smoothstep(0.55, 1, age)) * step(k, e.timing.w - 1);
    alpha *= 1 - smoothstep(600, 750, distance(centre, GetCameraPositionWS()));

    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
    float2 corner = EffectCorner[vertex];
    PlumeVertex o;
    o.positionWS = centre + (right * corner.x + up * corner.y) * size * step(0.001, alpha);
    o.uv = corner;
    o.alpha = alpha;
    o.age = age;
    return o;
}

struct FallVertex
{
    float3 positionWS;
    float3 normalWS;
    float2 uv;      // across 0..1, down 0..1
    float fallLength;   // metres of fall, for streak scale
};

/// A curtain of water: leaves the lip horizontally and falls on a parabola, widening a little as it drops.
FallVertex BuildWaterfall(uint vertex, uint instance)
{
    LandmarkEmitter e = _LandmarkEmitters[instance];
    uint columns = (uint)_WaterfallGrid.x;
    uint quad = vertex / 6;
    float2 corner = EffectCorner[vertex % 6] * 0.5 + 0.5;
    float u = ((quad % columns) + corner.x) / _WaterfallGrid.x;
    float t = ((quad / columns) + corner.y) / _WaterfallGrid.y;

    float3 outDir = float3(e.outDir.x, 0, e.outDir.y);
    float3 across = normalize(cross(float3(0, 1, 0), outDir));
    float width = e.shape.y * (1 + 0.3 * t);
    float3 p = e.position + outDir * e.shape.x * t - float3(0, e.drop * t * t, 0) + across * (u - 0.5) * width;
    // Ripple the sheet so it isn't a perfect surface.
    p += outDir * sin(u * 9.0 + t * 4.0 + _Time.y * 1.7) * 0.15 * t;

    float3 tangent = normalize(outDir * e.shape.x - float3(0, 2 * e.drop * t, 0));
    FallVertex o;
    o.positionWS = p;
    o.normalWS = normalize(cross(across, tangent));
    o.uv = float2(u, t);
    o.fallLength = e.drop + e.shape.x;
    return o;
}

#endif
