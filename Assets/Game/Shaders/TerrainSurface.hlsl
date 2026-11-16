#ifndef PUNCHY_TERRAIN_SURFACE_INCLUDED
#define PUNCHY_TERRAIN_SURFACE_INCLUDED

// Terrain vertices carry fully blended surface properties, evaluated on the CPU from the biome rules:
//   COLOR      rgb = albedo,     a = smoothness
//   TEXCOORD0  x = metallic,     y = grass density, z = snow depth, w = skirt drop (metres; 0 on the surface)
//   TEXCOORD1  rgb = emission (HDR), w = geomorph offset (coarse-LOD height − height)
//   TEXCOORD2  xy = distance from the terrain focus where the geomorph starts / completes, z = surface bumpiness
//   TEXCOORD3  x = rock strata amount, y = cobble paving, z = hollow depth (puddles gather where it's positive)
//   TEXCOORD4  x = porosity (how much rain soaks in: sand darkens rather than pooling), y = the coarser LOD's hollow depth,
//              z, w = the biome ground's albedo before paving was painted over it (R-28: the grout between stones; 0 where
//              nothing set it): z blue, w red * 64 + green in 5 and 6 bits (TerrainUnder unpacks it)
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
    half4  strata     : TEXCOORD3;   // x rock strata amount, y cobble paving, z hollow depth (m, below the ground round it)
    half4  soak       : TEXCOORD4;   // x porosity, y the next-coarser LOD's hollow depth (W-169), zw ground albedo under paving (R-28)
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Cave entrances: the surface is cut where the cave walls' field opens (TerrainSource.MouthCut, per vertex); the walls'
// own skin of the surface runs on under the terrain round it, so the cut edge never shows a gap. Every pass must do
// this, or the hole would cast shadows or block the depth texture.
void TerrainCaveClip(float mouthCut)
{
    clip(mouthCut);
}

// Geomorphing: vertices slide onto the next-coarser LOD's surface as they approach the distance where their
// chunk switches LOD, so the switch itself changes nothing on screen. Every pass must use this.
float TerrainMorph(TerrainAttributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    return smoothstep(input.morph.x, input.morph.y, distance(positionWS.xz, _TerrainFocus.xy));
}

float3 TerrainPositionOS(TerrainAttributes input)
{
    float3 positionOS = input.positionOS;
    positionOS.y += input.emission.w * TerrainMorph(input);
    return positionOS;
}

// R-28: the ground's albedo under the paving, unpacked from soak.zw (see the TEXCOORD4 note above).
half3 TerrainUnder(TerrainAttributes input)
{
    float w = input.soak.w + 0.5;
    return half3(floor(w / 64) / 31, floor(fmod(w, 64)) / 63, input.soak.z);
}

// W-169: the hollow depth (puddles) morphs alongside, onto the coarser LOD's (0 where that LOD carries none), so no
// puddle stops at a chunk edge where the LOD changes.
half TerrainHollow(TerrainAttributes input)
{
    return lerp(input.strata.z, input.soak.y, (half)TerrainMorph(input));
}

#endif
