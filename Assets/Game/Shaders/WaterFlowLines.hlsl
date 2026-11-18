#ifndef PUNCHY_WATER_FLOW_LINES_INCLUDED
#define PUNCHY_WATER_FLOW_LINES_INCLUDED

#include "SkyCommon.hlsl"

float PunchyFlowFast(float speed)
{
    return saturate((speed - 0.8) / 3.5);
}

// (Spacing, length and the angle are fixed per sample: q reaches 90 m from its anchor, so letting them vary per pixel
// bends the lines into kinks at every triangle edge.)
static const float PunchyFlowLineSpacing = 1.1;
static const float PunchyFlowLineAngles = 16;

// xy slope, z height (m), w crest 0..1.
float4 PunchyFlowLine(float2 q, float2 along, float fast, float seed)
{
    float2 across = float2(-along.y, along.x);
    float a = dot(q, along);
    float c = dot(q, across);
    float k = 6.2832 / PunchyFlowLineSpacing;
    float warp = (PunchyValueNoise(float2(a * 0.12, c * 0.3) + seed) - 0.5) * lerp(2.0, 4.5, fast);
    float phi = c * k + warp;
    float s = 0.5 + 0.5 * sin(phi);
    float sharp = lerp(1.5, 5.0, fast);
    float env = smoothstep(lerp(0.45, 0.25, fast), 0.8, PunchyValueNoise(float2(a / 5.0, c / PunchyFlowLineSpacing * 0.5) + seed * 1.7));
    float crest = pow(max(s, 1e-4), sharp) * env;
    float rise = sharp * pow(max(s, 1e-4), sharp - 1) * 0.5 * cos(phi) * k * env;
    float amp = lerp(0.014, 0.02, fast);
    return float4(across * rise * amp, crest * amp, crest);
}

// (Two copies half a cycle apart cross-fade, like FlowFoam, so the lines never stretch out however long the water runs.)
float4 PunchyFlowLinesAt(float2 p, float2 along, float speed, float fast, float t, float seed)
{
    const float cycle = 2.0;
    float jitter = PunchyValueNoise(p * 0.04 + 1.3) * 2;
    float phase0 = frac(t / cycle + jitter);
    float phase1 = frac(t / cycle + jitter + 0.5);
    float w0 = 1 - abs(1 - 2 * phase0);
    float2 offset = along * speed * cycle;
    float4 l0 = PunchyFlowLine(p - offset * phase0, along, fast, seed);
    float4 l1 = PunchyFlowLine(p - offset * phase1, along, fast, seed + 5.3);
    return (l0 * w0 + l1 * (1 - w0)) * rsqrt(w0 * w0 + (1 - w0) * (1 - w0));
}

float4 PunchyFlowLines(float2 p, float2 along, float speed, float t)
{
    float fast = PunchyFlowFast(speed);
    float turn = atan2(along.y, along.x) * (PunchyFlowLineAngles / 6.2832);
    float i0 = floor(turn);
    float f = turn - i0;
    float a0 = i0 * (6.2832 / PunchyFlowLineAngles);
    float a1 = a0 + 6.2832 / PunchyFlowLineAngles;
    float4 l0 = PunchyFlowLinesAt(p, float2(cos(a0), sin(a0)), speed, fast, t, fmod(i0 + PunchyFlowLineAngles, PunchyFlowLineAngles) * 0.37);
    float4 l1 = PunchyFlowLinesAt(p, float2(cos(a1), sin(a1)), speed, fast, t, fmod(i0 + 1 + PunchyFlowLineAngles, PunchyFlowLineAngles) * 0.37);
    return (l0 * (1 - f) + l1 * f) * rsqrt((1 - f) * (1 - f) + f * f);
}

// (The same anchor as Water.shader's frag, so the lift and the lit lines line up.)
float2 PunchyFlowAnchor(int corner, out float weight)
{
    const float AnchorGrid = 64;
    float2 eye = _WorldSpaceCameraPos.xz;
    float2 cellA = floor(eye / AnchorGrid) * AnchorGrid;
    float2 cellB = floor(eye / AnchorGrid + 0.5) * AnchorGrid - AnchorGrid * 0.5;
    float2 inA = eye - cellA;
    float2 weightA = smoothstep(3, 10, min(inA, AnchorGrid - inA));
    bool bx = (corner & 1) != 0;
    bool bz = (corner & 2) != 0;
    weight = (bx ? 1 - weightA.x : weightA.x) * (bz ? 1 - weightA.y : weightA.y);
    return float2(bx ? cellB.x : cellA.x, bz ? cellB.y : cellA.y);
}

float PunchyFlowRiverSpeed(float speed, float depth, float rapids)
{
    return speed * (1 + 2.5 * rapids) * lerp(0.45, 1.25, saturate(depth / 1.6));
}

// (Only within about 10 m at the reference view, gone by 20: further out the sheet is too coarse for the lines, rule 20.)
float PunchyFlowLift(float3 restWS, float4 data, float flowSpeed, float t)
{
    float flowAmount = saturate(length(data.xy));
    float near = smoothstep(47, 90, PunchyWaterPixelsPerMetre(distance(restWS, GetCameraPositionWS())));
    float weight = flowAmount * near * PunchyWaterFieldWeight(restWS.xz) * (1 - smoothstep(0.5, 1, data.w));
    float lift = 0;
    [branch] if (weight > 0)
    {
        float2 along = data.xy / max(flowAmount, 1e-3);
        float speed = PunchyFlowRiverSpeed(flowSpeed, data.z, data.w);
        [unroll] for (int corner = 0; corner < 4; corner++)
        {
            float w;
            float2 origin = PunchyFlowAnchor(corner, w);
            [branch] if (w > 0)
                lift += PunchyFlowLines(restWS.xz - origin, along, speed, t).z * w;
        }
    }
    return lift * weight;
}

// (A T-edge vertex takes its edge's ends' lift, as in PunchyWaterPatchVertex, so the sheet never cracks.)
float PunchyFlowPatchLift(float3 restWS, float4 data, float4 parent, float flowSpeed, float t)
{
    [branch] if (abs(parent.w) > 1e-4)
    {
        float3 along = parent.w > 0 ? float3(0, 0, 1) : float3(1, 0, 0);
        float3 a = restWS - along * parent.z;
        float3 b = a + along * abs(parent.w);
        return lerp(PunchyFlowLift(a, data, flowSpeed, t), PunchyFlowLift(b, data, flowSpeed, t), parent.z / abs(parent.w));
    }
    return PunchyFlowLift(restWS, data, flowSpeed, t);
}

#endif
