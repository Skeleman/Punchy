using Unity.Burst;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Bake determinism: the island bake's Burst jobs (rivers, landmarks, roads, the salt tower, icebergs) must run as
    /// Burst code every time. Their managed copies do the same maths with different float results (noise, sincos), and
    /// the river network comes out different (21 sources and 83 falls instead of 23 and 79, landmark hash A30264B9
    /// instead of FA8F24B1). In the editor Burst compiles in the background after each domain reload, so a bake that
    /// started before it finished ran the managed copies. Every bake job is therefore marked
    /// <c>[BurstCompile(CompileSynchronously = true)]</c>. With Burst switched off entirely (the editor's Jobs menu)
    /// nothing can make them agree: <see cref="Check"/> warns, and the island cache is not written from such a bake.
    /// </summary>
    public static class BakeBurst
    {
        /// <summary>True when the bake jobs run as Burst code. Logs one warning per bake when they don't.</summary>
        public static bool Check()
        {
            if (BurstCompiler.IsEnabled) return true;
            Debug.LogWarning("[bake] Burst is off: the island bakes with managed job code and comes out different " +
                             "(other rivers and landmarks). The island cache is not saved from this bake.");
            return false;
        }
    }
}
