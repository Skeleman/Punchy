using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-164 (#334): level garden beds under landmarks' placed gardens. A garden was set at the ground height of its
    /// centre, so on a slope its uphill side sank into the hill and its downhill side floated. The ground under each
    /// bed is cut and filled to that centre height (the "level"), flat out past the garden's corners, then blended back
    /// to the hillside over a short ring. W-174 (#406): the ring is the shared <see cref="GroundEdits"/> profile (a
    /// wandering rim, a curved bank, a broken face on a deep cut) instead of 0.6 m terraces with a hard edge. Applied
    /// last of all the ground passes, towards a fixed level. TerrainSource runs the loop (the level is its own
    /// unbedded height at the bed's centre); this holds the shape.
    /// </summary>
    public static class LandmarkBeds
    {
        /// <summary>Flat ground past the garden's corners (metres).</summary>
        public const float Margin = 0.4f;
        /// <summary>The blend ring back to the hillside (metres), narrowed to stay inside the landmark's footprint.</summary>
        const float Blend = 3f;

        /// <summary>
        /// Whether bed <paramref name="bed"/> of an instance reaches a point: its centre (world XZ), the distance to it,
        /// and the ring width. The ring stops inside the landmark's footprint, as LandmarkSampling.CellRange lists a
        /// landmark only in cells its footprint touches.
        /// </summary>
        public static bool Reach(in LandmarkInstance inst, float radius, in LandmarkBedData bed, float2 xz,
                                 out float2 centre, out float distance, out float ring)
        {
            centre = LandmarkSampling.ToWorld(inst, bed.Offset, radius);
            ring = math.clamp(radius * (1f - math.length(bed.Offset)) - bed.Radius - 0.25f, 0.5f, Blend);
            distance = math.length(xz - centre);
            return distance < bed.Radius + ring;
        }

        /// <summary>The ground <paramref name="h"/> at <paramref name="xz"/> levelled towards <paramref name="level"/> at a distance from a bed.</summary>
        public static float Level(float2 xz, float h, float level, float distance, float bedRadius, float ring)
        {
            // Flat to the bed's radius at least; the bank's top wanders out over the first third of the ring.
            var edge = GroundEdits.EdgeNoise(xz, 0.18f, 7.1f);
            var w = GroundEdits.Band(distance, bedRadius, bedRadius + ring * 0.35f, bedRadius + ring, edge);
            return GroundEdits.Apply(xz, h, level, w, 0.4f);
        }
    }
}
