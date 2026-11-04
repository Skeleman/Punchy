using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The folk of the bat folk chambers (#10, #35): a band of the chamber folk (the blind cave pygmies) in each chamber
    /// a player comes near, at the jobs the chamber's props hand out (a roost keeper under each hut, a tanner at each
    /// drying rack, a forager at each basket, a fisher at its white pool). Band size and who takes which post come from a
    /// hash of the chamber's key, never the director's rng, so the other spawns keep their draws and the same chamber
    /// has the same band on every machine.
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

        /// <summary>The first chamber folk species in the table (the pygmies), or -1.</summary>
        int ChamberFolkIndex()
        {
            if (domestics == null || domestics.CaveFolk == null)
                return -1;
            foreach (var c in domestics.CaveFolk)
                if (c != null && c.Body.ChamberFolk)
                    return SpeciesIndex(c);
            return -1;
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
            // The props by job, in their baked order.
            var kinds = new[] { BatFolkPropKind.Hut, BatFolkPropKind.Rack, BatFolkPropKind.Basket };
            var next = new int[3];
            var any = true;
            while (chamberPosts.Count < band && any)
            {
                any = false;
                for (int k = 0; k < 3 && chamberPosts.Count < band; k++)
                {
                    for (; next[k] < bats.Props.Length; next[k]++)
                    {
                        ref var p = ref bats.Props[next[k]];
                        if (p.Site != site || p.Kind != kinds[k])
                            continue;
                        // A hut hangs from the roof: its keeper stands on the floor under it. A rack or basket's worker
                        // stands a little off it, on the side toward the chamber's middle.
                        var inward = math.normalizesafe(s.Centre.xz - p.Position.xz);
                        var at = p.Kind == BatFolkPropKind.Hut ? p.Position : p.Position + new float3(inward.x * 0.7f, 0.4f, inward.y * 0.7f);
                        next[k]++;
                        var stand = float3.zero;
                        var found = false;
                        // (Under a hut: a few drops below its anchor, clear of the pod and short of the floor.)
                        for (int d = 0; d < 4 && !found; d++)
                            found = ChamberFloor(source, at - new float3(0f, p.Kind == BatFolkPropKind.Hut ? 0.6f + d * 1.5f : d * 0.3f, 0f), out stand);
                        if (!found)
                            continue;
                        chamberPosts.Add(new ChamberPost
                        {
                            Stand = stand,
                            Face = p.Kind == BatFolkPropKind.Hut ? s.Centre.xz : p.Position.xz,
                            Job = FolkJobs.BatJob(p.Kind)
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
            int members = 0;
            var jobs = new Dictionary<FolkJob, int>();
            for (int i = 0; i < bats.Sites.Length; i++)
            {
                members += PlanChamber(source, i, species[sp]);
                foreach (var p in chamberPosts)
                    jobs[p.Job] = (jobs.TryGetValue(p.Job, out var had) ? had : 0) + 1;
            }
            var text = new System.Text.StringBuilder();
            foreach (var kv in jobs)
                text.Append($" {kv.Key} {kv.Value}");
            Debug.Log($"[cavefolk] {species[sp].name}: {bats.Sites.Length} chamber bands, {members} in all; posts{text}");
            LogFishFolk(source);
        }

        /// <summary>For the log: how many settlements have fish folk residents (the FishingVillage kit's), and how many.</summary>
        void LogFishFolk(in TerrainSource source)
        {
            if (domestics == null || !source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            int villages = 0, lo = 0, hi = 0;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var type = set.Instances[i].Type;
                if (type >= landmarks.Length || landmarks[type] == null)
                    continue;
                var any = false;
                foreach (var d in domestics.Entries)
                    if (d.Settlement != null && d.Species != null && d.Species.name == "FishFolk" && d.Settlement.name == landmarks[type].name)
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
