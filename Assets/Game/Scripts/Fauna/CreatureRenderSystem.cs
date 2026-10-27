using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
    /// replicas draw the same web with nothing extra sent.
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

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
            RequireForUpdate(query);
        }

        protected override void OnDestroy()
        {
            buffer?.Release();
            buffer = null;
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<FaunaRenderSettings>(out var settings) || settings.Material == null || settings.Capsule == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;

            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
            var offsets = new NativeArray<int>(bodies.Length, Allocator.TempJob);
            var total = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                offsets[i] = total;
                total += bodies[i].SegmentCount + MarkCount + (bodies[i].Blob.Value.SpinsWebs ? WebCount : 0);
            }
            bodies.Dispose();

            var instances = new NativeArray<SegmentInstance>(total, Allocator.TempJob);
            Dependency = new PoseJob
            {
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Offsets = offsets,
                Output = instances,
                Time = (float)SystemAPI.Time.ElapsedTime,
                CameraRight = cam.transform.right
            }.ScheduleParallel(query, Dependency);
            Dependency.Complete();

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
            public Unity.Mathematics.float3 CameraRight;

            void Execute(Entity entity, [EntityIndexInQuery] int index, in CreatureState state, in CreatureBody body)
            {
                var offset = Offsets[index];
                CreatureAnimator.Evaluate(ref body.Blob.Value, state, body.Coat, Terrain, Output, offset);
                FearMarks(state, offset, body.SegmentCount, entity.Index);
                if (body.Blob.Value.SpinsWebs)
                    Web(state, ref body.Blob.Value, offset + body.SegmentCount + MarkCount);
            }

            /// <summary>
            /// An orb web round the spider, in the plane facing its heading: spokes, a spiral (as rings of chords
            /// between the spokes), and anchor threads down to the ground and across to the tree it hangs by (see
            /// CreatureMode.Web for the side). Collapsed to nothing unless the spider is sitting in it.
            /// </summary>
            void Web(in CreatureState state, ref CreatureBlob b, int offset)
            {
                var inWeb = state.Mode == CreatureMode.Web && state.Stage >= 2 && state.Stage <= 4;
                if (!inWeb)
                {
                    for (int i = 0; i < WebCount; i++)
                        Output[offset + i] = default;
                    return;
                }
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
