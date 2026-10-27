using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A point on a cave's centre line and how wide the passage is there.</summary>
    public struct CaveNode
    {
        public float3 Position;
        public float Radius;
    }

    public struct CaveData
    {
        public int FirstNode, NodeCount;
        /// <summary>World-space box around all of the cave's air (with a margin).</summary>
        public float3 Min, Max;
        /// <summary>The middle of the entrance, where daylight comes in.</summary>
        public float3 Mouth;
        public float Seed;
    }

    /// <summary>Every cave: chains of nodes whose swept, merged spheres are the cave's air.</summary>
    public struct CaveSetBlob
    {
        public BlobArray<CaveData> Caves;
        public BlobArray<CaveNode> Nodes;
    }

    /// <summary>
    /// The shape of caves as a signed distance: negative inside a cave's air, positive in rock. A cave is a chain of
    /// capsules, blended smoothly where they meet, with a flatter floor than ceiling (easier to walk on) and noise
    /// on the walls. Rock is everywhere that is below the terrain surface and outside every cave.
    /// </summary>
    public static class CaveSampling
    {
        const float Blend = 1.5f;

        public static float Distance(ref CaveSetBlob set, int cave, float3 p)
        {
            ref var c = ref set.Caves[cave];
            var d = 1e9f;
            for (int i = 0; i < c.NodeCount - 1; i++)
            {
                ref var a = ref set.Nodes[c.FirstNode + i];
                ref var b = ref set.Nodes[c.FirstNode + i + 1];
                var ab = b.Position - a.Position;
                var t = math.saturate(math.dot(p - a.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
                var q = p - (a.Position + ab * t);
                if (q.y < 0f)
                    q.y *= 1.8f;   // flatter floor
                d = SmoothMin(d, math.length(q) - math.lerp(a.Radius, b.Radius, t), Blend);
            }
            var s = c.Seed;
            return d + noise.snoise(p * 0.22f + s) * 0.55f + noise.snoise(p * 0.6f - s) * 0.2f;
        }

        /// <summary>Distance to the nearest cave's air at a point (a large number away from every cave).</summary>
        public static float Distance(ref CaveSetBlob set, float3 p, out int cave)
        {
            cave = -1;
            var best = 1e9f;
            for (int i = 0; i < set.Caves.Length; i++)
            {
                ref var c = ref set.Caves[i];
                if (math.any(p < c.Min) || math.any(p > c.Max))
                    continue;
                var d = Distance(ref set, i, p);
                if (d < best)
                {
                    best = d;
                    cave = i;
                }
            }
            return best;
        }

        /// <summary>Whether a point's column passes through any cave's box (a cheap first test).</summary>
        public static bool NearColumn(ref CaveSetBlob set, float2 xz)
        {
            for (int i = 0; i < set.Caves.Length; i++)
            {
                ref var c = ref set.Caves[i];
                if (math.all(xz >= c.Min.xz) && math.all(xz <= c.Max.xz))
                    return true;
            }
            return false;
        }

        static float SmoothMin(float a, float b, float k)
        {
            var h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
