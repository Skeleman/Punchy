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

        public bool Equals(TerrainConfig o) =>
            Material == o.Material && ChunkSize == o.ChunkSize && Lod0Quads == o.Lod0Quads &&
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
