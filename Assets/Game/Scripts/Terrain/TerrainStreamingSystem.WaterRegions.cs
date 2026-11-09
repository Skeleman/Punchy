using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-117 row 2: far water draws as one mesh per 4x4-chunk region. Every chunk with water keeps its own water entity
    /// and mesh (near water, rebuilds, caves); a region whose nearest edge is more than <see cref="RegionNear"/> from the
    /// focus hides its members' entities and draws one combined copy instead. That turned ~490 sorted transparent draws
    /// at the v3 fishing village into ~100. Water.shader works from world position only, so the copy looks the same.
    /// </summary>
    public partial class TerrainStreamingSystem
    {
        /// <summary>Region side in chunks.</summary>
        const int RegionChunks = 4;
        /// <summary>Regions nearer than this (m, to their nearest edge) draw chunk by chunk.</summary>
        const float RegionNear = 256f;
        /// <summary>Margin either side of RegionNear, so a region on the line doesn't flip every step.</summary>
        const float RegionHysteresis = 16f;
        /// <summary>Off draws every water chunk on its own again (for before/after; PUNCHY_WATERMERGE=0).</summary>
        public static bool MergeFarWater = System.Environment.GetEnvironmentVariable("PUNCHY_WATERMERGE") != "0";

        struct WaterRegion
        {
            public Entity Entity;   // Entity.Null until the region first merges
            public Mesh Mesh;
            public BatchMeshID MeshId;
            public bool Merged;
            public bool Dirty;      // a member's water changed since the combined mesh was built
        }

        readonly Dictionary<int2, WaterRegion> regions = new Dictionary<int2, WaterRegion>();
        readonly List<int2> scratchRegions = new List<int2>();
        readonly List<Mesh> regionMembers = new List<Mesh>();
        readonly List<float3> regionOffsets = new List<float3>();
        Entity regionPrototype;
        float2 lastRegionFocus = new float2(float.MaxValue);
        bool regionsDirty, appliedMerge = true;

        static int2 RegionOf(int2 coord) => (int2)math.floor((float2)coord / RegionChunks);

        bool RegionMerged(int2 coord) => regions.TryGetValue(RegionOf(coord), out var r) && r.Merged;

        /// <summary>Flags the region holding this chunk for a rebuild (its water appeared, changed or went).</summary>
        void MarkWaterRegion(int2 coord)
        {
            var key = RegionOf(coord);
            regions.TryGetValue(key, out var r);
            r.Dirty = true;
            regions[key] = r;
            regionsDirty = true;
        }

        /// <summary>
        /// Merges regions that moved out past RegionNear, splits those that came back in, and rebuilds merged regions
        /// whose members changed. Runs only when something changed or the focus moved a few metres.
        /// </summary>
        void RefreshWaterRegions(float2 focus, float chunkSize)
        {
            if (appliedMerge != MergeFarWater)
            {
                appliedMerge = MergeFarWater;
                regionsDirty = true;
            }
            if (!regionsDirty && math.distancesq(focus, lastRegionFocus) < 64f)
                return;
            regionsDirty = false;
            lastRegionFocus = focus;

            var span = chunkSize * RegionChunks;
            var toHide = new NativeList<Entity>(Allocator.Temp);
            var toShow = new NativeList<Entity>(Allocator.Temp);
            scratchRegions.Clear();
            scratchRegions.AddRange(regions.Keys);
            foreach (var key in scratchRegions)
            {
                var r = regions[key];
                var min = (float2)key * span;
                var distance = math.distance(focus, math.clamp(focus, min, min + span));
                var merge = MergeFarWater && distance > RegionNear + (r.Merged ? -RegionHysteresis : RegionHysteresis);
                if (merge == r.Merged && !r.Dirty)
                    continue;

                var members = 0;
                if (merge)
                {
                    members = BuildRegionMesh(key, ref r, chunkSize);
                    if (members == 0)
                    {
                        // No water left in the region.
                        DropRegion(key, r);
                        continue;
                    }
                    if (r.Entity == Entity.Null)
                    {
                        var made = Spawn(ref regionPrototype, WaterDesc, waterMaterialId, r.MeshId, false, 1);
                        var origin = min;
                        r.Entity = Place(made[0], origin, waterMaterialId, r.MeshId, RegionBounds(r.Mesh), "WaterRegion");
                        made.Dispose();
                    }
                    else
                        EntityManager.SetComponentData(r.Entity, new RenderBounds { Value = RegionBounds(r.Mesh) });
                }

                // Members hide while merged; the region entity shows only then.
                var any = false;
                for (int dz = 0; dz < RegionChunks; dz++)
                for (int dx = 0; dx < RegionChunks; dx++)
                {
                    if (!loaded.TryGetValue(key * RegionChunks + new int2(dx, dz), out var chunk) || chunk.WaterEntity == Entity.Null)
                        continue;
                    any = true;
                    WantVisible(chunk.WaterEntity, !merge, toHide, toShow);
                }
                if (r.Entity != Entity.Null)
                    WantVisible(r.Entity, merge, toHide, toShow);
                r.Merged = merge;
                r.Dirty = false;
                if (!merge && !any)
                {
                    DropRegion(key, r);
                    continue;
                }
                regions[key] = r;
            }
            EntityManager.AddComponent<DisableRendering>(toHide.AsArray());
            EntityManager.RemoveComponent<DisableRendering>(toShow.AsArray());
            toHide.Dispose();
            toShow.Dispose();
        }

        void WantVisible(Entity e, bool visible, NativeList<Entity> toHide, NativeList<Entity> toShow)
        {
            var off = EntityManager.HasComponent<DisableRendering>(e);
            if (visible && off)
                toShow.Add(e);
            else if (!visible && !off)
                toHide.Add(e);
        }

        /// <summary>Copies the region's member water meshes into its own mesh, offset to the region origin. Returns the
        /// number of members.</summary>
        int BuildRegionMesh(int2 key, ref WaterRegion r, float chunkSize)
        {
            regionMembers.Clear();
            regionOffsets.Clear();
            var regionOrigin = (float2)(key * RegionChunks) * chunkSize;
            for (int dz = 0; dz < RegionChunks; dz++)
            for (int dx = 0; dx < RegionChunks; dx++)
            {
                var coord = key * RegionChunks + new int2(dx, dz);
                if (!loaded.TryGetValue(coord, out var chunk) || chunk.WaterEntity == Entity.Null)
                    continue;
                regionMembers.Add(chunk.WaterMesh);
                var o = (float2)coord * chunkSize - regionOrigin;
                regionOffsets.Add(new float3(o.x, 0f, o.y));
            }
            if (regionMembers.Count == 0)
                return 0;

            using var sources = Mesh.AcquireReadOnlyMeshData(regionMembers);
            var vertexCount = 0;
            var indexCount = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                vertexCount += sources[i].vertexCount;
                indexCount += sources[i].GetSubMesh(0).indexCount;
            }

            var target = Mesh.AllocateWritableMeshData(1);
            var data = target[0];
            var layout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.Temp);
            data.SetVertexBufferParams(vertexCount, layout);
            layout.Dispose();
            data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            var verts = data.GetVertexData<WaterVertex>();
            var indices = data.GetIndexData<uint>();
            var lo = new float3(float.MaxValue);
            var hi = new float3(float.MinValue);
            int v0 = 0, i0 = 0;
            for (int m = 0; m < sources.Length; m++)
            {
                var src = sources[m];
                var srcVerts = src.GetVertexData<WaterVertex>();
                var sub = src.GetSubMesh(0);
                var srcIndices = src.GetIndexData<uint>();
                var offset = regionOffsets[m];
                for (int v = 0; v < srcVerts.Length; v++)
                {
                    var vert = srcVerts[v];
                    vert.Position += offset;
                    verts[v0 + v] = vert;
                }
                for (int i = 0; i < sub.indexCount; i++)
                {
                    var p = verts[v0 + (int)srcIndices[sub.indexStart + i]].Position;
                    lo = math.min(lo, p);
                    hi = math.max(hi, p);
                    indices[i0 + i] = (uint)v0 + srcIndices[sub.indexStart + i];
                }
                v0 += srcVerts.Length;
                i0 += sub.indexCount;
            }
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshFlags);

            if (r.Mesh == null)
                (r.Mesh, r.MeshId) = RentMesh();
            Mesh.ApplyAndDisposeWritableMeshData(target, r.Mesh, MeshFlags);
            if (indexCount == 0)
                lo = hi = float3.zero;
            // (Grown by the swell's reach on every side: the shader moves the sea's vertices, refactor 3.4.)
            r.Mesh.bounds = new Bounds((lo + hi) * 0.5f, math.max(hi - lo, 1f) + 2f * WaterSwells.BoundsPad);
            return regionMembers.Count;
        }

        static AABB RegionBounds(Mesh mesh) => new AABB { Center = mesh.bounds.center, Extents = mesh.bounds.extents };

        /// <summary>Forgets a region: its entity goes and its mesh returns to the pool. Members are shown again.</summary>
        void DropRegion(int2 key, WaterRegion r)
        {
            if (r.Entity != Entity.Null)
                EntityManager.DestroyEntity(r.Entity);
            if (r.Mesh != null)
                meshPool.Push((r.Mesh, r.MeshId));
            for (int dz = 0; dz < RegionChunks; dz++)
            for (int dx = 0; dx < RegionChunks; dx++)
                if (loaded.TryGetValue(key * RegionChunks + new int2(dx, dz), out var chunk) && chunk.WaterEntity != Entity.Null
                    && EntityManager.HasComponent<DisableRendering>(chunk.WaterEntity) && !surfaceHidden)
                    EntityManager.RemoveComponent<DisableRendering>(chunk.WaterEntity);
            regions.Remove(key);
        }

        /// <summary>Destroys every region (unload all, shutdown). With <paramref name="destroyMeshes"/> their meshes go
        /// too; otherwise they return to the pool.</summary>
        void ClearWaterRegions(bool destroyMeshes)
        {
            foreach (var r in regions.Values)
            {
                if (r.Entity != Entity.Null && EntityManager.Exists(r.Entity))
                    EntityManager.DestroyEntity(r.Entity);
                if (r.Mesh == null)
                    continue;
                if (destroyMeshes)
                    Object.Destroy(r.Mesh);
                else
                    meshPool.Push((r.Mesh, r.MeshId));
            }
            regions.Clear();
            lastRegionFocus = new float2(float.MaxValue);
        }
    }
}
