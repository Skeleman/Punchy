#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Debugging
{
    /// <summary>
    /// W-153 row 1: measures where the island's water does not follow reality. It reads only TerrainSource (the river
    /// graph and SampleGround), so it sees what every water mesh, the swimmer and the far view are built from. Two
    /// passes: the river graph cell by cell (bends, widths, ends, sources, levels, falls) and a grid over the whole
    /// island (water standing above dry ground, steps between neighbouring surfaces). It fixes nothing. The counts
    /// are the baseline in WorldTests.Water_TruthCounts; later rows bring them down.
    /// Editor only: nothing here reaches a player build.
    /// </summary>
    public static class WaterTruth
    {
        public const int Bend = 0, WidthJump = 1, DeadEnd = 2, Source = 3, DryRun = 4, UphillGraph = 5, UphillSampled = 6,
            Wall = 7, Deep = 8, Tilt = 9, Step = 10, FallHead = 11, FallFoot = 12, Kinds = 13;

        public static readonly string[] Names =
        {
            "bend", "widthJump", "deadEnd", "source", "dryRun", "uphillGraph", "uphillSampled",
            "wall", "deep", "tilt", "step", "fallHead", "fallFoot"
        };

        /// <summary>One defect: where, how bad (Score, larger is worse) and two numbers the report prints.</summary>
        public struct Hit
        {
            public float Score;
            public float3 At;
            public float A, B;
            public int I;
        }

        /// <summary>What one run found.</summary>
        public class Result
        {
            public int[] Counts = new int[Kinds];
            /// <summary>Distinct 64 m squares holding a hit of each kind (one long wall is many hits but few places).</summary>
            public int[] Places = new int[Kinds];
            public List<Hit>[] Hits = new List<Hit>[Kinds];
            public int RiverCells, Sources, Falls, FallSteps, WetSamples, Samples;
            public float Step;
            /// <summary>Walls and steps split by what the water is ("river above dry, by a fall"), for telling causes apart.</summary>
            public SortedDictionary<string, int> Split = new SortedDictionary<string, int>();
        }

        public static Result Last;

        // Limits. Each is the size at which the defect shows from the bank, not a tuned pass mark.
        const float WetDepth = 0.05f;      // a sample counts as wet with this much water over the ground
        const float WallHeight = 1f;       // water standing this far above dry ground one sample away
        const float StepHeight = 1f;       // neighbouring surfaces this far apart away from a fall
        const float DeepOver = 2f;         // river water this much deeper than its channel was dug
        const float TiltAcross = 0.3f;     // surface difference bank to bank
        const float Rise = 0.05f;          // surface rising downstream
        const float FallDeep = 3f;         // water this deep just above a fall's lip
        const float PlaceSize = 64f;

        /// <summary>Ground and water at a list of points, or over a square grid when Points is empty.</summary>
        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public float Step;
            public int Side;
            [ReadOnly] public NativeArray<float2> Points;
            [WriteOnly] public NativeArray<float> Ground, Surface;
            /// <summary>0 none, 1 sea, 2 river (flowing), 3 lake, 4 pool or pond.</summary>
            [WriteOnly] public NativeArray<byte> Type;

            public void Execute(int i)
            {
                var p = Points.Length > 0 ? Points[i] : Origin + (new float2(i % Side, i / Side) + 0.5f) * Step;
                var g = Source.SampleGround(p);
                Ground[i] = g.Height;
                Surface[i] = g.WaterSurface;
                byte type = 0;
                if (g.HasWater)
                {
                    type = 4;
                    if (math.abs(g.WaterSurface - Source.World.SeaLevel) < 0.01f)
                        type = 1;
                    else if (math.lengthsq(g.Flow) > 1e-4f)
                        type = 2;
                    else if (Source.Rivers.IsCreated && RiverSampling.Lake(ref Source.Rivers.Value, p, out var lake, out _) > 0f
                             && math.abs(lake - g.WaterSurface) < 0.05f)
                        type = 3;
                }
                Type[i] = type;
            }
        }

        static readonly string[] TypeNames = { "dry", "sea", "river", "lake", "pool" };

        /// <summary>Runs both passes over the live TerrainSource. step is the grid spacing in metres.</summary>
        public static Result Measure(TerrainSource s, float step = 4f)
        {
            var result = new Result { Step = step };
            for (int k = 0; k < Kinds; k++)
                result.Hits[k] = new List<Hit>();
            if (!s.Rivers.IsCreated)
                return Last = result;
            Graph(s, result);
            Grid(s, result, step);
            for (int k = 0; k < Kinds; k++)
            {
                result.Counts[k] = result.Hits[k].Count;
                var places = new HashSet<long>();
                foreach (var h in result.Hits[k])
                    places.Add(((long)math.floor(h.At.x / PlaceSize) << 32) ^ (uint)(int)math.floor(h.At.z / PlaceSize));
                result.Places[k] = places.Count;
                result.Hits[k].Sort((a, b) => b.Score.CompareTo(a.Score));
            }
            return Last = result;
        }

        /// <summary>The river graph: every cell's bend, width, end, source and levels, sampled along its centreline, and every fall.</summary>
        static void Graph(TerrainSource s, Result result)
        {
            ref var r = ref s.Rivers.Value;
            int n = r.Width.Length;
            var feeders = new int[n];
            for (int i = 0; i < n; i++)
                if (r.Width[i] > 0f && r.Down[i] >= 0)
                    feeders[r.Down[i]]++;

            // Sample points: per river cell its start, its mid curve, and both banks at the mid curve; per fall just
            // above the lip and at the landing.
            var cells = new List<int>();
            var points = new List<float2>();
            for (int i = 0; i < n; i++)
            {
                if (r.Width[i] <= 0f)
                    continue;
                cells.Add(i);
                int down = r.Down[i];
                var a = r.Point(i);
                var mid = a;
                var flow = float2.zero;
                if (down >= 0)
                    mid = RiverSampling.CurveMid(ref r, i, out flow);
                var side = new float2(-flow.y, flow.x) * r.Width[i] * 0.3f;
                points.Add(a);
                points.Add(mid);
                points.Add(mid + side);
                points.Add(mid - side);
                points.Add(down >= 0 ? r.Point(down) : a);
            }
            int fallBase = points.Count;
            for (int f = 0; f < r.Falls.Length; f++)
            {
                points.Add(r.Falls[f].Lip.xz - r.Falls[f].Out * 3f);
                points.Add(r.Falls[f].Lip.xz + r.Falls[f].Out * 4f);
            }
            Sample(s, points, out var ground, out var surface, out var type);

            result.RiverCells = cells.Count;
            result.Falls = r.Falls.Length;
            var hits = result.Hits;
            for (int c = 0; c < cells.Count; c++)
            {
                int i = cells[c], down = r.Down[i], up = r.Up[i], b = c * 5;
                var a = r.Point(i);
                var lake = r.LakeDepth[i] > 0f;
                var runs = down >= 0 && r.Width[down] > 0f;
                float At(int k) => math.max(surface[b + k], ground[b + k]);
                bool Wet(int k) => surface[b + k] > ground[b + k] + WetDepth;
                var here = new float3(a.x, At(0), a.y);
                var mid = new float3(points[b + 1].x, At(1), points[b + 1].y);

                // (a) A bend tighter than half the width folds the inside bank onto itself.
                if (up >= 0 && runs && !lake)
                {
                    var before = a - r.Point(up);
                    var after = r.Point(down) - a;
                    if (math.lengthsq(before) > 1e-4f && math.lengthsq(after) > 1e-4f)
                    {
                        var turn = math.acos(math.clamp(math.dot(math.normalize(before), math.normalize(after)), -1f, 1f));
                        var radius = math.min(math.length(before), math.length(after)) / (2f * math.max(math.tan(turn * 0.5f), 1e-4f));
                        if (radius < r.Width[i] * 0.5f)
                            hits[Bend].Add(new Hit { Score = r.Width[i] * 0.5f - radius, At = here, A = math.degrees(turn), B = r.Width[i] });
                    }
                }
                // (a) Width along the main stem: narrowing downstream, or widening by half with nothing joining.
                if (runs && r.Up[down] == i && !lake && r.LakeDepth[down] <= 0f)
                {
                    var ratio = r.Width[down] / r.Width[i];
                    if (ratio < 0.75f || (ratio > 1.5f && feeders[down] == 1))
                        hits[WidthJump].Add(new Hit { Score = math.max(ratio, 1f / ratio), At = here, A = r.Width[i], B = r.Width[down] });
                }
                // (b) An end that is neither sea nor lake.
                if (!runs)
                {
                    int e = down >= 0 ? down : i;
                    var endGround = ground[b + 4];
                    if (r.LakeDepth[e] <= 0f && !lake && endGround > s.World.SeaLevel + 0.5f)
                        hits[DeadEnd].Add(new Hit { Score = r.Width[i], At = new float3(points[b + 4].x, endGround, points[b + 4].y), A = endGround - s.World.SeaLevel, B = r.Width[i] });
                }
                // (b) A source: dry at its spring, or already wide where it starts.
                if (up < 0 && !lake)
                {
                    result.Sources++;
                    var wide = r.Width[i] > s.RiverSettings.MinWidth * 2f;
                    if (!Wet(0) || wide)
                        hits[Source].Add(new Hit { Score = (Wet(0) ? 0f : 100f) + r.Width[i], At = here, A = Wet(0) ? 1f : 0f, B = r.Width[i] });
                }
                // (b, c) The centreline out of the water: the bed stands above the surface, or there is none.
                if (!lake)
                    for (int k = 0; k < 2; k++)
                        if (!Wet(k) && (k == 0 || down >= 0))
                        {
                            var none = type[b + k] == 0;
                            // Ground well above what the river carved here was raised afterwards (a paddy, a landmark, a plot).
                            var carved = RiverSampling.Query(ref r, s.RiverSettings, points[b + k], s.RiverRoughOn).Carve;
                            hits[DryRun].Add(new Hit { Score = none ? 50f : ground[b + k] - surface[b + k], At = k == 0 ? here : mid, A = none ? 1f : 0f, B = r.Width[i], I = ground[b + k] > carved + 2f ? 1 : 0 });
                            break;
                        }
                // (c) Levels in the graph rising downstream.
                if (runs && (r.WaterLevel[down] > r.WaterLevel[i] + 0.01f || r.Bed[down] > r.Bed[i] + 0.01f))
                    hits[UphillGraph].Add(new Hit { Score = math.max(r.WaterLevel[down] - r.WaterLevel[i], r.Bed[down] - r.Bed[i]), At = here, A = r.WaterLevel[down] - r.WaterLevel[i], B = r.Bed[down] - r.Bed[i] });
                // (c) The sampled surface rising downstream: start to mid, mid to the next cell.
                if (down >= 0)
                {
                    var rise = 0f;
                    if (Wet(0) && Wet(1))
                        rise = math.max(rise, surface[b + 1] - surface[b]);
                    if (Wet(1) && Wet(4))
                        rise = math.max(rise, surface[b + 4] - surface[b + 1]);
                    if (rise > Rise)
                        hits[UphillSampled].Add(new Hit { Score = rise, At = mid, A = rise, B = r.Width[i] });
                }
                // (c) River water much deeper than the channel that was dug for it.
                if (!lake && down >= 0 && type[b + 1] == 2 && Wet(1))
                {
                    var depth = surface[b + 1] - ground[b + 1];
                    var dug = RiverSampling.Depth(r.Width[i], s.RiverSettings);
                    if (depth > dug + DeepOver)
                        hits[Deep].Add(new Hit { Score = depth - dug, At = mid, A = depth, B = dug });
                }
                // (c) The surface tilted from bank to bank.
                if (!lake && down >= 0 && surface[b + 2] > ground[b + 2] + WetDepth && surface[b + 3] > ground[b + 3] + WetDepth)
                {
                    var tilt = math.abs(surface[b + 2] - surface[b + 3]);
                    if (tilt > TiltAcross)
                        hits[Tilt].Add(new Hit { Score = tilt, At = mid, A = tilt, B = r.Width[i] });
                }
            }

            // (d) Falls: the sheet's top against the water behind it, its foot against the water it lands in.
            for (int f = 0; f < r.Falls.Length; f++)
            {
                var fall = r.Falls[f];
                int b = fallBase + f * 2;
                var headWet = surface[b] > ground[b] + WetDepth;
                var headOff = headWet ? math.abs(surface[b] - fall.Lip.y) : 100f;
                var headDeep = headWet ? surface[b] - ground[b] : 0f;
                if (headOff > 0.3f || headDeep > FallDeep)
                    hits[FallHead].Add(new Hit { Score = math.max(headOff, headDeep), At = fall.Lip, A = headOff, B = headDeep, I = f });
                var landing = fall.Lip.y - fall.Drop;
                var footWet = surface[b + 1] > ground[b + 1] + WetDepth;
                var footOff = footWet ? math.abs(surface[b + 1] - landing) : 100f;
                if (footOff > 0.5f)
                    hits[FallFoot].Add(new Hit { Score = footOff, At = new float3(points[b + 1].x, landing, points[b + 1].y), A = footOff, B = fall.Drop, I = f });
            }
        }

        /// <summary>The whole island on a grid: water standing above dry ground beside it, and steps between neighbouring surfaces.</summary>
        static void Grid(TerrainSource s, Result result, float step)
        {
            ref var r = ref s.Rivers.Value;
            int side = (int)math.ceil(r.Resolution * r.CellSize / step);
            var ground = new NativeArray<float>(side * side, Allocator.TempJob);
            var surface = new NativeArray<float>(side * side, Allocator.TempJob);
            var type = new NativeArray<byte>(side * side, Allocator.TempJob);
            var none = new NativeArray<float2>(0, Allocator.TempJob);
            new SampleJob { Source = s, Origin = r.Origin, Step = step, Side = side, Points = none, Ground = ground, Surface = surface, Type = type }
                .Schedule(side * side, 4096).Complete();
            none.Dispose();

            result.Samples = side * side;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int i = z * side + x;
                if (surface[i] > ground[i] + WetDepth)
                    result.WetSamples++;
                // Each pair of neighbours once: the one to the right and the one above.
                for (int d = 0; d < 2; d++)
                {
                    int j = d == 0 ? i + 1 : i + side;
                    if ((d == 0 && x == side - 1) || (d == 1 && z == side - 1))
                        continue;
                    var wetI = surface[i] > ground[i] + WetDepth;
                    var wetJ = surface[j] > ground[j] + WetDepth;
                    if (!wetI && !wetJ)
                        continue;
                    // hi holds the higher water (or the only water).
                    int hi = i, lo = j;
                    if (!wetI || (wetJ && surface[j] > surface[i]))
                    {
                        hi = j;
                        lo = i;
                    }
                    var wetLo = hi == i ? wetJ : wetI;
                    var gap = wetLo ? surface[hi] - surface[lo] : surface[hi] - ground[lo];
                    if (gap <= (wetLo ? StepHeight : WallHeight))
                        continue;
                    var hiAt = r.Origin + (new float2(hi % side, hi / side) + 0.5f) * step;
                    var loAt = r.Origin + (new float2(lo % side, lo / side) + 0.5f) * step;
                    if (AtFallLip(ref r, hiAt, loAt, step))
                    {
                        result.FallSteps++;
                        continue;
                    }
                    result.Hits[wetLo ? Step : Wall].Add(new Hit
                    {
                        Score = gap, At = new float3(hiAt.x, surface[hi], hiAt.y), A = surface[hi], B = wetLo ? surface[lo] : ground[lo],
                        I = type[hi] * 8 + type[lo]
                    });
                    var split = $"{(wetLo ? "step" : "wall")}: {TypeNames[type[hi]]} above {(wetLo ? TypeNames[type[lo]] : "dry ground")}{(NearFall(ref r, hiAt) ? ", by a fall" : "")}";
                    result.Split.TryGetValue(split, out var seen);
                    result.Split[split] = seen + 1;
                }
            }
            ground.Dispose();
            surface.Dispose();
            type.Dispose();
        }

        /// <summary>A drop from hi to lo is a fall's own sheet: hi sits on the lip line, inside the fall's width, and lo is downstream of it.</summary>
        static bool AtFallLip(ref RiverMapBlob r, float2 hi, float2 lo, float step)
        {
            var dir = math.normalizesafe(lo - hi);
            for (int f = 0; f < r.Falls.Length; f++)
            {
                var rel = hi - r.Falls[f].Lip.xz;
                var o = r.Falls[f].Out;
                var along = math.dot(rel, o);
                var across = math.abs(rel.x * o.y - rel.y * o.x);
                if (across <= r.Falls[f].Width * 0.5f + step * 0.5f && along >= -step - 1f && along <= 1f && math.dot(dir, o) > 0.5f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Seams between the water meshes of loaded chunks round a place: every wet vertex on a shared border is
        /// compared with the neighbour's water along that border. Returns { borders checked, wet vertices compared,
        /// gaps over 5 cm, open ends (water above the ground that the neighbour doesn't continue), largest gap in mm }.
        /// Only what is loaded can be checked, so this is a spot check per place, not an island count. size is the chunk size.
        /// </summary>
        public static int[] Seams(TerrainSource s, TerrainStreamingSystem streaming, float size, float2 centre, int radius)
        {
            var result = new int[5];
            var edges = new Dictionary<int2, List<float3>[]>();
            var first = (int2)math.floor(centre / size);
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                {
                    var coord = first + new int2(x, z);
                    if (streaming.TryGetWater(coord, out var mesh, out _, out _) && mesh != null && mesh.vertexCount > 0)
                        edges[coord] = Edges(mesh, size);
                    else if (streaming.TryGetLod(coord, out _))   // loaded, no water drawn: its neighbours' water must end at ground
                        edges[coord] = new[] { new List<float3>(), new List<float3>(), new List<float3>(), new List<float3>() };
                }
            foreach (var kv in edges)
                for (int axis = 0; axis < 2; axis++)
                {
                    var other = kv.Key + (axis == 0 ? new int2(1, 0) : new int2(0, 1));
                    if (!edges.TryGetValue(other, out var b))
                        continue;
                    // This chunk's far edge against the neighbour's near edge, then the other way round.
                    var far = kv.Value[axis * 2 + 1];
                    var near = b[axis * 2];
                    if (far.Count == 0 && near.Count == 0)
                        continue;
                    result[0]++;
                    var origin = (float2)kv.Key * size;
                    Compare(s, far, near, origin, axis, size, result);
                    Compare(s, near, far, origin, axis, size, result);
                }
            return result;
        }

        /// <summary>A chunk water mesh's border vertices as (along, height, wet 1/0), sorted along: -x, +x, -z, +z.</summary>
        static List<float3>[] Edges(UnityEngine.Mesh mesh, float size)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var wet = new bool[vertices.Length];
            foreach (var t in triangles)
                wet[t] = true;
            var edges = new[] { new List<float3>(), new List<float3>(), new List<float3>(), new List<float3>() };
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                var w = wet[i] ? 1f : 0f;
                if (v.x < 0.01f) edges[0].Add(new float3(v.z, v.y, w));
                if (v.x > size - 0.01f) edges[1].Add(new float3(v.z, v.y, w));
                if (v.z < 0.01f) edges[2].Add(new float3(v.x, v.y, w));
                if (v.z > size - 0.01f) edges[3].Add(new float3(v.x, v.y, w));
            }
            foreach (var e in edges)
                e.Sort((a, b) => a.x.CompareTo(b.x));
            return edges;
        }

        /// <summary>Each wet vertex of one border against the other border's water at the same point along it.</summary>
        static void Compare(TerrainSource s, List<float3> from, List<float3> to, float2 origin, int axis, float size, int[] result)
        {
            foreach (var v in from)
            {
                if (v.z < 0.5f)
                    continue;
                result[1]++;
                // The two vertices of the other border that bracket this point.
                int hi = 0;
                while (hi < to.Count - 1 && to[hi].x < v.x - 0.001f)
                    hi++;
                int lo = math.max(hi - 1, 0);
                if (to.Count > 0 && math.abs(to[hi].x - v.x) < 0.001f)
                    lo = hi;
                if (to.Count == 0 || to[lo].z < 0.5f || to[hi].z < 0.5f)
                {
                    // Not continued: fine at a bank (the water meets the ground), an open end if it stands above it.
                    var along = axis == 0 ? new float2(size, v.x) : new float2(v.x, size);
                    if (v.y > s.SampleHeight(origin + along) + WetDepth)
                        result[3]++;
                    continue;
                }
                var span = to[hi].x - to[lo].x;
                var there = span > 0.001f ? math.lerp(to[lo].y, to[hi].y, (v.x - to[lo].x) / span) : to[hi].y;
                var gap = math.abs(v.y - there);
                if (gap > WetDepth)
                    result[2]++;
                result[4] = math.max(result[4], (int)math.round(gap * 1000f));
            }
        }

        /// <summary>Within a fall's reach: its half width plus 30 m from the lip.</summary>
        static bool NearFall(ref RiverMapBlob r, float2 at)
        {
            for (int f = 0; f < r.Falls.Length; f++)
                if (math.distance(at, r.Falls[f].Lip.xz) < r.Falls[f].Width * 0.5f + 30f)
                    return true;
            return false;
        }

        static void Sample(TerrainSource s, List<float2> points, out float[] ground, out float[] surface, out byte[] type)
        {
            var p = new NativeArray<float2>(points.ToArray(), Allocator.TempJob);
            var g = new NativeArray<float>(p.Length, Allocator.TempJob);
            var w = new NativeArray<float>(p.Length, Allocator.TempJob);
            var t = new NativeArray<byte>(p.Length, Allocator.TempJob);
            new SampleJob { Source = s, Points = p, Ground = g, Surface = w, Type = t }.Schedule(p.Length, 256).Complete();
            ground = g.ToArray();
            surface = w.ToArray();
            type = t.ToArray();
            p.Dispose();
            g.Dispose();
            w.Dispose();
            t.Dispose();
        }

        /// <summary>The worst hits of one kind, no two within apart metres of each other.</summary>
        public static List<Hit> Worst(Result result, int kind, int count = 10, float apart = 80f)
        {
            var picked = new List<Hit>();
            foreach (var h in result.Hits[kind])
            {
                var near = false;
                foreach (var p in picked)
                    near |= math.distance(p.At.xz, h.At.xz) < apart;
                if (near)
                    continue;
                picked.Add(h);
                if (picked.Count >= count)
                    break;
            }
            return picked;
        }

        /// <summary>One line for a hit of the given kind.</summary>
        public static string Describe(int kind, Hit h)
        {
            var at = $"({h.At.x:F0}, {h.At.y:F1}, {h.At.z:F0})";
            switch (kind)
            {
                case Bend: return $"{at} turn {h.A:F0} deg in a {h.B:F1} m river, inside bank {h.Score:F1} m short";
                case WidthJump: return $"{at} width {h.A:F1} -> {h.B:F1} m in one cell";
                case DeadEnd: return $"{at} a {h.B:F1} m river ends on ground {h.A:F1} m above the sea, no lake";
                case Source: return $"{at} source {h.B:F1} m wide{(h.A < 0.5f ? ", dry at its spring" : "")}";
                case DryRun: return $"{at} centreline of a {h.B:F1} m river {(h.A > 0.5f ? "has no water" : $"stands {h.Score:F2} m above its water")}{(h.I == 1 ? " (ground raised over the carved channel)" : "")}";
                case UphillGraph: return $"{at} water level {h.A:+0.00;-0.00} m, bed {h.B:+0.00;-0.00} m to the next cell down";
                case UphillSampled: return $"{at} surface rises {h.A:F2} m downstream ({h.B:F1} m river)";
                case Wall: return $"{at} {TypeNames[h.I / 8]} water at {h.A:F1} stands {h.Score:F1} m above dry ground at {h.B:F1} one sample away";
                case Deep: return $"{at} river water {h.A:F1} m deep in a channel dug {h.B:F1} m";
                case Tilt: return $"{at} surface differs {h.A:F2} m bank to bank ({h.B:F1} m river)";
                case Step: return $"{at} {TypeNames[h.I / 8]} at {h.A:F1} beside {TypeNames[h.I % 8]} at {h.B:F1}: a {h.Score:F1} m step";
                case FallHead: return $"{at} fall {h.I}: water behind the lip {(h.A > 99f ? "missing" : $"{h.A:F2} m off the lip")}, {h.B:F1} m deep";
                case FallFoot: return $"{at} fall {h.I} ({h.B:F1} m drop): landing water {(h.Score > 99f ? "missing" : $"{h.Score:F1} m off the foot")}";
            }
            return at;
        }

        /// <summary>Counts and the worst of each kind, as text for a report.</summary>
        public static string Report(Result result, int worst = 10)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"river cells {result.RiverCells}, sources {result.Sources}, falls {result.Falls}, grid {result.Step:F0} m: {result.Samples} samples, {result.WetSamples} wet, {result.FallSteps} steps at fall lips");
            for (int k = 0; k < Kinds; k++)
            {
                sb.AppendLine($"{Names[k]}: {result.Counts[k]} in {result.Places[k]} places");
                foreach (var h in Worst(result, k, worst))
                    sb.AppendLine("  " + Describe(k, h));
            }
            foreach (var kv in result.Split)
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            return sb.ToString();
        }
    }
}
#endif
