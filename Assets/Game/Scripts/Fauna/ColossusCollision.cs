using System.Collections.Generic;
using Punchy.Flora;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>One upright cylinder of a colossus's body: its foot (world), radius and height (m).</summary>
    public struct ColossusCylinder
    {
        public float3 Base;
        public float Radius, Height;
    }

    /// <summary>
    /// W-101 row 4 (#40): the colossi are solid. Each colossus (StoneColossi's slots, the Dunes colossus) is turned, once
    /// when it is built, into upright cylinders: its meshes are sampled into a grid of 3 m columns, each spanning the
    /// lowest to the highest point of the body over it, so an arm held out overhead or a floating golem rock leaves
    /// room underneath. The rolling salt crystal's ball is one more cylinder that follows the ball.
    /// The player gets them through the flora obstacle buffer (FloraSystem.UpdateObstacles adds the ones in reach, so
    /// player movement slides round them like trunks); creatures are pushed out by <see cref="ColossusPushSystem"/>
    /// after their brains move them. Seed-derived like the colossi themselves, so every machine has the same columns.
    /// W-105 row 6: the columns are kept relative to the colossus's root (its spot and heading), and follow it when the
    /// root moves, so the walking colossi (ColossusWalker) are solid where they are, not where they were built.
    /// </summary>
    public static class ColossusCollision
    {
        /// <summary>The grid the meshes are sampled into (m); each column's radius covers its cell.</summary>
        public const float Cell = 3f;
        const float ColumnRadius = Cell * 0.75f;

        class Group
        {
            public string Name;
            public Transform Root, Moving;
            public float MovingRadius;
            public float2 Centre;
            public float Reach;
            public readonly List<ColossusCylinder> Columns = new List<ColossusCylinder>();
            // W-105 row 6: the columns and centre in the root's frame (its spot, turned by its heading), and the root's
            // pose they were last laid at; Moves counts each re-lay, so readers know to copy them again.
            public readonly List<ColossusCylinder> Local = new List<ColossusCylinder>();
            public float2 LocalCentre;
            public Vector3 PosedAt;
            public Quaternion PosedAs;
            public int Moves, MovesSeen;
        }

        /// <summary>The root's heading only (a walker's sway and lean leave its columns upright).</summary>
        static quaternion Heading(Quaternion rotation)
        {
            var f = rotation * Vector3.forward;
            return quaternion.RotateY(math.atan2(f.x, f.z));
        }

        /// <summary>
        /// Lays the group's columns again where its root now stands, if the root has moved since (a walking colossus).
        /// No allocation: the world columns are rewritten in place. True when they moved.
        /// </summary>
        static bool Follow(Group g)
        {
            if (g.Root == null)
                return false;
            var at = g.Root.position;
            var turn = g.Root.rotation;
            if (at == g.PosedAt && turn == g.PosedAs)
                return false;
            g.PosedAt = at;
            g.PosedAs = turn;
            var q = Heading(turn);
            var origin = (float3)at;
            for (int i = 0; i < g.Local.Count; i++)
            {
                var c = g.Local[i];
                c.Base = origin + math.mul(q, c.Base);
                g.Columns[i] = c;
            }
            g.Centre = origin.xz + math.mul(q, new float3(g.LocalCentre.x, 0f, g.LocalCentre.y)).xz;
            g.Moves++;
            return true;
        }

        static readonly Dictionary<int, Group> groups = new Dictionary<int, Group>();

        /// <summary>Forgets a slot (before it is placed again, or when it finds no room).</summary>
        public static void Clear(int slot)
        {
            if (groups.Remove(slot))
                version++;
        }

        /// <summary>
        /// Samples the colossus under <paramref name="root"/> into columns for <paramref name="slot"/> (replacing what the
        /// slot had). <paramref name="moving"/>, if any, is left out of the columns and follows its transform instead, as
        /// a sphere of <paramref name="movingRadius"/>; parts named in <paramref name="skip"/> (crumbs on the ground) are
        /// left out altogether.
        /// </summary>
        public static void Register(int slot, Transform root, string name, Transform moving = null, float movingRadius = 0f, params string[] skip)
        {
            var g = new Group { Name = name, Root = root, Moving = moving, MovingRadius = movingRadius };
            var cells = new Dictionary<int2, float2>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || moving != null && filter.transform.IsChildOf(moving) || System.Array.IndexOf(skip, filter.name) >= 0)
                    continue;
                var mesh = filter.sharedMesh;
                var toWorld = filter.transform.localToWorldMatrix;
                mesh.GetVertices(verts);
                for (int i = 0; i < verts.Count; i++)
                    verts[i] = toWorld.MultiplyPoint3x4(verts[i]);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    mesh.GetTriangles(tris, sub);
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                        SampleTriangle(cells, verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]]);
                }
            }
            var sum = float2.zero;
            foreach (var kv in cells)
                sum += ((float2)kv.Key + 0.5f) * Cell;
            g.Centre = cells.Count > 0 ? sum / cells.Count : ((float3)root.position).xz;
            foreach (var kv in cells)
            {
                var xz = ((float2)kv.Key + 0.5f) * Cell;
                g.Columns.Add(new ColossusCylinder { Base = new float3(xz.x, kv.Value.x, xz.y), Radius = ColumnRadius, Height = kv.Value.y - kv.Value.x });
                g.Reach = math.max(g.Reach, math.distance(xz, g.Centre) + ColumnRadius);
            }
            if (moving != null)
                g.Reach = 0f;   // (The ball's own circle is checked on its own: it may roll anywhere.)
            // W-105 row 6: the same columns in the root's frame, to follow it from here.
            g.PosedAt = root.position;
            g.PosedAs = root.rotation;
            var back = math.inverse(Heading(g.PosedAs));
            var from = (float3)g.PosedAt;
            foreach (var c in g.Columns)
                g.Local.Add(new ColossusCylinder { Base = math.mul(back, c.Base - from), Radius = c.Radius, Height = c.Height });
            g.LocalCentre = math.mul(back, new float3(g.Centre.x - from.x, 0f, g.Centre.y - from.z)).xz;
            groups[slot] = g;
            version++;
            Debug.Log($"ColossusCollision: {name} solid, {g.Columns.Count} columns of {Cell:F0} m within {g.Reach:F0} m of ({g.Centre.x:F0}, {g.Centre.y:F0}){(moving != null ? $", plus a rolling ball of {movingRadius:F0} m" : "")}");
        }

        // Every point of the triangle no more than half a cell apart marks its cell's lowest and highest point.
        static void SampleTriangle(Dictionary<int2, float2> cells, Vector3 a, Vector3 b, Vector3 c)
        {
            var longest = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
            var n = Mathf.Clamp(Mathf.CeilToInt(longest / (Cell * 0.5f)), 1, 64);
            for (int i = 0; i <= n; i++)
            for (int j = 0; i + j <= n; j++)
            {
                var p = a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n);
                var cell = (int2)math.floor(new float2(p.x, p.z) / Cell);
                cells[cell] = cells.TryGetValue(cell, out var span) ? new float2(math.min(span.x, p.y), math.max(span.y, p.y)) : new float2(p.y, p.y);
            }
        }

        /// <summary>One line per solid colossus (slot, name, columns, circle), for probes.</summary>
        public static string Summary()
        {
            var s = new System.Text.StringBuilder();
            foreach (var kv in groups)
                s.Append($"{kv.Key} {kv.Value.Name}: {kv.Value.Columns.Count} columns, centre ({kv.Value.Centre.x:F0}, {kv.Value.Centre.y:F0}), reach {kv.Value.Reach:F0} m, shown {Shown(kv.Value)}; ");
            return s.ToString();
        }

        static bool Shown(Group g) => g.Root != null && g.Root.gameObject.activeInHierarchy;

        static ColossusCylinder Ball(Group g)
        {
            var at = (float3)g.Moving.position;
            return new ColossusCylinder { Base = at - new float3(0f, g.MovingRadius, 0f), Radius = g.MovingRadius, Height = g.MovingRadius * 2f };
        }

        /// <summary>Whether a moving part (the salt ball) is within <paramref name="range"/> of xz: the obstacle buffer
        /// near it has to be rebuilt as it rolls.</summary>
        public static bool MovingNear(float2 xz, float range)
        {
            var near = false;
            foreach (var g in groups.Values)
            {
                if (!Shown(g))
                    continue;
                if (g.Moving != null && math.distance(((float3)g.Moving.position).xz, xz) < range + g.MovingRadius)
                    near = true;
                // W-105 row 6: a walking colossus in reach that has moved since the buffer was last filled.
                Follow(g);
                if (g.Moves != g.MovesSeen && math.distance(g.Centre, xz) < range + g.Reach)
                    near = true;
            }
            return near;
        }

        /// <summary>The flora obstacle buffer gets every column within <paramref name="range"/> of xz (FloraSystem).</summary>
        public static void AddNear(DynamicBuffer<FloraObstacle> buffer, float2 xz, float range)
        {
            foreach (var g in groups.Values)
            {
                if (!Shown(g))
                    continue;
                Follow(g);
                g.MovesSeen = g.Moves;
                if (g.Moving != null)
                {
                    var ball = Ball(g);
                    if (math.distance(ball.Base.xz, xz) < range + ball.Radius)
                        buffer.Add(new FloraObstacle { Position = ball.Base, Radius = ball.Radius, Height = ball.Height });
                }
                if (math.distance(g.Centre, xz) > range + g.Reach && g.Moving == null)
                    continue;
                foreach (var c in g.Columns)
                    if (math.distance(c.Base.xz, xz) < range + c.Radius)
                        buffer.Add(new FloraObstacle { Position = c.Base, Radius = c.Radius, Height = c.Height });
            }
        }

        /// <summary>
        /// This frame's columns, for a job: every column (columns), and per colossus its circle (xz, reach) and range
        /// in columns. A moving ball goes in as a one-column colossus of its own. Empty arrays when there are none.
        /// </summary>
        public static void Snapshot(out NativeArray<ColossusCylinder> columns, out NativeArray<float3> circles, out NativeArray<int2> ranges, Allocator allocator)
        {
            // (Gathered again only when a colossus is registered, shown or hidden; the balls are moved every frame.)
            var shown = 0;
            foreach (var g in groups.Values)
                shown = shown * 31 + (Shown(g) ? 1 : 2);
            if (shown != cachedShown || version != cachedVersion)
            {
                cachedShown = shown;
                cachedVersion = version;
                allCache.Clear(); circleCache.Clear(); rangeCache.Clear(); balls.Clear(); walkers.Clear();
                foreach (var g in groups.Values)
                {
                    if (!Shown(g))
                        continue;
                    Follow(g);
                    if (g.Columns.Count > 0)
                    {
                        walkers.Add((g, circleCache.Count, allCache.Count, g.Moves));
                        circleCache.Add(new float3(g.Centre, g.Reach));
                        rangeCache.Add(new int2(allCache.Count, g.Columns.Count));
                        allCache.AddRange(g.Columns);
                    }
                    if (g.Moving != null)
                    {
                        balls.Add((g, circleCache.Count, allCache.Count));
                        circleCache.Add(default);
                        rangeCache.Add(new int2(allCache.Count, 1));
                        allCache.Add(default);
                    }
                }
            }
            // W-105 row 6: a colossus that has walked since is copied over its old columns (same count, same place).
            for (int w = 0; w < walkers.Count; w++)
            {
                var (g, circle, column, moves) = walkers[w];
                Follow(g);
                if (g.Moves == moves)
                    continue;
                walkers[w] = (g, circle, column, g.Moves);
                circleCache[circle] = new float3(g.Centre, g.Reach);
                for (int i = 0; i < g.Columns.Count; i++)
                    allCache[column + i] = g.Columns[i];
            }
            foreach (var (g, circle, column) in balls)
            {
                var ball = Ball(g);
                circleCache[circle] = new float3(ball.Base.xz, ball.Radius);
                allCache[column] = ball;
            }
            columns = new NativeArray<ColossusCylinder>(allCache.Count, allocator, NativeArrayOptions.UninitializedMemory);
            circles = new NativeArray<float3>(circleCache.Count, allocator, NativeArrayOptions.UninitializedMemory);
            ranges = new NativeArray<int2>(rangeCache.Count, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < allCache.Count; i++) columns[i] = allCache[i];
            for (int i = 0; i < circleCache.Count; i++) { circles[i] = circleCache[i]; ranges[i] = rangeCache[i]; }
        }

        static int version, cachedVersion = -1, cachedShown;
        static readonly List<ColossusCylinder> allCache = new List<ColossusCylinder>();
        static readonly List<float3> circleCache = new List<float3>();
        static readonly List<int2> rangeCache = new List<int2>();
        static readonly List<(Group, int, int)> balls = new List<(Group, int, int)>();
        static readonly List<(Group, int, int, int)> walkers = new List<(Group, int, int, int)>();

        /// <summary>Logs the frames the push system moves creatures (set from a probe; off by default, as reading the
        /// count waits for the creature jobs).</summary>
        public static bool LogPushes;

        /// <summary>The colossus whose column the point (feet at y, body of this radius) is against, within a gap of
        /// <paramref name="slack"/> (m); null when none. For the push log.</summary>
        public static string Against(float3 at, float radius, float slack, out float gap)
        {
            var best = float.MaxValue;
            string name = null;
            foreach (var g in groups.Values)
            {
                if (!Shown(g))
                    continue;
                Follow(g);
                void Check(in ColossusCylinder c)
                {
                    if (at.y + 0.5f > c.Base.y + c.Height || at.y + 1.8f < c.Base.y)
                        return;
                    var d = math.distance(at.xz, c.Base.xz) - c.Radius - radius;
                    if (d < slack && d < best)
                    {
                        best = d;
                        name = g.Name;
                    }
                }
                if (g.Moving != null)
                    Check(Ball(g));
                if (g.Moving == null && math.distance(g.Centre, at.xz) > g.Reach + radius + slack)
                    continue;
                foreach (var c in g.Columns)
                    Check(c);
            }
            gap = best;
            return name;
        }
    }

    /// <summary>
    /// A-77: the colossi's columns (as ColossusCollision.Snapshot) handed from ColossusPushSystem to the next frame's
    /// CreatureBrainSystem, whose Burst update can't take the snapshot itself: the brain takes them (leaving this empty)
    /// and frees them after its job; a set it never took is freed when the next one is made.
    /// </summary>
    public struct ColossusSnapshot : IComponentData
    {
        public NativeArray<ColossusCylinder> Columns;
        public NativeArray<float3> Circles;
        public NativeArray<int2> Ranges;

        public void Dispose()
        {
            if (Columns.IsCreated) Columns.Dispose();
            if (Circles.IsCreated) Circles.Dispose();
            if (Ranges.IsCreated) Ranges.Dispose();
        }
    }

    /// <summary>
    /// W-101 row 4 (#40): pushes creatures back out of the colossi's columns after their brains move them, sideways to
    /// the nearest edge, so a walker slides along a colossus's flank and never through it. A body whose feet are above a
    /// column's top less a step, or whose head is under its foot, is left alone (a bird overhead, a beast under an arm).
    /// Logs the first pushes, and the player coming up against a colossus (the player itself slides round the same
    /// columns in its own movement, through the flora obstacle buffer).
    /// </summary>
    // Runs where the animals are simulated, like CreatureBrainSystem.
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    public partial struct ColossusPushSystem : ISystem
    {
        static int pushLogs;
        static double nextPlayerLog;
        static string lastAgainst;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureState>();
            state.EntityManager.CreateSingleton(new ColossusSnapshot());
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingletonRW<ColossusSnapshot>(out var snap))
            {
                snap.ValueRW.Dispose();
                snap.ValueRW = default;
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            ColossusCollision.Snapshot(out var columns, out var circles, out var ranges, Allocator.TempJob);
            // A-77: a copy for the next frame's brain (CreatureBrainSystem steers round them); last frame's, if it went
            // untaken, is freed first.
            if (SystemAPI.TryGetSingletonRW<ColossusSnapshot>(out var snap))
            {
                snap.ValueRW.Dispose();
                snap.ValueRW = circles.Length == 0 ? default : new ColossusSnapshot
                {
                    Columns = new NativeArray<ColossusCylinder>(columns, Allocator.TempJob),
                    Circles = new NativeArray<float3>(circles, Allocator.TempJob),
                    Ranges = new NativeArray<int2>(ranges, Allocator.TempJob)
                };
            }
            if (circles.Length == 0)
            {
                columns.Dispose(); circles.Dispose(); ranges.Dispose();
                return;
            }
            // The player against a colossus: one line each time it comes up against one (and at most once a second).
            foreach (var player in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Punchy.Player.PlayerTag>())
            {
                var name = ColossusCollision.Against(player.ValueRO.Position, 0.35f, 0.05f, out var gap);
                if (name != null && name != lastAgainst && Time.realtimeSinceStartupAsDouble >= nextPlayerLog)
                {
                    nextPlayerLog = Time.realtimeSinceStartupAsDouble + 1.0;
                    var p = player.ValueRO.Position;
                    Debug.Log($"ColossusCollision: player stopped against the {name} at ({p.x:F1}, {p.y:F1}, {p.z:F1}), gap {gap:F2} m");
                }
                lastAgainst = name;
            }
            var pushed = new NativeArray<int>(1, Allocator.TempJob);
            state.Dependency = new PushJob { Columns = columns, Circles = circles, Ranges = ranges, Pushed = pushed }.Schedule(state.Dependency);
            if (ColossusCollision.LogPushes)
            {
                state.Dependency.Complete();
                if (pushed[0] > 0 && pushLogs++ < 20)
                    Debug.Log($"ColossusCollision: pushed {pushed[0]} creature(s) out of a colossus this frame");
            }
            columns.Dispose(state.Dependency);
            circles.Dispose(state.Dependency);
            ranges.Dispose(state.Dependency);
            pushed.Dispose(state.Dependency);
        }

        [BurstCompile]
        [WithNone(typeof(PlayerAvatar), typeof(RemoteAvatar), typeof(PosedForTest))]
        partial struct PushJob : IJobEntity
        {
            [ReadOnly] public NativeArray<ColossusCylinder> Columns;
            [ReadOnly] public NativeArray<float3> Circles;
            [ReadOnly] public NativeArray<int2> Ranges;
            public NativeArray<int> Pushed;

            void Execute(ref CreatureState s)
            {
                var radius = 0.4f * s.Scale;
                var xz = s.Position.xz;
                var moved = false;
                for (int g = 0; g < Circles.Length; g++)
                {
                    if (math.distancesq(xz, Circles[g].xy) > (Circles[g].z + radius) * (Circles[g].z + radius))
                        continue;
                    for (int k = Ranges[g].x; k < Ranges[g].x + Ranges[g].y; k++)
                    {
                        var c = Columns[k];
                        if (s.Position.y + 0.5f * s.Scale > c.Base.y + c.Height || s.Position.y + 1.6f * s.Scale < c.Base.y)
                            continue;
                        var away = xz - c.Base.xz;
                        var min = c.Radius + radius;
                        var d2 = math.lengthsq(away);
                        if (d2 >= min * min)
                            continue;
                        xz = c.Base.xz + (d2 > 1e-8f ? away * math.rsqrt(d2) : new float2(1f, 0f)) * min;
                        moved = true;
                    }
                }
                if (!moved)
                    return;
                s.Position.xz = xz;
                Pushed[0]++;
            }
        }
    }
}
