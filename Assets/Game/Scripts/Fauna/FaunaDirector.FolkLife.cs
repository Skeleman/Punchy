using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-103 row 3: the small life of the turtle and sea folk villages, kept sparse (the details tenet). A turtle
    /// village gets a child or two out on the open snow in its middle, who now and then belly-slide across it (the
    /// brain's FolkLife step); a sea folk village gets a child by the spire, who runs along under the fish school, and
    /// two keepers on the bed: one tending a clam bed, one at a glow stone. The elders' nap in the midday sun is the
    /// brain's alone (they're already at their porches). The children come from the overworld table's FolkChild rows
    /// (Count: children per village), the keepers from the village's household row; each village draws from its own
    /// rng here, so no other spawn's draws move, and nothing is placed.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>The flora species of the sea folk village's clam beds and glow stones.</summary>
        const string SeaClamName = "SeaFolkClamBed", SeaGlowName = "SeaFolkGlowStone";

        // The turtle village being peopled: each igloo's door and its middle with its porch radius (xz, porch).
        readonly List<float4> turtleDoors = new List<float4>();
        readonly List<float3> turtleIgloos = new List<float3>();
        // The sea folk village's clam beds and glow stones, as placed (from SeaVillageSpots).
        readonly List<float2> seaClams = new List<float2>();
        readonly List<float2> seaGlows = new List<float2>();

        /// <summary>
        /// A turtle village's children: homed on open snow 5 to 9 m from its middle (clear of the igloos, not on water),
        /// each by one of the igloos' doors, so they go in at dusk with everyone else.
        /// </summary>
        void SpawnTurtleChildren(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index)
        {
            if (turtleDoors.Count == 0)
                return;
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)(TurtleSalt ^ 0xC41Du))) | 1u);
            var children = 0;
            foreach (var ri in TableRows(OverworldSlot.FolkChild, type).ToArray())
            {
                var row = overworld.Rows[ri];
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count; n++)
                {
                    // (A few tries for a clear spot; none, and this child stays out.)
                    for (int t = 0; t < 6; t++)
                    {
                        var home = inst.Position + r.NextFloat2Direction() * r.NextFloat(5f, 9f);
                        var clear = true;
                        foreach (var ig in turtleIgloos)
                            if (math.distance(ig.xy, home) < ig.z + 1.5f)
                                clear = false;
                        var g = source.SampleGround(home);
                        if (!clear || g.HasWater && g.WaterSurface > g.Height)
                            continue;
                        if (SpawnChild(source, row, home, inst.Position, turtleDoors[r.NextInt(turtleDoors.Count)], math.max(6f, row.Range), ref r))
                            children++;
                        break;
                    }
                }
            }
            Debug.Log($"[turtlefolk] village {index}: {children} children");
        }

        /// <summary>
        /// A sea folk village's small life: a child or two by the spire (homed by the nearest dome's door), a clam tender
        /// at one of the clam beds and a glow keeper at one of the glow stones, working there by day (CreatureTask Tend,
        /// jobs ClamTender and GlowKeeper) and home by the nearest door at night. Needs SeaVillageSpots just before.
        /// </summary>
        void SpawnSeaLife(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, SpawnRow household)
        {
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)(SeaSalt ^ 0x3C1Au))) | 1u);
            var spire = inst.Position;
            var children = 0;
            foreach (var ri in TableRows(OverworldSlot.FolkChild, type).ToArray())
            {
                var row = overworld.Rows[ri];
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count; n++)
                {
                    var home = spire + r.NextFloat2Direction() * r.NextFloat(4f, 7f);
                    if (SpawnChild(source, row, home, spire, NearestDoor(home), math.max(8f, row.Range * 1.4f), ref r))
                        children++;
                }
            }
            var keepers = new System.Text.StringBuilder();
            // The clam tender: beside one of the beds, on its spire side, working at it.
            if (seaClams.Count > 0)
            {
                var clam = seaClams[r.NextInt(seaClams.Count)];
                var stand = clam + math.normalizesafe(spire - clam, new float2(0f, 1f)) * 1.6f;
                if (SpawnSea(source, household, new Post { Job = FolkJob.ClamTender, Task = CreatureTask.Tend, Stand = stand, Face = clam },
                             NearestDoor(stand), 6f, ref r))
                    keepers.Append($" clam tender at {stand.x:F0},{stand.y:F0}");
            }
            // The glow keeper: just outside one of the glow stones round the spire, facing it.
            if (seaGlows.Count > 0)
            {
                var glow = seaGlows[r.NextInt(seaGlows.Count)];
                var stand = glow + math.normalizesafe(glow - spire, new float2(0f, 1f)) * 1.3f;
                if (SpawnSea(source, household, new Post { Job = FolkJob.GlowKeeper, Task = CreatureTask.Tend, Stand = stand, Face = glow },
                             NearestDoor(stand), 6f, ref r))
                    keepers.Append($" glow keeper at {stand.x:F0},{stand.y:F0}");
            }
            Debug.Log($"[seafolk] village {index}: {children} children;{(keepers.Length > 0 ? keepers.ToString() : " no keepers")}");
        }

        /// <summary>One village child (counted with the settled ones): homed in the village's middle, by a door.</summary>
        bool SpawnChild(in TerrainSource source, SpawnRow row, float2 home, float2 centre, float4 door, float leash, ref Unity.Mathematics.Random r)
        {
            if (row.Species == null || settledCount >= MaxSettled)
                return false;
            var index = SpeciesIndex(row.Species);
            if (index < 0)
                return false;
            var p = home + r.NextFloat2Direction() * 0.5f;
            var y = source.SampleHeight(p);
            var yaw = math.atan2(centre.x - p.x, centre.y - p.y);
            Enqueue(index, new float3(p.x, y, p.y), SpawnTier.Settled, e =>
            {
                var em = simWorld.EntityManager;
                var state = em.GetComponentData<CreatureState>(e);
                state.Home = home;
                state.Leash = leash;
                state.OwnDoor = door;
                em.SetComponentData(e, state);
            }, yaw);
            return true;
        }
    }
}
