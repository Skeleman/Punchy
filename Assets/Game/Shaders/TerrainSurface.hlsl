#ifndef PUNCHY_TERRAIN_SURFACE_INCLUDED
#define PUNCHY_TERRAIN_SURFACE_INCLUDED

// Terrain vertices carry fully blended surface properties, evaluated on the CPU from the biome rules:
//   COLOR      rgb = albedo,     a = smoothness
//   TEXCOORD0  x = metallic,     y = grass density, z = snow depth, w = skirt drop (metres; 0 on the surface)
//   TEXCOORD1  rgb = emission (HDR), w = unused
// Grass density and snow depth are read by the (future) grass and snow-trail systems, not by this shader.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half _EmissionScale;
CBUFFER_END

struct TerrainAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  surface    : COLOR;
    half4  extra      : TEXCOORD0;
    half4  emission   : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#endif
