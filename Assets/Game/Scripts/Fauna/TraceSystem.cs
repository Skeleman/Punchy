using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Traces creatures leave behind, near the camera: footprints on soft ground (snow, bare sand or earth, wet mud by
    /// water), alternating left and right at each step, in a darker shade of the ground they're on; and feathers where a
    /// bird preens or shakes. All of it is worked out on each machine from creatures' motion (replicated for animals),
    /// so nothing is sent. Traces live in one fixed ring (the newest overwrites the oldest), are drawn by
    /// CreatureRenderSystem in its single instanced draw, and shrink away at the end of their life, so they never
    /// build up.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CreatureRenderSystem))]
    public partial class TraceSystem : SystemBase
    {
        public struct Trace
        {
            public float3 Position;
            public quaternion Rotation;
            public float Length, Radius, Flatten;
            public float4 Color;
            public float Born, Life;
        }

        /// <summary>The most traces at once (all kinds).</summary>
        public const int Capacity = 1536;
        const float Range = 40f;
        const float FootprintLife = 90f, FeatherLife = 120f;

        public NativeArray<Trace> Traces;
        public int Count;
        int next;
        EntityQuery query;
        /// <summary>Per creature: where its last print was, and which foot made it (w: +1 or -1); its last action.</summary>
        NativeHashMap<Entity, float4> steps;
        NativeHashMap<Entity, byte> actions;

        protected override void OnCreate()
        {
            Traces = new NativeArray<Trace>(Capacity, Allocator.Persistent);
            steps = new NativeHashMap<Entity, float4>(256, Allocator.Persistent);
            actions = new NativeHashMap<Entity, byte>(256, Allocator.Persistent);
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
        }

        protected override void OnDestroy()
        {
            if (Traces.IsCreated) Traces.Dispose();
            if (steps.IsCreated) steps.Dispose();
            if (actions.IsCreated) actions.Dispose();
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            var now = (float)SystemAPI.Time.ElapsedTime;
            float3 camPos = cam.transform.position;
            var entities = query.ToEntityArray(Allocator.Temp);
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                if (math.distancesq(s.Position, camPos) > Range * Range)
                {
                    steps.Remove(entities[i]);
                    continue;
                }
                ref var b = ref bodies[i].Blob.Value;
                Feather(entities[i], s, ref b, terrain, now);
                Footprint(entities[i], s, ref b, terrain, now);
            }
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();
        }

        void Add(in Trace trace)
        {
            Traces[next] = trace;
            next = (next + 1) % Capacity;
            Count = math.min(Count + 1, Capacity);
        }

        /// <summary>A print each time it has gone half a stride, on alternate sides, where the ground takes one.</summary>
        void Footprint(Entity e, in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float now)
        {
            if (b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats || s.Fly > 0.5f || s.Lie > 0.5f || s.Speed < 0.05f)
                return;
            var ground = terrain.SampleGround(s.Position.xz);
            if (s.Position.y - ground.Height > 0.3f)
                return;   // up a tree, on a branch, in a cave above the surface, mid-jump
            var stride = b.WalkStride * 0.5f * math.max(s.Scale, 0.1f);
            if (!steps.TryGetValue(e, out var last))
            {
                steps[e] = new float4(s.Position, 1f);
                return;
            }
            if (math.distance(last.xyz, s.Position) < stride)
                return;
            var foot = -last.w;
            steps[e] = new float4(s.Position, foot);

            // Soft ground only: snow, bare sand or earth (little grass, few pebbles), or mud at the water's edge.
            var surface = terrain.SampleSurface(s.Position.xz, ground, terrain.SampleNormal(s.Position.xz, 1f));
            var bare = (1f - math.saturate(surface.Grass * 2.5f)) * (1f - math.saturate(surface.Pebbles * 1.5f)) * (1f - surface.Metallic);
            var mud = ground.HasWater && ground.WaterSurface - ground.Height > -1.2f ? 1f : 0f;
            var soft = math.max(surface.Snow, math.max(bare * 0.8f, mud));
            if (soft < 0.5f || ground.HasWater && ground.WaterSurface > ground.Height + 0.02f)
                return;
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var right = new float3(forward.z, 0f, -forward.x);
            var size = math.clamp(b.FrontRadius * s.Scale * 0.35f, 0.02f, 0.25f);
            var at = s.Position + right * foot * b.FrontRadius * s.Scale * 0.6f - forward * size;
            at.y = terrain.SampleHeight(at.xz) + 0.012f;
            // A darker, damper shade of the ground; snow prints stay pale blue-grey.
            var colour = surface.Snow > 0.5f ? new float3(0.62f, 0.68f, 0.78f) : surface.Albedo * 0.55f;
            Add(new Trace
            {
                Position = at,
                Rotation = quaternion.LookRotationSafe(forward, new float3(0f, 1f, 0f)),
                Length = size * 1.2f,
                Radius = size,
                Flatten = 0.06f,
                Color = new float4(colour, 0f),
                Born = now,
                Life = FootprintLife
            });
        }

        /// <summary>A feather drifts to the ground when a bird finishes preening or shaking.</summary>
        void Feather(Entity e, in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float now)
        {
            if (b.Habitat != Habitat.Flies)
                return;
            var action = (byte)s.Action;
            actions.TryGetValue(e, out var was);
            actions[e] = action;
            if (was == action || (was != (byte)IdleAction.Preen && was != (byte)IdleAction.Shake))
                return;
            // Where it falls: under the bird, a little to one side; tinted like the bird's coat (its first segment).
            var seed = math.hash(new float2(now, e.Index));
            var rng = new Unity.Mathematics.Random(seed | 1u);
            var at = s.Position + new float3(rng.NextFloat(-0.3f, 0.3f), 0f, rng.NextFloat(-0.3f, 0.3f)) * s.Scale;
            at.y = terrain.SampleHeight(at.xz) + 0.01f;
            var colour = b.SegmentColor(0, 0);
            Add(new Trace
            {
                Position = at,
                Rotation = quaternion.LookRotationSafe(new float3(math.cos(rng.NextFloat(6.28f)), 0.05f, math.sin(rng.NextFloat(6.28f))), new float3(0f, 1f, 0f)),
                Length = 0.06f * math.max(s.Scale, 0.8f),
                Radius = 0.008f * math.max(s.Scale, 0.8f),
                Flatten = 0.3f,
                Color = new float4(math.lerp(colour.xyz, new float3(0.9f, 0.88f, 0.84f), 0.3f), 0f),
                Born = now,
                Life = FeatherLife
            });
        }

        /// <summary>Writes the live traces as capsule instances, shrinking each away over the last fifth of its life.</summary>
        public int Write(NativeArray<SegmentInstance> output, int at, float now)
        {
            for (int i = 0; i < Count; i++)
            {
                var t = Traces[i];
                var left = t.Born + t.Life - now;
                var fade = math.saturate(left / (t.Life * 0.2f));
                output[at++] = left <= 0f ? default : new SegmentInstance
                {
                    Position = t.Position,
                    Rotation = t.Rotation,
                    Length = t.Length * fade,
                    RadiusStart = t.Radius * fade,
                    RadiusEnd = t.Radius * fade,
                    Flatten = t.Flatten,
                    Color = t.Color
                };
            }
            return at;
        }
    }
}
