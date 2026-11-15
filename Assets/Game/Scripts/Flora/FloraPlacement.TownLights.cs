using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Flora
{
    // A-115 (#315): a settlement's street lights as a list, for the lamplighter's rounds. Additive only: it walks the same
    // Door and Post spots TownFixture does and keeps the ones Place really puts a light on, so it draws nothing and moves
    // nothing (no cell's draws are taken; the flora hashes stay).
    public static partial class FloraPlacement
    {
        /// <summary>A-115: one street light a settlement's lamplighter tends: its foot (xz, exactly the placed
        /// instance's) and where its light comes from (as SurfaceGlow lights it).</summary>
        public struct TownLight
        {
            public float2 Foot;
            public float3 Light;
        }

        /// <summary>
        /// A-115: appends the Door and Post street lights that stand in this settlement (beacon towers and pier lamps
        /// left out: they burn all night). Each candidate spot is checked with Place, so only lights the flora really
        /// places come back, at its height. On the main thread at build time: a few hundred Place calls a town.
        /// </summary>
        public static void TownLights(ref FloraBlob f, in TerrainSource source, int settlement, List<TownLight> into)
        {
            if (!source.Settlements.IsCreated || f.Fixtures.Length == 0)
                return;
            ref var towns = ref source.Settlements.Value;
            if (settlement < 0 || settlement >= towns.Settlements.Length)
                return;
            ref var town = ref towns.Settlements[settlement];
            if (town.Layout < 0 || town.Layout >= f.LayoutFixtures.Length || f.LayoutFixtures[town.Layout].y == 0)
                return;
            var scale = math.clamp(towns.Layouts[town.Layout].BuildingScale, 0.75f, 1.4f);
            for (int k = town.FirstPlot; k < town.FirstPlot + town.PlotCount; k++)
            {
                ref var plot = ref towns.Plots[k];
                if (plot.Role > PlotRole.Stable || plot.Role == PlotRole.Landscaping || plot.Role == PlotRole.Market || plot.Building < 0)
                    continue;
                var right = new float2(plot.Facing.y, -plot.Facing.x);
                AddTownLight(ref f, source, plot.Centre + plot.Facing * (plot.Half.y + 0.45f * scale) + right * (plot.Half.x * 0.55f), into);
            }
            for (int index = town.FirstStreet; index < town.FirstStreet + town.StreetCount; index++)
            {
                var street = towns.Streets[index];
                if (street.Road != 0)
                    continue;
                var ab = street.B.xz - street.A.xz;
                if (math.lengthsq(ab) < 4f)
                    continue;
                var dir = math.normalize(ab);
                var side = (index & 1) == 0 ? new float2(dir.y, -dir.x) : new float2(-dir.y, dir.x);
                AddTownLight(ref f, source, street.A.xz + dir * 1.5f + side * (street.Width * 0.5f + 0.35f * scale), into);
            }
        }

        static void AddTownLight(ref FloraBlob f, in TerrainSource source, float2 spot, List<TownLight> into)
        {
            var size = f.CellSizes[TreeLayer];
            // (A footprint is enough to tell: its Position.xz and Scale are the full placement's; the height is then
            // sampled as FixtureInstance does.)
            var lamp = Place(ref f, source, (int2)math.floor(spot / size), TreeLayer, footprint: true);
            // (Anything else in the cell, a building or the beacon, stands somewhere else: same floats, same spot.)
            if (lamp.Model < 0 || math.any(lamp.Position.xz != spot))
                return;
            var foot = new float3(spot.x, source.SampleHeight(spot), spot.y);
            into.Add(new TownLight
            {
                Foot = spot,
                Light = foot + new float3(0f, StructureKit.FixtureLightHeight * math.max(lamp.Scale, 0.3f), 0f)
            });
        }
    }
}
