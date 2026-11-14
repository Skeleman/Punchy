#ifndef PUNCHY_LANDMARK_EFFECTS_INCLUDED
#define PUNCHY_LANDMARK_EFFECTS_INCLUDED

#include "SharedClock.hlsl"

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
    float underRock; // 1 for a cave's waterfall: no sun, a trace of sky, lit by the lantern and glowing things
    float4 fall;     // waterfall: lip skew, lip bow (m), cascade 0..1, what it lands on (FallLanding)
};

StructuredBuffer<LandmarkEmitter> _LandmarkEmitters;
float4 _PunchyLantern;        // the player's lantern: position, strength (CaveSystem)
float4 _PunchyLanternColor;   // its colour and reach

// The light on an effect: sun and sky in the open; under rock, a trace of the sky (from the mouth), the glowing
// things nearby and the player's lantern.
half3 PunchyEffectLight(LandmarkEmitter e, float3 p, half3 n, half3 sun, half3 sky)
{
    if (e.underRock < 0.5)
        return sun + sky;
    half3 light = sky * 0.06 + PunchyGlowLight(p, n);
    if (_PunchyLantern.w > 0)
    {
        float3 toLantern = _PunchyLantern.xyz - p;
        float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
        light += _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * saturate(dot(n, normalize(toLantern)) * 0.7 + 0.3);
    }
    // Water and spray keep their own cool white rather than taking on the lantern's amber.
    return lerp(light, dot(light, half3(0.3, 0.5, 0.2)) * half3(0.82, 0.95, 1.12), 0.6) + half3(0.02, 0.03, 0.04);
}
uint _MaxParticles;
float4 _WaterfallGrid;   // columns, rows, the first emitter of the draw
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
    // Bubbles (a negative lifetime): each at its own spot on the surface for one cycle, swelling, then bursting into a
    // ring; age comes out negative so the fragment shader draws a bubble, not a puff.
    if (e.shape.w < 0)
    {
        float bubbleLife = -e.shape.w;
        float phase = _PunchyClock / bubbleLife + r;
        float cycle = floor(phase);
        float bubbleAge = frac(phase);
        float h1 = EffectHash(cycle * 0.618 + r * 13.1), h2 = EffectHash(cycle * 0.377 + r * 7.9);
        float2 spot = float2(cos(h1 * 6.2832), sin(h1 * 6.2832)) * e.shape.y * sqrt(h2);
        float3 at = e.position + float3(spot.x, 0.02, spot.y);
        float bubbleSize = e.shape.z * lerp(0.6, 1.4, EffectHash(cycle + r * 3.3)) * (bubbleAge < 0.6 ? 0.5 + 0.5 * bubbleAge / 0.6 : 1 + (bubbleAge - 0.6) * 2.5);
        float bubbleAlpha = e.timing.z * step(k, e.timing.w - 1) * (1 - smoothstep(600, 750, distance(at, GetCameraPositionWS())));
        // The swelling dome faces the camera; once it bursts its ripples lie flat on the water (Z-36).
        float3 bRight = UNITY_MATRIX_V[0].xyz, bUp = UNITY_MATRIX_V[1].xyz;
        if (bubbleAge >= 0.6)
        {
            bRight = float3(1, 0, 0);
            bUp = float3(0, 0, 1);
        }
        float2 bCorner = EffectCorner[vertex];
        PlumeVertex b;
        b.positionWS = at + (bRight * bCorner.x + bUp * bCorner.y) * bubbleSize * step(0.001, bubbleAlpha);
        b.uv = bCorner;
        b.alpha = bubbleAlpha;
        b.age = -max(bubbleAge, 0.001);
        return b;
    }
    float life = e.shape.w;
    float age = frac(_PunchyClock / life + r);
    float born = _PunchyClock - age * life;

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
    float2 uv;      // across 0..1, down the fall 0..1
    float fallLength;   // metres of fall, for streak scale
    float lipIn;    // 0 on the water behind the lip, 1 once over it
    uint emitter;   // which emitter the sheet is
};

/// An uneven line along a lip (-1..1): two waves across it, the fall's own.
float FallLipWave(float x, float seed) { return sin(x * 1.3 + seed * 37.0) * 0.6 + sin(x * 3.1 + seed * 91.0) * 0.4; }

