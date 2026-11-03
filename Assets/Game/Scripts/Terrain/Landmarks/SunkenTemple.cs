using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// One sunken temple's frame (W-32), derived from its landmark instance alone so every machine builds the same
    /// temple. Local u runs along Facing (the entrance ramp is at -u), v along R (Facing turned a quarter left) and y
    /// up from Base, just above the highest ground under the rooms. The mesh frame is (u, y, v).
    /// </summary>
    public struct SunkenTempleLayout
    {
        public float2 Centre, F;
        public float Base;
        public float2 R => new float2(-F.y, F.x);
    }

    /// <summary>
    /// The sunken temple's shape (W-32): a sand mound with the top of a ziggurat, a broken portico and a few column
    /// stumps poking out, a sloping trench down to the door, and rooms on three levels inside (entry hall, pillared
    /// hall, a corridor and stair down to a sand-filled chamber, a second corridor and stair down to the sanctum),
    /// with light shafts through the roof. Everything is analytic boxes and rooms, like SaltTowerShape: the mound is
    /// solid except where a room is air, boxes (columns, altar, statue, fallen drums) are solid, and floors are the
    /// room floors, sand drifts, box tops and the mound top. SunkenTempleSystem draws exactly these, so collision
    /// matches what's drawn.
    /// </summary>
    public static class SunkenTempleShape
    {
        public const float Top = 12.5f, Bottom = -6f, FloorReach = 0.45f, Margin = 0.3f;
        /// <summary>The mound's flat top (u InU0..InU1, |v| up to InV) and how far it falls away in front and elsewhere.</summary>
        public const float InU0 = -19f, InU1 = 28f, InV = 12f, FrontFall = 40f, Fall = 22f;
        /// <summary>The mound's extents in local u and v; nothing reaches further from the centre than Reach.</summary>
        public const float MinU = -59f, MaxU = 50f, MaxV = 34f, Reach = 70f;
        public const float Open = 1e4f;
        public const int Fixed = 15, Stair1 = 16, Stair2 = 14, Rooms = Fixed + Stair1 + Stair2;
        public const int Trench = 0, FirstShaft = 10, Shafts = 5;
        public const float Tread1 = 0.5625f, Tread2 = 10f / 14f;

        /// <summary>A room: rectangle, floor (rising by Slope towards -u from U1 for the ramp), ceiling, and kind
        /// (0 room, 1 open ramp, 2 light shaft whose floor is the ceiling of the room below).</summary>
        public struct Room
        {
            public float U0, U1, V0, V1, Floor, Ceil, Slope;
            public int Kind;
            public bool Contains(float u, float v) => u >= U0 && u <= U1 && v >= V0 && v <= V1;
            public float FloorAt(float u) => Floor + Slope * (U1 - u);
        }

        // u0, u1, v0, v1, floor, ceil, slope, kind
        static readonly float[] RoomTable =
        {
            -27f, -18.6f, -1.6f, 1.6f, 8f, Open, 0.3f, 1f,   // 0 entrance trench (ramp)
            -18.6f, -18f, -1.2f, 1.2f, 8f, 11f, 0f, 0f,      // 1 door
            -18f, -6.6f, -6f, 6f, 8f, 12f, 0f, 0f,           // 2 entry hall
            -6.6f, -6f, -1.5f, 1.5f, 8f, 11.2f, 0f, 0f,      // 3 door
            -6f, 10f, -8f, 8f, 8f, 12f, 0f, 0f,              // 4 pillared hall
            10f, 13f, 4.7f, 6.3f, 8f, 10.8f, 0f, 0f,         // 5 corridor to the first stair
            22f, 26f, 1.6f, 6.3f, 4f, 7.4f, 0f, 0f,          // 6 landing
            14f, 26f, -8f, 1.6f, 4f, 7.6f, 0f, 0f,           // 7 sand chamber
            0f, 14f, -5.6f, -4.4f, 4f, 6.6f, 0f, 0f,         // 8 corridor to the second stair
            -22f, -10f, -9f, 3f, 0.5f, 6f, 0f, 0f,           // 9 sanctum
            -12.4f, -11.6f, -3.4f, -2.6f, 12f, Open, 0f, 2f, // 10 shaft: entry hall
            -1.4f, -0.6f, -0.4f, 0.4f, 12f, Open, 0f, 2f,    // 11 shaft: pillared hall
            5.6f, 6.4f, -0.4f, 0.4f, 12f, Open, 0f, 2f,      // 12 shaft: pillared hall
            -16.4f, -15.6f, -8.4f, -7.6f, 6f, Open, 0f, 2f,  // 13 shaft: sanctum
            19.6f, 20.4f, -7.4f, -6.6f, 7.6f, Open, 0f, 2f,  // 14 shaft: sand chamber
        };

        /// <summary>Room i: the fixed rooms, then the first stair's treads (down along +u), then the second's (down along -u).</summary>
        public static Room GetRoom(int i)
        {
            if (i < Fixed)
            {
                var k = i * 8;
                return new Room
                {
                    U0 = RoomTable[k], U1 = RoomTable[k + 1], V0 = RoomTable[k + 2], V1 = RoomTable[k + 3],
                    Floor = RoomTable[k + 4], Ceil = RoomTable[k + 5], Slope = RoomTable[k + 6], Kind = (int)RoomTable[k + 7]
                };
            }
            i -= Fixed;
            if (i < Stair1)
            {
                var u0 = 13f + i * Tread1;
                return new Room { U0 = u0, U1 = u0 + Tread1, V0 = 4.7f, V1 = 6.3f, Floor = 7.75f - 0.25f * i, Ceil = 11f - 0.25f * i };
            }
            i -= Stair1;
            return new Room { U0 = -(i + 1) * Tread2, U1 = -i * Tread2, V0 = -5.8f, V1 = -4.2f, Floor = 3.75f - 0.25f * i, Ceil = 7f - 0.25f * i };
        }

        // Sand drifts: centre u, v, radius u, v, height, room
        public const int Drifts = 5;
        static readonly float[] DriftTable =
        {
            -22.5f, 1.3f, 4.5f, 1.2f, 0.7f, 0f,
            -25.5f, -1.7f, 2f, 1.3f, 0.6f, 0f,
            -17f, 0f, 2.2f, 2.6f, 0.35f, 2f,
            20f, -3.8f, 5.5f, 4.6f, 1.4f, 7f,
            15.8f, -0.2f, 2.2f, 1.8f, 0.7f, 7f,
        };

        public static void Drift(int k, out float4 ellipse, out float height, out int room)
        {
            var j = k * 6;
            ellipse = new float4(DriftTable[j], DriftTable[j + 1], DriftTable[j + 2], DriftTable[j + 3]);
            height = DriftTable[j + 4];
            room = (int)DriftTable[j + 5];
        }

        /// <summary>Sand drifted onto room i's floor at (u, v).</summary>
        public static float Sand(int room, float u, float v)
        {
            var s = 0f;
            for (int k = 0; k < Drifts; k++)
            {
                Drift(k, out var e, out var h, out var r);
                if (r != room)
                    continue;
                float du = (u - e.x) / e.z, dv = (v - e.y) / e.w;
                s += h * math.smoothstep(0f, 1f, 1f - du * du - dv * dv);
            }
            return s;
        }

        // Solid boxes: u0, u1, v0, v1, y0, y1, drawn as (0 box, 1 upright drum, 2 drum along u, 3 drum along v), outside (1) or in.
        public const int Boxes = 37;
        static readonly float[] BoxTable =
        {
            // Portico: four columns (two broken), the beam over the trench and its stepped pediment, a fallen chunk.
            -21.5f, -20.5f, 2.3f, 3.3f, 7f, 16f, 1f, 1f,
            -21.5f, -20.5f, -3.3f, -2.3f, 7f, 16f, 1f, 1f,
            -21.5f, -20.5f, 5.1f, 6.1f, 7f, 13.5f, 1f, 1f,
            -21.5f, -20.5f, -6.1f, -5.1f, 7f, 15f, 1f, 1f,
            -21.6f, -20.4f, -6.2f, 1.2f, 16f, 16.9f, 0f, 1f,
            -21.4f, -20.6f, -5.2f, 0.6f, 16.9f, 17.6f, 0f, 1f,
            -21.4f, -20.6f, -3.8f, -0.6f, 17.6f, 18.2f, 0f, 1f,
            -24f, -22.5f, 5f, 7.4f, 11.4f, 12.4f, 0f, 1f,
            // The ziggurat's top tiers.
            14f, 24f, -5f, 5f, 11f, 14f, 0f, 1f,
            15.5f, 22.5f, -3.5f, 3.5f, 14f, 15.5f, 0f, 1f,
            17f, 21f, -2f, 2f, 15.5f, 17f, 0f, 1f,
            18f, 20f, -1f, 1f, 17f, 18.6f, 0f, 1f,
            // Column stumps along the sides.
            -0.5f, 0.5f, 12f, 13f, 9f, 13.5f, 1f, 1f,
            11.5f, 12.5f, 12f, 13f, 9f, 14.2f, 1f, 1f,
            -0.5f, 0.5f, -13f, -12f, 9f, 14.2f, 1f, 1f,
            11.5f, 12.5f, -13f, -12f, 9f, 13.5f, 1f, 1f,
            // Pillared hall: two rows of four.
            -3.55f, -2.45f, 3.95f, 5.05f, 8f, 12f, 1f, 0f,
            -0.05f, 1.05f, 3.95f, 5.05f, 8f, 12f, 1f, 0f,
            3.45f, 4.55f, 3.95f, 5.05f, 8f, 12f, 1f, 0f,
            6.95f, 8.05f, 3.95f, 5.05f, 8f, 12f, 1f, 0f,
            -3.55f, -2.45f, -5.05f, -3.95f, 8f, 12f, 1f, 0f,
            -0.05f, 1.05f, -5.05f, -3.95f, 8f, 12f, 1f, 0f,
            3.45f, 4.55f, -5.05f, -3.95f, 8f, 12f, 1f, 0f,
            6.95f, 8.05f, -5.05f, -3.95f, 8f, 12f, 1f, 0f,
            // Entry hall: two fallen columns, a stump and one still standing.
            -15f, -9.5f, 2.65f, 3.75f, 8f, 9.1f, 2f, 0f,
            -9.7f, -8.6f, -5.4f, -1.8f, 8f, 9.1f, 3f, 0f,
            -16.55f, -15.45f, 4.05f, 5.15f, 8f, 9.6f, 1f, 0f,
            -12.55f, -11.45f, -5.15f, -4.05f, 8f, 12f, 1f, 0f,
            // Sanctum: altar, statue on its plinth, four pillars.
            -19f, -17.6f, -4.2f, -1.8f, 0.5f, 1.6f, 0f, 0f,
            -21.8f, -20.2f, -3.8f, -2.2f, 0.5f, 1f, 0f, 0f,
            -21.6f, -20.4f, -3.6f, -2.4f, 1f, 3.6f, 1f, 0f,
            -21.35f, -20.65f, -3.35f, -2.65f, 3.6f, 4.4f, 0f, 0f,
            -13.55f, -12.45f, -7.55f, -6.45f, 0.5f, 6f, 1f, 0f,
            -13.55f, -12.45f, 0.45f, 1.55f, 0.5f, 6f, 1f, 0f,
            -19.55f, -18.45f, -7.55f, -6.45f, 0.5f, 6f, 1f, 0f,
            -19.55f, -18.45f, 0.45f, 1.55f, 0.5f, 6f, 1f, 0f,
            // Sand chamber: a fallen column half in the drift.
            23.45f, 24.55f, -7.5f, -3f, 4f, 5f, 3f, 0f,
        };

        public static void Box(int k, out float4 uv, out float2 y, out int draw, out bool outside)
        {
            var j = k * 8;
            uv = new float4(BoxTable[j], BoxTable[j + 1], BoxTable[j + 2], BoxTable[j + 3]);
            y = new float2(BoxTable[j + 4], BoxTable[j + 5]);
            draw = (int)BoxTable[j + 6];
            outside = BoxTable[j + 7] > 0.5f;
        }

        /// <summary>Wall sconces (drawn, each with a point light): the wall point u, y, v and the way into the room (nu, nv).</summary>
        public const int Sconces = 8;
        public static readonly float[] SconceTable =
        {
            -18f, 10.2f, -4f, 1f, 0f,
            2f, 10.2f, 8f, 0f, -1f,
            2f, 10.2f, -8f, 0f, 1f,
            26f, 6.2f, -4f, -1f, 0f,
            -22f, 3.2f, -7f, 1f, 0f,
            -22f, 3.2f, 1f, 1f, 0f,
            -10f, 3.2f, 0f, -1f, 0f,
            7f, 6f, -5.6f, 0f, 1f,
        };

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

        /// <summary>0 on the mound's flat top, 1 where it has fallen to Bottom.</summary>
        public static float MoundT(float u, float v)
        {
            float df = math.max(InU0 - u, 0f) / FrontFall, db = math.max(u - InU1, 0f) / Fall, ds = math.max(math.abs(v) - InV, 0f) / Fall;
            return math.length(new float2(math.max(df, db), ds));
        }

        public static float MoundTop(float u, float v) => Bottom + (Top - Bottom) * (1f - math.smoothstep(0f, 1f, MoundT(u, v)));

        static bool InExtents(float u, float v) => u >= MinU && u <= MaxU && math.abs(v) <= MaxV;

        /// <summary>Where the mound's top is cut open: the trench and the light shafts.</summary>
        public static bool InCut(float u, float v)
        {
            if (GetRoom(Trench).Contains(u, v))
                return true;
            for (int i = FirstShaft; i < FirstShaft + Shafts; i++)
                if (GetRoom(i).Contains(u, v))
                    return true;
            return false;
        }

        /// <summary>Whether a local point is in a room (above its floor and sand, below its ceiling).</summary>
        public static bool Air(float u, float y, float v)
        {
            for (int i = 0; i < Rooms; i++)
            {
                var r = GetRoom(i);
                if (!r.Contains(u, v))
                    continue;
                var f = r.Kind == 2 ? r.Floor : r.FloorAt(u) + Sand(i, u, v);
                if (y >= f && y < r.Ceil)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a local point is inside a box or the mound (outside every room).</summary>
        public static bool Solid(float u, float y, float v)
        {
            for (int k = 0; k < Boxes; k++)
            {
                Box(k, out var b, out var by, out _, out _);
                if (u >= b.x && u <= b.y && v >= b.z && v <= b.w && y >= by.x && y <= by.y)
                    return true;
            }
            if (!InExtents(u, v) || y < Bottom - 14f || MoundT(u, v) >= 1f || y >= MoundTop(u, v))
                return false;
            return !Air(u, y, v);
        }

        /// <summary>The highest floor, drift, box top or mound top under p that's no more than FloorReach above it.</summary>
        public static bool Floor(in SunkenTempleLayout l, float3 p, out float top)
        {
            top = 0f;
            var q = Local(l, p);
            float u = q.x, v = q.z;
            if (!InExtents(u, v))
                return false;
            var limit = q.y + FloorReach;
            var best = float.MinValue;
            for (int i = 0; i < Rooms; i++)
            {
                var r = GetRoom(i);
                if (r.Kind == 2 || !r.Contains(u, v))
                    continue;
                var f = r.FloorAt(u) + Sand(i, u, v);
                if (f <= limit && f > best)
                    best = f;
            }
            for (int k = 0; k < Boxes; k++)
            {
                Box(k, out var b, out var by, out _, out _);
                if (u >= b.x && u <= b.y && v >= b.z && v <= b.w && by.y <= limit && by.y > best)
                    best = by.y;
            }
            if (MoundT(u, v) < 1f && !InCut(u, v))
            {
                var m = MoundTop(u, v);
                if (m <= limit && m > best)
                    best = m;
            }
            if (best == float.MinValue)
                return false;
            top = best + l.Base;
            return true;
        }

        /// <summary>
        /// Whether a body with its feet at p (radius Margin) would stand in the mound or a box anywhere between stepUp
        /// above its feet and its head: sampled at its centre and four sides, at three heights.
        /// </summary>
        public static bool Wall(in SunkenTempleLayout l, float3 p, float stepUp, float head)
        {
            var q = Local(l, p);
            if (q.x < MinU - 1f || q.x > MaxU + 1f || math.abs(q.z) > MaxV + 1f)
                return false;
            float y0 = q.y + stepUp, y2 = q.y + head, y1 = 0.5f * (y0 + y2);
            for (int k = 0; k < 5; k++)
            {
                var du = k == 1 ? Margin : k == 2 ? -Margin : 0f;
                var dv = k == 3 ? Margin : k == 4 ? -Margin : 0f;
                if (Solid(q.x + du, y0, q.z + dv) || Solid(q.x + du, y1, q.z + dv) || Solid(q.x + du, y2, q.z + dv))
                    return true;
            }
            return false;
        }
    }
}
