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
            /// <summary>Thrown things (splash droplets, foam, mist): where it's going, and whether gravity takes it
            /// (1) down to Floor, where it's gone; how fast it grows (per second, of its size).</summary>
            public float3 Velocity;
            public float Falls, Floor, Grow;
            /// <summary>Above 0: see-through by this much (dithered), fading out rather than shrinking at the end.</summary>
            public float Dither;
        }

        const float Gravity = 9.81f;

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
        /// <summary>Per sky serpent: whether it was under the water last frame (a splash where it crosses).</summary>
        NativeHashMap<Entity, bool> under;

        protected override void OnCreate()
        {
            Traces = new NativeArray<Trace>(Capacity, Allocator.Persistent);
            steps = new NativeHashMap<Entity, float4>(256, Allocator.Persistent);
            actions = new NativeHashMap<Entity, byte>(256, Allocator.Persistent);
            under = new NativeHashMap<Entity, bool>(8, Allocator.Persistent);
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
        }

        protected override void OnDestroy()
        {
            if (Traces.IsCreated) Traces.Dispose();
            if (steps.IsCreated) steps.Dispose();
            if (actions.IsCreated) actions.Dispose();
            if (under.IsCreated) under.Dispose();
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
                ref var b = ref bodies[i].Blob.Value;
                // (Huge animals seen from far off leave their marks as far: a serpent's splash across the lake.)
                var range = math.max(Range, b.SeenFrom);
                if (math.distancesq(s.Position, camPos) > range * range)
                {
                    steps.Remove(entities[i]);
                    continue;
                }
                if (b.Breaches)
                {
                    Splash(entities[i], s, ref b, terrain, now);
                    continue;
                }
                Feather(entities[i], s, ref b, terrain, now);
                if (!Wake(entities[i], s, ref b, terrain, now))
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

        /// <summary>
        /// A print where each foot comes down (the moment its leg's cycle turns from swing to stance), at that foot as
        /// the animator poses it, where the ground takes one. Legless bodies (snakes) leave none.
        /// </summary>
        void Footprint(Entity e, in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float now)
        {
            if (b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats || s.Fly > 0.5f || s.Lie > 0.5f || s.Speed < 0.05f)
            {
                steps.Remove(e);
                return;
            }
            // (The body's gait phase last frame, to see which feet came down since.)
            if (!steps.TryGetValue(e, out var last) || last.w < 0f)
            {
                steps[e] = new float4(s.Position, s.Phase);
                return;
            }
            steps[e] = new float4(s.Position, s.Phase);
            // Only when a foot came down this frame is the ground looked at (most frames none does).
            var touchdown = false;
            for (int i = 0; i < b.Parts.Length && !touchdown; i++)
            {
                ref var leg = ref b.Parts[i];
                if (leg.Role != LimbRole.Leg || leg.Parent >= 0 || leg.SegmentCount == 0)
                    continue;
                var before = math.frac(last.w + leg.Phase);
                var after = math.frac(s.Phase + leg.Phase);
                touchdown = after < before && before - after >= 0.5f;
            }
            if (!touchdown)
                return;
            if (s.Position.y - terrain.SampleHeight(s.Position.xz) > 0.3f)
                return;   // up a tree, on a branch, in a cave above the surface, mid-jump
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var right = new float3(forward.z, 0f, -forward.x);
            var scale = math.max(s.Scale, 0.1f);
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var was = math.frac(last.w + part.Phase);
                var now0 = math.frac(s.Phase + part.Phase);
                if (now0 >= was || was - now0 < 0.5f)
                    continue;   // no touchdown (its cycle didn't wrap round to stance this frame)
                var foot = CreatureMotion.FootAt(ref b, ref part, now0, s.Speed) * scale;
                Print(s, ref b, terrain, s.Position + right * foot.x + forward * foot.z, forward, now);
            }
        }

        void Print(in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float3 at, float3 forward, float now)
        {
            var ground = terrain.SampleGround(at.xz);

            // Soft ground only: snow, bare sand or earth (little grass, few pebbles), or mud at the water's edge.
            var surface = terrain.SampleSurface(at.xz, ground, terrain.SampleNormal(at.xz, 1f));
            var bare = (1f - math.saturate(surface.Grass * 2.5f)) * (1f - math.saturate(surface.Pebbles * 1.5f)) * (1f - surface.Metallic);
            var mud = ground.HasWater && ground.WaterSurface - ground.Height > -1.2f ? 1f : 0f;
            var soft = math.max(surface.Snow, math.max(bare * 0.8f, mud));
            if (soft < 0.5f || ground.HasWater && ground.WaterSurface > ground.Height + 0.02f)
                return;
            var size = math.clamp(b.FrontRadius * s.Scale * 0.35f, 0.02f, 0.25f);
            at.y = terrain.SampleHeight(at.xz) + 0.012f;
            // A darker, damper shade of the ground; snow prints stay pale blue-grey. On glowing ground the print glows
            // too, a little less than the ground round it (the creature shader's glow is colour × alpha × about 2.2),
            // so it reads as a pressed print, not a dark hole.
            var colour = surface.Snow > 0.5f ? new float3(0.62f, 0.68f, 0.78f) : surface.Albedo * 0.55f;
            // (Ramped in from faint glow, so dimly hot ground doesn't get dark prints: about two-thirds of the ground's
            // own glow, which the terrain adds at its emission × 1.)
            var glow = math.saturate(math.cmax(surface.Emission) / 0.04f);
            if (glow > 0f)
                colour = math.lerp(colour, colour * 0.5f + surface.Emission * 0.3f / glow, glow);
            Add(new Trace
            {
                Position = at,
                Rotation = quaternion.LookRotationSafe(forward, new float3(0f, 1f, 0f)),
                Length = size * 1.2f,
                Radius = size,
                Flatten = 0.06f,
                Color = new float4(colour, glow),
                Born = now,
                Life = FootprintLife
            });
        }

        const float WakeLife = 2.2f;

        /// <summary>
        /// A land animal swimming leaves a V of ripples behind its head: a pair of pale streaks on the surface every
        /// little way, angled back either side, that spread and fade in a couple of seconds. True while it's in the water.
        /// </summary>
        bool Wake(Entity e, in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float now)
        {
            if (!b.CrossesWater)
                return false;
            var g = terrain.SampleGround(s.Position.xz);
            if (!g.HasWater || s.Position.y > g.WaterSurface - b.StandHeight * s.Scale * 0.5f)
                return false;
            if (s.Speed < 0.1f)
                return true;
            var spacing = math.max(b.BodyLength * s.Scale * 0.35f, 0.15f);
            if (steps.TryGetValue(e, out var last) && math.distance(last.xyz, s.Position) < spacing)
                return true;
            steps[e] = new float4(s.Position, -1f);   // (-1: a wake's, not a gait phase)
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var head = s.Position + forward * b.BodyLength * s.Scale * 0.6f;
            var width = math.max(b.FrontRadius * s.Scale, 0.05f);
            for (int side = -1; side <= 1; side += 2)
            {
                // Back and out from the head at about 30°.
                var back = math.normalize(-forward + new float3(forward.z, 0f, -forward.x) * side * 0.6f);
                var at = new float3(head.x, g.WaterSurface + 0.015f, head.z) + back * width;
                Add(new Trace
                {
                    Position = at,
                    Rotation = quaternion.LookRotationSafe(back, new float3(0f, 1f, 0f)),
                    Length = width * 2.2f,
                    Radius = width * 0.18f,
                    Flatten = 0.12f,
                    Color = new float4(0.82f, 0.88f, 0.92f, 0f),
                    Born = now,
                    Life = WakeLife
                });
            }
            return true;
        }

        /// <summary>
        /// A sky serpent bursting out of the water or diving back in: where it crosses the surface, a burst of round,
        /// see-through droplets thrown up and out that arc over and fall back into the water, a ring of white foam
        /// spreading out across the surface and fading, and a few soft puffs of mist rising off it, all sized to the
        /// serpent and over in a few seconds. (Thrown once; each frame just works out where everything is by now.)
        /// </summary>
        void Splash(Entity e, in CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float now)
        {
            var g = terrain.SampleGround(s.Position.xz);
            var isUnder = g.HasWater && s.Position.y < g.WaterSurface;
            var known = under.TryGetValue(e, out var was);
            under[e] = isUnder;
            if (!known || was == isUnder || !g.HasWater)
                return;
            var size = math.max(b.FrontRadius * s.Scale, 0.2f);
            var rng = new Unity.Mathematics.Random(math.hash(new float2(now, e.Index)) | 1u);
            var centre = new float3(s.Position.x, g.WaterSurface, s.Position.z);
            // (Water-tinted and see-through; only the odd one catches the light white.)
            var water = new float4(0.55f, 0.7f, 0.74f, 0f);
            var crest = new float4(0.95f, 0.97f, 0.98f, 0.25f);
            var foam = new float4(0.94f, 0.96f, 0.97f, 0f);
            // Droplets: up and out, as fast as the serpent is big (a few metres high for one this size), and a column of
            // them straight up from where it broke the surface; each drawn as a streak along the way it's flying.
            var lift = 5f + 3f * math.sqrt(size);
            for (int k = 0; k < 110; k++)
            {
                var column = k < 24;
                var out0 = rng.NextFloat2Direction();
                var up = lift * (column ? rng.NextFloat(1.1f, 1.6f) : rng.NextFloat(0.4f, 1.1f));
                var r = size * rng.NextFloat(0.04f, 0.11f);
                Add(new Trace
                {
                    Position = centre + new float3(out0.x, 0f, out0.y) * size * (column ? rng.NextFloat(0f, 0.4f) : rng.NextFloat(0.3f, 1.2f)),
                    Rotation = quaternion.identity,
                    Length = r,
                    Radius = r,
                    Flatten = 1f,
                    Color = rng.NextFloat() < 0.15f ? crest : water,
                    Born = now,
                    Life = 2f * up / Gravity + 0.1f,
                    Velocity = new float3(out0.x, 0f, out0.y) * (column ? rng.NextFloat(0.2f, 0.8f) : rng.NextFloat(1f, 4f)) * math.sqrt(size) + new float3(0f, up, 0f),
                    Falls = 1f,
                    Floor = g.WaterSurface - r,
                    Dither = 0.4f
                });
            }
            // Foam: patches of it in a broken ring round the splash, thin and see-through, spreading out and gone in
            // a second or two.
            const int ring = 22;
            for (int k = 0; k < ring; k++)
            {
                if (rng.NextFloat() < 0.4f)
                    continue;
                var angle = (k + rng.NextFloat(-0.3f, 0.3f)) * math.PI * 2f / ring;
                var out0 = new float3(math.cos(angle), 0f, math.sin(angle));
                var start = size * rng.NextFloat(1f, 1.4f);
                Add(new Trace
                {
                    Position = centre + new float3(0f, 0.03f, 0f) + out0 * start,
                    Rotation = quaternion.LookRotationSafe(new float3(-out0.z, 0f, out0.x), new float3(0f, 1f, 0f)),
                    Length = 2f * math.PI * start / ring * rng.NextFloat(0.4f, 0.9f),
                    Radius = size * rng.NextFloat(0.03f, 0.06f),
                    Flatten = 0.1f,
                    Color = foam,
                    Born = now,
                    Life = rng.NextFloat(1.2f, 1.9f),
                    Velocity = out0 * (1.5f + size) * rng.NextFloat(0.8f, 1.1f),
                    Grow = (1.5f + size) / start,
                    Dither = 0.5f
                });
            }
            // Mist: a low, wide, faint haze hugging the water round the splash, spreading and thinning.
            for (int k = 0; k < 6; k++)
            {
                var out0 = rng.NextFloat2Direction();
                var off = out0 * size * rng.NextFloat(0.5f, 1.6f);
                Add(new Trace
                {
                    Position = centre + new float3(off.x, 0.15f + size * 0.1f, off.y),
                    Rotation = quaternion.LookRotationSafe(new float3(out0.x, 0f, out0.y), new float3(0f, 1f, 0f)),
                    Length = size * 1.2f,
                    Radius = size * rng.NextFloat(0.9f, 1.4f),
                    Flatten = 0.22f,
                    Color = foam,
                    Born = now,
                    Life = 2.6f,
                    Velocity = new float3(off.x, 0.25f, off.y) * 0.5f,
                    Grow = 0.45f,
                    Dither = 0.9f
                });
            }
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
                if (left <= 0f)
                {
                    output[at++] = default;
                    continue;
                }
                var fade = math.saturate(left / (t.Life * 0.2f));
                if (t.Dither <= 0f)
                {
                    output[at++] = new SegmentInstance
                    {
                        Position = t.Position,
                        Rotation = t.Rotation,
                        Length = t.Length * fade,
                        RadiusStart = t.Radius * fade,
                        RadiusEnd = t.Radius * fade,
                        Flatten = t.Flatten,
                        Color = t.Color
                    };
                    continue;
                }
                // Thrown: where it is by now, and how big; gone once it falls back below its floor.
                var age = now - t.Born;
                var position = t.Position + t.Velocity * age - new float3(0f, 0.5f * Gravity * t.Falls * age * age, 0f);
                if (t.Falls > 0f && age > 0.05f && position.y < t.Floor)
                {
                    output[at++] = default;
                    continue;
                }
                var grow = 1f + t.Grow * age;
                // A falling drop is a streak along the way it's going, drawn out the faster it flies, tapering behind.
                var rotation = t.Rotation;
                var length = t.Length * grow;
                var tail = t.Radius * grow;
                if (t.Falls > 0f)
                {
                    var velocity = t.Velocity - new float3(0f, Gravity * age, 0f);
                    var speed = math.length(velocity);
                    rotation = quaternion.LookRotationSafe(velocity / math.max(speed, 1e-3f), new float3(1f, 0f, 0f));
                    length += speed * 0.035f;
                    tail *= 0.35f;
                    position -= velocity / math.max(speed, 1e-3f) * length;
                }
                output[at++] = new SegmentInstance
                {
                    Position = position,
                    Rotation = rotation,
                    Length = length,
                    RadiusStart = tail,
                    RadiusEnd = t.Radius * grow,
                    Flatten = t.Flatten,
                    Pad = math.lerp(1f, t.Dither, fade),
                    Color = t.Color
                };
            }
            return at;
        }
    }
}
