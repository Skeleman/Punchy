using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Where plants find room round a scarp (W-155 row 2), read by flora placement as one factor on land plants:
    /// behind the rim a bare strip of rock and thin soil (ragged, about a metre to two and a half wide) with plants
    /// set back past it, on the face only ledges hold plants (half the chance, and the slope test keeps them off the
    /// steep rock), and past the foot the scree fans thin them out for a few metres. 1 everywhere away from scarps,
    /// so an island without scarps places exactly as before. Burst-safe.
    /// </summary>
    public static class ScarpLife
    {
        /// <summary>How far past the foot the scree fans thin plants out (m).</summary>
        public const float FanReach = 6f;

        /// <summary>Where along the rim scree fans spill (above 0) or the toe stays clean (below): shared by the
        /// face's paint, the debris and the plants, so all three agree where the rubble lies.</summary>
        public static float Fan(ref Scarp sc, float s)
        {
            float seed = sc.Seed % 977u * 1.37f;
            return noise.snoise(new float2(s / 9f, seed + 97f));
        }

        /// <summary>The bare strip behind the rim at a place along it (m): ragged, never under 0.8 m.</summary>
        public static float Bare(ref Scarp sc, float s)
        {
            float seed = sc.Seed % 977u * 1.37f;
            float n = 0.5f + 0.5f * noise.snoise(new float2(s / 7f, seed + 211f)) + 0.15f * noise.snoise(new float2(s / 1.3f, seed + 223f));
            return math.lerp(0.8f, 2.5f, math.saturate(n));
        }

        /// <summary>The room plants have at a point, 0 none to 1 as anywhere else.</summary>
        public static float PlantRoom(in TerrainSource source, float2 xz)
        {
            if (!source.Scarps.IsCreated)
                return 1f;
            ref var set = ref source.Scarps.Value;
            if (!Inside(set.Bounds, xz))
                return 1f;
            float room = 1f;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                ref var sc = ref set.Scarps[i];
                if (Inside(sc.Bounds, xz) && ScarpSet.Locate(ref sc, xz, out var at))
                    room = math.min(room, Room(ref sc, at));
            }
            return room;
        }

        static float Room(ref Scarp sc, in ScarpAt at)
        {
            // Nothing changes towards the very ends of the rim, where the strip narrows to nothing.
            float ends = math.smoothstep(0f, 4f, at.S) * math.smoothstep(0f, 4f, sc.Length - at.S) * math.saturate(at.W / 1.5f);
            float room;
            if (at.D <= 0f)
            {
                // Behind the rim: bare for the strip's width, then plants come back over a metre and a half.
                float bare = Bare(ref sc, at.S);
                room = math.smoothstep(bare, bare + 1.5f, -at.D);
            }
            else if (at.D < at.W)
                room = 0.5f;
            else
            {
                // Past the foot: thinned where a fan spills, thinning less with distance from the wall.
                float fan = math.saturate(2f * Fan(ref sc, at.S) + 0.4f) * (sc.Kind == ScarpKind.RiverCut ? 0.5f : 1f);
                room = 1f - 0.75f * fan * (1f - math.smoothstep(0f, FanReach, at.D - at.W));
            }
            return math.lerp(1f, room, ends);
        }

        static bool Inside(float4 bounds, float2 xz) => math.all(xz >= bounds.xy) && math.all(xz <= bounds.zw);
    }
}
