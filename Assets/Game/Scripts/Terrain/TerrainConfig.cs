using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>Singleton describing how the terrain is split into chunks and how detail falls off with distance.</summary>
    public struct TerrainConfig : IComponentData, IEquatable<TerrainConfig>
    {
        public UnityObjectRef<Material> Material;
        public UnityObjectRef<Material> WaterMaterial;
        public float ChunkSize;
        /// <summary>Quads per chunk edge at LOD 0. Each further LOD halves it.</summary>
        public int Lod0Quads;
        public int LodLevels;
        /// <summary>Chunks closer than this use LOD 0; each LOD after covers double the previous distance.</summary>
        public float Lod0Distance;
        public int ViewRadiusChunks;
        public int MaxBuildsPerFrame;
        public float SkirtDepth;

        public int LodForDistance(float distance)
        {
            if (distance <= Lod0Distance)
                return 0;
            return math.min(LodLevels - 1, (int)math.floor(math.log2(distance / Lod0Distance)) + 1);
        }

        public int QuadsForLod(int lod) => math.max(1, Lod0Quads >> lod);

        /// <summary>
        /// Distances from the focus over which an LOD's vertices geomorph towards the next-coarser LOD, finishing
        /// exactly where chunks switch to the coarser LOD. Normally the last 40% of the LOD's distance band, but
        /// never within a chunk diagonal of the band's start: a chunk's edge shared with a finer neighbour can lie
        /// that far out, and it must stay unmorphed there so the neighbour (fully morphed) meets it without a crack.
        /// </summary>
        public float2 MorphRange(int lod)
        {
            if (lod >= LodLevels - 1)
                return new float2(1e9f, 2e9f);
            var bandEnd = Lod0Distance * (1 << lod);
            var bandStart = lod == 0 ? 0f : Lod0Distance * (1 << (lod - 1));
            var start = math.max(bandEnd - (bandEnd - bandStart) * 0.4f, bandStart + ChunkSize * 1.415f);
            return new float2(math.min(start, bandEnd - 1f), bandEnd);
        }

        public bool Equals(TerrainConfig o) =>
            Material == o.Material && WaterMaterial == o.WaterMaterial && ChunkSize == o.ChunkSize && Lod0Quads == o.Lod0Quads &&
            LodLevels == o.LodLevels && Lod0Distance == o.Lod0Distance && ViewRadiusChunks == o.ViewRadiusChunks &&
            MaxBuildsPerFrame == o.MaxBuildsPerFrame && SkirtDepth == o.SkirtDepth;
    }

    /// <summary>Tag + bookkeeping on each streamed terrain chunk entity.</summary>
    public struct TerrainChunk : IComponentData
    {
        public int2 Coord;
        public int Lod;
    }
}
