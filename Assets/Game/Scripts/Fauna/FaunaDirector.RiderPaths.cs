using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-117 row 2: the way a stable horse and the one fetching it take through a town, and a check that they keep
    /// clear of its walls. The way follows the town's streets (Dijkstra over the street stretches near the stable,
    /// joined where they meet, from the nearest point on a stretch to the nearest point on another), then is pulled
    /// straight wherever a horse-wide line is clear of every wall, so it cuts no corner through a stall or a fence.
    /// The brain walks each waypoint with LeadOn (never into a wall by the body's width); FaunaDirector.Riders hands
    /// them over one at a time. The clearance check counts every frame a ride's horse (its middle or its
    /// hindquarters) or its rider on foot stands inside a building's, stall's or fence's walls by its width: from
    /// FloraPlacement.Blocked and, as a second opinion with no plot-bounds shortcut, each nearby plot's building
    /// placed afresh. Host only, main thread; a route is worked out once per leg of a ride.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>How far round a stable's gate the town's streets are routed over (m).</summary>
        const float RouteReach = 260f;
        /// <summary>A sample every this far along a line tested for walls (m).</summary>
        const float RouteStep = 0.5f;
        /// <summary>The grid a way round the walls is searched on when the streets give none: a cell this wide (m), over
        /// the box round both ends and this far beyond (m).</summary>
        const float RouteCell = 0.75f, RouteMargin = 30f;

        int clearFrames, clearOverlapFrames, clearEvents;
        readonly List<string> clearNotes = new List<string>();
        readonly Dictionary<int, Punchy.Flora.FloraSystem.FloraInstance> clearPlots = new Dictionary<int, Punchy.Flora.FloraSystem.FloraInstance>();
        readonly HashSet<int> clearNoWalls = new HashSet<int>();

        /// <summary>
        /// Waypoints from one spot to another through a town (the last one the goal itself), keeping a body of this
        /// radius off every wall where the line can be pulled straight; just the goal when nothing stands between.
        /// </summary>
        List<float2> StreetRoute(float2 from, float2 to, float2 near, float radius)
        {
            var path = new List<float2>();
            if (!populationSource.Settlements.IsCreated || Clear(from, to, radius))
            {
                path.Add(to);
                return path;
            }
            ref var set = ref populationSource.Settlements.Value;
            var a = new List<float2>();
            var b = new List<float2>();
            for (int k = 0; k < set.Streets.Length; k++)
            {
                ref var st = ref set.Streets[k];
                if (st.Road != 0 || SegmentDistance(near, st.A.xz, st.B.xz, out _) > RouteReach)
                    continue;
                a.Add(st.A.xz);
                b.Add(st.B.xz);
            }
            if (a.Count == 0)
            {
                path.Add(to);
                return path;
            }
            // Nodes: each stretch's ends (merged within 1.5 m); a stretch passes through every node lying on it (where
            // another meets it side-on), in order along it.
            var nodes = new List<float2>();
            int NodeAt(float2 p)
            {
                for (int n = 0; n < nodes.Count; n++)
                    if (math.distancesq(nodes[n], p) < 2.25f)
                        return n;
                nodes.Add(p);
                return nodes.Count - 1;
            }
            for (int k = 0; k < a.Count; k++)
            {
                NodeAt(a[k]);
                NodeAt(b[k]);
            }
            var links = new List<List<(int, float)>>();
            // (Only where a body this wide gets through: a stall, a paddock's fence or a house can stand over a street.)
            void Link(int i, int j)
            {
                while (links.Count < nodes.Count)
                    links.Add(new List<(int, float)>());
                if (!Clear(nodes[i], nodes[j], radius))
                    return;
                var w = math.distance(nodes[i], nodes[j]);
                links[i].Add((j, w));
                links[j].Add((i, w));
            }
            var onStretch = new List<List<int>>();
            for (int k = 0; k < a.Count; k++)
            {
                var on = new List<(float, int)>();
                for (int n = 0; n < nodes.Count; n++)
                    if (SegmentDistance(nodes[n], a[k], b[k], out var t) < 1.5f)
                        on.Add((t, n));
                on.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                var list = new List<int>();
                foreach (var (_, n) in on)
                    list.Add(n);
                onStretch.Add(list);
                for (int i = 1; i < list.Count; i++)
                    Link(list[i - 1], list[i]);
            }
            // Start and goal: the nearest point on the nearest stretch, joined to every node along that stretch.
            int Join(float2 p, out int stretch)
            {
                stretch = 0;
                var best = float.MaxValue;
                var at = p;
                for (int k = 0; k < a.Count; k++)
                {
                    var d = SegmentDistance(p, a[k], b[k], out var t);
                    if (d < best)
                    {
                        best = d;
                        stretch = k;
                        at = math.lerp(a[k], b[k], t);
                    }
                }
                nodes.Add(at);
                var index = nodes.Count - 1;
                foreach (var n in onStretch[stretch])
                    Link(index, n);
                return index;
            }
            var start = Join(from, out var startStretch);
            var goal = Join(to, out var goalStretch);
            if (startStretch == goalStretch)
                Link(start, goal);
            while (links.Count < nodes.Count)
                links.Add(new List<(int, float)>());
            // Dijkstra (a town has a few dozen nodes).
            var dist = new float[nodes.Count];
            var prev = new int[nodes.Count];
            var done = new bool[nodes.Count];
            for (int n = 0; n < nodes.Count; n++)
            {
                dist[n] = float.MaxValue;
                prev[n] = -1;
            }
            dist[start] = 0f;
            for (int step = 0; step < nodes.Count; step++)
            {
                var u = -1;
                for (int n = 0; n < nodes.Count; n++)
                    if (!done[n] && dist[n] < float.MaxValue && (u < 0 || dist[n] < dist[u]))
                        u = n;
                if (u < 0 || u == goal)
                    break;
                done[u] = true;
                foreach (var (v, w) in links[u])
                    if (dist[u] + w < dist[v])
                    {
                        dist[v] = dist[u] + w;
                        prev[v] = u;
                    }
            }
            // (No clear way along the streets, or none on to them or off them at the ends: round the walls cell by cell.)
            if (dist[goal] == float.MaxValue || !Clear(from, nodes[start], radius) || !Clear(nodes[goal], to, radius))
                return GridRoute(from, to, radius);
            var chain = new List<float2> { to };
            for (int n = goal; n >= 0; n = prev[n])
                chain.Add(nodes[n]);
            chain.Add(from);
            chain.Reverse();
            return Pulled(chain, radius);
        }

        /// <summary>
        /// The way round every wall on a grid (A*, eight ways, a cell RouteCell across) over the box round both ends
        /// and RouteMargin beyond, pulled straight; just the goal if there is none.
        /// </summary>
        List<float2> GridRoute(float2 from, float2 to, float radius)
        {
            var lo = math.min(from, to) - RouteMargin;
            var size = (int2)math.ceil((math.max(from, to) + RouteMargin - lo) / RouteCell) + 1;
            size = math.min(size, 240);
            int Cell(float2 p)
            {
                var c = math.clamp((int2)math.round((p - lo) / RouteCell), 0, size - 1);
                return c.y * size.x + c.x;
            }
            float2 At(int i) => lo + new float2(i % size.x, i / size.x) * RouteCell;
            // (0 untested, 1 free, 2 walled: tested only as the search reaches them.)
            var wall = new byte[size.x * size.y];
            bool Free(int i)
            {
                if (wall[i] == 0)
                    wall[i] = (byte)(WallAt(At(i), radius) ? 2 : 1);
                return wall[i] == 1;
            }
            var start = Cell(from);
            var goal = Cell(to);
            var cost = new float[wall.Length];
            var prev = new int[wall.Length];
            for (int i = 0; i < cost.Length; i++)
            {
                cost[i] = float.MaxValue;
                prev[i] = -1;
            }
            var open = new SortedSet<(float, int)>();
            cost[start] = 0f;
            open.Add((math.distance(At(start), to), start));
            var found = false;
            while (open.Count > 0)
            {
                var (_, u) = open.Min;
                open.Remove(open.Min);
                if (u == goal)
                {
                    found = true;
                    break;
                }
                int ux = u % size.x, uy = u / size.x;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int vx = ux + dx, vy = uy + dy;
                        if ((dx | dy) == 0 || vx < 0 || vy < 0 || vx >= size.x || vy >= size.y)
                            continue;
                        var v = vy * size.x + vx;
                        // (The goal cell counts as free: it may sit right up against the wall it leads past.)
                        if (v != goal && !Free(v))
                            continue;
                        var c = cost[u] + ((dx & dy) != 0 ? 1.4142f : 1f) * RouteCell;
                        if (c >= cost[v])
                            continue;
                        if (cost[v] < float.MaxValue)
                            open.Remove((cost[v] + math.distance(At(v), to), v));
                        cost[v] = c;
                        prev[v] = u;
                        open.Add((c + math.distance(At(v), to), v));
                    }
            }
            if (!found)
                return new List<float2> { to };
            var chain = new List<float2> { to };
            for (int n = prev[goal]; n >= 0 && n != start; n = prev[n])
                chain.Add(At(n));
            chain.Add(from);
            chain.Reverse();
            return Pulled(chain, radius);
        }

        /// <summary>A chain of points pulled straight: from each on to the furthest a clear line still reaches (the start
        /// left out, the end kept).</summary>
        List<float2> Pulled(List<float2> chain, float radius)
        {
            var path = new List<float2>();
            // Pulled straight: from each point on to the furthest one a clear line still reaches.
            for (int i = 0; i < chain.Count - 1;)
            {
                var j = i + 1;
                while (j + 1 < chain.Count && Clear(chain[i], chain[j + 1], radius))
                    j++;
                path.Add(chain[j]);
                i = j;
            }
            return path;
        }

        /// <summary>How far a point is from a stretch, and where along it (0 at a, 1 at b) the nearest point lies.</summary>
        static float SegmentDistance(float2 p, float2 a, float2 b, out float t)
        {
            var ab = b - a;
            var len = math.lengthsq(ab);
            t = len < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / len);
            return math.distance(p, a + ab * t);
        }

        /// <summary>Whether a body of this radius can go straight from a to b without touching a wall.</summary>
        bool Clear(float2 from, float2 to, float radius)
        {
            var steps = (int)math.ceil(math.distance(from, to) / RouteStep);
            for (int i = 1; i <= steps; i++)
                if (WallAt(math.lerp(from, to, i / (float)steps), radius))
                    return false;
            return true;
        }

        /// <summary>Whether a body of this radius here is inside a building's, stall's or fence's walls (as the brain's
        /// Walled sees them).</summary>
        bool WallAt(float2 at, float radius) =>
            floraBlob.IsCreated && Punchy.Flora.FloraPlacement.Blocked(ref floraBlob.Value, populationSource, at, radius);

        /// <summary>The second opinion: every plot whose middle is within 25 m, its building placed afresh (cached), with
        /// no plot-bounds shortcut.</summary>
        bool PlotWallAt(float2 at, float radius)
        {
            if (!floraBlob.IsCreated || !populationSource.Settlements.IsCreated)
                return false;
            ref var f = ref floraBlob.Value;
            ref var set = ref populationSource.Settlements.Value;
            for (int p = 0; p < set.Plots.Length; p++)
            {
                if (math.distancesq(set.Plots[p].Centre, at) > 625f || clearNoWalls.Contains(p))
                    continue;
                if (!clearPlots.TryGetValue(p, out var plant))
                {
                    if (!BuildingFootprints.PlacePlot(ref f, populationSource, ref set, set.Plots[p], out plant))
                    {
                        clearNoWalls.Add(p);
                        continue;
                    }
                    clearPlots[p] = plant;
                }
                if (Punchy.Flora.FloraPlacement.InsideWalls(ref f, plant, at, radius))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Walks a creature along waypoints (Travelling's Stage 1 to each, Stage 2 once there): the next one once it is
        /// within reach of this one, or stopped short of one that isn't the last. Resumed after being held, it carries
        /// on to the same waypoint. True once it has reached the last one.
        /// </summary>
        bool FollowPath(Entity who, List<float2> path, ref int leg, float reach, bool resumed = false)
        {
            if (leg >= path.Count)
                return true;
            var em = simWorld.EntityManager;
            var s = em.GetComponentData<CreatureState>(who);
            var last = leg == path.Count - 1;
            if (math.distance(s.Position.xz, path[leg]) < reach || !resumed && !last && s.Stage == 2 && s.StageTime > 0.5f)
            {
                leg++;
                if (leg >= path.Count)
                    return true;
            }
            if (s.Task != CreatureTask.Travel || s.Mode != CreatureMode.Work || s.Stage != 1 || math.any(s.WorkSpot != path[leg]))
            {
                s.Task = CreatureTask.Travel;
                s.Mode = CreatureMode.Work;
                s.Stage = 1;
                s.StageTime = 0f;
                s.WorkSpot = path[leg];
                em.SetComponentData(who, s);
            }
            return false;
        }

        /// <summary>Holds a creature where it stands (Travelling's Stage 2 on its own spot).</summary>
        void HoldStill(Entity who)
        {
            var em = simWorld.EntityManager;
            var s = em.GetComponentData<CreatureState>(who);
            if (s.Stage == 2 && math.all(s.WorkSpot == s.Position.xz))
                return;
            s.Stage = 2;
            s.StageTime = 0f;
            s.WorkSpot = s.Position.xz;
            em.SetComponentData(who, s);
        }

        /// <summary>A body's lead radius (Riding.LeadRadius), from its species.</summary>
        float LeadRadiusOf(Entity who)
        {
            var em = simWorld.EntityManager;
            var body = em.GetComponentData<CreatureBody>(who);
            var s = em.GetComponentData<CreatureState>(who);
            return Riding.LeadRadius(ref body.Blob.Value, s.Scale);
        }

        /// <summary>
        /// The clearance check, once a frame for each ride: its horse's middle and hindquarters, and its rider while on
        /// foot, each at its lead radius. Counts the frames anything overlapped and how many times something went in.
        /// </summary>
        void CheckClearance(HorseRide ride, bool horseHere, bool riderHere)
        {
            var em = simWorld.EntityManager;
            var any = false;
            if (horseHere)
            {
                var body = em.GetComponentData<CreatureBody>(ride.Horse);
                var s = em.GetComponentData<CreatureState>(ride.Horse);
                var radius = Riding.LeadRadius(ref body.Blob.Value, s.Scale);
                var rear = Riding.LeadRear(ref body.Blob.Value, s.Scale, s.Position.xz, s.Yaw);
                var inside = Overlaps(s.Position.xz, radius) || Overlaps(rear, radius);
                if (inside && !ride.HorseInside)
                    Note(ride, "horse", s.Position);
                ride.HorseInside = inside;
                any |= inside;
            }
            if (riderHere && !em.HasComponent<Rider>(ride.Rider))
            {
                var s = em.GetComponentData<CreatureState>(ride.Rider);
                var inside = Overlaps(s.Position.xz, LeadRadiusOf(ride.Rider));
                if (inside && !ride.RiderInside)
                    Note(ride, "rider", s.Position);
                ride.RiderInside = inside;
                any |= inside;
            }
            clearFrames++;
            if (any)
                clearOverlapFrames++;
        }

        bool Overlaps(float2 at, float radius) => WallAt(at, radius) || PlotWallAt(at, radius);

        void Note(HorseRide ride, string who, float3 at)
        {
            clearEvents++;
            if (clearNotes.Count < 12)
                clearNotes.Add($"{who} of the ride from {ride.From.Landmark} ({ride.Phase}) at ({at.x:F1}, {at.z:F1})");
        }

        /// <summary>For the tests (TestHooks.RiderClearance): frames checked, frames with an overlap, times something went in.</summary>
        public string ClearanceReport()
        {
            var s = $"clearance: frames {clearFrames}, overlap frames {clearOverlapFrames}, overlaps {clearEvents}";
            foreach (var note in clearNotes)
                s += "\n  " + note;
            return s;
        }
    }
}
