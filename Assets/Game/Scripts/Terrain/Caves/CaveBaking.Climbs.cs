using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-144 row 1 (#267): climb routes and shimmy ledges in natural caves' vertical sections. A route is a band of wall
    /// marked climbable (CaveClimb: foot and top on the face, width, Climbability grade) that looks it: rough stepped
    /// rock or a crack line with chockstones (craggy, grade 4), or roots, hanging vines or an old rope (assisted, grade 2).
    /// Routes go up natural shafts to the sky, up the tall drops in side passages (taller than anyone mantles), and up
    /// the face beside a river chamber's chute. A drop with no other way back to the mouth always gets an assisted one;
    /// the rest are optional, and craggy ones are shortcuts for good climbers. Along the rim of each tall drop the lip is
    /// marked as a ledge (CaveLedge, graded by its grip) to hang from and shimmy along. Mines keep their ladders (W-147)
    /// and road tunnels have no drops. Rule 22: plain records with stable ids from each cave's own seed, every draw of a
    /// try made before any test can skip it; they change no rock, so nothing placed before them moves.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Taller than this a drop can't be mantled (PlayerMovementSystem.MantleReach is 1.8 m): it needs a route.</summary>
        const float ClimbDrop = 1.85f;
        /// <summary>How close two routes' feet may be (m), and the least height a route is worth marking.</summary>
        const float ClimbApart = 4f, ClimbLeast = 1.85f;
        /// <summary>Of the optional drops (another way out exists), the share given a route.</summary>
        const float OptionalShare = 0.6f;

        /// <summary>A tall drop found along a passage, before it's given a route.</summary>
        struct ClimbDropSpot
        {
            public int Segment;
            /// <summary>The face's foot (low side) and top (high side) on the floor, and into the face (xz, unit).</summary>
            public float2 LowQ, HighQ, Into;
            public float Low, High;
            /// <summary>Whether walking the segment from A to B climbs it (A on the low side).</summary>
            public bool UpFromA;
        }

        /// <summary>Climb routes and ledges in every natural cave.</summary>
        static void PlaceClimbs(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                List<CaveSegment> segments, List<CaveStyle> styles, Extras extras)
        {
            // Caves in parallel (each draws from its own seed and only reads the terrain), each into its own lists,
            // joined in cave order afterwards, so the result is exactly what one loop gave (as PlaceRubble).
            var terrain = source;   // lambdas can't capture an in parameter
            var perCave = new (List<CaveClimb> climbs, List<CaveLedge> ledges, int[] counts)[caves.Count];
            var caveStyles = new CaveStyle[caves.Count];
            for (int c = 0; c < caves.Count; c++)
                caveStyles[c] = caves[c].Style > 0 ? styles[caves[c].Style - 1] : null;
            bool Natural(int c) => caves[c].Tunnel == 0 && (ReferenceEquals(caveStyles[c], null) || ReferenceEquals(caveStyles[c].Ladder, null));
            // The tall drops along every natural cave's passages first, segments in parallel (the long scan; one big cave
            // would otherwise hold up the rest), each segment into its own list, read back in segment order.
            var segmentDrops = new List<ClimbDropSpot>[segments.Count];
            var scan = new List<int>();
            for (int c = 0; c < caves.Count; c++)
                if (Natural(c))
                    for (int si = caves[c].FirstSegment; si < caves[c].FirstSegment + caves[c].SegmentCount; si++)
                        scan.Add(si);
            System.Threading.Tasks.Parallel.For(0, scan.Count, k =>
            {
                var found = segmentDrops[scan[k]] = new List<ClimbDropSpot>();
                FindDrops(terrain, nodes, kinds, segments, scan[k], found);
            });
            System.Threading.Tasks.Parallel.For(0, caves.Count, c =>
            {
                var climbs = new List<CaveClimb>();
                var ledges = new List<CaveLedge>();
                // Counts for the log: shafts, needed drops, optional drops, chutes, ledges; and drops seen but left bare.
                var counts = new int[6];
                perCave[c] = (climbs, ledges, counts);
                var cave = caves[c];
                if (!Natural(c))
                    return;
                var rng = new Random(math.hash(new uint2(cave.Key, 0xC11Bu)) | 1u);
                var feet = new List<(float3 at, byte grade)>();
                var made = 0;

                void Add(CaveClimb climb, bool ledge, float ledgeDepth)
                {
                    climb.Cave = c;
                    climb.Id = math.hash(new uint3(cave.Key, 0xC11Bu, (uint)made++));
                    climb.Grade = ClimbGrade(climb.Kind);
                    // From its id, not the cave's draws, so a route kept or skipped never shifts the next one's.
                    climb.Seed = (math.hash(new uint2(climb.Id, 0x5EEDu)) & 0xFFFFFFu) / 16777216f;
                    climbs.Add(climb);   // its points come after, routes in parallel
                    feet.Add((climb.Base, climb.Grade));
                    if (ledge)
                        AddLedge(terrain, climb, ledgeDepth, ledges, counts, c, cave.Key, made);
                }

                // A route's foot too near another's; a needed way out only counts the assisted ones (it must stay grade 2).
                bool Crowded(float3 p, bool needed = false)
                {
                    foreach (var f in feet)
                        if ((!needed || f.grade <= 2) && math.distance(f.at, p) < ClimbApart)
                            return true;
                    return false;
                }

                // Up the shafts to the sky: the wall nearest the axis all the way up, as the mines' ladders find it.
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
                    var seg = segments[si];
                    int chamber, shaft;
                    if (kinds[seg.A] == CaveNodeKind.Chamber && kinds[seg.B] == CaveNodeKind.Shaft) { chamber = seg.A; shaft = seg.B; }
                    else if (kinds[seg.B] == CaveNodeKind.Chamber && kinds[seg.A] == CaveNodeKind.Shaft) { chamber = seg.B; shaft = seg.A; }
                    else continue;
                    // Every draw first: the look, its width and where round the shaft to start looking.
                    var look = rng.NextFloat();
                    var half = rng.NextFloat(0.4f, 0.65f);
                    var start = rng.NextFloat(math.PI * 2f);
                    var centre = nodes[chamber].Position;
                    var floor = terrain.FloorUnder(centre, out var inCave);
                    if (!inCave)
                        continue;
                    var axis = nodes[shaft].Position.xz;
                    var ground = terrain.SampleGround(axis).Height;
                    if (!ShaftWall(terrain, axis, floor, ground, start, out var dir, out var reach))
                        continue;
                    var at = axis + dir * (reach - 0.1f);
                    var footFloor = terrain.FloorUnder(new float3(at.x, centre.y, at.y), out var footInCave);
                    if (!footInCave || math.abs(footFloor - floor) > 1.5f)
                        continue;
                    var rim = axis + dir * (reach + 0.4f);
                    var top = terrain.SampleGround(rim).Height;
                    var foot = new float3(at.x, footFloor, at.y);
                    if (top - footFloor < ClimbLeast || Crowded(foot))
                        continue;
                    // A skylight's wall: roots most often (the surface is just above), then vines, then bare crag.
                    var kind = look < 0.45f ? ClimbKind.Roots : look < 0.75f ? ClimbKind.Vines : look < 0.9f ? ClimbKind.Crag : ClimbKind.Crack;
                    Add(new CaveClimb
                    {
                        Base = foot, Top = new float3(rim.x, top, rim.y), Out = -dir, HalfWidth = half, Kind = kind, Leads = ClimbLeads.Surface
                    }, false, 0f);
                    counts[0]++;
                }

                // The river chambers' chutes: a way up the face beside the water, against a side wall.
                foreach (var ch in extras.Chambers)
                {
                    if (ch.Cave != c)
                        continue;
                    var look = rng.NextFloat();
                    var side = rng.NextFloat() < 0.5f ? -1f : 1f;
                    var half = rng.NextFloat(0.4f, 0.6f);
                    if (ch.Drop < ClimbLeast)
                        continue;
                    var along = math.normalizesafe(ch.B.xz - ch.A.xz, new float2(1f, 0f));
                    var across = new float2(-along.y, along.x) * side;
                    var at = math.lerp(ch.A.xz, ch.B.xz, ch.DropAt) + across * math.max(ch.Width - 0.9f, 0.6f);
                    if (!FindFace(terrain, at + along * 1.5f, -along, ch.A.y, out var lowQ, out var highQ, out var low, out var high))
                        continue;
                    var foot = new float3(lowQ.x, low, lowQ.y);
                    if (high - low < ClimbLeast || Crowded(foot))
                        continue;
                    var kind = look < 0.5f ? ClimbKind.Crag : look < 0.8f ? ClimbKind.Roots : ClimbKind.Rope;
                    Add(new CaveClimb
                    {
                        Base = foot, Top = new float3(highQ.x, high, highQ.y), Out = along, HalfWidth = half, Kind = kind, Leads = ClimbLeads.Upstream
                    }, true, 1f);
                    counts[3]++;
                }

                // The tall drops along the passages, then which of them are the only way back to the mouth.
                var drops = new List<ClimbDropSpot>();
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                    drops.AddRange(segmentDrops[si]);
                var escapes = Escapes(cave, nodes, segments, drops);
                foreach (var d in drops)
                {
                    var seg = segments[d.Segment];
                    var lowNode = d.UpFromA ? seg.A : seg.B;
                    var highNode = d.UpFromA ? seg.B : seg.A;
                    var needed = !escapes[lowNode - cave.FirstNode];
                    // Every draw first: whether an optional one gets a route, its look and width.
                    var roll = rng.NextFloat();
                    var look = rng.NextFloat();
                    var half = rng.NextFloat(0.4f, 0.7f);
                    var foot = new float3(d.LowQ.x, d.Low, d.LowQ.y);
                    var nearChute = false;
                    foreach (var ch in extras.Chambers)
                        if (ch.Cave == c && foot.x > ch.Min.x - 2f && foot.x < ch.Max.x + 2f && foot.z > ch.Min.z - 2f && foot.z < ch.Max.z + 2f)
                            nearChute = true;
                    // A needed way out is always given one unless an assisted route already stands there.
                    if (needed ? Crowded(foot, true) : nearChute || Crowded(foot) || roll > OptionalShare)
                    {
                        counts[5]++;
                        continue;
                    }
                    // A way out that must be taken is assisted (roots, vines or a rope someone left); an optional one is
                    // craggy rock or a crack half the time.
                    ClimbKind kind;
                    if (needed)
                        kind = look < 0.4f ? ClimbKind.Roots : look < 0.75f ? ClimbKind.Rope : ClimbKind.Vines;
                    else
                        kind = look < 0.3f ? ClimbKind.Crag : look < 0.5f ? ClimbKind.Crack : look < 0.8f ? ClimbKind.Roots : ClimbKind.Vines;
                    var leads = kinds[highNode] == CaveNodeKind.Room ? ClimbLeads.Room
                        : Degree(cave, segments, highNode) == 1 ? ClimbLeads.DeadEnd : ClimbLeads.Passage;
                    Add(new CaveClimb
                    {
                        Base = foot, Top = new float3(d.HighQ.x, d.High, d.HighQ.y), Out = -d.Into, HalfWidth = half, Kind = kind,
                        Leads = leads, Needed = needed
                    }, true, 1f);
                    counts[needed ? 1 : 2]++;
                }
            });
            // Joined in cave order.
            var total = new int[6];
            var firstRoute = extras.Climbs.Count;
            foreach (var (climbs, ledges, counts) in perCave)
            {
                extras.Climbs.AddRange(climbs);
                extras.Ledges.AddRange(ledges);
                for (int k = 0; k < total.Length; k++)
                    total[k] += counts[k];
            }
            // Then every route's pieces, routes in parallel (each from its own id), joined in route order.
            // A band whose face is mostly missing (an opening or a deep undercut between the two floors, so most of its
            // holds find no rock) would read as bare: a needed one becomes a rope (it hangs clear of the rock), an
            // optional one is dropped.
            var routes = extras.Climbs.GetRange(firstRoute, extras.Climbs.Count - firstRoute);
            var routePoints = new List<ClimbPoint>[routes.Count];
            var keep = new bool[routes.Count];
            var roped = new bool[routes.Count];
            System.Threading.Tasks.Parallel.For(0, routes.Count, k =>
            {
                var climb = routes[k];
                var covered = ClimbPoints(terrain, ref climb, routePoints[k] = new List<ClimbPoint>()) >= MinFaceCover;
                if (!covered && climb.Needed)
                {
                    roped[k] = true;
                    climb.Kind = ClimbKind.Rope;
                    climb.Grade = ClimbGrade(ClimbKind.Rope);
                    ClimbPoints(terrain, ref climb, routePoints[k] = new List<ClimbPoint>());
                    covered = routePoints[k].Count > 0;
                }
                routes[k] = climb;
                keep[k] = covered;
            });
            extras.Climbs.RemoveRange(firstRoute, routes.Count);
            int dropped = 0, ropes = 0;
            for (int k = 0; k < routes.Count; k++)
            {
                if (roped[k])
                    ropes++;
                if (!keep[k])
                {
                    dropped++;
                    if (routes[k].Needed)
                        total[5]++;
                    continue;
                }
                var climb = routes[k];
                climb.FirstPoint = extras.ClimbPoints.Count;
                climb.PointCount = routePoints[k].Count;
                extras.Climbs.Add(climb);
                extras.ClimbPoints.AddRange(routePoints[k]);
            }
            UnityEngine.Debug.Log($"[cave-climbs] shafts {total[0]}, needed drops {total[1]}, optional drops {total[2]}, " +
                                  $"chutes {total[3]}, ledges {total[4]}, drops left bare {total[5]}; " +
                                  $"bare bands dropped {dropped}, turned to rope {ropes}; " +
                                  $"routes {extras.Climbs.Count}, points {extras.ClimbPoints.Count}");
        }

        /// <summary>A band needs at least this share of its pieces on rock to read as a climb.</summary>
        const float MinFaceCover = 0.4f;

        /// <summary>The Climbability grade a route's look gives it (W-144 card: craggy rock 4; roots, vines, rope 2).</summary>
        static byte ClimbGrade(ClimbKind kind) => kind == ClimbKind.Crag || kind == ClimbKind.Crack ? (byte)4 : (byte)2;

        /// <summary>How many of the cave's segments meet node n.</summary>
        static int Degree(CaveData cave, List<CaveSegment> segments, int n)
        {
            var count = 0;
            for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                if (segments[si].A == n || segments[si].B == n)
                    count++;
            return count;
        }

        /// <summary>
        /// The wall of a shaft nearest its axis at every height from above head height in the chamber up to the rim,
        /// trying six ways round from start; the way (out from the axis) and that least reach. As PlaceLadders' search.
        /// </summary>
        static bool ShaftWall(in TerrainSource terrain, float2 axis, float floor, float ground, float start, out float2 dir, out float reach)
        {
            for (int tries = 0; tries < 6; tries++)
            {
                var a = start + tries * math.PI / 3f;
                dir = new float2(math.cos(a), math.sin(a));
                reach = float.MaxValue;
                for (var y = floor + 2.5f; y < ground - 0.5f; y += 0.5f)
                {
                    // Out from the axis by part of the distance to the nearest rock (as OnRock), the last step halved.
                    var t = 0f;
                    var from = new float3(axis.x, y, axis.y);
                    var way = new float3(dir.x, 0f, dir.y);
                    var r = 0.2f;
                    var d = terrain.CaveDistance(from + way * r);
                    if (d >= 0f)
                        t = r;
                    for (int k = 0; k < 64 && t <= 0f && r < 12f; k++)   // gameplay: bake-time wall search
                    {
                        var next = r + math.max(0.05f, -d * 0.4f);
                        var dn = terrain.CaveDistance(from + way * next);
                        if (dn >= 0f)
                        {
                            float lo = r, hi = next;
                            for (int h = 0; h < 3; h++)
                            {
                                var mid = (lo + hi) * 0.5f;
                                if (terrain.CaveDistance(from + way * mid) >= 0f) hi = mid; else lo = mid;
                            }
                            t = hi;
                        }
                        r = next;
                        d = dn;
                    }
                    if (t <= 0f) { reach = 0f; break; }
                    reach = math.min(reach, t);
                }
                if (reach >= 0.6f && reach < float.MaxValue)
                    return true;
            }
            dir = default;
            reach = 0f;
            return false;
        }

        static float FloorNear(in TerrainSource terrain, float2 q, float near, out bool ok) => terrain.FloorUnder(new float3(q.x, near + 0.5f, q.y), out ok);

        /// <summary>
        /// From a point on the low floor, walking along into: where the floor rises more than ClimbDrop within half a
        /// metre (up to 4 m on), the face found by halving; its foot and top on the two floors.
        /// </summary>
        static bool FindFace(in TerrainSource terrain, float2 from, float2 into, float near, out float2 lowQ, out float2 highQ, out float low, out float high)
        {
            lowQ = highQ = default;
            low = high = 0f;
            var prev = FloorNear(terrain, from, near, out var prevOk);
            var prevQ = from;
            for (var d = 0.5f; d <= 4f; d += 0.5f)
            {
                var q = from + into * d;
                var here = FloorNear(terrain, q, math.max(prev, near) + ClimbDrop + 1f, out var ok);
                if (ok && prevOk && here - prev > ClimbDrop)
                    return Face(terrain, prevQ, q, prev, here, out lowQ, out highQ, out low, out high);
                prevQ = q;
                prev = here;
                prevOk = ok;
            }
            return false;
        }

        /// <summary>Halves between a low and a high floor point to the face; the face's foot and top (on the floors).</summary>
        static bool Face(in TerrainSource terrain, float2 lowQ0, float2 highQ0, float low0, float high0,
                         out float2 lowQ, out float2 highQ, out float low, out float high)
        {
            lowQ = lowQ0;
            highQ = highQ0;
            for (int k = 0; k < 5; k++)
            {
                var m = (lowQ + highQ) * 0.5f;
                var f = FloorNear(terrain, m, high0, out var mok);
                if (mok && f > low0 + ClimbDrop * 0.5f) highQ = m; else lowQ = m;
            }
            var into = math.normalizesafe(highQ - lowQ, new float2(1f, 0f));
            // The foot a little out from the face, the top a little in from the rim, each on its own floor.
            var foot = highQ - into * 0.25f;
            var rim = highQ + into * 0.35f;
            low = FloorNear(terrain, foot, low0, out var fok);
            high = FloorNear(terrain, rim, high0, out var hok);
            lowQ = foot;
            highQ = rim;
            return fok && hok && math.abs(low - low0) < 0.5f && high - low > ClimbDrop * 0.8f
                && terrain.CaveDistance(new float3(foot.x, low + 1f, foot.y)) < 0f;
        }

        /// <summary>The tall drops along one passage segment (as PlaceLadders' scan, with ClimbDrop for the step).</summary>
        static void FindDrops(in TerrainSource terrain, List<CaveNode> nodes, List<CaveNodeKind> kinds, List<CaveSegment> segments, int si,
                              List<ClimbDropSpot> drops)
        {
            var seg = segments[si];
            if (kinds[seg.A] == CaveNodeKind.Shaft || kinds[seg.B] == CaveNodeKind.Shaft)
                return;
            var a = nodes[seg.A].Position;
            var b = nodes[seg.B].Position;
            var length = math.distance(a.xz, b.xz);
            if (length < 1f)
                return;
            var along = math.normalize((b - a).xz);
            var prevQ = a.xz;
            var prev = FloorNear(terrain, prevQ, a.y, out var prevOk);
            var lastAt = -10f;
            for (var d = 0.5f; d <= length; d += 0.5f)
            {
                var q = a.xz + along * d;
                var line = math.lerp(a.y, b.y, d / length);
                var here = FloorNear(terrain, q, line + math.max(0f, prev - line), out var ok);
                if (ok && prevOk && math.abs(here - prev) > ClimbDrop && d - lastAt > 2f)
                {
                    var up = here > prev;
                    float2 lowQ = up ? prevQ : q, highQ = up ? q : prevQ;
                    if (Face(terrain, lowQ, highQ, math.min(here, prev), math.max(here, prev), out var fl, out var fh, out var low, out var high))
                    {
                        drops.Add(new ClimbDropSpot
                        {
                            Segment = si, LowQ = fl, HighQ = fh, Low = low, High = high,
                            Into = math.normalizesafe(fh - fl, along), UpFromA = up
                        });
                        lastAt = d;
                    }
                }
                prevQ = q;
                prev = here;
                prevOk = ok;
            }
        }

        /// <summary>
        /// Per node of the cave (from FirstNode): whether it walks back to the mouth without climbing a tall drop. A
        /// segment with a drop on it is walked only downwards across it; shafts aren't ways out (they need a climb).
        /// </summary>
        static bool[] Escapes(CaveData cave, List<CaveNode> nodes, List<CaveSegment> segments, List<ClimbDropSpot> drops)
        {
            var escapes = new bool[cave.NodeCount];
            var mouth = cave.FirstNode;
            var best = float.MaxValue;
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
            {
                var d = math.distancesq(nodes[n].Position, cave.Mouth);
                if (d < best) { best = d; mouth = n; }
            }
            // Which ways each segment can be walked: a drop climbed going A to B stops that way, and the other.
            var aToB = new Dictionary<int, bool>();
            var bToA = new Dictionary<int, bool>();
            foreach (var d in drops)
            {
                if (d.UpFromA) aToB[d.Segment] = false;
                else bToA[d.Segment] = false;
            }
            escapes[mouth - cave.FirstNode] = true;
            var queue = new Queue<int>();
            queue.Enqueue(mouth);
            while (queue.Count > 0)
            {
                var m = queue.Dequeue();
                // Nodes that can walk to m: across a segment walkable towards m.
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
                    var seg = segments[si];
                    int from;
                    if (seg.B == m && !aToB.ContainsKey(si)) from = seg.A;
                    else if (seg.A == m && !bToA.ContainsKey(si)) from = seg.B;
                    else continue;
                    var i = from - cave.FirstNode;
                    if (i < 0 || i >= escapes.Length || escapes[i])
                        continue;
                    escapes[i] = true;
                    queue.Enqueue(from);
                }
            }
            return escapes;
        }

        /// <summary>The lip along the top of a drop route: a ledge across its width and on along the rim while it holds.</summary>
        static void AddLedge(in TerrainSource source, in CaveClimb route, float depthWanted, List<CaveLedge> ledges, int[] counts, int cave, uint key, int index)
        {
            var terrain = source;   // local functions can't capture in parameters
            var climb = route;
            var into = -climb.Out;
            var across = new float2(-into.y, into.x);
            var lip = climb.Top.xz - into * 0.35f;   // back at the rim from where the top was measured
            // On along the rim each way while the floor behind stays high and the floor in front stays low.
            float Reach(float sign)
            {
                var r = 0f;
                for (var s = 0.25f; s <= 3f; s += 0.25f)
                {
                    var q = lip + across * (sign * s);
                    var top = FloorNear(terrain, q + into * 0.3f, climb.Top.y, out var tok);
                    var under = FloorNear(terrain, q - into * 0.5f, climb.Base.y, out var uok);
                    if (!tok || !uok || math.abs(top - climb.Top.y) > 0.3f || top - under < 1.2f)
                        break;
                    r = s;
                }
                return r;
            }
            var left = math.max(Reach(-1f), climb.HalfWidth);
            var right = math.max(Reach(1f), climb.HalfWidth);
            // How deep the top is behind the lip: until the floor steps or rock (head height) is met.
            var depth = 0f;
            for (var s = 0.25f; s <= depthWanted + 0.01f; s += 0.25f)
            {
                var q = lip + into * s;
                var f = FloorNear(terrain, q, climb.Top.y, out var ok);
                if (!ok || math.abs(f - climb.Top.y) > 0.3f || terrain.CaveDistance(new float3(q.x, f + 0.8f, q.y)) >= 0f)
                    break;
                depth = s;
            }
            var from = lip - across * left;
            var to = lip + across * right;
            ledges.Add(new CaveLedge
            {
                From = new float3(from.x, climb.Top.y, from.y), To = new float3(to.x, climb.Top.y, to.y), Out = climb.Out, Depth = depth,
                // Its grip: a root-grown lip is easy (2), bare rock 3 (A-101 1b: a thin crack would be 4).
                Grade = climb.Kind == ClimbKind.Roots || climb.Kind == ClimbKind.Vines ? (byte)2 : (byte)3,
                Cave = cave, Id = math.hash(new uint3(key, 0x1ED6u, (uint)index))
            });
            counts[4]++;
        }

        /// <summary>
        /// The pieces that make a route look climbable, each on the rock (found by stepping in from the air): for crag,
        /// stepped shelves and knobs in a loose grid; for a crack, a zigzag line with chockstones either side; for roots
        /// and vines, wandering strands from the top down (Join links a point to the one before); for a rope, one line
        /// from a peg at the top, hanging a little off the rock, with knots.
        /// Returns the share of pieces that found rock (how much of the band is a real face).
        /// </summary>
        static float ClimbPoints(in TerrainSource source, ref CaveClimb climb, List<ClimbPoint> points)
        {
            int tries = 0, hits = 0;
            var terrain = source;   // local functions can't capture in parameters
            var rng = new Random(math.hash(new uint2((uint)climb.Id, 0x9017u)) | 1u);
            var height = climb.Top.y - climb.Base.y;
            var outward = new float3(climb.Out.x, 0f, climb.Out.y);
            var across = new float3(-climb.Out.y, 0f, climb.Out.x);
            var footFace = climb.Base;
            var topFace = new float3(climb.Top.x, climb.Top.y, climb.Top.z) + outward * 0.35f;   // the rim, over the face
            // How far in to look for the rock: a shaft's walls lean back further than a drop's.
            var searchIn = climb.Leads == ClimbLeads.Surface ? 6f : 3f;
            // A point on the band (x across, v up 0..1) placed on the rock: from 1.2 m out, in until rock.
            bool OnRock(float x, float v, out float3 p, out float3 n)
            {
                tries++;
                var line = math.lerp(footFace, topFace, v) + across * x;
                var start = line + outward * 1.2f;
                p = n = default;
                // Stepped in by part of the distance to the nearest rock (0.4 of it: the field can overstate it), then
                // the last step halved to a couple of centimetres.
                float s = 0f, d = terrain.CaveDistance(start);
                if (d < 0f)
                {
                    var hit = false;
                    for (int k = 0; k < 64 && s < searchIn; k++)   // gameplay: bake-time surface search
                    {
                        var next = s + math.max(0.03f, -d * 0.4f);
                        var dn = terrain.CaveDistance(start - outward * next);
                        if (dn >= 0f)
                        {
                            float lo = s, hi = next;
                            for (int h = 0; h < 4; h++)
                            {
                                var mid = (lo + hi) * 0.5f;
                                if (terrain.CaveDistance(start - outward * mid) >= 0f) hi = mid; else lo = mid;
                            }
                            s = hi;
                            hit = true;
                            break;
                        }
                        s = next;
                        d = dn;
                    }
                    if (!hit)
                        return false;
                }
                p = start - outward * s;
                n = RockNormal(terrain, p, outward);
                hits++;
                return true;
            }
            void Point(float3 p, float3 n, float size, bool join, float tint)
                => points.Add(new ClimbPoint { Position = p, Normal = n, Size = size, Join = join, Tint = tint });

            switch (climb.Kind)
            {
                case ClimbKind.Crag:
                {
                    // Shelves every 0.3-0.45 m up, two or three across, each a little off the last row's.
                    for (var y = 0.25f; y < height + 0.1f; y += rng.NextFloat(0.3f, 0.45f))
                    {
                        var count = rng.NextInt(2, 4);
                        for (int k = 0; k < count; k++)
                        {
                            var x = (k + rng.NextFloat(0.15f, 0.85f)) / count * 2f - 1f;
                            var size = rng.NextFloat(0.08f, 0.17f);
                            var tint = rng.NextFloat(0.75f, 1.05f);
                            if (OnRock(x * climb.HalfWidth, y / math.max(height, 0.1f), out var p, out var n))
                                Point(p, n, size, false, tint);
                        }
                    }
                    break;
                }
                case ClimbKind.Crack:
                {
                    // The crack: a dark zigzag line up the middle (joined), then chockstones and edges either side.
                    var x = rng.NextFloat(-0.15f, 0.15f);
                    for (var y = 0.1f; y < height + 0.1f; y += 0.22f)
                    {
                        x = math.clamp(x + rng.NextFloat(-0.12f, 0.12f), -0.3f, 0.3f);
                        if (OnRock(x * climb.HalfWidth, y / math.max(height, 0.1f), out var p, out var n))
                            Point(p, n, 0.035f, true, 0.12f);
                    }
                    for (var y = 0.3f; y < height; y += rng.NextFloat(0.35f, 0.55f))
                    {
                        var side = rng.NextFloat() < 0.5f ? -1f : 1f;
                        var size = rng.NextFloat(0.06f, 0.12f);
                        var off = rng.NextFloat(0.25f, 0.6f) * side;
                        if (OnRock(off * climb.HalfWidth, y / math.max(height, 0.1f), out var p, out var n))
                            Point(p, n, size, false, rng.NextFloat(0.75f, 1f));
                    }
                    break;
                }
                case ClimbKind.Roots:
                case ClimbKind.Vines:
                {
                    // Strands from the rim down, wandering across, the roots thicker and fewer, the vines many and thin.
                    var vines = climb.Kind == ClimbKind.Vines;
                    var strands = vines ? rng.NextInt(5, 9) : rng.NextInt(3, 6);
                    for (int s = 0; s < strands; s++)
                    {
                        var x = (s + rng.NextFloat(0.2f, 0.8f)) / strands * 2f - 1f;
                        var thick = vines ? rng.NextFloat(0.012f, 0.022f) : rng.NextFloat(0.03f, 0.06f);
                        var reach = rng.NextFloat(0.75f, 1.05f);   // how far down (share of the height; the longest touch the floor)
                        var drift = rng.NextFloat(-0.4f, 0.4f);
                        var tint = rng.NextFloat(0.8f, 1.1f);
                        var join = false;
                        for (var v = 1f; v >= 1f - reach && v >= 0f; v -= 0.3f / math.max(height, 0.5f))
                        {
                            var down = 1f - v;
                            var wx = x + drift * down + 0.12f * math.sin(down * 9f + s * 2.1f);
                            var size = thick * math.lerp(1f, 0.45f, down / math.max(reach, 0.1f));
                            if (OnRock(math.clamp(wx, -1.2f, 1.2f) * climb.HalfWidth, v, out var p, out var n))
                            {
                                Point(p + n * size, n, size, join, tint);
                                join = true;
                            }
                            else
                                join = false;
                        }
                    }
                    break;
                }
                case ClimbKind.Rope:
                {
                    // A peg in the rim, then the rope hanging straight down from it, clear of the rock, knotted.
                    var x = rng.NextFloat(-0.3f, 0.3f) * climb.HalfWidth;
                    if (!OnRock(x, 1f, out var peg, out var pn))
                        break;
                    Point(peg, pn, 0.05f, false, 0.55f);
                    var knot = rng.NextFloat(0f, 0.5f);
                    var join = true;   // the first point hangs from the peg
                    for (var y = 0f; y <= height + 0.3f; y += 0.15f)
                    {
                        var q = peg + pn * 0.08f + new float3(0f, -y, 0f);
                        // Kept off the rock where the face leans out.
                        for (int k = 0; k < 20 && terrain.CaveDistance(q) > -0.06f; k++)
                            q += outward * 0.04f;
                        var isKnot = math.frac((y + knot) / 0.6f) < 0.2f;
                        Point(q, outward, isKnot ? 0.032f : 0.018f, join, 1f);
                        join = true;
                    }
                    return 1f;   // a rope needs only its peg
                }
            }
            return tries > 0 ? hits / (float)tries : 0f;
        }

        /// <summary>Out of the rock at p (the cave field's gradient, which grows into rock), or fallback if flat.</summary>
        static float3 RockNormal(in TerrainSource terrain, float3 p, float3 fallback)
        {
            const float e = 0.05f;
            var g = new float3(
                terrain.CaveDistance(p + new float3(e, 0f, 0f)) - terrain.CaveDistance(p - new float3(e, 0f, 0f)),
                terrain.CaveDistance(p + new float3(0f, e, 0f)) - terrain.CaveDistance(p - new float3(0f, e, 0f)),
                terrain.CaveDistance(p + new float3(0f, 0f, e)) - terrain.CaveDistance(p - new float3(0f, 0f, e)));
            return math.normalizesafe(-g, fallback);
        }
    }
}
