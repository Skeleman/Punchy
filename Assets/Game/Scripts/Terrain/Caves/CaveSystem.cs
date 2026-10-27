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
    /// Builds and draws the inside of caves near the camera, and looks after being underground.
    ///
    /// A cave's walls are the boundary of the rock: everywhere below the terrain surface and outside the cave's air.
    /// That field is sampled on a grid round the cave and turned into a mesh by surface nets (a vertex in each grid
    /// cell the boundary passes through, joined across every grid edge it crosses), in background Burst jobs.
    /// Only the boundary close to the cave is kept, so the mesh is the cave walls plus a strip of ground a few metres
    /// round the entrance, 10 cm under the terrain (measured across the slope, so steep faces don't flicker),
    /// filling the hole the terrain mesh leaves there. That strip takes the terrain's own colour and blends into cave
    /// rock as it goes underground.
    ///
    /// Underground (camera in a cave, well below the surface) fades from 0 to 1: weather stops, and a lantern on the
    /// player lights the way.
    /// </summary>
    public class CaveSystem : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Caves within this distance of the camera are built and drawn.")]
        public float LoadRadius = 400f;
        [Tooltip("Grid spacing for building cave walls, in metres.")]
        [Range(0.25f, 1.5f)] public float Voxel = 0.5f;

        [Header("Lantern")]
        public Color LanternColor = new Color(1f, 0.78f, 0.5f);
        public float LanternIntensity = 6f;
        public float LanternRange = 15f;

        /// <summary>0 above ground, 1 deep in a cave (from the camera). Weather and ambience read it.</summary>
        public static float Underground { get; private set; }

        class Built
        {
            public Mesh Mesh;
            public JobHandle Job;
            public NativeArray<float> Field, CaveField, Surface, SurfaceScale;
            public NativeArray<bool> NearColumn;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float2> Data;
            public NativeList<int> Indices;
            public bool Pending;

            public void Dispose()
            {
                Job.Complete();
                if (Field.IsCreated) Field.Dispose();
                if (CaveField.IsCreated) CaveField.Dispose();
                if (Surface.IsCreated) Surface.Dispose();
                if (SurfaceScale.IsCreated) SurfaceScale.Dispose();
                if (NearColumn.IsCreated) NearColumn.Dispose();
                if (Data.IsCreated) Data.Dispose();
                if (Positions.IsCreated) Positions.Dispose();
                if (Normals.IsCreated) Normals.Dispose();
                if (Colors.IsCreated) Colors.Dispose();
                if (Indices.IsCreated) Indices.Dispose();
                if (Mesh != null) Destroy(Mesh);
            }
        }

        readonly Dictionary<int, Built> caves = new Dictionary<int, Built>();
        readonly List<int> scratch = new List<int>();
        World queryWorld;
        EntityQuery terrainQuery, playerQuery;
        TerrainSource lastSource;
        Light lantern;
        float lanternLevel;

        static readonly int UndergroundId = Shader.PropertyToID("_PunchyUnderground");

        void OnDisable()
        {
            foreach (var c in caves.Values)
                c.Dispose();
            caves.Clear();
            Underground = 0f;
            Shader.SetGlobalFloat(UndergroundId, 0f);
            if (lantern != null)
                lantern.enabled = false;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || Material == null || !TryGetWorld(out var source))
                return;
            if (!source.Equals(lastSource))
            {
                OnDisable();
                lastSource = source;
            }
            if (!source.Caves.IsCreated)
                return;

            var camPos = (float3)cam.transform.position;
            Stream(source, camPos);
            Draw();

            // Underground: the camera in a cave's air, a couple of metres below the surface.
            var deep = source.CaveDistance(camPos) < 0f ? math.saturate((source.SampleHeight(camPos.xz) - camPos.y - 1f) / 3f) : 0f;
            Underground = Mathf.MoveTowards(Underground, deep, Time.deltaTime * 1.5f);
            Shader.SetGlobalFloat(UndergroundId, Underground);
            UpdateLantern(source);
        }

        bool TryGetWorld(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerTag>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                queryWorld = world;
            }
            return terrainQuery.TryGetSingleton(out source);
        }

        // ------------------------------------------------------------------------------------------- building

        void Stream(in TerrainSource source, float3 camPos)
        {
            ref var set = ref source.Caves.Value;
            for (int i = 0; i < set.Caves.Length; i++)
            {
                ref var c = ref set.Caves[i];
                var distance = math.distance(math.clamp(camPos, c.Min, c.Max), camPos);
                var has = caves.TryGetValue(i, out var built);
                if (!has && distance < LoadRadius)
                    caves[i] = StartBuild(source, i, ref c);
                else if (has && distance > LoadRadius * 1.2f)
                {
                    built.Dispose();
                    caves.Remove(i);
                }
            }

            foreach (var kv in caves)
            {
                var b = kv.Value;
                if (!b.Pending || !b.Job.IsCompleted)
                    continue;
                b.Job.Complete();
                b.Pending = false;
                var mesh = new Mesh { name = $"Cave {kv.Key}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(b.Positions.AsArray());
                mesh.SetNormals(b.Normals.AsArray());
                mesh.SetColors(b.Colors.AsArray());
                mesh.SetUVs(0, b.Data.AsArray());
                mesh.SetIndices(b.Indices.AsArray(), MeshTopology.Triangles, 0);
                ref var c = ref set.Caves[kv.Key];
                mesh.bounds = new Bounds((c.Min + c.Max) * 0.5f, c.Max - c.Min + 4f);
                b.Mesh = mesh;
                b.Field.Dispose();
                b.CaveField.Dispose();
                b.Surface.Dispose();
                b.SurfaceScale.Dispose();
                b.NearColumn.Dispose();
                b.Positions.Dispose();
                b.Normals.Dispose();
                b.Colors.Dispose();
                b.Data.Dispose();
                b.Indices.Dispose();
            }
        }

        Built StartBuild(in TerrainSource source, int index, ref CaveData c)
        {
            var origin = c.Min - Voxel * 2f;
            var cells = (int3)math.ceil((c.Max - c.Min + Voxel * 4f) / Voxel);
            var points = cells + 1;
            var b = new Built
            {
                Field = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                CaveField = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Surface = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                SurfaceScale = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                NearColumn = new NativeArray<bool>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Data = new NativeList<float2>(Allocator.Persistent),
                Positions = new NativeList<float3>(Allocator.Persistent),
                Normals = new NativeList<float3>(Allocator.Persistent),
                Colors = new NativeList<float4>(Allocator.Persistent),
                Indices = new NativeList<int>(Allocator.Persistent),
                Pending = true
            };
            var columns = new ColumnJob { Source = source, Cave = index, Origin = origin, Points = points, Voxel = Voxel, Surface = b.Surface, SurfaceScale = b.SurfaceScale, NearColumn = b.NearColumn }
                .Schedule(points.x * points.z, 64);
            var field = new FieldJob { Source = source, Cave = index, Origin = origin, Points = points, Voxel = Voxel, Field = b.Field, CaveField = b.CaveField, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.y * points.z, 4, columns);
            b.Job = new MeshJob
            {
                Source = source, Cave = index, Origin = origin, Cells = cells, Voxel = Voxel, Mouth = c.Mouth,
                Field = b.Field, CaveField = b.CaveField, NearColumn = b.NearColumn,
                Positions = b.Positions, Normals = b.Normals, Colors = b.Colors, Data = b.Data, Indices = b.Indices
            }.Schedule(field);
            JobHandle.ScheduleBatchedJobs();
            return b;
        }

        /// <summary>
        /// Per grid column: the surface height, how to turn height above it into distance across the slope, and
        /// whether the column is close enough to the entrance for the cave mesh to cover the ground there (wider
        /// than the terrain's hole, which can be a whole terrain quad bigger than the cut-away surface itself).
        /// </summary>
        [BurstCompile]
        struct ColumnJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Cave;
            public float3 Origin;
            public int3 Points;
            public float Voxel;
            [WriteOnly] public NativeArray<float> Surface, SurfaceScale;
            [WriteOnly] public NativeArray<bool> NearColumn;

            public void Execute(int i)
            {
                var xz = Origin.xz + new float2(i % Points.x, i / Points.x) * Voxel;
                var h = Source.SampleHeight(xz);
                const float e = 0.5f;
                var slope = new float2(Source.SampleHeight(xz + new float2(e, 0f)) - Source.SampleHeight(xz - new float2(e, 0f)),
                                       Source.SampleHeight(xz + new float2(0f, e)) - Source.SampleHeight(xz - new float2(0f, e))) / (2f * e);
                Surface[i] = h;
                SurfaceScale[i] = math.rsqrt(1f + math.lengthsq(slope));
                NearColumn[i] = CaveSampling.Distance(ref Source.Caves.Value, Cave, new float3(xz.x, h + 0.05f, xz.y)) < 3f;
            }
        }

        /// <summary>
        /// Samples, at every grid point, the rock's signed distance (negative in rock: 10 cm under the surface,
        /// measured across the slope, and outside the cave) and the cave's own distance. One job item per grid row.
        /// </summary>
        [BurstCompile]
        struct FieldJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Cave;
            public float3 Origin;
            public int3 Points;
            public float Voxel;
            [NativeDisableParallelForRestriction] public NativeArray<float> Field, CaveField;
            [ReadOnly] public NativeArray<float> Surface, SurfaceScale;

            public void Execute(int row)
            {
                var y = row % Points.y;
                var z = row / Points.y;
                for (int x = 0; x < Points.x; x++)
                {
                    var p = Origin + new float3(x, y, z) * Voxel;
                    var cave = CaveSampling.Distance(ref Source.Caves.Value, Cave, p);
                    var i = (z * Points.y + y) * Points.x + x;
                    CaveField[i] = cave;
                    var column = z * Points.x + x;
                    Field[i] = math.max((p.y - Surface[column]) * SurfaceScale[column] + 0.1f, -cave);
                }
            }
        }

        [BurstCompile]
        struct MeshJob : IJob
        {
            public TerrainSource Source;
            public int Cave;
            public float3 Origin;
            public int3 Cells;
            public float Voxel;
            public float3 Mouth;
            [ReadOnly] public NativeArray<float> Field, CaveField;
            [ReadOnly] public NativeArray<bool> NearColumn;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float2> Data;
            public NativeList<int> Indices;

            int Point(int x, int y, int z) => (z * (Cells.y + 1) + y) * (Cells.x + 1) + x;
            int Cell(int x, int y, int z) => (z * Cells.y + y) * Cells.x + x;

            public void Execute()
            {
                var cellVertex = new NativeArray<int>(Cells.x * Cells.y * Cells.z, Allocator.Temp);
                for (int i = 0; i < cellVertex.Length; i++)
                    cellVertex[i] = -1;

                // A vertex in every cell the boundary crosses (near the cave): the average of the crossing points
                // on the cell's edges.
                for (int z = 0; z < Cells.z; z++)
                for (int y = 0; y < Cells.y; y++)
                for (int x = 0; x < Cells.x; x++)
                {
                    int rock = 0;
                    var nearCave = false;
                    for (int c = 0; c < 8; c++)
                    {
                        var i = Point(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1));
                        if (Field[i] < 0f) rock++;
                        nearCave |= CaveField[i] < 1.2f || NearColumn[(z + ((c >> 2) & 1)) * (Cells.x + 1) + x + (c & 1)];
                    }
                    if (rock == 0 || rock == 8 || !nearCave)
                        continue;

                    var sum = float3.zero;
                    var crossings = 0;
                    for (int c = 0; c < 8; c++)
                    for (int bit = 1; bit <= 4; bit <<= 1)
                    {
                        if ((c & bit) != 0)
                            continue;
                        var d = c | bit;
                        var ca = new int3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                        var cb = new int3(d & 1, (d >> 1) & 1, (d >> 2) & 1);
                        var va = Field[Point(x + ca.x, y + ca.y, z + ca.z)];
                        var vb = Field[Point(x + cb.x, y + cb.y, z + cb.z)];
                        if (va < 0f == vb < 0f)
                            continue;
                        sum += math.lerp((float3)ca, (float3)cb, va / (va - vb));
                        crossings++;
                    }
                    var p = Origin + (new float3(x, y, z) + sum / crossings) * Voxel;
                    cellVertex[Cell(x, y, z)] = Positions.Length;
                    Positions.Add(p);
                    Normals.Add(Gradient(p));
                    Colors.Add(Look(p, out var data));
                    Data.Add(data);
                }

                // A quad across every grid edge the boundary crosses, joining the four cells round it.
                for (int z = 0; z <= Cells.z; z++)
                for (int y = 0; y <= Cells.y; y++)
                for (int x = 0; x <= Cells.x; x++)
                {
                    var a = Field[Point(x, y, z)] < 0f;
                    if (x < Cells.x && y >= 1 && y < Cells.y && z >= 1 && z < Cells.z && a != Field[Point(x + 1, y, z)] < 0f)
                        Quad(cellVertex, Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), a);
                    if (y < Cells.y && x >= 1 && x < Cells.x && z >= 1 && z < Cells.z && a != Field[Point(x, y + 1, z)] < 0f)
                        Quad(cellVertex, Cell(x - 1, y, z - 1), Cell(x - 1, y, z), Cell(x, y, z), Cell(x, y, z - 1), a);
                    if (z < Cells.z && x >= 1 && x < Cells.x && y >= 1 && y < Cells.y && a != Field[Point(x, y, z + 1)] < 0f)
                        Quad(cellVertex, Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z), a);
                }
                cellVertex.Dispose();
            }

            void Quad(NativeArray<int> cellVertex, int c0, int c1, int c2, int c3, bool flip)
            {
                int v0 = cellVertex[c0], v1 = cellVertex[c1], v2 = cellVertex[c2], v3 = cellVertex[c3];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                    return;
                if (flip)
                {
                    Indices.Add(v0); Indices.Add(v2); Indices.Add(v1);
                    Indices.Add(v0); Indices.Add(v3); Indices.Add(v2);
                }
                else
                {
                    Indices.Add(v0); Indices.Add(v1); Indices.Add(v2);
                    Indices.Add(v0); Indices.Add(v2); Indices.Add(v3);
                }
            }

            float RockAt(float3 p) => math.max(p.y - Source.SampleHeight(p.xz) + 0.1f, -CaveSampling.Distance(ref Source.Caves.Value, Cave, p));

            /// <summary>Points out of the rock, into the air.</summary>
            float3 Gradient(float3 p)
            {
                const float e = 0.15f;
                return math.normalizesafe(new float3(
                    RockAt(p + new float3(e, 0f, 0f)) - RockAt(p - new float3(e, 0f, 0f)),
                    RockAt(p + new float3(0f, e, 0f)) - RockAt(p - new float3(0f, e, 0f)),
                    RockAt(p + new float3(0f, 0f, e)) - RockAt(p - new float3(0f, 0f, e))), new float3(0f, 1f, 0f));
            }

            /// <summary>
            /// rgb the terrain's colour at this spot; alpha how much daylight reaches (the entrance and near the
            /// surface). Data: x how much to look like the terrain (1 at the surface, fading out a few metres
            /// underground), y a slight brightness tint for the cave rock.
            /// </summary>
            float4 Look(float3 p, out float2 data)
            {
                var surface = Source.SampleHeight(p.xz);
                var fromMouth = math.saturate(1f - (math.distance(p, Mouth) - 3f) / 14f);
                var nearSurface = math.saturate(1f - (surface - p.y - 1f) / 3f);
                var ground = Source.SampleGround(p.xz);
                var albedo = Source.SampleSurface(p.xz, ground, Source.SampleNormal(p.xz, 1f)).Albedo;
                data = new float2(math.saturate(1f - (surface - p.y - 0.3f) / 2.5f), 0.85f + 0.3f * (noise.snoise(p * 0.15f) * 0.5f + 0.5f));
                return new float4(albedo, math.max(fromMouth, nearSurface));
            }
        }

        void Draw()
        {
            foreach (var b in caves.Values)
            {
                if (b.Mesh == null)
                    continue;
                var rp = new RenderParams(Material)
                {
                    worldBounds = b.Mesh.bounds,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, b.Mesh, 0, Matrix4x4.identity);
            }
        }

        // ------------------------------------------------------------------------------------------- lantern

        void UpdateLantern(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingleton<Unity.Transforms.LocalTransform>(out var player))
                return;
            // Lit while the player is in a cave and away from the daylight at the entrance.
            source.FloorUnder(player.Position, out var inCave);
            var depth = source.SampleHeight(player.Position.xz) - player.Position.y;
            var wanted = inCave ? math.saturate((depth - 1f) / 3f) : 0f;
            lanternLevel = Mathf.MoveTowards(lanternLevel, wanted, Time.deltaTime * 1.2f);
            if (lantern == null)
            {
                var go = new GameObject("Lantern");
                go.transform.SetParent(transform, false);
                lantern = go.AddComponent<Light>();
                lantern.type = LightType.Point;
                lantern.shadows = LightShadows.None;
            }
            lantern.enabled = lanternLevel > 0.01f;
            lantern.color = LanternColor;
            lantern.range = LanternRange;
            var flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, 0.5f);
            lantern.intensity = LanternIntensity * lanternLevel * flicker;
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
            lantern.transform.position = player.Position + new float3(0f, 1.6f, 0f) + forward * 0.4f;
        }
    }
}
