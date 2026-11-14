using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The colour of a scarp's face (W-155 row 2), worked out from its shape so the material follows the rock: each
    /// bed takes its own colour from its hardness and the scarp's seed (so colour bands line up with the ledges they
    /// make), slots and overhang undersides are darker, water streaks run down from the rim and from each ledge, moss
    /// sits low on the damp shaded side, and soil and grass gather on ledge tops. All of it is laid over the
    /// terrain's own surface at that point (the same triplanar rock steep ground gets) and fades out towards the
    /// seams, so skin and ground meet in one colour.
    /// </summary>
    public static class ScarpPaint
    {
        /// <summary>Height over which the paint fades in from each seam (m), and width over which it fades in from
        /// where the skin starts at a strip end.</summary>
        public const float SeamFade = 1.5f, EndFade = 6f;

        /// <summary>
        /// Paints one skin vertex. y is its height, reach how far out from the rim it stands, normal its own (from
        /// the skin grid), fade 0 at the seams to 1 on the open face.
        /// </summary>
        public static SurfaceProps Paint(ref Scarp sc, in ScarpAt at, float3 cracks, float y, float3 normal, float fade, SurfaceProps surface)
        {
            if (fade <= 0f)
                return surface;
            float drop = math.max(at.RimY - at.FootY, 0.01f), u = at.RimY - y, up = y - at.FootY;
            float seed = sc.Seed % 977u * 1.37f;
            float bedY = ScarpSet.BedY(ref sc, at.S, y, cracks);
            float bed = ScarpSet.Bed(ref sc, bedY), hard = ScarpSet.Hardness(ref sc, at.S, bedY);
            int n = math.max(sc.Layers.Length, 1);
            float layer = bed - n * math.floor(bed / n);

            // Bed colour: hard beds pale buff to grey, soft beds ochre to dark shale, picked per layer of the stack
            // so a bed keeps its colour each time the stack repeats, with a slight drift along the rim.
            float pick = math.frac(math.sin(layer * 7.13f + seed * 3.1f) * 43758.5453f);
            float drift = 0.5f + 0.5f * noise.snoise(new float2(at.S / 70f, seed + 3f + layer));
            var hardTone = math.lerp(new float3(0.30f, 0.26f, 0.20f), new float3(0.31f, 0.30f, 0.28f), pick);
            var softTone = math.lerp(new float3(0.23f, 0.17f, 0.11f), new float3(0.13f, 0.12f, 0.10f), pick);
            var tone = math.lerp(softTone, hardTone, math.smoothstep(0.3f, 0.7f, hard)) * (0.9f + 0.2f * drift);
            // Keep the ground's own brightness range, so the face sits in the same light as the slope beside it.
            float baseLum = math.dot(surface.Albedo, new float3(0.3f, 0.59f, 0.11f));
            float toneLum = math.dot(tone, new float3(0.3f, 0.59f, 0.11f));
            tone *= math.clamp(math.lerp(1f, baseLum / math.max(toneLum, 1e-3f), 0.35f), 0.6f, 1.6f);
            // A river cut is mostly earth with a rock bed showing, so its beds lean further to the ground colour.
            var albedo = math.lerp(surface.Albedo, tone, sc.Kind == ScarpKind.RiverCut ? 0.45f : 0.8f);

            // Slots (gullies, joints) and overhang undersides are darker and drier.
            var slot = ScarpSet.Slots(ref sc, at, cracks, u, bed);
            float shade = math.saturate(0.8f * slot.x + 0.6f * hard * slot.y);
            float under = math.smoothstep(-0.05f, -0.5f, normal.y);
            albedo *= 1f - 0.45f * shade - 0.3f * under;

            // Water streaks: long thin dark runs that start at the rim or under a ledge and fade down the face.
            // They gather in a few stretches of the rim (where the rim dips and water runs off), not everywhere.
            float run = noise.snoise(new float2(at.S / 2.2f, y / 25f + seed));
            float gather = math.saturate(1.6f * noise.snoise(new float2(at.S / 18f, seed + 23f)));
            float streaks = math.saturate(run * 3f - 1.9f) * gather * (1f - under) * (0.5f + 0.5f * math.smoothstep(0f, 0.3f * drop, u));
            albedo *= 1f - 0.35f * streaks;

            // Moss low on the damp shaded side (facing away from the sun, north), more on a river bank.
            float damp = math.saturate(1.2f - up / math.max(0.6f * drop, 1f)) * (sc.Kind == ScarpKind.RiverCut ? 1f : 0.6f);
            float shaded = math.saturate(0.3f + normal.z);
            float patch = math.saturate(0.5f + 1.5f * noise.snoise(new float2(at.S / 6f, y / 3f + seed + 50f)));
            float moss = damp * shaded * patch * (1f - under) * (1f - shade);
            albedo = math.lerp(albedo, new float3(0.07f, 0.10f, 0.035f), 0.7f * moss);

            // Ledge tops hold soil and grass.
            float ledge = math.smoothstep(0.55f, 0.85f, normal.y) * (1f - 0.5f * hard);

            var painted = surface;
            painted.Albedo = albedo;
            painted.Smoothness = surface.Smoothness + 0.05f * streaks;
            painted.Grass = math.max(surface.Grass, ledge);
            painted.Strata = 0f;
            var result = SurfaceProps.Lerp(surface, painted, fade);
            // The ground's strata bands are projected from above, so on a near-vertical face they smear into
            // vertical stripes: drop them faster than the colour fades in, so strip ends do not show them.
            result.Strata = surface.Strata * (1f - math.saturate(4f * fade));
            return result;
        }
    }
}
