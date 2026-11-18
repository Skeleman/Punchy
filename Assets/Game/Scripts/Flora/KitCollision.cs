using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Flora
{
    /// <summary>
    /// One solid box of a structure-kit building or ruin (a StructureKit part: wall, floor, stair, roof, column...),
    /// W-67 (#79). A FloraModel keeps its boxes in model space; FloraSystem places the ones near each player in world
    /// space on the flora obstacle entity, refreshed with the support tops. Seed-derived, so every machine builds the same.
    /// </summary>
    public struct KitBox : IBufferElementData
    {
        public float3 Centre;
        /// <summary>Half its size along its own axes.</summary>
        public float3 Half;
        public quaternion Rotation;
        /// <summary>Its corners' distance from the centre: a quick reject before the exact test.</summary>
        public float Reach;
    }

    /// <summary>
    /// Collision against kit boxes, W-67 (#79): the art's own rotated boxes, so what you see is what you bump into.
    /// A vertical line through a box enters at its top and leaves at its bottom (a tilted box, such as a stair or a roof,
    /// gives a sloped top): tops are floors, the parts between the feet and the head are walls, bottoms are ceilings.
    /// Burst-safe statics, used by the player's movement and by the -walk check.
    /// </summary>
    public static class KitCollision
    {
        /// <summary>A model-space box turned, scaled and moved as the model's instance is drawn (FloraSupport's yaw convention).</summary>
        public static KitBox Placed(in KitBox local, float3 position, float yaw, float scale)
        {
            float c = math.cos(yaw), s = math.sin(yaw);
            var p = local.Centre;
            return new KitBox
            {
                Centre = position + new float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c) * scale,
                Half = local.Half * scale,
                Rotation = math.mul(quaternion.RotateY(-yaw), local.Rotation),
                Reach = local.Reach * scale
            };
        }

        /// <summary>Where the vertical line at xz passes through the box: its bottom and top heights there, if it does.</summary>
        public static bool Span(in KitBox b, float2 xz, out float bottom, out float top)
        {
            bottom = top = 0f;
            var d = xz - b.Centre.xz;
            if (math.lengthsq(d) > b.Reach * b.Reach)
                return false;
            var inv = math.conjugate(b.Rotation);
            var o = math.mul(inv, new float3(d.x, 0f, d.y));
            var dir = math.mul(inv, new float3(0f, 1f, 0f));
            float t0 = -1e9f, t1 = 1e9f;
            for (int k = 0; k < 3; k++)
            {
                if (math.abs(dir[k]) < 1e-6f)
                {
                    if (math.abs(o[k]) > b.Half[k])
                        return false;
                    continue;
                }
                var a = (-b.Half[k] - o[k]) / dir[k];
                var e = (b.Half[k] - o[k]) / dir[k];
                t0 = math.max(t0, math.min(a, e));
                t1 = math.min(t1, math.max(a, e));
            }
            if (t0 > t1)
                return false;
            bottom = b.Centre.y + t0;
            top = b.Centre.y + t1;
            return true;
        }

        /// <summary>
        /// The highest box top within a step up of the feet, at the feet and four points round them (radius out).
        /// </summary>
        public static bool Floor(in DynamicBuffer<KitBox> boxes, float3 feet, float radius, float stepUp, out float height)
        {
            height = float.MinValue;
            var limit = feet.y + stepUp;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                if (math.distancesq(b.Centre.xz, feet.xz) > math.square(b.Reach + radius))
                    continue;
                for (int k = 0; k < 5; k++)
                {
                    var at = feet.xz + (k == 0 ? float2.zero : Round(k, 4) * radius);
                    if (Span(b, at, out _, out var top) && top <= limit && top > height)
                        height = top;
                }
            }
            return height > float.MinValue;
        }

        /// <summary>
        /// Whether a body at the feet would be in a wall: any box spanning the band between a step up and the head, at
        /// the feet or at eight points round the body's edge.
        /// </summary>
        public static bool Wall(in DynamicBuffer<KitBox> boxes, float3 feet, float radius, float stepUp, float head)
        {
            for (int i = 0; i < boxes.Length; i++)
                if (WallBox(boxes[i], feet, radius, stepUp, head, out _))
                    return true;
            return false;
        }

        /// <summary>Wall, for one box; tall when it also rises over the head there (a wall, not a tread or a ledge).</summary>
        static bool WallBox(in KitBox b, float3 feet, float radius, float stepUp, float head, out bool tall, float lift = 0f)
        {
            float low = feet.y + lift + stepUp, high = feet.y + head;
            bool wall = tall = false;
            if (math.distancesq(b.Centre.xz, feet.xz) > math.square(b.Reach + radius))
                return false;
            for (int k = 0; k < 9; k++)
            {
                var at = feet.xz + (k == 0 ? float2.zero : Round(k, 8) * radius);
                if (Span(b, at, out var bottom, out var top) && top > low && bottom < high)
                {
                    wall = true;
                    // (Tall: from the knees to over the head, a real wall. A slab overhead whose underside only brushes the
                    // head, a tower's roof deck over the stair's last turns, is a ceiling and stopped the climb at 5 m.)
                    tall |= top >= high && bottom <= low;
                }
            }
            return wall;
        }

        /// <summary>
        /// Whether a body moving from the feet to xz (at the feet's height) would go into a wall box it isn't already in.
        /// A body already in one (spawned there, a box placed round it, a stair's tread two up under its edge) may go
        /// into low boxes, so it can walk out and on up a stair, but never into a tall one.
        /// </summary>
        static bool Entering(in DynamicBuffer<KitBox> boxes, float3 from, float2 xz, bool inOne, float radius, float stepUp, float head)
            => EnteredBox(boxes, from, xz, inOne, radius, stepUp, head) >= 0;

        /// <summary>Entering's box: its index, or -1 (public for the tower test's trail).</summary>
        public static int EnteredBox(in DynamicBuffer<KitBox> boxes, float3 from, float2 xz, bool inOne, float radius, float stepUp, float head)
        {
            // (A step up from the floor the body would stand on there: a stair's tread is shallower than the body is wide,
            // so at the next tread's edge the one after it is already under the body's edge, two steps up. The head is
            // still measured from the feet, as before.)
            var to = new float3(xz.x, from.y, xz.y);
            var lift = Floor(boxes, to, radius * 0.6f, stepUp, out var step) ? math.max(step - from.y, 0f) : 0f;
            for (int i = 0; i < boxes.Length; i++)
                if (WallBox(boxes[i], to, radius, stepUp, head, out var tall, lift) && (tall || !inOne)
                    && !WallBox(boxes[i], from, radius, stepUp, head, out _))
                    return i;
            return -1;
        }

        /// <summary>
        /// A move into a kit wall: cut back to its clear X or Z part, or cancelled. A body already in a wall box (spawned
        /// there, or a box placed round it) is let move out of it and into low boxes, but not into a tall one: #518, when
        /// any box round the body let every move through, a stair's tread two up under the body's edge opened the tower
        /// wall beside it, and a long frame on the stair walked the player out through it.
        /// </summary>
        public static float2 KeepOut(in DynamicBuffer<KitBox> boxes, float3 from, float2 to, float radius, float stepUp, float head, ref bool blocked)
        {
            bool inOne = Wall(boxes, from, radius, stepUp, head);
            if (!Entering(boxes, from, to, inOne, radius, stepUp, head))
                return to;
            blocked = true;
            if (!Entering(boxes, from, new float2(to.x, from.z), inOne, radius, stepUp, head))
                return new float2(to.x, from.z);
            return !Entering(boxes, from, new float2(from.x, to.y), inOne, radius, stepUp, head) ? new float2(from.x, to.y) : from.xz;
        }

        /// <summary>The lowest box bottom above a step up of the feet, straight overhead: the ceiling a jump stops under.</summary>
        public static bool Ceiling(in DynamicBuffer<KitBox> boxes, float3 feet, float stepUp, out float height)
        {
            height = float.MaxValue;
            for (int i = 0; i < boxes.Length; i++)
                if (Span(boxes[i], feet.xz, out var bottom, out _) && bottom > feet.y + stepUp && bottom < height)
                    height = bottom;
            return height < float.MaxValue;
        }

        /// <summary>
        /// The highest box top at xz no higher than below, with a body's headroom clear above it (so a wall's or a
        /// roof's top under a floor doesn't count): the floor a walker there should stand on (-walk's check).
        /// </summary>
        public static bool TopAt(in DynamicBuffer<KitBox> boxes, float2 xz, float below, float headroom, out float height)
        {
            height = float.MinValue;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!Span(boxes[i], xz, out _, out var top) || top > below || top <= height)
                    continue;
                var clear = true;
                for (int j = 0; j < boxes.Length && clear; j++)
                    clear = j == i || !Span(boxes[j], xz, out var b2, out var t2) || t2 <= top + 0.05f || b2 >= top + headroom;
                if (clear)
                    height = top;
            }
            return height > float.MinValue;
        }

        /// <summary>
        /// W-67b: the lowest box top at xz no lower than above, with a body's headroom clear over it: the first floor
        /// over the ground there (a deck over the seabed, a building's ground floor rather than its roof).
        /// W-67c (#38): a box whose bottom is roofGap or more over above has standing room under it: it's a roof over
        /// that floor (an igloo's tunnel roof, 2.2 m over the dry ground), not the floor. Pass float.MaxValue where the
        /// ground is under water, so a deck still counts over the seabed.
        /// </summary>
        public static bool LowestTopAt(in DynamicBuffer<KitBox> boxes, float2 xz, float above, float headroom, out float height,
                                       float roofGap = float.MaxValue)
        {
            height = float.MaxValue;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!Span(boxes[i], xz, out var bottom, out var top) || top < above || top >= height || bottom >= above + roofGap)
                    continue;
                var clear = true;
                for (int j = 0; j < boxes.Length && clear; j++)
                    clear = j == i || !Span(boxes[j], xz, out var b2, out var t2) || t2 <= top + 0.05f || b2 >= top + headroom;
                if (clear)
                    height = top;
            }
            return height < float.MaxValue;
        }

        /// <summary>
        /// Whether the straight line from a to b passes through any box (W-97): a wall, roof or floor between two heads,
        /// so walk-by folk talk doesn't carry through a house wall.
        /// </summary>
        public static bool Blocks(in DynamicBuffer<KitBox> boxes, float3 a, float3 b)
        {
            var mid = (a + b) * 0.5f;
            var halfLength = math.distance(a, b) * 0.5f;
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                if (math.distance(box.Centre, mid) > box.Reach + halfLength)
                    continue;
                // (Slab test in the box's own frame.)
                var inv = math.inverse(box.Rotation);
                var from = math.mul(inv, a - box.Centre);
                var dir = math.mul(inv, b - a);
                float enter = 0f, exit = 1f;
                var hit = true;
                for (int k = 0; k < 3 && hit; k++)
                {
                    if (math.abs(dir[k]) < 1e-6f)
                    {
                        hit = math.abs(from[k]) <= box.Half[k];
                        continue;
                    }
                    var t0 = (-box.Half[k] - from[k]) / dir[k];
                    var t1 = (box.Half[k] - from[k]) / dir[k];
                    enter = math.max(enter, math.min(t0, t1));
                    exit = math.min(exit, math.max(t0, t1));
                    hit = enter <= exit;
                }
                if (hit)
                    return true;
            }
            return false;
        }

        /// <summary>Whether any box reaches within margin of xz (in plan): the kit set round there has been placed.</summary>
        public static bool AnyNear(in DynamicBuffer<KitBox> boxes, float2 xz, float margin)
        {
            for (int i = 0; i < boxes.Length; i++)
                if (math.distancesq(boxes[i].Centre.xz, xz) <= math.square(boxes[i].Reach + margin))
                    return true;
            return false;
        }

        static float2 Round(int k, int n)
        {
            math.sincos((k - 1) * (2f * math.PI / n), out var s, out var c);
            return new float2(c, s);
        }
    }
}
