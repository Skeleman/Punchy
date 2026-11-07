using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-88 row 2 (#37): the sea folk village's lake life. A school of fish hangs over each shell dome (the overworld
    /// table's LakeSchool rows, every row per dome), just clear of the dome tops; they scatter when a player swims over
    /// them and drift back to their dome after. A few big slow fish (LakeBed rows) cruise near the lake bed outside the
    /// dome ring. They have their own cap (MaxLake), so a lake full of fish never crowds out a nearby village's folk, and
    /// they draw from their own rng per village, so no other spawn's draws move.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>Most lake fish alive at once (their own tier: not counted towards MaxSettled).</summary>
        public int MaxLake = 48;
        int lakeCount;

        /// <summary>The flora species whose placed props the schools gather round.</summary>
        const string LakeDomeName = "SeaFolkDome";
        /// <summary>
        /// How high over the bed a school swims: just over the tallest dome (SeaFolkDome is 7 to 9.5 m, sized up to
        /// 1.15, so about 11 m), a spread so a school isn't one flat sheet. Shallower water caps it a little under the
        /// surface (CreatureBrainSystem keeps it there).
        /// </summary>
        static readonly float2 SchoolAbove = new float2(12f, 14f);
        /// <summary>How high over the bed the big fish cruise, and the water they need under the surface there.</summary>
        static readonly float2 BedAbove = new float2(1.2f, 2f);
        const float BedMinDepth = 6f;

        /// <summary>Spawns a sea folk village's schools and bed fish (nothing for any other landmark).</summary>
        void SpawnLakeLife(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, float radius)
        {
            if (overworld == null || type == null || type.name != SeaFolkBaking.VillageName || !floraBlob.IsCreated)
                return;
            var r = new Random(math.hash(new int2(index, 0x5EAF01)) | 1u);
            var schools = TableRows(OverworldSlot.LakeSchool, type).ToArray();
            ref var f = ref floraBlob.Value;
            if (schools.Length > 0 && inst.Type * 3 + 2 < f.PropRanges.Length)
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[inst.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Species < 0 || prop.Species >= floraSpecies.Count || floraSpecies[prop.Species] == null ||
                            floraSpecies[prop.Species].name != LakeDomeName)
                            continue;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var dome = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / math.max(1, prop.Count)), radius);
                            foreach (var ri in schools)
                            {
                                var row = overworld.Rows[ri];
                                var leash = math.max(2f, row.Range);
                                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                                for (int n = 0; n < count; n++)
                                    SpawnLakeFish(source, row, dome, dome + r.NextFloat2Direction() * r.NextFloat(0f, 0.6f) * leash, leash,
                                                  r.NextFloat(SchoolAbove.x, SchoolAbove.y));
                            }
                        }
                    }
                }
            // The big fish: each its own patch of deep water outside the dome ring (from the edge of the village out).
            foreach (var ri in TableRows(OverworldSlot.LakeBed, type).ToArray())
            {
                var row = overworld.Rows[ri];
                var leash = math.max(2f, row.Range);
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count; n++)
                    for (int t = 0; t < 10; t++)
                    {
                        var home = inst.Position + r.NextFloat2Direction() * radius * r.NextFloat(1.15f, 1.5f);
                        var g = source.SampleGround(home);
                        if (!g.HasWater || g.WaterSurface - g.Height < BedMinDepth)
                            continue;
                        SpawnLakeFish(source, row, home, home, leash, r.NextFloat(BedAbove.x, BedAbove.y));
                        break;
                    }
            }
        }

        /// <summary>One lake fish at a spot, homed and leashed, swimming a set height over the bed.</summary>
        void SpawnLakeFish(in TerrainSource source, SpawnRow row, float2 home, float2 at, float leash, float above)
        {
            if (lakeCount >= MaxLake || row.Species == null)
                return;
            var index = SpeciesIndex(row.Species);
            if (index < 0)
                return;
            var g = source.SampleGround(at);
            if (!g.HasWater || g.WaterSurface - g.Height < 1f)
                return;
            var y = math.min(g.Height + above, g.WaterSurface - 0.25f);
            var task = row.Task;
            Enqueue(index, new float3(at.x, y, at.y), SpawnTier.Lake, e =>
            {
                var em = simWorld.EntityManager;
                var state = em.GetComponentData<CreatureState>(e);
                state.Home = home;
                state.Leash = leash;
                state.Task = task;
                state.SwimAbove = above;
                state.Position.y = y;
                em.SetComponentData(e, state);
            });
        }
    }
}
