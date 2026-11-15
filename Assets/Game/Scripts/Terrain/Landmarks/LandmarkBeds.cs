using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-164 (#334): level garden beds under landmarks' placed gardens. A garden was set at the ground height of its
    /// centre, so on a slope its uphill side sank into the hill and its downhill side floated. The ground under each
    /// bed is cut and filled to that centre height (the "level"), flat out past the garden's corners, then blended back
    /// to the hillside over a short ring: in terraces of <see cref="Step"/> on a big drop, ending in a short steep edge
    /// (a kerb on the fill side, a cut bank uphill), which the slope shading draws as stone. Applied last of all the
    /// ground passes, towards a fixed level, so applying it again changes nothing. TerrainSource runs the loop (the
    /// level is its own unbedded height at the bed's centre); this holds the shape.
    /// </summary>
    public static class LandmarkBeds
    {
        /// <summary>Flat ground past the garden's corners (metres).</summary>
        public const float Margin = 0.4f;
        /// <summary>The blend ring back to the hillside (metres), narrowed to stay inside the landmark's footprint.</summary>
        const float Blend = 3f;
        /// <summary>Terrace height (metres): a drop larger than this steps down in flat treads.</summary>
        const float Step = 0.6f;

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

        /// <summary>The ground <paramref name="h"/> levelled towards <paramref name="level"/> at a distance from a bed.</summary>
        public static float Level(float h, float level, float distance, float bedRadius, float ring)
        {
            var t = math.saturate((distance - bedRadius) / ring);
            var offset = (h - level) * math.smoothstep(0f, 1f, t);
            // Terraces: flat treads of Step, each rising over the last third of its width.
            var a = math.abs(offset) / Step;
            var terraced = math.sign(offset) * Step * (math.floor(a) + math.smoothstep(0.65f, 1f, math.frac(a)));
            // The last fifth of the ring meets the hillside: a short steep edge.
            return level + math.lerp(terraced, h - level, math.smoothstep(0.8f, 1f, t));
        }
    }
}
