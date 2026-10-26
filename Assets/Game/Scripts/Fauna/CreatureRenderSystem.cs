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
    /// instanced draw of a unit capsule: the shader stretches, tapers and places each instance.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CreatureRenderSystem : SystemBase
    {
        EntityQuery query;
        GraphicsBuffer buffer;
        MaterialPropertyBlock props;
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");

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
                total += bodies[i].SegmentCount;
            }
            bodies.Dispose();

            var instances = new NativeArray<SegmentInstance>(total, Allocator.TempJob);
            Dependency = new PoseJob
            {
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Offsets = offsets,
                Output = instances
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

            void Execute([EntityIndexInQuery] int index, in CreatureState state, in CreatureBody body)
            {
                CreatureAnimator.Evaluate(ref body.Blob.Value, state, body.Coat, Terrain, Output, Offsets[index]);
            }
        }
    }
}
