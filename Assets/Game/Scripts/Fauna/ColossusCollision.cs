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
    /// A-107 (#276): each column also has a climbing grade, from its definition's climb sections (<see cref="Mark"/>);
    /// ColossusClimbSystem hands the columns near the players to movement (ColossusClimbs), which climbs them like trunks
    /// and stands on their tops.
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
            // A-107: each column's climbing grade (Smooth where no section marks it) and section (-1 for none), the
            // sections, and each grid cell's column as built (to place the sections' details on the meshes).
            public byte[] Grades = new byte[0];
            public sbyte[] InSection = new sbyte[0];
            public ColossusClimbSection[] Sections = new ColossusClimbSection[0];
            public readonly Dictionary<int2, int> Cells = new Dictionary<int2, int>();
            // A-107 row 2: the root's place and heading when the climb columns were last gathered, to carry a climber on
            // by how far it has walked and turned since (Carry).
            public float3 GatheredAt;
            public quaternion GatheredAs = quaternion.identity;
            public bool Gathered;
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
                g.Cells[kv.Key] = g.Columns.Count;
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
            g.Grades = new byte[g.Columns.Count];
            g.InSection = new sbyte[g.Columns.Count];
            for (int i = 0; i < g.Columns.Count; i++)
            {
                g.Grades[i] = Climbing.Smooth;
                g.InSection[i] = -1;
            }
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

        /// <summary>The way a climb section points, from the colossus's middle (local x right, z forward; unit).</summary>
        static float2 SectionDir(in ColossusClimbSection s)
        {
            var yaw = math.radians(s.Yaw);
            return new float2(math.sin(yaw), math.cos(yaw));
        }

        /// <summary>
        /// A-107: grades slot's columns by its climb sections: a column whose middle is in a section's wedge (seen from
        /// the colossus's middle) or within a column of the wedge's middle line takes its grade, the easiest if several;
        /// the rest are smooth walls. Once after <see cref="Register"/> (seed-derived, so every machine marks the same).
        /// </summary>
        public static void Mark(int slot, ColossusClimbSection[] sections)
        {
            if (!groups.TryGetValue(slot, out var g))
                return;
            g.Sections = sections ?? new ColossusClimbSection[0];
            var counts = new int[g.Sections.Length];
            for (int s = 0; s < g.Sections.Length && s < sbyte.MaxValue; s++)
            {
                var sec = g.Sections[s];
                var grade = (byte)math.clamp(sec.Grade, Climbing.Ladder, Climbing.Smooth);
                var dir = SectionDir(sec);
                var half = math.radians(math.clamp(sec.Arc, 0f, 360f) * 0.5f);
                for (int i = 0; i < g.Local.Count; i++)
                {
                    var d = g.Local[i].Base.xz - g.LocalCentre;
                    var along = math.dot(d, dir);
                    var across = math.abs(d.x * dir.y - d.y * dir.x);
                    var inWedge = math.atan2(across, along) <= half;
                    var onLine = along > -Cell && across <= Cell * 0.9f;
                    if ((inWedge || onLine) && grade < g.Grades[i])
                    {
                        g.Grades[i] = grade;
                        g.InSection[i] = (sbyte)s;
                    }
                }
            }
            for (int i = 0; i < g.InSection.Length; i++)
                if (g.InSection[i] >= 0)
                    counts[g.InSection[i]]++;
            var line = new System.Text.StringBuilder();
            for (int s = 0; s < g.Sections.Length; s++)
                line.Append($"{(s > 0 ? ", " : "")}{g.Sections[s].Look} grade {g.Sections[s].Grade} at {g.Sections[s].Yaw:F0} degrees ({counts[s]} columns)");
            Debug.Log($"ColossusCollision: {g.Name} climb sections: {(g.Sections.Length == 0 ? "none" : line.ToString())}");
        }

        /// <summary>A-107: slot's climb sections (empty when it has none or isn't solid).</summary>
        public static ColossusClimbSection[] SectionsOf(int slot) => groups.TryGetValue(slot, out var g) ? g.Sections : new ColossusClimbSection[0];

        /// <summary>A-107: the climb section of the column over a world point, at the pose slot was built in (for placing
        /// its details right after it is marked); -1 for none.</summary>
        public static int SectionAt(int slot, Vector3 point)
        {
            if (!groups.TryGetValue(slot, out var g))
                return -1;
            var cell = (int2)math.floor(new float2(point.x, point.z) / Cell);
            return g.Cells.TryGetValue(cell, out var i) && i < g.InSection.Length ? g.InSection[i] : -1;
        }

        /// <summary>
        /// A-107: where a climb of slot's section starts and ends, for probes and shots: a step out from one of its columns
        /// that stands on the ground (xz; y that column's foot), the way in (xz, unit), and the highest top of its columns.
        /// The column is the outermost low one whose way in from the step out is not crossed by any other column (a smooth
        /// one in front, or a shoulder's column overhead, would make the climb start somewhere else), a tall one (a leg)
        /// before a short one (a toe, from which the way on may lead under a belly). False when the slot or section has no
        /// columns.
        /// </summary>
        public static bool SectionRoute(int slot, int section, out float3 start, out float2 inward, out float summit)
        {
            start = default;
            inward = default;
            summit = float.MinValue;
            if (!groups.TryGetValue(slot, out var g) || section < 0 || section >= g.Sections.Length || !Shown(g))
                return false;
            Follow(g);
            var q = Heading(g.Root.rotation);
            var sd = SectionDir(g.Sections[section]);
            var dir = math.mul(q, new float3(sd.x, 0f, sd.y)).xz;
            var lowest = float.MaxValue;
            for (int i = 0; i < g.Columns.Count; i++)
                if (g.InSection[i] == section)
                {
                    var c = g.Columns[i];
                    summit = math.max(summit, c.Base.y + c.Height);
                    lowest = math.min(lowest, c.Base.y);
                }
            if (lowest == float.MaxValue)
                return false;
            // (Outermost first, among those whose feet are within a step of the section's lowest.)
            var order = new List<int>();
            for (int i = 0; i < g.Columns.Count; i++)
                if (g.InSection[i] == section && g.Columns[i].Base.y <= lowest + 1.5f)
                    order.Add(i);
            order.Sort((x, y) => math.dot(g.Columns[y].Base.xz - g.Centre, dir).CompareTo(math.dot(g.Columns[x].Base.xz - g.Centre, dir)));
            var tall = lowest + 0.5f * (summit - lowest);
            for (int pass = 0; pass < 2; pass++)
            foreach (var i in order)
            {
                var c = g.Columns[i];
                if (pass == 0 && c.Base.y + c.Height < tall)
                    continue;
                var way = math.normalizesafe(c.Base.xz - g.Centre, dir);
                var from = c.Base.xz + way * (c.Radius + 1f);
                if (Crossed(g, i, from, c.Base.xz + way * c.Radius, c.Base.y))
                    continue;
                start = new float3(from.x, c.Base.y, from.y);
                inward = -way;
                return true;
            }
            // (Every way in crossed: the outermost, as it is.)
            var o = g.Columns[order[0]];
            var xz = o.Base.xz + dir * (o.Radius + 1f);
            start = new float3(xz.x, o.Base.y, xz.y);
            inward = -dir;
            return true;
        }

        /// <summary>A-107: where slot's section is highest now (the top middle of its tallest column) and the body's
        /// middle now (xz), for probes walking up a moving colossus. False when the slot or section has no columns.</summary>
        public static bool SectionPeak(int slot, int section, out float3 peak, out float2 middle)
        {
            peak = default;
            middle = default;
            if (!groups.TryGetValue(slot, out var g) || section < 0 || section >= g.Sections.Length)
                return false;
            Follow(g);
            middle = g.Centre;
            var best = float.MinValue;
            for (int i = 0; i < g.Columns.Count; i++)
                if (g.InSection[i] == section && g.Columns[i].Base.y + g.Columns[i].Height > best)
                {
                    var c = g.Columns[i];
                    best = c.Base.y + c.Height;
                    peak = new float3(c.Base.x, best, c.Base.z);
                }
            return best > float.MinValue;
        }

        /// <summary>Whether a column of g other than <paramref name="skip"/>, standing within a body's height of
        /// <paramref name="feet"/>, comes within a body's width of the line from a to b (xz).</summary>
        static bool Crossed(Group g, int skip, float2 a, float2 b, float feet)
        {
            var ab = b - a;
            var len = math.max(math.lengthsq(ab), 1e-6f);
            for (int i = 0; i < g.Columns.Count; i++)
            {
                if (i == skip)
                    continue;
                var c = g.Columns[i];
                if (c.Base.y > feet + 1.8f || c.Base.y + c.Height < feet)
                    continue;
                var t = math.saturate(math.dot(c.Base.xz - a, ab) / len);
                if (math.distance(a + ab * t, c.Base.xz) < c.Radius + 0.4f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A-107: adds every shown colossus column within <paramref name="range"/> of any of <paramref name="near"/>
        /// (xz) to <paramref name="list"/>, with its grade, while there is room (main thread; ColossusClimbSystem).
        /// </summary>
        public static void GatherClimb(ref FixedList4096Bytes<ColossusClimbColumn> list, in FixedList128Bytes<float2> near, float range)
        {
            if (near.Length == 0)
                return;
            foreach (var g in groups.Values)
            {
                if (!Shown(g))
                    continue;
                Follow(g);
                g.GatheredAt = g.Root.position;
                g.GatheredAs = Heading(g.Root.rotation);
                g.Gathered = true;
                var close = false;
                for (int p = 0; p < near.Length && !close; p++)
                    close = math.distance(g.Centre, near[p]) <= range + g.Reach;
                if (!close)
                    continue;
                var slot = (byte)SlotOf(g);
                for (int i = 0; i < g.Columns.Count && list.Length < list.Capacity; i++)
                {
                    var c = g.Columns[i];
                    for (int p = 0; p < near.Length; p++)
                        if (math.distance(c.Base.xz, near[p]) <= range + c.Radius)
                        {
                            list.Add(new ColossusClimbColumn { Base = c.Base, Radius = c.Radius, Height = c.Height, Grade = i < g.Grades.Length ? g.Grades[i] : Climbing.Smooth, Slot = slot });
                            break;
                        }
                }
            }
        }

        static int SlotOf(Group g)
        {
            foreach (var kv in groups)
                if (kv.Value == g)
                    return kv.Key;
            return 255;
        }

        /// <summary>
        /// A-107 row 2: how a point on slot's body has moved since the climb columns were last gathered (the walker has
        /// walked and turned under it): where it is now, and the turn (radians about up). False when the colossus has not
        /// moved or is not gathered; then moved is point and turn 0.
        /// </summary>
        public static bool Carry(int slot, float3 point, out float3 moved, out float turn)
        {
            moved = point;
            turn = 0f;
            if (!groups.TryGetValue(slot, out var g) || !g.Gathered || !Shown(g))
                return false;
            var at = (float3)g.Root.position;
            var heading = Heading(g.Root.rotation);
            if (math.all(at == g.GatheredAt) && math.all(heading.value == g.GatheredAs.value))
                return false;
            var delta = math.mul(heading, math.inverse(g.GatheredAs));
            moved = at + math.mul(delta, point - g.GatheredAt);
            var f = math.mul(delta, new float3(0f, 0f, 1f));
            turn = math.atan2(f.x, f.z);
            return math.all(math.isfinite(moved));
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
