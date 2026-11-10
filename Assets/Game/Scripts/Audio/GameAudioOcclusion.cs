using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>
    /// Walls and hills between the listener and a sound (A-98 rows 4-5). A few playing voices a frame (ChecksPerFrame,
    /// so each is looked at a few times a second) get a line-of-sight test from the ear to the sound: against the
    /// buildings' kit boxes near the player (only boxes big enough to be a wall panel, ThinWall: a fence rail, a post
    /// or a beam doesn't block, and a window's gap is open) and against the ground (a hill, sampled along the line; a
    /// point in a cave's air is open, not rock). A blocked voice is muffled on its own air low-pass (down to
    /// BlockedCut) and made quieter (BlockedGain), eased over BlockEase seconds so it never clicks; a new sound is
    /// tested as it starts, so it starts right.
    /// When a voice is blocked, the nearest few openings (doors and cave mouths within OpeningReach of the ear) are
    /// tried as a way round: if one can see both the ear and the sound, the sound is heard from that opening's
    /// direction, as far away as the way round is long, and muffled only as much as the detour is long (at least
    /// ShutDoor through a door leaf).
    /// The tests run as a small Burst job, run in place (a managed ground sample costs ~25 us, a Burst one far less).
    /// Under water the water's muffle wins (GameAudioWater) and this fades out. Local only: nothing is sent.
    /// </summary>
    public partial class GameAudio
    {
        /// <summary>A blocked voice's cutoff (Hz) and level, and how long it takes to get there (s).</summary>
        const float BlockedCut = 1500f, BlockedGain = 0.4f, BlockEase = 0.2f;
        /// <summary>How many playing voices are tested each frame.</summary>
        const int ChecksPerFrame = 3;
        /// <summary>A box blocks only when its middle size is at least this (m): a wall panel (even the strip under a
        /// window), not a rail, a post or a beam. A window's gap in a wall lets sound through on its own.</summary>
        const float ThinWall = 0.5f;
        /// <summary>The ground along a line is sampled about this often (m), at most LineSamples times.</summary>
        const float LineStep = 4f;
        const int LineSamples = 16;
        /// <summary>How far from the ear openings are looked for (m), how many are tried, and the detour (m) that
        /// muffles a way round as much as a wall.</summary>
        const float OpeningReach = 30f, DetourFull = 15f;
        const int OpeningsTried = 4;
        /// <summary>How far above a door anchor (on the ground just outside the door) the doorway's middle is (m).</summary>
        const float DoorHeight = 1.2f;
        /// <summary>A box thinner than this (m) is a door leaf: a way round through it is muffled at least ShutDoor (0..1),
        /// shut or open (whether a door stands open isn't known here yet).</summary>
        const float DoorLeaf = 0.2f, ShutDoor = 0.5f;

        int checkNext;
        EntityQuery kitQuery;
        World kitWorld;
        readonly Voice[] checking = new Voice[ChecksPerFrame];
        readonly System.Diagnostics.Stopwatch occlusionWatch = new System.Diagnostics.Stopwatch();
        /// <summary>The time the tests took this frame, and averaged over about a second (ms).</summary>
        float occlusionMs, occlusionAverage;

        /// <summary>The tests' cost (ms per frame, averaged), and how many playing voices are blocked and routed round now.</summary>
        public static (float ms, int blocked, int routed, int playing) Occlusion
        {
            get
            {
                if (instance == null)
                    return default;
                int blocked = 0, routed = 0, playing = 0;
                foreach (var v in instance.voices)
                    if (v.Source.isPlaying)
                    {
                        playing++;
                        if (v.OccludedTarget > 0f)
                            blocked++;
                        if (v.Routed)
                            routed++;
                    }
                return (instance.occlusionAverage, blocked, routed, playing);
            }
        }

        /// <summary>Tests the next few playing voices, and eases every playing voice towards what its last test found.</summary>
        void Occlude()
        {
            occlusionWatch.Restart();
            var count = 0;
            for (int n = 0; n < voices.Count && count < ChecksPerFrame; n++)
            {
                var voice = voices[checkNext];
                checkNext = (checkNext + 1) % voices.Count;
                if (voice.Source.isPlaying)
                    checking[count++] = voice;
            }
            Check(checking, count);
            var dt = Time.unscaledDeltaTime;
            var k = 1f - Mathf.Exp(-dt / (BlockEase * 0.5f));
            // (Only voices still easing: most sit where their last test put them, and a transform write isn't free.)
            foreach (var voice in voices)
            {
                var moving = voice.Detour != voice.DetourTarget;
                if (voice.Occluded == voice.OccludedTarget && !moving || !voice.Source.isPlaying)
                    continue;
                voice.Occluded = Mathf.MoveTowards(voice.Occluded, voice.OccludedTarget, dt / BlockEase);
                if (!moving)
                    continue;
                voice.Detour = Vector3.Lerp(voice.Detour, voice.DetourTarget, k);
                if (voice.Detour == voice.DetourTarget)
                    voice.Detour = voice.DetourTarget;
                voice.Source.transform.position = voice.At + voice.Detour;
            }
            occlusionWatch.Stop();
            occlusionMs += (float)occlusionWatch.Elapsed.TotalMilliseconds;
            occlusionAverage = Mathf.Lerp(occlusionAverage, occlusionMs, 0.03f);
            occlusionMs = 0f;
        }

        /// <summary>A new sound is tested as it starts, and starts where its test puts it (no ease from open).</summary>
        void OccludeAtStart(Voice voice)
        {
            occlusionWatch.Restart();
            checking[0] = voice;
            Check(checking, 1);
            voice.Occluded = voice.OccludedTarget;
            voice.Detour = voice.DetourTarget;
            voice.Source.transform.position = voice.At + voice.Detour;
            occlusionWatch.Stop();
            occlusionMs += (float)occlusionWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Tests the first count voices of these: blocked or not, and the way round through an opening.</summary>
        void Check(Voice[] these, int count)
        {
            for (int i = 0; i < count; i++)
            {
                these[i].OccludedTarget = 0f;
                these[i].DetourTarget = Vector3.zero;
                these[i].Routed = false;
            }
            // (Under water the water's muffle is the whole story.)
            if (count == 0 || wet > 0f)
                return;
            var hasKit = Kit(out var kit);
            var job = new OcclusionJob
            {
                Ear = Ear,
                Ground = Terrain,
                HasGround = HasTerrain,
                Kit = hasKit ? kit.AsNativeArray() : new NativeArray<KitBox>(0, Allocator.TempJob),
                At = new NativeArray<float3>(count, Allocator.TempJob),
                Out = new NativeArray<Heard>(count, Allocator.TempJob),
            };
            for (int i = 0; i < count; i++)
                job.At[i] = these[i].At;
            Unity.Jobs.IJobExtensions.Run(job);
            for (int i = 0; i < count; i++)
            {
                var heard = job.Out[i];
                these[i].OccludedTarget = heard.Occluded;
                these[i].DetourTarget = heard.Detour;
                these[i].Routed = heard.Routed != 0;
            }
            job.At.Dispose();
            job.Out.Dispose();
            if (!hasKit)
                job.Kit.Dispose();
        }

        /// <summary>The kit boxes near the player (the buildings' walls, roofs and floors), if there are any.</summary>
        bool Kit(out DynamicBuffer<KitBox> kit)
        {
            kit = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (kitWorld != world)
            {
                kitQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                kitWorld = world;
            }
            return kitQuery.TryGetSingletonBuffer(out kit, true) && kit.Length > 0;
        }

        /// <summary>What a test found for one voice: how blocked (0..1), and where it's heard from (an offset from it).</summary>
        struct Heard
        {
            public float3 Detour;
            public float Occluded;
            public byte Routed;
        }

        /// <summary>The line-of-sight tests for a few voices, and the ways round through openings.</summary>
        [BurstCompile]
        struct OcclusionJob : Unity.Jobs.IJob
        {
            public float3 Ear;
            public TerrainSource Ground;
            public bool HasGround;
            [ReadOnly] public NativeArray<KitBox> Kit;
            [ReadOnly] public NativeArray<float3> At;
            public NativeArray<Heard> Out;
            /// <summary>Whether the last ways to an opening tested went through a door leaf.</summary>
            bool leafCrossed;

            public void Execute()
            {
                for (int i = 0; i < At.Length; i++)
                    Out[i] = Check(At[i]);
            }

            Heard Check(float3 at)
            {
                var heard = default(Heard);
                if (!Blocked(Ear, at))
                    return heard;
                heard.Occluded = 1f;
                if (!Opening(at, out var opening, out var path, out var leaf))
                    return heard;
                // Heard from the opening's direction, as far off as the way round, muffled by how far round it is (and
                // by a door leaf in the way).
                var detour = path - math.distance(Ear, at);
                heard.Occluded = math.max(math.lerp(0.2f, 1f, math.saturate(detour / DetourFull)), leaf ? ShutDoor : 0f);
                heard.Detour = Ear + math.normalize(opening - Ear) * path - at;
                heard.Routed = 1;
                return heard;
            }

            /// <summary>
            /// Whether a wall (a big enough kit box) or the ground (a hill, not a cave's air) stands between a and b. On the
            /// way to an opening (throughLeaves) a door leaf doesn't block, but crossing one sets leafCrossed.
            /// </summary>
            bool Blocked(float3 a, float3 b, bool throughLeaves = false)
            {
                var length = math.distance(a, b);
                if (length < 1.5f)
                    return false;
                if (Walled(a, b, length, throughLeaves))
                    return true;
                if (!HasGround)
                    return false;
                // (The ends lifted a little and half a metre of grace: a sound over a hump in the ground still carries,
                // as it bends over it; a hill or a ridge doesn't.)
                var lift = new float3(0f, 0.3f, 0f);
                a += lift;
                b += lift;
                var n = math.clamp((int)math.ceil(length / LineStep), 2, LineSamples);
                for (int i = 1; i < n; i++)
                {
                    var p = math.lerp(a, b, i / (float)n);
                    if (p.y + 0.5f >= Ground.SampleHeight(p.xz))
                        continue;
                    // (Under the ground: rock, unless it's a cave's air.)
                    if (!Ground.Caves.IsCreated || Ground.CaveDistance(p) > 0f)
                        return true;
                }
                return false;
            }

            /// <summary>KitCollision.Blocks without the thin boxes: a slab test against each box big enough to be a wall.</summary>
            bool Walled(float3 a, float3 b, float length, bool throughLeaves)
            {
                var mid = (a + b) * 0.5f;
                var halfLength = length * 0.5f;
                for (int i = 0; i < Kit.Length; i++)
                {
                    var box = Kit[i];
                    if (math.distance(box.Centre, mid) > box.Reach + halfLength)
                        continue;
                    // (The middle of its three sizes: a wall panel is long and tall, a rail, a post or a beam is not.)
                    var size = box.Half * 2f;
                    if (math.csum(size) - math.cmax(size) - math.cmin(size) < ThinWall)
                        continue;
                    var inv = math.conjugate(box.Rotation);
                    var from = math.mul(inv, a - box.Centre);
                    var dir = math.mul(inv, b - a);
                    float enter = 0f, exit = 1f;
                    var hit = true;
                    for (int k = 0; k < 3 && hit; k++)
                    {
                        if (math.abs(dir[k]) < 1e-6f)
                        {
                            hit = math.abs(from[k]) <= box.Half[k];
                            continue;
                        }
                        var t0 = (-box.Half[k] - from[k]) / dir[k];
                        var t1 = (box.Half[k] - from[k]) / dir[k];
                        enter = math.max(enter, math.min(t0, t1));
                        exit = math.min(exit, math.max(t0, t1));
                        hit = enter <= exit;
                    }
                    if (!hit)
                        continue;
                    if (!throughLeaves || math.cmin(size) >= DoorLeaf)
                        return true;
                    leafCrossed = true;
                }
                return false;
            }

            /// <summary>
            /// The shortest way round through one of the nearest openings (doors, cave mouths) within OpeningReach of
            /// the ear that can see both the ear and the sound: where it is, and how long the way through it is.
            /// </summary>
            bool Opening(float3 at, out float3 opening, out float path, out bool leaf)
            {
                opening = default;
                path = 0f;
                leaf = false;
                if (!HasGround)
                    return false;
                // (Each kept as xyz and the way's length, shortest first.)
                var kept = new FixedList128Bytes<float4>();
                if (Ground.Anchors.IsCreated)
                {
                    var near = new NativeList<int>(16, Allocator.Temp);
                    ref var set = ref Ground.Anchors.Value;
                    Punchy.Terrain.Anchors.Near(ref set, Ear, OpeningReach, AnchorKind.Door, ref near);
                    for (int i = 0; i < near.Length; i++)
                        Keep(ref kept, set.Anchors[near[i]].Position + new float3(0f, DoorHeight, 0f), at);
                    near.Dispose();
                }
                if (Ground.Caves.IsCreated)
                {
                    ref var caves = ref Ground.Caves.Value.Caves;
                    for (int i = 0; i < caves.Length; i++)
                        if (math.distancesq(caves[i].Mouth, Ear) < OpeningReach * OpeningReach)
                            Keep(ref kept, caves[i].Mouth, at);
                }
                // (Shortest way first; the first that sees both ends is the way the sound goes.)
                for (int i = 0; i < kept.Length; i++)
                {
                    leafCrossed = false;
                    if (!Blocked(Ear, kept[i].xyz, true) && !Blocked(kept[i].xyz, at, true))
                    {
                        opening = kept[i].xyz;
                        path = kept[i].w;
                        leaf = leafCrossed;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Keeps an opening among the OpeningsTried shortest ways round, sorted shortest first.</summary>
            void Keep(ref FixedList128Bytes<float4> kept, float3 opening, float3 at)
            {
                var path = math.distance(Ear, opening) + math.distance(opening, at);
                var slot = kept.Length;
                while (slot > 0 && kept[slot - 1].w > path)
                    slot--;
                if (slot >= OpeningsTried)
                    return;
                kept.Insert(slot, new float4(opening, path));
                if (kept.Length > OpeningsTried)
                    kept.RemoveAt(OpeningsTried);
            }
        }
    }
}
