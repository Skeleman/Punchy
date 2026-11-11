using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-134 row 2: cave water baked like chunk water. Each cave's pools and streams become water quads on a fixed world
    /// lattice, fitted to the carved floor: a lattice vertex is wet where the cave's air reaches down below the water's
    /// surface (the floor found by marching the cave field, as FloorUnder does), and a dry one sits at the surface inside
    /// the rock round it (sinking only at the very end of the water's reach), so a quad from wet to dry ends where the water meets the rock, exactly as surface water hides
    /// its edge in the bank. Shore foam then comes from the water shader's depth-buffer thickness, and the depth under
    /// each vertex drives its river speed, soft rim and white middle, as on the surface. Streams and their pools share
    /// the lattice, so the water runs from one into the other with no seam.
    ///
    /// A cave is sampled in a background Burst job the first time it comes within LoadRadius (one cave at a time,
    /// collected on a later frame), cut into tiles of the lattice, and each tile is drawn only while it is big enough on
    /// screen (DetailPolicy.ScreenSize, rule 20). Lava pools keep their smooth disc and glow.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Lattice spacing of baked cave water (m): fine enough to follow a basin's rim and a channel's banks.</summary>
        const float WaterStep = 0.5f;
        /// <summary>Lattice cells per side of one drawn water tile (16 m).</summary>
        const int WaterTileCells = 32;
        /// <summary>A water tile is drawn while it is at least this big on screen (reference pixels, at Detail 1).</summary>
        const float WaterTilePixels = 24f;
        /// <summary>
        /// The last this-many metres of a water's reach, over which waters compare as if sinking by ShoreSink: where two
        /// reaches overlap, the one whose middle is nearer wins. Wet vertices lie flat at their water's level everywhere,
        /// filling whatever the carving left under it, so the shoreline is where the floor rises through the surface (the
        /// chunk water rule), not the pool's radius; the carved basins and channels run well past the stored radius and width.
        /// </summary>
        const float ShoreBand = 1.25f;
        /// <summary>How far a dry vertex (inside the bank) sinks by the end of its water's reach (m), so the rim ends under rock.</summary>
        const float ShoreSink = 1.2f;
        /// <summary>
        /// The last this-many metres of a water's reach. A dry vertex sinks across it; a vertex still wet in it lies over a
        /// drop or a bank lower than the water, so the claim is cut there and the sheet ends along the cut (see
        /// BakeCaveWaterJob).
        /// </summary>
        const float ShoreCut = 0.5f;
        /// <summary>Two lattice neighbours whose surfaces differ more than this aren't joined (m).</summary>
        const float WaterSeam = 0.75f;

        /// <summary>One drawn piece of a cave's water: a lattice tile's quads (submesh 1, if any, is white pool water).</summary>
        class WaterTile
        {
            public Mesh Mesh;
            public Bounds Bounds;
            public bool Shown, White;
            public int Triangles;
        }

        /// <summary>Baked water per cave (index into the cave set's Caves); an empty list for a cave with none.</summary>
        readonly Dictionary<int, List<WaterTile>> caveWaterTiles = new Dictionary<int, List<WaterTile>>();
        JobHandle waterJob;
        int waterBakingCave = -1;
        NativeArray<int2> waterKeys;
        NativeArray<float4> waterSamples;
        NativeArray<float3> waterFlow;
        NativeArray<byte> waterWhite;
        /// <summary>Per stream point, how white its water runs (the W-98 drop over five points), built once.</summary>
        NativeArray<float> streamRapids;
        /// <summary>Bumped when the white pool look changes (SetWhitePools), so baked water rebuilds.</summary>
        static int waterLooks;
        int waterLooksBaked;

        /// <summary>Triangles and tiles of baked cave water drawn last frame (W-134: counts against the old discs and ribbons).</summary>
        public static int WaterTrianglesDrawn, WaterTilesDrawn;

        /// <summary>Memory held by the baked water meshes (bytes), and how many tiles are loaded.</summary>
        public long WaterMemory(out int tiles)
        {
            long bytes = 0;
            tiles = 0;
            foreach (var list in caveWaterTiles.Values)
                foreach (var t in list)
                {
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t.Mesh);
                    tiles++;
                }
            return bytes;
        }

        /// <summary>
        /// Draws the baked water of every cave within LoadRadius, collects a finished bake, starts the next one, and lets
        /// go of caves well out of range.
        /// </summary>
        void DrawCaveWater(in TerrainSource source, float3 camPos, Material water)
        {
            WaterTrianglesDrawn = 0;
            WaterTilesDrawn = 0;
            ref var set = ref source.Caves.Value;
            if (waterLooksBaked != waterLooks)
            {
                ReleaseCaveWater();
                waterLooksBaked = waterLooks;
            }
            if (waterBakingCave >= 0 && waterJob.IsCompleted)
            {
                waterJob.Complete();
                caveWaterTiles[waterBakingCave] = BuildWaterTiles(source);
                DisposeWaterBake();
            }
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var pixels = WaterTilePixels / math.max(view.Detail, 0.1f);
            List<int> gone = null;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                var distance = math.distance(math.clamp(camPos, cave.Min, cave.Max), camPos);
                var baked = caveWaterTiles.TryGetValue(c, out var tiles);
                if (!baked)
                {
                    if (distance < LoadRadius && waterBakingCave < 0)
                        StartWaterBake(source, c);
                    continue;
                }
                if (distance > LoadRadius * 1.2f)
                {
                    (gone ??= new List<int>()).Add(c);
                    continue;
                }
                if (water == null || distance > LoadRadius)
                    continue;
                foreach (var t in tiles)
                {
                    var d = math.distance((float3)t.Bounds.ClosestPoint(camPos), camPos);
                    var size = math.cmax((float3)t.Bounds.size);
                    t.Shown = Punchy.Settings.DetailPolicy.Holds(
                        Punchy.Settings.DetailPolicy.ScreenSize(size, d, view.PixelsPerMetre), pixels, t.Shown);
                    if (!t.Shown || d > LoadRadius)
                        continue;
                    var rp = new RenderParams(water) { worldBounds = t.Bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                    Graphics.RenderMesh(rp, t.Mesh, 0, Matrix4x4.identity);
                    if (t.White)
                    {
                        // White pools fade out towards their rim (W-16), now by the real depth under each vertex.
                        rp.matProps = SoftRimProps;
                        Graphics.RenderMesh(rp, t.Mesh, 1, Matrix4x4.identity);
                    }
                    WaterTrianglesDrawn += t.Triangles;
                    WaterTilesDrawn++;
                }
            }
            if (gone != null)
                foreach (var c in gone)
                {
                    foreach (var t in caveWaterTiles[c])
                        Destroy(t.Mesh);
                    caveWaterTiles.Remove(c);
                }
        }

        /// <summary>
        /// Lists the lattice vertices round cave c's water pools and streams and starts sampling them. A cave with no
        /// water gets an empty list at once.
        /// </summary>
        void StartWaterBake(in TerrainSource source, int c)
        {
            ref var set = ref source.Caves.Value;
            ref var cave = ref set.Caves[c];
            var keys = new HashSet<long>();
            for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f)
                    continue;
                var reach = PoolReach(pool) + WaterStep;
                AddNear(keys, pool.Centre.xz, pool.Centre.xz, reach);
            }
            for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
            {
                ref var st = ref set.Streams[s];
                for (int i = st.FirstPoint; i < st.FirstPoint + st.PointCount - 1; i++)
                {
                    ref var a = ref set.StreamPoints[i];
                    ref var b = ref set.StreamPoints[i + 1];
                    var reach = StreamReach(math.max(a.Width, b.Width) * 0.5f) + WaterStep;
                    AddNear(keys, a.Position.xz, b.Position.xz, reach);
                }
            }
            if (keys.Count == 0)
            {
                caveWaterTiles[c] = new List<WaterTile>();
                return;
            }
            if (!streamRapids.IsCreated)
                streamRapids = StreamRapids(ref set);
            waterKeys = new NativeArray<int2>(keys.Count, Allocator.Persistent);
            var n = 0;
            foreach (var k in keys)
                waterKeys[n++] = new int2((int)(k >> 32), (int)(k & 0xffffffffL));
            waterSamples = new NativeArray<float4>(keys.Count, Allocator.Persistent);
            waterFlow = new NativeArray<float3>(keys.Count, Allocator.Persistent);
            // W-18: bat folk chambers' pools are white; -whitepools turns them all white.
            var batFolk = source.BatFolk.IsCreated && source.BatFolk.Value.WhitePools.Length == set.Pools.Length;
            waterWhite = new NativeArray<byte>(set.Pools.Length, Allocator.Persistent);
            for (int i = 0; i < set.Pools.Length; i++)
                waterWhite[i] = (byte)(WhitePools || batFolk && source.BatFolk.Value.WhitePools[i] != 0 ? 1 : 0);
            waterJob = new BakeCaveWaterJob
            {
                Source = source,
                Cave = c,
                Keys = waterKeys,
                StreamRapids = streamRapids,
                White = waterWhite,
                Samples = waterSamples,
                Flow = waterFlow
            }.Schedule(keys.Count, 64);
            waterBakingCave = c;
        }

        /// <summary>How far from a pool's middle its water may reach (m), past where its basin rises out of it.</summary>
        // A lake's beach rises steadily from its edge (W-134 row 3), so its water ends close past the basin.
        static float PoolReach(in CavePool pool) => pool.Hall > 0f ? pool.Radius * 1.2f + 3f : pool.Radius * 2f + 1.5f;

        /// <summary>How far from a stream's middle its water may reach (m), past where StreamBed's banks rise out of it.</summary>
        static float StreamReach(float half) => half * 2f + 2.5f;

        /// <summary>Adds the lattice vertices within reach of the segment a-b.</summary>
        static void AddNear(HashSet<long> keys, float2 a, float2 b, float reach)
        {
            var lo = (int2)math.floor((math.min(a, b) - reach) / WaterStep);
            var hi = (int2)math.ceil((math.max(a, b) + reach) / WaterStep);
            var ab = b - a;
            var length2 = math.max(math.lengthsq(ab), 1e-6f);
            for (int x = lo.x; x <= hi.x; x++)
                for (int z = lo.y; z <= hi.y; z++)
                {
                    var p = new float2(x, z) * WaterStep;
                    var t = math.saturate(math.dot(p - a, ab) / length2);
                    if (math.distancesq(p, a + ab * t) <= reach * reach)
                        keys.Add(((long)x << 32) | (uint)z);
                }
        }

        /// <summary>
        /// How white each stream point's water runs: the drop measured over five points (about 6 m), so the water quickens
        /// into a steep step and slows after it (W-98); cave streams run steep all the way, so only the steepest steps churn
        /// white, but a brook is never still.
        /// </summary>
        static NativeArray<float> StreamRapids(ref CaveSetBlob set)
        {
            var rapids = new NativeArray<float>(set.StreamPoints.Length, Allocator.Persistent);
            for (int s = 0; s < set.Streams.Length; s++)
            {
                ref var st = ref set.Streams[s];
                for (int k = 0; k < st.PointCount; k++)
                {
                    var k0 = math.max(k - 2, 0);
                    var k1 = math.min(k + 2, st.PointCount - 1);
                    var run = 0f;
                    for (int j = k0; j < k1; j++)
                        run += math.distance(set.StreamPoints[st.FirstPoint + j].Position.xz, set.StreamPoints[st.FirstPoint + j + 1].Position.xz);
                    var drop = (set.StreamPoints[st.FirstPoint + k0].Position.y - set.StreamPoints[st.FirstPoint + k1].Position.y) / math.max(run, 0.1f);
                    rapids[st.FirstPoint + k] = math.max(math.saturate(drop * 1.5f - 0.15f), 0.2f);
                }
            }
            return rapids;
        }

        /// <summary>
        /// Per lattice vertex of one cave: which water claims it (pool, stream or white pool; inside most of a pool's
        /// radius the pool always does, elsewhere the higher surface), that water's surface, flow and whiteness, and the
        /// depth to the carved floor below the surface (-1 where the surface is inside rock: a dry vertex hidden in the bank).
        /// </summary>
        [BurstCompile]
        struct BakeCaveWaterJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Cave;
            [ReadOnly] public NativeArray<int2> Keys;
            [ReadOnly] public NativeArray<float> StreamRapids;
            [ReadOnly] public NativeArray<byte> White;
            /// <summary>x surface, y depth (-1 dry), z rapids, w kind (0 none, 1 pool, 2 stream, 3 white pool).</summary>
            [WriteOnly] public NativeArray<float4> Samples;
            /// <summary>xy flow direction, z claim: how far inside its water's reach the vertex lies (m; negative where no
            /// water claims it, or where the claim was cut over a drop), so BuildWaterTiles can cut cells along it.</summary>
            [WriteOnly] public NativeArray<float3> Flow;

            public void Execute(int index)
            {
                ref var set = ref Source.Caves.Value;
                ref var cave = ref set.Caves[Cave];
                var xz = (float2)Keys[index] * WaterStep;
                var surface = -1e6f;
                var kind = 0f;
                var flow = float2.zero;
                var rapids = 0f;
                var reach = 0f;
                var edge = -1f;
                var band = 0f;
                var held = false;
                for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                {
                    ref var pool = ref set.Pools[i];
                    if (pool.Lava > 0.5f)
                        continue;
                    var d = math.distance(xz, pool.Centre.xz);
                    var poolReach = PoolReach(pool);
                    if (d >= poolReach)
                        continue;
                    // Wherever it lies flat, a pool's level holds against the streams running into it (their last points
                    // reach its middle a little above it).
                    var inner = d < poolReach - ShoreBand;
                    var level = pool.Centre.y - ShoreSink * math.smoothstep(poolReach - ShoreBand, poolReach, d);
                    if (held && !inner || level <= surface - ShoreSink * band && (held || !inner))
                        continue;
                    held |= inner;
                    surface = pool.Centre.y;
                    edge = poolReach - d;
                    band = math.smoothstep(poolReach - ShoreBand, poolReach, d);
                    kind = White[i] != 0 ? 3f : 1f;
                    flow = float2.zero;
                    rapids = 0f;
                    reach = pool.Depth + 1.5f;
                }
                for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount && !held; s++)
                {
                    // A stream's water at this vertex is its nearest segment's (the one it lies furthest inside): on the
                    // inside of a bend, the segments upstream also reach it, higher, and would step the surface.
                    ref var st = ref set.Streams[s];
                    int best = -1;
                    float bestEdge = 0f, bestT = 0f, bestReach = 0f, bestAcross = 0f;
                    for (int i = st.FirstPoint; i < st.FirstPoint + st.PointCount - 1; i++)
                    {
                        ref var a = ref set.StreamPoints[i];
                        ref var e = ref set.StreamPoints[i + 1];
                        var ab = e.Position.xz - a.Position.xz;
                        var t = math.saturate(math.dot(xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                        var across = math.distance(xz, a.Position.xz + ab * t);
                        var streamReach = StreamReach(math.lerp(a.Width, e.Width, t) * 0.5f);
                        if (streamReach - across > bestEdge)
                        {
                            best = i;
                            bestEdge = streamReach - across;
                            bestT = t;
                            bestReach = streamReach;
                            bestAcross = across;
                        }
                    }
                    if (best < 0)
                        continue;
                    ref var p0 = ref set.StreamPoints[best];
                    ref var p1 = ref set.StreamPoints[best + 1];
                    var flat = math.lerp(p0.Position.y, p1.Position.y, bestT);
                    var level = flat - ShoreSink * math.smoothstep(bestReach - ShoreBand, bestReach, bestAcross);
                    if (level <= surface - ShoreSink * band)
                        continue;
                    surface = flat;
                    edge = bestEdge;
                    band = math.smoothstep(bestReach - ShoreBand, bestReach, bestAcross);
                    kind = 2f;
                    var dir = p1.Position.xz - p0.Position.xz;
                    flow = math.lengthsq(dir) > 1e-8f ? math.normalize(dir) : float2.zero;
                    rapids = math.lerp(StreamRapids[best], StreamRapids[best + 1], bestT);
                    reach = math.lerp(p0.Depth, p1.Depth, bestT) + 1.5f;
                }
                var depth = -1f;
                if (kind > 0f)
                {
                    // The carved floor under the surface: march down through the cave's air by part of the distance to the
                    // rock (as FloorUnder does), then halve the last step.
                    var claimed = kind;
                    var y = surface;
                    var dist = CaveSampling.Distance(ref set, new float3(xz.x, y, xz.y), out _);
                    if (dist < 0f)
                    {
                        // No floor within reach: the surface would hang over a drop, so the water doesn't claim it.
                        kind = 0f;
                        for (int i = 0; i < 48; i++)
                        {
                            var next = y - math.max(0.1f, -dist * 0.4f);
                            if (surface - next > reach)
                                break;
                            var dn = CaveSampling.Distance(ref set, new float3(xz.x, next, xz.y), out _);
                            if (dn >= 0f)
                            {
                                float lo = next, hi = y;
                                for (int b = 0; b < 6; b++)
                                {
                                    var mid = (lo + hi) * 0.5f;
                                    if (CaveSampling.Distance(ref set, new float3(xz.x, mid, xz.y), out _) >= 0f)
                                        lo = mid;
                                    else
                                        hi = mid;
                                }
                                depth = surface - (lo + hi) * 0.5f;
                                kind = claimed;
                                break;
                            }
                            y = next;
                            dist = dn;
                        }
                    }
                    if (kind > 0f && depth <= 0f)
                    {
                        // Dry: inside the bank. It stays flat, so the GPU clips the sheet along the floor between it and
                        // its wet neighbours (sinking it would pull the shoreline onto the lattice), except over the last
                        // ShoreCut of the reach, where it sinks so the rim ends under the rock. Wet vertices stay flat
                        // everywhere (sinking them shows a slope).
                        surface -= ShoreSink * (1f - math.smoothstep(0f, ShoreCut, edge));
                        depth = -1f;
                    }
                    // Still wet near the end of its reach, the floor falls away (a drop, or a bank lower than the water):
                    // the claim is cut and the sheet ends along the cut, flat, instead of hanging down the slope.
                    else if (kind > 0f && edge < ShoreCut)
                        kind = 0f;
                    // White water breaks against the banks more than mid-channel (G-28).
                    if (kind == 2f && depth >= 0f)
                        rapids = math.min(rapids + 0.15f * math.saturate(1f - depth / 0.3f), 1f);
                }
                Samples[index] = new float4(surface, depth, rapids, kind);
                // The claim: positive inside. A cut vertex's is how far it lies past ShoreCut, and its wet neighbours' are
                // measured from ShoreCut too, so the cut falls along the line edge == ShoreCut, a curve, not the lattice.
                var cut = kind == 0f && edge > 0f;
                var claim = kind > 0f ? math.max(edge - ShoreCut, 0.001f) : cut ? math.min(edge - ShoreCut, -0.001f) : -1f;
                Flow[index] = new float3(flow, claim);
            }
        }

        /// <summary>
        /// Turns the finished samples into tile meshes. Each lattice cell with a wet corner becomes water (the chunk water
        /// rule), its corners at their water's surface, cut along the claim's edge (marching squares on the claim distance)
        /// where some corners lie outside every water's reach, so the outer rim is a curve rather than the lattice's
        /// stairs. Where the floor rises through the surface, the corners sit inside the rock and the GPU clips the rest.
        /// </summary>
        List<WaterTile> BuildWaterTiles(in TerrainSource source)
        {
            var count = waterKeys.Length;
            var at = new Dictionary<long, int>(count);
            for (int i = 0; i < count; i++)
                at[Key(waterKeys[i])] = i;
            var builds = new Dictionary<long, TileBuild>();
            var corners = new int[4];
            var polygon = new List<int>(5);
            for (int i = 0; i < count; i++)
            {
                var k = waterKeys[i];
                corners[0] = i;
                if (!at.TryGetValue(Key(k + new int2(1, 0)), out corners[1]) || !at.TryGetValue(Key(k + new int2(1, 1)), out corners[2]) ||
                    !at.TryGetValue(Key(k + new int2(0, 1)), out corners[3]))
                    continue;
                bool wet = false, white = false;
                int inside = 0;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int j = 0; j < 4; j++)
                {
                    var c = corners[j];
                    if (waterFlow[c].z <= 0f)
                        continue;
                    var sample = waterSamples[c];
                    inside++;
                    wet |= sample.y > 0.02f;
                    white |= sample.w == 3f;
                    lo = math.min(lo, sample.x);
                    hi = math.max(hi, sample.x);
                }
                if (inside == 0 || !wet || hi - lo > WaterSeam)
                    continue;
                var tile = Key(new int2((int)math.floor(k.x / (float)WaterTileCells), (int)math.floor(k.y / (float)WaterTileCells)));
                if (!builds.TryGetValue(tile, out var build))
                    builds[tile] = build = new TileBuild();
                var target = white ? build.Whites : build.Plain;
                // The cell's outline with each corner outside the claim replaced by the edge's crossings.
                polygon.Clear();
                for (int j = 0; j < 4; j++)
                {
                    int c = corners[j], n = corners[(j + 1) & 3];
                    float cc = waterFlow[c].z, cn = waterFlow[n].z;
                    if (cc > 0f)
                        polygon.Add(build.Vertex(this, c));
                    if (cc > 0f != cn > 0f)
                        polygon.Add(cc > 0f ? build.Crossing(this, c, n) : build.Crossing(this, n, c));
                }
                for (int j = 1; j + 1 < polygon.Count; j++)
                {
                    target.Add(polygon[0]);
                    target.Add(polygon[j + 1]);
                    target.Add(polygon[j]);
                }
            }
            var tiles = new List<WaterTile>();
            foreach (var build in builds.Values)
            {
                var vertices = build.Vertices;
                var mesh = new Mesh { name = "Cave water" };
                mesh.SetVertexBufferParams(vertices.Count, WaterVertex.Layout);
                mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.subMeshCount = build.Whites.Count > 0 ? 2 : 1;
                mesh.SetIndices(build.Plain, MeshTopology.Triangles, 0);
                if (build.Whites.Count > 0)
                    mesh.SetIndices(build.Whites, MeshTopology.Triangles, 1);
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                // Drawn from the GPU copy only.
                mesh.UploadMeshData(true);
                tiles.Add(new WaterTile { Mesh = mesh, Bounds = bounds, White = build.Whites.Count > 0, Triangles = (build.Plain.Count + build.Whites.Count) / 3 });
            }
            return tiles;
        }

        /// <summary>One water tile's mesh while it is built: lattice vertices shared between cells, crossings per cell.</summary>
        class TileBuild
        {
            public readonly List<WaterVertex> Vertices = new List<WaterVertex>();
            public readonly List<int> Plain = new List<int>(), Whites = new List<int>();
            readonly Dictionary<int, int> local = new Dictionary<int, int>();

            public int Vertex(CaveSystem sys, int i)
            {
                if (local.TryGetValue(i, out var v))
                    return v;
                Vertices.Add(sys.WaterVertexAt(i, (float2)sys.waterKeys[i] * WaterStep));
                local[i] = Vertices.Count - 1;
                return Vertices.Count - 1;
            }

            /// <summary>Where the claim's edge crosses from claimed lattice vertex i to unclaimed o: i's water, moved there.</summary>
            public int Crossing(CaveSystem sys, int i, int o)
            {
                float ci = sys.waterFlow[i].z, co = sys.waterFlow[o].z;
                var t = ci / (ci - co);
                var xz = math.lerp((float2)sys.waterKeys[i], (float2)sys.waterKeys[o], t) * WaterStep;
                Vertices.Add(sys.WaterVertexAt(i, xz));
                return Vertices.Count - 1;
            }
        }

        /// <summary>Lattice vertex i's water as a mesh vertex at xz.</summary>
        WaterVertex WaterVertexAt(int i, float2 xz)
        {
            var s = waterSamples[i];
            var white = s.w == 3f;
            return new WaterVertex
            {
                Position = new float3(xz.x, s.x, xz.y),
                Data = new float4(waterFlow[i].xy, math.max(s.y, 0f), s.z),
                Look = white ? WhitePoolWater : s.w == 2f ? CaveStreamWater : CaveWater,
                Glow = new float2(white ? WhitePoolGlow : 0f, 0f)
            };
        }

        static long Key(int2 k) => ((long)k.x << 32) | (uint)k.y;

        void DisposeWaterBake()
        {
            waterBakingCave = -1;
            if (waterKeys.IsCreated) waterKeys.Dispose();
            if (waterSamples.IsCreated) waterSamples.Dispose();
            if (waterFlow.IsCreated) waterFlow.Dispose();
            if (waterWhite.IsCreated) waterWhite.Dispose();
        }

        /// <summary>Drops every baked cave's water (and any bake in flight).</summary>
        void ReleaseCaveWater()
        {
            waterJob.Complete();
            DisposeWaterBake();
            foreach (var list in caveWaterTiles.Values)
                foreach (var t in list)
                    if (t.Mesh != null) Destroy(t.Mesh);
            caveWaterTiles.Clear();
        }
    }
}
