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
        MaterialPropertyBlock props, flatProps;
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        static readonly int SegmentOffsetId = Shader.PropertyToID("_SegmentOffset");
        const int MarkCount = 3;
        const int Spokes = 12, Rings = 6, Anchors = 4;
        const int WebCount = Spokes + Spokes * Rings + Anchors;
        const int MoundCount = 7;
        /// <summary>Capsules per nest: its floor, a ring of twigs (or grass), and the darkening under it.</summary>
        const int NestCount = 20;
        const int NestRim = 14;
        /// <summary>Room for the animal's lair: a burrow's mound or a nest.</summary>
        static int LairCount(ref CreatureBlob b) => b.Burrows ? MoundCount : b.Nests != NestSite.None ? NestCount : 0;
        /// <summary>Capsules in a lantern (bail, cap, glass, base), and room for whatever is held.</summary>
        const int LanternParts = 4;
        /// <summary>A lantern's light in the glow list: warm (linear rgb × intensity), w its reach (m); and how near the
        /// camera a lantern has to be to light anything.</summary>
        static readonly UnityEngine.Vector4 LanternLight = new UnityEngine.Vector4(1.6f, 0.9f, 0.4f, 9f);
        /// <summary>A torch's: redder, brighter and further.</summary>
        static readonly UnityEngine.Vector4 TorchLight = new UnityEngine.Vector4(2.4f, 1.0f, 0.3f, 11f);
        const float LanternLightRange = 60f;
        /// <summary>How far off a torch's flame is lit (further than its light reaches: a torch is seen across a town).</summary>
        const float TorchFlameRange = 150f;
        /// <summary>How near the camera feet are fitted to the ground (CreatureAnimator.Evaluate's detail).</summary>
        const float FootDetailRange = 40f;
        /// <summary>A torch's flame: its height (m), and how far it streams back as the bearer moves (m at the tip per
        /// m/s, at most).</summary>
        const float TorchFlameSize = 0.35f, TorchFlameLean = 0.05f, TorchFlameMaxLean = 0.3f;
        /// <summary>
        /// Capsules in a guard's kit: what it holds (a weapon, or a torch), a shield or a bow on its back, its helmet,
        /// breastplate, sash and shoulder pads. The torch's handle is always the first (its flame is lit from it).
        /// </summary>
        const int GuardParts = 14;
        /// <summary>Capsules in a miner's gear: its pick (the haft, the head's two points) and a sack of ore.</summary>
        const int MinerParts = 4;
        /// <summary>When in a throw (its ActionTime) the stone leaves the hand: TraceSystem throws it from there.</summary>
        public const float ThrowRelease = 0.3f;

        /// <summary>Capsules in a fisher's gear: the rod, the line, the float, a fish, and the basket (its staves, its
        /// rim, the dark inside and the base).</summary>
        const int BasketStaves = 8;
        const int RodParts = 4 + BasketStaves * 2 + 2;
        static int HeldCount(HeldItem held, CreatureTask task) => task == CreatureTask.Hunt && (held == HeldItem.Spear || held == HeldItem.SpearDrawn) ? 0 :
                                               held == HeldItem.Lantern ? LanternParts : held == HeldItem.Spear || held == HeldItem.SpearDrawn || held == HeldItem.Torch ? GuardParts :
                                               held == HeldItem.Rod ? RodParts : held == HeldItem.Stick || held == HeldItem.Stone ? 1 :
                                               held == HeldItem.Pickaxe || held == HeldItem.OreSack ? MinerParts : held == HeldItem.Staff || held == HeldItem.Letter ? 2 : held == HeldItem.Honey ? 1 : 0;
        /// <summary>Where a held item's light comes from among its capsules (the lantern's glass, the torch's flame).</summary>
        static int LightPart(HeldItem held) => held == HeldItem.Lantern ? 2 : held == HeldItem.Torch ? 0 : -1;
        /// <summary>Capsules per hanging bat, the most per roost, and how near the camera colonies show.</summary>
        const int BatParts = 4, ColonyMax = 10;
        const float ColonyRange = 45f;
        Unity.Collections.NativeList<int> roosts;
        /// <summary>
        /// Bees at the wild hives near the camera (Hive anchors): drawn only, like the colonies, never simulated or
        /// sent. By day SwarmRound circle the comb and SwarmForagers fly out to the ground near it and back; at night a
        /// few crawl on the comb. Someone coming close sets SwarmChasers on them for a few seconds.
        /// </summary>
        const int SwarmRound = 26, SwarmForagers = 6, SwarmNight = 8, SwarmChasers = 9;
        const float SwarmRange = 40f, SwarmAngerDistance = 5f, SwarmChaseTime = 6f, SwarmCalmTime = 20f;
        Unity.Collections.NativeList<int> hives;
        /// <summary>When each hive (by anchor index) last set its bees on someone: per machine, for looks only.</summary>
        readonly System.Collections.Generic.Dictionary<int, float> hiveAngry = new System.Collections.Generic.Dictionary<int, float>();
        /// <summary>The bear (entity index) a hive's bees are after, if it was a bear that set them off.</summary>
        readonly System.Collections.Generic.Dictionary<int, int> hiveRaider = new System.Collections.Generic.Dictionary<int, int>();

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
            RequireForUpdate(query);
        }

        protected override void OnDestroy()
        {
            if (roosts.IsCreated) roosts.Dispose();
            if (hives.IsCreated) hives.Dispose();
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
        /// Writes a roost's sleeping bats (count of them): each hangs head down from the ceiling, wings wrapped round, at
        /// a spot spread over the roost picked from its index.
        /// </summary>
        static int Colony(in Anchor roost, int index, int count, float faded, NativeArray<SegmentInstance> output, int at)
        {
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
                output[at++] = Part(top, hang, 0.09f * size, 0.032f * size, 0.026f * size, 1f, fur, faded);
                output[at++] = Part(top + down * 0.1f * size, hang, 0.015f * size, 0.022f * size, 0.02f * size, 1f, fur, faded);
                for (int w = -1; w <= 1; w += 2)
                    output[at++] = Part(top + side * w * 0.025f * size, hang, 0.1f * size, 0.03f * size, 0.02f * size, 0.25f, wing, faded);
            }
            return at;
        }

        /// <summary>
        /// Writes a hive's bees, one tiny capsule each, along the way it's flying: by day SwarmRound weaving round the
        /// comb (on the trunk, the way the anchor faces) and SwarmForagers each out to a spot on the ground 8 to 20 m off and
        /// back on its own round trip, hovering there a while; at night SwarmNight crawling slowly over the comb.
        /// Angry (seconds since someone came too close, while under SwarmChaseTime), SwarmChasers of the round leave it
        /// to buzz round that one's head, and go back.
        /// </summary>
        static int Swarm(in Anchor hive, int index, bool day, float now, float angry, float3 player, float faded,
                         TerrainSource terrain, NativeArray<SegmentInstance> output, int at)
        {
            var rng = new Unity.Mathematics.Random(math.hash(new int2(index, 0xB33)) | 1u);
            var colour = new float4(0.5f, 0.33f, 0.04f, 0f);
            // (The comb hangs on the trunk the way the anchor faces: its Size is the trunk's radius and a metre.)
            var trunk = math.max(hive.Size - 1f, 0.1f);
            var comb = hive.Position + new float3(hive.Facing.x, 0f, hive.Facing.y) * (trunk + 0.3f);
            var count = day ? SwarmRound + SwarmForagers : SwarmNight;
            var up = new float3(0f, 1f, 0f);
            for (int k = 0; k < count; k++)
            {
                var phase = rng.NextFloat(0f, 100f);
                var speed = rng.NextFloat(1.5f, 3f) * (rng.NextBool() ? 1f : -1f);
                var reach = rng.NextFloat(0.3f, 1.1f);
                float3 at3, ahead;
                if (!day)
                {
                    // Crawling on the comb.
                    var a = phase + now * 0.15f * speed;
                    at3 = comb + new float3(math.cos(a) * 0.2f, math.sin(a * 1.3f) * 0.3f, math.sin(a) * 0.2f);
                    ahead = new float3(-math.sin(a), 0f, math.cos(a));
                }
                else if (k >= SwarmRound)
                {
                    // A forager's round trip: out (the first 40%), round the flower it's at (to 60%), and back.
                    var trip = rng.NextFloat(20f, 40f);
                    var u = math.frac(now / trip + phase);
                    var dir = rng.NextFloat2Direction();
                    var spot2 = comb.xz + dir * rng.NextFloat(8f, 20f);
                    var spot = new float3(spot2.x, terrain.SampleHeight(spot2) + 0.4f, spot2.y);
                    var out_ = math.smoothstep(0f, 0.4f, u) * (1f - math.smoothstep(0.6f, 1f, u));
                    var lift = math.sin(out_ * math.PI) * 1.5f;
                    at3 = math.lerp(comb, spot, out_) + up * lift;
                    var hover = now * 4f + phase;
                    at3 += new float3(math.cos(hover), math.sin(hover * 1.7f) * 0.5f, math.sin(hover)) * 0.12f;
                    ahead = math.normalizesafe((spot - comb) * (u < 0.5f ? 1f : -1f), new float3(1f, 0f, 0f));
                }
                else
                {
                    // Weaving round the comb.
                    var a = phase + now * speed;
                    var r = 0.35f + 0.9f * reach;
                    at3 = comb + new float3(math.cos(a) * r, math.sin(a * 1.9f + phase) * 0.35f, math.sin(a) * r);
                    ahead = new float3(-math.sin(a), 0f, math.cos(a)) * math.sign(speed);
                    // Angry: off round the head of whoever came close, and back.
                    if (k < SwarmChasers && angry >= 0f && angry < SwarmChaseTime)
                    {
                        var go = math.smoothstep(0f, 0.8f, angry) * (1f - math.smoothstep(SwarmChaseTime - 1.2f, SwarmChaseTime, angry));
                        var b = now * 6f + phase;
                        var head = player + new float3(math.cos(b) * 0.45f, 1.45f + math.sin(b * 1.4f) * 0.25f, math.sin(b) * 0.45f);
                        at3 = math.lerp(at3, head, go);
                        ahead = math.lerp(ahead, new float3(-math.sin(b), 0f, math.cos(b)), go);
                    }
                }
                output[at++] = Part(at3 - math.normalizesafe(ahead, new float3(1f, 0f, 0f)) * 0.009f, quaternion.LookRotationSafe(math.normalizesafe(ahead, new float3(1f, 0f, 0f)), up),
                                    0.028f, 0.011f, 0.009f, 1f, colour, faded);
            }
            return at;
        }

        static SegmentInstance Part(float3 from, quaternion rotation, float length, float r0, float r1, float flatten, float4 colour,
                                    float faded = 0f) =>
            new SegmentInstance
            {
                Position = from,
                Rotation = rotation,
                Length = length,
                RadiusStart = r0,
                RadiusEnd = r1,
                Flatten = flatten,
                Pad = faded,
                Color = colour
            };

        /// <summary>
        /// When each roost's colony was last scattered (roost index → time): someone waving, bowing, cheering or dancing
        /// near it sends its sleeping bats off. Every machine sees the same players' poses, so they all scatter it.
        /// </summary>
        readonly System.Collections.Generic.Dictionary<int, float> scattered = new System.Collections.Generic.Dictionary<int, float>();
        const float ScatterTime = 60f;

        /// <summary>How far out of sight a roost's colony is (0 there, 1 gone): gone in a second, back over a few.</summary>
        float ColonyFade(int roost, in Anchor anchor, NativeList<float3> emoting, float now)
        {
            foreach (var p in emoting)
                if (math.distance(p, anchor.Position) < 15f)
                {
                    if (!scattered.TryGetValue(roost, out var since) || now - since > ScatterTime)
                        scattered[roost] = now;
                }
            if (!scattered.TryGetValue(roost, out var at))
                return 0f;
            var t = now - at;
            if (t > ScatterTime + 4f)
            {
                scattered.Remove(roost);
                return 0f;
            }
            return math.saturate(t / 1f) * (1f - math.saturate((t - ScatterTime) / 4f));
        }

        /// <summary>A spider sitting in its web (only then is the web drawn, and room made for it).</summary>
        static bool InWeb(in CreatureState s) => s.Mode == CreatureMode.Web && s.Stage >= 2 && s.Stage <= 4;

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<FaunaRenderSettings>(out var settings) || settings.Material == null || settings.Capsule == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;

            // (Copied out once: the counts below and the pose job both read these.)
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
            var states = query.ToComponentDataArray<CreatureState>(Allocator.TempJob);
            var entities = query.ToEntityArray(Allocator.TempJob);
            // Deep in a cave, out of sight of every mouth (CaveSystem.SurfaceHidden): the creatures up on the surface are
            // neither posed nor drawn. (Players' bodies always are.)
            if (Punchy.Terrain.CaveSystem.SurfaceHidden >= 0.99f && SystemAPI.TryGetSingleton<TerrainSource>(out var hiddenFrom))
            {
                var keep = 0;
                for (int i = 0; i < bodies.Length; i++)
                    if (states[i].Mode == CreatureMode.Puppet || states[i].Position.y < hiddenFrom.SampleHeight(states[i].Position.xz) - 1f)
                    {
                        bodies[keep] = bodies[i];
                        states[keep] = states[i];
                        entities[keep] = entities[i];
                        keep++;
                    }
                if (keep < bodies.Length)
                {
                    var b2 = new NativeArray<CreatureBody>(keep, Allocator.TempJob);
                    var s2 = new NativeArray<CreatureState>(keep, Allocator.TempJob);
                    var e2 = new NativeArray<Entity>(keep, Allocator.TempJob);
                    NativeArray<CreatureBody>.Copy(bodies, b2, keep);
                    NativeArray<CreatureState>.Copy(states, s2, keep);
                    NativeArray<Entity>.Copy(entities, e2, keep);
                    bodies.Dispose();
                    states.Dispose();
                    entities.Dispose();
                    bodies = b2;
                    states = s2;
                    entities = e2;
                }
            }
            var offsets = new NativeArray<int>(bodies.Length, Allocator.TempJob);
            var hanging = new NativeList<float3>(8, Allocator.Temp);
            var emoting = new NativeList<float3>(2, Allocator.Temp);
            var total = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                offsets[i] = total;
                total += bodies[i].SegmentCount + MarkCount + (InWeb(states[i]) ? WebCount : 0) +
                         LairCount(ref bodies[i].Blob.Value) + (states[i].Task == CreatureTask.Hunt ? 1 : 0) + HeldCount(states[i].Held, states[i].Task);
                if (states[i].Mode == CreatureMode.Roost && states[i].Stage == 200)
                    hanging.Add(states[i].Position);
                // (Bodies of players emoting: their gestures scatter colonies.)
                if (states[i].Mode == CreatureMode.Puppet && states[i].Action >= IdleAction.Wave)
                    emoting.Add(states[i].Position);
            }

            // Colonies at the roosts near the camera.
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            if (!roosts.IsCreated)
                roosts = new Unity.Collections.NativeList<int>(16, Allocator.Persistent);
            roosts.Clear();
            if (terrain.Anchors.IsCreated)
                Punchy.Terrain.Anchors.Near(ref terrain.Anchors.Value, cam.transform.position, ColonyRange, AnchorKind.Roost, ref roosts);
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            // (Less any real bats hanging there now, so they don't double up with the drawn ones.)
            var colonyStart = total;
            var colonySizes = new NativeArray<int>(roosts.Length, Allocator.Temp);
            var colonyFades = new NativeArray<float>(roosts.Length, Allocator.Temp);
            var now = (float)SystemAPI.Time.ElapsedTime;
            for (int i = 0; i < roosts.Length; i++)
            {
                var roost = terrain.Anchors.Value.Anchors[roosts[i]];
                var real = 0;
                foreach (var p in hanging)
                    if (math.distance(p, roost.Position) < roost.Size + 0.6f)
                        real++;
                colonyFades[i] = ColonyFade(roosts[i], roost, emoting, now);
                colonySizes[i] = colonyFades[i] >= 1f ? 0 : math.max(ColonySize(roost, roosts[i], night) - real, 0);
                total += colonySizes[i] * BatParts;
            }
            hanging.Dispose();
            emoting.Dispose();

            // Bees at the hives near the camera, set on whoever comes too close by day.
            if (!hives.IsCreated)
                hives = new Unity.Collections.NativeList<int>(4, Allocator.Persistent);
            hives.Clear();
            if (terrain.Anchors.IsCreated)
                Punchy.Terrain.Anchors.Near(ref terrain.Anchors.Value, cam.transform.position, SwarmRange, AnchorKind.Hive, ref hives);
            var day = night < 0.5f;
            var swarmPlayer = new float3(float.MaxValue);
            foreach (var t in SystemAPI.Query<RefRO<Unity.Transforms.LocalTransform>>().WithAll<Punchy.Player.PlayerTag>())
                swarmPlayer = t.ValueRO.Position;
            var swarmTargets = new NativeArray<float3>(hives.Length, Allocator.Temp);
            for (int i = 0; i < hives.Length; i++)
            {
                var hive = terrain.Anchors.Value.Anchors[hives[i]];
                var last = hiveAngry.TryGetValue(hives[i], out var since) ? since : -1000f;
                swarmTargets[i] = swarmPlayer;
                // A bear at the comb: mobbed for as long as it's there (and a few seconds after, as it makes off).
                var raider = -1;
                for (int k = 0; k < states.Length && raider < 0; k++)
                    if (states[k].Mode == CreatureMode.Work && (states[k].Stage == CreatureAnimator.RaidAtComb || states[k].Stage == CreatureAnimator.RaidLick) &&
                        math.distance(states[k].Position.xz, hive.Position.xz) < hive.Size + 2f)
                        raider = k;
                if (raider >= 0)
                {
                    hiveAngry[hives[i]] = now - 1f;
                    hiveRaider[hives[i]] = entities[raider].Index;
                }
                else if (day && now - last > SwarmChaseTime + SwarmCalmTime && math.distance(swarmPlayer, hive.Position) < SwarmAngerDistance + hive.Size)
                {
                    hiveAngry[hives[i]] = now;
                    hiveRaider.Remove(hives[i]);
                }
                // (Still after the bear as it goes, while the chase lasts.)
                if (hiveRaider.TryGetValue(hives[i], out var bearIndex))
                    for (int k = 0; k < states.Length; k++)
                        if (entities[k].Index == bearIndex)
                            swarmTargets[i] = states[k].Position;
                total += day ? SwarmRound + SwarmForagers : SwarmNight;
            }

            // Footprints and feathers (TraceSystem).
            var traces = World.GetExistingSystemManaged<TraceSystem>();
            var traceCount = traces != null ? traces.Count : 0;
            total += traceCount;

            var instances = new NativeArray<SegmentInstance>(total, Allocator.TempJob);
            // Over the copies, a few creatures per batch, so every worker takes a share (per chunk, it was one or two).
            new PoseJob
            {
                States = states,
                Bodies = bodies,
                Entities = entities,
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Offsets = offsets,
                Output = instances,
                Time = (float)SystemAPI.Time.ElapsedTime,
                CameraRight = cam.transform.right,
                CameraPosition = cam.transform.position,
                FadeInTime = 1.2f,
                FadeOutRange = 25f,
                // A little short of FaunaDirector's despawn distance (170 m), so it's gone before it's removed.
                FadeFar = 165f,
                Night = night
            }.Schedule(bodies.Length, 8).Complete();
            // Lanterns and torches near the camera light the ground round them: each joins the glow list as a moving
            // light (by its carrier, so its fade-in carries on as it moves), at the middle of a lantern's glass as just
            // posed, or a torch's flame. A torch's flame itself is the world's (FireSystem), lit each frame at the top
            // of its handle, streaming back as its bearer goes, and further off than its light reaches.
            float3 camAt = cam.transform.position;
            for (int i = 0; i < bodies.Length; i++)
            {
                var lightPart = LightPart(states[i].Held);
                var torch = states[i].Held == HeldItem.Torch;
                var far = math.distancesq(states[i].Position, camAt);
                if (lightPart < 0 || far > (torch ? TorchFlameRange * TorchFlameRange : LanternLightRange * LanternLightRange))
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
                    Punchy.Ambience.FireSystem.AddFlame(top, size, entities[i].Index, Punchy.Ambience.FireSystem.Wood, lean);
                    centre = top + new float3(0f, size * 0.4f, 0f);
                }
                else
                    centre = part.Position + math.mul(part.Rotation, new float3(0f, 0f, part.Length * 0.5f));
                if (far <= LanternLightRange * LanternLightRange)
                    Punchy.Terrain.CaveSystem.MovingLights.Add((entities[i].Index, centre, torch ? TorchLight : LanternLight));
            }
            bodies.Dispose();
            states.Dispose();
            entities.Dispose();
            var at = colonyStart;
            for (int i = 0; i < roosts.Length; i++)
                at = Colony(terrain.Anchors.Value.Anchors[roosts[i]], roosts[i], colonySizes[i], colonyFades[i], instances, at);
            colonySizes.Dispose();
            colonyFades.Dispose();
            for (int i = 0; i < hives.Length; i++)
            {
                var hive = terrain.Anchors.Value.Anchors[hives[i]];
                var angry = hiveAngry.TryGetValue(hives[i], out var since) ? now - since : -1f;
                var faded = math.saturate((math.distance(camAt, hive.Position) - SwarmRange * 0.75f) / (SwarmRange * 0.25f));
                at = Swarm(hive, hives[i], day, now, angry, swarmTargets[i], faded, terrain, instances, at);
            }
            swarmTargets.Dispose();
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

            // Two draws from the one buffer: the creatures, casting shadows; then the colonies and traces after them,
            // which don't (sleeping bats under a cave ceiling and prints on the ground gain nothing from a shadow pass).
            props ??= new MaterialPropertyBlock();
            props.SetBuffer(SegmentsId, buffer);
            props.SetInteger(SegmentOffsetId, 0);
            var rp = new RenderParams(settings.Material)
            {
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 2000f),
                matProps = props,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            };
            if (colonyStart > 0)
                Graphics.RenderMeshPrimitives(rp, settings.Capsule, 0, colonyStart);
            if (total > colonyStart)
            {
                flatProps ??= new MaterialPropertyBlock();
                flatProps.SetBuffer(SegmentsId, buffer);
                flatProps.SetInteger(SegmentOffsetId, colonyStart);
                rp.matProps = flatProps;
                rp.shadowCastingMode = ShadowCastingMode.Off;
                Graphics.RenderMeshPrimitives(rp, settings.Capsule, 0, total - colonyStart);
            }
        }

        [BurstCompile]
        struct PoseJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeArray<Entity> Entities;
            public TerrainSource Terrain;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<SegmentInstance> Output;
            public float Time;
            public Unity.Mathematics.float3 CameraRight, CameraPosition;
            /// <summary>Faded in over this long after it's made; out over this far before it's let go.</summary>
            public float FadeInTime, FadeOutRange, FadeFar;
            /// <summary>0 by day, 1 at night (ghosts are only seen at night).</summary>
            public float Night;

            public void Execute(int index)
            {
                var state = States[index];
                var body = Bodies[index];
                var entity = Entities[index];
                var offset = Offsets[index];
                // (Feet fitted to the ground only near the camera: past 40 m nobody can tell, and it's most of the cost.)
                CreatureAnimator.Evaluate(ref body.Blob.Value, state, body.Coat, Terrain, Output, offset,
                                          Unity.Mathematics.math.distancesq(state.Position, CameraPosition) < FootDetailRange * FootDetailRange);
                // (The ground it stands on, for the shader's grounding: darker where it meets it.)
                for (int i = 0; i < body.SegmentCount; i++)
                {
                    var seg = Output[offset + i];
                    seg.Material.y = state.Position.y;
                    Output[offset + i] = seg;
                }
                // Appearing (its first moments: Clock counts from when it was made, here) or nearing the distance
                // where it's let go: dithered in and out by the shader, so nothing pops.
                var fadeIn = Unity.Mathematics.math.saturate(state.Clock / FadeInTime);
                // (Huge animals, seen across the land, from their own distance.)
                var fadeFar = Unity.Mathematics.math.max(FadeFar, body.Blob.Value.SeenFrom);
                var fadeOut = Unity.Mathematics.math.saturate((fadeFar - Unity.Mathematics.math.distance(state.Position, CameraPosition)) / FadeOutRange);
                var faded = 1f - fadeIn * fadeOut;
                // Stepping through a door: dithered out going in, gone indoors, dithered back in coming out.
                if (state.Mode == CreatureMode.Home && state.Stage >= 1)
                    faded = Unity.Mathematics.math.max(faded, state.Stage == 2 ? 1f : state.ActionTime);
                // A ghost is only half there (shimmering), and not there at all by day.
                if (body.Blob.Value.Haunts)
                {
                    var shimmer = 0.42f + 0.1f * Unity.Mathematics.math.sin(Time * 1.3f + (state.Individual % 13));
                    var day = 1f - Unity.Mathematics.math.smoothstep(0.2f, 0.6f, Night);
                    faded = Unity.Mathematics.math.max(faded, Unity.Mathematics.math.max(shimmer, day));
                }
                var haunts = body.Blob.Value.Haunts;
                if (faded > 0f)
                    for (int i = 0; i < body.SegmentCount; i++)
                    {
                        var instance = Output[offset + i];
                        instance.Pad = faded;
                        // (A ghost glows faintly all over, so the whole of it shows in the dark.)
                        if (haunts)
                            instance.Color.w = Unity.Mathematics.math.max(instance.Color.w, 0.3f);
                        Output[offset + i] = instance;
                    }
                // A lava dweller's bones glow like embers, all over, pulsing slowly.
                if (body.Blob.Value.LavaDweller)
                    for (int i = 0; i < body.SegmentCount; i++)
                    {
                        var instance = Output[offset + i];
                        instance.Color.w = Unity.Mathematics.math.max(instance.Color.w, 0.28f + 0.08f * Unity.Mathematics.math.sin(Time * 1.7f + i * 0.6f));
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
                var held = lair + LairCount(ref body.Blob.Value) + (state.Task == CreatureTask.Hunt ? 1 : 0);
                if (state.Task == CreatureTask.Hunt)
                    Spear(state, ref body.Blob.Value, offset, held - 1);
                if (state.Held == HeldItem.Lantern)
                    Lantern(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Task == CreatureTask.Patrol && (state.Held == HeldItem.Spear || state.Held == HeldItem.SpearDrawn || state.Held == HeldItem.Torch))
                    Guard(state, ref body.Blob.Value, offset, held, faded, entity.Index);
                else if (state.Held == HeldItem.Rod)
                    Rod(state, ref body.Blob.Value, offset, held, faded, entity.Index);
                else if (state.Held == HeldItem.Stick || state.Held == HeldItem.Stone)
                    Plaything(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Pickaxe || state.Held == HeldItem.OreSack)
                    Miner(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Staff)
                    Traveller(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Letter)
                    Letter(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Honey)
                    Honey(state, ref body.Blob.Value, offset, held, faded);
            }

            /// <summary>
            /// The chunk of honeycomb a raiding bear knocked down: falling from up at the comb in its first half second,
            /// then lying on the ground at its nose while it licks at it.
            /// </summary>
            void Honey(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                var head = -1;
                for (int i = 0; i < b.Parts.Length && head < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Head)
                        head = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                if (head < 0)
                    return;
                var h = Output[offset + head];
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var nose = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length)) + forward * 0.08f;
                var ground = Terrain.SampleHeight(nose.xz);
                var fall = Unity.Mathematics.math.saturate(state.StageTime / 0.5f);
                var y = Unity.Mathematics.math.lerp(ground + 2.5f, ground + 0.05f, fall * fall);
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                Output[at] = Piece(new Unity.Mathematics.float3(nose.x, y, nose.z) - right * 0.1f, right, new Unity.Mathematics.float3(0f, 1f, 0f),
                                   0.2f, 0.11f, 0.09f, 0.55f, new Unity.Mathematics.float4(0.75f, 0.45f, 0.06f, 0.15f), faded, SegmentMaterial.Body);
            }

            /// <summary>
            /// A rolled message: a little roll on a messenger bird's leg, under its body; in the hands of one reading it,
            /// the roll across between the hands and the sheet hanging open from it, towards their face.
            /// </summary>
            void Letter(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                Output[at + 1] = default;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.1f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var paper = new Unity.Mathematics.float4(0.62f, 0.55f, 0.4f, 0f);
                if (!b.Upright)
                {
                    var body = Output[offset];
                    var leg = body.Position + Unity.Mathematics.math.mul(body.Rotation, new Unity.Mathematics.float3(0f, 0f, body.Length * 0.5f)) -
                              up * Unity.Mathematics.math.max(body.RadiusStart, body.RadiusEnd) * 0.9f;
                    Output[at] = Piece(leg - right * 0.025f, right, up, 0.05f, 0.012f, 0.012f, 1f, paper, faded, SegmentMaterial.Cloth);
                    return;
                }
                int hand = -1;
                for (int i = 0; i < b.Parts.Length && hand < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                if (hand < 0)
                    return;
                var h = Output[offset + hand];
                var grip = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.6f));
                // (The roll from this hand across to the other; the sheet down from it, tipped back towards the reader.)
                var rollFrom = grip + up * 0.02f * scale;
                Output[at] = Piece(rollFrom, -right, up, 0.2f * scale, 0.018f * scale, 0.018f * scale, 1f, paper, faded, SegmentMaterial.Cloth);
                var down = Unity.Mathematics.math.normalize(-up + forward * 0.35f);
                Output[at + 1] = Piece(rollFrom - right * 0.1f * scale, down, forward, 0.16f * scale, 0.09f * scale, 0.09f * scale, 0.08f,
                                       paper * 1.1f, faded, SegmentMaterial.Cloth);
            }

            /// <summary>A traveller's walking staff, planted ahead in the right hand, and a pack on its back.</summary>
            void Traveller(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                Output[at + 1] = default;
                int hand = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0 || chest < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var upward = new Unity.Mathematics.float3(0f, 1f, 0f);
                var h = Output[offset + hand];
                var grip = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.5f));
                var dir = Unity.Mathematics.math.normalize(upward + forward * 0.18f);
                var length = 1.7f * scale;
                Output[at] = Piece(grip - dir * length * 0.65f, dir, forward, length, 0.02f * scale, 0.016f * scale, 1f,
                                   new Unity.Mathematics.float4(0.24f, 0.17f, 0.09f, 0f), faded, SegmentMaterial.Wood);
                var torso = Output[offset + chest];
                var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                var back = -Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                Output[at + 1] = Piece(torso.Position + back * r * 1.15f, axis, back, torso.Length * 0.9f, r * 0.7f, r * 0.62f, 0.7f,
                                       new Unity.Mathematics.float4(0.3f, 0.2f, 0.11f, 0f), faded, SegmentMaterial.Leather);
            }

            /// <summary>
            /// A miner's gear: its pick across its back on the way in and out, in its hands swinging at the ore face
            /// (the head's two points out either side of the haft's end); and carrying ore out, a full sack hugged to its
            /// chest.
            /// </summary>
            void Miner(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < MinerParts; i++)
                    Output[at + i] = default;
                int hand = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0 || chest < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var wood = new Unity.Mathematics.float4(0.18f, 0.12f, 0.06f, 0f);
                var iron = new Unity.Mathematics.float4(0.3f, 0.3f, 0.32f, 0f);
                var torso = Output[offset + chest];
                var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                var length = 0.8f * scale;
                float3 butt, dir;
                if (state.Mode == CreatureMode.Work && state.Stage == CreatureAnimator.MinerAtFace && state.Held == HeldItem.Pickaxe)
                {
                    // In both hands, held at the end of the haft, along the arms' swing.
                    var h = Output[offset + hand];
                    dir = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    butt = h.Position + dir * h.Length * 0.4f - dir * 0.08f * scale;
                }
                else
                {
                    // Across the back, from low on the left to high on the right.
                    dir = Unity.Mathematics.math.normalize(axis + right * 0.75f);
                    butt = torso.Position + axis * torso.Length * 0.5f - forward * r * 1.3f - dir * length * 0.5f;
                }
                var tip = butt + dir * length;
                // (In the hands the head is in the swing's plane; worn on the back it lies across, pointing to either side.)
                var swinging = state.Mode == CreatureMode.Work && state.Stage == CreatureAnimator.MinerAtFace && state.Held == HeldItem.Pickaxe;
                var across = swinging ? Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(dir, right), forward)
                                      : Unity.Mathematics.math.normalizesafe(right - dir * Unity.Mathematics.math.dot(right, dir), right);
                Output[at] = Piece(butt, dir, right, length, 0.02f * scale, 0.022f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                // (A pick's head: a bar across the haft's top, its two points curving a little back towards the handle.)
                Output[at + 1] = Piece(tip, Unity.Mathematics.math.normalize(across - dir * 0.28f), right, 0.24f * scale, 0.03f * scale, 0.004f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                Output[at + 2] = Piece(tip, Unity.Mathematics.math.normalize(-across - dir * 0.28f), right, 0.2f * scale, 0.03f * scale, 0.006f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                if (state.Held == HeldItem.OreSack)
                {
                    // A lumpy sack of ore held to the chest, low.
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var at3 = torso.Position + front * r * 1.25f;
                    Output[at + 3] = Piece(at3 - axis * 0.06f * scale, axis, front, 0.14f * scale, 0.16f * scale, 0.12f * scale, 0.85f,
                                           new Unity.Mathematics.float4(0.36f, 0.3f, 0.2f, 0f), faded, SegmentMaterial.Cloth);
                }
            }

            /// <summary>
            /// A child's plaything in its right hand: a crooked stick held out ahead and down, or a stone in the fist
            /// (gone from the hand from the moment it's thrown until another's picked up).
            /// </summary>
            void Plaything(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
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
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length * 0.6f));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                if (state.Held == HeldItem.Stone)
                {
                    if (state.Action == IdleAction.Throw && state.ActionTime > ThrowRelease && state.ActionTime < 0.9f)
                        return;
                    Output[at] = new SegmentInstance { Position = grip, Rotation = segment.Rotation, Length = 0.03f * scale, RadiusStart = 0.06f * scale, RadiusEnd = 0.05f * scale, Flatten = 0.8f, Color = new Unity.Mathematics.float4(0.45f, 0.44f, 0.42f, 0f), Pad = faded };
                    return;
                }
                // The stick goes on from the hand the way the forearm points, dipping a little towards the ground.
                var along = Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var dir = Unity.Mathematics.math.normalizesafe(along + new Unity.Mathematics.float3(0f, -0.25f, 0f));
                Output[at] = new SegmentInstance
                {
                    Position = grip - dir * 0.06f * scale,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = 0.8f * scale,
                    RadiusStart = 0.014f * scale,
                    RadiusEnd = 0.009f * scale,
                    Flatten = 0.85f,
                    Color = new Unity.Mathematics.float4(0.24f, 0.16f, 0.08f, 0f), Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
            }

            /// <summary>
            /// A fisher's gear: the rod held out over the water (swung back over the shoulder and through in a cast,
            /// lifted high landing a fish), its line down to a float bobbing on the water where it fishes (WorkFace) or,
            /// landing one, to a fish wriggling on the hook; and a basket on the ground beside it.
            /// </summary>
            void Rod(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded, int id)
            {
                for (int i = 0; i < RodParts; i++)
                    Output[at + i] = default;
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
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length * 0.55f));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                // The rod's rise: out over the water; back over the shoulder in a cast; high, landing a fish.
                var casting = state.Stage == 2 ? CreatureAnimator.CastBack(state.ActionTime) : 0f;
                var rise = state.Stage == 3 ? 1.05f : Unity.Mathematics.math.lerp(0.55f, 2.3f, casting);
                var dir = forward * Unity.Mathematics.math.cos(rise) + new Unity.Mathematics.float3(0f, Unity.Mathematics.math.sin(rise), 0f);
                var length = 2.6f * scale;
                var butt = grip - dir * 0.35f * scale;
                var tip = butt + dir * length;
                var dark = new Unity.Mathematics.float4(0.18f, 0.12f, 0.06f, 0f);
                Output[at] = new SegmentInstance { Position = butt, Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, forward), Length = length, RadiusStart = 0.013f * scale, RadiusEnd = 0.004f * scale, Flatten = 1f, Color = dark, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f), Pad = faded };
                // The line: to the float on the water (not while the cast is in the air), or to a fish on the hook.
                var line = new Unity.Mathematics.float4(0.85f, 0.85f, 0.8f, 0f);
                Unity.Mathematics.float3 end;
                if (state.Stage == 3)
                {
                    end = tip - new Unity.Mathematics.float3(0f, 0.7f * scale, 0f);
                    var wriggle = Unity.Mathematics.math.sin(Time * 14f + id) * 0.6f;
                    var along = Unity.Mathematics.math.mul(Unity.Mathematics.quaternion.RotateY(wriggle), forward);
                    Output[at + 3] = new SegmentInstance
                    {
                        Position = end - along * 0.12f * scale,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, new Unity.Mathematics.float3(0f, 1f, 0f)),
                        Length = 0.24f * scale,
                        RadiusStart = 0.035f * scale,
                        RadiusEnd = 0.012f * scale,
                        Flatten = 0.55f,
                        Color = new Unity.Mathematics.float4(0.55f, 0.6f, 0.62f, 0.1f),
                        Pad = faded
                    };
                }
                else if (state.Stage == 2 && state.ActionTime < 0.75f)
                    end = tip;
                else
                {
                    var water = Terrain.SampleGround(state.WorkFace);
                    end = new Unity.Mathematics.float3(state.WorkFace.x, water.WaterSurface + 0.02f + 0.015f * Unity.Mathematics.math.sin(Time * 2.1f + id), state.WorkFace.y);
                    Output[at + 2] = new SegmentInstance
                    {
                        Position = end - new Unity.Mathematics.float3(0f, 0.03f, 0f),
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(new Unity.Mathematics.float3(0f, 1f, 0f), forward),
                        Length = 0.06f,
                        RadiusStart = 0.025f,
                        RadiusEnd = 0.025f,
                        Flatten = 1f,
                        Color = new Unity.Mathematics.float4(0.85f, 0.12f, 0.08f, 0f),
                        Pad = faded
                    };
                }
                var span = end - tip;
                var reach = Unity.Mathematics.math.length(span);
                if (reach > 0.02f)
                    Output[at + 1] = new SegmentInstance { Position = tip, Rotation = Unity.Mathematics.quaternion.LookRotationSafe(span / reach, forward), Length = reach, RadiusStart = 0.0035f, RadiusEnd = 0.0035f, Flatten = 1f, Color = line, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Cloth, 0f, 0f, 0f), Pad = faded };
                // The basket, on the ground at its side: an open wicker basket, a ring of staves flaring a little to a
                // rim, dark inside.
                var spot = state.Position.xz + right.xz * 0.45f * scale - forward.xz * 0.15f * scale;
                var foot = new Unity.Mathematics.float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                var radius = 0.14f * scale;
                var height = 0.2f * scale;
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wicker = new Unity.Mathematics.float4(0.42f, 0.3f, 0.14f, 0f);
                var weave = new Unity.Mathematics.float4(0.5f, 0.37f, 0.18f, 0f);
                for (int k = 0; k < BasketStaves; k++)
                {
                    var a = (k + 0.5f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var outward = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var bottom = foot + outward * radius * 0.85f;
                    var top = foot + outward * radius * 1.12f + up * height;
                    Output[at + 4 + k] = new SegmentInstance
                    {
                        Position = bottom,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(Unity.Mathematics.math.normalize(top - bottom), outward),
                        Length = Unity.Mathematics.math.distance(top, bottom),
                        RadiusStart = 0.045f * scale,
                        RadiusEnd = 0.05f * scale,
                        Flatten = 0.45f,
                        Color = wicker, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                        Pad = faded
                    };
                    var b0 = (k + 0f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var b1 = (k + 1f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var r0 = foot + up * height + new Unity.Mathematics.float3(Unity.Mathematics.math.cos(b0), 0f, Unity.Mathematics.math.sin(b0)) * radius * 1.14f;
                    var r1 = foot + up * height + new Unity.Mathematics.float3(Unity.Mathematics.math.cos(b1), 0f, Unity.Mathematics.math.sin(b1)) * radius * 1.14f;
                    Output[at + 4 + BasketStaves + k] = new SegmentInstance
                    {
                        Position = r0,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(Unity.Mathematics.math.normalize(r1 - r0), up),
                        Length = Unity.Mathematics.math.distance(r0, r1),
                        RadiusStart = 0.016f * scale,
                        RadiusEnd = 0.016f * scale,
                        Flatten = 1f,
                        Color = weave, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                        Pad = faded
                    };
                }
                // The dark inside, just under the rim, and the base it stands on.
                Output[at + 4 + BasketStaves * 2] = new SegmentInstance
                {
                    Position = foot + up * (height * 0.85f) - forward * radius,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(forward, up),
                    Length = radius * 2f,
                    RadiusStart = radius,
                    RadiusEnd = radius,
                    Flatten = 0.05f,
                    Color = new Unity.Mathematics.float4(0.1f, 0.07f, 0.03f, 0f), Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
                Output[at + 4 + BasketStaves * 2 + 1] = new SegmentInstance
                {
                    Position = foot + up * 0.01f - forward * radius,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(forward, up),
                    Length = radius * 2f,
                    RadiusStart = radius * 0.9f,
                    RadiusEnd = radius * 0.9f,
                    Flatten = 0.08f,
                    Color = wicker, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
            }

            /// <summary>
            /// A town guard's kit, by its folk (GuardKit), so a guard is known at a glance across the town: a helmet of
            /// its folk's shape, a breastplate and shoulder pads (metal or leather), a sash in the town's colour from the
            /// shoulder across to the other hip, and in a closed fist its folk's weapon (a spear, an axe, a club; an elf's bow on its back instead,
            /// a goblin's scrap shield on its other arm, an ogre's great club over its shoulder), or after dark a torch
            /// held up and forward (the handle: its flame is FireSystem's). Running at a beast, the weapon is levelled
            /// at it, or raised.
            /// </summary>
            void Guard(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded, int id)
            {
                for (int i = 0; i < GuardParts; i++)
                    Output[at + i] = default;
                int hand = -1, offHand = -1, head = -1, chest = -1, shoulderR = -1, shoulderL = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                    {
                        hand = part.FirstSegment + part.SegmentCount - 1;
                        shoulderR = part.FirstSegment;
                    }
                    else if (part.Role == LimbRole.Arm && part.Side < 0f && offHand < 0)
                    {
                        offHand = part.FirstSegment + part.SegmentCount - 1;
                        shoulderL = part.FirstSegment;
                    }
                    else if (part.Role == LimbRole.Head && head < 0)
                        head = part.FirstSegment + part.SegmentCount - 1;   // (the head itself, past the neck)
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0)
                    return;
                var k = 0;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var upward = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wood = new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f);
                var iron = new Unity.Mathematics.float4(0.32f, 0.33f, 0.35f, 0f);
                var bone = new Unity.Mathematics.float4(0.78f, 0.74f, 0.62f, 0f);
                var armour = b.GuardArmour;
                // (Plate for humans, dwarves and elves; goblins and ogres in leather.)
                var armourMaterial = b.GuardKit == GuardKit.Goblin || b.GuardKit == GuardKit.Ogre ? SegmentMaterial.Leather : SegmentMaterial.Metal;
                var levelled = state.Mode == CreatureMode.Patrol && state.Stage == 2;

                // How far the weapon is out of its sheath and in hand: drawn, all the way; worn (or a torch in hand
                // instead), not at all; drawing or sheathing, from its sheath to the hand at the middle of the reach.
                var u = state.ActionTime;
                var inHand = state.Held == HeldItem.SpearDrawn ? (state.Action == IdleAction.Draw ? Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 1f)
                           : state.Held == HeldItem.Spear && state.Action == IdleAction.Sheathe ? 1f - Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 0f;

                // The hand closed round what it holds (a fist, shorter and rounder than the open hand), if anything.
                var fist = Output[offset + hand];
                var holding = state.Held == HeldItem.Torch || inHand > 0.5f;
                if (holding)
                {
                    fist.Length *= 0.5f;
                    fist.RadiusStart *= 1.25f;
                    fist.RadiusEnd *= 1.35f;
                    Output[offset + hand] = fist;
                }
                var grip = fist.Position + Unity.Mathematics.math.mul(fist.Rotation, new Unity.Mathematics.float3(0f, 0f, fist.Length * 0.6f));

                if (state.Held == HeldItem.Torch)
                {
                    var up = Unity.Mathematics.math.normalize(upward + forward * 0.45f);
                    var handle = 0.5f * scale;
                    var bottom = grip - up * handle * 0.35f;
                    Output[at + k++] = Piece(bottom, up, forward, handle, 0.02f * scale, 0.028f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    // (Its flame is lit on the main thread, from the top of this handle: FireSystem's, like any other.)
                }

                // The weapon (an elf's is the bow on its back, below): in hand as it's carried drawn, or worn in its
                // sheath, on the back if it's long for its bearer (GuardWeapons.OnBack), else at the hip; between the
                // two as it's drawn or put away.
                if (b.GuardKit != GuardKit.Elf && chest >= 0)
                {
                    var length = GuardWeapons.Length(b.GuardKit) * scale;
                    float3 handDir;
                    float handFrom;   // (how far along the weapon the hand holds it, from the butt)
                    switch (b.GuardKit)
                    {
                        case GuardKit.Dwarf:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.7f) : Unity.Mathematics.math.normalize(upward + forward * 0.15f);
                            handFrom = 0.3f;
                            break;
                        case GuardKit.Goblin:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.8f) : Unity.Mathematics.math.normalize(upward * 0.6f + forward * 0.5f);
                            handFrom = 0.11f;
                            break;
                        case GuardKit.Ogre:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.25f) : Unity.Mathematics.math.normalize(upward * 0.55f - forward * 0.75f);
                            handFrom = 0.07f;
                            break;
                        default:
                            // (Levelled: tilted up a little and held near its middle, so its butt doesn't trail low behind.)
                            handDir = levelled ? Unity.Mathematics.math.normalize(forward + upward * 0.28f) : Unity.Mathematics.math.normalize(upward + forward * 0.12f);
                            handFrom = levelled ? 0.42f : 0.45f;
                            break;
                    }
                    // Worn: across the back, from low on the left to high on the right, or hanging from the belt at the left
                    // hip, head down along the thigh.
                    var torso = Output[offset + chest];
                    var torsoAxis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var torsoR = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                    float3 wornAt, wornDir;
                    if (GuardWeapons.OnBack(b.GuardKit, scale, b.StandHeight))
                    {
                        wornDir = Unity.Mathematics.math.normalize(torsoAxis + right * 0.75f);
                        wornAt = torso.Position + torsoAxis * torso.Length * 0.5f - forward * torsoR * 1.3f - wornDir * length * (0.5f - handFrom);
                    }
                    else
                    {
                        // At the hip: hung from the belt by its grip, the heavy end down along the thigh and a little back.
                        wornDir = Unity.Mathematics.math.normalize(-upward - forward * 0.3f - right * 0.08f);
                        wornAt = torso.Position - torsoAxis * torsoR * 0.5f - right * torsoR * 1.15f;
                    }
                    var dir = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.lerp(wornDir, handDir, inHand), handDir);
                    var held = Unity.Mathematics.math.lerp(wornAt, grip, inHand);
                    var butt = held - dir * length * handFrom;
                    var face = Unity.Mathematics.math.normalizesafe(forward - dir * Unity.Mathematics.math.dot(forward, dir), right);
                    switch (b.GuardKit)
                    {
                        case GuardKit.Dwarf:
                            // An axe: a short haft, a broad blade at its head facing out from it.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.022f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            Output[at + k++] = Piece(butt + dir * length * 0.86f, Unity.Mathematics.math.normalize(face - dir * 0.1f), Unity.Mathematics.math.cross(dir, face), 0.13f * scale, 0.05f * scale, 0.1f * scale, 0.18f, iron, faded, SegmentMaterial.Metal);
                            break;
                        case GuardKit.Goblin:
                            // A crude club, thick at the end.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.025f * scale, 0.07f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            break;
                        case GuardKit.Ogre:
                            // A great club (over the shoulder in hand; raised overhead running at a beast).
                            Output[at + k++] = Piece(butt, dir, face, length, 0.045f * scale, 0.13f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            break;
                        default:
                            // A spear: upright at its side in hand, its butt near the ground; levelled at a beast.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.017f * scale, 0.014f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            Output[at + k++] = Piece(butt + dir * length, dir, face, 0.2f * scale, 0.03f * scale, 0.003f * scale, 0.4f, iron, faded, SegmentMaterial.Metal);
                            break;
                    }
                }

                // The chest: a breastplate over it, the sash down its front, pads on the shoulders.
                if (chest >= 0)
                {
                    var c = Output[offset + chest];
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var plate = c;
                    plate.RadiusStart *= 1.12f;
                    plate.RadiusEnd *= 1.12f;
                    plate.Color = armour;
                    plate.Material = new Unity.Mathematics.float4((float)armourMaterial, 0f, 0f, 0f);
                    plate.Pad = faded;
                    Output[at + k++] = plate;
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var top = c.Position + axis * c.Length;
                    // (The chest's own reach forward and to the side: a flattened chest, an ogre's or a dwarf's, is shallower
                    // than it is wide, so the sash is laid on its surface from these rather than its radius.)
                    var cx = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(1f, 0f, 0f));
                    var cy = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 1f, 0f));
                    var deep = r * Unity.Mathematics.math.sqrt(Unity.Mathematics.math.square(Unity.Mathematics.math.dot(cx, front)) + Unity.Mathematics.math.square(c.Flatten * Unity.Mathematics.math.dot(cy, front))) * 1.12f;
                    var wide = r * Unity.Mathematics.math.sqrt(Unity.Mathematics.math.square(Unity.Mathematics.math.dot(cx, right)) + Unity.Mathematics.math.square(c.Flatten * Unity.Mathematics.math.dot(cy, right))) * 1.12f;
                    var band = Unity.Mathematics.math.min(r * 0.2f, wide * 0.22f);
                    var from = top + right * wide * 0.7f + front * deep * 0.35f;
                    var over = c.Position + axis * c.Length * 0.45f + front * deep * 1.02f;
                    var to = c.Position - axis * r * 0.4f - right * wide * 0.75f + front * deep * 0.6f;
                    Output[at + k++] = Piece(from, Unity.Mathematics.math.normalize(over - from), front, Unity.Mathematics.math.distance(from, over), band, band, 0.25f, b.GuardSash, faded, SegmentMaterial.Cloth);
                    Output[at + k++] = Piece(over, Unity.Mathematics.math.normalize(to - over), front, Unity.Mathematics.math.distance(over, to), band, band, 0.25f, b.GuardSash, faded, SegmentMaterial.Cloth);
                    // An elf's bow across its back: an arc from low on one side to high on the other, and its string.
                    if (b.GuardKit == GuardKit.Elf)
                    {
                        var centre = c.Position + axis * c.Length * 0.5f - front * r * 1.25f;
                        var across = Unity.Mathematics.math.normalize(axis + right * 0.9f);
                        var bowLength = 1.1f * scale;
                        var lower = centre - across * bowLength * 0.5f;
                        var mid = centre - front * 0.08f * scale;
                        var upper = centre + across * bowLength * 0.5f;
                        Output[at + k++] = Piece(lower, Unity.Mathematics.math.normalize(mid - lower), front, Unity.Mathematics.math.distance(lower, mid), 0.012f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(mid, Unity.Mathematics.math.normalize(upper - mid), front, Unity.Mathematics.math.distance(upper, mid), 0.02f * scale, 0.012f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(lower, across, front, bowLength, 0.003f * scale, 0.003f * scale, 1f, bone, faded, SegmentMaterial.Cloth);
                    }
                    for (int side = 0; side < 2; side++)
                    {
                        var arm = side == 0 ? shoulderR : shoulderL;
                        if (arm < 0)
                            continue;
                        var sh = Output[offset + arm];
                        var outward = side == 0 ? right : -right;
                        // (An ogre's are iron on its dark hide, so it stands out from its folk at a distance.)
                        var ogre = b.GuardKit == GuardKit.Ogre;
                        Output[at + k++] = Piece(sh.Position + upward * sh.RadiusStart * 0.4f, outward, upward, 0.02f * scale,
                                                 sh.RadiusStart * 1.9f, sh.RadiusStart * 1.6f, 0.55f, ogre ? iron : armour, faded, ogre ? SegmentMaterial.Metal : armourMaterial);
                    }
                }

                // A goblin's scrap shield on its other forearm.
                if (b.GuardKit == GuardKit.Goblin && offHand >= 0)
                {
                    var fore = Output[offset + offHand - 1];
                    var at2 = fore.Position + Unity.Mathematics.math.mul(fore.Rotation, new Unity.Mathematics.float3(0f, 0f, fore.Length * 0.5f)) - right * 0.06f * scale;
                    Output[at + k++] = Piece(at2, forward, -right, 0.01f * scale, 0.17f * scale, 0.155f * scale, 0.12f, new Unity.Mathematics.float4(0.36f, 0.3f, 0.24f, 0f), faded, SegmentMaterial.Metal);
                }

                // The helmet, its folk's shape (the hair under it hidden: whatever grows from the top of the head, not
                // beards and moustaches below).
                if (head >= 0 && k + 4 <= GuardParts)
                {
                    for (int i = 0; i < b.Parts.Length; i++)
                    {
                        ref var part = ref b.Parts[i];
                        if (part.Socket.y < 0f || part.SegmentCount == 0 || b.Segments[part.FirstSegment].Hair == 0)
                            continue;
                        for (int j = 0; j < part.SegmentCount; j++)
                            Output[offset + part.FirstSegment + j] = default;
                    }
                    var h = Output[offset + head];
                    var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                    var crown = h.Position + ax * h.Length;
                    var face = Unity.Mathematics.math.normalize(forward - ax * Unity.Mathematics.math.dot(forward, ax));
                    var side = Unity.Mathematics.math.normalize(Unity.Mathematics.math.cross(ax, face));
                    // (A dome: wide round the head, flattened along it, sitting on the crown.)
                    var dome = Piece(crown + ax * r * 0.12f, face, ax, 0f, r * 1.18f, r * 1.18f, 0.72f, armour, faded, armourMaterial);
                    switch (b.GuardKit)
                    {
                        case GuardKit.Dwarf:
                            Output[at + k++] = dome;
                            for (int e = -1; e <= 1; e += 2)
                                Output[at + k++] = Piece(crown + side * e * r * 0.85f, Unity.Mathematics.math.normalize(side * e * 0.8f + ax * 0.7f), face,
                                                         r * 1.3f, r * 0.32f, r * 0.04f, 1f, bone, faded, SegmentMaterial.Bone);
                            break;
                        case GuardKit.Elf:
                            Output[at + k++] = dome;
                            // A tall crest, broad from front to back and thin across, swept back like a leaf.
                            Output[at + k++] = Piece(crown + ax * r * 0.4f, Unity.Mathematics.math.normalize(ax - face * 0.35f), side, r * 2.6f, r * 0.95f, r * 0.08f, 0.14f, b.GuardSash, faded, SegmentMaterial.Cloth);
                            break;
                        case GuardKit.Goblin:
                            // A dented pot, tipped askew.
                            var tip = Unity.Mathematics.math.normalize(ax + side * 0.3f - face * 0.1f);
                            Output[at + k++] = Piece(crown - tip * r * 0.55f, tip, face, r * 0.95f, r * 1.12f, r * 1.2f, 0.92f, new Unity.Mathematics.float4(0.3f, 0.29f, 0.27f, 0f), faded, SegmentMaterial.Metal);
                            Output[at + k++] = Piece(crown - tip * r * 0.2f, face, tip, 0f, r * 1.42f, r * 1.42f, 0.12f, new Unity.Mathematics.float4(0.22f, 0.21f, 0.2f, 0f), faded, SegmentMaterial.Metal);
                            break;
                        case GuardKit.Ogre:
                        {
                            // A great beast's skull: a bone cap, a heavy brow, and two short horns.
                            var skull = dome;
                            skull.Color = bone;
                            skull.Material = new Unity.Mathematics.float4((float)SegmentMaterial.Bone, 0f, 0f, 0f);
                            Output[at + k++] = skull;
                            Output[at + k++] = Piece(crown + face * r * 0.75f - side * r * 0.85f, side, ax, r * 1.7f, r * 0.3f, r * 0.3f, 0.7f, bone, faded, SegmentMaterial.Bone);
                            for (int e = -1; e <= 1; e += 2)
                                Output[at + k++] = Piece(crown + side * e * r * 0.7f + face * r * 0.3f, Unity.Mathematics.math.normalize(side * e * 0.5f + ax * 0.6f + face * 0.4f), face,
                                                         r * 0.7f, r * 0.25f, r * 0.05f, 1f, bone, faded, SegmentMaterial.Bone);
                            break;
                        }
                        default:
                            // A kettle hat: a dome with a broad brim all round.
                            Output[at + k++] = dome;
                            Output[at + k++] = Piece(crown - ax * r * 0.3f, face, ax, 0f, r * 1.95f, r * 1.95f, 0.07f, armour, faded, armourMaterial);
                            break;
                    }
                }
            }

            /// <summary>One capsule of a kit: from a point along a direction, its flattening across "flat" (local y), and what it's made of.</summary>
            static SegmentInstance Piece(Unity.Mathematics.float3 from, Unity.Mathematics.float3 along, Unity.Mathematics.float3 flat, float length,
                                         float radiusStart, float radiusEnd, float flatten, Unity.Mathematics.float4 color, float faded,
                                         SegmentMaterial material) =>
                new SegmentInstance
                {
                    Position = from,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, flat),
                    Length = length,
                    RadiusStart = radiusStart,
                    RadiusEnd = radiusEnd,
                    Flatten = flatten,
                    Color = color,
                    Pad = faded,
                    Material = new Unity.Mathematics.float4((float)material, 0f, 0f, 0f)
                };

            /// <summary>
            /// A lantern hanging from the off hand by its bail: a dark iron cap and base, and between them a warm glass
            /// core that glows. It hangs plumb, swinging gently fore and aft with the walk (and still when standing).
            /// </summary>
            void Lantern(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < LanternParts; i++)
                    Output[at + i] = default;
                var hand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side < 0f)
                    {
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (hand < 0)
                    return;
                var segment = Output[offset + hand];
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                // Swinging with the stride: back and forth along the heading, more the faster it goes.
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var swing = Unity.Mathematics.math.sin(state.Phase * 2f * Unity.Mathematics.math.PI) * 0.35f * Unity.Mathematics.math.saturate(state.Speed / 2f);
                var down = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(0f, -1f, 0f) + forward * swing);
                var hang = Unity.Mathematics.quaternion.LookRotationSafe(down, forward);
                var iron = new Unity.Mathematics.float4(0.05f, 0.045f, 0.04f, 0f);
                var glass = new Unity.Mathematics.float4(1f, 0.62f, 0.22f, 1f);
                var bail = 0.07f * scale;
                var cap = grip + down * bail;
                var core = 0.11f * scale;
                // The bail (handle), the cap, the glass, the base.
                Output[at] = new SegmentInstance { Position = grip, Rotation = hang, Length = bail, RadiusStart = 0.006f * scale, RadiusEnd = 0.006f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
                Output[at + 1] = new SegmentInstance { Position = cap, Rotation = hang, Length = 0.025f * scale, RadiusStart = 0.012f * scale, RadiusEnd = 0.045f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
                Output[at + 2] = new SegmentInstance { Position = cap + down * 0.02f * scale, Rotation = hang, Length = core, RadiusStart = 0.04f * scale, RadiusEnd = 0.04f * scale, Flatten = 1f, Color = glass, Pad = faded };
                Output[at + 3] = new SegmentInstance { Position = cap + down * (0.02f * scale + core), Rotation = hang, Length = 0.02f * scale, RadiusStart = 0.048f * scale, RadiusEnd = 0.048f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
            }

            /// <summary>
            /// A hunter's spear: worn across its back about the town (and after dark, a lantern in hand), drawn when it
            /// goes after game (the same half-second reach as a guard's, Draw and Sheathe), then carried pointing ahead
            /// and a little down while creeping, held level over the shoulder at the game while aiming; gone at the
            /// throw (it's been thrown), back in hand after. (Drawn for every hunter: the Hunt task is replicated, so
            /// each machine draws it.)
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
                var held = grip - dir * length * 0.4f;
                // Worn, or on its way between back and hand: across the back, from low on the left to high on the right.
                var u = state.ActionTime;
                var inHand = state.Held == HeldItem.SpearDrawn ? (state.Action == IdleAction.Draw ? Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 1f)
                           : state.Held == HeldItem.Spear && state.Action == IdleAction.Sheathe ? 1f - Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u)
                           : state.Held == HeldItem.Spear || state.Held == HeldItem.Lantern ? 0f : 1f;
                if (inHand < 1f)
                {
                    var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(yaw), 0f, Unity.Mathematics.math.cos(yaw));
                    var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                    var chest = -1;
                    for (int i = 0; i < b.Parts.Length; i++)
                        if (b.Parts[i].Parent < 0 && b.Parts[i].Role != LimbRole.Leg && b.Parts[i].Role != LimbRole.Arm)
                        {
                            chest = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                            break;
                        }
                    if (chest >= 0)
                    {
                        var torso = Output[offset + chest];
                        var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                        var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                        var wornDir = Unity.Mathematics.math.normalize(axis + right * 0.75f);
                        var wornStart = torso.Position + axis * torso.Length * 0.5f - forward * r * 1.3f - wornDir * length * 0.5f;
                        dir = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.lerp(wornDir, dir, inHand), dir);
                        held = Unity.Mathematics.math.lerp(wornStart, held, inHand);
                    }
                }
                Output[at] = new SegmentInstance
                {
                    Position = held,
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
                // (Linear colours: straw; grey-brown mud; dark twigs.)
                var twigs = ground ? new Unity.Mathematics.float4(0.4f, 0.31f, 0.12f, 0f)
                          : site == NestSite.Ledge ? new Unity.Mathematics.float4(0.16f, 0.12f, 0.08f, 0f)
                          : new Unity.Mathematics.float4(0.1f, 0.06f, 0.025f, 0f);
                var x = new Unity.Mathematics.float3(1f, 0f, 0f);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                // The floor: a flat disc just under the bird's feet.
                Output[offset] = new SegmentInstance
                {
                    // (On the ground a round disc, its edge under the rim; in a tree the long shape it always had.)
                    Position = c + new Unity.Mathematics.float3(ground ? -r * 0.05f : -r * 0.45f, ground ? 0.01f : -r * 0.05f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = ground ? r * 0.1f : r * 0.9f,
                    RadiusStart = ground ? r * 0.68f : r * 0.55f,
                    RadiusEnd = ground ? r * 0.68f : r * 0.55f,
                    Flatten = ground ? 0.1f : 0.3f,
                    // (On the ground, the hollow: dark, trodden straw.)
                    Color = ground ? new Unity.Mathematics.float4(0.12f, 0.09f, 0.04f, 0f) : twigs * 0.8f
                };
                // The rim: twigs round it, each a little askew (sticks on a treetop more so).
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                var rimHeight = ground ? r * 0.16f : r * 0.15f;
                var thick = r * (ground ? 0.045f : top ? 0.16f : 0.18f);
                // Contact darkening: a dark, flat disc a little wider than the nest just under it (the ground in its
                // shade, or the shadowed underside of a nest in a tree).
                Output[offset + NestCount - 1] = new SegmentInstance
                {
                    Position = c + new Unity.Mathematics.float3(ground ? -r * 0.05f : -r * 0.5f, ground ? 0.004f : -r * 0.06f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = ground ? r * 0.1f : r,
                    RadiusStart = ground ? r * 0.95f : r * 0.7f,
                    RadiusEnd = ground ? r * 0.95f : r * 0.7f,
                    Flatten = 0.05f,
                    Color = ground ? new Unity.Mathematics.float4(0.05f, 0.04f, 0.02f, 0f) : new Unity.Mathematics.float4(0.02f, 0.018f, 0.012f, 0f)
                };
                for (int k = 1; k <= NestRim; k++)
                {
                    var a = k / (float)NestRim * Unity.Mathematics.math.PI * 2f + rng.NextFloat(-0.25f, 0.25f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    // (On the ground, short tufts of straw heaped up raggedly: shorter, more askew, in and out.)
                    var along = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(-dir.z, rng.NextFloat(-0.15f, 0.15f) * (top ? 2f : ground ? 2.5f : 1f), dir.x));
                    var length = r * (top ? rng.NextFloat(1.1f, 1.6f) : ground ? rng.NextFloat(0.55f, 0.85f) : 0.95f);
                    var layer = ground && (k & 1) == 0 ? 1f : 0f;
                    var centre = c + dir * r * (ground ? rng.NextFloat(0.82f, 1.0f) - 0.1f * layer : 0.9f) +
                                 up * rimHeight * (ground ? rng.NextFloat(0.5f, 0.9f) + 0.7f * layer : 1f);
                    Output[offset + k] = new SegmentInstance
                    {
                        Position = centre - along * length * 0.5f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, up),
                        Length = length,
                        RadiusStart = thick,
                        RadiusEnd = thick * 0.8f,
                        Flatten = 1f,
                        Color = twigs * rng.NextFloat(0.75f, 1.15f)
                    };
                }
                // A scrape on the ground has a few straws sticking out of the rim at angles, and eggs in the hollow when
                // the hen is off it.
                for (int k = NestRim + 1; k < NestCount - 1; k++)
                    Output[offset + k] = default;
                if (!ground)
                    return;
                for (int k = 0; k < 2; k++)
                {
                    var a = rng.NextFloat(0f, Unity.Mathematics.math.PI * 2f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var outward = Unity.Mathematics.math.normalize(dir + up * rng.NextFloat(0.3f, 0.8f) + new Unity.Mathematics.float3(-dir.z, 0f, dir.x) * rng.NextFloat(-0.6f, 0.6f));
                    Output[offset + NestRim + 1 + k] = new SegmentInstance
                    {
                        Position = c + dir * r * 0.75f + up * rimHeight,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(outward, up),
                        Length = r * rng.NextFloat(0.6f, 0.9f),
                        RadiusStart = r * 0.035f,
                        RadiusEnd = r * 0.02f,
                        Flatten = 1f,
                        Color = twigs * 1.1f
                    };
                }
                if (Unity.Mathematics.math.distance(state.Position, c) < r * 1.5f)
                    return;
                for (int k = 0; k < 2; k++)
                {
                    var egg = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(rng.NextFloat(-1f, 1f), 0.15f, rng.NextFloat(-1f, 1f)));
                    var at = c + new Unity.Mathematics.float3(k == 0 ? -r * 0.28f : r * 0.25f, r * 0.12f, k == 0 ? r * 0.14f : -r * 0.2f);
                    Output[offset + NestRim + 3 + k] = new SegmentInstance
                    {
                        Position = at - egg * r * 0.05f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(egg, up),
                        Length = r * 0.1f,
                        RadiusStart = r * 0.13f,
                        RadiusEnd = r * 0.1f,
                        Flatten = 1f,
                        Color = new Unity.Mathematics.float4(0.72f, 0.62f, 0.46f, 0f)
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
            /// between the spokes), and anchor threads: down to the ground and across to the tree it hangs by, or out to
            /// both things it's strung between (see CreatureMode.Web). Only for a spider sitting in its web (room is
            /// made for it then only).
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
                if (state.Stage == 4)
                {
                    // Strung between two things: from above and below the middle of each side, out to each one.
                    var span = Unity.Mathematics.math.max(state.AnchorSize, radius);
                    for (int k = 0; k < 4; k++)
                    {
                        var side = k < 2 ? 1f : -1f;
                        var tilt = (k % 2 == 0 ? 0.3f : -0.3f);
                        var start = Point(side > 0f ? tilt : Unity.Mathematics.math.PI - tilt, radius);
                        var end = centre + right * side * span + up * Unity.Mathematics.math.sin(tilt) * radius * 1.6f;
                        Output[offset + n++] = Thread(start, end, thick, colour);
                    }
                    return;
                }
                // Down to the ground from the lower edge, either side.
                for (int k = 0; k < 2; k++)
                {
                    var start = Point(Unity.Mathematics.math.PI * (k == 0 ? 1.3f : 1.7f), radius);
                    var foot = start + right * (k == 0 ? -0.3f : 0.3f) * radius;
                    foot.y = Terrain.FloorUnder(start, out _);
                    Output[offset + n++] = Thread(start, foot, thick, colour);
                }
                // Across to the trunk, from the top and the near side (stage 2: tree to the spider's right, 3: left).
                var trunkSide = state.Stage == 2 ? 1f : -1f;
                for (int k = 0; k < 2; k++)
                {
                    var start = k == 0 ? Point(Unity.Mathematics.math.PI * 0.5f, radius) : Point(trunkSide >= 0f ? 0f : Unity.Mathematics.math.PI, radius);
                    var end = centre + right * trunkSide * radius * 1.05f + up * (k == 0 ? radius * 1.4f : 0f);
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
