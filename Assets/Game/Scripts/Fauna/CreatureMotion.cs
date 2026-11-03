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
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.GaitSpeed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.GaitSpeed);
            var sprint = b.Sprint(s, s.GaitSpeed);
            var stride = math.lerp(b.WalkStride, b.RunStride, run) * (1f + b.SprintStride * sprint);
            var sweep = 0f;
            var planted = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var p = math.frac(s.Phase + b.LegPhase(ref part, sprint));
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
            // (None planted, as in a run's flight: the stride's average pace. Feet barely sweeping, just setting off with
            // the swing still small: quicker steps, up to five times the stride's pace, rather than the body outrunning
            // its feet.)
            var perCycle = planted > 0 ? math.max(sweep / planted, 0.2f * stride) : stride;
            // A snake's body lies along a wave fixed to the ground, one walk stride long (CreatureAnimator): at any
            // speed the phase must run one cycle per wavelength, or the wave slides along with the body and the tail
            // whips side to side instead of following the head's track.
            if (b.Slithers)
                perCycle = b.WalkStride;
            return distance / math.max(perCycle * scale, 1e-3f);
        }

        /// <summary>
        /// Where a leg's foot is in the body's frame (unscaled; x right, z forward) at a point of its cycle, posed as the
        /// animator poses it for this speed.
        /// </summary>
        public static float3 FootAt(ref CreatureBlob b, ref CreaturePart part, float p, float speed) => FootAt(ref b, ref part, p, speed, b.Sprint(speed));

        /// <summary>FootAt for this creature as it is now (its Sprint can depend on what it's doing: a chase's gallop).</summary>
        public static float3 FootAt(ref CreatureBlob b, ref CreaturePart part, float p, in CreatureState s) =>
            FootAt(ref b, ref part, p, s.GaitSpeed, b.Sprint(s, s.GaitSpeed));

        static float3 FootAt(ref CreatureBlob b, ref CreaturePart part, float p, float speed, float sprint)
        {
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), speed);
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

        /// <summary>How near a player a creature's planted feet are kept planted through changes of pace (m).</summary>
        public const float NearFeet = 30f;

        /// <summary>How quickly a body on foot gets up to speed (m/s²): a full run in a third of a second.</summary>
        public static float Acceleration(ref CreatureBlob b) => math.max(b.RunSpeed, 0.5f) * 3f;

        /// <summary>
        /// Holds a body on foot to its acceleration from the speed it was going (GaitSpeed): setting off, or breaking
        /// into a run, it builds up rather than leaping to full speed while its legs are still posed for standing or a
        /// walk. Slowing down and stopping stay immediate. For the movers to call before they step.
        /// </summary>
        public static void Accelerate(ref CreatureState s, ref CreatureBlob b, float dt)
        {
            if (s.Fly < 0.5f)
                s.Speed = math.min(s.Speed, s.GaitSpeed + Acceleration(ref b) * dt);
        }

        /// <summary>
        /// Eases the speed its legs are posed for (GaitSpeed) towards its real speed, at most a full run in a third of a
        /// second, and keeps its planted feet planted while the pose changes: blending towards another speed's pose moves
        /// a planted foot in the body's frame, so the gait runs on by as much as sweeps it back again. (Not backwards:
        /// a foot the new pose pulls back slides that little.) Once a frame, after the gait has advanced.
        /// </summary>
        /// <param name="near">Near enough someone for its feet to be seen (CreatureBrainSystem: within NearFeet of a player);
        /// further off only the speed eases, which is all that's cheap.</param>
        public static void EaseGait(ref CreatureState s, ref CreatureBlob b, float dt, bool near = true)
        {
            var before = s.GaitSpeed;
            var rate = Acceleration(ref b) * dt;
            // (A leaping run, A-43: crossing the sprint band at a full run's acceleration swapped the run's legs for the
            // leap's poses in a tenth of a second. Through the top of the range its legs change pace more gently: about
            // a third of a second into the leap, a little quicker back out of it.)
            if (b.LeapingRun && !b.Upright && math.max(before, s.Speed) > b.RunSpeed * 0.8f * math.max(s.Scale, 0.01f))
                rate *= s.Speed > before ? 0.3f : 0.55f;
            s.GaitSpeed = before + math.clamp(s.Speed - before, -rate, rate);
            // (A wanderer's pace wavers by a hair from frame to frame, steering: too little to move a foot, and most
            // frames end here.)
            if (!near || s.Fly > 0.5f || math.abs(s.GaitSpeed - before) < 0.01f)
                return;
            float move0 = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), before),
                  move1 = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.GaitSpeed);
            float run0 = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), before),
                  run1 = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.GaitSpeed);
            float sprint0 = b.Sprint(s, before), sprint1 = b.Sprint(s, s.GaitSpeed);
            var shift = 0f;
            var planted = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var p = math.frac(s.Phase + b.LegPhase(ref part, sprint1));
                if (p < 0.03f || p > 0.47f)
                    continue;   // swinging, or just touching down or lifting off
                shift += FootZ(ref b, ref part, p, move1, run1, sprint1, 0f) - FootZ(ref b, ref part, p, move0, run0, sprint0, 0f);
                planted++;
            }
            if (planted == 0 || shift <= 0f)
                return;
            // (The phase a metre's worth of foot sweep takes at the new pose; per unit body size, as FootZ is.)
            var perMetre = StepPhase(ref b, s, s.Scale);
            s.Phase = math.frac(s.Phase + math.min(shift / planted * perMetre, 0.25f));
        }

        // ------------------------------------------------------------ solids (A-44)

        /// <summary>Tests ("-solidsoff"): creatures walk through cave rock and towers and stand on the raw heightmap at cave
        /// mouths, as before A-44, for before numbers and shots from the same build.</summary>
        public static readonly Unity.Burst.SharedStatic<int> SolidsOff = Unity.Burst.SharedStatic<int>.GetOrCreate<SolidsOffKey>();
        struct SolidsOffKey { }
        /// <summary>Tests ("-solidstats"): the brain counts what <see cref="Stats"/> holds for creatures near a player.</summary>
        public static readonly Unity.Burst.SharedStatic<int> StatsOn = Unity.Burst.SharedStatic<int>.GetOrCreate<StatsOnKey>();
        struct StatsOnKey { }
        public static readonly Unity.Burst.SharedStatic<SolidStats> Stats = Unity.Burst.SharedStatic<SolidStats>.GetOrCreate<SolidStatsKey>();
        struct SolidStatsKey { }

        /// <summary>Counters for "-solidstats" (DebugControls logs and clears them): creature-ticks near a player, moves cut
        /// by a solid, ticks stopped against one, ticks with the body more than 0.15 m into one, the deepest sink (mm),
        /// ticks floating over 0.2 m above the carved ground at a cave mouth, and solid queries made.</summary>
        public struct SolidStats
        {
            public int Samples, Contacts, Stuck, Hidden, MaxSinkMm, Floating, Queries;
        }

        /// <summary>Only creatures this close to a player (m, across the ground) test solids: further off nobody sees them.</summary>
        public const float SolidReach = 48f;

        /// <summary>
        /// The ground a land animal stands on: the carved floor where a cave entrance cuts the surface away (FloorUnder, the
        /// query the player's floor uses there), else the surface. Away from caves this is SampleHeight alone.
        /// </summary>
        public static float Ground(in TerrainSource terrain, float2 xz, float y)
        {
            var surface = terrain.SampleHeight(xz);
            if (!terrain.Caves.IsCreated || !CaveSampling.NearColumn(ref terrain.Caves.Value, xz) || terrain.MouthCut(xz, surface) >= 0f)
                return surface;
            return terrain.FloorUnder(new float3(xz.x, math.max(y, surface - 2f), xz.y), out _);
        }

        /// <summary>
        /// Whether a land animal's body at xz (feet at feetY, facing forward) would be in a solid the player is kept out of:
        /// a salt tower or iceberg (TowerWall, tested at its nose, tail and both flanks) or cave rock round an entrance
        /// (PlayerMovementSystem.SolidDistance at body height, at its middle and nose), or would step off the lip of a cave
        /// entrance into a drop deeper than its legs. Sink: how far the body is into the rock (m, 0 when clear or when
        /// only a tower or the lip stops it).
        /// </summary>
        public static bool Solid(in TerrainSource terrain, float2 xz, float2 forward, float feetY, float radius, float halfLength,
                                 float height, out float sink)
        {
            sink = 0f;
            var side = new float2(-forward.y, forward.x);
            var nose = xz + forward * (halfLength + radius * 0.5f);
            var stepUp = math.max(0.3f, height * 0.35f);
            var hit = false;
            if (terrain.TowerWall(new float3(nose.x, feetY, nose.y), stepUp, height) ||
                terrain.TowerWall(new float3(xz.x + side.x * radius, feetY, xz.y + side.y * radius), stepUp, height) ||
                terrain.TowerWall(new float3(xz.x - side.x * radius, feetY, xz.y - side.y * radius), stepUp, height) ||
                terrain.TowerWall(new float3(xz.x - forward.x * halfLength, feetY, xz.y - forward.y * halfLength), stepUp, height))
                hit = true;
            if (!terrain.Caves.IsCreated || !CaveSampling.NearColumn(ref terrain.Caves.Value, xz))
                return hit;
            var centreY = feetY + math.max(height * 0.6f, radius);
            var mid = RockDistance(terrain, new float3(xz.x, centreY, xz.y));
            var front = RockDistance(terrain, new float3(nose.x, centreY, nose.y));
            sink = math.max(0f, radius - math.min(mid, front));
            if (math.min(mid, front) < radius * 0.75f)
                hit = true;
            // (The lip of an entrance: a shallow scoop is walked into, a pit isn't stepped off.)
            var surface = terrain.SampleHeight(nose);
            if (terrain.MouthCut(nose, surface) < 0f && Ground(terrain, nose, feetY) < feetY - math.max(0.5f, height))
                hit = true;
            return hit;
        }

        /// <summary>Distance from a point to cave rock (negative inside it), as PlayerMovementSystem.SolidDistance has it
        /// under the surface; above the surface open air (an ordinary hillside is the ground's business, not a wall).</summary>
        static float RockDistance(in TerrainSource terrain, float3 p) =>
            p.y < terrain.SampleHeight(p.xz) ? -terrain.CaveDistance(p) : 100f;
    }
}
