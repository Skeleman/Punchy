using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Flora
{
    // What stands on a settlement plot (#24, P-51). Moved out of FloraPlacement.cs (over 2500 lines) when elf towers
    // started keeping their own species in a kit town.
    public static partial class FloraPlacement
    {
        /// <summary>
        /// What stands on a plot: its layout building's species and layer, except that the island's biggest settlement
        /// (largest radius, lowest index on a tie) builds its house, seat, inn, smithy, store and workshop plots from its
        /// layout's Kit Building (#24), each a variant drawn as before. Plots, roles and placement draws are unchanged.
        /// P-51: buildings marked KeepsOwn (towers) stay themselves, so an elf grove still reads as elven when it is the
        /// biggest town.
        /// </summary>
        public static int2 PlotSpecies(ref FloraBlob f, ref SettlementSetBlob towns, in SettlementPlot plot)
        {
            var species = f.SettlementSpecies[plot.Building];
            if (f.LayoutKit.Length == 0 || species.x < 0 || plot.Settlement < 0 || plot.Settlement >= towns.Settlements.Length)
                return species;
            if (plot.Building < f.KeepsOwn.Length && f.KeepsOwn[plot.Building] != 0)
                return species;
            if (plot.Role != PlotRole.House && plot.Role != PlotRole.Seat && plot.Role != PlotRole.Inn &&
                plot.Role != PlotRole.Smithy && plot.Role != PlotRole.Store && plot.Role != PlotRole.Workshop)
                return species;
            ref var town = ref towns.Settlements[plot.Settlement];
            if (town.Layout < 0 || town.Layout >= f.LayoutKit.Length || f.LayoutKit[town.Layout].x < 0)
                return species;
            for (int i = 0; i < towns.Settlements.Length; i++)
            {
                var r = towns.Settlements[i].Radius;
                if (r > town.Radius || (r == town.Radius && i < plot.Settlement))
                    return species;
            }
            return f.LayoutKit[town.Layout];
        }

        /// <summary>Whether a settlement building keeps its own species in a kit town (P-51: towers, Form Tower).</summary>
        public static bool KeepsOwnSpecies(FloraSpecies s) => s != null && s.Form == FloraForm.Tower;

        /// <summary>FloraBlob.KeepsOwn, per building in SettlementBaking.Buildings order.</summary>
        static void BakeKeepsOwn(ref BlobBuilder builder, ref FloraBlob root, List<SettlementBuilding> buildings)
        {
            var keeps = builder.Allocate(ref root.KeepsOwn, buildings.Count);
            for (int i = 0; i < buildings.Count; i++)
                keeps[i] = (byte)(KeepsOwnSpecies(buildings[i].Species) ? 1 : 0);
        }
    }
}
