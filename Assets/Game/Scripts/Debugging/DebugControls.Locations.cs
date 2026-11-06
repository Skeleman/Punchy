using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, preset locations (N), the survey and the warp keys (F5-F8). Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        /// <summary>Teleports the player to the next (or previous) preset location.</summary>
        public void Jump(int step)
        {
            if (!EnsureLocations(out var source))
                return;
            current = ((current + step) % locations.Count + locations.Count) % locations.Count;
            var location = locations[current];

            var em = queryWorld.EntityManager;
            var player = playerQuery.GetSingletonEntity();
            var transform = em.GetComponentData<LocalTransform>(player);
            transform.Position = new float3(location.Position.x, source.SampleHeight(location.Position) + 0.1f, location.Position.y);
            transform.Rotation = quaternion.RotateY(math.radians(location.Yaw));
            em.SetComponentData(player, transform);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(location.Yaw, 12f);
        }

        float3 locationsKey;

        /// <summary>A-48: the cached queries belong to the live world. Before EnsureLocations has run (a client at join
        /// time) or once a lost host has disposed the world, they are default or stale and TryGetSingleton throws.</summary>
        bool QueriesLive()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
                EnsureLocations(out _);
            return queryWorld == world && queryWorld.IsCreated;
        }

        bool EnsureLocations(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                floraQuery = em.CreateEntityQuery(ComponentType.ReadOnly<FloraLookup>());
                queryWorld = world;
                locations.Clear();
                centres.Clear();
            }
            if (!terrainQuery.TryGetSingleton(out source) || playerQuery.CalculateEntityCount() != 1 || paletteQuery.CalculateEntityCount() != 1)
                return false;
            // (The baked v1 island comes first and v2 replaces it: find the spots again when the landmarks change, A-29b.)
            var landmarkKey = float3.zero;
            if (source.Landmarks.IsCreated && source.Landmarks.Value.Instances.Length > 0)
                landmarkKey = new float3(source.Landmarks.Value.Instances.Length, source.Landmarks.Value.Instances[0].Position);
            if (math.any(landmarkKey != locationsKey))
            {
                locationsKey = landmarkKey;
                locations.Clear();
                centres.Clear();
            }
            if (locations.Count == 0)
            {
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                FindLocations(source, palette);
                FindWarpTargets(source, palette);
            }
            return locations.Count > 0;
        }

        /// <summary>Finds a representative spot in every land biome plus a few landmarks.</summary>
        void FindLocations(in TerrainSource source, BiomePalette palette)
        {
            locations.Add(new Location { Name = "Start", Position = float2.zero, Yaw = 0f });
            // Straight away: the nearest cave entrance and the nearest settlement, wherever they are.
            if (NearestNotable(source, palette, -1, 1, out var cave, out var caveFacing, out var caveName))
                locations.Add(new Location { Name = $"Nearest cave ({caveName})", Position = cave, Yaw = Yaw(caveFacing - cave) });
            if (NearestNotable(source, palette, -1, 0, out var town, out var townFacing, out var townName))
                locations.Add(new Location { Name = $"Nearest settlement ({townName})", Position = town, Yaw = Yaw(townFacing - town) });

            // Survey the island on a 150 m grid (Burst): dominant biome (or -1 for water) and height.
            var half = source.World.WorldSize * 0.5f;
            const float step = 150f;
            var n = (int)(half * 2f / step) + 1;
            var biomeGrid = new NativeArray<int>(n * n, Allocator.TempJob);
            var heightGrid = new NativeArray<float>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = step, Size = n, Biome = biomeGrid, Height = heightGrid }
                .Schedule(n * n, 64).Complete();

            int At(int x, int z) => x < 0 || z < 0 || x >= n || z >= n ? -2 : biomeGrid[z * n + x];
            float2 ToWorld(int x, int z) => new float2(-half + x * step, -half + z * step);

            var oceanIndex = source.Biomes.Value.OceanIndex;
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                if (b == oceanIndex)
                    continue;
                // The point most deeply inside the biome: the most rings of same-biome dry land around it.
                int bestX = -1, bestZ = -1, bestScore = -1;
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    if (At(x, z) != b)
                        continue;
                    var score = 0;
                    for (int r = 1; r <= 8; r++, score++)
                    {
                        var d = (int)math.round(r * 0.707f);
                        if (At(x + r, z) != b || At(x - r, z) != b || At(x, z + r) != b || At(x, z - r) != b ||
                            At(x + d, z + d) != b || At(x - d, z + d) != b || At(x + d, z - d) != b || At(x - d, z - d) != b)
                            break;
                    }
                    if (score > bestScore) { bestScore = score; bestX = x; bestZ = z; }
                }
                // Rather than open country, stand by the biome's nearest settlement, or failing that a cave entrance.
                if (NearestNotable(source, palette, b, -1, out var notable, out var facing, out var name))
                    locations.Add(new Location { Name = $"{palette.Biomes[b].name}: {name}", Position = notable, Yaw = Yaw(facing - notable) });
                else if (bestScore >= 0)
                {
                    var p = ToWorld(bestX, bestZ);
                    locations.Add(new Location { Name = palette.Biomes[b].name, Position = p, Yaw = Yaw(-p) });
                }
            }

            // Landmarks.
            var top = 0;
            for (int i = 1; i < heightGrid.Length; i++)
                if (heightGrid[i] > heightGrid[top]) top = i;
            var summit = ToWorld(top % n, top / n);
            locations.Add(new Location { Name = $"Summit ({heightGrid[top] - source.World.SeaLevel:F0} m)", Position = summit, Yaw = Yaw(summit) });
            biomeGrid.Dispose();
            heightGrid.Dispose();

            if (source.Rivers.IsCreated)
            {
                ref var map = ref source.Rivers.Value;
                int lake = -1, river = -1;
                float lakeScore = float.MaxValue, riverWidth = 0f;
                for (int i = 0; i < map.Width.Length; i++)
                {
                    var p = map.Point(i);
                    if (map.LakeDepth[i] > 4f && math.length(p) < lakeScore) { lakeScore = math.length(p); lake = i; }
                    // An inland river: well above sea level (not its mouth), and the widest such stretch.
                    if (map.LakeDepth[i] <= 0f && map.Width[i] > riverWidth && map.Down[i] >= 0 && map.Bed[i] > source.World.SeaLevel + 18f)
                    { riverWidth = map.Width[i]; river = i; }
                }
                if (lake >= 0)
                    locations.Add(new Location { Name = "Lake shore", Position = WalkToDryLand(source, map.Point(lake)), Yaw = Yaw(map.Point(lake) - WalkToDryLand(source, map.Point(lake))) });
                if (river >= 0)
                {
                    var p = map.Point(river);
                    var flow = math.normalize(map.Point(map.Down[river]) - p);
                    var bank = p + new float2(-flow.y, flow.x) * (map.Width[river] * 0.5f + 12f);
                    locations.Add(new Location { Name = "River", Position = bank, Yaw = Yaw(p - bank) });
                }
            }

            // Beach: walk outward from the centre until the ground meets the sea.
            for (var r = 500f; r < half * 1.2f; r += 25f)
            {
                var p = new float2(r * 0.8f, r * 0.6f);
                var h = source.SampleHeight(p) - source.World.SeaLevel;
                if (h > 0.5f && h < 3f)
                {
                    locations.Add(new Location { Name = "Beach", Position = p, Yaw = Yaw(p) });
                    break;
                }
            }

            // The landmark of each type nearest the start, seen from just outside it (from the low side of a cliff).
            if (source.Landmarks.IsCreated && palette.Landmarks != null)
            {
                ref var set = ref source.Landmarks.Value;
                for (int t = 0; t < palette.Landmarks.Length; t++)
                {
                    var best = -1;
                    for (int i = 0; i < set.Instances.Length; i++)
                        if (set.Instances[i].Type == t && (best < 0 || math.lengthsq(set.Instances[i].Position) < math.lengthsq(set.Instances[best].Position)))
                            best = i;
                    if (best < 0)
                        continue;
                    ref var inst = ref set.Instances[best];
                    var radius = set.Types[t].Radius * inst.Scale;
                    var away = set.Types[t].Shape == LandmarkShape.Cliff ? -inst.Facing : math.normalizesafe(inst.Position, new float2(1f, 0f));
                    var at = inst.Position + away * radius * 1.1f;
                    locations.Add(new Location { Name = palette.Landmarks[t].name, Position = at, Yaw = Yaw(inst.Position - at) });
                    // (And its middle, a few metres off it, for things that live there: "-place VolcanoCone:centre".)
                    var middle = inst.Position + away * 8f;
                    centres.Add(new Location { Name = palette.Landmarks[t].name + ":centre", Position = middle, Yaw = Yaw(inst.Position - middle) });
                }
            }
        }

        [BurstCompile]
        struct SurveyJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Step;
            public int Size;
            [WriteOnly] public NativeArray<int> Biome;
            [WriteOnly] public NativeArray<float> Height;

            public void Execute(int i)
            {
                var p = new float2(Origin + i % Size * Step, Origin + i / Size * Step);
                var g = Source.SampleGround(p);
                Biome[i] = g.WaterDepth < -1f ? g.Biomes.Dominant() : -1;
                Height[i] = g.Height;
            }
        }

        /// <summary>
        /// The settlement (a landmark with upright, humanoid residents) nearest the start in a biome (any biome if
        /// biome is -1), else its nearest cave entrance; or only one kind (kind 0 settlements, 1 caves, -1 either).
        /// Where to stand just outside it (in front of a cliff), and the point to face.
        /// </summary>
        static bool NearestNotable(in TerrainSource source, BiomePalette palette, int biome, int kind, out float2 stand, out float2 facing, out string name)
        {
            stand = facing = default;
            name = null;
            if (!source.Landmarks.IsCreated || palette.Landmarks == null)
                return false;
            ref var set = ref source.Landmarks.Value;
            int best = -1, bestRank = int.MaxValue;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var type = inst.Type < palette.Landmarks.Length ? palette.Landmarks[inst.Type] : null;
                if (type == null)
                    continue;
                var settlement = false;
                foreach (var r in type.Residents)
                    settlement |= r.Species != null && r.Species.Body.Upright;
                var rank = settlement ? 0 : type.Cave ? 1 : -1;
                if (rank < 0 || kind >= 0 && rank != kind)
                    continue;
                source.SampleBiomes(inst.Position, out var biomes, out _);
                if (biome >= 0 && biomes.Dominant() != biome)
                    continue;
                var distance = math.length(inst.Position);
                if (rank < bestRank || rank == bestRank && distance < bestDistance)
                {
                    best = i;
                    bestRank = rank;
                    bestDistance = distance;
                }
            }
            if (best < 0)
                return false;
            ref var chosen = ref set.Instances[best];
            var radius = set.Types[chosen.Type].Radius * chosen.Scale;
            var away = set.Types[chosen.Type].Shape == LandmarkShape.Cliff ? -chosen.Facing : math.normalizesafe(chosen.Position, new float2(1f, 0f));
            stand = chosen.Position + away * radius * 0.9f;
            facing = chosen.Position;
            name = palette.Landmarks[chosen.Type].name;
            return true;
        }

        static float2 WalkToDryLand(in TerrainSource source, float2 waterPoint)
        {
            var dir = math.normalizesafe(-waterPoint, new float2(1f, 0f));
            var p = waterPoint;
            for (int k = 0; k < 200 && source.SampleGround(p).WaterDepth > -0.3f; k++)
                p += dir * 6f;
            return p + dir * 6f;
        }

        static float Yaw(float2 towards) => math.degrees(math.atan2(towards.x, towards.y));

        // ---------------------------------------------------------------- warp keys (A-36)

        // F5-F8 were free (F1 HUD, F3 copy position here; F4 is bound in the input actions). Local player only:
        // nothing goes on the network, and the world data is only read.
        static readonly Key[] WarpKeys = { Key.F5, Key.F6, Key.F7, Key.F8 };
        /// <summary>After a warp the follow camera comes in to at most this (m).</summary>
        const float WarpCameraDistance = 5f;
        /// <summary>The warp camera's pitch: low, under canopy rather than looking down through it.</summary>
        const float WarpPitch = 8f;

        /// <summary>
        /// A-38: the camera's yaw after a warp, kept out of tree crowns. Trees aren't in TerrainSource, so the plants
        /// round the player are read from the flora tables (FloraPlacement.Place, the pure function FloraSystem draws
        /// from; read only). From the landing's own heading, it turns in 30 degree steps either way and takes the first
        /// side whose line from the head out to the lens stays clear of crowns and trunks for the whole arm; if none
        /// does, the side with the longest clear stretch, the arm shortened to it. Returns the heading unchanged if
        /// the flora tables aren't up yet.
        /// </summary>
        float ClearWarpView(in TerrainSource land, float3 head, float yaw, float pitch, ref float arm)
        {
            using var q = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Flora.FloraLookup));
            if (!q.TryGetSingleton<Punchy.Flora.FloraLookup>(out var lookup) || !lookup.Blob.IsCreated)
                return yaw;
            ref var f = ref lookup.Blob.Value;
            var bestYaw = yaw;
            var bestClear = -1f;
            for (int i = 0; i < 12; i++)
            {
                // 0, +30, -30, +60, -60 ... 180.
                var turn = (i + 1) / 2 * 30f * (i % 2 == 0 ? -1f : 1f);
                var y = yaw + turn;
                var rotation = Quaternion.Euler(pitch, y, 0f);
                // Where the lens ends up: lifted over the ground as ThirdPersonCamera lifts it (on a slope that rises
                // behind the player that is well above the 8 degree line, among the crowns), so that line is tested.
                var lens = head - (float3)(rotation * Vector3.forward) * arm;
                var ground = land.SampleGround(lens.xz);
                lens.y = math.max(lens.y, math.max(ground.Height, ground.HasWater ? ground.WaterSurface : float.MinValue) + 0.5f);
                var length = math.distance(head, lens);
                // (As a share of the arm: the lens comes in along its lifted line.)
                var clear = ClearArm(ref f, land, head, (lens - head) / math.max(length, 1e-4f), length) / math.max(length, 1e-4f);
                if (clear >= 0.999f)
                {
                    Debug.Log($"DebugControls: warp camera clear of crowns, turned {turn:F0} deg, arm {arm:F1} m, lens {lens.y - head.y:F1} m over the head");
                    return y;
                }
                if (clear > bestClear + 0.05f)
                {
                    bestClear = clear;
                    bestYaw = y;
                }
            }
            arm = Mathf.Max(bestClear * arm - 0.3f, 1.5f);
            Debug.Log($"DebugControls: warp camera, no side clear of crowns; turned {Mathf.DeltaAngle(yaw, bestYaw):F0} deg, arm {arm:F1} m");
            return bestYaw;
        }

        /// <summary>How far out from the head along <paramref name="dir"/> (unit) the lens can go, up to
        /// <paramref name="length"/>, before it meets a crown or a trunk, in 0.25 m steps. The margin widens to
        /// <c>LensMargin</c> over the last 1.5 m: right at the lens, leaves just off the line still fill the frame.</summary>
        static float ClearArm(ref Punchy.Flora.FloraBlob f, in TerrainSource land, float3 head, float3 dir, float length)
        {
            const float LineMargin = 0.5f, LensMargin = 1.2f;
            var end = head + dir * length;
            var lo = math.min(head.xz, end.xz) - 9f;
            var hi = math.max(head.xz, end.xz) + 9f;
            var t = length;
            for (int layer = 1; layer <= Punchy.Flora.FloraPlacement.TreeLayer; layer++)
            {
                var size = f.CellSizes[layer];
                var c0 = (int2)math.floor(lo / size);
                var c1 = (int2)math.floor(hi / size);
                for (int cy = c0.y; cy <= c1.y; cy++)
                for (int cx = c0.x; cx <= c1.x; cx++)
                {
                    var plant = Punchy.Flora.FloraPlacement.Place(ref f, land, new int2(cx, cy), layer);
                    if (plant.Model < 0 || plant.Model >= f.Models.Length)
                        continue;
                    var shape = f.Models[plant.Model];
                    var s = plant.Scale;
                    var top = plant.Position.y + shape.Height * s;
                    if (shape.Height * s < 1f)
                        continue;
                    var crownBase = plant.Position.y + shape.CrownBase * s;
                    // Walk the line from the head out, stopping at the first point inside this plant.
                    for (var d = 0.5f; d <= t; d += 0.25f)
                    {
                        var margin = d > length - 1.5f ? LensMargin : LineMargin;
                        var crown = shape.CrownRadius * s + margin;
                        var trunk = math.max(shape.TrunkRadius * s, 0.3f) + margin;
                        var p = head + dir * d;
                        var r = math.distance(p.xz, plant.Position.xz);
                        var inCrown = shape.CrownRadius > 0f && r < crown && p.y > crownBase - margin && p.y < top + margin;
                        var inTrunk = shape.TrunkRadius > 0f && r < trunk && p.y < top;
                        if (inCrown || inTrunk)
                        {
                            t = d - 0.25f;
                            break;
                        }
                    }
                }
            }
            return t;
        }
        static readonly string[] WarpNames = { "biome", "settlement", "cave", "landmark" };
        readonly List<Location>[] warpTargets = { new List<Location>(), new List<Location>(), new List<Location>(), new List<Location>() };
        readonly int[] warpIndex = { -1, -1, -1, -1 };
        readonly float2[] warpLast = new float2[4];
        string warpBanner;
        float warpBannerUntil;
        GUIStyle warpStyle;

        /// <summary>"biome", "town"/"settlement"/"city", "cave" or "landmark" to its warp category, else -1.</summary>
        static int WarpCategory(string word) => word?.ToLowerInvariant() switch
        {
            "biome" => 0,
            "town" or "settlement" or "city" => 1,
            "cave" => 2,
            "landmark" => 3,
            _ => -1,
        };

        /// <summary>
        /// F5-F8: the next (step 1) or previous (-1) point of interest of a category. The first press (or the first
        /// after wandering 300 m from the last warp) orders the list by distance from the player, nearest first.
        /// Lands like N and -goto: on the ground, never in water (stand points are walked to dry land).
        /// </summary>
        void Warp(int category, int step)
        {
            if (!EnsureLocations(out var source))
                return;
            var list = warpTargets[category];
            if (list.Count == 0)
            {
                warpBanner = $"F{5 + category} {WarpNames[category]}: none on this island";
                warpBannerUntil = Time.unscaledTime + 3f;
                return;
            }
            var em = queryWorld.EntityManager;
            var player = playerQuery.GetSingletonEntity();
            var transform = em.GetComponentData<LocalTransform>(player);
            var here = transform.Position.xz;
            if (warpIndex[category] < 0 || math.distance(here, warpLast[category]) > 300f)
            {
                list.Sort((a, b) => math.distancesq(a.Position, here).CompareTo(math.distancesq(b.Position, here)));
                warpIndex[category] = step > 0 ? 0 : list.Count - 1;
            }
            else
                warpIndex[category] = ((warpIndex[category] + step) % list.Count + list.Count) % list.Count;
            var target = list[warpIndex[category]];
            warpLast[category] = target.Position;

            transform.Position = new float3(target.Position.x, source.SampleHeight(target.Position) + 0.1f, target.Position.y);
            transform.Rotation = quaternion.RotateY(math.radians(target.Yaw));
            em.SetComponentData(player, transform);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            // Close in and low (A-37): under tree canopy rather than looking down through it; a hill in the way pulls
            // the camera in further (ThirdPersonCamera).
            if (cam != null)
            {
                var head = transform.Position + new float3(0f, cam.TargetHeight, 0f);
                var arm = Mathf.Min(cam.Distance, WarpCameraDistance);
                var yaw = ClearWarpView(source, head, target.Yaw, WarpPitch, ref arm);
                cam.Distance = arm;
                cam.SetView(yaw, WarpPitch);
            }
            warpBanner = $"F{5 + category} {WarpNames[category]}: <b>{target.Name}</b>  {warpIndex[category] + 1}/{list.Count}  ({target.Position.x:F0}, {target.Position.y:F0})";
            warpBannerUntil = Time.unscaledTime + 3f;
            Debug.Log($"DebugControls: warp {WarpNames[category]} {warpIndex[category] + 1}/{list.Count} {target.Name} at {target.Position.x:F0},{target.Position.y:F0}");
        }

        /// <summary>
        /// The warp lists, from the baked world data (read only): one spot per biome region (a connected patch of the
        /// 150 m survey), each settlement, each cave entrance and each landmark.
        /// </summary>
        void FindWarpTargets(in TerrainSource source, BiomePalette palette)
        {
            foreach (var list in warpTargets)
                list.Clear();
            for (int c = 0; c < 4; c++)
                warpIndex[c] = -1;

            // Biome regions: flood-fill the survey grid, keep patches of 4+ cells, stand at each one's deepest cell.
            var half = source.World.WorldSize * 0.5f;
            const float step = 150f;
            var n = (int)(half * 2f / step) + 1;
            var biomeGrid = new NativeArray<int>(n * n, Allocator.TempJob);
            var heightGrid = new NativeArray<float>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = step, Size = n, Biome = biomeGrid, Height = heightGrid }
                .Schedule(n * n, 64).Complete();
            var region = new int[n * n];
            var stack = new Stack<int>();
            var cells = new List<int>();
            var oceanIndex = source.Biomes.Value.OceanIndex;
            for (int start = 0; start < n * n; start++)
            {
                var b = biomeGrid[start];
                if (b < 0 || b == oceanIndex || region[start] != 0)
                    continue;
                cells.Clear();
                region[start] = 1;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    cells.Add(i);
                    int x = i % n, z = i / n;
                    if (x > 0 && region[i - 1] == 0 && biomeGrid[i - 1] == b) { region[i - 1] = 1; stack.Push(i - 1); }
                    if (x < n - 1 && region[i + 1] == 0 && biomeGrid[i + 1] == b) { region[i + 1] = 1; stack.Push(i + 1); }
                    if (z > 0 && region[i - n] == 0 && biomeGrid[i - n] == b) { region[i - n] = 1; stack.Push(i - n); }
                    if (z < n - 1 && region[i + n] == 0 && biomeGrid[i + n] == b) { region[i + n] = 1; stack.Push(i + n); }
                }
                if (cells.Count < 4)
                    continue;
                // The deepest cell: the most rings of the same biome around it.
                int best = cells[0], bestScore = -1;
                foreach (var i in cells)
                {
                    int x = i % n, z = i / n, score = 0;
                    for (int r = 1; r <= 8; r++, score++)
                    {
                        bool Same(int dx, int dz) => x + dx >= 0 && z + dz >= 0 && x + dx < n && z + dz < n && biomeGrid[(z + dz) * n + x + dx] == b;
                        if (!Same(r, 0) || !Same(-r, 0) || !Same(0, r) || !Same(0, -r))
                            break;
                    }
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                var p = new float2(-half + best % n * step, -half + best / n * step);
                warpTargets[0].Add(new Location { Name = $"{palette.Biomes[b].name} ({cells.Count * 2.25f:F0} ha)", Position = p, Yaw = Yaw(-p) });
            }
            biomeGrid.Dispose();
            heightGrid.Dispose();

            // Settlements: on the main road just outside, facing the middle.
            var hasLandmarks = source.Landmarks.IsCreated && palette.Landmarks != null;
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int s = 0; s < towns.Settlements.Length; s++)
                {
                    ref var town = ref towns.Settlements[s];
                    var name = "Settlement";
                    if (hasLandmarks && town.Landmark >= 0 && town.Landmark < source.Landmarks.Value.Instances.Length)
                    {
                        var t = source.Landmarks.Value.Instances[town.Landmark].Type;
                        if (t < palette.Landmarks.Length && palette.Landmarks[t] != null)
                            name = palette.Landmarks[t].name;
                    }
                    var road = math.lengthsq(town.Road) > 0.01f ? town.Road : math.normalizesafe(town.Centre, new float2(1f, 0f));
                    var at = Dry(source, town.Centre + road * math.max(town.Radius, town.Palisade) * 1.05f, town.Centre);
                    warpTargets[1].Add(new Location { Name = $"{name} #{s + 1}", Position = at, Yaw = Yaw(town.Centre - at) });
                }
            }

            // Cave entrances: a few metres out from the mouth (away from the first tunnel node), facing in.
            if (source.Caves.IsCreated)
            {
                ref var caves = ref source.Caves.Value;
                for (int c = 0; c < caves.Caves.Length; c++)
                {
                    ref var cave = ref caves.Caves[c];
                    var mouth = cave.Mouth.xz;
                    var outward = cave.NodeCount >= 2
                        ? math.normalizesafe(mouth - caves.Nodes[cave.FirstNode + 1].Position.xz, new float2(1f, 0f))
                        : math.normalizesafe(mouth, new float2(1f, 0f));
                    var at = Dry(source, mouth + outward * 6f, mouth);
                    source.SampleBiomes(mouth, out var biomes, out _);
                    var biome = biomes.Dominant();
                    var where = biome >= 0 && biome < palette.Biomes.Length ? palette.Biomes[biome].name : "cave";
                    warpTargets[2].Add(new Location { Name = $"Cave #{c + 1} ({where})", Position = at, Yaw = Yaw(mouth - at) });
                }
            }

            // Landmarks: every one, seen from just outside (the low side of a cliff), as N does. Not the caves and
            // grottoes (a landmark type with a Cave): F7 already goes to those (A-43, the owner's call).
            if (hasLandmarks)
            {
                ref var set = ref source.Landmarks.Value;
                var caveKinds = new SortedDictionary<string, int>();
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var inst = ref set.Instances[i];
                    if (inst.Type >= palette.Landmarks.Length || palette.Landmarks[inst.Type] == null)
                        continue;
                    if (palette.Landmarks[inst.Type].Cave)
                    {
                        caveKinds.TryGetValue(palette.Landmarks[inst.Type].name, out var seen);
                        caveKinds[palette.Landmarks[inst.Type].name] = seen + 1;
                        continue;
                    }
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    var away = set.Types[inst.Type].Shape == LandmarkShape.Cliff ? -inst.Facing : math.normalizesafe(inst.Position, new float2(1f, 0f));
                    var at = Dry(source, inst.Position + away * radius * 1.1f, inst.Position);
                    warpTargets[3].Add(new Location { Name = palette.Landmarks[inst.Type].name, Position = at, Yaw = Yaw(inst.Position - at) });
                }
                var dropped = 0;
                foreach (var kind in caveKinds.Values)
                    dropped += kind;
                Debug.Log($"DebugControls: F8 landmarks {warpTargets[3].Count + dropped} before, {warpTargets[3].Count} after leaving out caves: " +
                          string.Join(", ", System.Linq.Enumerable.Select(caveKinds, k => $"{k.Key} {k.Value}")));
            }

            Debug.Log($"DebugControls: warp keys F5 {WarpNames[0]}, F6 {WarpNames[1]}, F7 {WarpNames[2]}, F8 {WarpNames[3]} (Shift back; F4 is taken by the input actions); targets: biomes {warpTargets[0].Count}, settlements {warpTargets[1].Count}, caves {warpTargets[2].Count}, landmarks {warpTargets[3].Count}");
        }

        /// <summary>A stand point on dry land: if it is in water, walk back toward the thing it looks at until dry.</summary>
        static float2 Dry(in TerrainSource source, float2 p, float2 towards)
        {
            var dir = math.normalizesafe(towards - p, new float2(1f, 0f));
            for (int k = 0; k < 40 && source.SampleGround(p).WaterDepth > -0.3f; k++)
                p += dir * 4f;
            return source.SampleGround(p).WaterDepth > -0.3f ? WalkToDryLand(source, p) : p;
        }
    }
}
