using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Punchy.Weather
{
    /// <summary>Weather values at a point. Blendable, so cells and the background mix smoothly.</summary>
    public struct WeatherValues
    {
        public float Coverage, Darkness, Overcast, Sunlight, Fog, Wind, Precipitation, AlwaysSnow, Dust, Lightning;
        public float CloudBase, CloudThickness;

        public static WeatherValues From(WeatherPreset p) => new WeatherValues
        {
            Coverage = p.CloudCoverage, Darkness = p.CloudDarkness, Overcast = p.Overcast, Sunlight = p.Sunlight,
            Fog = p.Fog, Wind = p.Wind, Precipitation = p.Precipitation, AlwaysSnow = p.AlwaysSnow ? 1f : 0f,
            Dust = p.Dust, Lightning = p.LightningPerMinute, CloudBase = p.CloudBase, CloudThickness = p.CloudThickness
        };

        public static WeatherValues Lerp(in WeatherValues a, in WeatherValues b, float t) => new WeatherValues
        {
            Coverage = math.lerp(a.Coverage, b.Coverage, t), Darkness = math.lerp(a.Darkness, b.Darkness, t),
            Overcast = math.lerp(a.Overcast, b.Overcast, t), Sunlight = math.lerp(a.Sunlight, b.Sunlight, t),
            Fog = math.lerp(a.Fog, b.Fog, t), Wind = math.lerp(a.Wind, b.Wind, t),
            Precipitation = math.lerp(a.Precipitation, b.Precipitation, t), AlwaysSnow = math.lerp(a.AlwaysSnow, b.AlwaysSnow, t),
            Dust = math.lerp(a.Dust, b.Dust, t), Lightning = math.lerp(a.Lightning, b.Lightning, t),
            CloudBase = math.lerp(a.CloudBase, b.CloudBase, t), CloudThickness = math.lerp(a.CloudThickness, b.CloudThickness, t)
        };

        public void AddScaled(in WeatherValues v, float w)
        {
            Coverage += v.Coverage * w; Darkness += v.Darkness * w; Overcast += v.Overcast * w; Sunlight += v.Sunlight * w;
            Fog += v.Fog * w; Wind += v.Wind * w; Precipitation += v.Precipitation * w; AlwaysSnow += v.AlwaysSnow * w;
            Dust += v.Dust * w; Lightning += v.Lightning * w; CloudBase += v.CloudBase * w; CloudThickness += v.CloudThickness * w;
        }

        /// <summary>Encoding of cloud base and thickness in the cloud map (must match Sky.hlsl).</summary>
        public const float MaxCloudBase = 4000f, MaxCloudThickness = 9000f, MaxAnvilTop = 12000f;

        /// <summary>Light grey haze in the air: rain, or mist under thick, foggy cloud.</summary>
        public float Haze => math.max(Precipitation, math.saturate((Fog - 1f) * 0.25f));
    }

    /// <summary>
    /// Weather anchored in the world: a set of cells (rain cells, storms, sandstorms…) over a fair-weather
    /// background. Cells form at random places with the local biome's odds, drift with the wind, grow, linger and
    /// fade, and only exist where their preset is allowed (a sandstorm dissolves at the desert's edge). High ground
    /// keeps extra, lower cloud. The field is rendered into three world-covering textures each update:
    ///  weather map  rgba = cloud coverage, cloud darkness, rain/mist haze, dust;
    ///  wetness map  rg   = ground wetness (accumulates where it rains, dries over time), precipitation;
    ///  cloud map    rgba = cloud base / MaxCloudBase, cloud thickness / MaxCloudThickness,
    ///                      anvil strength, anvil top / MaxAnvilTop (storm anvils spread beyond their cells).
    /// </summary>
    public class WeatherField
    {
        public const int Resolution = 256;

        class Cell
        {
            public Vector2 Position;
            public Vector2 Drift;
            public float Radius;
            public int Preset;
            public float Age, Grow, Hold, Fade;

            public float Intensity => Age < Grow ? Mathf.SmoothStep(0f, 1f, Age / Grow)
                : Age < Grow + Hold ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (Age - Grow - Hold) / Fade);
            public bool Finished => Age >= Grow + Hold + Fade;
        }

        readonly List<Cell> cells = new List<Cell>();
        List<WeatherPreset> presets;
        float mapSize;
        Vector2 origin;
        float texel;

        // Static climate, per texel.
        float[] orography;                 // 0..1 how mountainous (extra cloud)
        float[] rainFraction;              // 1 = precipitation falls as rain, 0 = as snow
        float[][] chance;                  // [preset][texel] biome odds
        NativeArray<float> allowed;        // [preset * count + texel] 0..1 whether the preset can exist here
        NativeArray<float4> presetLook;    // per preset: coverage, darkness, haze, dust
        NativeArray<float> presetPrecip;
        NativeArray<float2> presetCloud;   // per preset: cloud base, thickness (metres)
        NativeArray<Color32> cloudPixels;
        NativeArray<float> orographyNative, rainFractionNative, wetness;
        NativeArray<Color32> weatherPixels, wetnessPixels;

        public Texture2D WeatherMap { get; private set; }
        public Texture2D WetnessMap { get; private set; }
        public Texture2D CloudMap { get; private set; }
        public Vector4 MapRect => new Vector4(origin.x, origin.y, 1f / mapSize, 1f / mapSize);
        public bool Ready { get; private set; }
        public int CellCount => cells.Count;

        public void Initialize(in TerrainSource source, BiomeDefinition[] palette, List<WeatherPreset> weatherPresets,
                               float snowLine, float size)
        {
            Dispose();
            presets = weatherPresets;
            mapSize = size;
            origin = new Vector2(-size * 0.5f, -size * 0.5f);
            texel = size / Resolution;
            var count = Resolution * Resolution;

            // Sample the terrain once per texel (Burst): height, snow, and the four strongest biomes.
            var heights = new NativeArray<float>(count, Allocator.TempJob);
            var snow = new NativeArray<float>(count, Allocator.TempJob);
            var biomeIds = new NativeArray<int4>(count, Allocator.TempJob);
            var biomeWeights = new NativeArray<float4>(count, Allocator.TempJob);
            new SampleClimateJob
            {
                Source = source, Origin = origin, Texel = texel, Resolution = Resolution,
                Heights = heights, Snow = snow, BiomeIds = biomeIds, BiomeWeights = biomeWeights
            }.Schedule(count, 256).Complete();

            orography = new float[count];
            rainFraction = new float[count];
            chance = new float[presets.Count][];
            for (int p = 0; p < presets.Count; p++)
                chance[p] = new float[count];
            allowed = new NativeArray<float>(presets.Count * count, Allocator.Persistent);

            for (int t = 0; t < count; t++)
            {
                var elevation = heights[t] - source.World.SeaLevel;
                orography[t] = math.saturate((elevation - 80f) / 220f);
                rainFraction[t] = 1f - math.max(math.saturate((elevation - snowLine) / 40f), math.saturate(snow[t] * 2f));
                for (int p = 0; p < presets.Count; p++)
                {
                    float c = 0f, a = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        var w = biomeWeights[t][k];
                        var id = biomeIds[t][k];
                        if (w <= 0f || id >= palette.Length) continue;
                        var biomeChance = ChanceIn(presets[p], palette[id]);
                        c += w * biomeChance;
                        a += w * (biomeChance > 0f ? 1f : 0f);
                    }
                    chance[p][t] = c;
                    allowed[p * count + t] = math.smoothstep(0.2f, 0.8f, a);
                }
            }
            heights.Dispose();
            snow.Dispose();
            biomeIds.Dispose();
            biomeWeights.Dispose();

            presetLook = new NativeArray<float4>(presets.Count, Allocator.Persistent);
            presetPrecip = new NativeArray<float>(presets.Count, Allocator.Persistent);
            presetCloud = new NativeArray<float2>(presets.Count, Allocator.Persistent);
            for (int p = 0; p < presets.Count; p++)
            {
                var v = WeatherValues.From(presets[p]);
                presetLook[p] = new float4(v.Coverage, v.Darkness, v.Haze, v.Dust);
                presetPrecip[p] = v.Precipitation;
                presetCloud[p] = new float2(v.CloudBase, v.CloudThickness);
            }
            orographyNative = new NativeArray<float>(orography, Allocator.Persistent);
            rainFractionNative = new NativeArray<float>(rainFraction, Allocator.Persistent);
            wetness = new NativeArray<float>(count, Allocator.Persistent);
            weatherPixels = new NativeArray<Color32>(count, Allocator.Persistent);
            wetnessPixels = new NativeArray<Color32>(count, Allocator.Persistent);
            cloudPixels = new NativeArray<Color32>(count, Allocator.Persistent);

            WeatherMap = CreateMap("WeatherMap");
            WetnessMap = CreateMap("WetnessMap");
            CloudMap = CreateMap("CloudMap");
            Ready = true;

            // Start with weather already in the world rather than an empty sky.
            for (int i = 0; i < 40 && cells.Count < MaxCellsHint; i++)
                TrySpawn(Random.Range(0f, 600f));
        }

        public int MaxCellsHint = 24;

        static Texture2D CreateMap(string name)
        {
            return new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
        }

        public static float ChanceIn(WeatherPreset preset, BiomeDefinition biome)
        {
            if (biome != null)
                foreach (var entry in biome.Weather)
                    if (entry.Preset == preset)
                        return entry.Chance;
            return preset.OnlyInListedBiomes ? 0f : preset.Chance;
        }

        public void Dispose()
        {
            if (allowed.IsCreated) allowed.Dispose();
            if (presetLook.IsCreated) presetLook.Dispose();
            if (presetPrecip.IsCreated) presetPrecip.Dispose();
            if (presetCloud.IsCreated) presetCloud.Dispose();
            if (cloudPixels.IsCreated) cloudPixels.Dispose();
            if (CloudMap != null) Object.Destroy(CloudMap);
            if (orographyNative.IsCreated) orographyNative.Dispose();
            if (rainFractionNative.IsCreated) rainFractionNative.Dispose();
            if (wetness.IsCreated) wetness.Dispose();
            if (weatherPixels.IsCreated) weatherPixels.Dispose();
            if (wetnessPixels.IsCreated) wetnessPixels.Dispose();
            if (WeatherMap != null) Object.Destroy(WeatherMap);
            if (WetnessMap != null) Object.Destroy(WetnessMap);
            cells.Clear();
            Ready = false;
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Ages and moves cells, spawns new ones, and occasionally redraws the maps.</summary>
        public void Step(float dt, Vector2 windDirection, float spawnsPerMinute, int maxCells, float soakSeconds, float drySeconds)
        {
            if (!Ready)
                return;
            MaxCellsHint = maxCells;

            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                c.Age += dt;
                var speed = 3f + 9f * presets[c.Preset].Wind;
                c.Position += (windDirection * speed + c.Drift) * dt;
                var outside = Mathf.Abs(c.Position.x) > mapSize * 0.55f || Mathf.Abs(c.Position.y) > mapSize * 0.55f;
                if (c.Finished || outside)
                    cells.RemoveAt(i);
            }

            if (cells.Count < maxCells && Random.value < spawnsPerMinute / 60f * dt)
                TrySpawn(0f);

            redrawTimer -= dt;
            pendingDt += dt;
            if (redrawTimer <= 0f)
            {
                redrawTimer = 0.1f;
                Redraw(pendingDt, soakSeconds, drySeconds);
                pendingDt = 0f;
            }
        }

        float redrawTimer, pendingDt;

        /// <summary>Picks a random place and lets the local biome odds decide what (if anything) forms there.</summary>
        void TrySpawn(float age)
        {
            var x = Random.Range(Resolution / 8, Resolution * 7 / 8);
            var y = Random.Range(Resolution / 8, Resolution * 7 / 8);
            var t = y * Resolution + x;

            // The fair-weather background (preset 0) competes too: picking it means nothing forms this time.
            float total = 0f;
            for (int p = 0; p < presets.Count; p++)
                total += chance[p][t];
            if (total <= 0f)
                return;
            var pick = Random.value * total;
            var chosen = 0;
            for (int p = 0; p < presets.Count; p++)
            {
                pick -= chance[p][t];
                if (pick <= 0f) { chosen = p; break; }
            }
            if (chosen == 0)
                return;
            Spawn(chosen, origin + (new Vector2(x, y) + Vector2.one * 0.5f) * texel, age, 90f);
        }

        void Spawn(int preset, Vector2 position, float age, float grow)
        {
            var p = presets[preset];
            cells.Add(new Cell
            {
                Position = position,
                Drift = Random.insideUnitCircle * 1.5f,
                Radius = Random.Range(p.Size.x, Mathf.Max(p.Size.x, p.Size.y)),
                Preset = preset,
                Age = age,
                Grow = grow,
                Hold = Random.Range(p.Duration.x, Mathf.Max(p.Duration.x, p.Duration.y)),
                Fade = 120f
            });
        }

        /// <summary>Forces a cell of the given preset centred on a point (for testing / scripted events).</summary>
        public void ForceCell(int preset, Vector2 position, float radius)
        {
            Spawn(preset, position, 0f, 4f);
            cells[cells.Count - 1].Radius = radius;
        }

        /// <summary>Starts fading every cell within the radius.</summary>
        public void Clear(Vector2 position, float radius)
        {
            foreach (var c in cells)
                if (Vector2.Distance(c.Position, position) < radius + c.Radius && c.Age < c.Grow + c.Hold)
                {
                    c.Hold = Mathf.Max(0f, c.Age - c.Grow);
                    c.Fade = 6f;
                }
        }

        /// <summary>Lightning: rolls for a strike in each stormy cell; returns the first strike position, if any.</summary>
        public bool TryStrike(float dt, out Vector3 position)
        {
            foreach (var c in cells)
            {
                var rate = presets[c.Preset].LightningPerMinute * c.Intensity;
                if (rate > 0f && Random.value < rate / 60f * dt)
                {
                    var p = c.Position + Random.insideUnitCircle * c.Radius * 0.6f;
                    position = new Vector3(p.x, 0f, p.y);
                    return true;
                }
            }
            position = default;
            return false;
        }

        // ---------------------------------------------------------------- sampling

        /// <summary>Weather at a world point (CPU, exact; the same blend the map job does).</summary>
        public WeatherValues Sample(Vector2 xz)
        {
            if (!Ready)
                return default;
            var background = WeatherValues.From(presets[0]);
            var oro = Bilinear(orography, xz);
            background.Coverage = math.min(1f, background.Coverage + oro * 0.35f);
            background.Darkness += oro * 0.15f;
            background.CloudBase -= oro * 250f;
            background.CloudThickness += oro * 500f;

            var sum = default(WeatherValues);
            float total = 0f;
            foreach (var c in cells)
            {
                var d = Vector2.Distance(c.Position, xz);
                if (d >= c.Radius) continue;
                var w = c.Intensity * math.smoothstep(1f, 0.55f, d / c.Radius) * BilinearAllowed(c.Preset, xz);
                if (w <= 0f) continue;
                sum.AddScaled(WeatherValues.From(presets[c.Preset]), w);
                total += w;
            }
            if (total <= 0f)
                return background;
            var k = math.min(total, 1f);
            var average = default(WeatherValues);
            average.AddScaled(sum, 1f / total);
            return WeatherValues.Lerp(background, average, k);
        }

        /// <summary>The live cells, for debug views and minimaps.</summary>
        public IEnumerable<(WeatherPreset preset, Vector2 position, float radius, float intensity)> Cells()
        {
            foreach (var c in cells)
                yield return (presets[c.Preset], c.Position, c.Radius, c.Intensity);
        }

        /// <summary>Whether precipitation here falls as rain (1) or snow (0), from height and snow cover.</summary>
        public float RainFraction(Vector2 xz) => Ready ? Bilinear(rainFraction, xz) : 1f;

        float Bilinear(float[] data, Vector2 xz) => BilinearAt(i => data[i], xz);

        float BilinearAllowed(int preset, Vector2 xz)
        {
            var offset = preset * Resolution * Resolution;
            return BilinearAt(i => allowed[offset + i], xz);
        }

        float BilinearAt(System.Func<int, float> value, Vector2 xz)
        {
            var uv = (xz - origin) / texel - Vector2.one * 0.5f;
            var x0 = Mathf.Clamp(Mathf.FloorToInt(uv.x), 0, Resolution - 2);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(uv.y), 0, Resolution - 2);
            var fx = Mathf.Clamp01(uv.x - x0);
            var fy = Mathf.Clamp01(uv.y - y0);
            var i = y0 * Resolution + x0;
            return Mathf.Lerp(Mathf.Lerp(value(i), value(i + 1), fx), Mathf.Lerp(value(i + Resolution), value(i + Resolution + 1), fx), fy);
        }

        // ---------------------------------------------------------------- maps

        void Redraw(float dt, float soakSeconds, float drySeconds)
        {
            var cellData = new NativeArray<CellData>(cells.Count, Allocator.TempJob);
            for (int i = 0; i < cells.Count; i++)
                cellData[i] = new CellData { Position = cells[i].Position, Radius = cells[i].Radius, Intensity = cells[i].Intensity, Preset = cells[i].Preset };

            var background = WeatherValues.From(presets[0]);
            new MapJob
            {
                Cells = cellData, PresetLook = presetLook, PresetPrecip = presetPrecip, PresetCloud = presetCloud, Allowed = allowed,
                Orography = orographyNative, RainFraction = rainFractionNative, Wetness = wetness,
                Background = new float4(background.Coverage, background.Darkness, background.Haze, background.Dust),
                BackgroundCloud = new float2(background.CloudBase, background.CloudThickness),
                Resolution = Resolution, Origin = origin, Texel = texel,
                SoakRate = 1f / math.max(soakSeconds, 0.1f), DryRate = 1f / math.max(drySeconds, 0.1f), Dt = dt,
                WeatherPixels = weatherPixels, WetnessPixels = wetnessPixels, CloudPixels = cloudPixels
            }.Schedule(Resolution * Resolution, 512).Complete();
            cellData.Dispose();

            WeatherMap.SetPixelData(weatherPixels, 0);
            WeatherMap.Apply(false);
            WetnessMap.SetPixelData(wetnessPixels, 0);
            WetnessMap.Apply(false);
            CloudMap.SetPixelData(cloudPixels, 0);
            CloudMap.Apply(false);
        }

        struct CellData
        {
            public float2 Position;
            public float Radius;
            public float Intensity;
            public int Preset;
        }

        [BurstCompile]
        struct MapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CellData> Cells;
            [ReadOnly] public NativeArray<float4> PresetLook;
            [ReadOnly] public NativeArray<float> PresetPrecip;
            [ReadOnly] public NativeArray<float2> PresetCloud;
            [ReadOnly] public NativeArray<float> Allowed;
            [ReadOnly] public NativeArray<float> Orography;
            [ReadOnly] public NativeArray<float> RainFraction;
            public NativeArray<float> Wetness;
            public float4 Background;
            public float2 BackgroundCloud;
            public int Resolution;
            public float2 Origin;
            public float Texel;
            public float SoakRate, DryRate, Dt;
            [WriteOnly] public NativeArray<Color32> WeatherPixels;
            [WriteOnly] public NativeArray<Color32> WetnessPixels;
            [WriteOnly] public NativeArray<Color32> CloudPixels;

            public void Execute(int t)
            {
                var xz = Origin + (new float2(t % Resolution, t / Resolution) + 0.5f) * Texel;
                var background = Background;
                var oro = Orography[t];
                background.x = math.min(1f, background.x + oro * 0.35f);
                background.y += oro * 0.15f;
                // Mountains hold lower, thicker cloud.
                var backgroundCloud = BackgroundCloud + new float2(-250f, 500f) * oro;

                float4 look = 0f;
                float2 cloud = 0f;
                float precip = 0f, total = 0f;
                float anvil = 0f, anvilTop = 0f;
                var count = Resolution * Resolution;
                for (int i = 0; i < Cells.Length; i++)
                {
                    var c = Cells[i];
                    var d = math.distance(c.Position, xz);

                    // Towering storms spread an anvil at their top, reaching well beyond the cell itself.
                    var tower = PresetCloud[c.Preset];
                    if (tower.y > 3000f && d < c.Radius * 1.7f)
                    {
                        var a = c.Intensity * math.smoothstep(1.7f, 1.0f, d / c.Radius) * Allowed[c.Preset * count + t];
                        if (a > anvil)
                        {
                            anvil = a;
                            anvilTop = tower.x + tower.y;
                        }
                    }

                    if (d >= c.Radius) continue;
                    var w = c.Intensity * math.smoothstep(1f, 0.55f, d / c.Radius) * Allowed[c.Preset * count + t];
                    if (w <= 0f) continue;
                    look += PresetLook[c.Preset] * w;
                    precip += PresetPrecip[c.Preset] * w;
                    cloud += PresetCloud[c.Preset] * w;
                    total += w;
                }

                var k = math.min(total, 1f);
                var value = total > 0f ? math.lerp(background, look / total, k) : background;
                // Cells are strongest at their centres, so tall clouds build into domes rather than flat slabs.
                var cloudShape = total > 0f ? math.lerp(backgroundCloud, cloud / total, k) : backgroundCloud;
                var precipitation = total > 0f ? precip / total * k : 0f;

                // Ground soaks up rain (not snow) and dries out afterwards.
                var rain = precipitation * RainFraction[t];
                var wet = Wetness[t];
                wet = rain > 0.05f ? math.min(1f, wet + rain * SoakRate * Dt) : math.max(0f, wet - DryRate * Dt);
                Wetness[t] = wet;

                WeatherPixels[t] = new Color32(Byte(value.x), Byte(value.y), Byte(value.z), Byte(value.w));
                WetnessPixels[t] = new Color32(Byte(wet), Byte(precipitation), 0, 255);
                CloudPixels[t] = new Color32(Byte(cloudShape.x / WeatherValues.MaxCloudBase), Byte(cloudShape.y / WeatherValues.MaxCloudThickness),
                                             Byte(anvil), Byte(anvilTop / WeatherValues.MaxAnvilTop));
            }

            static byte Byte(float v) => (byte)math.round(math.saturate(v) * 255f);
        }

        [BurstCompile]
        struct SampleClimateJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public float Texel;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Heights;
            [WriteOnly] public NativeArray<float> Snow;
            [WriteOnly] public NativeArray<int4> BiomeIds;
            [WriteOnly] public NativeArray<float4> BiomeWeights;

            public void Execute(int t)
            {
                var xz = Origin + (new float2(t % Resolution, t / Resolution) + 0.5f) * Texel;
                var ground = Source.SampleGround(xz);
                Heights[t] = ground.Height;
                Snow[t] = Source.SampleSurface(xz, ground, new float3(0f, 1f, 0f)).Snow;
                BiomeIds[t] = ground.Biomes.Ids;
                BiomeWeights[t] = ground.Biomes.Weights;
            }
        }
    }
}
