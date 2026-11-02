using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>One iceberg (W-28). Kind 0 is a small chunk or floe, 1 a big tabular berg, 2 the big berg with the ice cave.</summary>
    public struct IcebergData
    {
        public float2 Centre;
        /// <summary>Top radius (m), height above the sea and draft below it, rotation and the ramp's direction (radians).</summary>
        public float Radius, Height, Draft, Rotation, Ramp;
        /// <summary>How far from the centre any part of it reaches (m), for the cell grid.</summary>
        public float Reach;
        public uint Seed;
        public int Sides, Kind;
    }

    /// <summary>
    /// The island's icebergs (W-28): scenery with its own seed stream, built after everything else, so no landmark, cave or
    /// settlement hash sees it. The cave berg's tunnel is a polyline of (x, floor y, z) nodes ending at the chamber's edge.
    /// </summary>
    public struct IcebergSetBlob
    {
        public BlobArray<IcebergData> Bergs;
        /// <summary>Per cell: (first item, count) in Items.</summary>
        public BlobArray<int2> Cells;
        public BlobArray<int> Items;
        public float2 Origin;
        public float CellSize;
        public int2 GridSize;
        public int CaveBerg;
        public BlobArray<float3> Tunnel;
        /// <summary>The chamber's centre on its floor, its radius and its dome's height.</summary>
        public float3 Chamber;
        public float ChamberRadius, ChamberHeight;
    }

    /// <summary>
    /// The analytic shape every berg is drawn and stood on with: a flat top inside a jittered polygon, sides falling at
    /// Slope (steep, except a gentle ramp on big bergs to climb out of the water), and for the cave berg a tunnel and a
    /// domed chamber cut inside. TerrainSource.Deck stands bodies on it and TerrainSource.TowerWall keeps them out of it.
    /// </summary>
    public static class IcebergShape
    {
        public const float TunnelHalfWidth = 1.7f, TunnelHeight = 3.2f;
        public const float SteepSlope = 2.6f, ChunkSlope = 1.4f, RampSlope = 0.5f, RampHalfAngle = 0.45f;
        /// <summary>Anything this little above the sea is swum onto, never a wall.</summary>
        public const float WaterlineLip = 0.2f;

        public static float Hash01(uint seed, int k) => (BiomeSampling.Scramble(seed ^ (uint)(k * 0x9E3779B9u)) & 0xFFFFFF) / 16777216f;

        static float Wrap(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));

        /// <summary>The top's radius in a world direction (radians).</summary>
        public static float TopRadius(in IcebergData b, float angle)
        {
            var a = (Wrap(angle - b.Rotation) + math.PI) / (2f * math.PI) * b.Sides;
            var k = (int)math.floor(a);
            var t = a - k;
            var f0 = 0.82f + 0.33f * Hash01(b.Seed, k % b.Sides);
            var f1 = 0.82f + 0.33f * Hash01(b.Seed, (k + 1) % b.Sides);
            return b.Radius * math.lerp(f0, f1, t);
        }

        public static float Slope(in IcebergData b, float angle)
        {
            if (b.Kind == 0)
                return ChunkSlope;
            var d = math.abs(Wrap(angle - b.Ramp));
            return math.lerp(SteepSlope, RampSlope, math.smoothstep(RampHalfAngle, RampHalfAngle * 0.4f, d));
        }

        /// <summary>The berg's surface height above the sea at a point (negative beyond its waterline), and whether it's a steep side there.</summary>
        public static float Height(in IcebergData b, float2 xz, out bool steep)
        {
            var v = xz - b.Centre;
            var angle = math.atan2(v.y, v.x);
            var r = math.length(v);
            var top = TopRadius(b, angle);
            steep = false;
            if (r <= top)
                return b.Height;
            var s = Slope(b, angle);
            steep = s > 1.2f;
            return b.Height - (r - top) * s;
        }

        /// <summary>Whether a point is inside the cave berg's tunnel or chamber footprint, with the floor and roof there.</summary>
        public static bool CaveAt(ref IcebergSetBlob set, float2 xz, out float floor, out float roof)
        {
            floor = 0f;
            roof = float.MinValue;
            if (set.CaveBerg < 0)
                return false;
            var found = false;
            var dc = math.distance(xz, set.Chamber.xz);
            if (dc < set.ChamberRadius)
            {
                floor = set.Chamber.y;
                roof = set.Chamber.y + math.max(2.6f, set.ChamberHeight * math.sqrt(1f - dc * dc / (set.ChamberRadius * set.ChamberRadius)));
                found = true;
            }
            for (int i = 0; i + 1 < set.Tunnel.Length; i++)
            {
                var a = set.Tunnel[i];
                var b = set.Tunnel[i + 1];
                var ab = b.xz - a.xz;
                var t = math.saturate(math.dot(xz - a.xz, ab) / math.max(1e-6f, math.lengthsq(ab)));
                var d = math.distance(xz, a.xz + ab * t);
                if (d >= TunnelHalfWidth)
                    continue;
                var f = math.lerp(a.y, b.y, t);
                var q = d / TunnelHalfWidth;
                var top = f + TunnelHeight * (1f - 0.35f * q * q);
                if (top > roof)
                {
                    floor = f;
                    roof = top;
                    found = true;
                }
            }
            return found;
        }

        static int2 CellRange(ref IcebergSetBlob set, float2 xz)
        {
            var c = (int2)math.floor((xz - set.Origin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return int2.zero;
            return set.Cells[c.y * set.GridSize.x + c.x];
        }

        /// <summary>The ice floor under a point that's on or a little below it: a cave floor inside the cave, else a berg's top or side.</summary>
        public static bool Deck(ref IcebergSetBlob set, float sea, float3 p, out float deck)
        {
            deck = float.MinValue;
            if (set.Bergs.Length == 0)
                return false;
            if (CaveAt(ref set, p.xz, out var floor, out var roof) && p.y < roof && p.y >= floor - 1.6f)
            {
                deck = floor;
                return true;
            }
            var range = CellRange(ref set, p.xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var b = ref set.Bergs[set.Items[k]];
                var h = sea + Height(b, p.xz, out _);
                if (h > sea - 1.5f && p.y >= h - 1.6f && h > deck)
                    deck = h;
            }
            return deck > float.MinValue;
        }

        /// <summary>Whether ice stands where a body at p would be: a steep side, a rise too high to step up, or a cave wall.</summary>
        public static bool Wall(ref IcebergSetBlob set, float sea, float3 p, float stepUp, float head)
        {
            if (set.Bergs.Length == 0)
                return false;
            if (CaveAt(ref set, p.xz, out var floor, out var roof) && p.y < roof && p.y >= floor - 1.6f)
                return floor > p.y + stepUp && floor > sea + WaterlineLip;
            var range = CellRange(ref set, p.xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var b = ref set.Bergs[set.Items[k]];
                var h = sea + Height(b, p.xz, out var steep);
                if (h <= sea + WaterlineLip || p.y >= h - 0.05f || p.y + head < sea - b.Draft)
                    continue;
                // Floes and low chunks are climbed straight out of the water; a big berg's ramp never blocks (its deck lifts you).
                if (b.Kind == 0 ? h > sea + 0.6f && h > p.y + stepUp + 0.6f : steep)
                    return true;
            }
            return false;
        }
    }

    public static class IcebergBaking
    {
        const float Cell = 40f, Extent = 5120f, GridCell = 128f;
        const float SettlementClearance = 350f, ColdReach = 1400f;
        const byte Sea = 1, ColdLand = 2, WarmLand = 3, ShoreLand = 4, Other = 0;
        /// <summary>How far a coast's own strip (the Ocean biome on the beach) may lie between the sea and the biome behind it.</summary>
        const float ShoreStrip = 320f;

        public static string LastLog = "";

        /// <summary>The stage's own stream: the world seed with "icebergs" mixed in.</summary>
        static uint StreamSeed(uint seed) => BiomeSampling.Scramble(seed ^ 0x1CEB3A65u);

        [BurstCompile]
        struct ClassifyJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Size;
            public float Cell, Extent;
            [ReadOnly] public NativeArray<byte> Cold;
            [WriteOnly] public NativeArray<byte> Kind;
            [WriteOnly] public NativeArray<float> Depth;

            public void Execute(int i)
            {
                var p = new float2(i % Size, i / Size) * Cell - Extent;
                var g = Source.SampleGround(p);
                var sea = Source.World.SeaLevel;
                Depth[i] = sea - g.Height;
                if (g.Height >= sea)
                {
                    var best = -1;
                    var w = 0f;
                    for (int k = 0; k < 4; k++)
                        if (g.Biomes.Weights[k] > w)
                        {
                            w = g.Biomes.Weights[k];
                            best = g.Biomes.Ids[k];
                        }
                    var c = best >= 0 && best < Cold.Length ? Cold[best] : (byte)0;
                    Kind[i] = c == 1 ? ColdLand : c == 2 ? ShoreLand : WarmLand;
                    return;
                }
                var open = g.HasWater && math.abs(g.WaterSurface - sea) < 0.05f && g.River < 0.01f;
                Kind[i] = open ? Sea : Other;
            }
        }

        public static BlobAssetReference<IcebergSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, Allocator allocator)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var coldNames = new List<string>();
            var cold = new NativeArray<byte>(math.max(1, palette.Count), Allocator.TempJob);
            for (int i = 0; i < palette.Count; i++)
                if (palette[i] != null && (palette[i].name == "Tundra" || palette[i].name == "Glacier"))
                {
                    cold[i] = 1;
                    coldNames.Add(palette[i].name);
                }
                else if (palette[i] != null && (palette[i].name == "Ocean" || palette[i].name == "Beach"))
                    cold[i] = 2;
            var size = (int)(2f * Extent / Cell) + 1;
            var n = size * size;
            var kind = new NativeArray<byte>(n, Allocator.TempJob);
            var depth = new NativeArray<float>(n, Allocator.TempJob);
            new ClassifyJob { Source = source, Size = size, Cell = Cell, Extent = Extent, Cold = cold, Kind = kind, Depth = depth }.Schedule(n, 64).Complete();
            var kinds = kind.ToArray();
            var depths = depth.ToArray();
            cold.Dispose();
            kind.Dispose();
            depth.Dispose();

            // Open sea is only what joins the map's edge (no sunken lakes). Then distances (m) over sea from cold and warm shores.
            var open = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                int x = i % size, y = i / size;
                if ((x == 0 || y == 0 || x == size - 1 || y == size - 1) && kinds[i] == Sea) { open[i] = true; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var m in Neighbours(c, size))
                    if (!open[m] && kinds[m] == Sea) { open[m] = true; queue.Enqueue(m); }
            }
            var dCold = Distances(kinds, open, size, ColdLand);
            var dWarm = Distances(kinds, open, size, WarmLand);

            var settlements = new List<float3>();
            if (source.Settlements.IsCreated)
            {
                ref var st = ref source.Settlements.Value;
                for (int i = 0; i < st.Settlements.Length; i++)
                    settlements.Add(new float3(st.Settlements[i].Centre, st.Settlements[i].Radius + SettlementClearance));
            }
            bool NearSettlement(float2 p, float pad)
            {
                foreach (var s in settlements)
                    if (math.distance(p, s.xy) < s.z + pad) return true;
                return false;
            }

            var stream = StreamSeed(source.World.Seed);
            // Inside the world's coast band, where the terrain and sea are drawn.
            var limit = source.World.WorldSize * 0.5f + source.World.CoastWidth * 0.5f - 60f;
            var bergs = new List<IcebergData>();
            var coastCells = new HashSet<int>();
            // Big tabular bergs first (further out, 150 m apart), then chunks and floes nearer shore, clear of the big ones.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    if (!open[i] || dCold[i] > ColdReach || dCold[i] >= dWarm[i])
                        continue;
                    var h = BiomeSampling.Scramble(stream ^ (uint)i * 0x27D4EB2Du ^ (uint)pass * 0x165667B1u);
                    float U(int k) => IcebergShape.Hash01(h, k);
                    var d = dCold[i];
                    var p = new float2(i % size, i / size) * Cell - Extent + (new float2(U(1), U(2)) - 0.5f) * Cell;
                    if (math.cmax(math.abs(p)) > limit - (pass == 0 ? 60f : 0f))
                        continue;
                    IcebergData b;
                    if (pass == 0)
                    {
                        var chance = 0.035f * math.smoothstep(200f, 400f, d) * (1f - math.smoothstep(900f, ColdReach, d));
                        if (U(0) >= chance || depths[i] < 18f)
                            continue;
                        var radius = math.lerp(10f, 30f, U(3) * U(3));
                        var height = math.lerp(5f, 20f, U(4));
                        b = new IcebergData { Centre = p, Radius = radius, Height = height, Draft = math.min(height * 2.5f, depths[i] - 1f), Kind = 1, Sides = 7 + (int)(U(5) * 3f) };
                    }
                    else
                    {
                        var chance = 0.5f * math.exp(-math.max(0f, d - 40f) / 420f);
                        if (d < 40f || U(0) >= chance || depths[i] < 2f)
                            continue;
                        var floe = U(6) < 0.4f;
                        var radius = floe ? math.lerp(2f, 6f, U(3)) : math.lerp(1.2f, 6f, U(3) * U(3));
                        var height = floe ? math.lerp(0.25f, 0.5f, U(4)) : math.lerp(0.6f, 3f, U(4));
                        b = new IcebergData { Centre = p, Radius = radius, Height = height, Draft = math.min(height * (floe ? 2f : 3f), depths[i] - 0.5f), Kind = 0, Sides = 5 + (int)(U(5) * 3f) };
                    }
                    b.Rotation = U(7) * 2f * math.PI;
                    b.Ramp = U(8) * 2f * math.PI;
                    b.Seed = h;
                    b.Reach = b.Radius * 1.15f + b.Height / (b.Kind == 0 ? IcebergShape.ChunkSlope : IcebergShape.RampSlope) + 1f;
                    if (NearSettlement(p, b.Reach) || !Clear(bergs, b))
                        continue;
                    bergs.Add(b);
                    coastCells.Add((int)math.floor(p.x / 1024f) * 100000 + (int)math.floor(p.y / 1024f));
                }

            // The cave berg: the biggest big berg, made at least big enough for its chamber, its entrance facing the cold shore.
            int cave = -1;
            var bestSize = 0f;
            for (int i = 0; i < bergs.Count; i++)
                if (bergs[i].Kind == 1 && bergs[i].Radius * bergs[i].Height > bestSize) { bestSize = bergs[i].Radius * bergs[i].Height; cave = i; }
            var tunnel = new List<float3>();
            float3 chamber = default;
            float chamberRadius = 0f, chamberHeight = 0f;
            if (cave >= 0)
            {
                var b = bergs[cave];
                b.Kind = 2;
                b.Radius = math.max(b.Radius, 18f);
                b.Height = math.max(b.Height, 13f);
                b.Draft = math.max(b.Draft, 20f);
                var shore = ShoreDirection(dCold, open, size, b.Centre);
                var entrance = math.atan2(shore.y, shore.x);
                b.Ramp = entrance + 2.2f;
                b.Reach = b.Radius * 1.15f + b.Height / IcebergShape.RampSlope + 1f;
                bergs[cave] = b;
                Cave(b, source.World.SeaLevel, entrance, tunnel, out chamber, out chamberRadius, out chamberHeight);
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<IcebergSetBlob>();
            var arr = builder.Allocate(ref root.Bergs, bergs.Count);
            for (int i = 0; i < bergs.Count; i++) arr[i] = bergs[i];
            var origin = new float2(-Extent - GridCell);
            var grid = new int2((int)math.ceil((2f * Extent + 2f * GridCell) / GridCell));
            var lists = new List<int>[grid.x * grid.y];
            for (int i = 0; i < bergs.Count; i++)
            {
                var lo = (int2)math.floor((bergs[i].Centre - bergs[i].Reach - origin) / GridCell);
                var hi = (int2)math.floor((bergs[i].Centre + bergs[i].Reach - origin) / GridCell);
                for (int y = math.max(lo.y, 0); y <= math.min(hi.y, grid.y - 1); y++)
                    for (int x = math.max(lo.x, 0); x <= math.min(hi.x, grid.x - 1); x++)
                        (lists[y * grid.x + x] ??= new List<int>()).Add(i);
            }
            var cells = builder.Allocate(ref root.Cells, lists.Length);
            var items = new List<int>();
            for (int c = 0; c < lists.Length; c++)
            {
                cells[c] = new int2(items.Count, lists[c]?.Count ?? 0);
                if (lists[c] != null) items.AddRange(lists[c]);
            }
            var itemArr = builder.Allocate(ref root.Items, items.Count);
            for (int i = 0; i < items.Count; i++) itemArr[i] = items[i];
            root.Origin = origin;
            root.CellSize = GridCell;
            root.GridSize = grid;
            root.CaveBerg = cave;
            var tun = builder.Allocate(ref root.Tunnel, tunnel.Count);
            for (int i = 0; i < tunnel.Count; i++) tun[i] = tunnel[i];
            root.Chamber = chamber;
            root.ChamberRadius = chamberRadius;
            root.ChamberHeight = chamberHeight;
            var blob = builder.CreateBlobAssetReference<IcebergSetBlob>(allocator);
            builder.Dispose();

            int big = 0, chunks = 0, floes = 0;
            foreach (var b in bergs)
                if (b.Kind >= 1) big++;
                else if (b.Height <= 0.5f) floes++;
                else chunks++;
            var caveText = cave >= 0 ? $"cave berg #{cave} at ({bergs[cave].Centre.x:F0}, {bergs[cave].Centre.y:F0}) r {bergs[cave].Radius:F1} h {bergs[cave].Height:F1}, chamber ({chamber.x:F0}, {chamber.y:F1}, {chamber.z:F0})" : "no cave berg";
            LastLog = $"[icebergs] cold biomes [{string.Join(",", coldNames)}]: big {big}, chunks {chunks}, floes {floes} in {coastCells.Count} coast km-cells [{string.Join(" ", CellNames(coastCells))}]; {caveText}; {watch.ElapsedMilliseconds} ms";
            return blob;
        }

        static IEnumerable<string> CellNames(HashSet<int> cells)
        {
            var list = new List<int>(cells);
            list.Sort();
            foreach (var c in list)
            {
                var x = (int)math.floor(c / 100000f + 0.5f);
                yield return $"{x},{c - x * 100000}";
            }
        }

        static bool Clear(List<IcebergData> bergs, in IcebergData b)
        {
            foreach (var o in bergs)
            {
                var gap = o.Kind >= 1 && b.Kind >= 1 ? 150f : 2f;
                if (math.distance(o.Centre, b.Centre) < o.Reach + b.Reach + gap) return false;
            }
            return true;
        }

        /// <summary>Lays the tunnel from just outside the berg's side at the waterline, winding in and up to the chamber's edge.</summary>
        static void Cave(in IcebergData b, float sea, float entrance, List<float3> tunnel, out float3 chamber, out float radius, out float height)
        {
            float2 Dir(float a) => new float2(math.cos(a), math.sin(a));
            var minTop = b.Radius * 0.82f;
            var rTop = IcebergShape.TopRadius(b, entrance);
            var rBase = rTop + b.Height / IcebergShape.Slope(b, entrance);
            radius = 6f;
            height = 6.5f;
            var centre = b.Centre + Dir(entrance + math.PI) * b.Radius * 0.2f;
            chamber = new float3(centre.x, sea + 1.2f, centre.y);
            void Add(float2 xz, float y) => tunnel.Add(new float3(xz.x, y, xz.y));
            Add(b.Centre + Dir(entrance) * (rBase + 0.8f), sea + 0.15f);
            Add(b.Centre + Dir(entrance) * (rTop * 0.95f), sea + 0.4f);
            Add(b.Centre + Dir(entrance + 0.45f) * (minTop * 0.7f), sea + 0.7f);
            var p3 = b.Centre + Dir(entrance - 0.25f) * (minTop * 0.55f);
            Add(p3, sea + 0.95f);
            Add(centre + math.normalize(p3 - centre) * (radius - 0.3f), sea + 1.2f);
        }

        /// <summary>Which way the cold shore lies from a point: down the cold-distance field.</summary>
        static float2 ShoreDirection(float[] dCold, bool[] open, int size, float2 p)
        {
            var c = (int2)math.round((p + Extent) / Cell);
            float At(int x, int y) => x < 0 || y < 0 || x >= size || y >= size ? 1e9f : math.min(dCold[y * size + x], 1e5f);
            var g = new float2(At(c.x + 2, c.y) - At(c.x - 2, c.y), At(c.x, c.y + 2) - At(c.x, c.y - 2));
            return math.lengthsq(g) > 1e-6f ? -math.normalize(g) : new float2(1f, 0f);
        }

        static float[] Distances(byte[] kinds, bool[] open, int size, byte shore)
        {
            var n = kinds.Length;
            var dist = new float[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.MaxValue;
                if (kinds[i] == shore) { dist[i] = 0f; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var m in Neighbours(c, size))
                    // Across the open sea, or across the beach strip from the biome behind it.
                    if ((open[m] || kinds[m] == ShoreLand && dist[c] + Cell <= ShoreStrip) && dist[m] > dist[c] + Cell) { dist[m] = dist[c] + Cell; queue.Enqueue(m); }
            }
            return dist;
        }

        static IEnumerable<int> Neighbours(int c, int size)
        {
            int x = c % size, y = c / size;
            if (x > 0) yield return c - 1;
            if (x < size - 1) yield return c + 1;
            if (y > 0) yield return c - size;
            if (y < size - 1) yield return c + size;
        }

        public static string Report(in TerrainSource source)
        {
            if (!source.Icebergs.IsCreated) return "[icebergs] none";
            ref var set = ref source.Icebergs.Value;
            int big = 0, small = 0;
            for (int i = 0; i < set.Bergs.Length; i++) if (set.Bergs[i].Kind >= 1) big++; else small++;
            return $"[icebergs] {set.Bergs.Length} bergs (big {big}, small {small}), cave berg {set.CaveBerg}";
        }
    }
}
