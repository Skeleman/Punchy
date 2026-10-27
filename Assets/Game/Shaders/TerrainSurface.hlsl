#ifndef PUNCHY_TERRAIN_SURFACE_INCLUDED
#define PUNCHY_TERRAIN_SURFACE_INCLUDED

// Terrain vertices carry fully blended surface properties, evaluated on the CPU from the biome rules:
//   COLOR      rgb = albedo,     a = smoothness
//   TEXCOORD0  x = metallic,     y = grass density, z = snow depth, w = skirt drop (metres; 0 on the surface)
//   TEXCOORD1  rgb = emission (HDR), w = geomorph offset (coarse-LOD height − height)
//   TEXCOORD2  xy = distance from the terrain focus where the geomorph starts / completes, z = surface bumpiness
// Grass density and snow depth are read by the (future) grass and snow-trail systems, not by this shader.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half _EmissionScale;
CBUFFER_END

// Set every frame by TerrainStreamingSystem: xy = world XZ that chunk LODs are chosen around.
float4 _TerrainFocus;

struct TerrainAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  surface    : COLOR;
    half4  extra      : TEXCOORD0;
    half4  emission   : TEXCOORD1;
    float4 morph      : TEXCOORD2;   // xy geomorph range, z surface bumpiness, w distance to cave air
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Cave entrances: the surface is cut away a little inside a cave's air, so it overhangs the cave walls' top edge by a
// thin lip (hiding the seam). Every pass must do this, or the hole would cast shadows or block the depth texture.
void TerrainCaveClip(float caveDistance)
{
    clip(caveDistance + 0.25);
}

// Geomorphing: vertices slide onto the next-coarser LOD's surface as they approach the distance where their
// chunk switches LOD, so the switch itself changes nothing on screen. Every pass must use this.
float3 TerrainPositionOS(TerrainAttributes input)
{
    float3 positionOS = input.positionOS;
    float3 positionWS = TransformObjectToWorld(positionOS);
    float t = smoothstep(input.morph.x, input.morph.y, distance(positionWS.xz, _TerrainFocus.xy));
    positionOS.y += input.emission.w * t;
    return positionOS;
}

#endif
