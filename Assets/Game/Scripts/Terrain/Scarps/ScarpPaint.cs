using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The colour of a scarp's face (W-155 row 2), worked out from its shape so the material follows the rock, in
    /// the colours of the land round it: each bed is tinted from the ground just behind the rim and just past the
    /// foot (lighter for a hard bed, darker for a soft one, a little warmer or cooler per bed, so colour bands line up
    /// with the ledges they make without leaving the ground's palette). Slots and overhang undersides are darker,
    /// water streaks run down from the rim and from each ledge, moss sits low on the damp shaded side, the turf cap
    /// and the soil under it spill over the rim, the toe at the foot is the ground past it (with scree fans in its
    /// earth), and soil and grass gather on ledge tops. The rock's tone stays on the face: nothing paints the ground.
    /// All of it is laid over the terrain's own surface at that point (the same triplanar rock steep ground gets) and
    /// fades out towards the seams, so skin and ground meet in one colour.
    /// </summary>
    public static class ScarpPaint
    {
        /// <summary>Height over which the paint fades in from each seam (m), and width over which it fades in from
        /// where the skin starts at a strip end.</summary>
        public const float SeamFade = 1.5f, EndFade = 6f;

        static float Lum(float3 c) => math.dot(c, new float3(0.3f, 0.59f, 0.11f));

        /// <summary>The earth under a ground colour: a green (grassy) colour becomes a warm grey of the same
        /// lightness, so a meadow at the foot or on the top gives soil and rock its tone, not its green.</summary>
        static float3 Earth(float3 c)
        {
            float lum = Lum(c);
            return math.lerp(c, lum * new float3(1.1f, 1f, 0.86f), math.saturate((c.y - c.x) * 10f));
        }

        /// <summary>A bed's colours at one height of the face (<see cref="Bed"/>): the ground tones it is drawn from,
        /// the bed's rock base and finished tone, and which bed it is.</summary>
        public struct BedTone
        {
            public float3 RimEarth, FootEarth, Ground, RockBase, Tone;
            public float Bed, Hard, Pick;
        }

        /// <summary>
        /// The colour of the bed at height y: the ground's colour (the slope below's towards the foot, the top's
        /// towards the rim) made lighter for a hard bed and darker for a soft one, nudged warmer or cooler per layer
        /// of the stack (so a bed keeps its colour each time the stack repeats), with a slight drift along the rim.
        /// Pulled towards the ground's own grey, so a green or snowy top does not tint rock. Shared by the face and
        /// by the debris fallen from it (<see cref="ScarpDebris"/>).
        /// </summary>
        public static BedTone Bed(ref Scarp sc, in ScarpAt at, float3 cracks, float y, float3 rimTone, float3 footTone)
        {
            float drop = math.max(at.RimY - at.FootY, 0.01f), up = y - at.FootY;
            float seed = sc.Seed % 977u * 1.37f;
            float bedY = ScarpSet.BedY(ref sc, at.S, y, cracks);
            var b = new BedTone { Bed = ScarpSet.Bed(ref sc, bedY), Hard = ScarpSet.Hardness(ref sc, at.S, bedY) };
            int n = math.max(sc.Layers.Length, 1);
            float layer = b.Bed - n * math.floor(b.Bed / n);
            b.RimEarth = Earth(rimTone);
            b.FootEarth = Earth(footTone);
            b.Ground = math.lerp(b.FootEarth, b.RimEarth, math.smoothstep(0.1f, 0.9f, up / drop));
            b.Pick = math.frac(math.sin(layer * 7.13f + seed * 3.1f) * 43758.5453f);
            float drift = 0.5f + 0.5f * noise.snoise(new float2(at.S / 70f, seed + 3f + layer));
            float lum = Lum(b.Ground);
            b.RockBase = math.lerp(b.Ground, new float3(lum, lum, lum) * new float3(1.04f, 1f, 0.94f), 0.35f);
            float bright = math.lerp(0.86f, 1.14f, math.smoothstep(0.3f, 0.7f, b.Hard)) * (0.94f + 0.12f * drift);
            var tint = 1f + 0.07f * (b.Pick - 0.5f) * new float3(1f, 0.1f, -1f);
            b.Tone = b.RockBase * bright * tint;
            return b;
        }

        /// <summary>The scree colour for a bed: its rock half in the earth of the slope below.</summary>
        public static float3 Scree(in BedTone b) => math.lerp(b.RockBase, b.FootEarth, 0.5f) * (0.9f + 0.15f * b.Pick);

        /// <summary>
        /// Paints one skin vertex. y is its height, normal its own (from the skin grid), fade 0 at the seams to 1 on
        /// the open face; rimTone is the ground's colour behind the rim, foot the ground's surface past the foot.
        /// </summary>
        public static SurfaceProps Paint(ref Scarp sc, in ScarpAt at, float3 cracks, float y, float3 normal, float fade,
                                         float3 rimTone, in SurfaceProps foot, SurfaceProps surface)
        {
            float drop = math.max(at.RimY - at.FootY, 0.01f), u = at.RimY - y, up = y - at.FootY;
            float seed = sc.Seed % 977u * 1.37f;
            bool earth = sc.Kind == ScarpKind.RiverCut;
            var b = Bed(ref sc, at, cracks, y, rimTone, foot.Albedo);
            float bed = b.Bed, hard = b.Hard;
            var rimEarth = b.RimEarth;
            var groundTone = b.Ground;
            var tone = b.Tone;
            // The face reads as the rock of this ground, with the terrain's own steep-ground shading under it.
            var albedo = math.lerp(surface.Albedo, tone, earth ? 0.85f : 0.75f);

            // Slots (gullies, joints) and overhang undersides are a little darker: the light already shades them,
            // so this only adds the grime that collects there.
            var slot = ScarpSet.Slots(ref sc, at, cracks, u, bed);
            float shade = math.saturate(0.8f * slot.x + 0.6f * hard * slot.y);
            float under = math.smoothstep(-0.05f, -0.5f, normal.y);
            albedo *= 1f - 0.22f * shade - 0.15f * under;

            // Water streaks: long thin dark runs that start at the rim or under a ledge and fade down the face.
            // They gather in a few stretches of the rim (where the rim dips and water runs off), not everywhere.
            float run = noise.snoise(new float2(at.S / 2.2f, y / 25f + seed));
            float gather = math.saturate(1.6f * noise.snoise(new float2(at.S / 18f, seed + 23f)));
            float streaks = math.saturate(run * 3f - 1.9f) * gather * (1f - under) * (0.5f + 0.5f * math.smoothstep(0f, 0.3f * drop, u));
            albedo *= 1f - 0.25f * streaks;

            // Moss low on the damp shaded side (facing away from the sun, north), more on a river bank.
            float damp = math.saturate(1.2f - up / math.max(0.6f * drop, 1f)) * (earth ? 0.7f : 0.6f);
            float shaded = math.saturate(0.3f + normal.z);
            float patch = math.saturate(0.5f + 1.5f * noise.snoise(new float2(at.S / 6f, y / 3f + seed + 50f)));
            float moss = damp * shaded * patch * (1f - under) * (1f - shade);
            albedo = math.lerp(albedo, math.lerp(new float3(0.07f, 0.10f, 0.035f), groundTone, 0.3f), 0.6f * moss);

            // The turf cap and the darker soil and roots under it spill over the rim, raggedly.
            ScarpSet.Turf(ref sc, at.S, out var depth);
            float rag = 0.15f * noise.snoise(new float2(at.S / 0.6f, seed + 71f));
            float cap = 1f - math.smoothstep(depth - 0.05f + rag, depth + 0.1f + rag, u);
            float soil = (1f - cap) * (1f - math.smoothstep(depth + 0.3f, depth + (earth ? 0.8f : 0.7f), u + rag));
            albedo = math.lerp(albedo, rimEarth * 0.7f * new float3(1.05f, 0.95f, 0.85f), 0.7f * soil);

            // The toe: where the face lies back into the slope at its foot it is ground, not wall, so it takes the
            // ground's own surface from past the foot (a green meadow stays green right up the toe) and the rock's
            // tone never runs down onto it. How far up the toe the ground reaches wanders along the rim, and its
            // edge is broken by noise along the rim, not set by a height or a radius.
            float lie = math.smoothstep(0.4f, 0.75f, normal.y);
            float climb = drop * (0.1f + 0.25f * math.saturate(0.5f + noise.snoise(new float2(at.S / 16f, seed + 90f))));
            float ragToe = climb * 0.3f * noise.snoise(new float2(at.S / 2.5f, y / 2f + seed + 95f));
            float toe = lie * (1f - math.smoothstep(0.5f * climb, climb, up + ragToe));
            // Scree: rubble fans spilling down the toe from the wall here and there, in the earth of the slope below,
            // thickest near the wall and thinning out downhill in ragged tongues.
            float tongue = ScarpLife.Fan(ref sc, at.S) + 0.3f * noise.snoise(new float2(at.S / 2f, up / 1.5f + seed + 99f));
            float scree = toe * math.saturate(2f * tongue + 0.8f * up / climb - 0.4f) * (earth ? 0.5f : 1f);

            // Ledge tops hold soil and grass.
            float ledge = math.smoothstep(0.55f, 0.85f, normal.y) * (1f - 0.5f * hard) * (1f - toe);

            var painted = surface;
            painted.Albedo = albedo;
            painted.Smoothness = surface.Smoothness + 0.05f * streaks;
            painted.Grass = math.max(surface.Grass, ledge);
            painted.Strata = 0f;
            // The paint fades in from the seams, except the turf cap, which runs right to the rim, and the toe, which
            // is the foot's ground all the way down to it.
            var result = SurfaceProps.Lerp(surface, painted, fade);
            var ground = foot;
            ground.Albedo = math.lerp(foot.Albedo, Scree(b), 0.8f * scree);
            ground.Grass = foot.Grass * (1f - scree);
            ground.Pebbles = math.max(foot.Pebbles, scree);
            ground.Strata = 0f;
            result = SurfaceProps.Lerp(result, ground, toe);
            var turf = surface;
            turf.Albedo = rimTone;
            turf.Grass = math.max(surface.Grass, 1f);
            result = SurfaceProps.Lerp(result, turf, cap * (1f - math.smoothstep(-0.2f, 0.3f, -normal.y)));
            // The ground's strata bands are projected from above, so on a near-vertical face they smear into
            // vertical stripes: drop them faster than the colour fades in, so strip ends do not show them.
            result.Strata = surface.Strata * (1f - math.saturate(4f * fade));
            return result;
        }
    }
}
