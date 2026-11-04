using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>What part of a room an interior box is (all are solid; the kind is for whoever draws or lights them).</summary>
    public enum InteriorPart : byte { Floor = 0, Wall = 1, Ceiling = 2 }

    /// <summary>One solid axis-aligned box of an interior cell, in world space.</summary>
    public struct InteriorBox
    {
        public float3 Min, Max;
        public InteriorPart Part;

        public InteriorBox(float3 min, float3 max, InteriorPart part)
        {
            Min = math.min(min, max);
            Max = math.max(min, max);
            Part = part;
        }
    }

    /// <summary>
    /// #25 (W-83): "interior cells", rooms built at runtime off the island (a house's inside, reached through its
    /// door), as a list of solid boxes (floors, walls, ceilings) that the ground queries take in: TerrainSource.Deck
    /// (and so FloorUnder and the player's floor) stands bodies on a box's top, TerrainSource.TowerWall stops them at a
    /// box in the way, and PlayerMovementSystem.SolidDistance (the cave push-out and the camera) treats boxes as solid.
    /// The list lives in a SharedStatic, so Burst jobs read it without it being passed in. Fill it from the main
    /// thread while no job is reading (Set, Add, Clear); an empty list changes nothing.
    /// </summary>
    public static class InteriorCells
    {
        /// <summary>How far round a body's middle a wall stops it (about the player's radius).</summary>
        public const float BodyRadius = 0.35f;

        struct Key { }

        struct Table
        {
            public UnsafeList<InteriorBox> Boxes;
            /// <summary>The union of every box (xz min, xz max), a quick reject for points away from every room.</summary>
            public float4 Bounds;
            public int Count;
        }

        static readonly SharedStatic<Table> Shared = SharedStatic<Table>.GetOrCreate<Key>();

        /// <summary>How many boxes are in the list now.</summary>
        public static int Count => Shared.Data.Count;

        /// <summary>The box at an index (main thread, for tools and drawing).</summary>
        public static InteriorBox Get(int i) => Shared.Data.Boxes[i];

        /// <summary>Replaces the whole list.</summary>
        public static void Set(NativeArray<InteriorBox> boxes)
        {
            Clear();
            for (int i = 0; i < boxes.Length; i++)
                Add(boxes[i]);
        }

        /// <summary>Adds one box.</summary>
        public static void Add(InteriorBox box)
        {
            ref var t = ref Shared.Data;
            if (!t.Boxes.IsCreated)
                t.Boxes = new UnsafeList<InteriorBox>(16, Allocator.Persistent);
            t.Boxes.Add(box);
            t.Bounds = t.Count == 0 ? new float4(box.Min.xz, box.Max.xz)
                                    : new float4(math.min(t.Bounds.xy, box.Min.xz), math.max(t.Bounds.zw, box.Max.xz));
            t.Count = t.Boxes.Length;
        }

        /// <summary>Empties the list (and frees it).</summary>
        public static void Clear()
        {
            ref var t = ref Shared.Data;
            if (t.Boxes.IsCreated)
                t.Boxes.Dispose();
            t = default;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Clear();
            Application.quitting -= Clear;
            Application.quitting += Clear;
        }

        static bool Near(in Table t, float2 xz, float margin) =>
            t.Count > 0 && math.all(xz >= t.Bounds.xy - margin) && math.all(xz <= t.Bounds.zw + margin);

        /// <summary>
        /// Signed distance from a point to the nearest box (negative inside one); 1e9 with no boxes or far from them.
        /// </summary>
        public static float Distance(float3 p)
        {
            ref var t = ref Shared.Data;
            if (!Near(t, p.xz, 50f))
                return 1e9f;
            var best = 1e9f;
            for (int i = 0; i < t.Count; i++)
            {
                var b = t.Boxes[i];
                var c = (b.Min + b.Max) * 0.5f;
                var q = math.abs(p - c) - (b.Max - b.Min) * 0.5f;
                best = math.min(best, math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f));
            }
            return best;
        }

        /// <summary>
        /// The highest box top under a point that a body there stands on: its xz inside the box and the top no more than
        /// <paramref name="stepUp"/> above the point.
        /// </summary>
        public static bool Floor(float3 p, out float top, float stepUp = 0.6f)
        {
            top = float.MinValue;
            ref var t = ref Shared.Data;
            if (!Near(t, p.xz, 0f))
                return false;
            var found = false;
            for (int i = 0; i < t.Count; i++)
            {
                var b = t.Boxes[i];
                if (p.x < b.Min.x || p.x > b.Max.x || p.z < b.Min.z || p.z > b.Max.z || b.Max.y > p.y + stepUp || b.Max.y <= top)
                    continue;
                top = b.Max.y;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Whether a box stands where a body at p would be (within BodyRadius of its middle, between a step up off its
        /// feet and its head), as TerrainSource.TowerWall has it.
        /// </summary>
        public static bool Wall(float3 p, float stepUp, float head)
        {
            ref var t = ref Shared.Data;
            if (!Near(t, p.xz, BodyRadius))
                return false;
            for (int i = 0; i < t.Count; i++)
            {
                var b = t.Boxes[i];
                if (p.x > b.Min.x - BodyRadius && p.x < b.Max.x + BodyRadius && p.z > b.Min.z - BodyRadius && p.z < b.Max.z + BodyRadius &&
                    b.Max.y > p.y + stepUp && b.Min.y < p.y + head)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The "-interiortest" room (DebugControls): 8 x 3 x 6 m with a door gap in its +Z wall, its floor 20 m above
        /// the sea, 300 m out past the island's edge on +X. Returns a log line with the spot to stand on.
        /// </summary>
        public static string TestRoom(in TerrainSource terrain)
        {
            var x = terrain.World.WorldSize * 0.5f + 300f;
            var y = terrain.World.SeaLevel + 20f;
            var lo = new float3(x - 4f, y, -3f);
            var hi = new float3(x + 4f, y + 3f, 3f);
            AddRoom(lo, hi, 0.3f, 1.2f, 2.4f);
            return $"InteriorCells: test room inside ({lo.x:F0},{lo.y:F0},{lo.z:F0})-({hi.x:F0},{hi.y:F0},{hi.z:F0}), " +
                   $"stand at ({x:F0}, {y:F1}, 0), sea floor {terrain.SampleHeight(new float2(x, 0f)):F1}, {Count} boxes";
        }

        /// <summary>
        /// A closed room (floor, four walls, ceiling) whose inside runs from <paramref name="insideMin"/> to
        /// <paramref name="insideMax"/>, walls <paramref name="thick"/> thick. A gap <paramref name="doorWidth"/> wide and
        /// <paramref name="doorHeight"/> high is left in the +Z wall's middle when doorWidth is above zero.
        /// </summary>
        public static void AddRoom(float3 insideMin, float3 insideMax, float thick = 0.3f, float doorWidth = 0f, float doorHeight = 0f)
        {
            float3 lo = insideMin, hi = insideMax;
            Add(new InteriorBox(new float3(lo.x - thick, lo.y - thick, lo.z - thick), new float3(hi.x + thick, lo.y, hi.z + thick), InteriorPart.Floor));
            Add(new InteriorBox(new float3(lo.x - thick, hi.y, lo.z - thick), new float3(hi.x + thick, hi.y + thick, hi.z + thick), InteriorPart.Ceiling));
            Add(new InteriorBox(new float3(lo.x - thick, lo.y, lo.z - thick), new float3(lo.x, hi.y, hi.z + thick), InteriorPart.Wall));
            Add(new InteriorBox(new float3(hi.x, lo.y, lo.z - thick), new float3(hi.x + thick, hi.y, hi.z + thick), InteriorPart.Wall));
            Add(new InteriorBox(new float3(lo.x, lo.y, lo.z - thick), new float3(hi.x, hi.y, lo.z), InteriorPart.Wall));
            if (doorWidth <= 0f)
            {
                Add(new InteriorBox(new float3(lo.x, lo.y, hi.z), new float3(hi.x, hi.y, hi.z + thick), InteriorPart.Wall));
                return;
            }
            var mid = (lo.x + hi.x) * 0.5f;
            var dh = math.min(doorHeight, hi.y - lo.y);
            Add(new InteriorBox(new float3(lo.x, lo.y, hi.z), new float3(mid - doorWidth * 0.5f, hi.y, hi.z + thick), InteriorPart.Wall));
            Add(new InteriorBox(new float3(mid + doorWidth * 0.5f, lo.y, hi.z), new float3(hi.x, hi.y, hi.z + thick), InteriorPart.Wall));
            if (lo.y + dh < hi.y)
                Add(new InteriorBox(new float3(mid - doorWidth * 0.5f, lo.y + dh, hi.z), new float3(mid + doorWidth * 0.5f, hi.y, hi.z + thick), InteriorPart.Wall));
        }
    }
}
