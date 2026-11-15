using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-103 row 2: the sea folk of the lake village. One household lives in each shell dome (the overworld table's
    /// SeaHousehold rows, Count folk per dome). They breathe water and walk the lake bed as dry ground (CreatureBlob
    /// WalksBed): never swimming, they wander between their dome, the spire in the middle and the reef behind them, and
    /// go in by their own dome's door at night (CreatureState.OwnDoor). The landing on the bank has a few working folk:
    /// a fisher in the shallows beside the pier and a raft hand alongside the raft, homed by the nearest dome's door, so
    /// they walk back down the bed at dusk. Host only; the folk are ghosts like any other. They draw from their own rng
    /// per landmark, so no other spawn's draws move, and they add no placement.
    /// </summary>
    public partial class FaunaDirector
    {
        const string SeaDomeName = "SeaFolkDome", SeaReefName = "SeaFolkReef";
        /// <summary>How far in from a dome's middle toward the village's middle its door spot is (the dome is 7 to 9.5 m
        /// tall, about as wide; its arch faces the spire).</summary>
        const float SeaDomeDoor = 5.2f;
        const uint SeaSalt = 0x5EAF03u;
        /// <summary>How deep the landing fisher's spot is: just past the waterline, knee deep beside the pier's first piles
        /// (the bank drops steeply, so the scan steps finely or it overshoots into chest-deep water).</summary>
        const float SeaFisherDepth = 0.35f;

        readonly List<string> seaLog = new List<string>();
        readonly List<float4> seaDoors = new List<float4>();
        readonly List<float2> seaReefs = new List<float2>();

        /// <summary>Peoples a sea folk village's domes, or a sea folk landing's pier and raft (nothing for any other landmark).</summary>
        void SpawnSeaFolk(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, float radius)
        {
            if (overworld == null || type == null || !floraBlob.IsCreated)
                return;
            if (type.name == SeaFolkBaking.VillageName)
                SpawnSeaVillage(source, type, inst, index, radius);
            else if (type.name == SeaFolkBaking.LandingName)
                SpawnSeaLanding(source, inst, index);
        }

        /// <summary>Each dome's door (xz, yaw toward the village's middle in w) and the reef spots, as placed.</summary>
        bool SeaVillageSpots(in LandmarkInstance inst, float radius)
        {
            seaDoors.Clear();
            seaReefs.Clear();
            seaClams.Clear();
            seaGlows.Clear();
            ref var f = ref floraBlob.Value;
            if (inst.Type * 3 + 2 >= f.PropRanges.Length)
                return false;
            for (int l = 0; l < 3; l++)
            {
                var placed = f.PropRanges[inst.Type * 3 + l];
                for (int e = placed.x; e < placed.x + placed.y; e++)
                {
                    var prop = f.Props[e];
                    if (prop.Species < 0 || prop.Species >= floraSpecies.Count || floraSpecies[prop.Species] == null)
                        continue;
                    var name = floraSpecies[prop.Species].name;
                    if (name != SeaDomeName && name != SeaReefName && name != SeaClamName && name != SeaGlowName)
                        continue;
                    for (int k = 0; k < prop.Count; k++)
                    {
                        var at = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / math.max(1, prop.Count)), radius);
                        if (name != SeaDomeName)
                        {
                            // (Row 3: the clam beds and glow stones, for their keepers.)
                            (name == SeaReefName ? seaReefs : name == SeaClamName ? seaClams : seaGlows).Add(at);
                            continue;
                        }
                        var toCentre = math.normalizesafe(inst.Position - at, new float2(0f, 1f));
                        var door = at + toCentre * SeaDomeDoor;
                        seaDoors.Add(new float4(door.x, 0f, door.y, math.atan2(toCentre.x, toCentre.y)));
                    }
                }
            }
            return seaDoors.Count > 0;
        }

        void SpawnSeaVillage(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, float radius)
        {
            var rows = TableRows(OverworldSlot.SeaHousehold, type).ToArray();
            if (rows.Length == 0 || !SeaVillageSpots(inst, radius))
                return;
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)SeaSalt)) | 1u);
            var spire = inst.Position;
            var total = 0;
            seaLog.Clear();
            for (int d = 0; d < seaDoors.Count; d++)
            {
                var door = seaDoors[d];
                var doorAt = door.xz;
                var toCentre = new float2(math.sin(door.w), math.cos(door.w));
                var side = new float2(toCentre.y, -toCentre.x);
                var dome = doorAt - toCentre * SeaDomeDoor;
                // Its reef: the nearest one behind or beside the dome.
                var reef = dome - toCentre * radius * 0.24f;
                var best = float.MaxValue;
                foreach (var rf in seaReefs)
                    if (math.distancesq(rf, dome) < best)
                    {
                        best = math.distancesq(rf, dome);
                        reef = rf;
                    }
                var residents = 0;
                foreach (var ri in rows)
                {
                    var row = overworld.Rows[ri];
                    var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                    for (int n = 0; n < count; n++)
                    {
                        // The first keeps by its door, the second between the dome and the spire, the third out at the reef.
                        float2 home, face;
                        float leash;
                        switch (n % 3)
                        {
                            case 0:
                                home = doorAt + side * r.NextFloat(-2f, 2f) + toCentre * 1.5f;
                                face = home + toCentre;
                                leash = math.max(6f, row.Range * 1.4f);
                                break;
                            case 1:
                                home = math.lerp(doorAt, spire, r.NextFloat(0.4f, 0.6f));
                                face = spire;
                                leash = math.max(8f, row.Range * 2f);
                                break;
                            default:
                                home = reef + math.normalizesafe(dome - reef) * 3f;
                                face = reef;
                                leash = math.max(6f, row.Range * 1.4f);
                                break;
                        }
                        var post = new Post { Job = FolkJob.None, Task = CreatureTask.None, Stand = home, Face = face };
                        if (SpawnSea(source, row, post, door, leash, ref r))
                            residents++;
                    }
                }
                total += residents;
                seaLog.Add($"dome {d} residents {residents}");
            }
            Debug.Log($"[seafolk] village {index} at ({spire.x:F0}, {spire.y:F0}): {seaDoors.Count} domes, {total} folk; " + string.Join("; ", seaLog));
            // Row 3: its children and keepers, from their own rng.
            SpawnSeaLife(source, type, inst, index, overworld.Rows[rows[0]]);
        }

        void SpawnSeaLanding(in TerrainSource source, in LandmarkInstance inst, int index)
        {
            // The village it serves: the nearest sea folk village (its rows and its dome doors).
            ref var set = ref source.Landmarks.Value;
            var village = -1;
            var best = float.MaxValue;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var t = set.Instances[i].Type;
                if (t >= landmarks.Length || landmarks[t] == null || landmarks[t].name != SeaFolkBaking.VillageName)
                    continue;
                var dist = math.distancesq(set.Instances[i].Position, inst.Position);
                if (dist < best)
                {
                    best = dist;
                    village = i;
                }
            }
            if (village < 0)
                return;
            var vInst = set.Instances[village];
            var vType = landmarks[vInst.Type];
            var rows = TableRows(OverworldSlot.SeaHousehold, vType).ToArray();
            if (rows.Length == 0 || !SeaVillageSpots(vInst, set.Types[vInst.Type].Radius * vInst.Scale))
                return;
            var row = overworld.Rows[rows[0]];
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)SeaSalt ^ 0x1A4D)) | 1u);
            var radius = set.Types[inst.Type].Radius * inst.Scale;
            var outward = math.normalizesafe(inst.Facing, math.normalizesafe(vInst.Position - inst.Position));
            var side = new float2(outward.y, -outward.x);
            var pier = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(0f, 0f), radius);
            var raft = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(0.95f, 60f), radius);
            var spawned = 0;

            // The fisher: out past the waterline, to one side of the pier's piles, fishing out over the lake.
            var fish = pier + side * 2.4f;
            for (float t = 0f; t < 20f; t += 0.2f)
            {
                var g = source.SampleGround(pier + side * 2.4f + outward * t);
                if (g.HasWater && g.WaterSurface - g.Height >= SeaFisherDepth)
                {
                    fish = pier + side * 2.4f + outward * t;
                    break;
                }
            }
            if (SpawnSea(source, row, new Post { Job = FolkJob.Fisher, Task = CreatureTask.Fish, Stand = fish, Face = fish + outward * 3f },
                         NearestDoor(fish), 6f, ref r))
                spawned++;
            // The raft hand: alongside the raft (on the side away from the pier), fishing off it.
            var away = math.dot(raft - pier, side) >= 0f ? side : -side;
            var hand = raft + away * 2.2f;
            if (SpawnSea(source, row, new Post { Job = FolkJob.Fisher, Task = CreatureTask.Fish, Stand = hand, Face = hand + outward * 3f },
                         NearestDoor(hand), 6f, ref r))
                spawned++;
            Debug.Log($"[seafolk] landing {index} at ({inst.Position.x:F0}, {inst.Position.y:F0}) for village {village}: {spawned} workers " +
                      $"(fisher at {fish.x:F0},{fish.y:F0}; raft hand at {hand.x:F0},{hand.y:F0})");
        }

        /// <summary>The nearest of the village's dome doors (from the last SeaVillageSpots).</summary>
        float4 NearestDoor(float2 at)
        {
            var door = seaDoors[0];
            foreach (var d in seaDoors)
                if (math.distancesq(d.xz, at) < math.distancesq(door.xz, at))
                    door = d;
            return door;
        }

        /// <summary>One sea folk at its post on the bed (counted with the settled ones), homed by its dome's door.</summary>
        bool SpawnSea(in TerrainSource source, SpawnRow row, Post post, float4 door, float leash, ref Unity.Mathematics.Random r)
        {
            if (row.Species == null || settledCount >= MaxSettled)
                return false;
            var index = SpeciesIndex(row.Species);
            if (index < 0)
                return false;
            var worker = post.Task != CreatureTask.None;
            var p = post.Stand + r.NextFloat2Direction() * (worker ? 0.2f : 0.8f);
            var y = source.SampleHeight(p);
            var timer = r.NextFloat(40f, 120f);
            var yaw = math.atan2(post.Face.x - p.x, post.Face.y - p.y);
            // (A-105: its place in its dome's household, if one's free.)
            var slot = HousedFolk(index, out var child) ? TakeDoorSlot(door, child) : -1;
            Enqueue(index, new float3(p.x, y, p.y), SpawnTier.Settled, e =>
            {
                var em = simWorld.EntityManager;
                var state = em.GetComponentData<CreatureState>(e);
                state.Home = post.Stand;
                state.Leash = leash;
                state.OwnDoor = door;
                Settle(ref state, slot);
                state.Job = (byte)post.Job;
                if (worker)
                {
                    state.Task = post.Task;
                    state.WorkSpot = post.Stand;
                    state.WorkFace = post.Face;
                    state.Mode = CreatureMode.Work;
                    state.Timer = timer;
                }
                em.SetComponentData(e, state);
            }, yaw);
            return true;
        }
    }
}
