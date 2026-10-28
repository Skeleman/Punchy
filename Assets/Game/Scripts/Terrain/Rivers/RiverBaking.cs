using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds rivers by simulating drainage over the whole world on a coarse grid:
    ///  1. sample ground height and precipitation per cell (wetter uphill, plus snowmelt);
    ///  2. priority-flood from the sea inwards: every cell gets a downstream neighbour and a drainage level that
    ///     never rises downstream (hollows are filled);
    ///  3. accumulate water down the drainage tree; cells with enough upstream water become river;
    ///  4. keep the big, deep filled hollows as lakes; find each cell's base level (the first lake downstream, or
    ///     the sea) — nothing can drain below it, so hollows dipping below their base level become ponds;
    ///  5. set each river's bed: the lowest ground across its width, then forced downhill from source to sea so
    ///     where the path crosses a hollow's rim the river cuts through it (never below the base level), with
    ///     over-steep drops spread into gorges; then water levels that never rise downstream;
    ///  6. index which river segments are near each cell, so terrain sampling only checks a handful.
    /// </summary>
    public static class RiverBaking
    {
        public static BlobAssetReference<RiverMapBlob> Build(in TerrainSource source, in RiverSettings settings, Allocator allocator)
        {
            // Cover the landmass plus a margin of sea so every river reaches the coast.
            var cellSize = math.max(settings.CellSize, 4f);
            var span = source.World.WorldSize * 1.25f;
            var resolution = math.max(16, (int)math.ceil(span / cellSize));
            var origin = new float2(-resolution * cellSize * 0.5f);
            var count = resolution * resolution;

            var elevation = new NativeArray<float>(count, Allocator.TempJob);
            var rain = new NativeArray<float>(count, Allocator.TempJob);
            var level = new NativeArray<float>(count, Allocator.TempJob);
            var down = new NativeArray<int>(count, Allocator.TempJob);
            var order = new NativeList<int>(count, Allocator.TempJob);
            var width = new NativeArray<float>(count, Allocator.TempJob);
            var lakes = new NativeArray<float>(count, Allocator.TempJob);
            var bed = new NativeArray<float>(count, Allocator.TempJob);
            var water = new NativeArray<float>(count, Allocator.TempJob);
            var lakeLevel = new NativeArray<float>(count, Allocator.TempJob);
            var floor = new NativeArray<float>(count, Allocator.TempJob);
            var nearest = new NativeArray<int2>(count, Allocator.TempJob);
            var seed = source.World.Seed;

            try
            {
                var sample = new SampleCellsJob
                {
                    Source = source, Settings = settings, Origin = origin, CellSize = cellSize, Resolution = resolution,
                    Elevation = elevation, Rain = rain
                }.Schedule(count, 256);

                var flood = new PriorityFloodJob
                {
                    Resolution = resolution, CellSize = cellSize, SeaLevel = source.World.SeaLevel,
                    Elevation = elevation, Level = level, Down = down, Order = order
                }.Schedule(sample);

                var flow = new FlowJob
                {
                    Settings = settings, CellSize = cellSize, SeaLevel = source.World.SeaLevel,
                    Elevation = elevation, Rain = rain, Down = down, Order = order, Width = width
                }.Schedule(flood);

                var lake = new LakeJob
                {
                    Resolution = resolution, SeaLevel = source.World.SeaLevel,
                    MinCells = (int)math.ceil(settings.MinLakeArea * 1e6f / (cellSize * cellSize)),
                    MinDepth = settings.MinLakeDepth,
                    Elevation = elevation, Level = level, LakeDepth = lakes
                }.Schedule(flow);

                var floors = new BaseLevelJob
                {
                    SeaLevel = source.World.SeaLevel,
                    Elevation = elevation, Level = level, Down = down, Order = order,
                    LakeDepth = lakes, LakeLevel = lakeLevel, Floor = floor
                }.Schedule(lake);

                var bedStart = new BedStartJob
                {
                    Source = source, Resolution = resolution, Origin = origin, CellSize = cellSize,
                    Meander = settings.Meander, Seed = seed,
                    Elevation = elevation, LakeLevel = lakeLevel, Width = width, LakeDepth = lakes, Bed = bed
                }.Schedule(count, 256, floors);

                var bedDownhill = new BedDownhillJob
                {
                    Settings = settings, MinimumDrop = cellSize * 0.0005f, SeaLevel = source.World.SeaLevel,
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Seed = seed,
                    Down = down, Order = order, LakeDepth = lakes, LakeLevel = lakeLevel, Floor = floor,
                    Width = width, Bed = bed, WaterLevel = water
                }.Schedule(bedStart);

                new NearestSegmentsJob
                {
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Meander = settings.Meander, Seed = seed,
                    MaxDistance = settings.MaxCarveDistance + cellSize * 1.5f,
                    Down = down, Width = width, Nearest = nearest
                }.Schedule(bedDownhill).Complete();

                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<RiverMapBlob>();
                root.Resolution = resolution;
                root.Origin = origin;
                root.CellSize = cellSize;
                root.Meander = settings.Meander;
                root.Seed = seed;
                Copy(ref builder, ref root.LakeLevel, lakeLevel);
                Copy(ref builder, ref root.Bed, bed);
                Copy(ref builder, ref root.WaterLevel, water);
                Copy(ref builder, ref root.Down, down);
                Copy(ref builder, ref root.Width, width);
                Copy(ref builder, ref root.Nearest, nearest);
                Copy(ref builder, ref root.LakeDepth, lakes);
                // Each cell's highest lake level within two cells (for bays the cell grid misses).
                var shore = builder.Allocate(ref root.ShoreLevel, count);
                for (int i = 0; i < count; i++)
                {
                    int x = i % resolution, z = i / resolution;
                    var best = float.MinValue;
                    for (int dz = math.max(z - 2, 0); dz <= math.min(z + 2, resolution - 1); dz++)
                    for (int dx = math.max(x - 2, 0); dx <= math.min(x + 2, resolution - 1); dx++)
                    {
                        var n = dz * resolution + dx;
                        if (lakes[n] > 0f)
                            best = math.max(best, lakeLevel[n]);
                    }
                    shore[i] = best;
                }
                var blob = builder.CreateBlobAssetReference<RiverMapBlob>(allocator);
                builder.Dispose();
                return blob;
            }
            finally
            {
                elevation.Dispose();
                rain.Dispose();
                level.Dispose();
                down.Dispose();
                order.Dispose();
                width.Dispose();
                lakes.Dispose();
                bed.Dispose();
                water.Dispose();
                lakeLevel.Dispose();
                floor.Dispose();
                nearest.Dispose();
            }
        }

        static void Copy<T>(ref BlobBuilder builder, ref BlobArray<T> target, NativeArray<T> source) where T : unmanaged
        {
            var array = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++)
                array[i] = source[i];
        }

        [BurstCompile]
        struct SampleCellsJob : IJobParallelFor
        {
            public TerrainSource Source;
            public RiverSettings Settings;
            public float2 Origin;
            public float CellSize;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Elevation;
            [WriteOnly] public NativeArray<float> Rain;

            public void Execute(int i)
            {
                var xz = Origin + (new float2(i % Resolution, i / Resolution) + 0.5f) * CellSize;
                var ground = Source.SampleGround(xz);
                Elevation[i] = ground.Height;

                // Biome rainfall, scaled up over high ground (air cools and drops its moisture as it rises), plus
                // meltwater from snow-covered surfaces. Rivers therefore tend to rise in mountains and snowfields.
                var altitude = math.saturate((ground.Height - Source.World.SeaLevel) / math.max(Settings.HighlandHeight, 1f));
                var orographic = math.lerp(Settings.LowlandRain, Settings.HighlandRain, altitude);
                var snow = Source.SampleSurface(xz, ground, Source.SampleNormal(xz, CellSize * 0.5f)).Snow;
                Rain[i] = Source.Rainfall(xz) * orographic + snow * Settings.Snowmelt;
            }
        }

        /// <summary>
        /// Base level per cell: the surface of the first lake its water reaches, or the sea. A river can never cut
        /// below it. Filled hollows whose ground lies below their base level can't be drained by cutting a rim, so
        /// they hold water up to it: they become ponds (marked as lakes at the base level).
        /// </summary>
        [BurstCompile]
        struct BaseLevelJob : IJob
        {
            public float SeaLevel;
            [ReadOnly] public NativeArray<float> Elevation;
            [ReadOnly] public NativeArray<float> Level;
            [ReadOnly] public NativeArray<int> Down;
            [ReadOnly] public NativeList<int> Order;
            public NativeArray<float> LakeDepth;
            public NativeArray<float> LakeLevel;
            public NativeArray<float> Floor;

            public void Execute()
            {
                // Downstream cells first, so each cell inherits its outlet's base level.
                for (int k = 0; k < Order.Length; k++)
                {
                    var c = Order[k];
                    var d = Down[c];
                    if (LakeDepth[c] > 0f)
                        LakeLevel[c] = Floor[c] = Level[c];
                    else
                    {
                        Floor[c] = d >= 0 ? Floor[d] : SeaLevel;
                        var inHollow = Level[c] - Elevation[c] > 0.3f;
                        if (inHollow && Elevation[c] >= SeaLevel && Elevation[c] < Floor[c] - 0.3f)
                        {
                            LakeDepth[c] = Floor[c] - Elevation[c];
                            LakeLevel[c] = Floor[c];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starting bed level per cell. River cells take the lowest ground across their channel and banks (so the
        /// water can't spill over a low bank); lake cells take the lake surface; everything else its own height.
        /// </summary>
        [BurstCompile]
        struct BedStartJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Resolution;
            public float2 Origin;
            public float CellSize;
            public float Meander;
            public uint Seed;
            [ReadOnly] public NativeArray<float> Elevation;
            [ReadOnly] public NativeArray<float> LakeLevel;
            [ReadOnly] public NativeArray<float> Width;
            [ReadOnly] public NativeArray<float> LakeDepth;
            [WriteOnly] public NativeArray<float> Bed;

            public void Execute(int i)
            {
                if (LakeDepth[i] > 0f)
                {
                    Bed[i] = LakeLevel[i];
                    return;
                }
                if (Width[i] <= 0f)
                {
                    Bed[i] = Elevation[i];
                    return;
                }

                var centre = RiverMapBlob.JitteredPoint(i, Resolution, Origin, CellSize, Meander, Seed);
                var radius = math.max(Width[i] * 0.5f + 3f, CellSize * 0.6f);
                var lowest = Source.BaseHeight(centre);
                for (int k = 0; k < 8; k++)
                {
                    math.sincos(k * math.PI / 4f, out var s, out var c);
                    lowest = math.min(lowest, Source.BaseHeight(centre + new float2(c, s) * radius));
                    lowest = math.min(lowest, Source.BaseHeight(centre + new float2(c, s) * radius * 0.5f));
                }
                Bed[i] = math.min(lowest, Elevation[i]);
            }
        }

        /// <summary>
        /// Shapes river beds and water levels along the drainage tree:
        ///  1. upstream-first, each cell's downstream neighbour may be no higher than it (minus a tiny drop): a rise
        ///     along the path — a hollow's rim — is cut down to the level upstream of it;
        ///  2. downstream-first, a river cell may be no more than MaxGradient above the cell it drains into: falls
        ///     steeper than that are spread out by cutting a gorge back upstream;
        ///  3. water sits a quarter of the channel depth below the banks (lakes at their surface), then downstream-first
        ///     no river's water may be lower than the water it flows into, so it backs up to a lake's level ahead.
        /// </summary>
        [BurstCompile]
        struct BedDownhillJob : IJob
        {
            public RiverSettings Settings;
            public float MinimumDrop;
            public float SeaLevel;
            public int Resolution;
            public float2 Origin;
            public float CellSize;
            public uint Seed;
            [ReadOnly] public NativeArray<int> Down;
            [ReadOnly] public NativeList<int> Order;
            [ReadOnly] public NativeArray<float> LakeDepth;
            [ReadOnly] public NativeArray<float> LakeLevel;
            [ReadOnly] public NativeArray<float> Floor;
            [ReadOnly] public NativeArray<float> Width;
            public NativeArray<float> Bed;
            public NativeArray<float> WaterLevel;

            public void Execute()
            {
                for (int k = Order.Length - 1; k >= 0; k--)
                {
                    var c = Order[k];
                    var d = Down[c];
                    // Lakes stay flat at their surface; whatever drains out of them is cut to that level, and no cut
                    // goes below the base level the water ultimately drains to.
                    if (d < 0 || LakeDepth[d] > 0f)
                        continue;
                    Bed[d] = math.min(Bed[d], math.max(Bed[c] - MinimumDrop, Floor[d]));
                }

                for (int k = 0; k < Order.Length; k++)
                {
                    var c = Order[k];
                    var d = Down[c];
                    if (d < 0 || Width[c] <= 0f || LakeDepth[c] > 0f)
                        continue;
                    var length = math.distance(Point(c), Point(d));
                    Bed[c] = math.min(Bed[c], Bed[d] + Settings.MaxGradient * length);
                }

                for (int k = 0; k < Order.Length; k++)
                {
                    var c = Order[k];
                    WaterLevel[c] = LakeDepth[c] > 0f ? LakeLevel[c]
                        : Bed[c] < SeaLevel ? SeaLevel
                        : Bed[c] - RiverSampling.Depth(Width[c], Settings) * 0.25f;
                    var d = Down[c];
                    if (d >= 0 && Width[c] > 0f)
                        WaterLevel[c] = math.max(WaterLevel[c], WaterLevel[d]);
                }
            }

            float2 Point(int index) => RiverMapBlob.JitteredPoint(index, Resolution, Origin, CellSize, Settings.Meander, Seed);
        }

        [BurstCompile]
        struct PriorityFloodJob : IJob
        {
            public int Resolution;
            public float CellSize;
            public float SeaLevel;
            [ReadOnly] public NativeArray<float> Elevation;
            public NativeArray<float> Level;
            public NativeArray<int> Down;
            public NativeList<int> Order;

            public void Execute()
            {
                var count = Resolution * Resolution;
                var visited = new NativeArray<bool>(count, Allocator.Temp);
                var heap = new MinHeap(count);

                // Seeds: the sea and the map border. Water leaves the world through them.
                for (int i = 0; i < count; i++)
                {
                    var x = i % Resolution;
                    var y = i / Resolution;
                    var border = x == 0 || y == 0 || x == Resolution - 1 || y == Resolution - 1;
                    if (border || Elevation[i] < SeaLevel)
                    {
                        visited[i] = true;
                        Level[i] = Elevation[i];
                        Down[i] = -1;
                        heap.Push(Elevation[i], i);
                    }
                }

                // Always expand the lowest known cell; a newly reached neighbour drains into it, raised if
                // necessary so the drainage level never rises downstream (that raised part is a filled dip).
                const float minimumDrop = 0.001f;
                while (heap.Count > 0)
                {
                    var c = heap.Pop();
                    Order.Add(c);
                    var cx = c % Resolution;
                    var cy = c / Resolution;
                    for (int n = 0; n < 8; n++)
                    {
                        var nx = cx + Dx(n);
                        var ny = cy + Dy(n);
                        if (nx < 0 || ny < 0 || nx >= Resolution || ny >= Resolution)
                            continue;
                        var ni = ny * Resolution + nx;
                        if (visited[ni])
                            continue;
                        visited[ni] = true;
                        Level[ni] = math.max(Elevation[ni], Level[c] + minimumDrop);
                        Down[ni] = c;
                        heap.Push(Level[ni], ni);
                    }
                }

                heap.Dispose();
                visited.Dispose();
            }

            static int Dx(int n) => n switch { 0 => -1, 1 => 0, 2 => 1, 3 => -1, 4 => 1, 5 => -1, 6 => 0, _ => 1 };
            static int Dy(int n) => n < 3 ? -1 : n < 5 ? 0 : 1;
        }

        [BurstCompile]
        struct FlowJob : IJob
        {
            public RiverSettings Settings;
            public float CellSize;
            public float SeaLevel;
            [ReadOnly] public NativeArray<float> Elevation;
            [ReadOnly] public NativeArray<float> Rain;
            [ReadOnly] public NativeArray<int> Down;
            [ReadOnly] public NativeList<int> Order;
            public NativeArray<float> Width;

            public void Execute()
            {
                // Upstream cells were reached last by the flood, so walking the order backwards visits every
                // cell after everything that drains into it.
                var flow = new NativeArray<float>(Rain, Allocator.Temp);
                for (int k = Order.Length - 1; k >= 0; k--)
                {
                    var c = Order[k];
                    if (Down[c] >= 0)
                        flow[Down[c]] += flow[c];
                }

                var cellArea = CellSize * CellSize / 1e6f; // km²
                var source = math.max(Settings.SourceCatchment, 1e-3f);
                var range = math.log(math.max(Settings.FullCatchment / source, 1.0001f));
                for (int i = 0; i < flow.Length; i++)
                {
                    var catchment = flow[i] * cellArea;
                    Width[i] = Elevation[i] >= SeaLevel && catchment >= source
                        ? math.lerp(Settings.MinWidth, Settings.MaxWidth, math.saturate(math.log(catchment / source) / range))
                        : 0f;
                }
                flow.Dispose();
            }
        }

        [BurstCompile]
        struct NearestSegmentsJob : IJob
        {
            public int Resolution;
            public float2 Origin;
            public float CellSize;
            public float Meander;
            public uint Seed;
            public float MaxDistance;
            [ReadOnly] public NativeArray<int> Down;
            [ReadOnly] public NativeArray<float> Width;
            public NativeArray<int2> Nearest;

            public void Execute()
            {
                var distances = new NativeArray<float2>(Nearest.Length, Allocator.Temp);
                for (int i = 0; i < Nearest.Length; i++)
                {
                    Nearest[i] = new int2(-1);
                    distances[i] = new float2(float.MaxValue);
                }

                var reach = (int)math.ceil(MaxDistance / CellSize);
                for (int r = 0; r < Width.Length; r++)
                {
                    if (Width[r] <= 0f)
                        continue;
                    var a = Point(r);
                    var b = Down[r] >= 0 ? Point(Down[r]) : a;
                    var min = (int2)math.floor((math.min(a, b) - Origin) / CellSize) - reach;
                    var max = (int2)math.floor((math.max(a, b) - Origin) / CellSize) + reach;
                    min = math.max(min, 0);
                    max = math.min(max, Resolution - 1);

                    for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        var centre = Origin + (new float2(x, y) + 0.5f) * CellSize;
                        var ab = b - a;
                        var t = math.saturate(math.dot(centre - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
                        var d = math.distance(centre, a + ab * t);
                        if (d > MaxDistance)
                            continue;

                        var i = y * Resolution + x;
                        var best = distances[i];
                        var ids = Nearest[i];
                        if (d < best.x)
                        {
                            distances[i] = new float2(d, best.x);
                            Nearest[i] = new int2(r, ids.x);
                        }
                        else if (d < best.y)
                        {
                            distances[i] = new float2(best.x, d);
                            Nearest[i] = new int2(ids.x, r);
                        }
                    }
                }
                distances.Dispose();
            }

            float2 Point(int index) => RiverMapBlob.JitteredPoint(index, Resolution, Origin, CellSize, Meander, Seed);
        }

        /// <summary>Finds the hollows the flood filled, and keeps the ones big and deep enough to be lakes.</summary>
        [BurstCompile]
        struct LakeJob : IJob
        {
            public int Resolution;
            public float SeaLevel;
            public int MinCells;
            public float MinDepth;
            [ReadOnly] public NativeArray<float> Elevation;
            [ReadOnly] public NativeArray<float> Level;
            public NativeArray<float> LakeDepth;

            public void Execute()
            {
                var count = Resolution * Resolution;
                var visited = new NativeArray<bool>(count, Allocator.Temp);
                var stack = new NativeList<int>(256, Allocator.Temp);
                var component = new NativeList<int>(256, Allocator.Temp);

                for (int start = 0; start < count; start++)
                {
                    if (visited[start] || !Filled(start))
                        continue;

                    // Flood-fill one connected hollow, measuring its size and depth.
                    component.Clear();
                    stack.Add(start);
                    visited[start] = true;
                    var deepest = 0f;
                    while (stack.Length > 0)
                    {
                        var c = stack[stack.Length - 1];
                        stack.RemoveAt(stack.Length - 1);
                        component.Add(c);
                        deepest = math.max(deepest, Level[c] - Elevation[c]);
                        var x = c % Resolution;
                        var y = c / Resolution;
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= Resolution || ny >= Resolution)
                                continue;
                            var n = ny * Resolution + nx;
                            if (!visited[n] && Filled(n))
                            {
                                visited[n] = true;
                                stack.Add(n);
                            }
                        }
                    }

                    if (component.Length < MinCells || deepest < MinDepth)
                        continue;
                    for (int k = 0; k < component.Length; k++)
                        LakeDepth[component[k]] = Level[component[k]] - Elevation[component[k]];
                }

                visited.Dispose();
                stack.Dispose();
                component.Dispose();
            }

            bool Filled(int i) => Elevation[i] >= SeaLevel && Level[i] - Elevation[i] > 0.3f;
        }

        /// <summary>Binary min-heap of (key, cell) for the priority flood.</summary>
        struct MinHeap
        {
            NativeList<float> keys;
            NativeList<int> values;

            public MinHeap(int capacity)
            {
                keys = new NativeList<float>(capacity, Allocator.Temp);
                values = new NativeList<int>(capacity, Allocator.Temp);
            }

            public int Count => keys.Length;

            public void Push(float key, int value)
            {
                keys.Add(key);
                values.Add(value);
                var i = keys.Length - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (keys[parent] <= keys[i])
                        break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                var result = values[0];
                var last = keys.Length - 1;
                Swap(0, last);
                keys.RemoveAt(last);
                values.RemoveAt(last);

                var i = 0;
                while (true)
                {
                    var l = i * 2 + 1;
                    var r = l + 1;
                    var smallest = i;
                    if (l < keys.Length && keys[l] < keys[smallest]) smallest = l;
                    if (r < keys.Length && keys[r] < keys[smallest]) smallest = r;
                    if (smallest == i)
                        break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return result;
            }

            void Swap(int a, int b)
            {
                (keys[a], keys[b]) = (keys[b], keys[a]);
                (values[a], values[b]) = (values[b], values[a]);
            }

            public void Dispose()
            {
                keys.Dispose();
                values.Dispose();
            }
        }
    }
}
