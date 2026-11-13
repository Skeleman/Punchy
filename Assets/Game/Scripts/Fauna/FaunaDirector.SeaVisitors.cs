using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-103 row 2b: sea folk out on dry land. Zach: "Seafolk NPCs don't like being in the surface, but they can be found
    /// on cities and on the surface complaining about how dry it is or staying near water." A few seeded spots get a sea
    /// folk visitor by day while a player is within 120 m: a town's well or fountain (about one in three) and a river
    /// crossing's bank that has no fisher (about one in four, and at least one). Each keeps to its spot (a short leash
    /// round the water), and out of the water it walks slower and stooped (CreatureBrainSystem, CreatureAnimator) and
    /// grumbles about the dryness (dialogue.json, "state": "dry"; FolkState.OutOfWater). At dusk it walks off (into the
    /// river, or out of town) and the despawn takes it, like a lone fisher. No homes and no placement: the spots are the
    /// bake's own wells and crossings, rolled from fixed hashes of where they are (no draw from the director's rng), so
    /// every run finds the same ones.
    /// </summary>
    public partial class FaunaDirector
    {
        const uint SeaVisitorSalt = 0x5EAF2Bu;
        /// <summary>The share of town wells and fountains, and of fisherless river banks, that get a sea folk visitor.</summary>
        const float SeaVisitorTownShare = 0.35f, SeaVisitorRiverShare = 0.25f;
        /// <summary>How far a visitor wanders from its spot (round the fountain's basin, along the bank).</summary>
        const float SeaVisitorLeash = 7f;

        struct SeaVisitorSite
        {
            public float2 Spot, Face;
            public bool River;
        }

        readonly List<SeaVisitorSite> seaVisitorSites = new List<SeaVisitorSite>();
        readonly Dictionary<int, Entity> seaVisitors = new Dictionary<int, Entity>();
        readonly HashSet<int> seaVisitorsLeft = new HashSet<int>();
        int seaVisitorKey = -1;

        void SpawnSeaVisitors(in TerrainSource source)
        {
            if (simWorld == null || !source.Anchors.IsCreated)
                return;
            RoadCrossings.Refresh(source);
            var key = RoadCrossings.Version * 7919 + source.Anchors.Value.Anchors.Length;
            if (key != seaVisitorKey)
            {
                seaVisitorKey = key;
                seaVisitors.Clear();
                seaVisitorsLeft.Clear();
                SeaVisitorSites(source);
            }
            if (seaVisitorSites.Count == 0)
                return;
            var index = SpeciesIndex(SeaFolkSpecies);
            if (index < 0)
                return;
            var em = simWorld.EntityManager;
            var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
            var day = hour >= 5.5f && hour < 19.5f;
            if (day)
                seaVisitorsLeft.Clear();
            for (int i = 0; i < seaVisitorSites.Count; i++)
            {
                var site = seaVisitorSites[i];
                if (seaVisitors.TryGetValue(i, out var e) && em.Exists(e))
                {
                    // Dusk: off it goes, away from the town or along the bank (a destination, not a wander), and the
                    // despawn takes it once no one is near.
                    if (!day)
                    {
                        var s = em.GetComponentData<CreatureState>(e);
                        s.Mode = CreatureMode.Idle;
                        // (A river visitor walks off into the river; a town one walks out of town.)
                        s.Home = site.River ? site.Spot + site.Face * 10f : site.Spot - site.Face * 60f;
                        s.Leash = 6f;
                        em.SetComponentData(e, s);
                        seaVisitors.Remove(i);
                        seaVisitorsLeft.Add(i);
                    }
                    continue;
                }
                if (!day || seaVisitorsLeft.Contains(i) || NearestPlayer(site.Spot) > 120f || settledCount >= MaxSettled)
                    continue;
                var h = SpawnTable.Scramble(math.hash(new int2((int)math.floor(site.Spot.x * 4f), (int)math.floor(site.Spot.y * 4f))) ^ SeaVisitorSalt);
                var r = new Unity.Mathematics.Random(h | 1u);
                // (Beside the basin or on the bank, a step or two off the anchor, looking at the water.)
                var p = site.Spot + r.NextFloat2Direction() * r.NextFloat(0.3f, 1.4f);
                var yaw = math.atan2(site.Face.x, site.Face.y);
                e = Spawn(index, new float3(p.x, source.SampleHeight(p), p.y), yaw);
                var st = em.GetComponentData<CreatureState>(e);
                st.Home = site.Spot;
                st.Leash = SeaVisitorLeash;
                st.Mode = r.NextFloat() < 0.5f ? CreatureMode.Idle : CreatureMode.Wander;
                st.Timer = r.NextFloat(20f, 60f);
                em.SetComponentData(e, st);
                seaVisitors[i] = e;
                settledCount++;
            }
        }

        const string SeaFolkSpecies = "SeaFolk";

        /// <summary>The visitor spots for this island (from the bake's anchors and crossings), logged once.</summary>
        void SeaVisitorSites(in TerrainSource source)
        {
            seaVisitorSites.Clear();
            int wells = 0, banks = 0;
            ref var set = ref source.Anchors.Value;
            var seen = new HashSet<int>();
            for (int a = 0; a < set.Anchors.Length; a++)
            {
                ref var w = ref set.Anchors[a];
                // (One per town: its first well or fountain.)
                if (w.Kind != AnchorKind.Well || w.InCave || !seen.Add(w.Owner))
                    continue;
                wells++;
                if (SeaVisitorRoll(w.Position.xz, 0x7A11u) < SeaVisitorTownShare)
                    seaVisitorSites.Add(new SeaVisitorSite { Spot = w.Position.xz, Face = math.normalizesafe(w.Facing, new float2(0f, 1f)) });
            }
            var towns = seaVisitorSites.Count;
            var bestBankRoll = 2f;
            var bestBank = default(SeaVisitorSite);
            RoadCrossings.Refresh(source);
            var list = RoadCrossings.List;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.HasSpot || c.Fisher)
                    continue;
                banks++;
                var roll = SeaVisitorRoll(c.Spot, 0x41BEu);
                var site = new SeaVisitorSite { Spot = c.Spot, Face = math.normalizesafe(c.Face, new float2(0f, 1f)), River = true };
                if (roll < SeaVisitorRiverShare)
                    seaVisitorSites.Add(site);
                else if (roll < bestBankRoll)
                {
                    bestBankRoll = roll;
                    bestBank = site;
                }
            }
            // (An island with banks always has at least one river visitor: the bank that rolled lowest.)
            if (seaVisitorSites.Count == towns && banks > 0)
                seaVisitorSites.Add(bestBank);
            var sample = "";
            for (int i = 0; i < seaVisitorSites.Count && i < 6; i++)
                sample += $" {(seaVisitorSites[i].River ? "river" : "town")}@{seaVisitorSites[i].Spot.x:F0},{seaVisitorSites[i].Spot.y:F0}";
            UnityEngine.Debug.Log($"SeaVisitors: {seaVisitorSites.Count} spots (towns {towns} of {wells} wells, rivers {seaVisitorSites.Count - towns} of {banks} banks):{sample}");
        }

        /// <summary>A fixed 0..1 roll from where a spot is (the same every run and on every machine).</summary>
        static float SeaVisitorRoll(float2 at, uint salt)
        {
            var h = SpawnTable.Scramble(math.hash(new int2((int)math.floor(at.x * 2f), (int)math.floor(at.y * 2f))) ^ SeaVisitorSalt ^ salt);
            return (h >> 8) / 16777216f;
        }
    }
}
