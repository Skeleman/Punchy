using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A species flattened for Burst: every part (mirrored slots expanded into left and right copies, parents before
    /// children), every segment (index 0 is the body), and every segment's seven key poses.
    /// </summary>
    public struct CreatureBlob
    {
        public const int PoseCount = 7;   // idle, walk reach/push/lift, run reach/push/lift
        public const int Idle = 0, WalkReach = 1, WalkPush = 2, WalkLift = 3, RunReach = 4, RunPush = 5, RunLift = 6;

        public float BodyLength, RearRadius, FrontRadius, BodyFlatten;
        public float WalkSpeed, RunSpeed;
        /// <summary>Distance the body travels per gait cycle, walking and running (from the key poses).</summary>
        public float WalkStride, RunStride;
        /// <summary>Height of the body's centre above the ground when standing (from the idle pose).</summary>
        public float StandHeight;
        public float2 Bob, BobsPerCycle;
        public float RunRock;
        public float WanderRadius, FleeDistance, TurnRate;
        public float2 IdleTime, Size;

        public BlobArray<CreaturePart> Parts;
        public BlobArray<CreatureSegment> Segments;
        public BlobArray<float3> Poses;   // Segments.Length × PoseCount, radians

        /// <summary>Blend of a part's key poses for one segment at gait phase p (0..1).</summary>
        public float3 Angles(int segment, float p, float move, float run)
        {
            var i = segment * PoseCount;
            var walk = Cycle(Poses[i + WalkReach], Poses[i + WalkPush], Poses[i + WalkLift], p);
            var running = Cycle(Poses[i + RunReach], Poses[i + RunPush], Poses[i + RunLift], p);
            return math.lerp(Poses[i + Idle], math.lerp(walk, running, run), move);
        }

        /// <summary>
        /// A gait cycle from three keys: the first half sweeps steadily from reach to push (the foot planted, the
        /// body passing over it); the second half eases back to reach with the lift added in the middle (the foot
        /// carried forward off the ground).
        /// </summary>
        public static float3 Cycle(float3 reach, float3 push, float3 lift, float p)
        {
            if (p < 0.5f)
                return math.lerp(reach, push, p * 2f);
            var t = (p - 0.5f) * 2f;
            return math.lerp(push, reach, math.smoothstep(0f, 1f, t)) + lift * math.sin(t * math.PI);
        }
    }

    public struct CreaturePart
    {
        /// <summary>-1 = the body, otherwise an earlier part this one hangs from the end of.</summary>
        public int Parent;
        /// <summary>Attachment point in metres, in the parent's frame (body frame for the body).</summary>
        public float3 Socket;
        public quaternion Rest;
        public float Phase;
        public int FirstSegment, SegmentCount;
        public LimbRole Role;
        public float Spring;
        /// <summary>+1, or -1 for the mirror image (yaw and roll flipped).</summary>
        public float Side;
    }

    public struct CreatureSegment
    {
        public float Length, RadiusStart, RadiusEnd, Flatten;
        public float4 Color;   // linear
    }

    /// <summary>Which species a creature is. Blobs are built and owned by FaunaDirector.</summary>
    public struct CreatureBody : IComponentData
    {
        public BlobAssetReference<CreatureBlob> Blob;
        public int Species;
        public int SegmentCount;
    }

    public enum CreatureMode : byte
    {
        Idle,
        Wander,
        Flee,
        /// <summary>Under outside control: keeps its heading and speed, ignores the player (scripts, tests).</summary>
        Puppet
    }

    /// <summary>A creature's position, heading, gait and what it's up to.</summary>
    public struct CreatureState : IComponentData
    {
        public float3 Position;
        /// <summary>Heading in radians; forward is (sin, 0, cos).</summary>
        public float Yaw;
        public float Speed;
        public float Phase;
        public float YawRate;
        /// <summary>Spring state for tails and ears (lags the turns).</summary>
        public float Sway;
        /// <summary>Head turn relative to the body, radians.</summary>
        public float Look;
        public float Scale;
        public float2 Target;
        public float Timer;
        public CreatureMode Mode;
        public uint Seed;
    }

    /// <summary>One segment as the creature shader draws it (a tapered capsule). Must match Creature.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SegmentInstance
    {
        public float3 Position;
        public float Length;
        public quaternion Rotation;
        public float RadiusStart, RadiusEnd, Flatten, Pad;
        public float4 Color;
    }

    /// <summary>Material and mesh for drawing creatures (a managed singleton set up by FaunaDirector).</summary>
    public class FaunaRenderSettings : IComponentData
    {
        public Material Material;
        public Mesh Capsule;
    }

    public static class CreatureBlobBuilder
    {
        struct Expanded
        {
            public LimbSlot Slot;
            public bool Mirrored;
            public int Parent;
        }

        public static BlobAssetReference<CreatureBlob> Build(CreatureSpecies species)
        {
            // Expand slots: mirrored slots become left and right copies; children follow every copy of their parent.
            var parts = new List<Expanded>();
            var copies = new List<List<int>>();
            for (int i = 0; i < species.Parts.Count; i++)
            {
                var slot = species.Parts[i];
                var mine = new List<int>();
                copies.Add(mine);
                if (slot.Limb == null)
                    continue;
                var parents = slot.Parent >= 0 && slot.Parent < i ? copies[slot.Parent] : new List<int> { -1 };
                foreach (var parent in parents)
                {
                    var parentMirrored = parent >= 0 && parts[parent].Mirrored;
                    mine.Add(parts.Count);
                    parts.Add(new Expanded { Slot = slot, Mirrored = parentMirrored, Parent = parent });
                    if (slot.Mirror)
                    {
                        mine.Add(parts.Count);
                        parts.Add(new Expanded { Slot = slot, Mirrored = !parentMirrored, Parent = parent, });
                    }
                }
            }

            var segmentCount = 1;
            foreach (var p in parts)
                segmentCount += p.Slot.Limb.Segments.Count;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CreatureBlob>();
            root.BodyLength = species.BodyLength;
            root.FrontRadius = species.BodyRadius;
            root.RearRadius = species.BodyRadius * species.BodyTaper;
            root.BodyFlatten = species.BodyFlatten;
            root.WalkSpeed = species.WalkSpeed;
            root.RunSpeed = math.max(species.RunSpeed, species.WalkSpeed * 1.3f);
            root.Bob = new float2(species.Bob.x, species.Bob.y);
            root.BobsPerCycle = new float2(species.BobsPerCycle.x, species.BobsPerCycle.y);
            root.RunRock = math.radians(species.RunRock);
            root.WanderRadius = species.WanderRadius;
            root.FleeDistance = species.FleeDistance;
            root.TurnRate = math.radians(species.TurnRate);
            root.IdleTime = new float2(species.IdleTime.x, species.IdleTime.y);
            root.Size = new float2(species.Size.x, species.Size.y);

            var partArray = builder.Allocate(ref root.Parts, parts.Count);
            var segArray = builder.Allocate(ref root.Segments, segmentCount);
            var poseArray = builder.Allocate(ref root.Poses, segmentCount * CreatureBlob.PoseCount);

            var baseColor = (Vector4)species.Base.linear;
            var accent = (Vector4)species.Accent.linear;
            segArray[0] = new CreatureSegment
            {
                Length = math.max(0f, species.BodyLength - root.RearRadius - root.FrontRadius),
                RadiusStart = root.RearRadius,
                RadiusEnd = root.FrontRadius,
                Flatten = species.BodyFlatten,
                Color = baseColor
            };

            var cursor = 1;
            for (int i = 0; i < parts.Count; i++)
            {
                var e = parts[i];
                var slot = e.Slot;
                var limb = slot.Limb;
                var side = e.Mirrored ? -1f : 1f;
                var socket = (float3)slot.Socket;
                if (e.Parent < 0)
                {
                    // Body units → metres: across the radius at that point along the body, along the half-length.
                    var radius = math.lerp(root.RearRadius, root.FrontRadius, socket.z * 0.5f + 0.5f);
                    socket = new float3(socket.x * radius, socket.y * radius * species.BodyFlatten, socket.z * species.BodyLength * 0.5f);
                }
                socket.x *= side;
                var dir = (float3)slot.Direction;
                partArray[i] = new CreaturePart
                {
                    Parent = e.Parent,
                    Socket = socket,
                    Rest = Rotation(dir, side),
                    Phase = math.frac(slot.Phase + (e.Mirrored && slot.Mirror ? slot.MirrorPhase : 0f)),
                    FirstSegment = cursor,
                    SegmentCount = limb.Segments.Count,
                    Role = limb.Role,
                    Spring = limb.Spring,
                    Side = side
                };

                var poses = new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };
                for (int s = 0; s < limb.Segments.Count; s++)
                {
                    var seg = limb.Segments[s];
                    var color = seg.UseFixedColor ? (Vector4)seg.FixedColor.linear : Vector4.Lerp(baseColor, accent, seg.Accent);
                    segArray[cursor] = new CreatureSegment
                    {
                        Length = seg.Length * slot.Scale,
                        RadiusStart = seg.RadiusStart * slot.Scale,
                        RadiusEnd = seg.RadiusEnd * slot.Scale,
                        Flatten = seg.Flatten,
                        Color = color
                    };
                    for (int k = 0; k < CreatureBlob.PoseCount; k++)
                    {
                        var a = math.radians((float3)poses[k][s]);
                        poseArray[cursor * CreatureBlob.PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                    }
                    cursor++;
                }
            }

            var blob = builder.CreateBlobAssetReference<CreatureBlob>(Allocator.Persistent);
            builder.Dispose();
            MeasureGait(ref blob.Value);
            return blob;
        }

        /// <summary>Rest orientation from degrees; the mirror image flips yaw and roll.</summary>
        public static quaternion Rotation(float3 degrees, float side)
        {
            var r = math.radians(degrees);
            return quaternion.EulerZXY(r.x, r.y * side, r.z * side);
        }

        /// <summary>
        /// Works out from the key poses how high the body stands and how far it travels per gait cycle, so the feet
        /// don't slide: in a cycle a planted foot sweeps back from reach to push while the body passes over it, so
        /// the body moves twice that sweep per cycle.
        /// </summary>
        static void MeasureGait(ref CreatureBlob b)
        {
            float stand = 0f, walk = 0f, run = 0f;
            var legs = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0)
                    continue;
                stand += -Foot(ref b, ref part, CreatureBlob.Idle).y;
                walk += math.abs(Foot(ref b, ref part, CreatureBlob.WalkReach).z - Foot(ref b, ref part, CreatureBlob.WalkPush).z) * 2f;
                run += math.abs(Foot(ref b, ref part, CreatureBlob.RunReach).z - Foot(ref b, ref part, CreatureBlob.RunPush).z) * 2f;
                legs++;
            }
            b.StandHeight = legs > 0 ? stand / legs : b.FrontRadius * b.BodyFlatten;
            b.WalkStride = legs > 0 ? math.max(walk / legs, 0.05f) : 1f;
            b.RunStride = legs > 0 ? math.max(run / legs, 0.05f) : 1.5f;
        }

        /// <summary>End of a body-attached part's chain in the body frame, at one key pose.</summary>
        static float3 Foot(ref CreatureBlob b, ref CreaturePart part, int pose)
        {
            var pos = part.Socket;
            var rot = part.Rest;
            for (int s = 0; s < part.SegmentCount; s++)
            {
                var seg = part.FirstSegment + s;
                var a = b.Poses[seg * CreatureBlob.PoseCount + pose];
                rot = math.mul(rot, quaternion.EulerZXY(a));
                pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
            }
            // The last segment's rounded end reaches a little further down.
            return pos + new float3(0f, -b.Segments[part.FirstSegment + part.SegmentCount - 1].RadiusEnd, 0f);
        }
    }
}
