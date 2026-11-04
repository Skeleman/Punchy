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
            float low = feet.y + stepUp, high = feet.y + head;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                if (math.distancesq(b.Centre.xz, feet.xz) > math.square(b.Reach + radius))
                    continue;
                for (int k = 0; k < 9; k++)
                {
                    var at = feet.xz + (k == 0 ? float2.zero : Round(k, 8) * radius);
                    if (Span(b, at, out var bottom, out var top) && top > low && bottom < high)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A move into a kit wall: cut back to its clear X or Z part, or cancelled. A body already in a wall (spawned
        /// there, or a box placed round it) is let move, so it can walk out.
        /// </summary>
        public static float2 KeepOut(in DynamicBuffer<KitBox> boxes, float3 from, float2 to, float radius, float stepUp, float head, ref bool blocked)
        {
            if (!Wall(boxes, new float3(to.x, from.y, to.y), radius, stepUp, head))
                return to;
            if (Wall(boxes, from, radius, stepUp, head))
                return to;
            blocked = true;
            if (!Wall(boxes, new float3(to.x, from.y, from.z), radius, stepUp, head))
                return new float2(to.x, from.z);
            return !Wall(boxes, new float3(from.x, from.y, to.y), radius, stepUp, head) ? new float2(from.x, to.y) : from.xz;
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
