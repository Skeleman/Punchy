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
    /// steps) this copies the chunk water's wet quads and cuts each into 8 x 8 (12.5 cm), drawn in the same water
    /// material with _WATER_FIELD on (WaterField.hlsl); the chunk water clips itself out of the cells it covers. Over the
    /// same square it steps a wave field (WaterField.compute, 256 x 256 texels, 60 steps a second): every creature at the
    /// surface there (CreatureWakeSystem's choice: swimmers, floaters, animals crossing, folk, players' bodies) presses a
    /// dimple into it, deeper the faster it goes, so it pushes a bow wave and trails a wake, and anything dropping into
    /// the water knocks out a splash ring. The fine sheet's vertices rise and fall by the field, and its normals follow.
    /// Outside the square, on Low water quality or with "-waterfield 0", the swimmer ripples carry on alone.
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
        /// <summary>Each chunk water quad is cut into Cuts x Cuts.</summary>
        const int Cuts = 8, QuadVertices = (Cuts + 1) * (Cuts + 1), QuadIndices = Cuts * Cuts * 6;
        /// <summary>The field's step (s), the most steps a frame catches up, and how fast its waves run (m/s): slower than
        /// a walk, so a wader trails a V rather than pushing a ring ahead of it.</summary>
        const float StepTime = 1f / 60f, WaveSpeed = 0.9f;
        const int MaxSteps = 4, MaxSources = 64;

        static readonly int FieldId = Shader.PropertyToID("_PunchyWaterField");
        static readonly int FieldAtId = Shader.PropertyToID("_PunchyWaterFieldAt");
        static readonly int RectId = Shader.PropertyToID("_PunchyWaterPatchRect");
        static readonly int MaskId = Shader.PropertyToID("_PunchyWaterPatchMask");
        static readonly int SrcId = Shader.PropertyToID("_Src");
        static readonly int DstId = Shader.PropertyToID("_Dst");
        static readonly int SourcesId = Shader.PropertyToID("_Sources");
        static readonly int SourceCountId = Shader.PropertyToID("_SourceCount");
        static readonly int ShiftId = Shader.PropertyToID("_Shift");
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int CourantId = Shader.PropertyToID("_Courant2");
        static readonly int DampingId = Shader.PropertyToID("_Damping");
        static readonly int DecayId = Shader.PropertyToID("_Decay");
        static readonly Vector4 NoRect = new Vector4(1e9f, 1e9f, -1e9f, -1e9f);

        /// <summary>A fine sheet vertex: the chunk water's (WaterVertex) plus where it sits on the square's edge.</summary>
        struct PatchVertex
        {
            public float3 Position;
            public float4 Data, Look;
            public float2 Glow;
            public float4 Parent;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
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

        /// <summary>Cuts each quad into Cuts x Cuts, split along the same diagonal as the chunk water (B to C), so the fine
        /// sheet lies where the chunk water did.</summary>
        [BurstCompile]
        struct CutJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ParentQuad> Quads;
            public int2 RectMin, RectMax;
            // (Both come from one MeshData, which the safety system takes for the same container twice.)
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<PatchVertex> Vertices;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<uint> Indices;

            static float4 Mix(float4 a, float4 b, float4 c, float4 d, float u, float v) =>
                u + v <= 1f ? a + u * (b - a) + v * (c - a) : d + (1f - u) * (c - d) + (1f - v) * (b - d);

            public void Execute(int index)
            {
                var q = Quads[index];
                var first = index * QuadVertices;
                bool west = q.Cell.x == RectMin.x, east = q.Cell.x + 1 == RectMax.x;
                bool south = q.Cell.y == RectMin.y, north = q.Cell.y + 1 == RectMax.y;
                for (int j = 0; j <= Cuts; j++)
                for (int i = 0; i <= Cuts; i++)
                {
                    float u = i / (float)Cuts, v = j / (float)Cuts;
                    var p = Mix(new float4(q.A.Position, 0), new float4(q.B.Position, 0), new float4(q.C.Position, 0), new float4(q.D.Position, 0), u, v);
                    var g = Mix(new float4(q.A.Glow, 0, 0), new float4(q.B.Glow, 0, 0), new float4(q.C.Glow, 0, 0), new float4(q.D.Glow, 0, 0), u, v);
                    // On the square's edge (not at a chunk water corner, which is exact anyway): the edge's two corners'
                    // depths, how far along it, and which way it runs (WaterField.hlsl, PunchyWaterPatchVertex).
                    var parent = float4.zero;
                    if (j % Cuts != 0 && (i == 0 && west || i == Cuts && east))
                        parent = i == 0 ? new float4(q.A.Data.z, q.C.Data.z, v, 1f) : new float4(q.B.Data.z, q.D.Data.z, v, 1f);
                    else if (i % Cuts != 0 && (j == 0 && south || j == Cuts && north))
                        parent = j == 0 ? new float4(q.A.Data.z, q.B.Data.z, u, 2f) : new float4(q.C.Data.z, q.D.Data.z, u, 2f);
                    Vertices[first + j * (Cuts + 1) + i] = new PatchVertex
                    {
                        Position = p.xyz,
                        Data = Mix(q.A.Data, q.B.Data, q.C.Data, q.D.Data, u, v),
                        Look = Mix(q.A.Look, q.B.Look, q.C.Look, q.D.Look, u, v),
                        Glow = g.xy,
                        Parent = parent
                    };
                }
                var t = index * QuadIndices;
                for (int j = 0; j < Cuts; j++)
                for (int i = 0; i < Cuts; i++)
                {
                    var a = (uint)(first + j * (Cuts + 1) + i);
                    uint b = a + 1, c = a + Cuts + 1, d = c + 1;
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
            /// <summary>Each creature's height last frame (to see it drop through the surface).</summary>
            public NativeHashMap<Entity, float> LastY;
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
                    var hadY = LastY.TryGetValue(e, out var lastY);
                    LastY[e] = s.Position.y;
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
        RenderTexture fieldA, fieldB;
        ComputeShader compute;
        int kernel = -1;
        ComputeBuffer sourceBuffer;
        readonly float4[] sourceArray = new float4[MaxSources];
        float lastSourceTime;
        NativeHashMap<Entity, float> lastY;
        NativeList<ParentQuad> quads;
        readonly List<Mesh> gatherMeshes = new List<Mesh>(4);
        readonly List<int2> gatherOrigins = new List<int2>(4);

        /// <summary>The square on show (its corner; NoCorner while none), and the chunk builds its sheet was cut from.</summary>
        static readonly int2 NoCorner = new int2(int.MinValue, int.MinValue);
        int2 shownCorner = NoCorner;
        int shownBuilds;
        Bounds shownBounds;
        /// <summary>A sheet being cut (applied next frame): its square, builds and bounds.</summary>
        JobHandle cutting;
        bool cutPending;
        Mesh.MeshDataArray cutData;
        int2 cutCorner;
        int cutBuilds;
        Bounds cutBounds;
        /// <summary>The field's corner (texels follow the square), texels it still has to move, and time not yet stepped.</summary>
        int2 fieldCorner = NoCorner;
        int2 pendingShift;
        float stepClock;
        bool fieldClear;

        protected override void OnCreate()
        {
            focusQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainFocus>(), ComponentType.ReadOnly<LocalTransform>());
            creatureQuery = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            lastY = new NativeHashMap<Entity, float>(64, Allocator.Persistent);
            quads = new NativeList<ParentQuad>(1024, Allocator.Persistent);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-waterfield");
            if (at >= 0 && at + 1 < args.Length && args[at + 1] == "0")
                On = false;
            Shader.SetGlobalVector(RectId, NoRect);
        }

        protected override void OnDestroy()
        {
            FinishCut(false);
            Shader.SetGlobalVector(RectId, NoRect);
            if (lastY.IsCreated) lastY.Dispose();
            if (quads.IsCreated) quads.Dispose();
            sourceBuffer?.Release();
            if (fieldA != null) fieldA.Release();
            if (fieldB != null) fieldB.Release();
            Object.Destroy(fieldA);
            Object.Destroy(fieldB);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(mask);
        }

        protected override void OnUpdate()
        {
            var enabled = On && Punchy.Settings.WaterQuality.Reflection != Punchy.Settings.ReflectionQuality.Low &&
                          SystemAPI.HasSingleton<TerrainConfig>() && SystemAPI.HasSingleton<TerrainSource>();
            var streaming = World.GetExistingSystemManaged<TerrainStreamingSystem>();
            if (!enabled || streaming == null || !TryFocus(out var focus) || !Setup())
            {
                Hide();
                return;
            }
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            FinishCut(true);

            // The square round the player, and whether the chunk water under it is all fine (LOD0) and loaded.
            var corner = (int2)math.floor((focus.xz - Span * 0.5f) / Snap) * Snap;
            var builds = 17;
            var ready = Gather(streaming, config, corner, ref builds, false);
            if (!ready)
            {
                Hide();
                return;
            }
            if (!cutPending && (!corner.Equals(shownCorner) || builds != shownBuilds))
                StartCut(streaming, config, corner, builds);

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
        bool Gather(TerrainStreamingSystem streaming, TerrainConfig config, int2 corner, ref int builds, bool collect)
        {
            if (collect)
            {
                gatherMeshes.Clear();
                gatherOrigins.Clear();
            }
            var size = config.ChunkSize;
            var lo = (int2)math.floor((float2)corner / size);
            var hi = (int2)math.floor((float2)(corner + Span - 1) / size);
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
        void StartCut(TerrainStreamingSystem streaming, TerrainConfig config, int2 corner, int builds)
        {
            var check = 17;
            Gather(streaming, config, corner, ref check, true);
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
            System.Array.Clear(maskBytes, 0, maskBytes.Length);
            for (int i = 0; i < quads.Length; i++)
            {
                var q = quads[i];
                lowY = math.min(lowY, math.min(math.min(q.A.Position.y, q.B.Position.y), math.min(q.C.Position.y, q.D.Position.y)));
                highY = math.max(highY, math.max(math.max(q.A.Position.y, q.B.Position.y), math.max(q.C.Position.y, q.D.Position.y)));
                var c = q.Cell - corner;
                maskBytes[c.y * Span + c.x] = 255;
            }
            if (quads.Length == 0)
                lowY = highY = 0f;
            var pad = WaterSwells.BoundsPad + new float3(0f, 0.5f, 0f);
            var lo = new float3(corner.x, lowY, corner.y) - pad;
            var hi = new float3(corner.x + Span, highY, corner.y + Span) + pad;
            cutBounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            cutCorner = corner;
            cutBuilds = builds;

            cutData = Mesh.AllocateWritableMeshData(1);
            var data = cutData[0];
            data.SetVertexBufferParams(quads.Length * QuadVertices, Layout);
            data.SetIndexBufferParams(quads.Length * QuadIndices, IndexFormat.UInt32);
            cutting = new CutJob
            {
                Quads = quads.AsArray(), RectMin = corner, RectMax = corner + Span,
                Vertices = data.GetVertexData<PatchVertex>(), Indices = data.GetIndexData<uint>()
            }.Schedule(quads.Length, 16);
            cutPending = true;
        }

        /// <summary>Puts last frame's cut sheet on show (or, with <paramref name="apply"/> false, drops it), with its
        /// square and mask, and moves the field along with it.</summary>
        void FinishCut(bool apply)
        {
            if (!cutPending)
                return;
            cutting.Complete();
            cutPending = false;
            if (!apply || mesh == null)
            {
                cutData.Dispose();
                return;
            }
            var data = cutData[0];
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, quads.Length * QuadIndices), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.ApplyAndDisposeWritableMeshData(cutData, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = cutBounds;
            shownBounds = cutBounds;
            shownCorner = cutCorner;
            shownBuilds = cutBuilds;
            mask.SetPixelData(maskBytes, 0);
            mask.Apply(false, false);
            Shader.SetGlobalTexture(MaskId, mask);
            Shader.SetGlobalVector(RectId, new Vector4(cutCorner.x, cutCorner.y, cutCorner.x + Span, cutCorner.y + Span));
            // (The field's texels move with the square, whole texels at a time; the shader's corner moves with them.)
            if (!fieldCorner.Equals(NoCorner))
                pendingShift += (cutCorner - fieldCorner) * (int)TexelsPerMetre;
            fieldCorner = cutCorner;
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
            }
            var dt = UnityEngine.Time.deltaTime;
            stepClock = math.min(stepClock + dt, StepTime * MaxSteps);
            var steps = (int)(stepClock / StepTime);
            stepClock -= steps * StepTime;
            if (steps == 0 && !pendingShift.Equals(int2.zero))
                steps = 1;
            if (steps > 0)
            {
                var presses = new NativeList<float4>(16, Allocator.TempJob);
                var splashes = new NativeList<float4>(4, Allocator.TempJob);
                var now = (float)SystemAPI.Time.ElapsedTime;
                CreatureSnapshotSystem.Get(World, creatureQuery, out var states, out var bodies, out var entities);
                new SourceJob
                {
                    States = states, Bodies = bodies, Entities = entities, Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                    Corner = fieldCorner, DeltaTime = math.max(dt, StepTime), Time = now, LastTime = lastSourceTime, LastY = lastY, Presses = presses, Splashes = splashes
                }.Run();
                CreatureSnapshotSystem.Release(World, states, bodies, entities);
                lastSourceTime = now;
                if (lastY.Count > 2000)
                    lastY.Clear();
                // (Dimples first, splashes after: later steps leave the splashes out, as they knock the surface once.)
                var pressCount = presses.Length;
                var count = math.min(MaxSources, pressCount + splashes.Length);
                for (int i = 0; i < count; i++)
                    sourceArray[i] = i < pressCount ? presses[i] : splashes[i - pressCount];
                presses.Dispose();
                splashes.Dispose();
                if (count > 0)
                    sourceBuffer.SetData(sourceArray, 0, 0, count);

                compute.SetInt(SizeId, Size);
                var courant = WaveSpeed * StepTime * TexelsPerMetre;
                compute.SetFloat(CourantId, courant * courant);
                compute.SetFloat(DampingId, 0.994f);
                compute.SetFloat(DecayId, 0.999f);
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
            }
            Shader.SetGlobalTexture(FieldId, fieldA);
            Shader.SetGlobalVector(FieldAtId, new Vector4(fieldCorner.x, fieldCorner.y, TexelsPerMetre, Size));
        }
    }
}
