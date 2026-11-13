using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Spell chemistry (Assets/Game/Scripts/ChemistryBridge/README.md): a window of height edits laid over the ground,
    /// metres added to what <see cref="TerrainSource"/> samples (craters, raised earth). One value a whole metre, at
    /// <see cref="Origin"/> + (x + 0.5, z + 0.5) for x, z in [0, Size), sampled bilinearly, and 0 past the window. Off
    /// (the default) <see cref="Add"/> returns the height it is given, so the ground is exactly as without it.
    /// <para>
    /// A shared static rather than a field on TerrainSource: systems compare TerrainSource to find a new world (and
    /// rebuild everything), and jobs carry copies of it. Whoever writes it (the bridge) owns the buffer: it never
    /// changes a buffer a job may still read, but fills a second one and swaps (see ChemistryBridge.Terrain.cs).
    /// </para>
    /// </summary>
    public struct TerrainEdits
    {
        public bool On;
        public int2 Origin;
        public int2 Size;
        /// <summary>Size.x × Size.y metres, row by row (z), each at its whole metre's centre.</summary>
        public UnsafeList<float> Deltas;
        /// <summary>Bumped at each swap.</summary>
        public int Version;

        /// <summary>The edit at a point, bilinear between whole-metre centres, 0 past the window.</summary>
        public float At(float2 xz)
        {
            var u = xz - Origin - 0.5f;
            var i = (int2)math.floor(u);
            if (math.any(i < -1) || math.any(i >= Size))
                return 0f;
            var f = u - i;
            float a = Value(i.x, i.y), b = Value(i.x + 1, i.y), c = Value(i.x, i.y + 1), d = Value(i.x + 1, i.y + 1);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }

        float Value(int x, int z) => x < 0 || z < 0 || x >= Size.x || z >= Size.y ? 0f : Deltas[z * Size.x + x];
    }

    /// <summary>The height edits in force (<see cref="TerrainEdits"/>), read by TerrainSource's samples.</summary>
    public static class TerrainEditLayer
    {
        sealed class Key { }
        static readonly SharedStatic<TerrainEdits> Shared = SharedStatic<TerrainEdits>.GetOrCreate<Key>();

        /// <summary>The edits in force (main thread only, for whoever writes them).</summary>
        public static ref TerrainEdits Current => ref Shared.Data;

        // A domain reload with play mode's options off keeps the shared memory: start off, never pointing at a freed buffer.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Shared.Data = default;

        /// <summary>The height <paramref name="h"/> at <paramref name="xz"/> with the edits added (Burst-safe).</summary>
        public static float Add(float2 xz, float h)
        {
            ref var e = ref Shared.Data;
            return e.On ? h + e.At(xz) : h;
        }
    }
}
