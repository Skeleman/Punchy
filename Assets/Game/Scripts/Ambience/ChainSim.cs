using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-178: the one local chain sim (rule 33) for things that hang and swing: cave tendrils now, later vines, lanterns,
    /// chimes, curtains and rope bridges. Verlet points on a chain pinned at its anchor (and optionally at its far end, for
    /// a bridge), held to their segment length, pulled by gravity, nudged by a slow sway and pushed out of spheres (players,
    /// creatures). Cosmetic and local: every machine swings its own copy and nothing goes over the network.
    /// Use: each frame call Complete(), read Points / move anchors / SetPushers, then Schedule(dt) so the step runs while
    /// the rest of the frame does; Add and Remove complete the running step first.
    /// </summary>
    public sealed class ChainSim : IDisposable
    {
        /// <summary>How one chain moves (per chain, so a lantern and a vine can share a sim).</summary>
        public struct Settings
        {
            /// <summary>Gravity in m/s^2 (down is -y).</summary>
            public float Gravity;
            /// <summary>Velocity kept per step at 60 Hz (0..1).</summary>
            public float Damping;
            /// <summary>Peak sideways sway acceleration (m/s^2) and how fast it turns (rad/s).</summary>
            public float Sway, SwayRate;
            /// <summary>Pin the last point to End too (a bridge or a strung line).</summary>
            public bool PinEnd;

            public static Settings Hanging => new Settings { Gravity = 9.8f, Damping = 0.96f, Sway = 0.25f, SwayRate = 0.7f };
        }

        struct Chain
        {
            public float3 Anchor, End;
            public float Segment, Phase;
            public int Points;
            public Settings Settings;
            public bool Live;
        }

        public readonly int MaxPoints;
        NativeArray<float3> points, previous;
        NativeArray<Chain> chains;
        NativeList<float4> pushers;
        NativeList<int> free;
        JobHandle running;
        int used;
        float time;

        public ChainSim(int maxPoints, int capacity = 64)
        {
            MaxPoints = math.max(2, maxPoints);
            points = new NativeArray<float3>(capacity * MaxPoints, Allocator.Persistent);
            previous = new NativeArray<float3>(capacity * MaxPoints, Allocator.Persistent);
            chains = new NativeArray<Chain>(capacity, Allocator.Persistent);
            pushers = new NativeList<float4>(16, Allocator.Persistent);
            free = new NativeList<int>(capacity, Allocator.Persistent);
        }

        /// <summary>Live chains.</summary>
        public int Count { get; private set; }

        /// <summary>A chain of `count` points and `length` metres hanging straight down from anchor (or strung to end).</summary>
        public int Add(float3 anchor, float length, int count, Settings settings, float phase = 0f, float3 end = default)
        {
            Complete();
            count = math.clamp(count, 2, MaxPoints);
            int id;
            if (free.Length > 0)
            {
                id = free[free.Length - 1];
                free.RemoveAt(free.Length - 1);
            }
            else
            {
                if (used == chains.Length)
                    Grow(chains.Length * 2);
                id = used++;
            }
            var to = settings.PinEnd ? end : anchor - new float3(0f, length, 0f);
            for (int i = 0; i < count; i++)
                points[id * MaxPoints + i] = previous[id * MaxPoints + i] = math.lerp(anchor, to, i / (count - 1f));
            chains[id] = new Chain
            {
                Anchor = anchor, End = end, Segment = length / (count - 1), Phase = phase, Points = count, Settings = settings, Live = true
            };
            Count++;
            return id;
        }

        public void Remove(int id)
        {
            Complete();
            if (id < 0 || id >= used || !chains[id].Live)
                return;
            var c = chains[id];
            c.Live = false;
            chains[id] = c;
            free.Add(id);
            Count--;
        }

        /// <summary>Moves a chain's pins (call between Complete and Schedule).</summary>
        public void SetAnchor(int id, float3 anchor, float3 end = default)
        {
            var c = chains[id];
            c.Anchor = anchor;
            c.End = end;
            chains[id] = c;
        }

        /// <summary>Spheres (xyz centre, w radius) the points are pushed out of in the next step.</summary>
        public void SetPushers(System.Collections.Generic.List<float4> spheres)
        {
            pushers.Clear();
            foreach (var s in spheres)
                pushers.Add(s);
        }

        /// <summary>A chain's points, root first (valid until the next Schedule).</summary>
        public NativeSlice<float3> Points(int id) => new NativeSlice<float3>(points, id * MaxPoints, chains[id].Points);

        public void Complete() => running.Complete();

        public void Schedule(float dt)
        {
            Complete();
            if (Count == 0 || dt <= 0f)
                return;
            dt = math.min(dt, 1f / 20f);
            time += dt;
            running = new StepJob
            {
                Points = points, Previous = previous, Chains = chains, Pushers = pushers.AsArray(), MaxPoints = MaxPoints,
                Dt = dt, Time = time
            }.Schedule(used, 8);
            JobHandle.ScheduleBatchedJobs();
        }

        void Grow(int capacity)
        {
            var p = new NativeArray<float3>(capacity * MaxPoints, Allocator.Persistent);
            var q = new NativeArray<float3>(capacity * MaxPoints, Allocator.Persistent);
            var c = new NativeArray<Chain>(capacity, Allocator.Persistent);
            NativeArray<float3>.Copy(points, p, points.Length);
            NativeArray<float3>.Copy(previous, q, previous.Length);
            NativeArray<Chain>.Copy(chains, c, chains.Length);
            points.Dispose();
            previous.Dispose();
            chains.Dispose();
            points = p;
            previous = q;
            chains = c;
        }

        public void Dispose()
        {
            running.Complete();
            if (points.IsCreated)
            {
                points.Dispose();
                previous.Dispose();
                chains.Dispose();
                pushers.Dispose();
                free.Dispose();
            }
        }

        [BurstCompile]
        struct StepJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<float3> Points, Previous;
            [ReadOnly] public NativeArray<Chain> Chains;
            [ReadOnly] public NativeArray<float4> Pushers;
            public int MaxPoints;
            public float Dt, Time;

            public void Execute(int id)
            {
                var c = Chains[id];
                if (!c.Live)
                    return;
                var s = c.Settings;
                var b = id * MaxPoints;
                var n = c.Points;
                var keep = math.pow(s.Damping, Dt * 60f);
                var t = Time * s.SwayRate + c.Phase;
                var accel = new float3(math.sin(t) + 0.4f * math.sin(t * 2.3f + 1f), 0f, math.cos(t * 0.8f + 2f)) * s.Sway -
                            new float3(0f, s.Gravity, 0f);
                var last = s.PinEnd ? n - 1 : n;
                for (int i = 1; i < last; i++)
                {
                    var p = Points[b + i];
                    var v = (p - Previous[b + i]) * keep;
                    Previous[b + i] = p;
                    Points[b + i] = p + v + accel * (Dt * Dt);
                }
                Points[b] = c.Anchor;
                if (s.PinEnd)
                    Points[b + n - 1] = c.End;
                for (int k = 0; k < Pushers.Length; k++)
                {
                    var sphere = Pushers[k];
                    for (int i = 1; i < last; i++)
                    {
                        var d = Points[b + i] - sphere.xyz;
                        var l2 = math.lengthsq(d);
                        if (l2 < sphere.w * sphere.w)
                            Points[b + i] = sphere.xyz + (l2 > 1e-8f ? d * math.rsqrt(l2) : new float3(1f, 0f, 0f)) * sphere.w;
                    }
                }
                for (int iter = 0; iter < 4; iter++)
                    for (int i = 0; i < n - 1; i++)
                    {
                        var a = Points[b + i];
                        var e = Points[b + i + 1];
                        var d = e - a;
                        var l = math.length(d);
                        if (l < 1e-6f)
                            continue;
                        var fix = d * ((l - c.Segment) / l);
                        var pinA = i == 0;
                        var pinE = s.PinEnd && i + 1 == n - 1;
                        if (pinA && pinE)
                            continue;
                        if (pinA)
                            Points[b + i + 1] = e - fix;
                        else if (pinE)
                            Points[b + i] = a + fix;
                        else
                        {
                            Points[b + i] = a + fix * 0.5f;
                            Points[b + i + 1] = e - fix * 0.5f;
                        }
                    }
            }
        }
    }
}
