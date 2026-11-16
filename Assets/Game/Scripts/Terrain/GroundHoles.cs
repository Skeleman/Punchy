using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-174 row 4 (#405, rule 33): the one hole mask. The ground is removed in three places: a cave mouth
    /// (TerrainSource.MouthCut), a sunken temple's entrance (TempleCut) and a scarp's stepped strip, which its skin mesh
    /// draws instead (ScarpSet.Strip). Before this each sampler heard of a different subset (flora knew only the cave
    /// clearance and cliff pieces, so plants stood over the temple's opening and in scarp strips; nothing knew the
    /// strips), so every sampler now asks here. TerrainSource.IsCaveHole is this mask, so its callers (creatures, roads,
    /// roadside, settlements) hear of every hole too. The terrain mesh still writes the per-vertex cut itself.
    /// </summary>
    public static class GroundHoles
    {
        /// <summary>Whether the ground surface here is removed (a cave mouth, a temple entrance or a scarp strip).</summary>
        public static bool Removed(in TerrainSource source, float2 xz)
        {
            var h = source.SampleHeight(xz);
            if (source.MouthCut(xz, h) < 0f || source.TempleCut(xz) < 0f)
                return true;
            // step 0: the strip itself, without the thin ends the mesh keeps to show the stepped ground.
            return source.Scarps.IsCreated && ScarpSet.Strip(ref source.Scarps.Value, xz, 0f, h, out var cut, out _) && cut < 0f;
        }

        /// <summary>
        /// Signed distance (m) to the nearest cave mouth or temple entrance cut at ground height h, negative inside, for
        /// samplers that keep a margin from the edge (grass blades hanging over an opening). Scarp strips keep their own
        /// margin (ScarpSet.Edge). 100 away from both.
        /// </summary>
        public static float Edge(in TerrainSource source, float2 xz, float h) =>
            math.min(source.MouthCut(xz, h), source.Landmarks.IsCreated ? source.TempleCut(xz) : 100f);

        /// <summary>
        /// Whether nothing may stand here: the ground is removed, or a cliff kit piece rises over it or hangs over it
        /// (CliffKit.Covered). Flora and other placed things ask this.
        /// </summary>
        public static bool Blocked(in TerrainSource source, float2 xz) => CliffKit.Covered(source, xz) || Removed(source, xz);
    }
}
