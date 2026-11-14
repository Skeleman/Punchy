using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-117 row 1: a town's stable and its horses. SettlementBaking puts a stable on the outermost green of a big enough
    /// town; here it becomes the home of a stablekeeper household (kind "stable", its door the paddock gate) which keeps
    /// two or three horses, and when the town is peopled those horses stand in the paddock, leashed to it. Everything
    /// comes from the plot and the household's key, with no spawn rng drawn, so every other spawn draw stays and every
    /// machine agrees on how many horses each stable keeps.
    /// </summary>
    public partial class FaunaDirector
    {
        const string HorseName = "Horse";

        /// <summary>
        /// Whether a plot holds a stable (not a hitching rail), and where: its paddock gate (xz, outward yaw in w), the
        /// paddock's middle and the room a horse has in it. Matches FloraGenerator.Stable's frame: local +Z is the plot's
        /// facing, the shed across the back, the paddock in front from 0.42 of the depth on, sized from the species'
        /// middle height (a variant may be a little bigger or smaller).
        /// </summary>
        static bool StableAt(List<SettlementBuilding> buildings, in SettlementPlot plot, float scale, out float4 gate, out float2 paddock, out float room)
        {
            gate = default;
            paddock = default;
            room = 0f;
            if (plot.Role != PlotRole.Stable || plot.Building < 0 || plot.Building >= buildings.Count)
                return false;
            var species = buildings[plot.Building].Species;
            if (species == null || species.Form != Punchy.Flora.FloraForm.Stable)
                return false;
            var height = (species.Height.x + math.max(species.Height.x, species.Height.y)) * 0.5f;
            var width = height * species.Spread * scale;
            var depth = width * 1.05f;
            var at = plot.Centre + plot.Facing * (depth * 0.5f + 0.6f);
            gate = new float4(at.x, 0f, at.y, math.atan2(plot.Facing.x, plot.Facing.y));
            paddock = plot.Centre + plot.Facing * (depth * 0.21f);
            room = math.min(width * 0.5f, depth * 0.29f) - 0.9f;
            return true;
        }

        /// <summary>The stablekeeper household at a stable's gate: a grown-up or two (and their children), with two or
        /// three horses from its key.</summary>
        static void AddStable(List<Household> into, int landmark, float4 gate)
        {
            AddHouse(into, landmark, gate, "stable", 1, 2, 2);
            var house = into[into.Count - 1];
            house.Horses = 2 + (int)(BiomeSampling.Scramble(house.Key ^ 0x484F5253u) % 2u);
        }

        /// <summary>
        /// A town being peopled: each of its stables' horses in the paddock, leashed to its middle. Positions and facings
        /// come from the household's key, so no spawn rng is drawn.
        /// </summary>
        void SpawnStableHorses(in TerrainSource source, int landmark)
        {
            var horse = SpeciesIndex(HorseName);
            if (horse < 0 || !source.Settlements.IsCreated)
                return;
            ref var set = ref source.Settlements.Value;
            List<SettlementBuilding> buildings = null;
            for (int t = 0; t < set.Settlements.Length; t++)
            {
                var town = set.Settlements[t];
                if (town.Landmark != landmark)
                    continue;
                buildings ??= SettlementBaking.Buildings(landmarks);
                var scale = set.Layouts[town.Layout].BuildingScale;
                for (int p = town.FirstPlot; p < town.FirstPlot + town.PlotCount; p++)
                {
                    if (!StableAt(buildings, set.Plots[p], scale, out var gate, out var paddock, out var room) ||
                        !Households.ByLandmark.TryGetValue(landmark, out var houses))
                        continue;
                    Household keeper = null;
                    foreach (var h in houses)
                        if (Households.All[h].Kind == "stable" && math.all(Households.All[h].Door.xz == gate.xz))
                            keeper = Households.All[h];
                    if (keeper == null)
                        continue;
                    var leash = math.max(1f, room);
                    var spawned = 0;
                    for (int k = 0; k < keeper.Horses && settledCount < MaxSettled; k++)
                    {
                        var bits = BiomeSampling.Scramble(keeper.Key ^ (uint)(k + 1) * 0x85EBCA6Bu);
                        var angle = (bits & 0xFFFF) / 65535f * 2f * math.PI;
                        var reach = (bits >> 16) / 65535f * leash * 0.6f;
                        math.sincos(angle, out var sin, out var cos);
                        var at = paddock + new float2(cos, sin) * reach;
                        var yaw = ((bits >> 8) & 0xFF) / 255f * 2f * math.PI;
                        var home = paddock;
                        Enqueue(horse, new float3(at.x, source.SampleHeight(at), at.y), SpawnTier.Settled, e =>
                        {
                            var em = simWorld.EntityManager;
                            var state = em.GetComponentData<CreatureState>(e);
                            state.Home = home;
                            state.Leash = leash;
                            em.SetComponentData(e, state);
                        }, yaw);
                        spawned++;
                    }
                    Debug.Log($"[stable] landmark {landmark} at ({paddock.x:F0}, {paddock.y:F0}): {spawned}/{keeper.Horses} horses, " +
                                  $"keeper household of {keeper.Members.Length}, leash {leash:F1} m");
                }
            }
        }
    }
}
