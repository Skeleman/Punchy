using System.Collections.Generic;
using Punchy.CameraRig;
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
    /// <summary>
    /// Development helpers for looking at the world under different conditions, with a small on-screen readout:
    ///  N / Shift+N  jump to the next / previous preset location (one deep in each biome, a summit, a lake shore,
    ///               a river, a beach, and one of each landmark type; found automatically from the terrain);
    ///  B            next day type (sunny, partly cloudy, changeable, overcast, stormy), replacing today's weather;
    ///  Y            advance the clock two hours;  T  toggle fast time (DayNightCycle);
    ///  ] / [        force the next weather over the player / clear it (WeatherSystem);
    ///  F1           show / hide this readout.
    /// </summary>
    public class DebugControls : MonoBehaviour
    {
        public WeatherSystem Weather;
        public DayNightCycle Cycle;
        public bool ShowReadout = true;

        struct Location
        {
            public string Name;
            public float2 Position;
            public float Yaw;
        }

        readonly List<Location> locations = new List<Location>();
        int current = -1;
        EntityQuery playerQuery, terrainQuery, paletteQuery;
        World queryWorld;
        GUIStyle style;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f1Key.wasPressedThisFrame)
                ShowReadout = !ShowReadout;
            if (keyboard.bKey.wasPressedThisFrame && Weather != null)
                Weather.NextDayType();
            if (keyboard.yKey.wasPressedThisFrame && Cycle != null)
                Cycle.TimeOfDay = Mathf.Repeat(Cycle.TimeOfDay + 2f, 24f);
            if (keyboard.nKey.wasPressedThisFrame)
                Jump(keyboard.shiftKey.isPressed ? -1 : 1);
        }

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
                queryWorld = world;
                locations.Clear();
            }
            if (!terrainQuery.TryGetSingleton(out source) || playerQuery.CalculateEntityCount() != 1 || paletteQuery.CalculateEntityCount() != 1)
                return false;
            if (locations.Count == 0)
                FindLocations(source, queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity()));
            return locations.Count > 0;
        }

        /// <summary>Finds a representative spot in every land biome plus a few landmarks.</summary>
        void FindLocations(in TerrainSource source, BiomePalette palette)
        {
            locations.Add(new Location { Name = "Start", Position = float2.zero, Yaw = 0f });

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
                if (bestScore >= 0)
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

        static float2 WalkToDryLand(in TerrainSource source, float2 waterPoint)
        {
            var dir = math.normalizesafe(-waterPoint, new float2(1f, 0f));
            var p = waterPoint;
            for (int k = 0; k < 200 && source.SampleGround(p).WaterDepth > -0.3f; k++)
                p += dir * 6f;
            return p + dir * 6f;
        }

        static float Yaw(float2 towards) => math.degrees(math.atan2(towards.x, towards.y));

        // ---------------------------------------------------------------- readout

        void OnGUI()
        {
            if (!ShowReadout || !Application.isPlaying)
                return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };

            var lines = new List<string>();
            if (EnsureLocations(out var source) && playerQuery.TryGetSingleton<LocalTransform>(out var player))
            {
                var xz = player.Position.xz;
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                var ground = source.SampleGround(xz);
                var place = current >= 0 ? locations[current].Name : "-";
                lines.Add($"<b>{place}</b>   biome: {palette.Biomes[ground.Biomes.Dominant()].name}   height {ground.Height - source.World.SeaLevel:F0} m");
                if (Weather != null && Weather.Field.Ready)
                    lines.Add($"day: <b>{Weather.DayTypeName}</b>   weather here: {Weather.Field.Describe(new Vector2(xz.x, xz.y))}");
            }
            if (Cycle != null)
            {
                var hours = (int)Cycle.TimeOfDay;
                lines.Add($"time {hours:00}:{(int)((Cycle.TimeOfDay - hours) * 60f):00}");
            }
            lines.Add("<color=#bbbbbb>N/Shift+N place · B day type · Y +2 h · T fast time · ] force weather · [ clear · F1 hide</color>");

            var text = string.Join("\n", lines);
            var size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, size.x + 12, size.y + 8), text, style);
        }
    }
}
