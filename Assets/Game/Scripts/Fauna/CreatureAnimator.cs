using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Poses a creature for drawing. Layers, in order:
    ///  1. gait: every part blends its key poses by its own phase in the gait cycle (walk and run keys mixed by
    ///     speed, idle mixed in as it stops);
    ///  2. body: stands at the height its legs give it, follows the slope of the ground, bobs with each step and
    ///     rocks when bounding;
    ///  3. secondary motion: tails and ears swing out of turns on a spring; the head turns to look;
    ///  4. feet: legs are bent with two-bone IK so their feet meet the actual ground rather than a flat plane.
    /// </summary>
    public static class CreatureAnimator
    {
        const int MaxSegments = 128;

        public static void Evaluate(ref CreatureBlob b, in CreatureState s, in TerrainSource terrain,
                                    NativeArray<SegmentInstance> output, int offset)
        {
            var scale = s.Scale;
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.Speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed);

            // Body on the ground: height from the legs, pitch from the ground ahead and behind.
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var halfLength = b.BodyLength * 0.5f * scale;
            var ground = terrain.SampleHeight(s.Position.xz);
            var front = terrain.SampleHeight(s.Position.xz + forward.xz * halfLength);
            var back = terrain.SampleHeight(s.Position.xz - forward.xz * halfLength);
            var gradient = (front - back) / (2f * halfLength);   // rise per metre forward
            var pitch = math.clamp(-math.atan(gradient), -0.45f, 0.45f);

            var bobs = math.lerp(b.BobsPerCycle.x, b.BobsPerCycle.y, run);
            var bob = math.lerp(b.Bob.x, b.Bob.y, run) * move * scale * (0.5f - 0.5f * math.cos(2f * math.PI * bobs * s.Phase));
            pitch += b.RunRock * run * move * math.sin(2f * math.PI * s.Phase);

            var rootRot = math.mul(quaternion.RotateY(s.Yaw), quaternion.RotateX(pitch));
            var rootPos = new float3(s.Position.x, ground + b.StandHeight * scale - bob, s.Position.z);

            // Body.
            ref var body = ref b.Segments[0];
            output[offset] = Instance(rootPos + math.mul(rootRot, new float3(0f, 0f, -halfLength + body.RadiusStart * scale)), rootRot, ref body, scale);

            var segPos = new FixedList4096Bytes<float3>();
            var segRot = new FixedList4096Bytes<quaternion>();
            var endPos = new FixedList4096Bytes<float3>();
            var endRot = new FixedList4096Bytes<quaternion>();
            segPos.Length = math.min(b.Segments.Length, MaxSegments);
            segRot.Length = segPos.Length;

            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                var parentPos = part.Parent < 0 ? rootPos : endPos[part.Parent];
                var parentRot = part.Parent < 0 ? rootRot : endRot[part.Parent];
                var pos = parentPos + math.mul(parentRot, part.Socket * scale);
                var rot = math.mul(parentRot, part.Rest);
                var phase = math.frac(s.Phase + part.Phase);

                for (int j = 0; j < part.SegmentCount; j++)
                {
                    var seg = part.FirstSegment + j;
                    var a = b.Angles(seg, phase, move, run);
                    a += Secondary(ref part, in s, j, run);
                    rot = math.mul(rot, quaternion.EulerZXY(a));
                    if (seg < MaxSegments)
                    {
                        segPos[seg] = pos;
                        segRot[seg] = rot;
                    }
                    pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length * scale));
                }

                if (part.Role == LimbRole.Leg && part.Parent < 0 && part.SegmentCount >= 2 && part.FirstSegment + part.SegmentCount <= MaxSegments)
                {
                    // Where the ground would be under this foot if it were the flat, sloped plane the body is
                    // standing on, versus where it really is: bend the leg by the difference.
                    var along = math.dot(pos.xz - s.Position.xz, forward.xz);
                    var nominal = ground + along * gradient;
                    var actual = terrain.SampleHeight(pos.xz);
                    var reach = 0f;
                    for (int j = 0; j < part.SegmentCount; j++)
                        reach += b.Segments[part.FirstSegment + j].Length * scale;
                    var correction = math.clamp(actual - nominal, -0.45f * reach, 0.45f * reach);
                    pos += PlaceFoot(ref b, ref part, ref segPos, ref segRot, new float3(0f, correction, 0f), scale);
                }

                endPos.Add(pos);
                endRot.Add(rot);
            }

            for (int seg = 1; seg < b.Segments.Length && seg < MaxSegments; seg++)
                output[offset + seg] = Instance(segPos[seg], segRot[seg], ref b.Segments[seg], scale);
        }

        /// <summary>Spring and look offsets added to a part's joint angles.</summary>
        static float3 Secondary(ref CreaturePart part, in CreatureState s, int joint, float run)
        {
            switch (part.Role)
            {
                case LimbRole.Tail:
                    // Each joint swings a little more than the last, so the tail curls out of the turn.
                    return new float3(0f, s.Sway * part.Spring * (1f + joint * 0.5f), 0f);
                case LimbRole.Ear:
                    return new float3(-0.5f * run * part.Spring, s.Sway * part.Spring * 0.5f * part.Side, 0f);
                case LimbRole.Head:
                case LimbRole.Neck:
                    return new float3(0f, s.Look * (part.Role == LimbRole.Head ? 0.6f : 0.4f / math.max(part.SegmentCount, 1)), 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Two-bone IK on a leg's first two segments: moves the end of the second segment by delta, keeping the knee
        /// bending the way it did. The rest of the leg (foot, toes) keeps its orientation and shifts with it.
        /// Returns how far the end of the leg moved.
        /// </summary>
        static float3 PlaceFoot(ref CreatureBlob b, ref CreaturePart part,
                                ref FixedList4096Bytes<float3> segPos, ref FixedList4096Bytes<quaternion> segRot,
                                float3 delta, float scale)
        {
            int s0 = part.FirstSegment, s1 = s0 + 1;
            var l0 = b.Segments[s0].Length * scale;
            var l1 = b.Segments[s1].Length * scale;
            var hip = segPos[s0];
            var knee = segPos[s1];
            var ankle = knee + math.mul(segRot[s1], new float3(0f, 0f, l1));
            var target = ankle + delta;

            var toTarget = target - hip;
            var d = math.clamp(math.length(toTarget), math.abs(l0 - l1) + 1e-3f, (l0 + l1) * 0.999f);
            var dir = math.normalizesafe(toTarget, math.mul(segRot[s0], new float3(0f, 0f, 1f)));
            target = hip + dir * d;

            // Keep the knee in the plane it already bends in, on the same side.
            var normal = math.cross(knee - hip, ankle - knee);
            normal = math.lengthsq(normal) > 1e-10f ? math.normalize(normal) : math.mul(segRot[s0], new float3(1f, 0f, 0f));
            var cosA = math.clamp((l0 * l0 + d * d - l1 * l1) / (2f * l0 * d), -1f, 1f);
            var sinA = math.sqrt(1f - cosA * cosA);
            var perp = math.normalizesafe(math.cross(normal, dir));
            var kneeA = hip + l0 * (dir * cosA + perp * sinA);
            var kneeB = hip + l0 * (dir * cosA - perp * sinA);
            var newKnee = math.distancesq(kneeA, knee) < math.distancesq(kneeB, knee) ? kneeA : kneeB;

            segRot[s0] = math.mul(FromTo(math.normalizesafe(knee - hip), math.normalizesafe(newKnee - hip)), segRot[s0]);
            segRot[s1] = math.mul(FromTo(math.normalizesafe(ankle - knee), math.normalizesafe(target - newKnee)), segRot[s1]);
            segPos[s1] = newKnee;

            var shift = target - ankle;
            for (int j = 2; j < part.SegmentCount; j++)
                segPos[s0 + j] += shift;
            return shift;
        }

        static quaternion FromTo(float3 a, float3 b)
        {
            var w = 1f + math.dot(a, b);
            if (w < 1e-5f)
            {
                var axis = math.abs(a.x) > 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                return quaternion.AxisAngle(math.normalize(math.cross(a, axis)), math.PI);
            }
            var c = math.cross(a, b);
            return math.normalize(new quaternion(c.x, c.y, c.z, w));
        }

        static SegmentInstance Instance(float3 position, quaternion rotation, ref CreatureSegment seg, float scale) => new SegmentInstance
        {
            Position = position,
            Rotation = rotation,
            Length = seg.Length * scale,
            RadiusStart = seg.RadiusStart * scale,
            RadiusEnd = seg.RadiusEnd * scale,
            Flatten = seg.Flatten,
            Color = seg.Color
        };
    }
}
