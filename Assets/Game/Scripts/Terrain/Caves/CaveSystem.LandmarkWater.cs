using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-148 row 3: landmark pools (oases, hot springs, mud pits, toxic pools, crater lava) are water fitted to their
    /// basins, built the way baked cave water is, instead of a disc. A pool's lattice (the same 0.5 m world lattice) is
    /// sampled round it in a background Burst job: a vertex is wet where the ground (TerrainSource, so the basin LandmarkData
    /// carves) lies under the pool's level. Each lattice cell with a wet corner becomes water at the level, and its dry
    /// corners sit at the level inside the bank, so the GPU clips the sheet along the shore as it does on the surface.
    /// The sheet ends where the pool's reach does (past the bank's crest, LandmarkWaterReach), and between two pools of one
    /// landmark (stepped pools) along the line where their scaled distances match, cut by marching squares so both edges
    /// are curves. The terrain's own water grid still leaves landmark pools out (BuildChunkMeshJob).
    ///
    /// A pool is baked when it first gets big enough on screen (one at a time, collected on a later frame), drawn while it
    /// stays so (DetailPolicy.ScreenSize, rule 20), and let go when it is far smaller.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>
        /// How far a landmark pool's water reaches, in pool radii: past where its bowl rises out of the water (about 1.17
        /// radii) and short of where its raised lip gives way to the ground round it (about 1.45 radii), so the sheet's
        /// edge always ends under the bank.
        /// </summary>
        const float LandmarkWaterReach = 1.3f;

        /// <summary>One landmark pool's water: where it is, its look, and its mesh once baked.</summary>
        class LandmarkPool
        {
            public float3 Centre;
            public float Radius;
            public int Instance;
            public float4 Look;
            public float2 Glow;
            public Mesh Mesh;
            public Bounds Bounds;
            public bool Shown;
            public int Triangles;
        }

        readonly List<LandmarkPool> landmarkPools = new List<LandmarkPool>();
        BlobAssetReference<LandmarkSetBlob> landmarkPoolsFrom;
        JobHandle landmarkWaterJob;
        int landmarkWaterBaking = -1;
        int2 landmarkWaterLo;
        int landmarkWaterSide;
        /// <summary>Per lattice vertex of the pool being baked: x ground height, y claim (m, positive inside its reach).</summary>
        NativeArray<float2> landmarkWaterSamples;
        NativeArray<float4> landmarkWaterOthers;

        /// <summary>Triangles and pools of landmark water drawn last frame.</summary>
        public static int LandmarkWaterTrianglesDrawn, LandmarkWatersDrawn;

        /// <summary>
        /// Draws every landmark pool big enough on screen, collects a finished bake and starts the next, and lets go of
        /// pools far too small to see.
        /// </summary>
        void DrawLandmarkPools(in TerrainSource source, float3 camPos)
        {
            LandmarkWaterTrianglesDrawn = 0;
            LandmarkWatersDrawn = 0;
            if (!source.Landmarks.IsCreated || configQuery.IsEmpty)
                return;
            var water = LandmarkWater(configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value);
            if (water == null)
                return;
            if (landmarkPoolsFrom != source.Landmarks)
            {
                ReleaseLandmarkPools();
                landmarkPoolsFrom = source.Landmarks;
                ref var set = ref source.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var type = ref set.Types[set.Instances[i].Type];
                    for (int p = 0; p < type.PoolCount; p++)
                    {
                        LandmarkSampling.Pool(ref set, i, p, out var centre, out var radius);
                        var look = source.WaterLook(centre.xz, source.SampleGround(centre.xz).Biomes, out var glow);
                        landmarkPools.Add(new LandmarkPool { Centre = centre, Radius = radius, Instance = i, Look = look, Glow = glow });
                    }
                }
            }
            if (landmarkWaterBaking >= 0 && landmarkWaterJob.IsCompleted)
            {
                landmarkWaterJob.Complete();
                BuildLandmarkWater(landmarkPools[landmarkWaterBaking]);
                DisposeLandmarkWaterBake();
            }
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var pixels = Punchy.Settings.DetailPolicy.CaveWaterPixels / math.max(view.Detail, 0.1f);
            int next = -1;
            var nextDistance = float.MaxValue;
            for (int i = 0; i < landmarkPools.Count; i++)
            {
                var pool = landmarkPools[i];
                var size = pool.Radius * LandmarkWaterReach * 2f;
                var d = math.max(math.distance(pool.Centre, camPos) - pool.Radius * LandmarkWaterReach, 0f);
                var screen = Punchy.Settings.DetailPolicy.ScreenSize(size, d, view.PixelsPerMetre);
                pool.Shown = Punchy.Settings.DetailPolicy.Holds(screen, pixels, pool.Shown);
                if (pool.Mesh == null)
                {
                    if (pool.Shown && landmarkWaterBaking < 0 && d < nextDistance)
                    {
                        next = i;
                        nextDistance = d;
                    }
                    continue;
                }
                // Let go only well past where it stops drawing, so a pool at the edge isn't rebaked back and forth.
                if (screen < pixels * 0.5f)
                {
                    Destroy(pool.Mesh);
                    pool.Mesh = null;
                    continue;
                }
                if (!pool.Shown || pool.Triangles == 0)
                    continue;
                var rp = new RenderParams(water) { worldBounds = pool.Bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                Graphics.RenderMesh(rp, pool.Mesh, 0, Matrix4x4.identity);
                LandmarkWaterTrianglesDrawn += pool.Triangles;
                LandmarkWatersDrawn++;
            }
            if (next >= 0)
                StartLandmarkWaterBake(source, next);
        }

        Material landmarkWater;

        /// <summary>
        /// A copy of the terrain's water material for landmark pools (W-148 row 5): open-air water like the chunk water's,
        /// but its lattice takes the wave field in its normals (_LatticeField), so a geyser's rings and a wader's wake show.
        /// Kept in step with the original's settings.
        /// </summary>
        Material LandmarkWater(Material water)
        {
            if (water == null)
                return null;
            if (landmarkWater == null || landmarkWater.shader != water.shader)
            {
                if (landmarkWater != null)
                    Destroy(landmarkWater);
                landmarkWater = new Material(water) { name = "Landmark pool water" };
            }
            else
                landmarkWater.CopyPropertiesFromMaterial(water);
            landmarkWater.SetFloat(LatticeFieldId, 1f);
            return landmarkWater;
        }

        /// <summary>Starts sampling the lattice round landmark pool i.</summary>
        void StartLandmarkWaterBake(in TerrainSource source, int i)
        {
            var pool = landmarkPools[i];
            var reach = pool.Radius * LandmarkWaterReach + WaterStep;
            landmarkWaterLo = (int2)math.floor((pool.Centre.xz - reach) / WaterStep);
            var hi = (int2)math.ceil((pool.Centre.xz + reach) / WaterStep);
            landmarkWaterSide = math.cmax(hi - landmarkWaterLo) + 1;
            // The landmark's other pools, which this one's water gives way to.
            var others = new List<float4>();
            foreach (var other in landmarkPools)
                if (other != pool && other.Instance == pool.Instance)
                    others.Add(new float4(other.Centre, other.Radius));
            landmarkWaterOthers = new NativeArray<float4>(others.Count, Allocator.Persistent);
            for (int k = 0; k < others.Count; k++)
                landmarkWaterOthers[k] = others[k];
            landmarkWaterSamples = new NativeArray<float2>(landmarkWaterSide * landmarkWaterSide, Allocator.Persistent);
            landmarkWaterJob = new BakeLandmarkWaterJob
            {
                Source = source,
                Centre = pool.Centre,
                Radius = pool.Radius,
                Lo = landmarkWaterLo,
                Side = landmarkWaterSide,
                Others = landmarkWaterOthers,
                Samples = landmarkWaterSamples
            }.Schedule(landmarkWaterSamples.Length, 64);
            landmarkWaterBaking = i;
        }

        /// <summary>
        /// Per lattice vertex round one landmark pool: the ground's height there, and how far inside the pool's water the
        /// vertex lies (its reach, cut where a neighbouring pool of the landmark lies nearer, distances scaled by radius).
        /// </summary>
        [BurstCompile]
        struct BakeLandmarkWaterJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float3 Centre;
            public float Radius;
            public int2 Lo;
            public int Side;
            [ReadOnly] public NativeArray<float4> Others;
            [WriteOnly] public NativeArray<float2> Samples;

            public void Execute(int index)
            {
                var xz = (float2)(Lo + new int2(index % Side, index / Side)) * WaterStep;
                var d = math.distance(xz, Centre.xz);
                var claim = Radius * LandmarkWaterReach - d;
                for (int o = 0; o < Others.Length; o++)
                {
                    var other = Others[o];
                    var dOther = math.distance(xz, other.xz);
                    if (dOther < other.w * LandmarkWaterReach)
                        claim = math.min(claim, (dOther / other.w - d / Radius) * math.min(other.w, Radius));
                }
                Samples[index] = new float2(Source.SampleHeight(xz), claim);
            }
        }

        /// <summary>
        /// Turns a finished bake into the pool's mesh: each lattice cell with a wet corner inside the claim becomes water
        /// at the pool's level, cut along the claim's edge by marching squares (CaveSystem.Water's rule).
        /// </summary>
        void BuildLandmarkWater(LandmarkPool pool)
        {
            var side = landmarkWaterSide;
            var level = pool.Centre.y;
            var vertices = new List<WaterVertex>();
            var indices = new List<int>();
            var shared = new Dictionary<int, int>();
            var corners = new int[4];
            var polygon = new List<int>(5);
            for (int z = 0; z + 1 < side; z++)
                for (int x = 0; x + 1 < side; x++)
                {
                    corners[0] = z * side + x;
                    corners[1] = corners[0] + 1;
                    corners[2] = corners[0] + side + 1;
                    corners[3] = corners[0] + side;
                    bool wet = false;
                    for (int j = 0; j < 4; j++)
                    {
                        var s = landmarkWaterSamples[corners[j]];
                        wet |= s.y > 0f && s.x < level - 0.02f;
                    }
                    if (!wet)
                        continue;
                    polygon.Clear();
                    for (int j = 0; j < 4; j++)
                    {
                        int c = corners[j], n = corners[(j + 1) & 3];
                        float cc = landmarkWaterSamples[c].y, cn = landmarkWaterSamples[n].y;
                        if (cc > 0f)
                        {
                            if (!shared.TryGetValue(c, out var v))
                            {
                                v = vertices.Count;
                                vertices.Add(Vertex(c, Position(c)));
                                shared[c] = v;
                            }
                            polygon.Add(v);
                        }
                        if (cc > 0f != cn > 0f)
                        {
                            int inside = cc > 0f ? c : n, outside = cc > 0f ? n : c;
                            float ci = landmarkWaterSamples[inside].y, co = landmarkWaterSamples[outside].y;
                            polygon.Add(vertices.Count);
                            vertices.Add(Vertex(inside, math.lerp(Position(inside), Position(outside), ci / (ci - co))));
                        }
                    }
                    for (int j = 1; j + 1 < polygon.Count; j++)
                    {
                        indices.Add(polygon[0]);
                        indices.Add(polygon[j + 1]);
                        indices.Add(polygon[j]);
                    }
                }
            var mesh = new Mesh { name = "Landmark pool" };
            mesh.SetVertexBufferParams(vertices.Count, WaterVertex.Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            pool.Bounds = mesh.bounds;
            // Drawn from the GPU copy only.
            mesh.UploadMeshData(true);
            pool.Mesh = mesh;
            pool.Triangles = indices.Count / 3;

            float2 Position(int i) => (float2)(landmarkWaterLo + new int2(i % side, i / side)) * WaterStep;

            // Lattice vertex i's water at xz: flat at the level, as deep as the ground under it lies below.
            WaterVertex Vertex(int i, float2 xz) => new WaterVertex
            {
                Position = new float3(xz.x, level, xz.y),
                Data = new float4(0f, 0f, math.max(level - landmarkWaterSamples[i].x, 0f), 0f),
                Look = pool.Look,
                Glow = pool.Glow
            };
        }

        void DisposeLandmarkWaterBake()
        {
            landmarkWaterBaking = -1;
            if (landmarkWaterSamples.IsCreated) landmarkWaterSamples.Dispose();
            if (landmarkWaterOthers.IsCreated) landmarkWaterOthers.Dispose();
        }

        /// <summary>Drops every landmark pool's water (and any bake in flight).</summary>
        void ReleaseLandmarkPools()
        {
            landmarkWaterJob.Complete();
            DisposeLandmarkWaterBake();
            foreach (var pool in landmarkPools)
                if (pool.Mesh != null) Destroy(pool.Mesh);
            landmarkPools.Clear();
            landmarkPoolsFrom = default;
        }
    }
}
