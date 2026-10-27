#ifndef PUNCHY_AMBIENT_PARTICLES_INCLUDED
#define PUNCHY_AMBIENT_PARTICLES_INCLUDED

#include "SharedClock.hlsl"

// Ambient particles (AmbientParticleSystem): each instance is one particle with a home spot on the ground. It
// hangs at a random height, wanders lazily around home, and (if the type drifts) travels up, down or with the wind
// over a set distance, fading in and out, then starts again. Must match AmbientParticleSystem.Particle.

struct AmbientParticle
{
    float3 position;
    float random;
};

StructuredBuffer<AmbientParticle> _AmbientParticles;
float4 _ParticleColor;    // rgb colour (HDR for glow), a opacity
float4 _ParticleSize;     // size min, max, height min, max
float4 _ParticleMotion;   // wander radius, rise speed, wind drift, travel distance
float4 _ParticleShape;    // twinkle, amount (condition), range
float2 _PunchyWindDirection;
half _PunchyWind;

static const float2 ParticleCorner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

float ParticleHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

struct ParticleVertex
{
    float3 positionWS;
    float2 uv;
    float visibility;   // presence × travel fade × distance fade
    float twinkle;
};

ParticleVertex BuildParticle(uint vertex, uint instance)
{
    AmbientParticle p = _AmbientParticles[instance];
    float r = p.random;
    float t = _PunchyClock;

    float presence = saturate((_ParticleShape.y - r * 0.95) / 0.05);
    float size = lerp(_ParticleSize.x, _ParticleSize.y, ParticleHash(r * 3.1));
    float height = lerp(_ParticleSize.z, _ParticleSize.w, ParticleHash(r * 7.7));
    float3 home = p.position + float3(0, height, 0);

    float wander = _ParticleMotion.x;
    float3 offset = float3(sin(t * 0.37 + r * 20.0), sin(t * 0.29 + r * 13.0) * 0.4, cos(t * 0.31 + r * 17.0)) * wander;

    // Steady drift: up/down and with the wind, looping over the travel distance.
    float3 velocity = float3(_PunchyWindDirection.x, 0, _PunchyWindDirection.y) * _ParticleMotion.z * (0.3 + _PunchyWind) +
                      float3(0, _ParticleMotion.y, 0);
    float speed = length(velocity);
    float fade = 1;
    if (speed > 1e-3)
    {
        float cycle = _ParticleMotion.w / speed;
        float phase = frac(t / cycle + r);
        offset += velocity * (phase - 0.5) * cycle;
        fade = sin(phase * PI);
    }

    float3 centre = home + offset;
    float distanceFade = 1 - smoothstep(_ParticleShape.z * 0.75, _ParticleShape.z, length(centre - GetCameraPositionWS()));
    float visibility = presence * fade * distanceFade;

    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
    float2 corner = ParticleCorner[vertex];
    ParticleVertex o;
    o.positionWS = centre + (right * corner.x + up * corner.y) * size * step(0.001, visibility);
    o.uv = corner;
    o.visibility = visibility;
    float flicker = 0.5 + 0.5 * sin(t * (2.0 + r * 6.0) + r * 31.0) * sin(t * (1.3 + r * 2.0) + r * 11.0);
    o.twinkle = lerp(1, flicker * flicker * 1.6, _ParticleShape.x);
    return o;
}

#endif
