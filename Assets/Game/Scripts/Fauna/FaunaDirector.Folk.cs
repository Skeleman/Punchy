using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 4: the folk spawns (wild folk, crossing fishers, residents, village posts), moved out of
    // FaunaDirector.cs as they were.
    public partial class FaunaDirector
    {
        /// <summary>A-106 row 2: the chore a hermit at this spot takes from its biome's work set (the set's first post),
        /// or None in a biome with no set. Local (Job is not a ghost field): its Stage at work carries it to clients.</summary>
        FolkJob HermitChoreAt(in TerrainSource source, float2 at)
        {
            var ground = source.SampleGround(at).Biomes.Dominant();
            var set = ground >= 0 && ground < biomes.Length ? FolkWorkSets.For(biomes[ground]) : null;
            return set != null && set.Jobs.Length > 0 ? set.Jobs[0].Job : FolkJob.None;
        }

        /// <summary>
        /// A-106 row 2, for tests and shots: a stand-in hermit living at a spot with no shelter (an open one: it sleeps
        /// out on the ground there), set up like a placed one, its chore from the biome there. The world places no hermit
        /// shelter in some biomes (no tundra hut), so this shows one there without touching placement. Host only.
        /// </summary>
        public Entity SpawnHermitAt(float2 at)
        {
            if (simWorld == null || !built)
                return Entity.Null;
            var hermit = TableSpecies(OverworldSlot.Hermit, 0);
            if (hermit < 0)
                hermit = TableSpecies(OverworldSlot.Camper, 0);
            if (hermit < 0)
                return Entity.Null;
            var em = simWorld.EntityManager;
            var e = Spawn(hermit, new float3(at.x, lastSource.SampleHeight(at), at.y));
            var st = em.GetComponentData<CreatureState>(e);
            st.Task = CreatureTask.Hermit;
            st.Home = at;
            st.Leash = 35f;
            st.Anchor = new float3(at.x, lastSource.SampleHeight(at), at.y);
            st.Job = (byte)HermitChoreAt(lastSource, at);
            em.SetComponentData(e, st);
            return e;
        }

        /// <summary>
        /// The folk of the wild places near players: a hermit at each hermit's shelter (its Home anchor) within 120 m of
        /// one, while none lives there; and, from late afternoon, now and then a traveller walking in from the road to a
        /// roadside camp with a free bedroll, to spend the night there.
        /// </summary>
        void SpawnWildFolk(in TerrainSource source)
        {
            if (!source.Anchors.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            var human = TableSpecies(OverworldSlot.Camper, 0);
            var hermit = TableSpecies(OverworldSlot.Hermit, 0);
            if (hermit < 0)
                hermit = human;
            if (human < 0)
                return;
            ref var set = ref source.Anchors.Value;
            var near = new NativeList<int>(8, Allocator.Temp);
            foreach (var player in players)
            {
                near.Clear();
                Anchors.Near(ref set, player, 120f, AnchorKind.Home, ref near);
                foreach (var i in near)
                {
                    if (hermits.TryGetValue(i, out var lives) && em.Exists(lives))
                        continue;
                    var home = set.Anchors[i];
                    // (Out in front of its shelter, getting on with its day.)
                    var at = home.Position.xz + home.Facing * 3f;
                    var e = Spawn(hermit, new float3(at.x, source.SampleHeight(at), at.y));
                    var st = em.GetComponentData<CreatureState>(e);
                    st.Task = CreatureTask.Hermit;
                    st.Home = home.Position.xz;
                    st.Leash = 35f;
                    st.Anchor = home.Position;
                    st.Job = (byte)HermitChoreAt(source, home.Position.xz);
                    // (A-105: the head of the hut by its shelter, if it has one.)
                    if (home.InLandmark)
                    {
                        EnsureHouses(source);
                        Settle(ref st, PinHut(home.OwnerIndex, home.Position.xz));
                    }
                    em.SetComponentData(e, st);
                    hermits[i] = e;
                    settledCount++;
                }
                // A traveller for a camp: from late afternoon to the small hours, now and then.
                var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
                if (Time.time < nextCamper || !(hour >= 16.5f || hour < 2f))
                    continue;
                near.Clear();
                Anchors.Near(ref set, player, 120f, AnchorKind.Camp, ref near);
                foreach (var i in near)
                {
                    if (!campers.TryGetValue(i, out var list))
                        campers[i] = list = new List<Entity>();
                    list.RemoveAll(x => !em.Exists(x));
                    var camp = set.Anchors[i];
                    if (list.Count >= math.max((int)camp.Capacity, 1))
                        continue;
                    nextCamper = Time.time + rng.NextFloat(40f, 90f);
                    // (Walking in from the way the road lies.)
                    var at = camp.Position.xz + camp.Facing * 45f + new float2(-camp.Facing.y, camp.Facing.x) * rng.NextFloat(-8f, 8f);
                    var e = Spawn(human, new float3(at.x, source.SampleHeight(at), at.y));
                    var st = em.GetComponentData<CreatureState>(e);
                    st.Task = CreatureTask.Camper;
                    st.Home = camp.Position.xz;
                    st.Leash = 60f;
                    st.Anchor = camp.Position;
                    em.SetComponentData(e, st);
                    list.Add(e);
                    settledCount++;
                    break;
                }
            }
            near.Dispose();
        }

        /// <summary>The fisher standing at each road crossing (by its index in RoadCrossings.List), and those gone home tonight.</summary>
        readonly Dictionary<int, Entity> crossingFishers = new Dictionary<int, Entity>();
        readonly HashSet<int> crossingLeft = new HashSet<int>();
        int crossingsVersion;

        /// <summary>
        /// A-31: where a road crosses a river, a seeded one in three has a fisher on the bank (RoadCrossings), spawned by
        /// day while a player is within 120 m. Its folk are the nearest settlement's; if that village is within 250 m it
        /// goes home there at night (its leash reaches the doors), else it's a lone fisher who packs up at dusk and walks
        /// off along the road, and isn't back till the next day. Nothing is drawn from the rng but the body's own.
        /// </summary>
        void SpawnCrossingFishers(in TerrainSource source)
        {
            if (simWorld == null || !source.Settlements.IsCreated)
                return;
            RoadCrossings.Refresh(source);
            if (RoadCrossings.Version != crossingsVersion)
            {
                crossingsVersion = RoadCrossings.Version;
                crossingFishers.Clear();
                crossingLeft.Clear();
                LogCrossings(source);
            }
            var em = simWorld.EntityManager;
            var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
            var day = hour >= 5.5f && hour < 19.5f;
            if (day)
                crossingLeft.Clear();
            ref var set = ref source.Settlements.Value;
            var list = RoadCrossings.List;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.Fisher || !c.HasSpot)
                    continue;
                if (crossingFishers.TryGetValue(i, out var e) && em.Exists(e))
                {
                    // Dusk: a lone fisher leaves along the road (a destination, not a wander) and the despawn takes it.
                    var s = em.GetComponentData<CreatureState>(e);
                    if (!day && s.Task == CreatureTask.Fish && s.Leash < 10f)
                    {
                        s.Task = CreatureTask.None;
                        s.Mode = CreatureMode.Idle;
                        s.Home = c.Bank + c.Outward * 60f;
                        s.Leash = 6f;
                        em.SetComponentData(e, s);
                        crossingFishers.Remove(i);
                        crossingLeft.Add(i);
                    }
                    continue;
                }
                if (!day || crossingLeft.Contains(i) || NearestPlayer(c.Spot) > 120f || settledCount >= MaxSettled)
                    continue;
                var village = NearestSettlement(ref set, c.Spot, out var gap);
                var index = village >= 0 ? CrossingFolk(set.Settlements[village].Type) : -1;
                if (index < 0)
                    index = TableSpecies(OverworldSlot.CrossingFisher, (uint)i);
                if (index < 0)
                    return;
                var yaw = math.atan2(c.Face.x, c.Face.y);
                e = Spawn(index, new float3(c.Spot.x, source.SampleHeight(c.Spot), c.Spot.y), yaw);
                var st = em.GetComponentData<CreatureState>(e);
                st.Task = CreatureTask.Fish;
                st.Job = (byte)FolkJob.Fisher;
                st.WorkSpot = c.Spot;
                st.WorkFace = c.Spot + c.Face * 4f;
                st.Mode = CreatureMode.Work;
                st.Timer = 200f;
                st.Home = c.Spot;
                // (A village fisher's leash reaches its village's doors, for home at night; a lone one stays put.)
                st.Leash = village >= 0 && gap < 250f ? math.distance(c.Spot, set.Settlements[village].Centre) + 10f : 8f;
                em.SetComponentData(e, st);
                crossingFishers[i] = e;
                settledCount++;
            }
        }

        static int NearestSettlement(ref SettlementSetBlob set, float2 at, out float gap)
        {
            var best = -1;
            gap = float.MaxValue;
            for (int i = 0; i < set.Settlements.Length; i++)
            {
                var d = math.distance(set.Settlements[i].Centre, at) - set.Settlements[i].Radius;
                if (d < gap)
                {
                    gap = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The first free grown folk species of a landmark type (its race), or -1.</summary>
        int CrossingFolk(int type)
        {
            if (type < 0 || type >= landmarks.Length || landmarks[type] == null)
                return -1;
            foreach (var r in landmarks[type].Residents)
                if (FreeFolk(r) && SpeciesIndex(r.Species) >= 0)
                    return SpeciesIndex(r.Species);
            return -1;
        }

        /// <summary>The crossing list once per island: counts and five samples (the same every run: seed-derived).</summary>
        void LogCrossings(in TerrainSource source)
        {
            var list = RoadCrossings.List;
            int bridges = 0, spots = 0, fishers = 0, lone = 0, rolled = 0;
            ref var set = ref source.Settlements.Value;
            var sb = new System.Text.StringBuilder();
            foreach (var c in list)
            {
                bridges += c.Bridge ? 1 : 0;
                spots += c.HasSpot ? 1 : 0;
                rolled += c.Fisher && !c.Fallback ? 1 : 0;
                if (c.Fisher && c.HasSpot)
                {
                    fishers++;
                    NearestSettlement(ref set, c.Spot, out var gap);
                    lone += gap >= 250f ? 1 : 0;
                    sb.Append($" ({c.Spot.x:F0},{c.Spot.y:F0}{(c.Bridge ? " bridge" : " ford")}{(c.Fallback ? " best score" : "")})");
                }
            }
            var samples = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < 5; i++)
                samples.Append($" [{list[i].Centre.x:F1},{list[i].Centre.y:F1} {(list[i].Bridge ? "bridge" : "ford")} spot {(list[i].HasSpot ? $"{list[i].Spot.x:F1},{list[i].Spot.y:F1}" : "none")}]");
            Debug.Log($"Crossings: {list.Count} ({bridges} bridges, {list.Count - bridges} fords), {spots} with a bank spot, " +
                      $"fishers {fishers} ({lone} lone; {rolled} rolled); samples{samples}; fishers at{sb}");
        }

        void SpawnResidents(in TerrainSource source)
        {
            if (!source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            LogBatJobs(source);
            LogChamberBands(source);
            LogSpawnTables(source);
            scratch.Clear();
            foreach (var i in occupied)
                if (NearestPlayer(set.Instances[i].Position) > DespawnDistance + 20f)
                    scratch.Add(i);
            foreach (var i in scratch)
                occupied.Remove(i);

            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                if (inst.Type >= landmarks.Length || landmarks[inst.Type] == null || occupied.Contains(i) ||
                    NearestPlayer(inst.Position) > Radius)
                    continue;
                // (Full of settled folk and animals already: this one is peopled once some are let go.)
                if (settledCount >= MaxSettled)
                    break;
                occupied.Add(i);
                var place = inst;
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                // (A-105: its houses' households, empty till its folk spawn into them.)
                EnsureHouses(source);
                ResetHouses(i);
                SpawnDomestics(source, landmarks[inst.Type], inst.Position, radius);
                SpawnLakeLife(source, landmarks[inst.Type], inst, i, radius);   // (A-88 row 2: the sea folk lake's fish)
                SpawnMarshLife(source, landmarks[inst.Type], inst, i, radius);  // (A-89 row 3: the marsh folk village's frogs, heron, dragonflies)
                SpawnTurtleFolk(source, landmarks[inst.Type], inst, i, radius); // (A-103 row 1: a turtle folk household in each igloo)
                SpawnSeaFolk(source, landmarks[inst.Type], inst, i, radius);    // (A-103 row 2: a sea folk household in each dome, workers at the landing)
                // Village jobs (A-29): free grown townsfolk take the posts this landmark's anchors offer (a baker at the
                // hearth, a herder by the yard, a fisher at the nearest water...), more the bigger the village, at least
                // one left free. Sized from the residents' smallest counts and no rng drawn, so every spawn draw stays.
                var freeFolk = 0;
                foreach (var r in landmarks[inst.Type].Residents)
                    if (FreeFolk(r))
                        freeFolk += math.max(0, r.Count.x);
                // (A-106: a harsh biome's settlement also takes its work set's posts, from the biome under its centre.)
                var ground = source.SampleGround(inst.Position).Biomes.Dominant();
                var workSet = ground >= 0 && ground < biomes.Length ? FolkWorkSets.For(biomes[ground]) : null;
                VillagePosts(source, inst.Position, radius, freeFolk, workSet, posts);
                var nextPost = 0;
                jobCounts.Clear();
                var stayFree = 0;
                foreach (var r in landmarks[inst.Type].Residents)
                {
                    var index = SpeciesIndex(r.Species);
                    if (index < 0)
                        continue;
                    var count = rng.NextInt(math.max(0, r.Count.x), math.max(r.Count.x, r.Count.y) + 1);
                    // Residents with a leash live round their own home spot (a pen); workers at their post (and stay near
                    // it); the rest anywhere in the landmark.
                    var worker = r.Task != CreatureTask.None;
                    var stationed = r.Leash > 0f || worker;
                    var home = stationed ? LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.HomeRing, r.HomeAngle), radius) : inst.Position;
                    var leash = r.Leash > 0f ? r.Leash : worker ? 7f : radius * 0.8f;
                    var face = worker ? LandmarkSampling.ToWorld(place, LandmarkLayout.Spot(r.FaceRing, r.FaceAngle), radius) : float2.zero;
                    var task = r.Task;
                    // A smith or woodcutter works where its prop's Work anchor stands it (at the anvil, beside the chopping
                    // block, facing it), if there's one near its post; its home and leash go with it.
                    if (worker && (task == CreatureTask.Hammer || task == CreatureTask.Chop) && source.Anchors.IsCreated &&
                        WorkAnchorNear(ref source.Anchors.Value, new float3(face.x, source.SampleHeight(face), face.y), 12f, out var stand, out var facing))
                    {
                        home = stand;
                        face = stand + facing;
                    }
                    for (int k = 0; k < count && settledCount < MaxSettled; k++)
                    {
                        var p = worker ? home + rng.NextFloat2Direction() * 0.2f
                              : r.Leash > 0f ? home + rng.NextFloat2Direction() * rng.NextFloat(0f, leash * 0.7f)
                              : inst.Position + rng.NextFloat2Direction() * rng.NextFloat(0.1f, 0.45f) * radius;
                        var g = source.SampleGround(p);
                        if (g.HasWater && g.WaterSurface > g.Height)
                            continue;
                        var scale = r.Size.y > 0f ? rng.NextFloat(r.Size.x, math.max(r.Size.x, r.Size.y)) : 0f;
                        var timer = rng.NextFloat(40f, 120f);
                        // (A free folk taking the next post: it lives and works there, like a landmark's own worker.)
                        var posted = false;
                        var post = default(Post);
                        if (FreeFolk(r) && nextPost < posts.Count && (stayFree > 0 || nextPost < freeFolk - 1))
                        {
                            post = posts[nextPost++];
                            posted = true;
                        }
                        else if (FreeFolk(r))
                            stayFree++;
                        var job = posted ? post.Job : FolkJobs.FromTask(task);
                        if (job != FolkJob.None)
                            jobCounts[job] = (jobCounts.TryGetValue(job, out var had) ? had : 0) + 1;
                        var myHome = posted ? post.Stand : home;
                        var myLeash = posted ? post.Task == CreatureTask.Patrol ? radius * 0.8f : 7f : leash;
                        // (A-105: a folk takes the next free place in one of the landmark's houses.)
                        var slot = HousedFolk(index, out var child) ? TakeHouseSlot(i, child) : -1;
                        Enqueue(index, new float3(p.x, g.Height, p.y), SpawnTier.Settled, e =>
                        {
                            var em = simWorld.EntityManager;
                            var state = em.GetComponentData<CreatureState>(e);
                            state.Home = myHome;
                            state.Leash = myLeash;
                            Settle(ref state, slot);
                            if (scale > 0f)
                                state.Scale = scale;
                            if (worker)
                            {
                                state.Task = task;
                                state.WorkSpot = home;
                                state.WorkFace = face;
                                state.Mode = CreatureMode.Work;
                                state.Timer = timer;
                            }
                            else if (posted)
                            {
                                state.Task = post.Task;
                                state.Job = (byte)post.Job;
                                state.WorkSpot = post.Stand;
                                state.WorkFace = post.Face;
                                state.Mode = CreatureMode.Work;
                                state.Timer = timer;
                            }
                            em.SetComponentData(e, state);
                        });
                    }
                }
                if (jobCounts.Count > 0 || stayFree > 0)
                {
                    var line = new System.Text.StringBuilder();
                    foreach (var pair in jobCounts)
                        line.Append(line.Length > 0 ? ", " : "").Append(pair.Key).Append(' ').Append(pair.Value);
                    Debug.Log($"FolkJobs: {landmarks[inst.Type].name} at ({inst.Position.x:F0}, {inst.Position.y:F0}) radius {radius:F0}: {line}, free {stayFree}");
                }
                LogLandmarkHouses(i, landmarks[inst.Type].name, inst.Position);
            }
        }

        struct Post
        {
            public FolkJob Job;
            public CreatureTask Task;
            public float2 Stand, Face;
        }

        readonly List<Post> posts = new List<Post>();
        readonly Dictionary<FolkJob, int> jobCounts = new Dictionary<FolkJob, int>();

        /// <summary>A resident with no task, pen or leash, grown and upright: one who can be handed a village job.</summary>
        static bool FreeFolk(LandmarkResident r) =>
            r.Task == CreatureTask.None && r.Leash <= 0f && r.Species != null && r.Species.Body != null && r.Species.Body.Upright &&
            !r.Species.name.Contains("Child");

        /// <summary>
        /// The village posts (FolkJobs.VillagePosts) a landmark can staff with this many free folk: each by the
        /// nearest unused anchor of its kind. Standing a step from the anchor, toward the village centre, facing it;
        /// a fisher at the water's edge (its brain finds the shore from there). A-106: a harsh biome's work set adds its
        /// posts after the usual ones that need as many free folk; a door job stands on the doorstep facing the door.
        /// No rng, and with no work set the posts are exactly the usual ones.
        /// </summary>
        static void VillagePosts(in TerrainSource source, float2 centre, float radius, int free, FolkWorkSet workSet, List<Post> into)
        {
            into.Clear();
            if (!source.Anchors.IsCreated || free < 2)
                return;
            postOrder.Clear();
            postOrder.AddRange(FolkJobs.VillagePosts);
            if (workSet != null)
                foreach (var e in workSet.Jobs)
                {
                    var at2 = 0;
                    while (at2 < postOrder.Count && postOrder[at2].MinFree <= e.MinFree)
                        at2++;
                    postOrder.Insert(at2, (e.Kind, e.Job, FolkJobs.TaskOf(e.Job), e.MinFree, e.Beyond));
                }
            ref var set = ref source.Anchors.Value;
            var near = new NativeList<int>(16, Allocator.Temp);
            var used = new NativeList<int>(8, Allocator.Temp);
            var at = new float3(centre.x, source.SampleHeight(centre), centre.y);
            foreach (var p in postOrder)
            {
                if (free < p.MinFree)
                    continue;
                var taken = false;
                foreach (var q in into)
                    taken |= q.Job == p.Job;
                if (taken)
                    continue;
                near.Clear();
                Anchors.Near(ref set, at, radius + p.Beyond, p.Kind, ref near);
                var best = -1;
                var bestD = float.MaxValue;
                foreach (var i in near)
                {
                    var d = math.distancesq(set.Anchors[i].Position.xz, centre);
                    if (d < bestD && !used.Contains(i))
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (best < 0)
                    continue;
                used.Add(best);
                var spot = set.Anchors[best].Position.xz;
                var toCentre = centre - spot;
                var dir = math.lengthsq(toCentre) > 0.01f ? math.normalize(toCentre) : new float2(0f, 1f);
                if (p.Kind == AnchorKind.Door)
                {
                    // (The door anchor already stands outside the door, facing out: work a step further out, facing in.)
                    var outward = set.Anchors[best].Facing;
                    outward = math.lengthsq(outward) > 0.01f ? math.normalize(outward) : dir;
                    into.Add(new Post { Job = p.Job, Task = p.Task, Stand = spot + outward * 0.9f, Face = spot - outward });
                    continue;
                }
                var stand = p.Task == CreatureTask.Fish || p.Task == CreatureTask.Patrol ? spot : spot + dir * 1.2f;
                into.Add(new Post { Job = p.Job, Task = p.Task, Stand = stand, Face = p.Task == CreatureTask.Fish ? spot - dir : spot });
            }
            near.Dispose();
            used.Dispose();
        }

        static readonly List<(AnchorKind Kind, FolkJob Job, CreatureTask Task, int MinFree, float Beyond)> postOrder =
            new List<(AnchorKind, FolkJob, CreatureTask, int, float)>();

        // (The chambers last logged, by count and first centre: the v1 bake comes first and can have none, then v2's.)
        float4 batJobsLogged = new float4(-1f);

        /// <summary>
        /// Once per island: the jobs each bat folk chamber would hand out, one per prop (a roost keeper per hut, a tanner per
        /// drying rack, a forager per basket) and a fisher for its white pool. The chamber folk (the blind cave pygmies,
        /// #10) take these posts when a player comes near: SpawnChamberBands.
        /// </summary>
        void LogBatJobs(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated)
                return;
            ref var bats = ref source.BatFolk.Value;
            var key = new float4(bats.Sites.Length, bats.Sites.Length > 0 ? bats.Sites[0].Centre : float3.zero);
            if (math.all(key == batJobsLogged))
                return;
            batJobsLogged = key;
            for (int s = 0; s < bats.Sites.Length; s++)
            {
                int huts = 0, racks = 0, baskets = 0;
                for (int i = 0; i < bats.Props.Length; i++)
                    if (bats.Props[i].Site == s)
                    {
                        var job = FolkJobs.BatJob(bats.Props[i].Kind);
                        if (job == FolkJob.RoostKeeper) huts++;
                        else if (job == FolkJob.Tanner) racks++;
                        else baskets++;
                    }
                var site = bats.Sites[s];
                Debug.Log($"FolkJobs: bat folk chamber {s} (cave {site.Cave}) at ({site.Centre.x:F0}, {site.Centre.y:F0}, {site.Centre.z:F0}) radius {site.Radius:F0}: " +
                          $"RoostKeeper {huts}, Tanner {racks}, Forager {baskets}, PoolFisher 1");
            }
        }

        float nextTraveller;
        float nextMessenger = 30f;
        float nextHoneyThief = 60f;
        Entity honeyThief;
    }
}
