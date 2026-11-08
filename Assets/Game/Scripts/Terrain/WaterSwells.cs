using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Sea swell (roadmap 3.4): the game-side twin of WaterSwell.hlsl, so a swimmer rides the swell the sea is drawn
    /// with, and the camera knows when a crest has closed over it. Height only: the drawn surface also slides a little
    /// along each wave, which this leaves out (a swimmer's bob is the part that shows).
    ///
    /// The swell runs towards the nearest shore (playtest 11-08 item 2: the sea breeze pushes it in), read from a
    /// coarse shore field built once per terrain from <see cref="TerrainSource.SampleHeight"/> (seed-derived, so the
    /// same on every machine). Each point sums two sets of waves turned to the two nearest of eight headings round
    /// its onshore heading, cross-faded, so a set's waves never bend (a heading that turned across the sea would warp
    /// the waves far out). Six waves of unrelated lengths and headings per set and a slow, broad rise and fall of their
    /// height (wave groups) keep the sea from showing a repeating pattern (item 1); over the shelf they grow and
    /// steepen (shoaling), so they crest and break before the beach (item 3).
    ///
    /// This class is the one source of the wave table and every number of the swell's shape: <see cref="Publish"/>
    /// uploads them as shader globals, so the drawn sea and the swimmer can't drift apart (refactor 3.4, audit
    /// section 2). WaterSwell.hlsl keeps only the maths, which must stay the same as <see cref="Height"/>.
    /// The state (on, sea level, wind, clock) is set once a frame on the main thread (Underwater.cs) and read from Burst.
    /// Limits: creatures, buoys, driftwood and boats still float at the flat level; the wind is the camera's, so in
    /// multiplayer each player's swell height follows their own wind.
    /// </summary>
    public static class WaterSwells
    {
        /// <summary>-noswells on the command line: a flat sea, as before, to compare.</summary>
        public static readonly bool Enabled = !System.Environment.CommandLine.Contains("-noswells");

        /// <summary>The shore field: onshore headings (unit xz) on a square grid, read bilinearly.</summary>
        struct ShoreField
        {
            public UnsafeList<float2> Headings;
            public float2 Origin;
            public float CellsPerMetre;
            /// <summary>Cells across; 0 until the field is built (the swell then runs towards the world's middle).</summary>
            public int Size;
        }

        /// <summary>What Burst code reads, kept apart: Burst can't run this class's managed static setup.</summary>
        static class Shared
        {
            struct StateKey { }
            struct TableKey { }
            struct ShapeKey { }
            struct GroupsKey { }
            struct ShoreKey { }
            /// <summary>x 1 on / 0 off, y the sea level, z the wind 0..1, w the shared clock (s).</summary>
            public static readonly SharedStatic<float4> State = SharedStatic<float4>.GetOrCreate<StateKey>();
            /// <summary>The wave table, as in Table.</summary>
            public static readonly SharedStatic<FixedList4096Bytes<float4>> Table = SharedStatic<FixedList4096Bytes<float4>>.GetOrCreate<TableKey>();
            /// <summary>Shape, Mix, Depths, GroupA, GroupB: as the shader's globals of those names.</summary>
            public static readonly SharedStatic<float4x4> Shape = SharedStatic<float4x4>.GetOrCreate<ShapeKey>();
            public static readonly SharedStatic<float4x2> Groups = SharedStatic<float4x2>.GetOrCreate<GroupsKey>();
            public static readonly SharedStatic<ShoreField> Shore = SharedStatic<ShoreField>.GetOrCreate<ShoreKey>();
        }

        static readonly int SwellId = Shader.PropertyToID("_PunchySwell");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly int WavesId = Shader.PropertyToID("_PunchySwellWaves");
        static readonly int ShapeId = Shader.PropertyToID("_PunchySwellShape");
        static readonly int MixId = Shader.PropertyToID("_PunchySwellMix");
        static readonly int DepthsId = Shader.PropertyToID("_PunchySwellDepths");
        static readonly int LimitsId = Shader.PropertyToID("_PunchySwellLimits");
        static readonly int GroupsId = Shader.PropertyToID("_PunchySwellGroups");
        static readonly int ShoreId = Shader.PropertyToID("_PunchySwellShore");
        static readonly int ShoreAtId = Shader.PropertyToID("_PunchySwellShoreAt");

        /// <summary>The shader's room for waves (PunchySwellMaxWaves in WaterSwell.hlsl: the global array's size only).
        /// Unity fixes a global array's length the first time it's set, so the upload is always this long.</summary>
        public const int MaxWaves = 12;

        /// <summary>
        /// The waves of one set, longest first (the shader's detail policy drops the short ones first, far off):
        /// wavelength (m), heading (radians, from the set's own onshore heading), relative height, phase. The lengths
        /// step by uneven ratios (1.46..1.53) so no two waves line up again over any short distance.
        /// </summary>
        static readonly float4[] Table =
        {
            new float4(41f, 0.00f, 1.00f, 0.0f),
            new float4(27.3f, -0.38f, 0.55f, 1.7f),
            new float4(17.9f, 0.31f, 0.34f, 4.1f),
            new float4(12.1f, -0.64f, 0.21f, 2.6f),
            new float4(8.3f, 0.57f, 0.13f, 5.3f),
            new float4(5.7f, -0.17f, 0.08f, 0.9f),
        };

        /// <summary>The swell's height (m) in a calm, and what a full wind adds, before each wave's relative height.</summary>
        const float CalmHeight = 0.18f, WindHeight = 0.9f;
        /// <summary>How far each wave carries a point along it, as a fraction of its height (Gerstner steepness).</summary>
        const float Along = 0.55f;
        /// <summary>Headings the sets are turned to (each set's waves keep straight; a point blends the two nearest).</summary>
        const float Sectors = 8f;
        /// <summary>How much the wave groups raise and lower the height (1 +- this).</summary>
        const float GroupDepth = 0.35f;
        /// <summary>Over the shelf (shoaling): how much higher and how much steeper the waves get at full shoaling.</summary>
        const float ShoalHeight = 0.25f, ShoalSteep = 0.6f;
        /// <summary>The swell fades in between these depths (m: none at the beach) ...</summary>
        const float FadeFrom = 0.1f, FadeTo = 2.5f;
        /// <summary>... and shoals fully at ShoalFull, not at all from ShoalNone deep.</summary>
        const float ShoalFull = 2.5f, ShoalNone = 8f;
        /// <summary>The wave groups: two slow, broad patterns (heading x z, wavelength m, speed m/s) whose sum raises and
        /// lowers the waves' height, so big sets roll through between quieter spells.</summary>
        static readonly float4 GroupA = new float4(0.8f, 0.6f, 260f, 4.0f), GroupB = new float4(-0.45f, 0.89f, 410f, 3.3f);
        /// <summary>The swell's height is held under this share of the water's depth, so a trough never bares the bed
        /// (over the shelf, shoaled troughs dipped below sand bars and showed ragged patches of bed).</summary>
        const float DepthShare = 0.3f;
        /// <summary>Extra room (m) round the furthest the swell can reach, for the render bounds.</summary>
        const float BoundsMargin = 0.5f;

        /// <summary>The shore field: cells across, and how far past the world's edge it reaches (m).</summary>
        const int ShoreCells = 160;
        const float ShoreBeyond = 450f;

        /// <summary>
        /// The furthest the swell can move a point of the sea's surface (m: x and z sideways, y up or down), with a
        /// margin, worked out from the wave table and shape (refactor 3.4, audit section 2: the bounds were the flat
        /// mesh's, so a heaving chunk at a frustum edge could be culled while still in view). Every wave of both sets
        /// at its crest at once, in the tallest group, fully shoaled: never reached, but never exceeded.
        /// TerrainStreamingSystem pads every water chunk's and region's render bounds by it.
        /// </summary>
        public static readonly float3 BoundsPad = ComputeBoundsPad();

        static float3 ComputeBoundsPad()
        {
            var total = 0f;
            foreach (var w in Table)
                total += w.z;
            // (Two sets cross-faded and renormalised add up to at most sqrt 2 of one.)
            var height = (CalmHeight + WindHeight) * total * math.SQRT2 * (1f + GroupDepth) * (1f + ShoalHeight);
            var along = Along * (1f + ShoalSteep) * height;
            return new float3(along, height, along) + BoundsMargin;
        }

        /// <summary>Sets this frame's state for the shader and for Burst code (main thread, once a frame), and builds
        /// the shore field for this terrain when it's new.</summary>
        public static void Publish(in TerrainSource terrain)
        {
            var clock = Shader.GetGlobalFloat(ClockId);
            var state = new float4(Enabled ? 1f : 0f, terrain.World.SeaLevel, Shader.GetGlobalFloat(WindId), clock > 0f ? clock : Time.time);
            Shared.State.Data = state;
            Shader.SetGlobalVector(SwellId, new Vector4(state.x, state.y, state.z, 0f));
            if (!built)
                Build();
            // (Every frame, cheap: globals are easily lost in the editor.)
            Shader.SetGlobalVectorArray(WavesId, upload);
            var shape = Shared.Shape.Data;
            Shader.SetGlobalVector(ShapeId, shape.c0);
            Shader.SetGlobalVector(MixId, shape.c1);
            Shader.SetGlobalVector(DepthsId, shape.c2);
            Shader.SetGlobalVector(LimitsId, shape.c3);
            Shader.SetGlobalVectorArray(GroupsId, groupsUpload);
            UpdateShore(terrain);
            Shader.SetGlobalVector(ShoreAtId, shoreReady ? new Vector4(shore.Origin.x, shore.Origin.y, shore.CellsPerMetre, shore.Size) : Vector4.zero);
            if (shoreTexture != null)
                Shader.SetGlobalTexture(ShoreId, shoreTexture);
        }

        static bool built;
        static readonly Vector4[] upload = new Vector4[MaxWaves];
        static readonly Vector4[] groupsUpload = new Vector4[2];

        /// <summary>The wave table and shape for Burst and for the shader upload (once: they never change in a run).
        /// Padding entries have no height, so the shader can loop over all of them.</summary>
        static void Build()
        {
            built = true;
            Debug.Assert(Table.Length <= MaxWaves, "WaterSwells: more waves than the shader has room for");
            var list = new FixedList4096Bytes<float4>();
            for (int i = 0; i < MaxWaves; i++)
            {
                var w = i < Table.Length ? Table[i] : new float4(10f, 0f, 0f, 0f);
                if (i < Table.Length)
                    list.Add(w);
                upload[i] = new Vector4(w.x, w.y, w.z, w.w);
            }
            Shared.Table.Data = list;
            Shared.Shape.Data = new float4x4(
                new float4(CalmHeight, WindHeight, Along, Table.Length),
                new float4(Sectors, GroupDepth, ShoalHeight, ShoalSteep),
                new float4(FadeFrom, FadeTo, ShoalFull, ShoalNone),
                new float4(DepthShare, 0f, 0f, 0f));
            Shared.Groups.Data = new float4x2(Group(GroupA), Group(GroupB));
            groupsUpload[0] = Shared.Groups.Data.c0;
            groupsUpload[1] = Shared.Groups.Data.c1;
        }

        /// <summary>A wave group as the maths wants it: heading x z, wavenumber, angular speed.</summary>
        static float4 Group(float4 g)
        {
            var k = 2f * math.PI / g.z;
            return new float4(math.normalize(g.xy), k, k * g.w);
        }

        // ---------------------------------------------------------------- the shore field

        static ShoreField shore;
        static bool shoreReady, shoreBuilding;
        static TerrainSource shoreFor, shoreBuildingFor;
        static JobHandle shoreJob;
        static NativeArray<float> shoreLand;
        static NativeArray<float2> shoreHeadings;
        static Texture2D shoreTexture;

        /// <summary>
        /// Starts building the shore field when the terrain is new, and takes it up once built (a frame or more later:
        /// a background job of ~26k height samples, never waited on in the frame it starts). Until then the swell runs
        /// towards the world's middle.
        /// </summary>
        static void UpdateShore(in TerrainSource terrain)
        {
            if (shoreBuilding && !terrain.Equals(shoreBuildingFor))
                DropShoreJob();
            if (shoreBuilding)
            {
                if (!shoreJob.IsCompleted)
                    return;
                shoreJob.Complete();
                TakeShore();
                DropShoreJob();
                return;
            }
            if (shoreReady && terrain.Equals(shoreFor) || !terrain.Biomes.IsCreated)
                return;
            var size = ShoreCells;
            var half = terrain.World.WorldSize * 0.5f + ShoreBeyond;
            var cell = 2f * half / size;
            shoreLand = new NativeArray<float>(size * size, Allocator.Persistent);
            shoreHeadings = new NativeArray<float2>(size * size, Allocator.Persistent);
            var land = new LandJob { Source = terrain, Origin = -half, Cell = cell, Size = size, Land = shoreLand }.Schedule(size * size, 64);
            shoreJob = new HeadingJob { Land = shoreLand, Size = size, Headings = shoreHeadings }.Schedule(land);
            shoreBuilding = true;
            shoreBuildingFor = terrain;
            shore.Origin = -half;
            shore.CellsPerMetre = 1f / cell;
            // The job reads the TerrainSource blobs, which quitting, leaving play mode or a world swap frees: finish it
            // first, while they are intact (as DunesColossus does).
            Application.quitting -= DropShoreJob;
            Application.quitting += DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing -= DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing += DropShoreJob;
        }

        /// <summary>Copies a finished field into the Burst copy and the shader's texture.</summary>
        static void TakeShore()
        {
            var size = ShoreCells;
            var field = Shared.Shore.Data;
            // (The Burst copy is made once and overwritten in place, never freed while a swimmer's job may read it.)
            if (!field.Headings.IsCreated)
                field.Headings = new UnsafeList<float2>(size * size, Allocator.Persistent);
            field.Headings.Resize(size * size);
            for (int i = 0; i < size * size; i++)
                field.Headings[i] = shoreHeadings[i];
            field.Origin = shore.Origin;
            field.CellsPerMetre = shore.CellsPerMetre;
            field.Size = size;
            Shared.Shore.Data = field;
            shore = field;
            if (shoreTexture == null)
                shoreTexture = new Texture2D(size, size, TextureFormat.RGFloat, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SwellShore", hideFlags = HideFlags.DontSave };
            shoreTexture.SetPixelData(shoreHeadings, 0);
            shoreTexture.Apply(false, false);
            shoreReady = true;
            shoreFor = shoreBuildingFor;
            Debug.Log($"WaterSwells: shore field built ({size}x{size}, {1f / shore.CellsPerMetre:F0} m cells)");
        }

        /// <summary>Waits for a field still in flight and frees its arrays (taken, stale, quitting or a world swap).</summary>
        static void DropShoreJob()
        {
            Application.quitting -= DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing -= DropShoreJob;
            if (!shoreBuilding)
                return;
            shoreJob.Complete();
            shoreLand.Dispose();
            shoreHeadings.Dispose();
            shoreBuilding = false;
        }

        /// <summary>How much of each cell is land (0 sea, 1 land, eased over 2 m round the sea level).</summary>
        [BurstCompile]
        struct LandJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Cell;
            public int Size;
            [WriteOnly] public NativeArray<float> Land;

            public void Execute(int i)
            {
                var xz = new float2(Origin + (i % Size + 0.5f) * Cell, Origin + (i / Size + 0.5f) * Cell);
                Land[i] = math.saturate((Source.SampleHeight(xz) - Source.World.SeaLevel) * 0.5f + 0.5f);
            }
        }

        /// <summary>
        /// The onshore heading per cell: up the slope of the land blurred at two scales, the fine one (~3 cells) for the
        /// coast close by, the broad one (~12 cells) so open water far out still faces the nearest land; a whisper of
        /// "towards the middle" settles cells with no land in reach.
        /// </summary>
        [BurstCompile]
        struct HeadingJob : IJob
        {
            [ReadOnly] public NativeArray<float> Land;
            public int Size;
            public NativeArray<float2> Headings;

            public void Execute()
            {
                var fine = new NativeArray<float>(Land, Allocator.Temp);
                var broad = new NativeArray<float>(Land, Allocator.Temp);
                var scratch = new NativeArray<float>(Land.Length, Allocator.Temp);
                for (int pass = 0; pass < 3; pass++)
                {
                    Blur(fine, scratch, 2);
                    Blur(broad, scratch, 8);
                }
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var g = Gradient(fine, x, y) + 2f * Gradient(broad, x, y);
                        var middle = new float2(Size * 0.5f - 0.5f - x, Size * 0.5f - 0.5f - y);
                        g += 1e-5f * math.normalizesafe(middle);
                        Headings[y * Size + x] = math.normalizesafe(g, new float2(1f, 0f));
                    }
            }

            float2 Gradient(NativeArray<float> f, int x, int y)
            {
                int x0 = math.max(x - 1, 0), x1 = math.min(x + 1, Size - 1), y0 = math.max(y - 1, 0), y1 = math.min(y + 1, Size - 1);
                return new float2(f[y * Size + x1] - f[y * Size + x0], f[y1 * Size + x] - f[y0 * Size + x]);
            }

            /// <summary>One box blur of the given radius along x, then along y (edge cells repeat).</summary>
            void Blur(NativeArray<float> f, NativeArray<float> tmp, int radius)
            {
                var n = Size;
                var norm = 1f / (2 * radius + 1);
                for (int y = 0; y < n; y++)
                {
                    var sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += f[y * n + math.clamp(k, 0, n - 1)];
                    for (int x = 0; x < n; x++)
                    {
                        tmp[y * n + x] = sum * norm;
                        sum += f[y * n + math.min(x + radius + 1, n - 1)] - f[y * n + math.max(x - radius, 0)];
                    }
                }
                for (int x = 0; x < n; x++)
                {
                    var sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += tmp[math.clamp(k, 0, n - 1) * n + x];
                    for (int y = 0; y < n; y++)
                    {
                        f[y * n + x] = sum * norm;
                        sum += tmp[math.min(y + radius + 1, n - 1) * n + x] - tmp[math.max(y - radius, 0) * n + x];
                    }
                }
            }
        }

        /// <summary>The way the swell runs at xz (not normalised): towards the nearest shore. As PunchySwellOnshore.</summary>
        static float2 Onshore(float2 xz)
        {
            var f = Shared.Shore.Data;
            if (f.Size < 1)
                return -xz / math.max(math.length(xz), 1f);
            var u = (xz - f.Origin) * f.CellsPerMetre - 0.5f;
            var i0 = math.floor(u);
            var t = u - i0;
            var n = f.Size - 1;
            var a = math.clamp((int2)i0, 0, n);
            var b = math.clamp((int2)i0 + 1, 0, n);
            var h = f.Headings;
            return math.lerp(math.lerp(h[a.y * f.Size + a.x], h[a.y * f.Size + b.x], t.x),
                             math.lerp(h[b.y * f.Size + a.x], h[b.y * f.Size + b.x], t.x), t.y);
        }

        /// <summary>
        /// How far the swell lifts the water surface at xz (m; 0 off the sea or with -noswells), for water whose flat
        /// surface is <paramref name="surface"/> over a bed at <paramref name="bed"/>. Same maths as PunchySwell, every
        /// wave counted (the shader drops the short ones only far from the camera).
        /// </summary>
        public static float Height(float2 xz, float surface, float bed)
        {
            var s = Shared.State.Data;
            if (s.x <= 0f || math.abs(surface - s.y) > 0.05f)
                return 0f;
            var shape = Shared.Shape.Data;
            var depth = surface - bed;
            var amount = math.smoothstep(shape.c2.x, shape.c2.y, depth);
            if (amount <= 0f)
                return 0f;
            var shoal = 1f - math.smoothstep(shape.c2.z, shape.c2.w, depth);
            var groups = Shared.Groups.Data;
            var e = 0.5f + 0.25f * (math.sin(math.dot(groups.c0.xy, xz) * groups.c0.z - groups.c0.w * s.w)
                                   + math.sin(math.dot(groups.c1.xy, xz) * groups.c1.z - groups.c1.w * s.w));
            var height = (shape.c0.x + shape.c0.y * math.saturate(s.z)) * amount * (1f + shape.c1.z * shoal)
                         * (1f + shape.c1.y * (2f * e - 1f));
            height = math.min(height, shape.c3.x * depth);
            var on = Onshore(xz);
            var sectors = shape.c1.x;
            var f = math.atan2(on.y, on.x) * sectors / (2f * math.PI);
            var s0 = math.floor(f);
            var blend = math.smoothstep(0f, 1f, f - s0);
            var norm = math.rsqrt(blend * blend + (1f - blend) * (1f - blend));
            var table = Shared.Table.Data;
            var y = 0f;
            for (int set = 0; set < 2; set++)
            {
                var weight = (set == 0 ? 1f - blend : blend) * norm;
                if (weight < 1e-3f)
                    continue;
                var sector = s0 + set;
                sector -= sectors * math.floor(sector / sectors);
                var heading = sector * 2f * math.PI / sectors;
                for (int i = 0; i < table.Length; i++)
                {
                    var w = table[i];
                    var k = 2f * math.PI / w.x;
                    var dir = new float2(math.cos(heading + w.y), math.sin(heading + w.y));
                    var theta = k * math.dot(dir, xz) - math.sqrt(9.81f * k) * s.w + w.w + sector * 2.39996f * (i + 1);
                    y += height * w.z * weight * math.sin(theta);
                }
            }
            return y;
        }
    }
}
