using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Gait timing shared by the brain (where animals are simulated) and replicas (CreatureReplicaSystem), so both
    /// step and beat their wings alike.
    /// </summary>
    public static class CreatureMotion
    {
        // Flight rules every flier keeps to (birds now; giant birds and flying serpents later): nothing in the air moves
        // straight up or down. Forward speed stays above a minimum, climbs and descents are held to a fixed angle, and
        // landings come in down a glide slope, flaring at the end.
        /// <summary>Slowest airspeed, as a share of the species' FlySpeed (except in the last moments of a landing).</summary>
        public const float MinAirspeed = 0.35f;
        /// <summary>Steepest climb or descent: tan 35° (with the ground pushing up now and then, about 45° at most).</summary>
        public const float MaxClimb = 0.7f;
        /// <summary>The glide slope a landing comes down: tan 25°.</summary>
        public const float GlideSlope = 0.47f;
        /// <summary>Distance from the landing spot at which a bird flares (slows, pitches up, legs forward).</summary>
        public const float FlareDistance = 1.5f;

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
                // Beating harder while climbing (taking off).
                if (Flapping(s, ref b))
                    s.Phase = math.frac(s.Phase + dt * b.FlapRate * (1f + math.saturate(s.Vertical) * 0.6f));
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
