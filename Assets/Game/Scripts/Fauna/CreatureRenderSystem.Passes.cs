using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    // A-81 row 4: the frame's posing hand-off (Posed, Finish), pass timing, pose rate and the index, cull and
    // compact jobs, moved out of CreatureRenderSystem.cs as they were.
    public partial class CreatureRenderSystem
    {
        /// <summary>One frame's posing, scheduled and left to run until the next update (A-51, #62).</summary>
        struct Posed
        {
            public JobHandle Job;
            public NativeArray<SegmentInstance> Instances, Extras;
            public NativeArray<int> Offsets;
            public NativeArray<CreatureBody> Bodies;
            public NativeArray<CreatureState> States;
            public NativeArray<Entity> Entities;
            /// <summary>Per worker (StatStride apart): the pose job's ticks, creatures posed afresh, moved on (A-70 row 4).</summary>
            public NativeArray<long> Stats;
            /// <summary>Each creature's detail decision (DetailPolicy.Decision, packed): next frame's tiers start from it.</summary>
            public NativeArray<byte> Decisions;
            /// <summary>Where each draw group (DrawGroup: capsule detail x shadow) starts among the creatures' parts,
            /// and where the last ends (GroupCount + 1).</summary>
            public NativeArray<int> GroupStarts;
            public int ColonyStart, Total;

            public void Dispose()
            {
                Job.Complete();
                Instances.Dispose();
                Extras.Dispose();
                Offsets.Dispose();
                Bodies.Dispose();
                States.Dispose();
                Entities.Dispose();
                Stats.Dispose();
                Decisions.Dispose();
                GroupStarts.Dispose();
            }
        }

        Posed posed;
        bool havePosed;

        /// <summary>Last frame's posing let go undrawn (no camera or settings now, or the system stopping), and the one
        /// before it kept for its poses.</summary>
        void DropPosed()
        {
            if (havePosed)
            {
                havePosed = false;
                posed.Dispose();
            }
            if (havePrev)
            {
                havePrev = false;
                prevPosed.Dispose();
            }
        }

        protected override void OnStopRunning() => DropPosed();

        float cueLogSince = -1f;

        /// <summary>
        /// "-needcues", once a second (#64): how many creatures the brain gives a cue, and the nearest one's cue capsules
        /// as uploaded for the draw (centre, length, radii, glow), and whether they fall in the drawn range with any size.
        /// </summary>
        void LogCues(float3 camAt, NativeArray<SegmentInstance> instances, NativeArray<CreatureBody> bodies,
                     NativeArray<CreatureState> states, NativeArray<int> offsets, int colonyStart)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - cueLogSince < 1f)
                return;
            cueLogSince = now;
            int cued = 0, nearest = -1;
            var best = float.MaxValue;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Cue != 0)
                    cued++;
                var d = math.distancesq(states[i].Position, camAt);
                if (states[i].Mode != CreatureMode.Flee && d < best) { best = d; nearest = i; }
            }
            var line = $"[cues] creatures {states.Length}, brain cue != none {cued}, drawn range {colonyStart}";
            if (nearest >= 0)
            {
                line += $"; nearest {states[nearest].Individual} at {math.sqrt(best):0.0} m, cue {states[nearest].Cue}:";
                var first = offsets[nearest] + bodies[nearest].SegmentCount;
                for (int k = 0; k < MarkCount; k++)
                {
                    var m = instances[first + k];
                    var drawn = first + k < colonyStart && math.max(m.RadiusStart, m.RadiusEnd) > 0f;
                    line += $" [{k}] at ({m.Position.x:0.00},{m.Position.y:0.00},{m.Position.z:0.00}) len {m.Length:0.000} r {m.RadiusStart:0.000}/{m.RadiusEnd:0.000} a {m.Color.w:0.00} drawn {drawn}";
                }
            }
            Debug.Log(line);
        }

        /// <summary>
        /// Last frame's posing, finished: the rest (colonies, bees, small life, birds, traces) copied in after the
        /// creatures, the lanterns and torches lit, then uploaded and drawn.
        /// </summary>
        void FinishPosed(Camera cam, FaunaRenderSettings settings)
        {
            if (!havePosed)
                return;
            havePosed = false;
            var p = posed;
            p.Job.Complete();
            Pass(7);
            var instances = p.Instances;
            var bodies = p.Bodies;
            var states = p.States;
            var entities = p.Entities;
            var offsets = p.Offsets;
            var colonyStart = p.ColonyStart;
            var total = p.Total;
            // (Nothing else to draw, some frames: then there's nothing to copy, and no room after the creatures to copy to.)
            if (p.Extras.Length > 0)
                NativeArray<SegmentInstance>.Copy(p.Extras, 0, instances, colonyStart, p.Extras.Length);
            // Lanterns and torches light the ground round them: each requests a carried light from LightBudget (by its
            // carrier, so its fade-in carries on as it moves), which decides by its on-screen reach whether it gets a
            // glow slot (refactor 3.1), at the middle of a lantern's glass as just posed, or a torch's flame. A torch's
            // flame itself is the world's (FireSystem), lit each frame at the top of its handle, streaming back as its
            // bearer goes, within TorchFlameRange.
            float3 camAt = cam.transform.position;
            for (int i = 0; i < bodies.Length; i++)
            {
                var lightPart = LightPart(states[i].Held);
                var torch = states[i].Held == HeldItem.Torch;
                if (lightPart < 0)
                    continue;
                var part = instances[offsets[i] + bodies[i].SegmentCount + MarkCount + (InWeb(states[i]) ? WebCount : 0) +
                                     LairCount(ref bodies[i].Blob.Value) + (states[i].Task == CreatureTask.Hunt ? 1 : 0) + lightPart];
                if (part.Length <= 0f)
                    continue;
                float3 centre;
                if (torch)
                {
                    var top = part.Position + math.mul(part.Rotation, new float3(0f, 0f, part.Length));
                    var velocity = new float3(math.sin(states[i].Yaw), 0f, math.cos(states[i].Yaw)) * states[i].Speed;
                    var lean = -velocity * TorchFlameLean;
                    var leanLength = math.length(lean);
                    if (leanLength > TorchFlameMaxLean)
                        lean *= TorchFlameMaxLean / leanLength;
                    var size = TorchFlameSize * math.max(states[i].Scale, 0.3f);
                    if (math.distancesq(states[i].Position, camAt) <= TorchFlameRange * TorchFlameRange)
                        Punchy.Ambience.FireSystem.AddFlame(top, size, entities[i].Index, Punchy.Ambience.FireSystem.Wood, lean);
                    centre = top + new float3(0f, size * 0.4f, 0f);
                }
                else
                    centre = part.Position + math.mul(part.Rotation, new float3(0f, 0f, part.Length * 0.5f));
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Carried, entities[i].Index, centre, torch ? TorchLight : LanternLight);
            }
            Pass(8);
            if (ForceNeedCues)
                LogCues(camAt, instances, bodies, states, offsets, colonyStart);

            if (buffer == null || buffer.count < total)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(total), Marshal.SizeOf<SegmentInstance>());
            }
            buffer.SetData(instances);
            // (The pose job's time and counts, for -perflog.)
            long ticks = 0;
            for (int w = 0; w < p.Stats.Length; w += StatStride)
            {
                ticks += p.Stats[w];
                PoseEvaluated += p.Stats[w + 1];
                PoseReused += p.Stats[w + 2];
            }
            var ratio = Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio;
            PoseMs.Add(ticks * (double)ratio.Numerator / ratio.Denominator * 1e-6);
            PoseFrames++;
            if (PoseMs.Count > 100000)
                PoseMs.Clear();
            // Kept until this frame's posing is done: the creatures not due a fresh pose this frame move on from theirs.
            if (havePrev)
                prevPosed.Dispose();
            prevPosed = p;
            havePrev = true;

            // Draws from the one buffer: the creatures in their draw groups (refactor 3.7: capsule detail and shadow each
            // by how big the creature looks, decided apart, A-70 row 4 and A-82 row 3 before it); then the colonies and
            // traces after them, which cast no shadow (sleeping bats under a cave ceiling and prints on the ground gain
            // nothing from a shadow pass).
            var rp = new RenderParams(settings.Material)
            {
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 2000f),
                receiveShadows = true
            };
            groupProps ??= new MaterialPropertyBlock[GroupCount];
            for (int g = 0; g < GroupCount; g++)
            {
                int from = p.GroupStarts[g], to = p.GroupStarts[g + 1];
                if (to <= from)
                    continue;
                // (A capsule detail the settings leave out falls back to the next finer one.)
                var detail = g / 2;
                var mesh = detail == 2 && settings.CapsuleFar != null ? settings.CapsuleFar
                         : detail >= 1 && settings.CapsuleMid != null ? settings.CapsuleMid : settings.Capsule;
                var block = groupProps[g] ??= new MaterialPropertyBlock();
                block.SetBuffer(SegmentsId, buffer);
                block.SetInteger(SegmentOffsetId, from);
                rp.matProps = block;
                rp.shadowCastingMode = (g & 1) == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                Graphics.RenderMeshPrimitives(rp, mesh, 0, to - from);
                // Shells (A-92, #230; "-noshells" turns them off): the full-detail groups again as fur shells, no shadow (the shader
                // keeps only bodies within a few metres of the camera).
                if (Shells && detail == 0)
                {
                    shellProps ??= new MaterialPropertyBlock[2 * ShellLayers];
                    for (int layer = 1; layer <= ShellLayers; layer++)
                    {
                        var shell = shellProps[(g & 1) * ShellLayers + layer - 1] ??= new MaterialPropertyBlock();
                        shell.SetBuffer(SegmentsId, buffer);
                        shell.SetInteger(SegmentOffsetId, from);
                        shell.SetFloat(ShellId, layer);
                        rp.matProps = shell;
                        rp.shadowCastingMode = ShadowCastingMode.Off;
                        Graphics.RenderMeshPrimitives(rp, mesh, 0, to - from);
                    }
                }
            }
            if (total > colonyStart)
            {
                flatProps ??= new MaterialPropertyBlock();
                flatProps.SetBuffer(SegmentsId, buffer);
                flatProps.SetInteger(SegmentOffsetId, colonyStart);
                rp.matProps = flatProps;
                rp.shadowCastingMode = ShadowCastingMode.Off;
                Graphics.RenderMeshPrimitives(rp, settings.Capsule, 0, total - colonyStart);
            }
            Pass(9);
        }

        // ------------------------------------------------------------------------------- pass timing (A-22, #23)

        /// <summary>"-renderpasslog": logs this system's main-thread time per pass, averaged over about 3 s.</summary>
        public static bool PassLog;
        /// <summary>"-oldcull": the managed cull loop (before A-22), for before numbers from the same build.</summary>
        public static bool OldCull;
        /// <summary>A-70 row 4 A/B switch ("-oldposerate"): every kept creature posed afresh every frame and drawn with the
        /// full capsule, and no frustum cull.</summary>
        public static bool OldPoseRate;
        /// <summary>For -perflog: the pose job's time each frame (ms, summed over the workers), and the creatures posed
        /// afresh and moved on from last frame's pose since the log was last read.</summary>
        public static readonly System.Collections.Generic.List<double> PoseMs = new System.Collections.Generic.List<double>();
        public static long PoseEvaluated, PoseReused, PoseFrames;

        // ------------------------------------------------------------------------------- pose rate (A-70 row 4)

        /// <summary>
        /// Detail by how big a creature looks (refactor 3.7, DetailPolicy.Creature; distance bands of 40 / 100 m before,
        /// A-70 row 4), three answers decided apart:
        /// - Pose rate: posed every frame, every 2nd or every 4th (staggered by entity, so each frame takes an even
        ///   share). On the frames between, its last pose is moved and turned with it (PoseJob.Reuse).
        /// - Capsule: full, or fewer sides and rows (FaunaRenderSettings.CapsuleMid, CapsuleFar).
        /// - Shadow: cast while it looks big enough for one to show.
        /// A deer keeps today's 40 / 100 m at 60 degrees; a sky serpent stays smooth, detailed and shadowed as far out
        /// as it looks as big as a deer does there.
        /// The creatures are drawn in GroupCount groups: capsule detail (0-2) x shadow (on, off), group = detail x 2 +
        /// (shadow ? 0 : 1).
        /// </summary>
        const int GroupCount = 6;
        MaterialPropertyBlock[] groupProps;

        /// <summary>Shells (A-92, #230; off with "-noshells"): near bodies drawn again as ShellLayers fur shells.</summary>
        public static bool Shells = true;
        const int ShellLayers = 2;
        static readonly int ShellId = Shader.PropertyToID("_Shell");
        MaterialPropertyBlock[] shellProps;
        NativeArray<byte> noDecisions;
        /// <summary>The draw group for a decision.</summary>
        static int DrawGroup(Punchy.Settings.DetailPolicy.Decision d) => math.min(d.Geometry, 2) * 2 + (d.Shadow ? 0 : 1);
        /// <summary>Creatures nearer than this to the camera are never frustum culled (their shadows can fall in view, as
        /// with BehindCullRange); further ones are when wholly outside it by FrustumMargin and more for the huge.</summary>
        const float FrustumMargin = 6f;
        /// <summary>Last frame's posing, kept until this frame's is done (its poses are moved on for the skipped), and
        /// where each creature is in it.</summary>
        Posed prevPosed;
        bool havePrev;
        NativeParallelHashMap<Entity, int> prevIndex;
        /// <summary>(Empty stand-ins for the first frame, when there is no last pose.)</summary>
        NativeArray<SegmentInstance> noSegments;
        NativeArray<int> noOffsets;
        NativeArray<CreatureState> noStates;
        NativeArray<CreatureBody> noBodies;
        NativeArray<Entity> noEntities;
        readonly UnityEngine.Plane[] frustumPlanes = new UnityEngine.Plane[6];
        /// <summary>Counts frames for the staggered pose tiers.</summary>
        int poseFrame;
        /// <summary>Per worker, longs apart so they don't share a cache line: pose ticks, posed afresh, moved on.</summary>
        const int StatStride = 8;

        /// <summary>
        /// A creature's detail under this view, from its last decision: players' bodies and the carried always in full.
        /// Its size is CreatureMotion.ApparentSize; how active: crossing up to three body lengths a second, or flying.
        /// </summary>
        static Punchy.Settings.DetailPolicy.Decision Decide(in CreatureState state, in CreatureBody body,
                                                            in Punchy.Settings.DetailPolicy.View view,
                                                            Punchy.Settings.DetailPolicy.Decision previous)
        {
            if (state.Mode == CreatureMode.Puppet || state.Mode == CreatureMode.Held)
                return Punchy.Settings.DetailPolicy.Decision.Of(0, 0, true);
            var size = CreatureMotion.ApparentSize(state, ref body.Blob.Value);
            var activity = math.max(math.saturate(state.Speed / math.max(size, 0.3f) / 3f), state.Fly > 0.5f ? 0.5f : 0f);
            return Punchy.Settings.DetailPolicy.Decide(Punchy.Settings.DetailPolicy.Creature, size,
                                                       math.distance(state.Position, view.Camera), activity, view, previous);
        }

        /// <summary>Where each creature is in last frame's posing, by entity, for PoseJob.Reuse.</summary>
        [BurstCompile]
        struct IndexJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            public NativeParallelHashMap<Entity, int> Index;

            public void Execute()
            {
                Index.Clear();
                if (Index.Capacity < Entities.Length)
                    Index.Capacity = Entities.Length;
                for (int i = 0; i < Entities.Length; i++)
                    Index.TryAdd(Entities[i], i);
            }
        }
        static readonly string[] PassNames = { "copy", "cull", "counts", "anchors", "gather", "schedule", "extras", "posewait", "lights", "upload+draw" };
        readonly double[] passMs = new double[10];
        long passMark;
        int passFrames, passCreatures, passKept, passSegments, passCreatureSegments;
        float passSince = -1f;

        void PassStart(int creatures)
        {
            if (!PassLog)
                return;
            passMark = System.Diagnostics.Stopwatch.GetTimestamp();
            passCreatures += creatures;
        }

        void Pass(int index)
        {
            if (!PassLog)
                return;
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            passMs[index] += (t - passMark) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            passMark = t;
        }

        void PassEnd(int kept, int creatureSegments, int segments, bool hidden)
        {
            if (!PassLog)
                return;
            passFrames++;
            passKept += kept;
            passCreatureSegments += creatureSegments;
            passSegments += segments;
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (passSince < 0f)
                passSince = now;
            if (now - passSince < 3f)
                return;
            var line = new System.Text.StringBuilder("CreatureRenderSystem passes (ms/frame, ");
            line.Append(passFrames).Append(" frames, ").Append(OldCull ? "old cull" : "burst cull").Append(hidden ? ", cave hidden" : "")
                .Append("): creatures ").Append(passCreatures / passFrames).Append(" kept ").Append(passKept / passFrames)
                .Append(" segs ").Append(passCreatureSegments / passFrames).Append('/').Append(passSegments / passFrames);
            var sum = 0.0;
            for (int i = 0; i < passMs.Length; i++)
            {
                line.Append(" | ").Append(PassNames[i]).Append(' ').Append((passMs[i] / passFrames).ToString("0.000"));
                sum += passMs[i];
                passMs[i] = 0;
            }
            line.Append(" | total ").Append((sum / passFrames).ToString("0.000"));
            Debug.Log(line.ToString());
            passFrames = passCreatures = passKept = passSegments = passCreatureSegments = 0;
            passSince = now;
        }

        /// <summary>
        /// Which creatures to pose and draw: not those well behind the camera, nor (deep in a cave, out of sight of
        /// every mouth) those up on the surface. Players' bodies always. In Burst: the cave test samples the ground under
        /// every creature, which in managed code cost several ms a frame.
        /// </summary>
        [BurstCompile]
        struct CullJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            public TerrainSource Terrain;
            public float3 CameraPosition, CameraForward;
            public float BehindCos;
            public bool Hidden;
            /// <summary>A-70 row 4: the camera's side planes (left, right, bottom, top: normal and distance, inside where
            /// positive), and whether to cull those wholly outside them.</summary>
            public bool Frustum;
            public float4 Left, Right, Bottom, Top;
            public NativeArray<bool> Keep;

            public void Execute(int i)
            {
                var state = States[i];
                if (state.Mode == CreatureMode.Puppet)
                {
                    Keep[i] = true;
                    return;
                }
                var to = state.Position - CameraPosition;
                var far = math.lengthsq(to) > BehindCullRange * BehindCullRange;
                var behind = far && math.dot(math.normalizesafe(to), CameraForward) < BehindCos &&
                             math.length(to) > Bodies[i].Blob.Value.SeenFrom * 0.5f;
                // Well off to a side of the view (A-70 row 4), by more than its own size and a margin. Not a lantern or
                // torch bearer, whose light reaches into view from further off.
                var outside = false;
                if (Frustum && far && state.Held != HeldItem.Lantern && state.Held != HeldItem.Torch)
                {
                    var r = -(FrustumMargin + 0.1f * Bodies[i].Blob.Value.SeenFrom);
                    var p = state.Position;
                    outside = math.dot(Left.xyz, p) + Left.w < r || math.dot(Right.xyz, p) + Right.w < r ||
                              math.dot(Bottom.xyz, p) + Bottom.w < r || math.dot(Top.xyz, p) + Top.w < r;
                }
                Keep[i] = !behind && !outside && !(Hidden && !(state.Position.y < Terrain.SampleHeight(state.Position.xz) - 1f));
            }
        }

        /// <summary>
        /// The creatures kept by CullJob packed to the front of the copies, their parts counted (each one's offset into
        /// the instances, and the total), and the bats hanging at roosts and the players' bodies emoting listed (A-51).
        /// </summary>
        [BurstCompile]
        struct CompactJob : IJob
        {
            /// <summary>Every creature (the snapshot's copy, read only: A-70 row 3), and the kept ones packed into these.</summary>
            [ReadOnly] public NativeArray<CreatureBody> AllBodies;
            [ReadOnly] public NativeArray<CreatureState> AllStates;
            [ReadOnly] public NativeArray<Entity> AllEntities;
            public NativeArray<CreatureBody> Bodies;
            public NativeArray<CreatureState> States;
            public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<bool> Keep;
            public NativeArray<int> Offsets;
            public NativeList<float3> Hanging, Emoting;
            /// <summary>How many were kept, and the parts they take.</summary>
            public NativeReference<int2> Counted;
            /// <summary>Refactor 3.7: when Tiers, each kept creature's detail decided (Decide, from its last decision:
            /// PrevIndex into PrevDecisions), and the kept packed by draw group (DrawGroup), with where each group starts
            /// among the parts. Without Tiers ("-oldposerate") all in full, posed every frame, with shadows.</summary>
            public bool Tiers;
            public Punchy.Settings.DetailPolicy.View View;
            [ReadOnly] public NativeParallelHashMap<Entity, int> PrevIndex;
            [ReadOnly] public NativeArray<byte> PrevDecisions;
            public NativeArray<byte> Decisions;
            public NativeArray<int> GroupStarts;

            public void Execute()
            {
                var decided = new NativeArray<byte>(AllBodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var full = Punchy.Settings.DetailPolicy.Decision.Of(0, 0, true);
                for (int i = 0; i < AllBodies.Length; i++)
                {
                    if (!Keep[i])
                        continue;
                    var previous = Punchy.Settings.DetailPolicy.Decision.First;
                    if (PrevIndex.TryGetValue(AllEntities[i], out var was) && was < PrevDecisions.Length)
                        previous.Packed = PrevDecisions[was];
                    decided[i] = (Tiers ? Decide(AllStates[i], AllBodies[i], View, previous) : full).Packed;
                }
                var keep = 0;
                for (int group = 0; group < (Tiers ? GroupCount : 1); group++)
                    for (int i = 0; i < AllBodies.Length; i++)
                        if (Keep[i] && (!Tiers || DrawGroup(new Punchy.Settings.DetailPolicy.Decision { Packed = decided[i] }) == group))
                        {
                            Bodies[keep] = AllBodies[i];
                            States[keep] = AllStates[i];
                            Entities[keep] = AllEntities[i];
                            Decisions[keep] = decided[i];
                            keep++;
                        }
                var total = 0;
                var nextGroup = 0;
                for (int i = 0; i < keep; i++)
                {
                    var state = States[i];
                    var body = Bodies[i];
                    // (Each group from where its first creature's parts start; empty ones start where the next does.)
                    var group = DrawGroup(new Punchy.Settings.DetailPolicy.Decision { Packed = Decisions[i] });
                    while (nextGroup <= group)
                        GroupStarts[nextGroup++] = total;
                    Offsets[i] = total;
                    total += body.SegmentCount + MarkCount + (InWeb(state) ? WebCount : 0) + LairCount(ref body.Blob.Value) +
                             (state.Task == CreatureTask.Hunt ? 1 : 0) + HeldCount(state.Held, state.Task) + GearCount(state.Task);
                    if (state.Mode == CreatureMode.Roost && state.Stage == 200)
                        Hanging.Add(state.Position);
                    // (Bodies of players emoting: their gestures scatter colonies.)
                    if (state.Mode == CreatureMode.Puppet && state.Action >= IdleAction.Wave)
                        Emoting.Add(state.Position);
                }
                Counted.Value = new int2(keep, total);
                while (nextGroup <= GroupCount)
                    GroupStarts[nextGroup++] = total;
            }
        }
    }
}
