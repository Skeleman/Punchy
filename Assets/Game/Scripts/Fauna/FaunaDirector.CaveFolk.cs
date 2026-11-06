using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The folk of the bat folk chambers (#10, #35): a band of the chamber folk (the bat folk, who shun light) in each
    /// chamber a player comes near, at the posts the chamber's props hand out (a sleeper by each hammock, a worker at
    /// each goggle peg, a roost keeper under each hut, a tanner at each drying rack, a forager at each basket, a fisher
    /// at its white pool). Band size and who takes which post come from a hash of the chamber's key, never the
    /// director's rng, so the other spawns keep their draws and the same chamber has the same band on every machine.
    /// The blind cave pygmies share each chamber with the bat folk (#35, Zach 11-04): a small band of their own, homed in
    /// the chamber's wall niches once the world lane places them (else round its middle), who tend the pool and forage
    /// the floor while the bat folk bring goods down from the surface (CreatureBrainSystem.DecideChamberChores).
    /// Homes are the world's BatFolkHomes anchors (W-96): a bat folk ceiling pod (its keeper at its door on the floor
    /// under it, ahead of the hammocks) and a pygmy wall niche (its sleeper at its door, facing in).
    /// </summary>
    public partial class FaunaDirector
    {
        readonly HashSet<int> occupiedChambers = new HashSet<int>();
        readonly List<int> chamberScratch = new List<int>();

        /// <summary>A post in a chamber: where its worker stands, what it faces, and the job.</summary>
        struct ChamberPost
        {
            public float3 Stand;
            public float2 Face;
            public FolkJob Job;
        }

        readonly List<ChamberPost> chamberPosts = new List<ChamberPost>();

        /// <summary>The chamber folk species that owns the chambers: the first that shuns light (the bat folk), else the first, or -1.</summary>
        int ChamberFolkIndex() => ChamberFolkIndex(true);

        /// <summary>The first chamber folk species that does (or doesn't) shun light, else the first chamber folk, or -1.</summary>
        int ChamberFolkIndex(bool shunsLight)
        {
            if (overworld == null)
                return -1;
            CreatureSpecies first = null;
            // (The overworld table's CaveFolk rows, in order.)
            foreach (var ri in TableRows(OverworldSlot.CaveFolk))
                if (overworld.Rows[ri].Species is { } c && c.Body.ChamberFolk)
                {
                    if (c.Body.ShunsLight == shunsLight)
                        return SpeciesIndex(c);
                    first ??= c;
                }
            return first != null ? SpeciesIndex(first) : -1;
        }

        /// <summary>A floor spot under the rock at (or just above) a point, or false.</summary>
        static bool ChamberFloor(in TerrainSource source, float3 at, out float3 floorAt)
        {
            var floor = source.FloorUnder(at, out var inCave);
            floorAt = new float3(at.x, floor, at.z);
            return inCave && source.CaveDistance(new float3(at.x, floor + 0.3f, at.z)) < -0.05f;
        }

        /// <summary>
        /// A chamber's band: its size (the species' group size, 3 to 6 for the pygmies, seeded by the chamber) and the
        /// posts it fills, the pool fisher first, then the huts, racks and baskets in turn so a small band still has a
        /// mix. Fills chamberPosts; returns the band size.
        /// </summary>
        int PlanChamber(in TerrainSource source, int site, CreatureSpecies sp)
        {
            ref var bats = ref source.BatFolk.Value;
            var s = bats.Sites[site];
            var seed = BiomeSampling.Scramble(s.Key ^ 0x68E31DA4u);
            var g = sp.Body.GroupSize;
            var lo = math.max(1, g.x);
            var band = lo + (int)((seed >> 4) % (uint)(math.max(lo, g.y) - lo + 1));
            chamberPosts.Clear();
            if (s.Pool >= 0 && source.Caves.IsCreated && s.Pool < source.Caves.Value.Pools.Length)
            {
                var pool = source.Caves.Value.Pools[s.Pool];
                // On the rim, on the side toward the chamber's middle (a few tries round it).
                var dir = math.normalizesafe(s.Centre.xz - pool.Centre.xz, new float2(1f, 0f));
                for (int t = 0; t < 6; t++)
                {
                    var a = t * 1.05f * (t % 2 == 0 ? 1f : -1f);
                    var d = new float2(dir.x * math.cos(a) - dir.y * math.sin(a), dir.x * math.sin(a) + dir.y * math.cos(a));
                    var at = pool.Centre + new float3(d.x, 0.5f, d.y) * (pool.Radius + 0.6f);
                    if (ChamberFloor(source, at, out var stand))
                    {
                        chamberPosts.Add(new ChamberPost { Stand = stand, Face = pool.Centre.xz, Job = FolkJob.PoolFisher });
                        break;
                    }
                }
            }
            // The homes (BatFolkHomes' ceiling pods, W-96) first, then the props by job in their baked order (hammocks
            // and goggle pegs ahead of the rest), so each has a keeper. A pod's keeper stands at its door, on the floor
            // under it, facing the way it opens.
            var kinds = new[] { BatFolkPropKind.Pod, BatFolkPropKind.Hammock, BatFolkPropKind.GogglePeg, BatFolkPropKind.Hut, BatFolkPropKind.Rack, BatFolkPropKind.Basket };
            var next = new int[kinds.Length];
            var pods = BatFolkHomes.Count(source, BatFolkHomeKind.BatFolkPod, site);
            var any = true;
            while (chamberPosts.Count < band && any)
            {
                any = false;
                for (int k = 0; k < kinds.Length && chamberPosts.Count < band; k++)
                {
                    if (kinds[k] == BatFolkPropKind.Pod)
                    {
                        for (; next[k] < pods; next[k]++)
                        {
                            BatFolkHomes.Get(source, BatFolkHomeKind.BatFolkPod, site, next[k], out var pod);
                            if (!ChamberFloor(source, pod.Door + new float3(0f, 0.4f, 0f), out var door))
                                continue;
                            next[k]++;
                            chamberPosts.Add(new ChamberPost { Stand = door, Face = door.xz + pod.Face, Job = FolkJob.RoostKeeper });
                            any = true;
                            break;
                        }
                        continue;
                    }
                    for (; next[k] < bats.Props.Length; next[k]++)
                    {
                        ref var p = ref bats.Props[next[k]];
                        if (p.Site != site || p.Kind != kinds[k])
                            continue;
                        // A hut hangs from the roof: its keeper stands on the floor under it. A rack or basket's worker
                        // stands a little off it, on the side toward the chamber's middle.
                        var inward = math.normalizesafe(s.Centre.xz - p.Position.xz);
                        var hung = p.Kind == BatFolkPropKind.Hut;
                        var at = hung ? p.Position : p.Position + new float3(inward.x * 0.7f, 0.4f, inward.y * 0.7f);
                        next[k]++;
                        var stand = float3.zero;
                        var found = false;
                        // (Under a hut: a few drops below its anchor, clear of the hut and short of the floor.)
                        for (int d = 0; d < 4 && !found; d++)
                            found = ChamberFloor(source, at - new float3(0f, hung ? 0.6f + d * 1.5f : d * 0.3f, 0f), out stand);
                        if (!found)
                            continue;
                        chamberPosts.Add(new ChamberPost
                        {
                            Stand = stand,
                            Face = hung ? s.Centre.xz : p.Position.xz,
                            // (A home's keeper is its sleeper: the roost keeper's job, at home.)
                            Job = p.Kind == BatFolkPropKind.Hammock ? FolkJob.RoostKeeper : FolkJobs.BatJob(p.Kind)
                        });
                        any = true;
                        break;
                    }
                }
            }
            return band;
        }

        /// <summary>
        /// Spawns the band of each bat folk chamber a player comes near (once while they stay about), each at its post;
        /// any beyond the posts go about the chamber. Forgets a chamber once every player is well away.
        /// </summary>
        void SpawnChamberBands(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated || !source.Caves.IsCreated)
                return;
            var sp = ChamberFolkIndex();
            if (sp < 0)
                return;
            ref var bats = ref source.BatFolk.Value;
            chamberScratch.Clear();
            foreach (var i in occupiedChambers)
                if (i >= bats.Sites.Length || NearestPlayer(bats.Sites[i].Centre.xz) > DespawnDistance + 20f)
                    chamberScratch.Add(i);
            foreach (var i in chamberScratch)
                occupiedChambers.Remove(i);
            for (int i = 0; i < bats.Sites.Length; i++)
            {
                var site = bats.Sites[i];
                if (occupiedChambers.Contains(i) || NearestPlayer(site.Centre.xz) > Radius)
                    continue;
                if (caveCount >= MaxCave)
                    break;
                occupiedChambers.Add(i);
                var band = PlanChamber(source, i, species[sp]);
                var r = new Unity.Mathematics.Random(BiomeSampling.Scramble(site.Key ^ 0x2C1B3C6Du) | 1u);
                for (int k = 0; k < band && caveCount < MaxCave; k++)
                {
                    var posted = k < chamberPosts.Count;
                    var post = posted ? chamberPosts[k] : default;
                    // Each comes in a little off its post (the free ones round the middle), and walks to it.
                    var at = posted ? post.Stand : site.Centre;
                    var off = r.NextFloat2Direction() * r.NextFloat(0.5f, posted ? 1.5f : site.Radius * 0.5f);
                    if (!ChamberFloor(source, at + new float3(off.x, 0.4f, off.y), out var start) && !ChamberFloor(source, at + new float3(0f, 0.4f, 0f), out start))
                        continue;
                    var home = posted ? post.Stand.xz : site.Centre.xz;
                    var leash = posted ? 7f : math.max(site.Radius, 6f);
                    var timer = r.NextFloat(40f, 120f);
                    Enqueue(sp, start, SpawnTier.Cave, e =>
                    {
                        var em = simWorld.EntityManager;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Home = home;
                        state.Leash = leash;
                        if (posted)
                        {
                            state.Task = post.Job == FolkJob.PoolFisher ? CreatureTask.Fish : CreatureTask.Tend;
                            state.Job = (byte)post.Job;
                            state.WorkSpot = post.Stand.xz;
                            state.WorkFace = post.Face;
                            state.Mode = CreatureMode.Work;
                            state.Timer = timer;
                        }
                        em.SetComponentData(e, state);
                    });
                }
                SpawnPygmies(source, i);
            }
        }




        /// <summary>
        /// The pygmies' share of a chamber: 2 or 3 of them (seeded by the chamber, apart from the bat folk's draws), each
        /// homed at one of the chamber's wall niches in turn when it has any (standing at its mouth), else round the
        /// chamber's middle. Fills chamberPosts with their homes; returns the band size.
        /// </summary>
        int PlanPygmies(in TerrainSource source, int site)
        {
            ref var bats = ref source.BatFolk.Value;
            var s = bats.Sites[site];
            var band = 2 + (int)((BiomeSampling.Scramble(s.Key ^ 0x51ED270Bu) >> 5) % 2u);
            chamberPosts.Clear();
            // (BatFolkHomes' niches, W-96: home is the niche's door, on the cave floor in front of it, facing in.)
            var niches = BatFolkHomes.Count(source, BatFolkHomeKind.PygmyNiche, site);
            for (int i = 0; i < niches && chamberPosts.Count < band; i++)
            {
                BatFolkHomes.Get(source, BatFolkHomeKind.PygmyNiche, site, i, out var niche);
                if (ChamberFloor(source, niche.Door + new float3(0f, 0.4f, 0f), out var stand))
                    chamberPosts.Add(new ChamberPost { Stand = stand, Face = niche.Stand.xz, Job = FolkJob.None });
            }
            return band;
        }

        /// <summary>Spawns a chamber's pygmy band (after its bat folk, so theirs keep their draws), homed by PlanPygmies.</summary>
        void SpawnPygmies(in TerrainSource source, int site)
        {
            var sp = ChamberFolkIndex(false);
            if (sp < 0 || sp == ChamberFolkIndex(true))
                return;
            var s = source.BatFolk.Value.Sites[site];
            var band = PlanPygmies(source, site);
            var r = new Unity.Mathematics.Random(BiomeSampling.Scramble(s.Key ^ 0x3A7F19C5u) | 1u);
            for (int k = 0; k < band && caveCount < MaxCave; k++)
            {
                var homed = k < chamberPosts.Count;
                var at = homed ? chamberPosts[k].Stand : s.Centre;
                var off = r.NextFloat2Direction() * r.NextFloat(0.5f, homed ? 1.5f : math.max(1f, s.Radius * 0.5f));
                if (!ChamberFloor(source, at + new float3(off.x, 0.4f, off.y), out var start) && !ChamberFloor(source, at + new float3(0f, 0.4f, 0f), out start))
                    continue;
                var home = homed ? chamberPosts[k].Stand.xz : start.xz;
                var leash = math.max(s.Radius, 6f);
                Enqueue(sp, start, SpawnTier.Cave, e =>
                {
                    var em = simWorld.EntityManager;
                    var state = em.GetComponentData<CreatureState>(e);
                    state.Home = home;
                    state.Leash = leash;
                    em.SetComponentData(e, state);
                });
            }
        }

        // (The bands last logged, by chamber count and first centre.)
        float4 bandsLogged = new float4(-1f);

        /// <summary>Once per island: the bands the chambers would get (no spawning), for the log: bands, members, posts by job.</summary>
        void LogChamberBands(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated)
                return;
            var sp = ChamberFolkIndex();
            if (sp < 0)
                return;
            ref var bats = ref source.BatFolk.Value;
            var key = new float4(bats.Sites.Length, bats.Sites.Length > 0 ? bats.Sites[0].Centre : float3.zero);
            if (math.all(key == bandsLogged))
                return;
            bandsLogged = key;
            int members = 0, pygmies = 0, niched = 0;
            var jobs = new Dictionary<FolkJob, int>();
            var pyg = ChamberFolkIndex(false);
            var shared = pyg >= 0 && pyg != sp;
            for (int i = 0; i < bats.Sites.Length; i++)
            {
                members += PlanChamber(source, i, species[sp]);
                foreach (var p in chamberPosts)
                    jobs[p.Job] = (jobs.TryGetValue(p.Job, out var had) ? had : 0) + 1;
                if (shared)
                {
                    pygmies += PlanPygmies(source, i);
                    niched += chamberPosts.Count;
                }
            }
            if (shared)
                Debug.Log($"[cavefolk] {species[pyg].name} share the chambers: {bats.Sites.Length} bands, {pygmies} in all, {niched} homed at wall niches " +
                          $"({bats.Homes.Length} homes baked)");
            var text = new System.Text.StringBuilder();
            foreach (var kv in jobs)
                text.Append($" {kv.Key} {kv.Value}");
            Debug.Log($"[cavefolk] {species[sp].name}: {bats.Sites.Length} chamber bands, {members} in all; posts{text}");
            if (species[sp].Body.ShunsLight)
                Debug.Log($"[batfolk] chambers with bat folk {bats.Sites.Length}, bat folk total {members}");
            // (Where each chamber's first pod door is, to frame shots by.)
            for (int i = 0; i < bats.Sites.Length; i++)
                if (BatFolkHomes.Count(source, BatFolkHomeKind.BatFolkPod, i) > 0 && BatFolkHomes.Get(source, BatFolkHomeKind.BatFolkPod, i, 0, out var pod))
                    Debug.Log($"[batfolk] pod in chamber {i}, door at {pod.Door}");
            LogFishFolk(source);
        }

        /// <summary>For the log: how many settlements have fish folk residents (the FishingVillage kit's), and how many.</summary>
        void LogFishFolk(in TerrainSource source)
        {
            if (overworld == null || !source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            int villages = 0, lo = 0, hi = 0;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var type = set.Instances[i].Type;
                if (type >= landmarks.Length || landmarks[type] == null)
                    continue;
                var any = false;
                foreach (var ri in TableRows(OverworldSlot.Domestic, landmarks[type]))
                    if (overworld.Rows[ri] is { Species: { name: "FishFolk" } } d)
                    {
                        any = true;
                        lo += math.max(0, d.Count.x);
                        hi += math.max(d.Count.x, d.Count.y);
                    }
                if (any)
                    villages++;
            }
            Debug.Log($"[fishfolk] {villages} fish folk villages, {lo} to {hi} fish folk in all");
        }
    }
}
