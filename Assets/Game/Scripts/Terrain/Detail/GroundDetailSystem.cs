using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Scatters small 3D ground detail near the camera: grass blades where surfaces have Grass Density, and small
    /// stones where they have Pebbles. Detail is generated per 16 m tile by Burst jobs that sample the terrain on the
    /// same 1 m lattice (and triangle split) as the terrain's full-detail mesh, so it sits exactly on the ground. Each
    /// tile is drawn with one procedural instanced draw; far tiles draw a thinned subset (instances are stored in
    /// random order, so drawing a prefix thins evenly) and blades shrink away smoothly before the cut-off.
    /// </summary>
    public class GroundDetailSystem : MonoBehaviour
    {
        public Material GrassMaterial;
        public Material PebbleMaterial;
        public float TileSize = 16f;
        public float GrassRadius = 60f;
        public float PebbleRadius = 45f;
        [Tooltip("Blades per square metre on full-density grass, up close.")]
        public float GrassPerSquareMetre = 28f;
        [Tooltip("Multiplies grass density (for comparing densities; -grassdensity <x> on the command line overrides it).")]
        [Min(0.1f)] public float GrassDensityScale = 1f;
        [Tooltip("Within this distance every blade is kept; beyond it grass thins (fewer, wider blades) to the radius.")]
        [Min(1f)] public float FullDensityRadius = 20f;

        // At the graphics settings' Detail distance (GraphicsSettings: the fields above are the ranges at Detail 1).
        float GrassRange => Punchy.Settings.GraphicsSettings.DetailRange(GrassRadius);
        float PebbleRange => Punchy.Settings.GraphicsSettings.DetailRange(PebbleRadius);
        float FullDensityRange => Punchy.Settings.GraphicsSettings.DetailRange(FullDensityRadius);
        [Tooltip("How much the ground under grass takes the blades' colour near the camera, so gaps between blades don't read as bare soil.")]
        [Range(0f, 1f)] public float GroundTint = 1f;
        [Tooltip("Stones per square metre on full-density pebbly ground.")]
        public float PebblesPerSquareMetre = 3f;
        public int MaxTileBuildsPerFrame = 8;

        const int GrassVertices = 15;    // 5 triangles: two tapered quads and a tip
        const int PebbleVertices = 24;   // 8 triangles: a squashed, jittered octahedron

        [StructLayout(LayoutKind.Sequential)]
        public struct Blade
        {
            public float3 Position;
            public float Facing;
            public float Height;
            public float Width;
            public float Bend;
            public float Random;
            public float4 Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Pebble
        {
            public float3 Position;
            public float Size;
            public float Random;
            public float Tilt;
            public float2 Pad;
            public float4 Color;
        }

        class Tile
        {
            public GraphicsBuffer Grass, Pebbles;
            public int GrassCount, PebbleCount;
            public Bounds Bounds;
            public MaterialPropertyBlock GrassProps, PebbleProps;

            public void Dispose()
            {
                Grass?.Dispose();
                Pebbles?.Dispose();
            }
        }

        readonly Dictionary<int2, Tile> tiles = new Dictionary<int2, Tile>();
        readonly List<int2> scratch = new List<int2>();
        readonly Plane[] frustum = new Plane[6];
        TerrainSource lastSource;
        bool hasSource;
        EntityQuery terrainQuery, playerQuery;
        World queryWorld;

        static readonly int BladesId = Shader.PropertyToID("_Blades");
        static readonly int PebblesId = Shader.PropertyToID("_Pebbles");
        static readonly int CountId = Shader.PropertyToID("_InstanceCount");
        static readonly int RadiusId = Shader.PropertyToID("_DetailRadius");
        static readonly int FullId = Shader.PropertyToID("_DetailFull");
        static readonly int GrassTintId = Shader.PropertyToID("_PunchyGrassTint");
        static readonly int PlayerId = Shader.PropertyToID("_PunchyPlayerPos");
        static readonly int OthersId = Shader.PropertyToID("_PunchyOthersPos");
        readonly Vector4[] othersPos = new Vector4[4];
        EntityQuery othersQuery;

        int GridSamples => (int)TileSize + 1;
        int MaxGrass => (int)(TileSize * TileSize * GrassPerSquareMetre * DensityScale);

        /// <summary>The density scale in use: the command line's -grassdensity if given, else the field.</summary>
        float DensityScale
        {
            get
            {
                if (commandLineDensity < 0f)
                {
                    commandLineDensity = 0f;
                    var args = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length - 1; i++)
                        if (args[i] == "-grassdensity" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                            commandLineDensity = math.max(d, 0.1f);
                }
                return commandLineDensity > 0f ? commandLineDensity : GrassDensityScale;
            }
        }
        float commandLineDensity = -1f;
        int MaxPebbles => (int)(TileSize * TileSize * PebblesPerSquareMetre);

        void OnDisable()
        {
            foreach (var t in tiles.Values)
                t.Dispose();
            tiles.Clear();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || !TryGetSource(out var source) || GrassMaterial == null || PebbleMaterial == null)
                return;

            // The world changed (re-bake): throw every tile away.
            if (!hasSource || !source.Equals(lastSource))
            {
                OnDisable();
                lastSource = source;
                hasSource = true;
            }

            Shader.SetGlobalFloat(FullId, FullDensityRange);
            Shader.SetGlobalVector(GrassTintId, new Vector4(GroundTint, GrassRange, 0f, 0f));

            if (playerQuery.TryGetSingleton<LocalTransform>(out var player))
                Shader.SetGlobalVector(PlayerId, new Vector4(player.Position.x, player.Position.y, player.Position.z, 0f));
            SetOthers(cam.transform.position);

            var camXZ = new float2(cam.transform.position.x, cam.transform.position.z);
            var radius = math.max(GrassRange, PebbleRange);
            UnloadFarTiles(camXZ, radius + TileSize * 1.5f);
            BuildMissingTiles(source, camXZ, radius);
            Draw(cam, camXZ);
        }

        /// <summary>Other players walking through the grass push it aside too: the nearest few to the camera.</summary>
        void SetOthers(Vector3 camera)
        {
            for (int i = 0; i < othersPos.Length; i++)
                othersPos[i] = Vector4.zero;
            if (!othersQuery.IsEmpty)
            {
                var em = queryWorld.EntityManager;
                var entities = othersQuery.ToEntityArray(Allocator.Temp);
                var used = 0;
                foreach (var e in entities)
                {
                    if (em.HasComponent<Unity.NetCode.GhostOwnerIsLocal>(e) && em.IsComponentEnabled<Unity.NetCode.GhostOwnerIsLocal>(e))
                        continue;
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    if (math.lengthsq(p) < 1e-6f || math.distance(p, camera) > GrassRange)
                        continue;
                    if (used < othersPos.Length)
                        othersPos[used++] = new Vector4(p.x, p.y, p.z, 1f);
                }
                entities.Dispose();
            }
            Shader.SetGlobalVectorArray(OthersId, othersPos);
        }

        void UnloadFarTiles(float2 camXZ, float radius)
        {
            scratch.Clear();
            foreach (var kv in tiles)
                if (math.distance(TileCentre(kv.Key), camXZ) > radius)
                    scratch.Add(kv.Key);
            foreach (var key in scratch)
            {
                tiles[key].Dispose();
                tiles.Remove(key);
            }
        }

        float2 TileCentre(int2 coord) => ((float2)coord + 0.5f) * TileSize;

        void BuildMissingTiles(in TerrainSource source, float2 camXZ, float radius)
        {
            scratch.Clear();
            var r = (int)math.ceil(radius / TileSize) + 1;
            var centre = (int2)math.floor(camXZ / TileSize);
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                var c = centre + new int2(x, z);
                var min = (float2)c * TileSize;
                if (!tiles.ContainsKey(c) && math.distance(math.clamp(camXZ, min, min + TileSize), camXZ) < radius)
                    scratch.Add(c);
            }
            if (scratch.Count == 0)
                return;
            scratch.Sort((a, b) => math.distancesq(TileCentre(a), camXZ).CompareTo(math.distancesq(TileCentre(b), camXZ)));
            var count = math.min(scratch.Count, MaxTileBuildsPerFrame);

            var g = GridSamples;
            var origins = new NativeArray<float2>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
                origins[i] = (float2)scratch[i] * TileSize;
            var samples = new NativeArray<GroundPoint>(count * g * g, Allocator.TempJob);
            var blades = new NativeArray<Blade>(count * MaxGrass, Allocator.TempJob);
            var pebbles = new NativeArray<Pebble>(count * MaxPebbles, Allocator.TempJob);
            var counts = new NativeArray<int2>(count, Allocator.TempJob);

            var sample = new SampleGroundJob
            {
                Source = source, Origins = origins, Grid = g, Spacing = TileSize / (g - 1), Points = samples
            }.Schedule(samples.Length, 64);
            new ScatterJob
            {
                Origins = origins, Points = samples, Grid = g, TileSize = TileSize,
                MaxGrass = MaxGrass, MaxPebbles = MaxPebbles, Blades = blades, Pebbles = pebbles, Counts = counts
            }.Schedule(count, 1, sample).Complete();

            for (int i = 0; i < count; i++)
            {
                var tile = new Tile
                {
                    GrassCount = counts[i].x,
                    PebbleCount = counts[i].y,
                    GrassProps = new MaterialPropertyBlock(),
                    PebbleProps = new MaterialPropertyBlock()
                };
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int s = i * g * g; s < (i + 1) * g * g; s++)
                {
                    minY = math.min(minY, samples[s].Height);
                    maxY = math.max(maxY, samples[s].Height);
                }
                var o = origins[i];
                tile.Bounds = new Bounds(new Vector3(o.x + TileSize * 0.5f, (minY + maxY) * 0.5f, o.y + TileSize * 0.5f),
                                         new Vector3(TileSize + 2f, maxY - minY + 3f, TileSize + 2f));
                if (tile.GrassCount > 0)
                {
                    tile.Grass = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tile.GrassCount, Marshal.SizeOf<Blade>());
                    tile.Grass.SetData(blades, i * MaxGrass, 0, tile.GrassCount);
                    tile.GrassProps.SetBuffer(BladesId, tile.Grass);
                }
                if (tile.PebbleCount > 0)
                {
                    tile.Pebbles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tile.PebbleCount, Marshal.SizeOf<Pebble>());
                    tile.Pebbles.SetData(pebbles, i * MaxPebbles, 0, tile.PebbleCount);
                    tile.PebbleProps.SetBuffer(PebblesId, tile.Pebbles);
                }
                tiles[scratch[i]] = tile;
            }

            origins.Dispose();
            samples.Dispose();
            blades.Dispose();
            pebbles.Dispose();
            counts.Dispose();
        }

        void Draw(Camera cam, float2 camXZ)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            foreach (var kv in tiles)
            {
                var tile = kv.Value;
                if (!GeometryUtility.TestPlanesAABB(frustum, tile.Bounds))
                    continue;
                var nearest = math.distance(camXZ, math.clamp(camXZ, (float2)kv.Key * TileSize, ((float2)kv.Key + 1f) * TileSize));
                DrawSet(tile.Grass, tile.GrassCount, tile.GrassProps, GrassMaterial, GrassVertices, nearest, GrassRange, tile.Bounds);
                DrawSet(tile.Pebbles, tile.PebbleCount, tile.PebbleProps, PebbleMaterial, PebbleVertices, nearest, PebbleRange, tile.Bounds);
            }
        }

        void DrawSet(GraphicsBuffer buffer, int count, MaterialPropertyBlock props, Material material, int vertices,
                            float nearest, float radius, Bounds bounds)
        {
            if (buffer == null || nearest > radius)
                return;
            // Draw only as many instances as the densest (nearest) point of the tile needs; the shader thins and
            // shrinks the rest smoothly by their own distance.
            var drawn = math.max(1, (int)math.ceil(count * Thinning(nearest, radius)));
            props.SetInt(CountId, count);
            props.SetFloat(RadiusId, radius);
            var rp = new RenderParams(material)
            {
                worldBounds = bounds,
                matProps = props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, vertices, drawn);
        }

        /// <summary>Fraction of instances kept at a distance (must match DetailThinning in GroundDetail.hlsl).</summary>
        float Thinning(float distance, float radius) => math.lerp(1f, 0.12f, math.smoothstep(FullDensityRange, radius, distance));

        bool TryGetSource(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                othersQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
                queryWorld = world;
            }
            return terrainQuery.TryGetSingleton(out source);
        }

        // ------------------------------------------------------------------------------------------------ jobs

        struct GroundPoint
        {
            public float Height;
            public float Grass;
            public float Pebbles;
            public float Snow;
            public float3 Albedo;
            public float Slope;      // normal.y
            public float Water;      // water surface height, or a large negative number
            public float Bare;       // 1 over a cave entrance or at its edge
        }

        [BurstCompile]
        struct SampleGroundJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float2> Origins;
            public int Grid;
            public float Spacing;
            [WriteOnly] public NativeArray<GroundPoint> Points;

            public void Execute(int i)
            {
                var perTile = Grid * Grid;
                var local = i % perTile;
                var xz = Origins[i / perTile] + new float2(local % Grid, local / Grid) * Spacing;
                var ground = Source.SampleGround(xz);
                var normal = Source.SampleNormal(xz, 1f);
                var surface = Source.SampleSurface(xz, ground, normal);
                // Nothing grows over a cave entrance, nor right at its edge (a blade there would hang over the opening).
                var bare = Source.MouthCut(xz, ground.Height) < 0.15f;
                Points[i] = new GroundPoint
                {
                    Height = ground.Height,
                    Grass = bare ? 0f : surface.Grass,
                    Pebbles = bare ? 0f : surface.Pebbles,
                    Snow = surface.Snow,
                    Albedo = surface.Albedo,
                    Slope = normal.y,
                    Water = ground.HasWater ? ground.WaterSurface : -1e6f,
                    Bare = bare ? 1f : 0f
                };

            }
        }

        [BurstCompile]
        struct ScatterJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Origins;
            [ReadOnly] public NativeArray<GroundPoint> Points;
            public int Grid;
            public float TileSize;
            public int MaxGrass, MaxPebbles;
            [NativeDisableParallelForRestriction] public NativeArray<Blade> Blades;
            [NativeDisableParallelForRestriction] public NativeArray<Pebble> Pebbles;
            [NativeDisableParallelForRestriction] public NativeArray<int2> Counts;

            public void Execute(int tile)
            {
                var origin = Origins[tile];
                var rng = new Unity.Mathematics.Random(math.hash(new int2((int)math.floor(origin.x), (int)math.floor(origin.y))) | 1u);
                var basePoint = tile * Grid * Grid;

                // Candidates are uniformly random (not a jittered grid), so any prefix of the list is an even thinning.
                int grass = 0;
                for (int i = 0; i < MaxGrass; i++)
                {
                    var local = rng.NextFloat2() * TileSize;
                    var p = Interpolate(basePoint, local);
                    var underwater = p.Water > p.Height + 0.02f;
                    var density = p.Grass * math.saturate(1f - p.Snow * 3f) * math.smoothstep(0.78f, 0.9f, p.Slope);
                    var keep = rng.NextFloat();
                    var shape = rng.NextFloat4();
                    if (underwater || keep >= density)
                        continue;
                    var tint = (0.8f + shape.w * 0.4f) * math.lerp(new float3(1f, 1f, 1f), new float3(1.25f, 1.08f, 0.6f), rng.NextFloat() * 0.45f);
                    Blades[tile * MaxGrass + grass++] = new Blade
                    {
                        Position = new float3(origin.x + local.x, p.Height - 0.03f, origin.y + local.y),
                        Facing = shape.x * math.PI * 2f,
                        Height = math.lerp(0.14f, 0.48f, shape.y * shape.y) * (0.55f + 0.45f * p.Grass),
                        Width = math.lerp(0.011f, 0.022f, shape.z),
                        Bend = math.lerp(0.15f, 0.5f, shape.z),
                        Random = rng.NextFloat(),
                        // A touch brighter and more saturated than the ground colour, which already averages
                        // grass with the soil showing between blades; some blades yellower.
                        Color = new float4(p.Albedo * tint * new float3(1.0f, 1.12f, 0.9f), 1f)
                    };
                }

                int stones = 0;
                for (int i = 0; i < MaxPebbles; i++)
                {
                    var local = rng.NextFloat2() * TileSize;
                    var p = Interpolate(basePoint, local);
                    var density = p.Pebbles * math.saturate(1f - p.Snow * 3f);
                    var keep = rng.NextFloat();
                    var shape = rng.NextFloat4();
                    if (keep >= density)
                        continue;
                    var size = 0.03f + shape.x * shape.x * shape.x * 0.22f;
                    var grey = math.lerp(p.Albedo, new float3(0.32f, 0.31f, 0.3f), 0.55f);
                    Pebbles[tile * MaxPebbles + stones++] = new Pebble
                    {
                        Position = new float3(origin.x + local.x, p.Height - size * 0.35f, origin.y + local.y),
                        Size = size,
                        Random = shape.y,
                        Tilt = shape.z,
                        Color = new float4(grey * (0.65f + shape.w * 0.6f), 1f)
                    };
                }

                Counts[tile] = new int2(grass, stones);
            }

            /// <summary>Ground at a point inside the tile, interpolated across the same triangle the terrain mesh uses.</summary>
            GroundPoint Interpolate(int basePoint, float2 local)
            {
                var spacing = TileSize / (Grid - 1);
                var uv = local / spacing;
                var cell = math.min((int2)math.floor(uv), Grid - 2);
                var f = uv - cell;
                var i0 = basePoint + cell.y * Grid + cell.x;
                GroundPoint a = Points[i0], b = Points[i0 + 1], c = Points[i0 + Grid], d = Points[i0 + Grid + 1];

                // The mesh splits each quad along the diagonal from (1,0) to (0,1).
                GroundPoint r;
                float w0, w1, w2;
                GroundPoint p0, p1, p2;
                if (f.x + f.y <= 1f) { p0 = a; p1 = b; p2 = c; w1 = f.x; w2 = f.y; }
                else { p0 = d; p1 = c; p2 = b; w1 = 1f - f.x; w2 = 1f - f.y; }
                w0 = 1f - w1 - w2;
                r.Height = p0.Height * w0 + p1.Height * w1 + p2.Height * w2;
                r.Grass = p0.Grass * w0 + p1.Grass * w1 + p2.Grass * w2;
                r.Pebbles = p0.Pebbles * w0 + p1.Pebbles * w1 + p2.Pebbles * w2;
                r.Snow = p0.Snow * w0 + p1.Snow * w1 + p2.Snow * w2;
                r.Albedo = p0.Albedo * w0 + p1.Albedo * w1 + p2.Albedo * w2;
                r.Slope = p0.Slope * w0 + p1.Slope * w1 + p2.Slope * w2;
                r.Water = math.max(math.max(p0.Water, p1.Water), p2.Water);
                // Next to a cave entrance, nothing: blending would carry grass and pebbles in over the opening (G-25).
                r.Bare = math.max(math.max(p0.Bare, p1.Bare), p2.Bare);
                if (r.Bare > 0f)
                {
                    r.Grass = 0f;
                    r.Pebbles = 0f;
                }
                return r;
            }
        }
    }
}
