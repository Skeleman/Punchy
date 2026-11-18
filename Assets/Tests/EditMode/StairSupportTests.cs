using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// #550: a kit stair can be walked. A capsule standing on any tread finds support there (its middle and both sides of
    /// its footprint), the bottom tread can be stepped onto from the ground, and the support carries on past the top tread
    /// onto the landing. Covers the wood plank stairs (stilt house, marsh walk) and a stone one. Reached by reflection.
    /// </summary>
    public class StairSupportTests
    {
        const int Layers = 3;            // SupportGrids.Layers
        // The support grid samples each cell at its middle, and a house's cells (about 0.35 m) are wider than a tread's
        // run, so a cell reads the tread under its middle: one step up or down from the tread being checked, stone or
        // wood alike. Within a step's rise either way is a surface the capsule stands on or steps onto.
        const float Below = 0.35f;
        const float Above = 0.35f;
        const float Foot = 0.25f;        // half a capsule's footprint across the tread

        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");

        static object Grow(string species, int variant)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"Assets/Game/Flora/{species}.asset");
            Assert.IsNotNull(asset, species);
            return Game.GetType("Punchy.Flora.FloraGenerator").GetMethod("Grow", BindingFlags.Public | BindingFlags.Static)
                       .Invoke(null, new object[] { asset, variant, false });
        }

        static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);

        /// <summary>The support heights under a model-space point, highest first (empty outside the grid).</summary>
        static float[] Support(object grid, Vector3 p)
        {
            var min = Field<Vector2>(grid, "Min");
            var cell = Field<float>(grid, "Cell");
            var res = Field<Vector2Int>(grid, "Res");
            var values = Field<float[]>(grid, "Values");
            int x = Mathf.FloorToInt((p.x - min.x) / cell), z = Mathf.FloorToInt((p.z - min.y) / cell);
            if (x < 0 || z < 0 || x >= res.x || z >= res.y)
                return new float[0];
            return Enumerable.Range(0, Layers).Select(l => values[(z * res.x + x) * Layers + l]).Where(v => !float.IsNaN(v)).ToArray();
        }

        static void AssertStands(object grid, Vector3 p, float top, string what)
        {
            // The ground (model y 0) counts too: a cell past the bottom tread's front edge has nothing of the model in it.
            var heights = Support(grid, p).Append(0f).ToArray();
            Assert.IsTrue(heights.Any(h => h >= top - Below && h <= top + Above),
                $"{what}: no support near {top:F2} at {p} (found {string.Join(", ", heights.Select(h => h.ToString("F2")))})");
        }

        [TestCase("FishStiltHouse", "Wood")]
        [TestCase("MarshWalk", "Wood")]
        public void EveryTreadHoldsACapsuleAndTheStairReachesGroundAndLanding(string species, string material)
        {
            for (int variant = 0; variant < 3; variant++)
            {
                var model = Grow(species, variant);
                var grid = Field<object>(model, "Support");
                Assert.IsNotNull(grid, $"{species} {variant}: no support grid");
                var parts = ((IEnumerable)Field<object>(model, "KitParts")).Cast<object>().ToList();
                var treads = parts.Where(p => Field<object>(p, "Piece").ToString() == "Stair").ToList();
                Assert.GreaterOrEqual(treads.Count, 2, $"{species} {variant}: no stair");
                var lowest = float.MaxValue;
                object highest = null;
                var highestTop = float.MinValue;
                foreach (var t in treads)
                {
                    Assert.AreEqual(material, Field<object>(t, "Material").ToString(), $"{species} {variant}: tread material");
                    var c = Field<Vector3>(t, "Centre");
                    var top = c.y + Field<Vector3>(t, "Size").y * 0.5f;
                    var turn = Field<Quaternion>(t, "Rotation");
                    foreach (var side in new[] { -Foot, 0f, Foot })
                        AssertStands(grid, c + turn * new Vector3(side, 0f, 0f), top, $"{species} {variant} tread at {top:F2}, side {side}");
                    lowest = Mathf.Min(lowest, top);
                    if (top > highestTop) { highestTop = top; highest = t; }
                }
                Assert.LessOrEqual(lowest, 0.35f, $"{species} {variant}: the bottom tread is out of a step's reach from the ground");
                // The landing: past the top tread along the stair, support at the top tread's height.
                var hc = Field<Vector3>(highest, "Centre");
                var landing = hc + Field<Quaternion>(highest, "Rotation") * new Vector3(0f, 0f, 0.6f);
                AssertStands(grid, landing, highestTop, $"{species} {variant} landing");
            }
        }
    }
}
