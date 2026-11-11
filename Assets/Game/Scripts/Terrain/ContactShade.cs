using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The seam darkening where a placed thing meets the ground or rock under it (W-135 row 4, shared with W-136): not a
    /// shadow, but the ground's side of the darkening Grounding.hlsl gives the thing's own base, so both faces of the seam
    /// are darkened alike and the hard edge between them reads softer. Darkest right at the contact (Seam: Grounding's
    /// tight contact times its broad rise, what a base gets at height 0), back to 1 within a short reach sized from the
    /// contact's width (not the thing's height or spread). Baked into vertex colours when a mesh builds: no per-frame cost.
    /// </summary>
    public static class ContactShade
    {
        /// <summary>The darkest it gets, at the seam: PunchyGroundContact (0.72) x PunchyGroundBroad (0.75), as a base.</summary>
        public const float Seam = 0.54f;
        /// <summary>The shortest and longest fall-off (m): a stem's seam still blends, a big plinth's stays a seam.</summary>
        public const float MinReach = 0.15f, MaxReach = 1.5f;

        /// <summary>How far the darkening reaches out from a contact this wide (m): about its own width.</summary>
        public static float Reach(float width) => math.clamp(width, MinReach, MaxReach);

        /// <summary>The shade (Seam..1) at distance d (m) from a contact whose darkening reaches this far.</summary>
        public static float Shade(float d, float reach) => math.lerp(Seam, 1f, math.smoothstep(0f, reach, d));

        /// <summary>
        /// Where something touches down: a footprint at its base, a rectangle (turned about y: Axis is its local x in the
        /// xz plane) grown by Radius, so a disc is a zero rectangle with a radius. Distance is to the footprint itself,
        /// in 3D, so a wall rising beside a thing darkens up from the floor as far as the floor does out from it.
        /// </summary>
        public struct Contact
        {
            public float3 Base;
            public float2 Axis, Half;
            public float Radius, Reach;

            /// <summary>A round footprint (a stem, a crystal's foot, a drum) of this radius.</summary>
            public static Contact Disc(float3 at, float radius) => new Contact
            {
                Base = at, Axis = new float2(1f, 0f), Radius = radius, Reach = ContactShade.Reach(radius * 2f)
            };

            /// <summary>A rectangular footprint (a chest, a plinth) with these half sizes along axis and across it.</summary>
            public static Contact Box(float3 at, float2 axis, float2 half) => new Contact
            {
                Base = at, Axis = math.normalizesafe(axis, new float2(1f, 0f)), Half = math.abs(half),
                Reach = ContactShade.Reach(math.cmin(math.abs(half)) * 2f)
            };

            /// <summary>The farthest from Base any point it darkens can be (a quick reject).</summary>
            public float Extent => math.length(Half) + Radius + Reach;

            /// <summary>Distance (m) from p to the footprint.</summary>
            public float Distance(float3 p)
            {
                var q = p - Base;
                var local = new float2(math.dot(q.xz, Axis), math.dot(q.xz, new float2(-Axis.y, Axis.x)));
                var e = math.max(math.abs(local) - Half, 0f);
                return math.max(math.length(new float3(e.x, q.y, e.y)) - Radius, 0f);
            }

            /// <summary>Its shade at p (Seam..1).</summary>
            public float ShadeAt(float3 p) => ContactShade.Shade(Distance(p), Reach);
        }
    }
}