/// R-21 row 4, the one fall builder: a point of a fall's sheet from its description alone (the emitter: lip, pour
/// direction, run, drop, width, lip skew and bow, cascade). u runs across it, f from behind its lip to its foot. In
/// the open the first of f is the water rolling over the lip: it starts flat on the water a little behind the lip
/// line (an oblique or bowed line, uneven along its length) and curves over; the rest (tau, 0..1) falls, thrown
/// clear on a parabola when sheer and nearer a straight slide, in bumps, when a cascade. The middle carries further
/// out than the sides, the reach and the width waver, and the edge columns peel away as strands. A cave's fall
/// leaves a narrow notch on a plain parabola and spreads as it drops.
float3 FallPoint(LandmarkEmitter e, float u, float f, out float tau, out float lipIn)
{
    float3 outDir = float3(e.outDir.x, 0, e.outDir.y);
    float3 across = normalize(cross(float3(0, 1, 0), outDir));
    float open = 1 - e.underRock;
    float side = u * 2 - 1;
    float x = (u - 0.5) * e.shape.y;
    float runIn = 0.12 * open;
    lipIn = runIn > 0 ? saturate(f / runIn) : 1;
    tau = saturate((f - runIn) / (1 - runIn));

    float lipShift = (e.fall.x * side * 0.5 - e.fall.y * side * side + FallLipWave(x, e.seed) * min(0.6, e.shape.y * 0.08)) * open;
    float run = e.shape.x * (1 + open * (0.15 * (1 - side * side) + 0.1 * FallLipWave(x * 0.7 + 11.0, e.seed + 0.37)));
    float dip = 0.15 * open;
    float down = dip * lipIn * lipIn + (e.drop - dip) * pow(tau, lerp(2.0, 1.15, e.fall.z));
    float along = lipShift - 1.5 * (1 - lipIn) * open + run * tau;
    float width = e.shape.y * lerp(1 - 0.12 * tau, 0.55 + 0.75 * tau, e.underRock) * (1 + open * 0.06 * sin(tau * 5.0 + e.seed * 20.0));

    float3 p = e.position + outDir * along - float3(0, down, 0) + across * (u - 0.5) * width;
    // A cascade breaks over the steps of its slope.
    p += outDir * e.fall.z * 0.3 * sin(tau * (e.drop + run) * 0.9 + x * 0.8 + e.seed * 17.0) * sin(3.14159 * tau);
    // The outer columns splay outwards as they fall, strands peeling off the sheet's sides.
    p += across * sign(side) * smoothstep(0.75, 1, abs(side)) * tau * tau * min(0.08 * e.drop, 1.2) * open;
    // Ripple the sheet so it isn't a perfect surface.
    p += outDir * sin(u * 9.0 + tau * 4.0 + _PunchyClock * 1.7) * 0.15 * tau;
    return p;
}

/// A fall's sheet, on the grid the draw gives (rule 20: a fine one for the falls that look big, a coarse one for
/// the rest, LandmarkEffectSystem.FallTiers).
FallVertex BuildWaterfall(uint vertex, uint instance)
{
    uint index = instance + (uint)_WaterfallGrid.z;
    LandmarkEmitter e = _LandmarkEmitters[index];
    uint columns = (uint)_WaterfallGrid.x;
    uint quad = vertex / 6;
    float2 corner = EffectCorner[vertex % 6] * 0.5 + 0.5;
    float u = ((quad % columns) + corner.x) / _WaterfallGrid.x;
    float f = ((quad / columns) + corner.y) / _WaterfallGrid.y;

    float tau, lipIn, tau2, lipIn2;
    float3 p = FallPoint(e, u, f, tau, lipIn);
    // The normal from the sheet's own shape (steps taken inwards, so the last row and column have one too).
    float du = u > 0.5 ? -0.02 : 0.02, df = f > 0.5 ? -0.02 : 0.02;
    float3 dPu = (FallPoint(e, u + du, f, tau2, lipIn2) - p) * sign(du);
    float3 dPf = (FallPoint(e, u, f + df, tau2, lipIn2) - p) * sign(df);
    float3 n = cross(dPu, dPf);

    FallVertex o;
    o.positionWS = p;
    o.normalWS = n / max(length(n), 1e-5);   // nan-safe
    o.uv = float2(u, tau);
    o.fallLength = e.drop + e.shape.x;
    o.lipIn = lipIn;
    o.emitter = index;
    return o;
}

#endif
