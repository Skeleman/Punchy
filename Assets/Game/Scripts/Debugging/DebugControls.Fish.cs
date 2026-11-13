using System.Collections.Generic;
using System.Text;
using Punchy.Fauna;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, fishing (A-100): the catch-table probe and the catch line-up. The class summary is in DebugControls.cs.
    public partial class DebugControls
    {
        /// <summary>"-fishprobe" (per line): rolls 100 catches per water kind and logs the counts, then finds each kind of
        /// water round the player and rolls there through the full classify path.</summary>
        bool fishProbe, fishProbeDone;
        /// <summary>"-fishlineup [small|big]" (per line): a row of catches held nose to tail in the air, side-on to a pinned camera.</summary>
        bool fishLineup;
        /// <summary>"-fishlineup small" / "big": one half of the line-up (null: all of it).</summary>
        string fishLineupGroup;
        /// <summary>The line-up pinned the shot camera (and frees it when it clears).</summary>
        bool lineupCam;
        /// <summary>The group the row standing now was built for.</summary>
        string lineupBuilt;
        readonly List<(Entity fish, float size, float3 offset)> lineup = new List<(Entity, float, float3)>();
        /// <summary>The gap between two fish in the row, nose to tail (m).</summary>
        const float Gap = 0.12f;
        float3 lineupBase;
        float lineupYaw;
        /// <summary>"-fishlure approach|nibble|bite|scatter" (per line; A-100 row 2): a bobber dropped in front of the
        /// nearest fish in a lake or river pool, held at that moment of a cast (null: off).</summary>
        string fishLure;
        /// <summary>The fish the bobber went in for, when (Time.time), where, and whether the scatter has been called.</summary>
        Entity lureFish;
        float lureCastAt;
        float3 lureAt;
        bool lureCam, lureScattered;
        /// <summary>The debug line's key in FishLure (players' lines use their own).</summary>
        const int DebugLureKey = -77;

        /// <summary>The line-up, small to big, by name in <see cref="FishCatch.Table"/>: the small half, then the big.</summary>
        static readonly string[] SmallNames = { "Minnow", "Blind cavefish", "Perch", "Herring", "Trout", "Mackerel", "Koi" };
        static readonly string[] BigNames = { "Carp", "Glow eel", "Salmon", "Pike", "Tuna", "Lake sturgeon" };

        void FishProbe()
        {
            if (!fishProbe)
            {
                fishProbeDone = false;
                return;
            }
            if (fishProbeDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            fishProbeDone = true;
            // 1. The table alone: 100 casts per water kind over a whole day (hour = cast * 24 / 100), temperate biome
            // (swamps: marsh).
            foreach (var water in FishCatch.Waters)
            {
                var biome = water == FishWater.Swamp ? FishBiome.Marsh : FishBiome.Temperate;
                var counts = new SortedDictionary<string, int>();
                int none = 0, junk = 0;
                float sizeSum = 0f;
                for (uint i = 0; i < 100; i++)
                {
                    if (!FishCatch.Roll(water, biome, i * 0.24f, 0xF15u * 977u + i * 7919u + (uint)water * 104729u, out var r))
                    {
                        none++;
                        continue;
                    }
                    counts.TryGetValue(r.Name, out var n);
                    counts[r.Name] = n + 1;
                    if (r.Junk)
                        junk++;
                    else
                        sizeSum += r.Size;
                }
                var line = new StringBuilder($"FishProbe: {water} ({biome}) 100 casts: ");
                foreach (var kv in counts)
                    line.Append($"{kv.Key} {kv.Value}, ");
                line.Append($"nothing {none}, junk {junk}, mean fish {(100 - none - junk > 0 ? sizeSum / (100 - none - junk) : 0f):F0} cm");
                Debug.Log(line.ToString());
            }
            // 2. The world: the nearest point of each water kind within 600 m (surface water; caves need a float3 sample
            // under the ground, so they're rolled from the table above), and one roll there.
            var found = new Dictionary<FishWater, float2>();
            var at0 = player.Position.xz;
            for (var d = 0f; d < 600f && found.Count < 8; d += 8f)
            {
                var steps = math.max(1, (int)(d * 2f * math.PI / 8f));
                for (int k = 0; k < steps; k++)
                {
                    var a = k * 2f * math.PI / steps;
                    var xz = at0 + new float2(math.sin(a), math.cos(a)) * d;
                    var g = source.SampleGround(xz);
                    if (!g.HasWater || g.WaterSurface <= g.Height)
                        continue;
                    var kind = FishCatch.Classify(source, g, new float3(xz.x, g.WaterSurface, xz.y));
                    if (kind != FishWater.None && !found.ContainsKey(kind))
                        found[kind] = xz;
                }
            }
            var hour = FishCatch.Hour();
            foreach (var kv in found)
            {
                var g = source.SampleGround(kv.Value);
                var at = new float3(kv.Value.x, g.WaterSurface, kv.Value.y);
                var got = FishCatch.Roll(source, g, at, 12345u, hour, out var r);
                Debug.Log($"FishProbe: world {kv.Key} at ({kv.Value.x:F0}, {kv.Value.y:F0}) biome {FishCatch.BiomeAt(source, g)} depth {g.WaterSurface - g.Height:F1} m " +
                          $"hour {hour:F1}: {(got ? $"{r.Name} {r.Size} cm str {r.Strength:F2} wait {r.Wait:F1} s" : "nothing")}");
            }
            Debug.Log($"FishProbe: done ({found.Count} water kinds found round the player)");
        }

        /// <summary>
        /// "-fishlineup [small|big]": the catches in <see cref="SmallNames"/> or <see cref="BigNames"/> (both with no
        /// word), each at the upper part of its size range in the body it wears, nose to tail in a row 1.2 m over the
        /// ground ahead of the player, heads to the camera's right. The camera is pinned side-on between the player and
        /// the row (through the same hold as "-shotcam"), so the player stays out of the frame. Posed (no brain).
        /// Cleared, and the camera freed, when a line without the flag starts.
        /// </summary>
        void FishLineup()
        {
            var em = queryWorld != null && queryWorld.IsCreated ? queryWorld.EntityManager : default;
            // A line without the flag, or one asking for the other half, clears the row (and frees the camera).
            if (!fishLineup || (lineup.Count > 0 && lineupBuilt != fishLineupGroup))
            {
                if (lineup.Count > 0 && queryWorld != null && queryWorld.IsCreated)
                    foreach (var f in lineup)
                        if (em.Exists(f.fish))
                            em.DestroyEntity(f.fish);
                lineup.Clear();
                if (lineupCam)
                    shotCamOn = lineupCam = false;
                if (!fishLineup)
                    return;
            }
            if (Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.CanSpawn || Camera.main == null)
                return;
            if (lineup.Count == 0)
            {
                var names = fishLineupGroup == "small" ? SmallNames : fishLineupGroup == "big" ? BigNames :
                    System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(SmallNames, BigNames));
                var sizes = new List<float>();
                var width = 0f;
                foreach (var name in names)
                {
                    var entry = System.Array.Find(FishCatch.Table, e => e.Name == name);
                    sizes.Add(math.lerp(entry.MinSize, entry.MaxSize, 0.75f) / 100f);
                    width += sizes[sizes.Count - 1] + Gap;
                }
                width -= Gap;
                // The row runs along the camera's right as it stands; the camera backs off far enough to fit it (with a
                // margin) and stands 3 m ahead of the player, looking away from it.
                var fwd = Camera.main.transform.forward;
                var ahead = math.normalizesafe(new float2(fwd.x, fwd.z), new float2(0f, 1f));
                var right2 = new float2(ahead.y, -ahead.x);
                lineupYaw = math.atan2(right2.x, right2.y);
                var halfTan = math.tan(math.radians(Camera.main.fieldOfView * 0.5f)) * Camera.main.aspect;
                var dist = math.max(1.2f, width * 0.5f * 1.6f / math.max(halfTan, 0.1f));
                var mid = player.Position.xz + ahead * (dist + 3f);
                lineupBase = new float3(mid.x, source.SampleHeight(mid) + 1.2f, mid.y);
                var camXZ = mid - ahead * dist;
                var camY = math.max(lineupBase.y + dist * 0.2f, source.SampleHeight(camXZ) + 1.2f);
                shotCamFrom = new Vector3(camXZ.x, camY, camXZ.y);
                shotCamTo = (Vector3)lineupBase;
                shotCamOn = lineupCam = true;
                var along = -width * 0.5f;
                for (int i = 0; i < names.Length; i++)
                {
                    var name = names[i];
                    var size = sizes[i];
                    var entry = System.Array.Find(FishCatch.Table, e => e.Name == name);
                    var index = director.SpeciesIndex(entry.Look ?? "");
                    var at = along + size * 0.5f;
                    along += size + Gap;
                    if (index < 0)
                    {
                        Debug.LogWarning($"FishLineup: no look '{entry.Look}' for {name}");
                        continue;
                    }
                    var offset = new float3(right2.x * at, 0f, right2.y * at);
                    var fish = director.Spawn(index, lineupBase + offset, lineupYaw);
                    em.AddComponent<PosedForTest>(fish);
                    var body = em.GetComponentData<CreatureBody>(fish);
                    ref var b = ref body.Blob.Value;
                    if (entry.Coat >= 0)
                    {
                        body.Coat = math.min(entry.Coat, b.Coats.Length - 1);
                        em.SetComponentData(fish, body);
                    }
                    lineup.Add((fish, size, offset));
                    Debug.Log($"FishLineup: {name} as {entry.Look} coat {body.Coat}, {size * 100f:F0} cm (body {b.BodyLength:F2} m x {size / b.BodyLength:F2})");
                }
                lineupBuilt = fishLineupGroup;
                Debug.Log($"FishLineup: {fishLineupGroup ?? "all"} row {width:F1} m wide, camera {dist:F1} m off");
            }
            foreach (var (fish, size, offset) in lineup)
            {
                if (!em.Exists(fish))
                    continue;
                var c = em.GetComponentData<CreatureState>(fish);
                ref var b = ref em.GetComponentData<CreatureBody>(fish).Blob.Value;
                c.Scale = size / math.max(b.BodyLength, 0.01f);
                c.Position = lineupBase + offset;
                c.Yaw = lineupYaw;
                c.Mode = CreatureMode.Idle;
                c.Speed = 0f;
                c.Fly = 0f;
                c.Clock += Time.deltaTime;
                em.SetComponentData(fish, c);
            }
        }

        /// <summary>
        /// "-fishlure approach|nibble|bite|scatter": once the world has settled, a bobber (FishLure) dropped 3 m in front
        /// of the nearest wild fish within 80 m in a lake, river pool or swamp (where the water there is 0.7 m deep or
        /// more), and the shot camera pinned 4 m to its side and 2.5 m up, looking down at it. approach: no bite comes;
        /// nibble: from 6 s on, held at the peak of the second nibble; bite: bitten at 6 s; scatter: scattered 0.5 s
        /// before the shot is taken. Logs where the fish is each second. Cleared (and the camera freed) by a line
        /// without the flag.
        /// </summary>
        void FishLurePose()
        {
            if (fishLure == null)
            {
                if (lureFish != Entity.Null || lureCam)
                {
                    FishLure.Clear(DebugLureKey);
                    lureFish = Entity.Null;
                    if (lureCam)
                        shotCamOn = lureCam = false;
                }
                return;
            }
            if (Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            if (lureFish == Entity.Null || !em.Exists(lureFish))
            {
                using var query = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                using var fishes = query.ToEntityArray(Allocator.Temp);
                var bestD = 80f;
                var best = Entity.Null;
                var bestAt = float3.zero;
                foreach (var e in fishes)
                {
                    if (em.HasComponent<PosedForTest>(e))
                        continue;
                    var c = em.GetComponentData<CreatureState>(e);
                    ref var b = ref em.GetComponentData<CreatureBody>(e).Blob.Value;
                    if (c.Gone || b.Habitat != Habitat.Swims || b.LavaDweller || b.Breaches)
                        continue;
                    var d = math.distance(c.Position.xz, player.Position.xz);
                    if (d >= bestD)
                        continue;
                    var g = source.SampleGround(c.Position.xz);
                    if (!g.HasWater || g.WaterSurface <= g.Height)
                        continue;
                    var kind = FishCatch.Classify(source, g, new float3(c.Position.x, g.WaterSurface, c.Position.z));
                    if (kind != FishWater.Lake && kind != FishWater.RiverPool && kind != FishWater.Swamp)
                        continue;
                    // 3 m ahead of it (or to one side), where the water is deep enough and is the same water.
                    for (int k = 0; k < 8; k++)
                    {
                        var a = c.Yaw + k * math.PI * 0.25f;
                        var xz = c.Position.xz + new float2(math.sin(a), math.cos(a)) * 3f;
                        var at = source.SampleGround(xz);
                        if (at.HasWater && at.WaterSurface - at.Height >= 0.7f && math.abs(at.WaterSurface - g.WaterSurface) < 0.3f)
                        {
                            bestD = d;
                            best = e;
                            bestAt = new float3(xz.x, at.WaterSurface, xz.y);
                            break;
                        }
                    }
                }
                if (best == Entity.Null)
                {
                    if (Time.frameCount % 120 == 0)
                        Debug.Log($"FishLure: no wild fish in a lake or pool within 80 m of ({player.Position.x:F0}, {player.Position.z:F0})");
                    return;
                }
                lureFish = best;
                lureAt = bestAt;
                lureCastAt = Time.time;
                lureScattered = false;
                FishLure.Cast(DebugLureKey, lureAt, 1e4f);
                var fishAt = em.GetComponentData<CreatureState>(best).Position;
                var side = math.normalizesafe(new float2(lureAt.z - fishAt.z, fishAt.x - lureAt.x), new float2(1f, 0f));
                var camXZ = lureAt.xz + side * 2.5f;
                var camY = math.max(lureAt.y + 3.5f, source.SampleHeight(camXZ) + 1f);
                shotCamFrom = new Vector3(camXZ.x, camY, camXZ.y);
                shotCamTo = new Vector3(lureAt.x, lureAt.y - 0.2f, lureAt.z);
                shotCamOn = lureCam = true;
                Debug.Log($"FishLure: {fishLure}: bobber at ({lureAt.x:F1}, {lureAt.y:F2}, {lureAt.z:F1}) for the fish at " +
                          $"({fishAt.x:F1}, {fishAt.y:F2}, {fishAt.z:F1}), {math.distance(fishAt, lureAt):F1} m off");
            }
            var since = Time.time - lureCastAt;
            ref var list = ref FishLure.Data.Data;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Key != DebugLureKey)
                    continue;
                var l = list[i];
                if (fishLure == "nibble" && since >= 6f && l.Stage == FishLure.Stage.Waiting)
                    l.Age = l.BiteIn - FishLure.SecondNibble + FishLure.NibbleTime * 0.5f;
                if (fishLure == "bite" && since >= 6f && l.Stage == FishLure.Stage.Waiting)
                    l.Stage = FishLure.Stage.Bite;
                list[i] = l;
            }
            if (fishLure == "scatter" && !lureScattered && shotStarted >= 0f && since >= 6f &&
                Time.realtimeSinceStartup >= shotStarted + shotWait - 0.5f)
            {
                lureScattered = true;
                FishLure.Scatter(DebugLureKey);
                Debug.Log("FishLure: scattered");
            }
            if (Mathf.FloorToInt(since) != Mathf.FloorToInt(since - Time.deltaTime) && em.Exists(lureFish))
            {
                var c = em.GetComponentData<CreatureState>(lureFish);
                Debug.Log($"FishLure: {since:F0} s: fish {math.distance(c.Position.xz, lureAt.xz):F2} m off, {lureAt.y - c.Position.y:F2} m down, " +
                          $"mode {c.Mode}, speed {c.Speed:F2}; " + FishNearLure());
            }
        }

        /// <summary>The swimming fish within FishLure.Reach of the debug bobber, nearest first (distance, mode), for the log.</summary>
        string FishNearLure()
        {
            var em = queryWorld.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var near = new System.Collections.Generic.List<(float d, CreatureMode m)>();
            foreach (var c in states)
            {
                var d = math.distance(c.Position.xz, lureAt.xz);
                if (!c.Gone && d < FishLure.Reach && math.abs(c.Position.y - lureAt.y) < 3f)
                    near.Add((d, c.Mode));
            }
            near.Sort((x, y) => x.d.CompareTo(y.d));
            return "near: " + string.Join(", ", near.ConvertAll(n => $"{n.d:F2} {n.m}"));
        }
    }
}
