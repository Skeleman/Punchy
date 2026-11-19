using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-174 (#406, rule 33): the one natural profile for every cut and fill that levels ground towards a target
    /// (landmark flattening, garden beds, plot pads, paddy rims). Before this each path had its own falloff (terraces,
    /// a disc mask, a rectangle's smoothstep), and each left a straight or perfectly round line along its rim (#403).
    ///
    /// The profile has three parts, shared by every caller:
    /// - <see cref="Band"/>: the weight from the edit's edge outwards. The outer reach is fixed (callers keep it inside
    ///   the footprint their cell lists know about), but where the bank begins wanders with world-space noise, so the
    ///   top and toe of a bank are never a straight line or a circle.
    /// - <see cref="Falloff"/>: a smootherstep curve, so the bank meets the flat and the hillside without a crease.
    /// - <see cref="Apply"/>: the one blend towards the target, plus a little roughness on the bank's face that grows
    ///   with the height of the cut, so a deep cut reads as a broken earth bank (the slope shading draws it as rock)
    ///   with talus slumped at its toe, not a smooth ramp. Zero on the flat and on the untouched ground.
    /// Everything is a pure function of world xz, so the mesh, collision, far ring, flora and spawns all agree.
    /// </summary>
    public static class GroundEdits
    {
        /// <summary>World-space edge noise, 0..1, two octaves at <paramref name="scale"/> (1/m), offset by a seed.</summary>
        public static float EdgeNoise(float2 xz, float scale, float seed)
        {
            var n = 0.65f * noise.snoise(xz * scale + seed) + 0.35f * noise.snoise(xz * (scale * 2.7f) - seed * 1.37f + 31.7f);
            return math.saturate(n * 0.5f + 0.5f);
        }

        /// <summary>1 inside, 0 outside, smootherstep between (t: 0 at the bank's top, 1 at its outer end).</summary>
        public static float Falloff(float t)
        {
            t = math.saturate(t);
            return 1f - t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        /// The edit's weight at <paramref name="distance"/> past its edge (metres or any unit), with the bank reaching
        /// out to <paramref name="reach"/> (fixed). The bank begins between <paramref name="startMin"/> and
        /// <paramref name="startMax"/> as the edge noise wanders, so the flat's rim follows no straight line.
        /// </summary>
        public static float Band(float distance, float startMin, float startMax, float reach, float edge)
        {
            var start = math.min(math.lerp(startMin, startMax, edge), reach - 1e-3f);
            return Falloff((distance - start) / (reach - start));
        }

        /// <summary>
        /// Ground <paramref name="h"/> pulled towards <paramref name="target"/> by weight <paramref name="w"/> (1 fully
        /// levelled), with the bank's face broken up by up to <paramref name="rough"/> of the cut's height (capped at
        /// 3 m). On a cut the break-up is mostly talus heaped on the lower half of the bank; on a fill, a lumpy slope.
        /// </summary>
        public static float Apply(float2 xz, float h, float target, float w, float rough)
        {
            var offset = h - target;
            var levelled = target + offset * (1f - w);
            if (rough <= 0f || w <= 0f || w >= 1f)
                return levelled;
            var face = 4f * w * (1f - w);   // 1 mid-bank, 0 on the flat and the hillside
            var size = math.min(math.abs(offset), 3f);
            var lumps = noise.snoise(xz * 0.45f + 5.3f) * 0.6f + noise.snoise(xz * 1.1f - 2.1f) * 0.4f;
            // Talus: on a cut, loose stuff slumps to the toe (the half of the bank nearest the flat, w > 0.5).
            var talus = offset > 0f ? math.saturate(w * 2f - 1f) * 0.5f : 0f;
            return levelled + rough * face * size * (lumps * 0.5f + talus);
        }
    }
}
