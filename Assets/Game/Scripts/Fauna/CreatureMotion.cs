using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Gait timing shared by the brain (where animals are simulated) and replicas (CreatureReplicaSystem), so both
    /// step and beat their wings alike.
    /// </summary>
    public static class CreatureMotion
    {
        /// <summary>
        /// Whether a flying bird is beating its wings rather than gliding: always for small birds; soaring birds flap
        /// while climbing and in short bursts between glides.
        /// </summary>
        public static bool Flapping(in CreatureState s, ref CreatureBlob b)
        {
            if (b.Glide <= 0f || s.Vertical > 0.15f)
                return true;
            var burst = math.lerp(0.6f, 0.15f, b.Glide);
            return math.frac(s.Clock / (2.5f + 5f * b.Glide) + (s.Individual % 97) * 0.01f) < burst;
        }

        /// <summary>
        /// Advances the gait: by the distance covered (so feet don't slide), or in flight by the wingbeat (held at the
        /// top of the stroke while gliding). Also eases the tail and ear spring out of turns.
        /// </summary>
        public static void AdvancePhase(ref CreatureState s, ref CreatureBlob b, float dt)
        {
            if (s.Fly > 0.5f)
            {
                if (Flapping(s, ref b))
                    s.Phase = math.frac(s.Phase + dt * b.FlapRate);
                else
                    s.Phase += (0.12f - s.Phase) * math.min(1f, dt * 4f);   // wings spread, held
            }
            else
            {
                var scale = math.max(s.Scale, 0.01f);
                var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed / scale);
                var stride = math.lerp(b.WalkStride, b.RunStride, run) * scale;
                s.Phase = math.frac(s.Phase + s.Speed * dt / stride);
            }
            s.Sway += (-s.YawRate * 0.35f - s.Sway) * math.min(1f, dt * 4f);
        }
    }
}
