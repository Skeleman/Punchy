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
    // A-81 row 4: the extras drawn beside the creatures (bat colonies, critters, cave fish, doors, the sky clock),
    // moved out of CreatureRenderSystem.cs as they were.
    public partial class CreatureRenderSystem
    {
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
                    if (!foragerGround.TryGetValue(index * 64 + k, out var ground))
                    {
                        ground = terrain.SampleHeight(spot2);
                        foragerGround[index * 64 + k] = ground;
                        ForagerGroundSamples++;
                    }
                    var spot = new float3(spot2.x, ground + 0.4f, spot2.y);
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

        /// <summary>Capsules for one of AmbientLife's small creatures (unused ones left empty).</summary>
        const int CritterParts = 6;

        /// <summary>
        /// Draws one of AmbientLife's: a songbird or woodpecker (body, head, beak, tail, two wings, out and beating when
        /// it's in the air), an owl (round body, big head turned to whatever it watches, ear tufts), a cricket or
        /// grasshopper (a speck, legs flicking when it hops), a frog (a squat body, two eyes), a cicada (body, folded wings).
        /// </summary>
        static int Critter(in Ambient a, float now, NativeArray<SegmentInstance> output, int at)
        {
            for (int i = 0; i < CritterParts; i++)
                output[at + i] = default;
            if (a.Hidden)
                return at + CritterParts;
            var f = new float3(math.sin(a.Yaw), 0f, math.cos(a.Yaw));
            var side = new float3(f.z, 0f, -f.x);
            var up = new float3(0f, 1f, 0f);
            var face = quaternion.LookRotationSafe(f, up);
            var p = a.Position;
            switch (a.Kind)
            {
                case AmbientKind.Songbird:
                case AmbientKind.Woodpecker:
                {
                    var wood = a.Kind == AmbientKind.Woodpecker;
                    var s = wood ? 1.12f : 1f;
                    // (A woodpecker clings upright to the bark.)
                    var bodyDir = wood ? math.normalize(up + f * 0.3f) : f;
                    var body = quaternion.LookRotationSafe(bodyDir, wood ? -f : up);
                    output[at] = Part(p + up * 0.04f * s - bodyDir * 0.04f * s, body, 0.07f * s, 0.028f * s, 0.022f * s, 1f, a.Colour);
                    var head = p + up * 0.04f * s + bodyDir * 0.045f * s + (wood ? -f * 0.0f : up * 0.02f * s);
                    output[at + 1] = Part(head - bodyDir * 0.01f, body, 0.02f * s, 0.02f * s, 0.018f * s, 1f,
                                          wood ? new float4(0.6f, 0.08f, 0.06f, 0f) : a.Colour * 1.1f);
                    output[at + 2] = Part(head + (wood ? -f : f) * 0.018f * s, quaternion.LookRotationSafe(wood ? -f : f, up), 0.018f * s,
                                          0.006f * s, 0.002f, 1f, new float4(0.35f, 0.3f, 0.15f, 0f));
                    output[at + 3] = Part(p + up * 0.04f * s - bodyDir * 0.04f * s, quaternion.LookRotationSafe(-bodyDir + up * 0.2f, up),
                                          0.05f * s, 0.014f * s, 0.006f * s, 0.3f, a.Colour * 0.8f);
                    var beat = a.Air > 0.5f ? math.sin(now * 40f) * 0.9f : 0f;
                    for (int w = -1; w <= 1; w += 2)
                    {
                        var wing = math.normalize(side * w * (a.Air > 0.5f ? 1f : 0.2f) - bodyDir * (a.Air > 0.5f ? 0.2f : 1f) + up * beat * 0.6f);
                        output[at + 4 + (w + 1) / 2] = Part(p + up * 0.05f * s + side * w * 0.015f * s, quaternion.LookRotationSafe(wing, up),
                                                            0.055f * s, 0.018f * s, 0.006f * s, 0.25f, a.Colour * 0.75f);
                    }
                    break;
                }
                case AmbientKind.Owl:
                {
                    output[at] = Part(p + up * 0.02f, quaternion.LookRotationSafe(up, -f), 0.16f, 0.09f, 0.075f, 1f, a.Colour);
                    var look = new float3(math.sin(a.Yaw + a.Head), 0f, math.cos(a.Yaw + a.Head));
                    var headAt = p + up * 0.27f;
                    var headRot = quaternion.LookRotationSafe(look, up);
                    output[at + 1] = Part(headAt - look * 0.05f, headRot, 0.06f, 0.07f, 0.065f, 1f, a.Colour * 1.1f);
                    // A pale face disc, and the two ear tufts.
                    output[at + 2] = Part(headAt + look * 0.035f, headRot, 0.01f, 0.05f, 0.05f, 0.3f, new float4(0.75f, 0.68f, 0.55f, 0f));
                    var lookSide = new float3(look.z, 0f, -look.x);
                    for (int e = -1; e <= 1; e += 2)
                        output[at + 3 + (e + 1) / 2] = Part(headAt + lookSide * e * 0.035f + up * 0.05f, quaternion.LookRotationSafe(up + lookSide * e * 0.3f, look),
                                                            0.045f, 0.014f, 0.004f, 1f, a.Colour * 0.8f);
                    output[at + 5] = Part(p - f * 0.03f, quaternion.LookRotationSafe(-up * 0.6f - f, up), 0.08f, 0.03f, 0.01f, 0.4f, a.Colour * 0.85f);
                    break;
                }
                case AmbientKind.Frog:
                {
                    output[at] = Part(p - f * 0.04f + up * 0.025f, face, 0.08f, 0.035f, 0.04f, 0.65f, a.Colour);
                    for (int e = -1; e <= 1; e += 2)
                        output[at + 1 + (e + 1) / 2] = Part(p + f * 0.03f + side * e * 0.018f + up * 0.05f, face, 0.005f, 0.011f, 0.011f, 1f,
                                                            new float4(0.6f, 0.55f, 0.15f, 0f));
                    break;
                }
                case AmbientKind.Cicada:
                {
                    var bodyDir = math.normalize(up + f * 0.2f);
                    output[at] = Part(p - bodyDir * 0.02f, quaternion.LookRotationSafe(bodyDir, -f), 0.045f, 0.013f, 0.011f, 1f, a.Colour);
                    output[at + 1] = Part(p - bodyDir * 0.03f - f * 0.008f, quaternion.LookRotationSafe(-bodyDir, -f), 0.05f, 0.012f, 0.004f, 0.2f,
                                          new float4(0.55f, 0.6f, 0.55f, 0f));
                    break;
                }
                default:
                {
                    // A cricket (dark) or grasshopper (green): a body and a pair of long hind legs, kicked out mid-hop.
                    var len = a.Kind == AmbientKind.Grasshopper ? 0.045f : 0.03f;
                    output[at] = Part(p - f * len * 0.5f, face, len, 0.008f, 0.006f, 1f, a.Colour);
                    var kick = a.Air > 0.5f ? 0.8f : 0f;
                    for (int l = -1; l <= 1; l += 2)
                        output[at + 1 + (l + 1) / 2] = Part(p + side * l * 0.006f, quaternion.LookRotationSafe(-f + up * (0.6f - kick) + side * l * 0.2f, up),
                                                            len * 0.9f, 0.003f, 0.002f, 1f, a.Colour * 0.8f);
                    break;
                }
            }
            return at + CritterParts;
        }

        // ------------------------------------------------------------------------------- cave fish

        /// <summary>How near the camera the caves' pools have their fish drawn, past the rim (m: W-135 row 3, a fish's size
        /// by DetailPolicy.CaveFishPixels, x Detail; 35 m at the reference view and Detail 1), and the parts of each fish.</summary>
        static float CaveFishRange => Punchy.Settings.DetailPolicy.ExtraReach(Punchy.Settings.DetailPolicy.CaveFishSize, Punchy.Settings.DetailPolicy.CaveFishPixels);
        const int FishParts = 3;
        /// <summary>The pools whose fish were drawn at the last gather (DetailPolicy.Within holds them).</summary>
        readonly System.Collections.Generic.HashSet<int> fishPoolsOn = new System.Collections.Generic.HashSet<int>();

        /// <summary>The water pools in range this frame: centre (surface), radius, depth; and how many fish each holds.</summary>
        readonly System.Collections.Generic.List<(float3 centre, float radius, float depth, int index, int fish)> fishPools = new System.Collections.Generic.List<(float3, float, float, int, int)>();

        /// <summary>
        /// The caves' water pools near the camera, each with a few pale fish (one to four, by its size): how many parts
        /// they need. A pool too small or shallow, or of lava, has none.
        /// </summary>
        int GatherCaveFish(in TerrainSource terrain, float3 camera)
        {
            fishPools.Clear();
            if (!terrain.Caves.IsCreated)
                return 0;
            ref var set = ref terrain.Caves.Value;
            var count = 0;
            var range = CaveFishRange;
            for (int i = 0; i < set.Pools.Length; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f || pool.Radius < 0.6f || pool.Depth < 0.3f)
                    continue;
                var on = Punchy.Settings.DetailPolicy.Within(math.distance(pool.Centre, camera) - pool.Radius, range, fishPoolsOn.Contains(i));
                if (!on)
                {
                    fishPoolsOn.Remove(i);
                    continue;
                }
                fishPoolsOn.Add(i);
                var fish = math.clamp((int)math.round(pool.Radius * 1.2f), 1, 4);
                fishPools.Add((pool.Centre, pool.Radius, pool.Depth, i, fish));
                count += fish;
            }
            return count;
        }

        /// <summary>
        /// The pools' fish, each going round its pool on a loop of its own (width, depth, pace, direction, from its pool and
        /// number), weaving a little, on the shared clock so every machine sees the same: a pale body, a fin, a tail
        /// beating as it goes.
        /// </summary>
        int WriteCaveFish(double clock, NativeArray<SegmentInstance> output, int at)
        {
            var t = (float)(clock % 100000.0);
            var up = new float3(0f, 1f, 0f);
            foreach (var pool in fishPools)
                for (int k = 0; k < pool.fish; k++)
                {
                    var rng = new Unity.Mathematics.Random(math.hash(new int2(pool.index, k)) | 1u);
                    var loop = pool.radius * rng.NextFloat(0.3f, 0.75f);
                    var pace = rng.NextFloat(0.15f, 0.35f) / math.max(loop, 0.3f) * (rng.NextBool() ? 1f : -1f);
                    var angle = rng.NextFloat(6.28f) + t * pace;
                    var depth = pool.depth * rng.NextFloat(0.3f, 0.6f);
                    var weave = math.sin(t * 0.7f + k) * 0.12f * loop;
                    var p = pool.centre + new float3(math.cos(angle), 0f, math.sin(angle)) * (loop + weave) - up * depth;
                    var f = math.normalize(new float3(-math.sin(angle), 0f, math.cos(angle)) * math.sign(pace));
                    var size = rng.NextFloat(0.8f, 1.2f);
                    var colour = new float4(0.85f, 0.78f, 0.74f, 0.15f);   // (pale, a little rosy: no light to colour it)
                    var beat = math.sin(t * 9f + k * 2f) * 0.5f;
                    var body = quaternion.LookRotationSafe(f, up);
                    var tail = quaternion.LookRotationSafe(-f + new float3(f.z, 0f, -f.x) * beat, up);
                    output[at] = Part(p - f * 0.08f * size, body, 0.16f * size, 0.022f * size, 0.03f * size, 0.6f, colour);
                    output[at + 1] = Part(p - f * 0.08f * size, tail, 0.06f * size, 0.02f * size, 0.002f, 0.15f, colour * 0.9f);
                    output[at + 2] = Part(p, quaternion.LookRotationSafe(-f + up * 0.8f, up), 0.04f * size, 0.012f * size, 0.002f, 0.15f, colour * 0.9f);
                    at += FishParts;
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

        Punchy.Weather.DayNightCycle skyCycle;

        // ------------------------------------------------------------------------------- doors

        /// <summary>How open each door a resident is passing through is this frame, and last frame's (to shut the rest).</summary>
        readonly System.Collections.Generic.Dictionary<float3, float> doorsNow = new System.Collections.Generic.Dictionary<float3, float>();
        readonly System.Collections.Generic.Dictionary<float3, float> doorsBefore = new System.Collections.Generic.Dictionary<float3, float>();

        /// <summary>
        /// Folk going in and coming out swing their door (FloraDoors, on every machine from the replicated state): it
        /// opens as they come up to it, stands open while they step through (Home stages 1 and 3; ActionTime runs the step),
        /// and swings shut behind them. Doors nobody is using go back to their own rhythm.
        /// </summary>
        void SwingDoors(NativeArray<CreatureState> states)
        {
            doorsNow.Clear();
            for (int i = 0; i < states.Length; i++)
            {
                var c = states[i];
                if (c.Mode != CreatureMode.Home)
                    continue;
                var door = c.Anchor;
                float amount;
                if (c.Stage == 0)
                {
                    // Coming up to it: opening over the last metre and a half.
                    var d = math.distance(c.Position.xz, door.xz);
                    if (d > 1.5f)
                        continue;
                    amount = math.saturate((1.5f - d) / 1.1f);
                }
                else if (c.Stage == 1)
                    amount = 1f - math.smoothstep(0.75f, 1f, c.ActionTime);   // through, and shut behind it
                else if (c.Stage == 2)
                    continue;
                else
                    amount = math.smoothstep(0f, 0.15f, c.ActionTime) + (1f - math.smoothstep(0f, 0.15f, c.ActionTime)) *
                             (1f - math.saturate(math.distance(c.Position.xz, door.xz) / 1.5f));
                doorsNow[door] = math.max(doorsNow.TryGetValue(door, out var was) ? was : 0f, amount);
            }
            foreach (var kv in doorsNow)
                Punchy.Flora.FloraDoors.SetOpen(kv.Key, kv.Value);
            foreach (var kv in doorsBefore)
                if (!doorsNow.ContainsKey(kv.Key))
                    Punchy.Flora.FloraDoors.SetOpen(kv.Key, 0f);
            doorsBefore.Clear();
            foreach (var kv in doorsNow)
                doorsBefore[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Seconds on the shared clock, counting whole days (the day and time of day every machine shares; a paused
        /// clock, for screenshots, runs on local time), and the hour.
        /// </summary>
        double SkyClock(out float hour)
        {
            if (skyCycle == null)
                skyCycle = Object.FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            hour = skyCycle != null ? skyCycle.TimeOfDay : 12f;
            if (skyCycle == null || skyCycle.Paused)
                return SystemAPI.Time.ElapsedTime;
            return (skyCycle.Day * 24.0 + skyCycle.TimeOfDay) * skyCycle.DayLengthMinutes * 2.5;
        }

        /// <summary>A spider sitting in its web (only then is the web drawn, and room made for it).</summary>
        static bool InWeb(in CreatureState s) => s.Mode == CreatureMode.Web && s.Stage >= 2 && s.Stage <= 4;
    }
}
