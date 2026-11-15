using Punchy.Fauna;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    // A-115 (#315): the far night map follows the lamplighters. A town's street light not yet lit (or already put out)
    // is left out of the map, and the map is baked again as the rounds go (at most once a second), so from a hill a town
    // lights up street by street. Lights not on a round are baked exactly as before.
    public partial class SurfaceGlow
    {
        /// <summary>How lit each route lamp under 1 was at the last far scan, by its foot (LampRoutes.Key); a light
        /// not in it is lit in full.</summary>
        NativeHashMap<int2, float> farDark;
        int farLitCount = -1;
        float farLitTime = float.MinValue;

        /// <summary>Whether the rounds have lit or put out lamps since the last far scan (checked once a second).</summary>
        bool LampsChanged() => LampRoutes.LitCount != farLitCount && Time.unscaledTime - farLitTime >= 1f;

        /// <summary>Before a far scan (none running): the route lamps not fully lit now.</summary>
        NativeHashMap<int2, float> FillDark()
        {
            if (!farDark.IsCreated)
                farDark = new NativeHashMap<int2, float>(64, Allocator.Persistent);
            farDark.Clear();
            farLitCount = LampRoutes.LitCount;
            farLitTime = Time.unscaledTime;
            if (!LampRoutes.Blob.IsCreated)
                return farDark;
            ref var stops = ref LampRoutes.Blob.Value.Stops;
            for (int i = 0; i < stops.Length; i++)
            {
                var lit = LampRoutes.Lit(stops[i].Hours, LampRoutes.Hour);
                if (lit < 1f)
                    farDark[LampRoutes.Key(stops[i].Foot)] = lit;
            }
            return farDark;
        }

        void ReleaseLamps()
        {
            if (farDark.IsCreated)
                farDark.Dispose();
            farLitCount = -1;
        }
    }
}
