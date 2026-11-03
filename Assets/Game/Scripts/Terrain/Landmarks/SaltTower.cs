using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// One salt tower's layout (W-27), derived from its landmark instance alone, so every machine builds the same
    /// tower: where the spiral starts and which way it turns, how many steps it has and where its two tunnels are.
    /// </summary>
    public struct SaltTowerLayout
    {
        public float2 Centre;
        /// <summary>Ground level at the foot (the highest ground under the tower) and the summit's height above it (m).</summary>
        public float Base, Height;
        /// <summary>Angle (radians) of the first step round the centre, and +1 or -1 for the way the spiral turns.</summary>
        public float StartAngle, Dir;
        /// <summary>Steps in all; the steps on the first arc, the second arc, and across each tunnel.</summary>
        public int Steps, Arc1, Arc2, Chord;
        /// <summary>Rise from one step to the next (m) and the angle between steps on an arc.</summary>
        public float Rise, ArcAngle;
    }

    /// <summary>
    /// The salt tower's shape (W-27): a basalt column about 7.5 m in radius, a spiral of column-stub steps round it at
    /// PathRadius, each MaxRise or less above the last (under the player's step up of 0.4 m) and overlapping the next
    /// so there's no gap to fall through, two straight tunnels across the body (the spiral cuts through the middle
    /// and comes out on the far side), and a flat summit. TerrainSource.FloorUnder stands things on the steps and
    /// summit, and TerrainSource.TowerWall keeps bodies out of the column and from walking into steps from below.
    /// It's all analytic (no blob), so the steps and walls the player meets are exactly the ones drawn.
    /// </summary>
    public static class SaltTowerShape
    {
        public const float BodyRadius = 7.5f, PathRadius = 8.4f;
        /// <summary>A step's standing area (radius) and its drawn hexagon's corner radius (about 2 m across).</summary>
        public const float StepRadius = 1.05f, StepDrawRadius = 1.15f;
        public const float Spacing = 0.9f, StepThick = 1.6f, MaxRise = 0.3f;
        public const float SummitRadius = 8f, SummitThick = 1.8f;
        public const float TunnelHalfWidth = 1.8f, TunnelHeight = 3.4f;
        /// <summary>How far a top may be above a point and still be stood on (the steps rise less than this).</summary>
        public const float FloorReach = 0.45f;
        /// <summary>A body's radius, kept off the column.</summary>
        public const float Margin = 0.3f;
        /// <summary>Nothing of the tower's collision reaches further from the centre than this.</summary>
        public const float Reach = 11f;

        public static SaltTowerLayout Layout(in LandmarkInstance tower, float towerHeight)
        {
            var l = new SaltTowerLayout
            {
                Centre = tower.Position,
                Base = tower.BaseHeight,
                Height = towerHeight * (0.9f + 0.2f * math.frac(tower.Seed)),
                StartAngle = math.atan2(tower.Facing.y, tower.Facing.x),
                Dir = (tower.Key & 2u) != 0 ? 1f : -1f,
                ArcAngle = Spacing / PathRadius,
                Chord = (int)math.ceil(2f * PathRadius / Spacing)
            };
            l.Steps = (int)math.ceil(l.Height / MaxRise);
            l.Rise = l.Height / l.Steps;
            var arcs = l.Steps - 2 * l.Chord;
            l.Arc1 = (int)(arcs * 0.38f);
            l.Arc2 = (int)(arcs * 0.34f);
            return l;
        }

        static float2 Ring(in SaltTowerLayout l, float angle) => l.Centre + PathRadius * new float2(math.cos(angle), math.sin(angle));

        /// <summary>Where tunnel t (0 or 1) starts and ends, and the index of its first step.</summary>
        public static void Tunnel(in SaltTowerLayout l, int t, out float2 a, out float2 b, out int first)
        {
            var angle = l.StartAngle + l.Dir * l.Arc1 * l.ArcAngle;
            first = l.Arc1;
            if (t == 1)
            {
                angle += math.PI + l.Dir * l.Arc2 * l.ArcAngle;
                first += l.Chord + l.Arc2;
            }
            a = Ring(l, angle);
            b = Ring(l, angle + math.PI);
        }

        /// <summary>Step i's centre and the height of its top: arcs round the outside, chords through the tunnels.</summary>
        public static void Step(in SaltTowerLayout l, int i, out float2 xz, out float top)
        {
            top = l.Base + l.Rise * (i + 1);
            var angle = l.StartAngle;
            var j = i;
            for (int segment = 0; segment < 4; segment++)
            {
                var arcSteps = segment == 0 ? l.Arc1 : l.Arc2;
                if (segment % 2 == 0)
                {
                    if (j < arcSteps)
                    {
                        xz = Ring(l, angle + l.Dir * j * l.ArcAngle);
                        return;
                    }
                    angle += l.Dir * arcSteps * l.ArcAngle;
                    j -= arcSteps;
                }
                else
                {
                    if (j < l.Chord)
                    {
                        xz = math.lerp(Ring(l, angle), Ring(l, angle + math.PI), (float)j / l.Chord);
                        return;
                    }
                    angle += math.PI;
                    j -= l.Chord;
                }
            }
            xz = Ring(l, angle + l.Dir * j * l.ArcAngle);
        }

        /// <summary>The highest step or summit top under p that's no more than FloorReach above it, if any.</summary>
        public static bool Floor(in SaltTowerLayout l, float3 p, out float top)
        {
            top = float.MinValue;
            var found = false;
            var r2 = math.distancesq(p.xz, l.Centre);
            if (r2 > Reach * Reach)
                return false;
            var limit = p.y + FloorReach;
            var summit = l.Base + l.Height;
            if (r2 < SummitRadius * SummitRadius && summit <= limit)
            {
                top = summit;
                found = true;
            }
            // Tops rise with the index, so only the steps up to the limit can be stood on.
            var last = math.min(l.Steps - 1, (int)math.floor((limit - l.Base) / l.Rise));
            for (int i = 0; i <= last; i++)
            {
                Step(l, i, out var xz, out var t);
                if (t > top && math.distancesq(xz, p.xz) < StepRadius * StepRadius)
                {
                    top = t;
                    found = true;
                }
            }
            return found;
        }

        static bool Overlap(float lo, float hi, float a, float b) => hi > a && lo < b;

        /// <summary>
        /// Whether a body with its feet at p would stand in the column (outside the tunnels), in the summit slab, or in
        /// a step whose top is more than stepUp above the feet (walking into it from the side or below).
        /// </summary>
        public static bool Wall(in SaltTowerLayout l, float3 p, float stepUp, float head)
        {
            var r = math.distance(p.xz, l.Centre);
            if (r > Reach)
                return false;
            float lo = p.y + stepUp, hi = p.y + head;
            var summit = l.Base + l.Height;
            if (r < BodyRadius + Margin && Overlap(l.Base - 3f, summit, lo, hi) && !InTunnel(l, p))
                return true;
            if (r < SummitRadius && Overlap(summit - SummitThick, summit, lo, hi))
                return true;
            var first = math.max(0, (int)math.floor((lo - l.Base) / l.Rise) - 1);
            var last = math.min(l.Steps - 1, (int)math.ceil((hi + StepThick - l.Base) / l.Rise));
            for (int i = first; i <= last; i++)
            {
                Step(l, i, out var xz, out var t);
                if (Overlap(t - StepThick, t, lo, hi) && math.distancesq(xz, p.xz) < StepRadius * StepRadius)
                    return true;
            }
            return false;
        }

        /// <summary>The floor of tunnel t at a point along it (s 0 at its start, 1 at its end).</summary>
        public static float TunnelFloor(in SaltTowerLayout l, int first, float s) => l.Base + l.Rise * (first + 1 + s * l.Chord);

        /// <summary>Whether feet at p are in one of the tunnels (near its floor, within its width).</summary>
        static bool InTunnel(in SaltTowerLayout l, float3 p)
        {
            for (int t = 0; t < 2; t++)
            {
                Tunnel(l, t, out var a, out var b, out var first);
                var ab = b - a;
                var s = math.dot(p.xz - a, ab) / math.lengthsq(ab);
                if (s < -0.05f || s > 1.05f || math.distance(p.xz, a + ab * s) > TunnelHalfWidth)
                    continue;
                var floor = TunnelFloor(l, first, math.saturate(s));
                if (p.y > floor - 1f && p.y < floor + 1.2f)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Places the one-per-island landmarks (W-27: the salt tower) after everything else, roads and roadside landmarks
    /// included, so no other landmark, settlement or road moves. The spot is the deepest point of the largest patch
    /// of its biome (a 40 m grid, 4-connected), dry, nearly level, clear of every landmark's footprint and at least
    /// SettlementClearance from any settlement.
    /// </summary>
    public static class SaltTowerBaking
    {
        const float Cell = 40f, Extent = 5120f, SettlementClearance = 300f, MaxRelief = 0.15f;

        /// <summary>The last placement's log line(s).</summary>
        public static string LastLog = "";

        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Size;
            public float Cell, Extent;
            /// <summary>Per biome id: 1 if it lists the type.</summary>
            [ReadOnly] public NativeArray<byte> Listed;
            [WriteOnly] public NativeArray<byte> Ok;

            public void Execute(int i)
            {
                var p = new float2(i % Size, i / Size) * Cell - Extent;
                var g = Source.SampleGround(p);
                Ok[i] = 0;
                if (g.Height < 2f || g.River > 0.02f || g.WaterDepth > -1.5f || g.HasWater && g.WaterSurface > g.Height - 1f)
                    return;
                var weight = 0f;
                for (int k = 0; k < 4; k++)
                    if (g.Biomes.Ids[k] >= 0 && g.Biomes.Ids[k] < Listed.Length &&Listed[g.Biomes.Ids[k]] != 0)
                        weight += g.Biomes.Weights[k];
                Ok[i] = (byte)(weight >= 0.75f ? 1 : 0);
            }
        }

        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types)
        {
            var result = new List<LandmarkInstance>();
            LastLog = "";
            // W-32: towers first, exactly as before; the temple then takes its own spot clear of them.
            for (int pass = 0; pass < 2; pass++)
                for (int t = 0; t < types.Count; t++)
                    if (types[t] != null && types[t].OnePerIsland && types[t].Temple == (pass == 1) && TryPlace(source, palette, types, t, result, out var inst))
                        result.Add(inst);
            return result;
        }

        static bool TryPlace(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types, int t,
                             List<LandmarkInstance> placedSoFar, out LandmarkInstance inst)
        {
            inst = default;
            var type = types[t];
            var size = (int)(2f * Extent / Cell) + 1;
            var count = size * size;
            var listed = new NativeArray<byte>(palette.Count, Allocator.TempJob);
            var any = false;
            for (int b = 0; b < palette.Count; b++)
                if (palette[b] != null && palette[b].Landmarks.Exists(l => l.Type == type))
                {
                    listed[b] = 1;
                    any = true;
                }
            var ok = new NativeArray<byte>(count, Allocator.TempJob);
            if (any)
                new SampleJob { Source = source, Size = size, Cell = Cell, Extent = Extent, Listed = listed, Ok = ok }.Schedule(count, 256).Complete();
            var okCells = ok.ToArray();
            listed.Dispose();
            ok.Dispose();
            if (!any)
                return false;

            // Label the patches (4-connected) and keep the largest.
            var label = new int[count];
            int best = 0, bestSize = 0, next = 0;
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (okCells[i] == 0 || label[i] != 0)
                    continue;
                next++;
                var n = 0;
                label[i] = next;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    n++;
                    foreach (var nb in Neighbours(c, size))
                        if (nb >= 0 && okCells[nb] != 0 && label[nb] == 0)
                        {
                            label[nb] = next;
                            queue.Enqueue(nb);
                        }
                }
                if (n > bestSize)
                {
                    bestSize = n;
                    best = next;
                }
            }
            if (bestSize == 0)
            {
                LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[landmarks] {type.name}: no patch of its biome";
                UnityEngine.Debug.Log(LastLog);
                return false;
            }

            // Depth: cells in from the patch's edge (a breadth-first pass from the cells just outside it).
            var depth = new int[count];
            for (int i = 0; i < count; i++)
            {
                depth[i] = label[i] == best ? int.MaxValue : 0;
                if (label[i] != best)
                    continue;
                foreach (var nb in Neighbours(i, size))
                    if (nb < 0 || label[nb] != best)
                    {
                        depth[i] = 1;
                        queue.Enqueue(i);
                        break;
                    }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var nb in Neighbours(c, size))
                    if (nb >= 0 && depth[nb] == int.MaxValue)
                    {
                        depth[nb] = depth[c] + 1;
                        queue.Enqueue(nb);
                    }
            }
            var order = new List<int>();
            for (int i = 0; i < count; i++)
                if (label[i] == best)
                    order.Add(i);
            order.Sort((a, b) => depth[b] != depth[a] ? depth[b].CompareTo(depth[a]) : a.CompareTo(b));

            // The deepest spot that's level and clear; failing that, the most level clear spot of the deepest few hundred.
            ref var set = ref source.Landmarks.Value;
            var footprint = type.Radius;
            int chosen = -1, fallback = -1;
            float chosenRelief = 0f, chosenBase = 0f, fallbackRelief = float.MaxValue, fallbackBase = 0f;
            for (int o = 0; o < order.Count && o < 600 && chosen < 0; o++)
            {
                var c = order[o];
                var p = new float2(c % size, c / size) * Cell - Extent;
                if (!Clear(ref set, types, p, footprint) || !ClearOf(placedSoFar, types, p, footprint))
                    continue;
                Relief(source, p, out var relief, out var top);
                if (relief <= MaxRelief)
                {
                    chosen = c;
                    chosenRelief = relief;
                    chosenBase = top;
                }
                else if (relief < fallbackRelief)
                {
                    fallback = c;
                    fallbackRelief = relief;
                    fallbackBase = top;
                }
            }
            if (chosen < 0)
            {
                chosen = fallback;
                chosenRelief = fallbackRelief;
                chosenBase = fallbackBase;
            }
            if (chosen < 0)
            {
                LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[landmarks] {type.name}: no clear spot in its largest patch ({bestSize} cells)";
                UnityEngine.Debug.Log(LastLog);
                return false;
            }

            var at = new float2(chosen % size, chosen / size) * Cell - Extent;
            var key = math.hash(new uint3(math.asuint(at.x), math.asuint(at.y), type.Temple ? 0x5E7Au : 0x7A3Bu)) | 1u;
            var spin = BiomeSampling.Scramble(key);
            var facing = (spin & 0xFFFF) * (2f * math.PI / 65536f);
            inst = new LandmarkInstance
            {
                Position = at,
                Facing = new float2(math.cos(facing), math.sin(facing)),
                Scale = 1f,
                Type = t,
                BaseHeight = type.Temple ? TempleBase(source, at, new float2(math.cos(facing), math.sin(facing))) : chosenBase,
                Seed = (BiomeSampling.Scramble(spin) >> 8) * (1f / 16777216f),
                Key = key
            };
            if (type.Temple)
            {
                LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[landmarks] {type.name} at {at.x:F1},{at.y:F1}: facing {math.degrees(facing):F0} deg, base {inst.BaseHeight:F1} " +
                           $"(spot top {chosenBase:F1}), relief {chosenRelief:F2} m, patch {bestSize} cells, {depth[chosen] * Cell:F0} m in from its edge";
                UnityEngine.Debug.Log(LastLog);
                return true;
            }
            var layout = SaltTowerShape.Layout(inst, type.TowerHeight);
            LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[landmarks] {type.name} at {at.x:F1},{at.y:F1}: height {layout.Height:F1} m above base {chosenBase:F1}, " +
                      $"relief {chosenRelief:F2} m, {layout.Steps} steps of {layout.Rise:F3} m, patch {bestSize} cells " +
                      $"({bestSize * Cell * Cell / 1e6f:F1} km2), {depth[chosen] * Cell:F0} m in from its edge";
            UnityEngine.Debug.Log(LastLog);
            return true;
        }

        /// <summary>W-32: the sunken temple's base, just above the highest ground under its rooms, so no room has ground in it.</summary>
        static float TempleBase(in TerrainSource source, float2 at, float2 f)
        {
            var r = new float2(-f.y, f.x);
            var top = float.MinValue;
            for (float u = -30f; u <= 30f; u += 4f)
                for (float v = -12f; v <= 12f; v += 4f)
                    top = math.max(top, source.SampleHeight(at + u * f + v * r));
            return top + 0.3f;
        }

        static IEnumerable<int> Neighbours(int c, int size)
        {
            int x = c % size, y = c / size;
            yield return x > 0 ? c - 1 : -1;
            yield return x < size - 1 ? c + 1 : -1;
            yield return y > 0 ? c - size : -1;
            yield return y < size - 1 ? c + size : -1;
        }

        /// <summary>Clear of every landmark's footprint (plus 40 m) and SettlementClearance from any settlement.</summary>
        static bool Clear(ref LandmarkSetBlob set, IReadOnlyList<LandmarkType> types, float2 p, float footprint)
        {
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var other = set.Instances[i];
                var type = other.Type < types.Count ? types[other.Type] : null;
                var reach = type != null ? type.Radius * other.Scale : 30f;
                var d = math.distance(other.Position, p);
                if (d < reach + footprint + 40f || type != null && type.Settlement != null && d < SettlementClearance)
                    return false;
            }
            return true;
        }

        static bool ClearOf(List<LandmarkInstance> placed, IReadOnlyList<LandmarkType> types, float2 p, float footprint)
        {
            foreach (var other in placed)
                if (math.distance(other.Position, p) < types[other.Type].Radius + footprint + 40f)
                    return false;
            return true;
        }

        /// <summary>How uneven the ground is under the tower and its steps (max - min) and its highest point.</summary>
        static void Relief(in TerrainSource source, float2 p, out float relief, out float top)
        {
            float lo = source.SampleHeight(p), hi = lo;
            for (int ring = 1; ring <= 3; ring++)
                for (int k = 0; k < 12; k++)
                {
                    var a = k * math.PI / 6f + ring * 0.3f;
                    var h = source.SampleHeight(p + ring * 3.3f * new float2(math.cos(a), math.sin(a)));
                    lo = math.min(lo, h);
                    hi = math.max(hi, h);
                }
            relief = hi - lo;
            top = hi;
        }
    }
}
