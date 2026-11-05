using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-70 row 1: settlement buildings' and landmark props' walls, placed once per settlement (and per landmark) near a
    /// player, for the brain's wall checks. FloraPlacement.Blocked places each nearby building again on every call
    /// (FloraPlacement.Place, which for a fishing village's houses measures every dock's shore), and a wild animal near a
    /// player asks it up to four times per solid test, several tests per step (KeepOutOfSolids): 5.4 ms of the brain at
    /// fishing village 3. Here the placements are baked by FootprintBakeJob (on a worker, before the brain) into one
    /// grid per settlement (its plots, by plot index: the settlement set's own plot cells index them) and one list per
    /// landmark, and Blocked below walks the same plots and props as FloraPlacement.Blocked with the stored placement in
    /// place of Place. Same answers by construction (anything not baked yet is placed as before); "-footprintcheck"
    /// counts any disagreement with FloraPlacement.Blocked, "-oldfootprints" turns the cache off for an A/B.
    /// Seed-derived (placement is a pure function of the seed), so every machine bakes the same.
    /// </summary>
    public struct BuildingFootprints
    {
        /// <summary>A landmark prop's placement, with where its spot is and how far its walls reach (scale 1 x its scale).</summary>
        public struct BakedProp
        {
            public float2 Spot;
            public float Reach;
            public FloraSystem.FloraInstance Plant;
        }

        /// <summary>Each walled settlement plot's building, placed (footprint), by plot index.</summary>
        [ReadOnly] public NativeHashMap<int, FloraSystem.FloraInstance> Plots;
        /// <summary>Each baked landmark instance's walled props: (first, count) in Props.</summary>
        [ReadOnly] public NativeHashMap<int, int2> Landmarks;
        [ReadOnly] public NativeList<BakedProp> Props;
        /// <summary>False when the cache is off ("-oldfootprints") or not made yet: Blocked is FloraPlacement.Blocked.</summary>
        public bool On;

        /// <summary>"-oldfootprints": every wall check places its buildings again, as before A-70.</summary>
        public static readonly SharedStatic<int> Off = SharedStatic<int>.GetOrCreate<OffKey>();
        struct OffKey { }
        /// <summary>"-footprintcheck": each check is also asked of FloraPlacement.Blocked and any disagreement counted.</summary>
        public static readonly SharedStatic<int> Check = SharedStatic<int>.GetOrCreate<CheckKey>();
        struct CheckKey { }
        /// <summary>For -perflog: settlements baked, landmarks baked, checks compared, disagreements.</summary>
        public static readonly SharedStatic<int4> Stats = SharedStatic<int4>.GetOrCreate<StatsKey>();
        struct StatsKey { }

        /// <summary>Whether a body of this radius at xz is inside a building's or prop's walls (FloraPlacement.Blocked).</summary>
        public bool Blocked(ref FloraBlob f, in TerrainSource source, float2 xz, float radius)
        {
            if (!On)
                return FloraPlacement.Blocked(ref f, source, xz, radius);
            var blocked = Walls(ref f, source, xz, radius);
            if (Check.Data != 0)
            {
                System.Threading.Interlocked.Increment(ref Stats.Data.z);
                if (blocked != FloraPlacement.Blocked(ref f, source, xz, radius))
                    System.Threading.Interlocked.Increment(ref Stats.Data.w);
            }
            return blocked;
        }

        // (Must walk exactly what FloraPlacement.Blocked walks, in the same order, with the same skips.)
        bool Walls(ref FloraBlob f, in TerrainSource source, float2 xz, float radius)
        {
            if (f.Obstacles.Length == 0)
                return false;
            if (source.Settlements.IsCreated && f.SettlementSpecies.Length > 0)
            {
                ref var towns = ref source.Settlements.Value;
                var townCell = SettlementSampling.Cell(ref towns, xz);
                if (townCell >= 0)
                {
                    var plots = towns.PlotCells[townCell];
                    for (int k = plots.x; k < plots.x + plots.y; k++)
                    {
                        var index = towns.PlotItems[k];
                        ref var plot = ref towns.Plots[index];
                        if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length ||
                            math.any(xz < plot.Min - radius) || math.any(xz > plot.Max + radius))
                            continue;
                        if (!Plots.TryGetValue(index, out var plant) && !PlacePlot(ref f, source, ref towns, plot, out plant))
                            continue;
                        if (FloraPlacement.InsideWalls(ref f, plant, xz, radius))
                            return true;
                    }
                }
            }
            if (source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out var landmark) && landmark.Type < f.LandmarkTypeCount)
            {
                if (Landmarks.TryGetValue(landmark.Instance, out var range))
                {
                    for (int e = range.x; e < range.x + range.y; e++)
                    {
                        var prop = Props[e];
                        var reach = FloraPlacement.OldWalls.Data != 0 ? float.MaxValue : prop.Reach + radius + 0.01f;
                        var away = math.distance(prop.Spot, xz);
                        if (away > 15f + radius || away >= reach)
                            continue;
                        if (FloraPlacement.InsideWalls(ref f, prop.Plant, xz, radius))
                            return true;
                    }
                    return false;
                }
                // (Not baked yet: placed here, as FloraPlacement.Blocked does.)
                ref var set = ref source.Landmarks.Value;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[landmark.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Count == 0 || f.Models[f.Rules[prop.Species].FirstModel].ObstacleCount == 0)
                            continue;
                        var reach = FloraPlacement.OldWalls.Data != 0 ? float.MaxValue
                                  : FloraPlacement.WallReachOf(ref f, prop.Species) * math.max(prop.Scale.x, prop.Scale.y) + radius + 0.01f;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            var away = math.distance(spot, xz);
                            if (away > 15f + radius || away >= reach)
                                continue;
                            var plant = FloraPlacement.Place(ref f, source, (int2)math.floor(spot / f.CellSizes[l]), l, footprint: true);
                            if (FloraPlacement.InsideWalls(ref f, plant, xz, radius))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>A plot's building placed as FloraPlacement.Blocked places it; false when it has no walls.</summary>
        internal static bool PlacePlot(ref FloraBlob f, in TerrainSource source, ref SettlementSetBlob towns, in SettlementPlot plot,
                                       out FloraSystem.FloraInstance plant)
        {
            plant = default;
            if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length)
                return false;
            var building = FloraPlacement.PlotSpecies(ref f, ref towns, plot);
            if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].ObstacleCount == 0)
                return false;
            plant = FloraPlacement.Place(ref f, source, (int2)math.floor(plot.Centre / f.CellSizes[building.y]), building.y, footprint: true);
            return true;
        }
    }

    /// <summary>
    /// A-70 row 1: places the walls of the settlements and landmarks a player has come near, once each, into
    /// BuildingFootprints' grid (CreatureBrainSystem schedules it before the brain when there is anything new).
    /// </summary>
    [BurstCompile]
    struct FootprintBakeJob : IJob
    {
        public TerrainSource Terrain;
        public BlobAssetReference<FloraBlob> Flora;
        [ReadOnly] public NativeArray<int> Settlements;
        [ReadOnly] public NativeArray<int> LandmarkInstances;
        public NativeHashMap<int, FloraSystem.FloraInstance> Plots;
        public NativeHashMap<int, int2> Landmarks;
        public NativeList<BuildingFootprints.BakedProp> Props;

        public void Execute()
        {
            ref var f = ref Flora.Value;
            if (Terrain.Settlements.IsCreated && f.SettlementSpecies.Length > 0)
            {
                ref var towns = ref Terrain.Settlements.Value;
                for (int i = 0; i < Settlements.Length; i++)
                {
                    ref var town = ref towns.Settlements[Settlements[i]];
                    for (int p = town.FirstPlot; p < town.FirstPlot + town.PlotCount && p < towns.Plots.Length; p++)
                        if (BuildingFootprints.PlacePlot(ref f, Terrain, ref towns, towns.Plots[p], out var plant))
                            Plots.TryAdd(p, plant);
                }
            }
            if (!Terrain.Landmarks.IsCreated)
                return;
            ref var set = ref Terrain.Landmarks.Value;
            for (int i = 0; i < LandmarkInstances.Length; i++)
            {
                var instance = LandmarkInstances[i];
                ref var inst = ref set.Instances[instance];
                var first = Props.Length;
                if (inst.Type < f.LandmarkTypeCount)
                {
                    // (Its radius as LandmarkSampling.Hit gives it, which is what Blocked's Find hands on.)
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    for (int l = 0; l < 3; l++)
                    {
                        var placed = f.PropRanges[inst.Type * 3 + l];
                        for (int e = placed.x; e < placed.x + placed.y; e++)
                        {
                            var prop = f.Props[e];
                            if (prop.Count == 0 || f.Models[f.Rules[prop.Species].FirstModel].ObstacleCount == 0)
                                continue;
                            var reach = FloraPlacement.WallReachOf(ref f, prop.Species) * math.max(prop.Scale.x, prop.Scale.y);
                            for (int k = 0; k < prop.Count; k++)
                            {
                                var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), radius);
                                Props.Add(new BuildingFootprints.BakedProp
                                {
                                    Spot = spot,
                                    Reach = reach,
                                    Plant = FloraPlacement.Place(ref f, Terrain, (int2)math.floor(spot / f.CellSizes[l]), l, footprint: true)
                                });
                            }
                        }
                    }
                }
                Landmarks.TryAdd(instance, new int2(first, Props.Length - first));
            }
        }
    }
}
