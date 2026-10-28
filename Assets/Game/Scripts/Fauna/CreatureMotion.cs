using Punchy.Terrain;
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
        /// The surface a crawler is on, as the way out of it (unit): in a cave, away from the nearest rock (the cave's
        /// distance field rises into rock); outside, the ground's normal. Steep tells whether that's more than a gentle
        /// slope (a rock face, a wall, a ceiling), where the body turns to lie on it. The same from the position alone on
        /// every machine.
        /// </summary>
        public static float3 Surface(in TerrainSource terrain, float3 at, out bool steep, out bool inCave)
        {
            inCave = false;
            if (terrain.Caves.IsCreated)
            {
                terrain.FloorUnder(at + new float3(0f, 0.3f, 0f), out inCave);
                inCave &= at.y < terrain.SampleHeight(at.xz) - 1f;
            }
            float3 up;
            if (inCave)
            {
                const float e = 0.15f;
                var grad = new float3(
                    terrain.CaveDistance(at + new float3(e, 0f, 0f)) - terrain.CaveDistance(at - new float3(e, 0f, 0f)),
                    terrain.CaveDistance(at + new float3(0f, e, 0f)) - terrain.CaveDistance(at - new float3(0f, e, 0f)),
                    terrain.CaveDistance(at + new float3(0f, 0f, e)) - terrain.CaveDistance(at - new float3(0f, 0f, e)));
                up = math.normalizesafe(-grad, new float3(0f, 1f, 0f));
            }
            else
                up = terrain.SampleNormal(at.xz, 0.5f);
            steep = up.y < 0.8f;
            return up;
        }

        /// <summary>
        /// How far through its gait cycle a body goes for covering a distance on foot, keeping planted feet planted:
        /// the feet on the ground (first half of their cycle) sweep back at the rate their legs' key poses (as blended
        /// for this speed, as the animator blends them) move them at this point of the cycle, and the body passes over
        /// them at the same rate. (An average stride would let them slide: a foot's sweep along its arc isn't even.)
        /// </summary>
        public static float StepPhase(ref CreatureBlob b, in CreatureState s, float distance)
        {
            var scale = math.max(s.Scale, 0.01f);
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.Speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed);
            var sprint = b.Sprint(s.Speed);
            var stride = math.lerp(b.WalkStride, b.RunStride, run) * (1f + CreatureBlob.SprintStride * sprint);
            var sweep = 0f;
            var planted = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var p = math.frac(s.Phase + part.Phase);
                if (p >= 0.5f)
                    continue;   // swinging through the air
                const float step = 0.01f;
                float p0 = math.max(p - step, 0f), p1 = math.min(p + step, 0.4999f);
                // (The body rocks fore and aft as it runs (RunRock), swinging the planted foot with it: included.)
                var rock0 = b.RunRock * run * move * math.sin(2f * math.PI * (s.Phase + p0 - p));
                var rock1 = b.RunRock * run * move * math.sin(2f * math.PI * (s.Phase + p1 - p));
                sweep += (FootZ(ref b, ref part, p0, move, run, sprint, rock0) - FootZ(ref b, ref part, p1, move, run, sprint, rock1)) / (p1 - p0);
                planted++;
            }
            // (A foot barely sweeping, or none planted, as in a run's flight: the stride's average pace.)
            var perCycle = planted > 0 && sweep / planted > 0.25f * stride ? sweep / planted : stride;
            return distance / math.max(perCycle * scale, 1e-3f);
        }

        /// <summary>
        /// Where a leg's foot is in the body's frame (unscaled; x right, z forward) at a point of its cycle, posed as the
        /// animator poses it for this speed.
        /// </summary>
        public static float3 FootAt(ref CreatureBlob b, ref CreaturePart part, float p, float speed)
        {
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), speed);
            var sprint = b.Sprint(speed);
            var pos = part.Socket;
            var rot = part.Rest;
            for (int j = 0; j < part.SegmentCount; j++)
            {
                var seg = part.FirstSegment + j;
                rot = math.mul(rot, quaternion.EulerZXY(b.LegAngles(seg, math.frac(p - j * part.JointPhaseStep), move, run, sprint)));
                pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
            }
            return pos;
        }

        /// <summary>
        /// How far forward a leg's foot is (unscaled) at a point in its cycle, with the body pitched by rock (radians,
        /// nose up as the animator pitches it).
        /// </summary>
        static float FootZ(ref CreatureBlob b, ref CreaturePart part, float p, float move, float run, float sprint, float rock)
        {
            var pos = part.Socket;
            var rot = part.Rest;
            for (int j = 0; j < part.SegmentCount; j++)
            {
                var seg = part.FirstSegment + j;
                rot = math.mul(rot, quaternion.EulerZXY(b.LegAngles(seg, math.frac(p - j * part.JointPhaseStep), move, run, sprint)));
                pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
            }
            return math.mul(quaternion.RotateX(rock), pos).z;
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
                s.Phase = math.frac(s.Phase + StepPhase(ref b, s, s.Speed * dt));
            s.Sway += (-s.YawRate * 0.35f - s.Sway) * math.min(1f, dt * 4f);
        }
    }
}
