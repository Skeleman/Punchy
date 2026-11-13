using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>W-144: what a cave climb route looks like (and so its Climbability grade: crag and crack 4, the rest 2).</summary>
    public enum ClimbKind : byte
    {
        /// <summary>Rough stepped rock: shelves and knobs.</summary>
        Crag,
        /// <summary>A crack line with chockstones and edges either side.</summary>
        Crack,
        /// <summary>Tree roots grown down through the rock.</summary>
        Roots,
        /// <summary>Hanging vines (shafts open to the sky, damp drops).</summary>
        Vines,
        /// <summary>An old rope someone pegged at the top.</summary>
        Rope
    }

    /// <summary>W-144: where a climb route comes out at the top.</summary>
    public enum ClimbLeads : byte
    {
        /// <summary>Up a shaft to the surface (a skylight).</summary>
        Surface,
        /// <summary>The passage on above the drop.</summary>
        Passage,
        /// <summary>A dead end above the drop (CaveSockets gives dead ends their chest or bones).</summary>
        DeadEnd,
        /// <summary>A destination chamber (W-115) above the drop.</summary>
        Room,
        /// <summary>The river chamber's upper level, beside the chute.</summary>
        Upstream
    }

    /// <summary>
    /// W-144 row 1 (#267): a climb route up a vertical section of a natural cave, a bake record (rule 22) placed by
    /// CaveBaking.PlaceClimbs. The band of wall from Base (its foot, on the low floor, against the face) up to Top (on
    /// the high floor just past the rim), HalfWidth either side, climbed from the Out side (horizontal, unit). Its
    /// Climbability grade is by its look (ClimbKind; A-101's scale: 4 craggy, 2 assisted). The pieces that show it
    /// (holds, strands, the rope) are its ClimbPoints. Seed-derived, so every player has the same routes; nothing sent.
    /// </summary>
    public struct CaveClimb
    {
        public float3 Base, Top;
        public float2 Out;
        public float HalfWidth;
        public byte Grade;
        public ClimbKind Kind;
        public ClimbLeads Leads;
        /// <summary>Whether it's the only way back out from below (the low side can't walk to the mouth otherwise).</summary>
        public bool Needed;
        /// <summary>The cave's index in CaveSetBlob.Caves, and a stable id (the cave's key and the route's place in it).</summary>
        public int Cave;
        public uint Id;
        /// <summary>Its pieces in CaveSetBlob.ClimbPoints.</summary>
        public int FirstPoint, PointCount;
        /// <summary>A per-route random (0-1) for the look.</summary>
        public float Seed;

        public float Height => Top.y - Base.y;

        /// <summary>A point in the band's frame: across (right of a climber facing the wall), out from the face, up from the foot.</summary>
        public float3 Local(float3 p)
        {
            var o = math.normalizesafe(Out, new float2(0f, 1f));
            var d = p - Base;
            return new float3(d.x * -o.y + d.z * o.x, d.x * o.x + d.z * o.y, d.y);
        }

        /// <summary>Whether a climber at p (its reach out from the wall) holds the band: as A-101's ClimbBand.Holds.</summary>
        public bool Holds(float3 p, float reach)
        {
            var l = Local(p);
            return math.abs(l.x) <= HalfWidth && math.abs(l.y) <= reach + 0.6f && l.z >= -0.5f && l.z <= Height + reach;
        }
    }

    /// <summary>
    /// W-144: one piece of a climb route's look: a hold (a stone shelf or knob, Size its radius), or a point on a root,
    /// vine or rope strand (Size its thickness; Join links it to the point before). Normal is out of the rock.
    /// </summary>
    public struct ClimbPoint
    {
        public float3 Position, Normal;
        public float Size;
        /// <summary>Shading (about 1; a crack line is dark, 0.1-0.2).</summary>
        public float Tint;
        public bool Join;
    }

    /// <summary>
    /// W-144: a shimmy ledge in a cave (A-101 row 1b's lip, ClimbLedge): the lip From to To (a straight run along the
    /// rim of a drop), hung from on its Out side (horizontal, unit), Depth the flat top behind the lip (0 = only a lip to
    /// hang from), graded by its grip (2 rooty or cut, 3 bare rock). A bake record with a stable id.
    /// </summary>
    public struct CaveLedge
    {
        public float3 From, To;
        public float2 Out;
        public float Depth;
        public byte Grade;
        public int Cave;
        public uint Id;
    }

    /// <summary>
    /// W-144: the queries A-101 hooks climbing onto (Punchy.Fauna wraps them as IClimbSections; Terrain can't see
    /// Fauna). Routes and ledges are a few dozen an island, so scanned whole like the mines' ladders.
    /// </summary>
    public static class CaveClimbs
    {
        /// <summary>
        /// The Climbability grade of the cave route holding point within reach (0 = no route here; the easiest if
        /// several overlap), and its up (unit, along the face from foot to rim) and out (from the face) directions.
        /// </summary>
        public static byte GradeAt(ref CaveSetBlob caves, float3 point, float reach, out float3 up, out float2 outward)
        {
            up = new float3(0f, 1f, 0f);
            outward = default;
            byte grade = 0;
            if (!math.all(math.isfinite(point)))
                return 0;
            ref var climbs = ref caves.Climbs;
            for (int i = 0; i < climbs.Length; i++)
            {
                ref var c = ref climbs[i];
                if (c.Grade == 0 || (grade != 0 && c.Grade >= grade) || !c.Holds(point, reach))
                    continue;
                grade = c.Grade;
                var face = new float3(c.Top.x, c.Top.y, c.Top.z) + new float3(c.Out.x, 0f, c.Out.y) * 0.35f - c.Base;
                up = math.normalizesafe(face, new float3(0f, 1f, 0f));
                outward = c.Out;
            }
            return grade;
        }

        /// <summary>The routes whose foot or top is within radius of at (xz), into list while it has room.</summary>
        public static void Near(ref CaveSetBlob caves, float2 at, float radius, ref FixedList512Bytes<CaveClimb> list)
        {
            ref var climbs = ref caves.Climbs;
            var r2 = radius * radius;
            for (int i = 0; i < climbs.Length && list.Length < list.Capacity; i++)
                if (math.distancesq(climbs[i].Base.xz, at) <= r2 || math.distancesq(climbs[i].Top.xz, at) <= r2)
                    list.Add(climbs[i]);
        }

        /// <summary>The ledges with any part within radius of at (xz), into list while it has room.</summary>
        public static void NearLedges(ref CaveSetBlob caves, float2 at, float radius, ref FixedList512Bytes<CaveLedge> list)
        {
            ref var ledges = ref caves.Ledges;
            for (int i = 0; i < ledges.Length && list.Length < list.Capacity; i++)
            {
                var a = ledges[i].From.xz;
                var ab = ledges[i].To.xz - a;
                var t = math.saturate(math.dot(at - a, ab) / math.max(math.lengthsq(ab), 1e-6f));
                if (math.distancesq(a + ab * t, at) <= radius * radius)
                    list.Add(ledges[i]);
            }
        }
    }
}
