#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Flora;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for W-175 #415 (world part): saved blueprints (Model Lab JSON in Flora/Units, a species' UnitJson) are
    /// read at runtime and stand in the world's settlements.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>"Species x n @ x,z" per blueprint species placed, for shots; set by BlueprintPlacements.</summary>
        public static string BlueprintTrail = "";

        /// <summary>
        /// Every species carrying a saved blueprint: is the JSON read (parts > 0, not shadowed by a filled Unit), is it
        /// a prop of some landmark type in this world, and does it stand at least once in such a landmark? Scans each
        /// landmark listing it, out to its radius, through the same per-cell Place the flora system draws. Empty when
        /// all good, else one line per problem; "no blueprints" when no species in this world carries one.
        /// </summary>
        public static string BlueprintPlacements()
        {
            BlueprintTrail = "";
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (!Source(out var s) || director == null || !director.FloraPlacementBlob.IsCreated)
                return "no world";
            if (!s.Landmarks.IsCreated)
                return "no landmarks";
            ref var f = ref director.FloraPlacementBlob.Value;
            ref var set = ref s.Landmarks.Value;
            var list = director.FloraSpeciesList;
            var problems = new List<string>();
            var trail = new List<string>();
            var any = false;
            for (int i = 0; i < list.Count; i++)
            {
                var sp = list[i];
                if (sp == null || sp.UnitJson == null)
                    continue;
                any = true;
                if (sp.JsonUnit == null || sp.JsonUnit.Parts == null || sp.JsonUnit.Parts.Length == 0)
                {
                    problems.Add($"{sp.name}: blueprint {sp.UnitJson.name} read no parts");
                    continue;
                }
                if (sp.Unit != null && sp.Unit.Parts != null && sp.Unit.Parts.Length > 0)
                    problems.Add($"{sp.name}: a filled Unit hides blueprint {sp.UnitJson.name}");
                var layer = (int)sp.Layer;
                var size = f.CellSizes[layer];
                int placed = 0, towns = 0, listed = 0;
                var first = new float2(float.NaN);
                for (int k = 0; k < set.Instances.Length; k++)
                {
                    var inst = set.Instances[k];
                    var range = f.PropRanges[inst.Type * 3 + layer];
                    var lists = false;
                    for (int e = range.x; e < range.x + range.y; e++)
                        lists |= f.Props[e].Species == i;
                    if (!lists)
                        continue;
                    listed++;
                    var reach = set.Types[inst.Type].Radius * math.max(1f, inst.Scale) * 1.1f;
                    var lo = (int2)math.floor((inst.Position - reach) / size);
                    var hi = (int2)math.floor((inst.Position + reach) / size);
                    var here = 0;
                    for (int x = lo.x; x <= hi.x; x++)
                        for (int y = lo.y; y <= hi.y; y++)
                        {
                            var plant = FloraPlacement.Place(ref f, s, new int2(x, y), layer);
                            if (plant.Model < f.Rules[i].FirstModel || plant.Model >= f.Rules[i].FirstModel + f.Rules[i].Variants)
                                continue;
                            if (math.distance(plant.Position.xz, inst.Position) > reach)
                                continue;
                            here++;
                            if (math.isnan(first.x))
                                first = plant.Position.xz;
                        }
                    placed += here;
                    if (here > 0)
                        towns++;
                }
                if (listed == 0)
                    problems.Add($"{sp.name}: no landmark in this world lists it");
                else if (placed == 0)
                    problems.Add($"{sp.name}: listed by {listed} landmarks, placed in none");
                else
                    trail.Add($"{sp.name} x{placed} in {towns}/{listed} @ {first.x:F0},{first.y:F0}");
            }
            BlueprintTrail = string.Join("; ", trail);
            if (!any)
                return "no blueprints";
            return string.Join("\n", problems);
        }
    }
}
#endif
