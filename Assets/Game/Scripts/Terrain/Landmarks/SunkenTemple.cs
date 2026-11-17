using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// One sunken temple's frame (W-32), derived from its landmark instance alone so every machine builds the same
    /// temple. Local u runs along Facing (the entrance is at -u), v along R (Facing turned a quarter left) and y up
    /// from Base, just above the highest ground under the rooms. The mesh frame is (u, y, v).
    /// </summary>
    public struct SunkenTempleLayout
    {
        public float2 Centre, F;
        public float Base;
        public float2 R => new float2(-F.y, F.x);
    }

    /// <summary>
    /// The sunken temple's shape (W-32, rebuilt from the ruin kit in W-56). The sand swell is now ground: LandmarkSampling
    /// raises the heightmap by MoundTop, so TerrainSource owns it. The rooms are the ruin kit's layout baked into
    /// LandmarkSetBlob (RuinRooms and friends): open rooms (the entrance) cut the ground away like a cave mouth (Cut),
    /// closed rooms are air under it (Air). Solid is the ruin's boxes, plus the ground itself near the rooms (walls
    /// between rooms are just ground that isn't air). Floors are room floors, sand drifts and box tops.
    /// SunkenTempleSystem draws exactly these, so collision matches what's drawn. Burst-safe: it only reads the blob.
    /// </summary>
    public static class SunkenTempleShape
    {
        public const float Top = 12.5f, Bottom = -6f, FloorReach = 0.45f, Margin = 0.3f;
        /// <summary>The swell's flat top (u InU0..InU1, |v| up to InV) and how far it falls away in front and elsewhere.</summary>
        public const float InU0 = -19f, InU1 = 28f, InV = 12f, FrontFall = 80f, Fall = 74f;
        /// <summary>The swell's extents in local u and v; nothing reaches further from the centre than Reach. W-95 (#80):
        /// the falls are long enough that no flank tops ~20 degrees, under the slope shading's rock and scree, so the
        /// swell stays sand; Reach runs past the type's Radius (the landmark grid lists the temple out to it).</summary>
        public const float MinU = -108f, MaxU = 110f, MaxV = 94f, Reach = 120f;
        /// <summary>Where the ruin's rooms and boxes can be (the ruin kit keeps its branches inside).</summary>
        public const float RuinU0 = -29f, RuinU1 = 29f, RuinV = 15f;
        public const float Open = RuinKit.Open;

        public static SunkenTempleLayout Layout(in LandmarkInstance temple) =>
            new SunkenTempleLayout { Centre = temple.Position, F = temple.Facing, Base = temple.BaseHeight };

        /// <summary>A world point in the temple's frame: (u, y above Base, v).</summary>
        public static float3 Local(in SunkenTempleLayout l, float3 p)
        {
            var d = p.xz - l.Centre;
            return new float3(math.dot(d, l.F), p.y - l.Base, math.dot(d, l.R));
        }

        public static float3 World(in SunkenTempleLayout l, float3 q)
        {
            var xz = l.Centre + q.x * l.F + q.z * l.R;
            return new float3(xz.x, q.y + l.Base, xz.y);
        }

        /// <summary>0 on the swell's flat top, 1 where it has fallen to the ground around it.</summary>
        public static float MoundT(float u, float v)
        {
            float df = math.max(InU0 - u, 0f) / FrontFall, db = math.max(u - InU1, 0f) / Fall, ds = math.max(math.abs(v) - InV, 0f) / Fall;
            return math.length(new float2(math.max(df, db), ds));
        }

        public static float MoundTop(float u, float v) => Bottom + (Top - Bottom) * (1f - math.smoothstep(0f, 1f, MoundT(u, v)));

        static bool InRuin(float u, float v) => u >= RuinU0 && u <= RuinU1 && math.abs(v) <= RuinV;

        /// <summary>Sand drifted onto room i's floor at (u, v).</summary>
        public static float Sand(ref LandmarkSetBlob set, int room, float u, float v)
        {
            var s = 0f;
            for (int k = 0; k < set.RuinDrifts.Length; k++)
            {
                ref var d = ref set.RuinDrifts[k];
                if (d.Room != room)
                    continue;
                float du = (u - d.Ellipse.x) / d.Ellipse.z, dv = (v - d.Ellipse.y) / d.Ellipse.w;
                s += d.Height * math.smoothstep(0f, 1f, 1f - du * du - dv * dv);
            }
            return s;
        }

        /// <summary>Whether a local point (u, y, v) is in a room (above its floor and sand, below its ceiling).</summary>
        public static bool Air(ref LandmarkSetBlob set, float3 q)
        {
            if (!InRuin(q.x, q.z))
                return false;
            for (int i = 0; i < set.RuinRooms.Length; i++)
            {
                var r = set.RuinRooms[i];
                if (!r.Contains(q.x, q.z))
                    continue;
                if (q.y >= r.FloorAt(q.x) + Sand(ref set, i, q.x, q.z) && q.y < r.Ceil)
                    return true;
            }
            return false;
        }

        static bool InBox(ref LandmarkSetBlob set, float u, float y, float v)
        {
            for (int k = 0; k < set.RuinBoxes.Length; k++)
            {
                ref var b = ref set.RuinBoxes[k];
                if (u >= b.UV.x && u <= b.UV.y && v >= b.UV.z && v <= b.UV.w && y >= b.Y.x && y <= b.Y.y)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a local point is inside a box, or under the ground (surface, local) near the ruin and outside every room.</summary>
        public static bool Solid(ref LandmarkSetBlob set, float u, float y, float v, float surface) =>
            InBox(ref set, u, y, v) || InRuin(u, v) && y < surface && !Air(ref set, new float3(u, y, v));

        /// <summary>The highest room floor, drift or box top under p that's no more than FloorReach above it.</summary>
        public static bool Floor(ref LandmarkSetBlob set, in SunkenTempleLayout l, float3 p, out float top)
        {
            top = 0f;
            var q = Local(l, p);
            float u = q.x, v = q.z;
            if (!InRuin(u, v))
                return false;
            var limit = q.y + FloorReach;
            var best = float.MinValue;
            for (int i = 0; i < set.RuinRooms.Length; i++)
            {
                var r = set.RuinRooms[i];
                if (!r.Contains(u, v))
                    continue;
                var f = r.FloorAt(u) + Sand(ref set, i, u, v);
                if (f <= limit && f > best)
                    best = f;
            }
            for (int k = 0; k < set.RuinBoxes.Length; k++)
            {
                ref var b = ref set.RuinBoxes[k];
                if (u >= b.UV.x && u <= b.UV.y && v >= b.UV.z && v <= b.UV.w && b.Y.y <= limit && b.Y.y > best)
                    best = b.Y.y;
            }
            if (best == float.MinValue)
                return false;
            top = best + l.Base;
            return true;
        }

        /// <summary>
        /// Signed distance (m, in the ground plane) to the open rooms (the entrance cut), negative inside; 100 if none.
        /// TerrainSource cuts the ground surface away where it's negative, like a cave mouth.
        /// </summary>
        public static float Cut(ref LandmarkSetBlob set, in SunkenTempleLayout l, float2 xz)
        {
            var d = xz - l.Centre;
            float u = math.dot(d, l.F), v = math.dot(d, l.R);
            var best = 100f;
            for (int i = 0; i < set.RuinRooms.Length; i++)
            {
                var r = set.RuinRooms[i];
                if (r.Kind != 1)
                    continue;
                float du = math.max(r.U0 - u, u - r.U1), dv = math.max(r.V0 - v, v - r.V1);
                var s = du > 0f || dv > 0f ? math.length(math.max(new float2(du, dv), 0f)) : math.max(du, dv);
                best = math.min(best, s);
            }
            return best;
        }

        /// <summary>How far outside an open room's face the entrance walls read the ground for their top (m).</summary>
        public const float WallTopOut = 0.3f;
        /// <summary>How far above that ground the walls stand (m).</summary>
        public const float WallTopLift = 0.05f;

        /// <summary>
        /// W-174 r6 (#405): the top of the entrance walls (world height) on an open room's face e (0 -u, 1 +u, 2 -v,
        /// 3 +v), whose line is u = c (e 0-1) or v = c (e 2-3), at s along it (v or u): the ground WallTopOut outside the
        /// face plus WallTopLift, read every half metre of s and straight between. It depends on the face's line only,
        /// not the room, so SunkenTempleSystem's walls and the terrain chunks' collars (BuildChunkMeshJob) meet on it.
        /// </summary>
        public static float WallTop(in TerrainSource source, in SunkenTempleLayout l, int e, float c, float s)
        {
            var k = math.floor(s * 2f) * 0.5f;
            var t = (s - k) * 2f;
            var a = WallKnot(source, l, e, c, k);
            return t <= 0f ? a : math.lerp(a, WallKnot(source, l, e, c, k + 0.5f), t);
        }

        static float WallKnot(in TerrainSource source, in SunkenTempleLayout l, int e, float c, float s)
        {
            var o = c + ((e & 1) == 0 ? -WallTopOut : WallTopOut);
            var q = e < 2 ? new float3(o, 0f, s) : new float3(s, 0f, o);
            return source.SampleHeight(World(l, q).xz) + WallTopLift;
        }

        /// <summary>
        /// The open-room face nearest a point on the entrance cut's outline (local u, v): its face e (as WallTop) and
        /// line c, and the point's s along it. False when there are no open rooms.
        /// </summary>
        public static bool Face(ref LandmarkSetBlob set, float u, float v, out int e, out float c, out float s)
        {
            e = 0;
            c = s = 0f;
            var best = float.MaxValue;
            for (int i = 0; i < set.RuinRooms.Length; i++)
            {
                var r = set.RuinRooms[i];
                if (r.Kind != 1)
                    continue;
                var d = new float4(r.U0 - u, u - r.U1, r.V0 - v, v - r.V1);
                // |signed distance| to this room's outline (as Cut): outside, the length past it; inside, to the nearest face.
                var sd = math.length(new float2(math.max(math.max(d.x, d.y), 0f), math.max(math.max(d.z, d.w), 0f)));
                var score = sd > 0f ? sd : -math.cmax(d);
                if (score >= best)
                    continue;
                best = score;
                int f = 0;
                for (int k = 1; k < 4; k++)
                    if (d[k] > d[f])
                        f = k;
                e = f;
                c = f == 0 ? r.U0 : f == 1 ? r.U1 : f == 2 ? r.V0 : r.V1;
                s = f < 2 ? v : u;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Whether a body with its feet at p (radius Margin) would stand in a box, or in the ground beside a room,
        /// anywhere between stepUp above its feet and its head: sampled at its centre and four sides, at three heights.
        /// surface is the ground height at p in the temple's frame.
        /// </summary>
        public static bool Wall(ref LandmarkSetBlob set, in SunkenTempleLayout l, float3 p, float stepUp, float head, float surface)
        {
            var q = Local(l, p);
            if (!InRuin(q.x, q.z) || set.RuinRooms.Length == 0)
                return false;
            float y0 = q.y + stepUp, y2 = q.y + head, y1 = 0.5f * (y0 + y2);
            for (int k = 0; k < 5; k++)
            {
                var du = k == 1 ? Margin : k == 2 ? -Margin : 0f;
                var dv = k == 3 ? Margin : k == 4 ? -Margin : 0f;
                float u = q.x + du, v = q.z + dv;
                if (Solid(ref set, u, y0, v, surface) || Solid(ref set, u, y1, v, surface) || Solid(ref set, u, y2, v, surface))
                    return true;
            }
            return false;
        }
    }
}
