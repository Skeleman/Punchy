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
        public static BlobAssetReference<RiverMapBlob> Build(in TerrainSource source, in RiverSettings settings, Allocator allocator,
                                                             bool meander = true)
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
            var points = new NativeArray<float2>(count, Allocator.TempJob);
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

                // W-37: segment points need the widths, and every later step reads them.
                new PathPointsJob
                {
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Meander = settings.Meander, Seed = seed,
                    Smooth = settings.Smooth && meander, Down = down, Width = width, Points = points
                }.Schedule(flow).Complete();

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
                    Meander = settings.Meander, Seed = seed, Points = points,
                    Elevation = elevation, LakeLevel = lakeLevel, Width = width, LakeDepth = lakes, Bed = bed
                }.Schedule(count, 256, floors);

                var bedDownhill = new BedDownhillJob
                {
                    Settings = settings, MinimumDrop = cellSize * 0.0005f, SeaLevel = source.World.SeaLevel,
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Seed = seed,
                    Down = down, Order = order, LakeDepth = lakes, LakeLevel = lakeLevel, Floor = floor,
                    Width = width, Bed = bed, WaterLevel = water, Points = points
                }.Schedule(bedStart);

                new NearestSegmentsJob
                {
                    Resolution = resolution, Origin = origin, CellSize = cellSize, Meander = settings.Meander, Seed = seed,
                    MaxDistance = settings.MaxCarveDistance + cellSize * 1.5f,
                    Down = down, Width = width, Nearest = nearest, Points = points
                }.Schedule(bedDownhill).Complete();
                var lakesBefore = new NativeArray<float>(lakes, Allocator.Temp);
                LowerBreachedLakes(resolution, elevation, bed, water, lakes, lakeLevel);
                ReshapeLoweredLakes(source, settings, cellSize * 0.0005f, resolution, origin, cellSize, seed, elevation,
                                    points, down, order, lakesBefore, lakes, lakeLevel, floor, width, bed, water);
                lakesBefore.Dispose();
                FillRiverPits(settings, source.World.SeaLevel, down, order, lakes, width, bed, water);
                if (settings.Smooth && meander)
                    CutDams(source, source.World.SeaLevel, points, down, lakes, lakeLevel, width, bed, water);

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
                Copy(ref builder, ref root.Points, points);
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
                // Main upstream neighbour (the widest feeder) of each river cell, for the curve tangents.
                var up = builder.Allocate(ref root.Up, count);
                for (int i = 0; i < count; i++) up[i] = -1;
                for (int i = 0; i < count; i++)
                {
                    var d = down[i];
                    if (width[i] <= 0f || d < 0) continue;
                    if (up[d] < 0 || width[i] > width[up[d]]) up[d] = i;
                }
                // Waterfalls: segments whose bed steps down by FallDrop or more (placed on the finished blob below).
                var fallCells = new System.Collections.Generic.List<int>();
                if (settings.FallDrop > 0f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var d = down[i];
                        if (width[i] <= 0f || d < 0 || width[d] <= 0f || lakes[i] > 0f || lakes[d] > 0f) continue;
                        if (bed[i] - bed[d] < settings.FallDrop || bed[i] <= source.World.SeaLevel + 1f) continue;
                        fallCells.Add(i);
                    }
                }
                if (fallCells.Count > 0)
                {
                    // #42: a step whose water surface drops under 3 m shows no curtain (the bed steps, the water doesn't), and a
                    // thin fall (under 8 m) within 60 m of a bigger one reads as the same cascade: both go, leaving one fall.
                    float Drop(int c) => water[c] - water[down[c]];
                    float2 Centre(int c) => origin + (new float2(c % resolution, c / resolution) + 0.5f) * cellSize;
                    var big = new System.Collections.Generic.List<int>(fallCells);
                    big.RemoveAll(c => Drop(c) < 3f);
                    big.Sort((a, c) => Drop(c).CompareTo(Drop(a)));
                    var kept = new System.Collections.Generic.List<int>();
                    foreach (var c in big)
                    {
                        var thin = Drop(c) < 8f;
                        var merged = false;
                        if (thin)
                            foreach (var k in kept)
                                if (math.distance(Centre(c), Centre(k)) < 60f) { merged = true; break; }
                        if (!merged) kept.Add(c);
                    }
                    kept.Sort();
                    fallCells = kept;
                }
                var fallArray = builder.Allocate(ref root.Falls, fallCells.Count);
                for (int k = 0; k < fallCells.Count; k++)
                    fallArray[k] = new RiverFall { Drop = water[fallCells[k]] - water[down[fallCells[k]]], Width = fallCells[k] };
                var blob = builder.CreateBlobAssetReference<RiverMapBlob>(allocator);
                builder.Dispose();
                if (fallCells.Count > 0)
                {
                    // Lips need the finished blob (the curve reads Up, Down and the points). Width holds the cell until here.
                    ref var done = ref blob.Value;
                    for (int k = 0; k < done.Falls.Length; k++)
                    {
                        var cell = (int)done.Falls[k].Width;
                        var mid = RiverSampling.CurveMid(ref done, cell, out var fallFlow);
                        done.Falls[k] = new RiverFall
                        {
                            Lip = new float3(mid.x, water[cell], mid.y), Out = fallFlow, Drop = done.Falls[k].Drop,
                            Width = math.max(done.Width[cell], 3f)
                        };
                    }
                }
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
                points.Dispose();
            }
        }

        /// <summary>
        /// A lake whose rim a river has cut through below its surface (a gorge alongside it, not its own outlet, which is cut
        /// only to its level) can't hold that surface: it's lowered to half a metre above the breach, and cells left
        /// shallower than the lakes' minimum stop being lake. Without this the lake's flat surface stood over the cut
        /// as walls of water round dry pits (F-22, kind-possum's glacier lake at 1118, 1553).
        /// </summary>
        static void LowerBreachedLakes(int resolution, NativeArray<float> elevation, NativeArray<float> bed, NativeArray<float> water,
                                       NativeArray<float> lakes, NativeArray<float> lakeLevel)
        {
            var count = lakes.Length;
            var seen = new NativeArray<bool>(count, Allocator.Temp);
            var stack = new NativeList<int>(256, Allocator.Temp);
            var cells = new NativeList<int>(256, Allocator.Temp);
            for (int start = 0; start < count; start++)
            {
                if (seen[start] || lakes[start] <= 0f)
                    continue;
                // One lake: the lake cells joined to this one.
                cells.Clear();
                stack.Clear();
                stack.Add(start);
                seen[start] = true;
                var level = lakeLevel[start];
                var breach = float.MaxValue;
                while (stack.Length > 0)
                {
                    var c = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);
                    cells.Add(c);
                    int x = c % resolution, z = c / resolution;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                            continue;
                        var n = nz * resolution + nx;
                        if (lakes[n] > 0f)
                        {
                            if (!seen[n])
                            {
                                seen[n] = true;
                                stack.Add(n);
                            }
                        }
                        else
                            breach = math.min(breach, bed[n]);
                    }
                }
                if (breach >= level - 2f)
                    continue;
                var lowered = breach + 0.5f;
                foreach (var c in cells)
                {
                    var depth = lowered - elevation[c];
                    if (depth > 0.3f)
                    {
                        lakes[c] = depth;
                        lakeLevel[c] = lowered;
                        bed[c] = lowered;
                        water[c] = lowered;
                    }
                    else
                    {
                        lakes[c] = 0f;
                        bed[c] = math.min(bed[c], elevation[c]);
                        water[c] = math.min(water[c], bed[c]);
                    }
                }
            }
            seen.Dispose();
            stack.Dispose();
            cells.Dispose();
        }

        /// <summary>
        /// W-35b: after LowerBreachedLakes, the rivers that ran into a lowered lake still had beds and water shaped for the
        /// old surface, and the cells the lake left behind kept their raw ground as bed (rivers there ran uphill, with
        /// water standing metres above the ground and stepping down). This reruns the bed shaping of BedDownhillJob, but
        /// only on the cells whose base level dropped or that stopped being lake: their base level is now the lowered
        /// lake, old lake cells get a channel bed like any river cell, beds are cut downhill and gorges spread, and the
        /// water is set again so it never rises along the flow. Every other cell keeps its bed and water exactly.
        /// </summary>
        static void ReshapeLoweredLakes(in TerrainSource source, in RiverSettings settings, float minimumDrop, int resolution,
                                        float2 origin, float cellSize, uint seed, NativeArray<float> elevation,
                                        NativeArray<float2> points, NativeArray<int> down, NativeList<int> order, NativeArray<float> lakesBefore,
                                        NativeArray<float> lakes, NativeArray<float> lakeLevel, NativeArray<float> floor,
                                        NativeArray<float> width, NativeArray<float> bed, NativeArray<float> water)
        {
            var count = lakes.Length;
            var newFloor = new NativeArray<float>(count, Allocator.Temp);
            var touched = new NativeArray<bool>(count, Allocator.Temp);
            var any = false;
            // Base levels again, downstream first (as BaseLevelJob, without making new ponds).
            for (int k = 0; k < order.Length; k++)
            {
                var c = order[k];
                var d = down[c];
                newFloor[c] = lakes[c] > 0f ? lakeLevel[c] : d >= 0 ? newFloor[d] : floor[c];
                var wasLake = lakesBefore[c] > 0f && lakes[c] <= 0f;
                touched[c] = wasLake || newFloor[c] < floor[c] - 0.01f;
                any |= touched[c];
                // A cell the lake left: its bed is the lowest ground across its channel, as BedStartJob sets it.
                if (wasLake && width[c] > 0f)
                {
                    var centre = points[c];
                    var radius = math.max(width[c] * 0.5f + 3f, cellSize * 0.6f);
                    var lowest = source.BaseHeight(centre);
                    for (int j = 0; j < 8; j++)
                    {
                        math.sincos(j * math.PI / 4f, out var s, out var co);
                        lowest = math.min(lowest, source.BaseHeight(centre + new float2(co, s) * radius));
                        lowest = math.min(lowest, source.BaseHeight(centre + new float2(co, s) * radius * 0.5f));
                    }
                    bed[c] = math.min(lowest, elevation[c]);
                }
            }
            if (any)
            {
                // 1. Upstream first: no touched cell may sit higher than what drains into it (down to its base level).
                for (int k = order.Length - 1; k >= 0; k--)
                {
                    var c = order[k];
                    var d = down[c];
                    if (d < 0 || !touched[d] || lakes[d] > 0f)
                        continue;
                    bed[d] = math.min(bed[d], math.max(bed[c] - minimumDrop, newFloor[d]));
                }
                // 2. Downstream first: drops steeper than MaxGradient are spread back upstream as a gorge.
                for (int k = 0; k < order.Length; k++)
                {
                    var c = order[k];
                    var d = down[c];
                    if (d < 0 || !touched[c] || width[c] <= 0f || lakes[c] > 0f)
                        continue;
                    var length = math.distance(points[c], points[d]);
                    bed[c] = math.min(bed[c], bed[d] + settings.MaxGradient * length);
                }
                // 3. Water below the banks, never lower than the water it flows into.
                for (int k = 0; k < order.Length; k++)
                {
                    var c = order[k];
                    if (!touched[c])
                        continue;
                    water[c] = lakes[c] > 0f ? lakeLevel[c]
                        : bed[c] < source.World.SeaLevel ? source.World.SeaLevel
                        : bed[c] - RiverSampling.Depth(width[c], settings) * 0.25f;
                    var d = down[c];
                    if (d >= 0 && width[c] > 0f)
                        water[c] = math.max(water[c], water[d]);
                }
            }
            newFloor.Dispose();
            touched.Dispose();
        }

        /// <summary>
        /// W-53: a river held up by a dam downstream stands above lower land beside it (the 34.51 m dam on v2 near
        /// -2343,-437 left 13 open ends). Instead of filling the low land, the river cuts down through the dam: a cell
        /// whose water stands more than 0.3 m over the lowest ground beside its banks is lowered to that ground, and so is
        /// every cell downstream of it until the river is already that low. Beds drop with the water, so the channel keeps
        /// its depth and the carve's BankSlope walls make the canyon (walkable banks). A cut is skipped when a lake or the
        /// sea downstream stands higher (backwater, which a cut can't drain), deeper than MaxCut, or longer than MaxLength.
        /// Only on the drawn (meandered) river map, so placement never sees it.
        /// </summary>
        static void CutDams(in TerrainSource source, float seaLevel, NativeArray<float2> points, NativeArray<int> down,
                            NativeArray<float> lakes, NativeArray<float> lakeLevel, NativeArray<float> width,
                            NativeArray<float> bed, NativeArray<float> water)
        {
            const float Margin = 0.3f, MaxCut = 16f, MaxLength = 900f;
            var count = down.Length;
            var target = new NativeArray<float>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) target[i] = float.MaxValue;
            for (int c = 0; c < count; c++)
            {
                if (width[c] <= 0f || lakes[c] > 0f)
                    continue;
                // The lowest ground just outside the banks, where the open-end rule looks.
                var low = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    math.sincos(k * math.PI / 4f, out var sn, out var cs);
                    var dir = new float2(cs, sn);
                    for (float r = width[c] * 0.5f + 4f; r <= width[c] * 0.5f + 12f; r += 4f)
                        low = math.min(low, source.BaseHeight(points[c] + dir * r));
                }
                var level = low - Margin;
                if (water[c] <= level + 0.01f)
                    continue;
                // Walk down until the river is already this low; give up at backwater or past the limits.
                var ok = false;
                var along = 0f;
                for (int d = down[c], prev = c, steps = 0; steps < 400; prev = d, d = down[d], steps++)
                {
                    if (d < 0) { ok = seaLevel <= level; break; }
                    if (lakes[d] > 0f) { ok = lakeLevel[d] <= level; break; }
                    if (width[d] <= 0f) break;
                    along += math.distance(points[prev], points[d]);
                    if (water[d] <= level) { ok = true; break; }
                    if (water[d] - level > MaxCut || along > MaxLength) break;
                }
                if (!ok || water[c] - level > MaxCut)
                    continue;
                target[c] = math.min(target[c], level);
                for (int d = down[c]; d >= 0 && lakes[d] <= 0f && width[d] > 0f && water[d] > level; d = down[d])
                    target[d] = math.min(target[d], level);
            }
            for (int i = 0; i < count; i++)
            {
                if (target[i] >= water[i])
                    continue;
                bed[i] -= water[i] - target[i];
                water[i] = target[i];
            }
            target.Dispose();
        }

        /// <summary>
        /// W-36: a river cell whose bed lies below the bed of the river cell it drains into is a pit: its water backs up
        /// to the dam downstream, metres above its own channel, while its carve still digs the ground down to its low bed
        /// across the whole width. The water then stood above the dug-out banks with open ends along the sides (65 of
        /// the 106 open ends on v2). This raises such a bed to sit under its backed-up water like any river bed, so the
        /// river is flat to the dam instead of a sunken channel. Downstream first, so a filled cell can be the dam of the
        /// one above it. Rivers backed up by a lake or the sea are left alone, and water levels do not change.
        /// </summary>
        static void FillRiverPits(in RiverSettings settings, float seaLevel, NativeArray<int> down, NativeList<int> order,
                                  NativeArray<float> lakes, NativeArray<float> width, NativeArray<float> bed, NativeArray<float> water)
        {
            for (int k = 0; k < order.Length; k++)
            {
                var c = order[k];
                var d = down[c];
                if (d < 0 || width[c] <= 0f || lakes[c] > 0f || lakes[d] > 0f || bed[d] <= bed[c] || water[c] < seaLevel + 0.5f)
                    continue;
                var under = RiverSampling.Depth(width[c], settings) * 0.25f;
                if (water[c] > bed[c] - under + 0.05f)
                    bed[c] = water[c] + under;
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
            [ReadOnly] public NativeArray<float2> Points;

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

                var centre = Points[i];
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
            [ReadOnly] public NativeArray<float2> Points;

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

            float2 Point(int index) => Points[index];
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
                // Springs on high ground (SourceHeight): a river cell below that height counts only if a river cell drains into it.
                var fed = new NativeArray<bool>(flow.Length, Allocator.Temp);
                if (Settings.SourceHeight > 0f)
                    for (int k = Order.Length - 1; k >= 0; k--)
                    {
                        var c = Order[k];
                        var on = Elevation[c] >= SeaLevel && flow[c] * cellArea >= source && (fed[c] || Elevation[c] - SeaLevel >= Settings.SourceHeight);
                        if (on && Down[c] >= 0) fed[Down[c]] = true;
                        fed[c] = on;
                    }
                for (int i = 0; i < flow.Length; i++)
                {
                    var catchment = flow[i] * cellArea;
                    Width[i] = Elevation[i] >= SeaLevel && catchment >= source && (Settings.SourceHeight <= 0f || fed[i])
                        ? math.lerp(Settings.MinWidth, Settings.MaxWidth, math.saturate(math.log(catchment / source) / range))
                        : 0f;
                }
                fed.Dispose();
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
            [ReadOnly] public NativeArray<float2> Points;

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

            float2 Point(int index) => Points[index];
        }

        /// <summary>
        /// W-37: where each cell's river segment starts. Every cell gets its jittered centre (RiverMapBlob.JitteredPoint);
        /// with Smooth (island v2) a wide river's point is then a Gaussian average of the jittered points along its main
        /// channel (widest feeder up, Down down), over a span growing with width. Cell-scale jitter gave every river the
        /// same ~3-cell wiggle, which on a 40-60 m channel made tight loops whose banks ballooned; averaged, wide rivers
        /// sweep in bends many widths long. Narrow streams (up to NarrowWidth) keep their jitter exactly. A point stays
        /// within one cell of its cell's centre so the carve and the segment index still find it.
        /// </summary>
        [BurstCompile]
        struct PathPointsJob : IJob
        {
            const float NarrowWidth = 12f, SigmaPerWidth = 1.5f, MaxSigma = 72f;
            public int Resolution;
            public float2 Origin;
            public float CellSize;
            public float Meander;
            public uint Seed;
            public bool Smooth;
            [ReadOnly] public NativeArray<int> Down;
            [ReadOnly] public NativeArray<float> Width;
            public NativeArray<float2> Points;

            public void Execute()
            {
                var count = Points.Length;
                for (int i = 0; i < count; i++)
                    Points[i] = RiverMapBlob.JitteredPoint(i, Resolution, Origin, CellSize, Meander, Seed);
                if (!Smooth)
                    return;
                var up = new NativeArray<int>(count, Allocator.Temp);
                for (int i = 0; i < count; i++) up[i] = -1;
                for (int i = 0; i < count; i++)
                {
                    var d = Down[i];
                    if (Width[i] <= 0f || d < 0) continue;
                    if (up[d] < 0 || Width[i] > Width[up[d]]) up[d] = i;
                }
                var jittered = new NativeArray<float2>(Points, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    var sigma = math.min((Width[i] - NarrowWidth) * SigmaPerWidth, MaxSigma);
                    if (sigma <= 0f)
                        continue;
                    var limit = sigma * 2.5f;
                    var sum = jittered[i];
                    var total = 1f;
                    // Down the channel, then up its main feeders, weighting each point by its distance along the path.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var c = i;
                        var along = 0f;
                        for (int steps = 0; steps < 64; steps++)
                        {
                            var n = pass == 0 ? Down[c] : up[c];
                            if (n < 0 || Width[n] <= 0f) break;
                            along += math.distance(jittered[c], jittered[n]);
                            if (along > limit) break;
                            var w = math.exp(-0.5f * along * along / (sigma * sigma));
                            sum += jittered[n] * w;
                            total += w;
                            c = n;
                        }
                    }
                    var centre = Origin + ((float2)new int2(i % Resolution, i / Resolution) + 0.5f) * CellSize;
                    var offset = sum / total - centre;
                    var length = math.length(offset);
                    Points[i] = centre + (length > CellSize ? offset * (CellSize / length) : offset);
                }
                jittered.Dispose();
                up.Dispose();
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
