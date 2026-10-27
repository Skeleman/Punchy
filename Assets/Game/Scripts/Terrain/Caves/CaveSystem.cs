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
    /// That field is sampled on a grid and turned into a mesh by surface nets (a vertex in each grid cell the boundary
    /// passes through, joined across every grid edge it crosses), in background Burst jobs. The grid is cut into fixed
    /// cubes (chunks), built only where a cave reaches and streamed by distance, so a big cave network costs no more
    /// to load than the part of it near the camera. Each chunk samples a one-voxel apron round itself and only joins
    /// the edges that start inside it, so neighbouring chunks share their boundary vertices exactly.
    /// Only the boundary close to the cave is kept: the cave walls, which end 10 cm under the terrain surface
    /// (measured across the slope). The terrain cuts its own opening just inside the cave's edge, overhanging the
    /// walls' top edge, so there's no seam. Near the surface the walls take the terrain's colour and blend into cave
    /// rock as they go underground.
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
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float4> Data;
            public NativeList<int> Indices;
            public bool Pending;
            /// <summary>Whether its walls cast sun shadows: only chunks reaching up near the surface can shade anything the sun lights.</summary>
            public bool CastsShadows;

            public void Dispose()
            {
                Job.Complete();
                if (Field.IsCreated) Field.Dispose();
                if (CaveField.IsCreated) CaveField.Dispose();
                if (Surface.IsCreated) Surface.Dispose();
                if (SurfaceScale.IsCreated) SurfaceScale.Dispose();
                if (Data.IsCreated) Data.Dispose();
                if (Positions.IsCreated) Positions.Dispose();
                if (Normals.IsCreated) Normals.Dispose();
                if (Colors.IsCreated) Colors.Dispose();
                if (Indices.IsCreated) Indices.Dispose();
                if (Mesh != null) Destroy(Mesh);
            }
        }

        [Tooltip("Cave chunks started per frame at most (nearest first), so a cave coming into range doesn't hitch.")]
        [Min(1)] public int BuildsPerFrame = 2;
        [Tooltip("Cave chunks building at once at most. Each holds a few MB of fields while it builds, and too many " +
                 "crowd the terrain's own jobs off the workers.")]
        [Min(1)] public int MaxBuilding = 4;

        /// <summary>Cave walls are built in cubes this many metres across (rounded to whole voxels).</summary>
        const float ChunkMetres = 32f;

        readonly Dictionary<int3, Built> chunks = new Dictionary<int3, Built>();
        /// <summary>Every chunk any cave reaches, by column (x, z), with the chunk heights (y) in that column.</summary>
        readonly Dictionary<int2, List<int>> columns = new Dictionary<int2, List<int>>();
        readonly List<int3> wanted = new List<int3>();
        readonly List<int3> scratch = new List<int3>();
        int chunkCells;
        float chunkSize;
        int3 lastCameraChunk = new int3(int.MinValue);
        World queryWorld;
        EntityQuery terrainQuery, playerQuery;
        TerrainSource lastSource;
        Light lantern;
        float lanternLevel;

        // Other players carry lanterns too (lit from where their bodies are; nothing extra is sent).
        class OtherLantern { public Light Light; public float Level; public bool Seen; }
        readonly Dictionary<Entity, OtherLantern> otherLanterns = new Dictionary<Entity, OtherLantern>();
        readonly List<Entity> goneLanterns = new List<Entity>();
        EntityQuery othersQuery;

        static readonly int UndergroundId = Shader.PropertyToID("_PunchyUnderground");
        static readonly int StylesId = Shader.PropertyToID("_PunchyCaveStyles");
        readonly Vector4[] styleTable = new Vector4[CaveStyleTable.MaxStyles * CaveStyleTable.Vectors];
        EntityQuery paletteQuery;
        BiomePalette stylesFrom;
        List<CaveStyle> styles;

        void OnEnable() => Punchy.Net.GameBootstrap.WorldsReplacing += ReleaseWorld;

        void OnDisable()
        {
            Punchy.Net.GameBootstrap.WorldsReplacing -= ReleaseWorld;
            Release();
        }

        /// <summary>
        /// Hosting, joining or leaving disposes the game worlds, and with them the TerrainSource blobs that chunk jobs
        /// still in flight are reading. This runs just before, while the old world is intact: every job is finished
        /// and every chunk dropped, and the world is looked up afresh next frame.
        /// </summary>
        void ReleaseWorld()
        {
            Release();
            lastSource = default;
            queryWorld = null;
        }

        void Release()
        {
            foreach (var c in chunks.Values)
                c.Dispose();
            chunks.Clear();
            columns.Clear();
            lastCameraChunk = new int3(int.MinValue);
            Underground = 0f;
            Shader.SetGlobalFloat(UndergroundId, 0f);
            if (lantern != null)
                lantern.enabled = false;
            foreach (var l in otherLanterns.Values)
                if (l.Light != null) Destroy(l.Light.gameObject);
            otherLanterns.Clear();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || Material == null || !TryGetWorld(out var source))
                return;
            if (!source.Equals(lastSource))
            {
                Release();
                lastSource = source;
            }
            if (!source.Caves.IsCreated)
                return;

            var camPos = (float3)cam.transform.position;
            UploadStyles();
            Stream(source, camPos);
            Draw();

            // Underground: the camera in a cave's air, a couple of metres below the surface.
            var deep = source.CaveDistance(camPos) < 0f ? math.saturate((source.SampleHeight(camPos.xz) - camPos.y - 1f) / 3f) : 0f;
            Underground = Mathf.MoveTowards(Underground, deep, Time.deltaTime * 1.5f);
            Shader.SetGlobalFloat(UndergroundId, Underground);
            UpdateLantern(source);
            UpdateOtherLanterns(source, cam.transform.position);
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
                othersQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.NetPlayer>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                queryWorld = world;
            }
            return terrainQuery.TryGetSingleton(out source);
        }

        /// <summary>
        /// Uploads the cave styles' looks in the order baking numbered them (slot 0 stays empty: the material's own
        /// colours). Every frame, so editing a style's colours or finish in play mode shows at once; its shape is
        /// baked and needs a re-bake.
        /// </summary>
        void UploadStyles()
        {
            if (paletteQuery.IsEmpty)
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            if (palette != stylesFrom)
            {
                styles = CaveStyleTable.Collect(palette.Landmarks);
                stylesFrom = palette;
            }
            for (int i = 0; i < styles.Count; i++)
                styles[i].Pack(styleTable, i + 1);
            Shader.SetGlobalVectorArray(StylesId, styleTable);
        }

        // ------------------------------------------------------------------------------------------- building

        /// <summary>
        /// Lists every chunk a cave segment's box touches, by column. Done once per baked world: chunks follow the
        /// segments, so the list is as long as the caves are, not as big as the world.
        /// </summary>
        void IndexChunks(in TerrainSource source)
        {
            chunkCells = math.max(4, (int)math.round(ChunkMetres / Voxel));
            chunkSize = chunkCells * Voxel;
            ref var set = ref source.Caves.Value;
            var seen = new HashSet<int3>();
            for (int i = 0; i < set.Segments.Length; i++)
            {
                ref var seg = ref set.Segments[i];
                var c0 = (int3)math.floor(seg.Min / chunkSize);
                var c1 = (int3)math.floor(seg.Max / chunkSize);
                for (int z = c0.z; z <= c1.z; z++)
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    var key = new int3(x, y, z);
                    if (!seen.Add(key))
                        continue;
                    if (!columns.TryGetValue(key.xz, out var ys))
                        columns[key.xz] = ys = new List<int>();
                    ys.Add(y);
                }
            }
        }

        float ChunkDistance(int3 key, float3 camPos)
        {
            var min = (float3)key * chunkSize;
            return math.distance(math.clamp(camPos, min, min + chunkSize), camPos);
        }

        void Stream(in TerrainSource source, float3 camPos)
        {
            if (columns.Count == 0)
                IndexChunks(source);

            // Which chunks are in range only changes when the camera crosses into another chunk.
            var cameraChunk = (int3)math.floor(camPos / chunkSize);
            if (!cameraChunk.Equals(lastCameraChunk))
            {
                lastCameraChunk = cameraChunk;
                wanted.Clear();
                var reach = (int)math.ceil(LoadRadius / chunkSize);
                for (int z = -reach; z <= reach; z++)
                for (int x = -reach; x <= reach; x++)
                {
                    var column = cameraChunk.xz + new int2(x, z);
                    if (!columns.TryGetValue(column, out var ys))
                        continue;
                    foreach (var y in ys)
                    {
                        var key = new int3(column.x, y, column.y);
                        if (!chunks.ContainsKey(key) && ChunkDistance(key, camPos) < LoadRadius)
                            wanted.Add(key);
                    }
                }
                // Nearest last, so they come off the end of the list first.
                var cam = camPos;
                wanted.Sort((a, b) => ChunkDistance(b, cam).CompareTo(ChunkDistance(a, cam)));

                scratch.Clear();
                foreach (var kv in chunks)
                    if (ChunkDistance(kv.Key, camPos) > LoadRadius * 1.2f)
                        scratch.Add(kv.Key);
                foreach (var key in scratch)
                {
                    chunks[key].Dispose();
                    chunks.Remove(key);
                }
            }

            var building = 0;
            foreach (var b in chunks.Values)
                if (b.Pending)
                    building++;
            for (int n = 0; n < BuildsPerFrame && building < MaxBuilding && wanted.Count > 0; n++, building++)
            {
                var key = wanted[wanted.Count - 1];
                wanted.RemoveAt(wanted.Count - 1);
                if (!chunks.ContainsKey(key))
                    chunks[key] = StartBuild(source, key);
            }

            foreach (var kv in chunks)
            {
                var b = kv.Value;
                if (!b.Pending || !b.Job.IsCompleted)
                    continue;
                b.Job.Complete();
                b.Pending = false;
                // Chunks the cave only grazes come out empty: keep the record (so they aren't rebuilt), not a mesh.
                if (b.Indices.Length > 0)
                {
                    var mesh = new Mesh { name = "Cave chunk", indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(b.Positions.AsArray());
                    mesh.SetNormals(b.Normals.AsArray());
                    mesh.SetColors(b.Colors.AsArray());
                    mesh.SetUVs(0, b.Data.AsArray());
                    mesh.SetIndices(b.Indices.AsArray(), MeshTopology.Triangles, 0);
                    var min = (float3)kv.Key * chunkSize;
                    mesh.bounds = new Bounds(min + chunkSize * 0.5f, new float3(chunkSize + Voxel * 4f));
                    b.Mesh = mesh;
                    // A chunk whose top is a few metres under the lowest ground above it can't shade anything the sun
                    // reaches, so it stays out of the shadow cascades (the column heights are already sampled).
                    var lowestGround = float.MaxValue;
                    for (int i = 0; i < b.Surface.Length; i++)
                        lowestGround = math.min(lowestGround, b.Surface[i]);
                    b.CastsShadows = min.y + chunkSize + Voxel > lowestGround - 4f;
                }
                b.Field.Dispose();
                b.CaveField.Dispose();
                b.Surface.Dispose();
                b.SurfaceScale.Dispose();
                b.Positions.Dispose();
                b.Normals.Dispose();
                b.Colors.Dispose();
                b.Data.Dispose();
                b.Indices.Dispose();
            }
        }

        Built StartBuild(in TerrainSource source, int3 key)
        {
            // Points run from one voxel before the chunk to one after its far side; cells from -1 to chunkCells. The
            // origin is a global grid index, so neighbouring chunks sample their shared apron at bit-identical points
            // (a float origin plus an offset rounds differently in each, and a value near zero can flip sign).
            var origin = key * chunkCells - 1;
            var cells = new int3(chunkCells + 2);
            var points = cells + 1;
            var b = new Built
            {
                Field = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                CaveField = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Surface = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                SurfaceScale = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Data = new NativeList<float4>(Allocator.Persistent),
                Positions = new NativeList<float3>(Allocator.Persistent),
                Normals = new NativeList<float3>(Allocator.Persistent),
                Colors = new NativeList<float4>(Allocator.Persistent),
                Indices = new NativeList<int>(Allocator.Persistent),
                Pending = true
            };
            var columnJob = new ColumnJob { Source = source, Origin = origin, Points = points, Voxel = Voxel, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.x * points.z, 64);
            var field = new FieldJob { Source = source, Origin = origin, Points = points, Voxel = Voxel, Field = b.Field, CaveField = b.CaveField, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.y * points.z, 4, columnJob);
            b.Job = new MeshJob
            {
                Source = source, Origin = origin, Cells = cells, Voxel = Voxel,
                Field = b.Field, CaveField = b.CaveField,
                Positions = b.Positions, Normals = b.Normals, Colors = b.Colors, Data = b.Data, Indices = b.Indices
            }.Schedule(field);
            JobHandle.ScheduleBatchedJobs();
            return b;
        }

        /// <summary>
        /// Per grid column: the surface height, and how to turn height above it into distance across the slope.
        /// </summary>
        [BurstCompile]
        struct ColumnJob : IJobParallelFor
        {
            public TerrainSource Source;
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
            public int3 Points;
            public float Voxel;
            [WriteOnly] public NativeArray<float> Surface, SurfaceScale;

            public void Execute(int i)
            {
                var xz = (float2)(Origin.xz + new int2(i % Points.x, i / Points.x)) * Voxel;
                var h = Source.SampleHeight(xz);
                const float e = 0.5f;
                var slope = new float2(Source.SampleHeight(xz + new float2(e, 0f)) - Source.SampleHeight(xz - new float2(e, 0f)),
                                       Source.SampleHeight(xz + new float2(0f, e)) - Source.SampleHeight(xz - new float2(0f, e))) / (2f * e);
                Surface[i] = h;
                SurfaceScale[i] = math.rsqrt(1f + math.lengthsq(slope));
            }
        }

        /// <summary>
        /// Samples, at every grid point, the rock's signed distance (negative in rock: 10 cm under the surface,
        /// measured across the slope, and outside every cave) and the nearest cave's distance. One job item per grid row.
        /// </summary>
        [BurstCompile]
        struct FieldJob : IJobParallelFor
        {
            public TerrainSource Source;
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
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
                    var p = (float3)(Origin + new int3(x, y, z)) * Voxel;
                    var cave = CaveSampling.Distance(ref Source.Caves.Value, p, out _);
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
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
            /// <summary>Cells including the apron: the chunk's own cells are 1 to Cells - 2.</summary>
            public int3 Cells;
            public float Voxel;
            [ReadOnly] public NativeArray<float> Field, CaveField;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float4> Data;
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
                        nearCave |= CaveField[i] < 1.2f;
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
                    var p = ((float3)(Origin + new int3(x, y, z)) + sum / crossings) * Voxel;
                    cellVertex[Cell(x, y, z)] = Positions.Length;
                    Positions.Add(p);
                    Normals.Add(Gradient(p));
                    Colors.Add(Look(p, out var data));
                    Data.Add(data);
                }

                // A quad across every grid edge the boundary crosses, joining the four cells round it. Only edges that
                // start at the chunk's own points (1 to Cells - 2; 0 and the last are the apron): the neighbour joins
                // the rest, using the same apron vertices, so the seam closes with nothing drawn twice.
                var own = Cells - 2;
                for (int z = 1; z <= own.z; z++)
                for (int y = 1; y <= own.y; y++)
                for (int x = 1; x <= own.x; x++)
                {
                    var a = Field[Point(x, y, z)] < 0f;
                    if (a != Field[Point(x + 1, y, z)] < 0f)
                        Quad(cellVertex, Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), a);
                    if (a != Field[Point(x, y + 1, z)] < 0f)
                        Quad(cellVertex, Cell(x - 1, y, z - 1), Cell(x - 1, y, z), Cell(x, y, z), Cell(x, y, z - 1), a);
                    if (a != Field[Point(x, y, z + 1)] < 0f)
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

            float RockAt(float3 p) => math.max(p.y - Source.SampleHeight(p.xz) + 0.1f, -CaveSampling.Distance(ref Source.Caves.Value, p, out _));

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
            /// underground), y a slight brightness tint for the cave rock, z the cave's style slot.
            /// </summary>
            float4 Look(float3 p, out float4 data)
            {
                var surface = Source.SampleHeight(p.xz);
                // Daylight from the mouth of whichever cave this wall belongs to.
                ref var set = ref Source.Caves.Value;
                CaveSampling.Distance(ref set, p, out var cave);
                var fromMouth = cave >= 0 ? math.saturate(1f - (math.distance(p, set.Caves[cave].Mouth) - 3f) / 14f) : 0f;
                var nearSurface = math.saturate(1f - (surface - p.y - 1f) / 3f);
                var ground = Source.SampleGround(p.xz);
                var albedo = Source.SampleSurface(p.xz, ground, Source.SampleNormal(p.xz, 1f)).Albedo;
                data = new float4(math.saturate(1f - (surface - p.y - 0.3f) / 2.5f), 0.85f + 0.3f * (noise.snoise(p * 0.15f) * 0.5f + 0.5f),
                                  cave >= 0 ? set.Caves[cave].Style : 0, 0f);
                return new float4(albedo, math.max(fromMouth, nearSurface));
            }
        }

        void Draw()
        {
            foreach (var b in chunks.Values)
            {
                if (b.Mesh == null)
                    continue;
                var rp = new RenderParams(Material)
                {
                    worldBounds = b.Mesh.bounds,
                    shadowCastingMode = b.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
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
            lanternLevel = Mathf.MoveTowards(lanternLevel, LanternWanted(source, player.Position), Time.deltaTime * 1.2f);
            if (lantern == null)
                lantern = CreateLantern("Lantern");
            PlaceLantern(lantern, player, lanternLevel, 0.5f);
        }

        /// <summary>How lit someone's lantern should be: in a cave and away from the daylight at the entrance.</summary>
        static float LanternWanted(in TerrainSource source, float3 feet)
        {
            source.FloorUnder(feet, out var inCave);
            var depth = source.SampleHeight(feet.xz) - feet.y;
            return inCave ? math.saturate((depth - 1f) / 3f) : 0f;
        }

        Light CreateLantern(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            return light;
        }

        void PlaceLantern(Light light, in Unity.Transforms.LocalTransform holder, float level, float flickerSeed)
        {
            light.enabled = level > 0.01f;
            light.color = LanternColor;
            light.range = LanternRange;
            var flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, flickerSeed);
            light.intensity = LanternIntensity * level * flicker;
            var forward = math.mul(holder.Rotation, new float3(0f, 0f, 1f));
            light.transform.position = holder.Position + new float3(0f, 1.6f, 0f) + forward * 0.4f;
        }

        void UpdateOtherLanterns(in TerrainSource source, Vector3 camera)
        {
            if (othersQuery.IsEmpty && otherLanterns.Count == 0)
                return;
            foreach (var l in otherLanterns.Values)
                l.Seen = false;
            var em = queryWorld.EntityManager;
            var entities = othersQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                if (em.HasComponent<Unity.NetCode.GhostOwnerIsLocal>(e) && em.IsComponentEnabled<Unity.NetCode.GhostOwnerIsLocal>(e))
                    continue;
                var t = em.GetComponentData<Unity.Transforms.LocalTransform>(e);
                if (math.lengthsq(t.Position) < 1e-6f || math.distance(t.Position, camera) > 150f)
                    continue;
                if (!otherLanterns.TryGetValue(e, out var l))
                    otherLanterns[e] = l = new OtherLantern { Light = CreateLantern("Lantern (other player)") };
                l.Seen = true;
                l.Level = Mathf.MoveTowards(l.Level, LanternWanted(source, t.Position), Time.deltaTime * 1.2f);
                PlaceLantern(l.Light, t, l.Level, 1.5f + (e.Index % 7));
            }
            entities.Dispose();
            goneLanterns.Clear();
            foreach (var pair in otherLanterns)
                if (!pair.Value.Seen)
                    goneLanterns.Add(pair.Key);
            foreach (var e in goneLanterns)
            {
                if (otherLanterns[e].Light != null) Destroy(otherLanterns[e].Light.gameObject);
                otherLanterns.Remove(e);
            }
        }
    }
}
