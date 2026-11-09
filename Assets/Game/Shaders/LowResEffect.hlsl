#ifndef PUNCHY_LOW_RES_EFFECT_INCLUDED
#define PUNCHY_LOW_RES_EFFECT_INCLUDED

// The shader side of LowResEffectFeature: an expensive full-screen effect drawn into a low-resolution buffer.
//
// A new effect's shader: a full-screen pass (Cull Off, ZWrite Off, ZTest Always) with the vertex shader PunchyLowResVert and
// a fragment shader that takes SV_POSITION, includes this file after the URP core, and writes its result. Inside it:
//   PunchyLowResRay(pos)       the world direction through a low-resolution pixel
//   PunchyLowResJitter()       the frame's sub-pixel offset in xy (add to pos before PunchyLowResRay); w a 0..1 value that changes every frame
//   PunchyLowResAnySky(pos)    true when this pixel or a neighbour shows the sky: sky-only effects skip the rest
// Compositing, full resolution, in the shader that draws the result (its Effect entry names the textures):
//   PunchyLowResUpsample(result, resultDepth, resultSize, svPos, sceneDepth)
// sceneDepth is the raw depth of the pixel being drawn (0 on the sky, whether reversed-Z or not: PUNCHY_LOW_RES_SKY).

#if UNITY_REVERSED_Z
    #define PUNCHY_LOW_RES_SKY 0.0
    #define PUNCHY_LOW_RES_FURTHER(a, b) min(a, b)
#else
    #define PUNCHY_LOW_RES_SKY 1.0
    #define PUNCHY_LOW_RES_FURTHER(a, b) max(a, b)
#endif

// Set by the feature on the effect's material.
Texture2D<float> _PunchyLowResDepth;
float4 _PunchyLowResSize;      // width, height, 1/width, 1/height of the low-resolution buffer
float4 _PunchyLowResJitter;    // xy sub-pixel offset, z frame index, w per-frame value in 0..1
float4x4 _PunchyLowResInvVP;   // inverse view-projection without the camera's position

struct PunchyLowResVaryings { float4 positionCS : SV_POSITION; };

PunchyLowResVaryings PunchyLowResVert(uint id : SV_VertexID)
{
    PunchyLowResVaryings o;
    o.positionCS = GetFullScreenTriangleVertexPosition(id);
    return o;
}

float4 PunchyLowResJitter() { return _PunchyLowResJitter; }

// The world-space direction through a point in the low-resolution buffer (SV_POSITION coordinates).
float3 PunchyLowResRay(float2 pos)
{
    float2 uv = pos * _PunchyLowResSize.zw;
    float4 ray = mul(_PunchyLowResInvVP, float4(uv.x * 2 - 1, (1 - uv.y) * 2 - 1, 0.5, 1));
    return normalize(ray.xyz / ray.w);
}

// True when this low-resolution pixel or one next to it holds sky (the buffer's upsample reads the 2x2 round a pixel).
bool PunchyLowResAnySky(float2 pos)
{
    int2 p = int2(pos);
    int2 last = int2(_PunchyLowResSize.xy) - 1;
    bool sky = false;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            sky = sky || abs(_PunchyLowResDepth.Load(int3(clamp(p + int2(x, y), 0, last), 0)) - PUNCHY_LOW_RES_SKY) < 1e-6;
    return sky;
}

// Reads a low-resolution result at a full-resolution pixel: the four nearest texels, weighted by the usual bilinear weights
// and by how close each texel's depth is to the pixel's, so an edge never pulls in the other side's values (halos).
half4 PunchyLowResUpsample(Texture2D<half4> result, Texture2D<float> resultDepth, float4 resultSize, float2 svPos, float sceneDepth)
{
    float2 texel = svPos / _ScaledScreenParams.xy * resultSize.xy - 0.5;
    int2 p0 = int2(floor(texel));
    float2 f = texel - p0;
    int2 last = int2(resultSize.xy) - 1;
    const int2 offsets[4] = { int2(0, 0), int2(1, 0), int2(0, 1), int2(1, 1) };
    float4 bilinear = float4((1 - f.x) * (1 - f.y), f.x * (1 - f.y), (1 - f.x) * f.y, f.x * f.y);
    half4 sum = 0;
    float total = 0;
    for (int i = 0; i < 4; i++)
    {
        int2 p = clamp(p0 + offsets[i], 0, last);
        float closeness = 1 / (1e-4 + abs(resultDepth.Load(int3(p, 0)) - sceneDepth) * 1000);
        float w = bilinear[i] * closeness + 1e-5;
        sum += result.Load(int3(p, 0)) * w;
        total += w;
    }
    return sum / total;
}

#endif
