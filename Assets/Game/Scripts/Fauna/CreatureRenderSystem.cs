using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    /// <summary>
    /// Poses every creature (Burst, in parallel) into one list of segments and draws them all with a single
    /// instanced draw of a unit capsule: the shader stretches, tapers and places each instance. Every creature also
    /// has a few spare capsules after its body for fear marks: short lines flashing out above the head while it
    /// flees (collapsed to nothing the rest of the time). Web spinners have their web's threads after that, drawn
    /// while they sit in it: everything about the web follows from where the spider is and which way it faces, so
    /// replicas draw the same web with nothing extra sent. Roosts near the camera show a colony of sleeping bats hanging
    /// from the ceiling: no creatures, just a few capsules each (placed from the roost's index, so every machine draws
    /// the same colony), thinner at night when most are out.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CreatureRenderSystem : SystemBase
    {
        EntityQuery query;
        GraphicsBuffer buffer;
        MaterialPropertyBlock props;
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        const int MarkCount = 3;
        const int Spokes = 12, Rings = 6, Anchors = 4;
        const int WebCount = Spokes + Spokes * Rings + Anchors;
        const int MoundCount = 7;
        /// <summary>Capsules per nest: its floor, a ring of twigs (or grass), and the darkening under it.</summary>
        const int NestCount = 10;
        const int NestRim = 8;
        /// <summary>Room for the animal's lair: a burrow's mound or a nest.</summary>
        static int LairCount(ref CreatureBlob b) => b.Burrows ? MoundCount : b.Nests != NestSite.None ? NestCount : 0;
        /// <summary>Capsules per hanging bat, the most per roost, and how near the camera colonies show.</summary>
        const int BatParts = 4, ColonyMax = 10;
        const float ColonyRange = 45f;
        Unity.Collections.NativeList<int> roosts;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
            RequireForUpdate(query);
        }

        protected override void OnDestroy()
        {
            if (roosts.IsCreated) roosts.Dispose();
            buffer?.Release();
            buffer = null;
        }

        /// <summary>How many sleeping bats a roost shows: up to ColonyMax (by its capacity), fewer at night.</summary>
        static int ColonySize(in Anchor roost, int index, float night)
        {
            var size = math.min((int)roost.Capacity, ColonyMax);
            var awake = (int)math.round(size * night * 0.75f);
            return math.max(size - awake, 0);
        }

        /// <summary>
        /// Writes a roost's sleeping bats: each hangs head down from the ceiling, wings wrapped round, at a spot spread
        /// over the roost picked from its index.
        /// </summary>
        static int Colony(in Anchor roost, int index, float night, NativeArray<SegmentInstance> output, int at)
        {
            var count = ColonySize(roost, index, night);
            var rng = new Unity.Mathematics.Random(math.hash(new int2(index, 0xBA7)) | 1u);
            var fur = new float4(0.2f, 0.15f, 0.11f, 0f);
            var wing = new float4(0.1f, 0.08f, 0.07f, 0f);
            var down = new float3(0f, -1f, 0f);
            for (int k = 0; k < count; k++)
            {
                var spread = rng.NextFloat2Direction() * rng.NextFloat(0f, math.max(roost.Size, 0.3f) * 0.8f);
                var size = rng.NextFloat(0.85f, 1.15f);
                var top = roost.Position + new float3(spread.x, -0.02f, spread.y);
                var facing = rng.NextFloat(math.PI * 2f);
                var side = new float3(math.cos(facing), 0f, math.sin(facing));
                var hang = quaternion.LookRotationSafe(down, side);
                // Body, head below it, and the two folded wings either side.
                output[at++] = Part(top, hang, 0.09f * size, 0.032f * size, 0.026f * size, 1f, fur);
                output[at++] = Part(top + down * 0.1f * size, hang, 0.015f * size, 0.022f * size, 0.02f * size, 1f, fur);
                for (int w = -1; w <= 1; w += 2)
                    output[at++] = Part(top + side * w * 0.025f * size, hang, 0.1f * size, 0.03f * size, 0.02f * size, 0.25f, wing);
            }
            return at;
        }

        static SegmentInstance Part(float3 from, quaternion rotation, float length, float r0, float r1, float flatten, float4 colour) =>
            new SegmentInstance
            {
                Position = from,
                Rotation = rotation,
                Length = length,
                RadiusStart = r0,
                RadiusEnd = r1,
                Flatten = flatten,
                Color = colour
            };

        /// <summary>A spider sitting in its web (only then is the web drawn, and room made for it).</summary>
        static bool InWeb(in CreatureState s) => s.Mode == CreatureMode.Web && s.Stage >= 2 && s.Stage <= 4;

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<FaunaRenderSettings>(out var settings) || settings.Material == null || settings.Capsule == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;

            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var offsets = new NativeArray<int>(bodies.Length, Allocator.TempJob);
            var total = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                offsets[i] = total;
                total += bodies[i].SegmentCount + MarkCount + (InWeb(states[i]) ? WebCount : 0) +
                         LairCount(ref bodies[i].Blob.Value) + (states[i].Task == CreatureTask.Hunt ? 1 : 0);
            }
            bodies.Dispose();
            states.Dispose();

            // Colonies at the roosts near the camera.
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            if (!roosts.IsCreated)
                roosts = new Unity.Collections.NativeList<int>(16, Allocator.Persistent);
            roosts.Clear();
            if (terrain.Anchors.IsCreated)
                Punchy.Terrain.Anchors.Near(ref terrain.Anchors.Value, cam.transform.position, ColonyRange, AnchorKind.Roost, ref roosts);
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            var colonyStart = total;
            for (int i = 0; i < roosts.Length; i++)
                total += ColonySize(terrain.Anchors.Value.Anchors[roosts[i]], roosts[i], night) * BatParts;

            // Footprints and feathers (TraceSystem).
            var traces = World.GetExistingSystemManaged<TraceSystem>();
            var traceCount = traces != null ? traces.Count : 0;
            total += traceCount;

            var instances = new NativeArray<SegmentInstance>(total, Allocator.TempJob);
            Dependency = new PoseJob
            {
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Offsets = offsets,
                Output = instances,
                Time = (float)SystemAPI.Time.ElapsedTime,
                CameraRight = cam.transform.right,
                CameraPosition = cam.transform.position,
                FadeInTime = 1.2f,
                FadeOutRange = 25f,
                // A little short of FaunaDirector's despawn distance (170 m), so it's gone before it's removed.
                FadeFar = 165f
            }.ScheduleParallel(query, Dependency);
            Dependency.Complete();
            var at = colonyStart;
            for (int i = 0; i < roosts.Length; i++)
                at = Colony(terrain.Anchors.Value.Anchors[roosts[i]], roosts[i], night, instances, at);
            if (traceCount > 0)
                at = traces.Write(instances, at, (float)SystemAPI.Time.ElapsedTime);

            if (buffer == null || buffer.count < total)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(total), Marshal.SizeOf<SegmentInstance>());
            }
            buffer.SetData(instances);
            instances.Dispose();
            offsets.Dispose();

            props ??= new MaterialPropertyBlock();
            props.SetBuffer(SegmentsId, buffer);
            var rp = new RenderParams(settings.Material)
            {
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 2000f),
                matProps = props,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            };
            Graphics.RenderMeshPrimitives(rp, settings.Capsule, 0, total);
        }

        [BurstCompile]
        partial struct PoseJob : IJobEntity
        {
            public TerrainSource Terrain;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<SegmentInstance> Output;
            public float Time;
            public Unity.Mathematics.float3 CameraRight, CameraPosition;
            /// <summary>Faded in over this long after it's made; out over this far before it's let go.</summary>
            public float FadeInTime, FadeOutRange, FadeFar;

            void Execute(Entity entity, [EntityIndexInQuery] int index, in CreatureState state, in CreatureBody body)
            {
                var offset = Offsets[index];
                CreatureAnimator.Evaluate(ref body.Blob.Value, state, body.Coat, Terrain, Output, offset);
                // Appearing (its first moments: Clock counts from when it was made, here) or nearing the distance
                // where it's let go: dithered in and out by the shader, so nothing pops.
                var fadeIn = Unity.Mathematics.math.saturate(state.Clock / FadeInTime);
                var fadeOut = Unity.Mathematics.math.saturate((FadeFar - Unity.Mathematics.math.distance(state.Position, CameraPosition)) / FadeOutRange);
                var faded = 1f - fadeIn * fadeOut;
                if (faded > 0f)
                    for (int i = 0; i < body.SegmentCount; i++)
                    {
                        var instance = Output[offset + i];
                        instance.Pad = faded;
                        Output[offset + i] = instance;
                    }
                FearMarks(state, offset, body.SegmentCount, entity.Index);
                if (InWeb(state))
                    Web(state, ref body.Blob.Value, offset + body.SegmentCount + MarkCount);
                var lair = offset + body.SegmentCount + MarkCount + (InWeb(state) ? WebCount : 0);
                if (body.Blob.Value.Burrows)
                    Mound(state, lair);
                else if (body.Blob.Value.Nests != NestSite.None)
                    Nest(state, body.Blob.Value.Nests, lair);
                if (state.Task == CreatureTask.Hunt)
                    Spear(state, ref body.Blob.Value, offset, lair + LairCount(ref body.Blob.Value));
            }

            /// <summary>
            /// A hunter's spear, in the right hand: carried pointing ahead and a little down while creeping, held level
            /// over the shoulder at the game while aiming; gone at the throw (it's been thrown), back in hand after.
            /// (Drawn for every hunter: the Hunt task is replicated, so each machine draws it.)
            /// </summary>
            void Spear(in CreatureState state, ref CreatureBlob b, int offset, int at)
            {
                Output[at] = default;
                if (state.Mode == CreatureMode.Hunt && state.Stage == FolkHunting.Throw)
                    return;
                // The right hand: the end of the right arm's last segment, as just posed.
                var hand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                    {
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (hand < 0)
                    return;
                var segment = Output[offset + hand];
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length));
                var yaw = state.Yaw;
                var aiming = state.Mode == CreatureMode.Hunt && state.Stage == FolkHunting.Aim;
                var pitch = aiming ? -0.08f : 0.25f;   // (positive: tip down)
                var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(yaw) * Unity.Mathematics.math.cos(pitch),
                                                       -Unity.Mathematics.math.sin(pitch),
                                                       Unity.Mathematics.math.cos(yaw) * Unity.Mathematics.math.cos(pitch));
                var length = 1.8f * state.Scale;
                var colour = new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f);   // (linear) dark wood
                Output[at] = new SegmentInstance
                {
                    Position = grip - dir * length * 0.4f,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = length,
                    RadiusStart = 0.016f * state.Scale,
                    RadiusEnd = 0.009f * state.Scale,
                    Flatten = 1f,
                    Color = colour
                };
            }

            /// <summary>
            /// A nest, where the bird's nest is (if it has one): a shallow bowl in a ring of twigs against a trunk, a
            /// bigger, untidier one of sticks on a treetop, or a pale ring of grass on the ground.
            /// </summary>
            void Nest(in CreatureState state, NestSite site, int offset)
            {
                if (state.AnchorSize <= 0f)
                {
                    for (int i = 0; i < NestCount; i++)
                        Output[offset + i] = default;
                    return;
                }
                var r = state.AnchorSize;
                var c = state.Anchor;
                var ground = site == NestSite.Ground;
                var top = site == NestSite.Treetop;
                // (Linear colours: dry grass; dark twigs.)
                var twigs = ground ? new Unity.Mathematics.float4(0.34f, 0.26f, 0.095f, 0f) : new Unity.Mathematics.float4(0.1f, 0.06f, 0.025f, 0f);
                var x = new Unity.Mathematics.float3(1f, 0f, 0f);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                // The floor: a flat disc just under the bird's feet.
                Output[offset] = new SegmentInstance
                {
                    Position = c + new Unity.Mathematics.float3(-r * 0.45f, ground ? 0.01f : -r * 0.05f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = r * 0.9f,
                    RadiusStart = r * 0.55f,
                    RadiusEnd = r * 0.55f,
                    Flatten = ground ? 0.1f : 0.3f,
                    Color = twigs * (ground ? 0.85f : 0.8f)
                };
                // The rim: twigs round it, each a little askew (sticks on a treetop more so).
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                var rimHeight = ground ? r * 0.05f : r * 0.15f;
                var thick = r * (ground ? 0.14f : top ? 0.16f : 0.18f);
                // Contact darkening: a dark, flat disc a little wider than the nest just under it (the ground in its
                // shade, or the shadowed underside of a nest in a tree).
                Output[offset + NestCount - 1] = new SegmentInstance
                {
                    Position = c + new Unity.Mathematics.float3(ground ? -r * 0.6f : -r * 0.5f, ground ? 0.004f : -r * 0.06f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = ground ? r * 1.2f : r,
                    RadiusStart = ground ? r * 0.8f : r * 0.7f,
                    RadiusEnd = ground ? r * 0.8f : r * 0.7f,
                    Flatten = 0.05f,
                    Color = new Unity.Mathematics.float4(0.02f, 0.018f, 0.012f, 0f)
                };
                for (int k = 1; k <= NestRim; k++)
                {
                    var a = k / (float)NestRim * Unity.Mathematics.math.PI * 2f + rng.NextFloat(-0.25f, 0.25f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var along = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(-dir.z, rng.NextFloat(-0.15f, 0.15f) * (top ? 2f : 1f), dir.x));
                    var length = r * (top ? rng.NextFloat(1.1f, 1.6f) : 0.95f);
                    var centre = c + dir * r * 0.9f + up * rimHeight;
                    Output[offset + k] = new SegmentInstance
                    {
                        Position = centre - along * length * 0.5f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, up),
                        Length = length,
                        RadiusStart = thick,
                        RadiusEnd = thick * 0.8f,
                        Flatten = ground ? 0.5f : 1f,
                        Color = twigs * rng.NextFloat(0.85f, 1.1f)
                    };
                }
            }

            /// <summary>A burrow: a dark hole in a ring of dug-out earth, where the animal's burrow is (if it has one).</summary>
            void Mound(in CreatureState state, int offset)
            {
                if (state.AnchorSize <= 0f)
                {
                    for (int i = 0; i < MoundCount; i++)
                        Output[offset + i] = default;
                    return;
                }
                var r = state.AnchorSize;
                var mouth = state.Anchor;
                var earth = new Unity.Mathematics.float4(0.11f, 0.06f, 0.027f, 0f);   // (linear)
                // The hole: a flat dark disc just above the ground.
                Output[offset] = new SegmentInstance
                {
                    Position = mouth + new Unity.Mathematics.float3(-r * 0.45f, 0.02f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(new Unity.Mathematics.float3(1f, 0f, 0f), new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = r * 0.9f,
                    RadiusStart = r * 0.55f,
                    RadiusEnd = r * 0.55f,
                    Flatten = 0.08f,
                    Color = new Unity.Mathematics.float4(0.05f, 0.04f, 0.03f, 0f)
                };
                // Heaped earth round it, thicker on one side (where it was thrown).
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                for (int k = 1; k < MoundCount; k++)
                {
                    var a = k / (float)(MoundCount - 1) * Unity.Mathematics.math.PI * 2f + rng.NextFloat(-0.3f, 0.3f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var along = new Unity.Mathematics.float3(-dir.z, 0f, dir.x);
                    var heap = k <= 2 ? 1.5f : 1f;
                    var centre = mouth + dir * r * 1.1f;
                    Output[offset + k] = new SegmentInstance
                    {
                        Position = centre - along * r * 0.5f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, new Unity.Mathematics.float3(0f, 1f, 0f)),
                        Length = r,
                        RadiusStart = r * 0.35f * heap,
                        RadiusEnd = r * 0.3f * heap,
                        Flatten = 0.45f,
                        Color = earth
                    };
                }
            }

            /// <summary>
            /// An orb web round the spider, in the plane facing its heading: spokes, a spiral (as rings of chords
            /// between the spokes), and anchor threads down to the ground and across to the tree it hangs by (see
            /// CreatureMode.Web for the side). Only for a spider sitting in its web (room is made for it then only).
            /// </summary>
            void Web(in CreatureState state, ref CreatureBlob b, int offset)
            {
                var radius = b.WebSize * state.Scale;
                var centre = state.Position;
                var right = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(state.Yaw), 0f, -Unity.Mathematics.math.sin(state.Yaw));
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var thick = Unity.Mathematics.math.clamp(radius * 0.006f, 0.003f, 0.012f);
                var colour = new Unity.Mathematics.float4(0.93f, 0.95f, 0.98f, 0.18f);
                // Every web is a little irregular, its own way.
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                var spoke = new Unity.Collections.FixedList512Bytes<float>();
                for (int k = 0; k < Spokes; k++)
                    spoke.Add((k + rng.NextFloat(-0.2f, 0.2f)) / Spokes * Unity.Mathematics.math.PI * 2f);
                Unity.Mathematics.float3 Point(float angle, float r) =>
                    centre + (right * Unity.Mathematics.math.cos(angle) + up * Unity.Mathematics.math.sin(angle)) * r;

                var n = 0;
                for (int k = 0; k < Spokes; k++)
                    Output[offset + n++] = Thread(centre, Point(spoke[k], radius * rng.NextFloat(0.95f, 1.05f)), thick, colour);
                for (int ring = 0; ring < Rings; ring++)
                {
                    var r = radius * (0.22f + 0.13f * ring);
                    for (int k = 0; k < Spokes; k++)
                    {
                        var next = spoke[(k + 1) % Spokes] + (k == Spokes - 1 ? Unity.Mathematics.math.PI * 2f : 0f);
                        Output[offset + n++] = Thread(Point(spoke[k], r), Point(next, r), thick * 0.8f, colour);
                    }
                }
                // Down to the ground from the lower edge, either side.
                for (int k = 0; k < 2; k++)
                {
                    var start = Point(Unity.Mathematics.math.PI * (k == 0 ? 1.3f : 1.7f), radius);
                    var foot = start + right * (k == 0 ? -0.3f : 0.3f) * radius;
                    foot.y = Terrain.SampleHeight(foot.xz);
                    Output[offset + n++] = Thread(start, foot, thick, colour);
                }
                // Across to the trunk, from the top and the near side (stage 2: tree to the spider's right, 3: left);
                // a web in the grass is tied up to stems instead.
                var trunkSide = state.Stage == 2 ? 1f : state.Stage == 3 ? -1f : 0f;
                for (int k = 0; k < 2; k++)
                {
                    var start = k == 0 ? Point(Unity.Mathematics.math.PI * 0.5f, radius) : Point(trunkSide >= 0f ? 0f : Unity.Mathematics.math.PI, radius);
                    var end = trunkSide != 0f
                        ? centre + right * trunkSide * radius * 1.05f + up * (k == 0 ? radius * 1.4f : 0f)
                        : start + up * radius * 0.4f + right * (k == 0 ? -0.15f : 0.15f) * radius;
                    Output[offset + n++] = Thread(start, end, thick, colour);
                }
            }

            static SegmentInstance Thread(Unity.Mathematics.float3 from, Unity.Mathematics.float3 to, float radius, Unity.Mathematics.float4 colour)
            {
                var d = to - from;
                var length = Unity.Mathematics.math.length(d);
                return new SegmentInstance
                {
                    Position = from,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(d / Unity.Mathematics.math.max(length, 1e-5f), new Unity.Mathematics.float3(0f, 1f, 0.01f)),
                    Length = length,
                    RadiusStart = radius,
                    RadiusEnd = radius,
                    Flatten = 1f,
                    Color = colour
                };
            }

            /// <summary>
            /// Three short lines fanning out above the creature's highest point, facing the camera, flashing on and
            /// off while it flees. Sized to the creature, but never too small to see.
            /// </summary>
            /// <param name="id">Fixed per creature, so each flashes on its own steady beat (its random seed changes
            /// every frame, which made the marks flicker).</param>
            void FearMarks(in CreatureState state, int offset, int segments, int id)
            {
                var top = float.MinValue;
                for (int i = 0; i < segments; i++)
                {
                    var seg = Output[offset + i];
                    var end = seg.Position + Unity.Mathematics.math.mul(seg.Rotation, new Unity.Mathematics.float3(0f, 0f, seg.Length));
                    top = Unity.Mathematics.math.max(top, Unity.Mathematics.math.max(seg.Position.y + seg.RadiusStart, end.y + seg.RadiusEnd));
                }
                var size = Unity.Mathematics.math.clamp(state.Scale * 0.3f, 0.25f, 0.5f);
                var flash = Unity.Mathematics.math.frac(Time * 3f + (id % 97) * 0.01f) < 0.6f;
                var visible = state.Mode == CreatureMode.Flee && flash;
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var right = Unity.Mathematics.math.normalizesafe(new Unity.Mathematics.float3(CameraRight.x, 0f, CameraRight.z), new Unity.Mathematics.float3(1f, 0f, 0f));
                for (int k = 0; k < MarkCount; k++)
                {
                    var angle = (k - 1) * 0.6f;
                    var dir = up * Unity.Mathematics.math.cos(angle) + right * Unity.Mathematics.math.sin(angle);
                    var start = new Unity.Mathematics.float3(state.Position.x, top, state.Position.z) + dir * size * 0.7f;
                    Output[offset + segments + k] = new SegmentInstance
                    {
                        Position = start,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, right),
                        Length = visible ? size : 0f,
                        RadiusStart = visible ? size * 0.16f : 0f,
                        RadiusEnd = visible ? size * 0.1f : 0f,
                        Flatten = 1f,
                        Color = new Unity.Mathematics.float4(1f, 0.92f, 0.55f, 0.6f)
                    };
                }
            }
        }
    }
}
