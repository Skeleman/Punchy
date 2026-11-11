using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-9 (#232): the water visibly gives under what swims, wades or lands in it. The chunk water's quads are a metre
    /// across, too coarse to dent, so near the player (a <see cref="Span"/> m square, following in <see cref="Snap"/> m
    /// steps) this copies the chunk water's wet quads and cuts each by how big it looks (R-9 row 4, rule 20:
    /// <see cref="Punchy.Settings.DetailPolicy.WaterSheet"/>): 8 x 8 (12.5 cm) right round the player, 4 x 4 and 2 x 2
    /// further out; the quads it would leave whole it leaves to the chunk water. Drawn in the same water material with
    /// _WATER_FIELD on (WaterField.hlsl); the chunk water clips itself out of the cells it covers. Over the
    /// same square it steps a wave field (WaterField.compute, 256 x 256 texels, 60 steps a second): every creature at the
    /// surface there (CreatureWakeSystem's choice: swimmers, floaters, animals crossing, folk, players' bodies) presses a
    /// dimple into it, deeper the faster it goes, so it pushes a bow wave and trails a wake (and, moving, lifts a swell
    /// just ahead of it: Zach's leading swell, row 4), and anything dropping into
    /// the water knocks out a splash ring. The fine sheet's vertices rise and fall by the field, and its normals follow.
    /// Outside the square, on Low water quality or with "-waterfield 0", the swimmer ripples carry on alone.
    /// A river fall that looks big enough gets a fine sheet of its own (row 5, WaterPatchSystem.Falls.cs): its sloping
    /// quads cut along the drop, with a curling lip, a rippled curtain and churn at the foot.
    /// Cosmetic and per machine, from state every machine already has (the creature snapshot); nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class WaterPatchSystem : SystemBase
    {
        /// <summary>Off for tests and profiling (and with "-waterfield 0").</summary>
        public static bool On = true;
        /// <summary>The square (m), the step it follows the player in (m), and the field's texels across it.</summary>
        public const int Span = 32, Snap = 4, Size = 256;
        const float TexelsPerMetre = Size / (float)Span;
        /// <summary>The finest cut (a chunk water quad into Cuts x Cuts, tier 0; each tier further out halves it).</summary>
        /// (Profiler experiment, b/fix-waterfield-step: "-waterfieldcuts 8|4|2" for playtesting a coarser finest cut.)
        static readonly int Cuts = LaunchInt("-waterfieldcuts", 8, 2, 8);
        /// <summary>How far (m) the player moves before the cut is redone round them, and how much the camera's distance
        /// from them may change (as a ratio) before it is.</summary>
        const int FineStep = 1;
        const float CameraBand = 1.2f;
        /// <summary>The most indices the sheet may have per unit of Detail (12,000 at High: half a lake chunk's water).</summary>
        const int SheetBudget = 8000;
        /// <summary>The field's step (s), the most steps a frame catches up, and how fast its waves run (m/s): slower than
        /// a walk, so a wader trails a V rather than pushing a ring ahead of it.</summary>
        /// (Profiler experiment: "-waterfieldhz 60|30|20" steps it less often; the damping and decay per step are rescaled
        /// so waves die away over the same seconds, and the wave speed is unchanged.)
        static readonly float StepTime = 1f / LaunchInt("-waterfieldhz", 60, 10, 60);
        const float WaveSpeed = 0.9f;
        const int MaxSteps = 4, MaxSources = 64;

        static readonly int FieldId = Shader.PropertyToID("_PunchyWaterField");
        static readonly int FieldAtId = Shader.PropertyToID("_PunchyWaterFieldAt");
        static readonly int RectId = Shader.PropertyToID("_PunchyWaterPatchRect");
        static readonly int MaskId = Shader.PropertyToID("_PunchyWaterPatchMask");
        static readonly int WeightId = Shader.PropertyToID("_PunchyWaterPatchWeight");
        static readonly int SrcId = Shader.PropertyToID("_Src");
        static readonly int DstId = Shader.PropertyToID("_Dst");
        static readonly int SourcesId = Shader.PropertyToID("_Sources");
        static readonly int SourceCountId = Shader.PropertyToID("_SourceCount");
        static readonly int ShiftId = Shader.PropertyToID("_Shift");
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int CourantId = Shader.PropertyToID("_Courant2");
        static readonly int DampingId = Shader.PropertyToID("_Damping");
        static readonly int DecayId = Shader.PropertyToID("_Decay");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        /// <summary>
        /// Profiler experiment (Zach 11-10, b/fix-waterfield-step): the field sleeps after SleepAfter steps with nothing
        /// pressing it (cleared flat, no dispatch) until something does; each step's thread groups loop only over the
        /// sources that reach them. "-waterfieldsleep 0" / "-waterfieldcull 0" turn each off (as before); "-waterfieldstep 0"
        /// never steps (the sheet drawn flat: what the compute costs, for the bench only).
        /// </summary>
        static readonly bool SleepOn = LaunchSwitch("-waterfieldsleep"), CullOn = LaunchSwitch("-waterfieldcull"), StepOn = LaunchSwitch("-waterfieldstep");
        static int SleepAfter => (int)(10f / StepTime);
        int quietSteps, dispatched, skipped, sourceSum;
        bool asleep;
        float logClock;

        static int LaunchInt(string name, int fallback, int min, int max)
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var v) ? math.clamp(v, min, max) : fallback;
        }

        static bool LaunchSwitch(string name)
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, name);
            return !(at >= 0 && at + 1 < args.Length && args[at + 1] == "0");
        }
        static readonly Vector4 NoRect = new Vector4(1e9f, 1e9f, -1e9f, -1e9f);

        /// <summary>A fine sheet vertex: the chunk water's (WaterVertex) plus where it sits on a coarser neighbour's edge.</summary>
        struct PatchVertex
        {
            public float3 Position;
            public float4 Data, Look;
            public float2 Glow;
            public float4 Parent;
            /// <summary>The heights of the neighbour's segment's ends, less the vertex's own (a fall's sheet stands near
            /// upright, and what moves it there depends on height).</summary>
            public float2 ParentY;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 2),
        };

        /// <summary>One wet chunk water quad in the square: its cell (world metres) and corners (world positions), in
        /// the chunk mesh's order: A (x, z), B (x + 1, z), C (x, z + 1), D (x + 1, z + 1).</summary>
        struct ParentQuad
        {
            public int2 Cell;
            public WaterVertex A, B, C, D;
        }

        /// <summary>The wet quads of one chunk's water inside the square (its index list names them, six to a quad,
        /// the first the quad's corner A).</summary>
        [BurstCompile]
        struct GatherJob : IJob
        {
            [ReadOnly] public NativeArray<WaterVertex> Vertices;
            [ReadOnly] public NativeArray<uint> Indices;
            public int Row;
            public int2 Origin, RectMin, RectMax;
            public NativeList<ParentQuad> Quads;

            public void Execute()
            {
                var offset = new float3(Origin.x, 0f, Origin.y);
                for (int t = 0; t + 5 < Indices.Length; t += 6)
                {
                    var i0 = (int)Indices[t];
                    var cell = Origin + new int2(i0 % Row, i0 / Row);
                    if (math.any(cell < RectMin) || math.any(cell >= RectMax))
                        continue;
                    var q = new ParentQuad { Cell = cell, A = Vertices[i0], B = Vertices[i0 + 1], C = Vertices[i0 + Row], D = Vertices[i0 + Row + 1] };
                    q.A.Position += offset;
                    q.B.Position += offset;
                    q.C.Position += offset;
                    q.D.Position += offset;
                    Quads.Add(q);
                }
            }
        }

        /// <summary>Cuts each quad into nx x nz (its cell's cuts across x in Grid and along z in Rows: the same for the
        /// player's sheet, apart on a fall's, whose upright quads want many more rows down the drop than across), split
        /// along the same diagonal as the chunk water (B to C), so the fine sheet lies where the chunk water did. A vertex
        /// on an edge it shares with a coarser neighbour (a coarser cut quad, or the chunk water past the rectangle's edge)
        /// is told that neighbour's segment, so the shader puts it on that segment's straight line and the two leave no
        /// crack.</summary>
        [BurstCompile]
        struct CutJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ParentQuad> Quads;
            /// <summary>Each of the rectangle's cells' cuts (0: no quad there), and each quad's first vertex and index.</summary>
            [ReadOnly] public NativeArray<byte> Grid, Rows;
            [ReadOnly] public NativeArray<int2> Starts;
            /// <summary>The rectangle's corner and cells across (Grid's row).</summary>
            public int2 RectMin;
            public int Width;
            // (Both come from one MeshData, which the safety system takes for the same container twice.)
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<PatchVertex> Vertices;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<uint> Indices;

            static float4 Mix(float4 a, float4 b, float4 c, float4 d, float u, float v) =>
                u + v <= 1f ? a + u * (b - a) + v * (c - a) : d + (1f - u) * (c - d) + (1f - v) * (b - d);

            /// <summary>A cell's cuts: the chunk water's 1 outside the rectangle, 0 where it's dry.</summary>
            int CutsAt(NativeArray<byte> cuts, int2 cell)
            {
                var c = cell - RectMin;
                return math.any(c < 0) || math.any(c >= Width) ? 1 : cuts[c.y * Width + c.x];
            }

            /// <summary>The k-th of n + 1 vertices along an edge (from corner a to corner b), next to a neighbour cut nc:
            /// if that's coarser and the vertex isn't one of its own, the depths at the ends of the neighbour's segment it
            /// sits on, how far along that it is (m), and the segment's length, signed by which way it runs (+ along z, -
            /// along x; WaterField.hlsl, PunchyWaterPatchVertex), and the ends' heights less the vertex's.</summary>
            static float4 EdgeParent(in WaterVertex a, in WaterVertex b, int k, int n, int nc, float sign, out float2 heights)
            {
                heights = float2.zero;
                if (nc <= 0 || nc >= n)
                    return float4.zero;
                var r = n / nc;
                if (k % r == 0)
                    return float4.zero;
                var s = k / r * r;
                float y = math.lerp(a.Position.y, b.Position.y, k / (float)n);
                heights = new float2(math.lerp(a.Position.y, b.Position.y, s / (float)n), math.lerp(a.Position.y, b.Position.y, (s + r) / (float)n)) - y;
                return new float4(math.lerp(a.Data.z, b.Data.z, s / (float)n), math.lerp(a.Data.z, b.Data.z, (s + r) / (float)n), (k - s) / (float)n, sign * r / n);
            }

            public void Execute(int index)
            {
                var q = Quads[index];
                int nx = CutsAt(Grid, q.Cell), nz = CutsAt(Rows, q.Cell);
                var first = Starts[index].x;
                // (An edge along z has as many vertices as the cell has rows; one along x, as it has columns.)
                int west = CutsAt(Rows, q.Cell - new int2(1, 0)), east = CutsAt(Rows, q.Cell + new int2(1, 0));
                int south = CutsAt(Grid, q.Cell - new int2(0, 1)), north = CutsAt(Grid, q.Cell + new int2(0, 1));
                for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = i / (float)nx, v = j / (float)nz;
                    var p = Mix(new float4(q.A.Position, 0), new float4(q.B.Position, 0), new float4(q.C.Position, 0), new float4(q.D.Position, 0), u, v);
                    var g = Mix(new float4(q.A.Glow, 0, 0), new float4(q.B.Glow, 0, 0), new float4(q.C.Glow, 0, 0), new float4(q.D.Glow, 0, 0), u, v);
                    // (Not at a chunk water corner, which is exact anyway.)
                    var parent = float4.zero;
                    var heights = float2.zero;
                    if (j % nz != 0 && i == 0)
                        parent = EdgeParent(q.A, q.C, j, nz, west, 1f, out heights);
                    else if (j % nz != 0 && i == nx)
                        parent = EdgeParent(q.B, q.D, j, nz, east, 1f, out heights);
                    else if (i % nx != 0 && j == 0)
                        parent = EdgeParent(q.A, q.B, i, nx, south, -1f, out heights);
                    else if (i % nx != 0 && j == nz)
                        parent = EdgeParent(q.C, q.D, i, nx, north, -1f, out heights);
                    Vertices[first + j * (nx + 1) + i] = new PatchVertex
                    {
                        Position = p.xyz,
                        Data = Mix(q.A.Data, q.B.Data, q.C.Data, q.D.Data, u, v),
                        Look = Mix(q.A.Look, q.B.Look, q.C.Look, q.D.Look, u, v),
                        Glow = g.xy,
                        Parent = parent,
                        ParentY = heights
                    };
                }
                var t = Starts[index].y;
                for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var a = (uint)(first + j * (nx + 1) + i);
                    uint b = a + 1, c = a + (uint)nx + 1, d = c + 1;
                    Indices[t++] = a; Indices[t++] = c; Indices[t++] = b;
                    Indices[t++] = b; Indices[t++] = c; Indices[t++] = d;
                }
            }
        }

        /// <summary>What presses on the field this frame (CreatureWakeSystem's choice of creatures): a dimple under each
        /// at the surface, and a splash where one has just dropped through it.</summary>
        [BurstCompile]
        struct SourceJob : IJob
        {
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public TerrainSource Terrain;
            public float2 Corner;
            public float DeltaTime;
            /// <summary>Seconds since play began, now and when this last ran (wading strides keep time by it).</summary>
            public float Time, LastTime;
            /// <summary>Each creature's place when this last ran (to see it drop through the surface, and which way it goes).</summary>
            public NativeHashMap<Entity, float3> LastAt;
            public NativeList<float4> Presses, Splashes;

            public void Execute()
            {
                for (int i = 0; i < States.Length; i++)
                {
                    var s = States[i];
                    var at = (s.Position.xz - Corner) * TexelsPerMetre;
                    if (math.any(at < -8f) || math.any(at > Size + 8f) || s.Gone || s.Fly > 0.5f)
                        continue;
                    ref var b = ref Bodies[i].Blob.Value;
                    bool upright = b.Upright || s.Mode == CreatureMode.Puppet || b.CrossesWater;
                    if (b.Habitat != Habitat.Swims && b.Habitat != Habitat.Floats && !upright)
                        continue;
                    var e = Entities[i];
                    var hadY = LastAt.TryGetValue(e, out var lastAt);
                    var lastY = lastAt.y;
                    LastAt[e] = s.Position;
                    var g = Terrain.SampleGround(s.Position.xz);
                    if (!g.HasWater || g.WaterSurface - g.Height < 0.15f)
                        continue;
                    var under = g.WaterSurface - s.Position.y;
                    if (under < -0.8f || under > 1.6f)
                        continue;
                    var size = math.clamp(s.Scale * b.FrontRadius * 6f, 0.5f, 1.5f);
                    // (At least 35 cm across, or a wader's legs dent the sheet too narrowly to read as a wake.)
                    var radius = math.clamp(s.Scale * b.FrontRadius * 4f, 0.35f, 1f) * TexelsPerMetre;
                    // (Something standing in it, feet down, crosses the surface wherever its feet are under; a fish or a
                    // diver only dents it near the top.)
                    var near = upright ? 1f : 1f - math.smoothstep(0.3f, 1.2f, under);
                    var depth = (0.04f + 0.05f * math.min(s.Speed, 3f)) * size * near;
                    if (depth > 0.002f && Presses.Length < MaxSources)
                        Presses.Add(new float4(at, radius, depth));
                    // And while it moves, it keeps shoving water aside (a small knock every step, in with the dimples so
                    // every step gets it): the troughs it leaves spring back as rings, and in a line they make its wake.
                    var shove = 0.006f * math.min(s.Speed, 4f) * size * near;
                    if (shove > 0.0005f && Presses.Length < MaxSources)
                        Presses.Add(new float4(at, -radius, shove));
                    // Zach's leading swell (13:05): moving, it heaps the water up just ahead of itself, a raised hump before
                    // the V of its wake (a held press lifting the surface: a negative depth). Which way is ahead: the way
                    // it actually went since last time, or where it faces when it has hardly moved.
                    var went = hadY ? (s.Position.xz - lastAt.xz) / math.max(Time - LastTime, 1e-3f) : float2.zero;
                    var ahead = math.lengthsq(went) > 0.04f ? math.normalize(went) : new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    var hump = 0.03f * math.min(s.Speed, 3f) * size * near;
                    if (s.Speed > 0.4f && hump > 0.002f && Presses.Length < MaxSources)
                        Presses.Add(new float4(at + ahead * radius * 1.6f, radius * 1.2f, -hump));
                    // A wader's strides: a small knock a few times a second, each spreading as a ring, so a line of them
                    // trails it (each creature on its own beat).
                    var beat = 1.2f + 0.8f * math.min(s.Speed, 3f);
                    var phase = (e.Index * 0.618f) % 1f;
                    if (upright && s.Speed > 0.3f && math.floor(Time * beat + phase) != math.floor(LastTime * beat + phase)
                        && Splashes.Length < MaxSources)
                        Splashes.Add(new float4(at, -radius, 0.035f * math.min(s.Speed, 2.5f) * size));
                    // A splash where it has just come down through the surface (a jump, a fall, a fish breaching).
                    var fall = hadY ? (s.Position.y - lastY) / math.max(DeltaTime, 1e-3f) : 0f;
                    if (hadY && lastY - g.WaterSurface > 0.05f && -under <= 0.05f && fall < -1.5f && Splashes.Length < MaxSources)
                        Splashes.Add(new float4(at, -radius * 1.5f, math.clamp(-fall * 0.03f, 0.04f, 0.2f) * size));
                }
            }
        }

        EntityQuery focusQuery, creatureQuery;
        Mesh mesh;
        Material material;
        Texture2D mask;
        readonly byte[] maskBytes = new byte[Span * Span];
        /// <summary>How much the field shows at each of the square's metre corners (bilinear between): 0 on the edge of
        /// what the sheet covers, so it meets the chunk water there with no step, rising over the next metres in.</summary>
        Texture2D weight;
        readonly byte[] weightBytes = new byte[(Span + 1) * (Span + 1)];
        readonly int[] weightSteps = new int[(Span + 1) * (Span + 1)];
        RenderTexture fieldA, fieldB;
        ComputeShader compute;
        int kernel = -1;
        ComputeBuffer sourceBuffer;
        readonly float4[] sourceArray = new float4[MaxSources];
        float lastSourceTime;
        NativeHashMap<Entity, float3> lastAt;
        NativeList<ParentQuad> quads;
        readonly List<Mesh> gatherMeshes = new List<Mesh>(4);
        readonly List<int2> gatherOrigins = new List<int2>(4);

        /// <summary>The square on show (its corner; NoCorner while none), and the chunk builds its sheet was cut from.</summary>
        static readonly int2 NoCorner = new int2(int.MinValue, int.MinValue);
        int2 shownCorner = NoCorner;
        int shownBuilds;
        Bounds shownBounds;
        /// <summary>What the sheet on show was cut for: the player's cell, the camera's distance from them, the Detail.</summary>
        int2 shownFocus;
        float shownCamera, shownDetail;
        /// <summary>A sheet being cut (applied next frame): its square, builds, bounds and what it was cut for.</summary>
        JobHandle cutting;
        bool cutPending;
        Mesh.MeshDataArray cutData;
        int2 cutCorner;
        int cutBuilds;
        Bounds cutBounds;
        int2 cutFocus;
        float cutCamera, cutDetail;
        int cutIndexCount;
        NativeArray<byte> cutGrid;
        NativeArray<int2> cutStarts;
        /// <summary>Each cell's cut tier last time (+1; 0 none), from the square at tierCorner: hysteresis keeps a cell from
        /// flicking between two cuts as the player moves to and fro across a boundary.</summary>
        readonly byte[] tiers = new byte[Span * Span], nextTiers = new byte[Span * Span];
        int2 tierCorner = NoCorner;
        /// <summary>Indices in the sheet on show (for probes and the R-9 bench).</summary>
        public int SheetIndices { get; private set; }
        /// <summary>The field's corner (texels follow the square), texels it still has to move, and time not yet stepped.</summary>
        int2 fieldCorner = NoCorner;
        int2 pendingShift;
        /// <summary>The square corner the field's texels are laid out for: fieldCorner once the pending shift is stepped in.
        /// The shaders read the field by this one, so between steps (a move waits for the next one, not forcing its own)
        /// the surface still lines up; it only trails the square by up to a step.</summary>
        int2 texelCorner;
        float stepClock;
        bool fieldClear;

        protected override void OnCreate()
        {
            focusQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainFocus>(), ComponentType.ReadOnly<LocalTransform>());
            creatureQuery = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            lastAt = new NativeHashMap<Entity, float3>(64, Allocator.Persistent);
            quads = new NativeList<ParentQuad>(1024, Allocator.Persistent);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-waterfield");
            if (at >= 0 && at + 1 < args.Length && args[at + 1] == "0")
                On = false;
            Shader.SetGlobalVector(RectId, NoRect);
            CreateFall();
        }

        protected override void OnDestroy()
        {
            FinishCut(false);
            DestroyFall();
            Shader.SetGlobalVector(RectId, NoRect);
            if (lastAt.IsCreated) lastAt.Dispose();
            if (quads.IsCreated) quads.Dispose();
            sourceBuffer?.Release();
            if (fieldA != null) fieldA.Release();
            if (fieldB != null) fieldB.Release();
            Object.Destroy(fieldA);
            Object.Destroy(fieldB);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(mask);
            Object.Destroy(weight);
        }

        protected override void OnUpdate()
        {
            var enabled = On && Punchy.Settings.WaterQuality.Reflection != Punchy.Settings.ReflectionQuality.Low &&
                          SystemAPI.HasSingleton<TerrainConfig>() && SystemAPI.HasSingleton<TerrainSource>();
            var streaming = World.GetExistingSystemManaged<TerrainStreamingSystem>();
            if (!enabled || streaming == null || !Setup())
            {
                Hide();
                HideFall();
                return;
            }
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            FinishCut(true);
            FinishFallCut(true);
            var cam = Camera.main;
            Punchy.Settings.DetailPolicy.SetView(cam);
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            UpdateFall(streaming, config, cam != null, view);
            if (!TryFocus(out var focus))
            {
                Hide();
                return;
            }

            // The square round the player, and whether the chunk water under it is all fine (LOD0) and loaded.
            var corner = (int2)math.floor((focus.xz - Span * 0.5f) / Snap) * Snap;
            var builds = 17;
            var ready = Gather(streaming, config, corner, Span, ref builds, false);
            if (!ready)
            {
                Hide();
                return;
            }
            // Recut when the square moves, a chunk under it is rebuilt, or the cut no longer fits the view: the player has
            // moved a cell on, the camera has come much nearer or gone much further, or the Detail setting changed (or a
            // fall's sheet took cells from it or gave them back).
            var focusCell = (int2)math.floor(focus.xz / FineStep) * FineStep;
            var camera = cam != null ? math.max(math.distance(view.Camera, focus), 1f) : 8f;
            var refit = !focusCell.Equals(shownFocus) || camera > shownCamera * CameraBand || camera * CameraBand < shownCamera ||
                        view.Detail != shownDetail || fallMaskChanged;
            if (!cutPending && (!corner.Equals(shownCorner) || builds != shownBuilds || refit))
                StartCut(streaming, config, corner, builds, focus, focusCell, camera, view);

            if (shownCorner.Equals(NoCorner))
                return;
            StepField();
            Graphics.RenderMesh(new RenderParams(material)
            {
                worldBounds = shownBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                layer = 0
            }, mesh, 0, Matrix4x4.identity);
        }

        bool TryFocus(out float3 focus)
        {
            focus = default;
            if (!focusQuery.IsEmpty)
            {
                focus = focusQuery.GetSingleton<LocalTransform>().Position;
                return true;
            }
            var cam = Camera.main;
            if (cam == null)
                return false;
            focus = cam.transform.position;
            return true;
        }

        /// <summary>Makes the sheet's mesh, material, mask, field and compute once (false if the water material or the
        /// compute isn't there).</summary>
        bool Setup()
        {
            if (material != null)
                return true;
            var water = SystemAPI.GetSingleton<TerrainConfig>().WaterMaterial.Value;
            compute = compute != null ? compute : Resources.Load<ComputeShader>("WaterField");
            if (water == null || compute == null || !SystemInfo.supportsComputeShaders)
                return false;
            kernel = compute.FindKernel("Step");
            material = new Material(water) { name = "Water (fine sheet)" };
            material.EnableKeyword("_WATER_FIELD");
            mesh = new Mesh { name = "Water fine sheet" };
            mesh.MarkDynamic();
            mask = new Texture2D(Span, Span, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Water fine sheet mask" };
            weight = new Texture2D(Span + 1, Span + 1, TextureFormat.R8, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Water fine sheet weight" };
            fieldA = MakeField("Water field A");
            fieldB = MakeField("Water field B");
            sourceBuffer = new ComputeBuffer(MaxSources, 16);
            fieldClear = false;
            return true;
        }

        static RenderTexture MakeField(string name)
        {
            var rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
            {
                name = name, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        /// <summary>Stops drawing the sheet (the chunk water draws everything again) and forgets the field.</summary>
        void Hide()
        {
            if (shownCorner.Equals(NoCorner) && !cutPending)
                return;
            FinishCut(false);
            shownCorner = NoCorner;
            fieldCorner = NoCorner;
            fieldClear = false;
            Shader.SetGlobalVector(RectId, NoRect);
        }

        /// <summary>
        /// The chunks under the square: whether they're all loaded and those with water fine (LOD0), and a fingerprint of
        /// their builds (a rebuilt chunk recuts the sheet). With <paramref name="collect"/>, also lists their water meshes.
        /// </summary>
        bool Gather(TerrainStreamingSystem streaming, TerrainConfig config, int2 corner, int2 extent, ref int builds, bool collect)
        {
            if (collect)
            {
                gatherMeshes.Clear();
                gatherOrigins.Clear();
            }
            var size = config.ChunkSize;
            var lo = (int2)math.floor((float2)corner / size);
            var hi = (int2)math.floor((float2)(corner + extent - 1) / size);
            var vertices = (config.Lod0Quads + 1) * (config.Lod0Quads + 1);
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var coord = new int2(x, z);
                var hasWater = streaming.TryGetWater(coord, out var water, out var lod, out var built);
                if (lod < 0)
                    return false;
                builds = builds * 31 + built;
                if (!hasWater || water == null)
                    continue;
                if (lod != 0 || water.vertexCount != vertices)
                    return false;
                if (collect)
                {
                    gatherMeshes.Add(water);
                    gatherOrigins.Add(coord * (int)size);
                }
            }
            return true;
        }

        /// <summary>Copies the wet quads in the square (Burst, now; it's a scan of a few index lists) and starts cutting
        /// them into the fine sheet, applied next frame.</summary>
        void StartCut(TerrainStreamingSystem streaming, TerrainConfig config, int2 corner, int builds, float3 focus, int2 focusCell,
            float camera, Punchy.Settings.DetailPolicy.View view)
        {
            var check = 17;
            Gather(streaming, config, corner, Span, ref check, true);
            quads.Clear();
            if (gatherMeshes.Count > 0)
            {
                using var read = Mesh.AcquireReadOnlyMeshData(gatherMeshes);
                for (int m = 0; m < read.Length; m++)
                    new GatherJob
                    {
                        Vertices = read[m].GetVertexData<WaterVertex>(), Indices = read[m].GetIndexData<uint>(),
                        Row = config.Lod0Quads + 1, Origin = gatherOrigins[m], RectMin = corner, RectMax = corner + Span, Quads = quads
                    }.Run();
            }
            float lowY = float.MaxValue, highY = float.MinValue;
            for (int i = 0; i < quads.Length; i++)
            {
                var q = quads[i];
                lowY = math.min(lowY, math.min(math.min(q.A.Position.y, q.B.Position.y), math.min(q.C.Position.y, q.D.Position.y)));
                highY = math.max(highY, math.max(math.max(q.A.Position.y, q.B.Position.y), math.max(q.C.Position.y, q.D.Position.y)));
            }
            if (quads.Length == 0)
                lowY = highY = 0f;
            var pad = WaterSwells.BoundsPad + new float3(0f, 0.5f, 0f);
            var lo = new float3(corner.x, lowY, corner.y) - pad;
            var hi = new float3(corner.x + Span, highY, corner.y + Span) + pad;
            cutBounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            cutCorner = corner;
            cutBuilds = builds;
            cutFocus = focusCell;
            cutCamera = camera;
            cutDetail = view.Detail;
            fallMaskChanged = false;

            // Each quad's cut, by how big a metre of water looks there (rule 20): its distance taken as the camera's
            // distance from the player and the quad's from the player together, so the cut doesn't change as the camera
            // swings round. Tiers last time hold a cell back from coarsening until it is well past the boundary.
            var thresholds = Punchy.Settings.DetailPolicy.WaterSheet.Scaled(math.max(view.Detail, 0.1f));
            cutGrid = new NativeArray<byte>(Span * Span, Allocator.TempJob);
            cutStarts = new NativeArray<int2>(quads.Length, Allocator.TempJob);
            System.Array.Clear(maskBytes, 0, maskBytes.Length);
            System.Array.Clear(nextTiers, 0, nextTiers.Length);
            // (With the camera pulled in close, say swimming, everything near looks big enough for the finest cut: past
            // the budget, the cut is redone as if the water looked smaller, a tenth at a time, until it fits; the finest
            // cut shrinks in towards the player, where the dents are, but stays.)
            var budget = (int)(SheetBudget * math.max(view.Detail, 0.1f));
            var shrink = 1f;
            for (int attempt = 0; attempt < 20; attempt++, shrink *= 0.9f)
            {
                var total = 0;
                for (int i = 0; i < quads.Length; i++)
                {
                    var cell = quads[i].Cell;
                    var reach = math.lengthsq((float2)cell + 0.5f - focus.xz);
                    var pixels = Punchy.Settings.DetailPolicy.ScreenSize(Punchy.Settings.DetailPolicy.WaterSheetSize,
                        math.sqrt(reach + camera * camera), view.PixelsPerMetre);
                    var was = cell - tierCorner;
                    var previous = tierCorner.Equals(NoCorner) || math.any(was < 0) || math.any(was >= Span) ? -1 : tiers[was.y * Span + was.x] - 1;
                    var tier = Punchy.Settings.DetailPolicy.Tier(pixels * shrink, thresholds, previous);
                    // (A cell a fall's sheet covers is the fall's: left whole here, so the edge meets it as it meets the
                    // chunk water.)
                    if (InFall(cell))
                        tier = 3;
                    var c = cell - corner;
                    nextTiers[c.y * Span + c.x] = (byte)(tier + 1);
                    var n = Cuts >> tier;
                    if (n > 1)
                        total += n * n * 6;
                }
                if (total <= budget)
                    break;
            }
            int vertexCount = 0, indexCount = 0, kept = 0;
            for (int i = 0; i < quads.Length; i++)
            {
                var c = quads[i].Cell - corner;
                var n = Cuts >> (nextTiers[c.y * Span + c.x] - 1);
                cutGrid[c.y * Span + c.x] = (byte)n;
                // (A quad left whole is the chunk water's own: the chunk water draws it, so the sheet covers only what it
                // cuts, a few metres round the player, and adds a fraction of the lake's indices rather than all of them
                // over again: job 45, R-9 row 4.)
                if (n <= 1)
                    continue;
                maskBytes[c.y * Span + c.x] = 255;
                quads[kept] = quads[i];
                cutStarts[kept++] = new int2(vertexCount, indexCount);
                vertexCount += (n + 1) * (n + 1);
                indexCount += n * n * 6;
            }
            quads.Length = kept;
            Weigh();
            System.Array.Copy(nextTiers, tiers, tiers.Length);
            tierCorner = corner;
            cutIndexCount = indexCount;

            cutData = Mesh.AllocateWritableMeshData(1);
            var data = cutData[0];
            data.SetVertexBufferParams(vertexCount, Layout);
            data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            cutting = new CutJob
            {
                Quads = quads.AsArray(), Grid = cutGrid, Starts = cutStarts, RectMin = corner, Width = Span, Rows = cutGrid,
                Vertices = data.GetVertexData<PatchVertex>(), Indices = data.GetIndexData<uint>()
            }.Schedule(quads.Length, 16);
            cutPending = true;
        }

        /// <summary>The field's weight at each corner (weightBytes): 0 at a corner touching a cell the sheet doesn't
        /// cover (the chunk water's, dry, or past the square), then up by 0.4 a step (8-way) further in.</summary>
        void Weigh()
        {
            const int row = Span + 1, far = 3;
            for (int z = 0; z < row; z++)
            for (int x = 0; x < row; x++)
            {
                var edge = false;
                for (int dz = -1; dz <= 0 && !edge; dz++)
                for (int dx = -1; dx <= 0 && !edge; dx++)
                {
                    int cx = x + dx, cz = z + dz;
                    edge = cx < 0 || cz < 0 || cx >= Span || cz >= Span || maskBytes[cz * Span + cx] == 0;
                }
                weightSteps[z * row + x] = edge ? 0 : far;
            }
            for (int pass = 0; pass < far - 1; pass++)
            for (int z = 0; z < row; z++)
            for (int x = 0; x < row; x++)
            {
                var d = weightSteps[z * row + x];
                if (d == 0)
                    continue;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < row && nz < row)
                        d = math.min(d, weightSteps[nz * row + nx] + 1);
                }
                weightSteps[z * row + x] = d;
            }
            for (int i = 0; i < weightBytes.Length; i++)
                weightBytes[i] = (byte)math.round(math.saturate(weightSteps[i] / 2.5f) * 255f);
        }

        /// <summary>Puts last frame's cut sheet on show (or, with <paramref name="apply"/> false, drops it), with its
        /// square and mask, and moves the field along with it.</summary>
        void FinishCut(bool apply)
        {
            if (!cutPending)
                return;
            cutting.Complete();
            cutPending = false;
            cutGrid.Dispose();
            cutStarts.Dispose();
            if (!apply || mesh == null)
            {
                cutData.Dispose();
                tierCorner = NoCorner;
                return;
            }
            var data = cutData[0];
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, cutIndexCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.ApplyAndDisposeWritableMeshData(cutData, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = cutBounds;
            shownBounds = cutBounds;
            shownCorner = cutCorner;
            shownBuilds = cutBuilds;
            shownFocus = cutFocus;
            shownCamera = cutCamera;
            shownDetail = cutDetail;
            SheetIndices = cutIndexCount;
            mask.SetPixelData(maskBytes, 0);
            mask.Apply(false, false);
            Shader.SetGlobalTexture(MaskId, mask);
            weight.SetPixelData(weightBytes, 0);
            weight.Apply(false, false);
            Shader.SetGlobalTexture(WeightId, weight);
            Shader.SetGlobalVector(RectId, new Vector4(cutCorner.x, cutCorner.y, cutCorner.x + Span, cutCorner.y + Span));
            // (The field's texels move with the square, whole texels at a time; the shader's corner moves with them.)
            if (!fieldCorner.Equals(NoCorner))
                pendingShift += (cutCorner - fieldCorner) * (int)TexelsPerMetre;
            fieldCorner = cutCorner;
        }

        /// <summary>Every 5 s: how many steps were dispatched and skipped (asleep, or -waterfieldstep 0), and the sources.</summary>
        void LogField()
        {
            logClock += UnityEngine.Time.unscaledDeltaTime;
            if (logClock < 5f)
                return;
            Debug.Log($"WaterField: {dispatched} steps dispatched, {skipped} skipped, {(float)sourceSum / math.max(1, dispatched + skipped):F1} sources a step, " +
                      $"{(asleep ? "asleep" : "awake")} (sleep {SleepOn}, cull {CullOn}, step {StepOn}, cuts {Cuts}, {1f / StepTime:F0} Hz), sheet {(mesh != null && mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) : 0)} indices");
            logClock = 0f;
            dispatched = skipped = sourceSum = 0;
        }

        /// <summary>Steps the wave field at 60 steps a second (at least once if it has moved, so its texels and the
        /// square stay together), pressed by this frame's creatures.</summary>
        void StepField()
        {
            if (!fieldClear)
            {
                var active = RenderTexture.active;
                RenderTexture.active = fieldA;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = fieldB;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = active;
                fieldClear = true;
                pendingShift = int2.zero;
                texelCorner = fieldCorner;
            }
            var dt = UnityEngine.Time.deltaTime;
            stepClock = math.min(stepClock + dt, StepTime * MaxSteps);
            var steps = (int)(stepClock / StepTime);
            stepClock -= steps * StepTime;
            // (A move used to force a step of its own, so at 30 steps a second a swimmer still stepped about every frame:
            // the shift now waits for the next scheduled step, and texelCorner keeps the shaders lined up meanwhile.)
            if (steps > 0)
            {
                var presses = new NativeList<float4>(16, Allocator.TempJob);
                var splashes = new NativeList<float4>(4, Allocator.TempJob);
                var now = (float)SystemAPI.Time.ElapsedTime;
                CreatureSnapshotSystem.Get(World, creatureQuery, out var states, out var bodies, out var entities);
                new SourceJob
                {
                    States = states, Bodies = bodies, Entities = entities, Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                    Corner = fieldCorner, DeltaTime = math.max(dt, StepTime), Time = now, LastTime = lastSourceTime, LastAt = lastAt, Presses = presses, Splashes = splashes
                }.Run();
                CreatureSnapshotSystem.Release(World, states, bodies, entities);
                lastSourceTime = now;
                if (lastAt.Count > 2000)
                    lastAt.Clear();
                // (Dimples first, splashes after: later steps leave the splashes out, as they knock the surface once.)
                var pressCount = presses.Length;
                var count = math.min(MaxSources, pressCount + splashes.Length);
                for (int i = 0; i < count; i++)
                    sourceArray[i] = i < pressCount ? presses[i] : splashes[i - pressCount];
                presses.Dispose();
                splashes.Dispose();
                if (count > 0)
                    sourceBuffer.SetData(sourceArray, 0, 0, count);

                sourceSum += count;
                quietSteps = count > 0 ? 0 : quietSteps + steps;
                if (count > 0)
                    asleep = false;
                else if (SleepOn && quietSteps >= SleepAfter && !asleep)
                {
                    // Nothing has pressed it for 10 s: what's left is a few mm settling. Clear it flat and stop stepping.
                    var active = RenderTexture.active;
                    RenderTexture.active = fieldA;
                    GL.Clear(false, true, Color.clear);
                    RenderTexture.active = fieldB;
                    GL.Clear(false, true, Color.clear);
                    RenderTexture.active = active;
                    asleep = true;
                }
                if (asleep || !StepOn)
                {
                    skipped += steps;
                    steps = 0;
                }
                else
                    dispatched += steps;

                compute.SetInt(SizeId, Size);
                compute.SetInt(CullId, CullOn ? 1 : 0);
                var courant = WaveSpeed * StepTime * TexelsPerMetre;
                compute.SetFloat(CourantId, courant * courant);
                // (Tuned per step at 60 a second; per step at another rate so a second's worth stays the same.)
                compute.SetFloat(DampingId, math.pow(0.994f, StepTime * 60f));
                compute.SetFloat(DecayId, math.pow(0.999f, StepTime * 60f));
                compute.SetBuffer(kernel, SourcesId, sourceBuffer);
                for (int s = 0; s < steps; s++)
                {
                    compute.SetInts(ShiftId, s == 0 ? pendingShift.x : 0, s == 0 ? pendingShift.y : 0);
                    compute.SetInt(SourceCountId, s == 0 ? count : math.min(count, pressCount));
                    compute.SetTexture(kernel, SrcId, fieldA);
                    compute.SetTexture(kernel, DstId, fieldB);
                    compute.Dispatch(kernel, Size / 8, Size / 8, 1);
                    (fieldA, fieldB) = (fieldB, fieldA);
                }
                pendingShift = int2.zero;
                texelCorner = fieldCorner;
            }
            // (Every frame, not only the ones that step: a 5 s log window over stepping frames alone spanned ~10 s at 20 Hz.)
            LogField();
            Shader.SetGlobalTexture(FieldId, fieldA);
            Shader.SetGlobalVector(FieldAtId, new Vector4(texelCorner.x, texelCorner.y, TexelsPerMetre, Size));
        }
    }
}
