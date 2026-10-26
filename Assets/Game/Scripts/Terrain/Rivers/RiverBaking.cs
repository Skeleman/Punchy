using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds rivers by simulating drainage over the whole world on a coarse grid:
    ///  1. sample ground height and rainfall per cell;
    ///  2. priority-flood from the sea inwards: every cell gets a downstream neighbour and a drainage level that
    ///     never rises downstream (dips are filled — future lakes);
    ///  3. accumulate rain down the drainage tree; cells with enough upstream rain become river;
    ///  4. index which river segments are near each cell, so terrain sampling only checks a handful.
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
            var nearest = new NativeArray<int2>(count, Allocator.TempJob);
            var seed = source.World.Seed;

            try
            {
                var sample = new SampleCellsJob
                {
                    Source = source, Origin = origin, CellSize = cellSize, Resolution = resolution,
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

                new NearestSegmentsJob
                {
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Meander = settings.Meander, Seed = seed,
                    MaxDistance = settings.MaxCarveDistance + cellSize * 1.5f,
                    Down = down, Width = width, Nearest = nearest
                }.Schedule(flow).Complete();

                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<RiverMapBlob>();
                root.Resolution = resolution;
                root.Origin = origin;
                root.CellSize = cellSize;
                root.Meander = settings.Meander;
                root.Seed = seed;
                Copy(ref builder, ref root.Level, level);
                Copy(ref builder, ref root.Down, down);
                Copy(ref builder, ref root.Width, width);
                Copy(ref builder, ref root.Nearest, nearest);
                var lakes = new NativeArray<float>(count, Allocator.TempJob);
                new LakeJob
                {
                    Resolution = resolution, SeaLevel = source.World.SeaLevel,
                    MinCells = (int)math.ceil(settings.MinLakeArea * 1e6f / (cellSize * cellSize)),
                    MinDepth = settings.MinLakeDepth,
                    Elevation = elevation, Level = level, LakeDepth = lakes
                }.Run();
                Copy(ref builder, ref root.LakeDepth, lakes);
                lakes.Dispose();
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
            public float2 Origin;
            public float CellSize;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Elevation;
            [WriteOnly] public NativeArray<float> Rain;

            public void Execute(int i)
            {
                var xz = Origin + (new float2(i % Resolution, i / Resolution) + 0.5f) * CellSize;
                Elevation[i] = Source.BaseHeight(xz);
                Rain[i] = Source.Rainfall(xz);
            }
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

            // Must match RiverMapBlob.Point.
            float2 Point(int index)
            {
                var cell = new int2(index % Resolution, index / Resolution);
                var h = math.hash(new int2(index, (int)Seed));
                var jitter = new float2(h & 0xFFFF, h >> 16) / 65535f - 0.5f;
                return Origin + ((float2)cell + 0.5f + jitter * Meander) * CellSize;
            }
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
