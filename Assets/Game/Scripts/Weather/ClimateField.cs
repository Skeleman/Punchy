using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// Design note (W-116 row 3, systems audit 2026-11-06, item 6)
// ----------------------------------------------------------
// ClimateField is the one place game code asks "what is the air like here, now?". Before it, the creature comfort
// grid, the weather talk and the ambience each rebuilt temperature, wetness and wind from the climate noise, the
// biome and the WeatherField in their own way. Now:
//  - Climate.At(pos, time) is the sample: temperature, humidity, air quality, wind, rain, snow, storm, sunlight and
//    season. It is plain maths over a SharedStatic grid, so it is safe in Burst jobs and costs a few loads.
//  - The grid (15 x 15 cells, 32 m apart, round the players) is filled on the main thread at most once a second,
//    because WeatherField.Sample is managed: about 20 ms in all, so after the first fill it is spread over frames
//    (0.4 ms each) and swapped in whole. Each cell holds what doesn't change with the hour: the climate's heat
//    and humidity (the climate field pulled toward its biome's own ranges, warmed by volcanism), ground elevation,
//    and the current weather (from WeatherField, the source behind all of this). At adds the hour on top: the day's
//    swing, the midday peak in hot climates, and cooling with height.
//  - The grid is lazy: it is only filled while something has called At in the last second, so it costs nothing
//    until a reader uses it.
//  - Climate.Exact is the same sample computed straight from the terrain and the weather at any point (managed,
//    for probes and one-off reads far from the players).
//  - Climate.Camera / CameraWind are the exact weather at the camera, published by WeatherSystem every frame, for
//    camera-side effects (ambience, breath, particles) that need it exact rather than per 32 m cell.
// Heat, Humidity, Rain, Snow, Wind, Storm and Sunlight use the same formulas as CreatureComfort's grid (A-71), so
// the animal lane (A-81) can switch Comfort and WeatherTalkSystem to Climate.At with no change in behaviour.
// Temperature is the felt air temperature (Comfort.FeltHeat outdoors, less a little with height). Season is a stub
// (always 0, high summer) until seasons exist.
namespace Punchy.Weather
{
    /// <summary>The air at one point and moment (Climate.At). All 0..1 unless noted.</summary>
    public struct ClimateSample
    {
        /// <summary>Felt air temperature: the climate's heat with the day's swing, sun, rain, wind and height.</summary>
        public float Temperature;
        /// <summary>The climate's heat before the hour is added (CreatureComfort's ComfortWeather.Heat).</summary>
        public float Heat;
        public float Humidity;
        /// <summary>1 clean air, falling with dust, thick fog and volcanic fumes.</summary>
        public float AirQuality;
        /// <summary>The way the wind blows (unit xz, toward) times its strength; WindStrength is its length.</summary>
        public float2 Wind;
        public float WindStrength;
        /// <summary>Rain and snow falling, storm (lightning, hail), sunlight reaching the ground (1 a clear day).</summary>
        public float Rain, Snow, Storm, Sunlight;
        /// <summary>Ground height above sea level, metres.</summary>
        public float Elevation;
        /// <summary>Time of year, 0..1 (stub: always 0, high summer).</summary>
        public float Season;
    }

    /// <summary>The moment a climate sample is for: hour of day 0..24, night 0..1 (DayNightCycle.Night), and whether
    /// the hour is known (no DayNightCycle in the scene: no midday peak).</summary>
    public struct ClimateTime
    {
        public float Hour, Night;
        public bool Known;

        public static ClimateTime At(float hour, float night) => new ClimateTime { Hour = hour, Night = night, Known = true };
    }

    /// <summary>The coarse grid Climate.At reads (see the design note at the top of ClimateField.cs).</summary>
    public struct ClimateGrid
    {
        public const int Size = 15;
        public const float Spacing = 32f;
        /// <summary>Per cell: x rain, y snow, z wind strength, w storm.</summary>
        public FixedList4096Bytes<float4> Sky;
        /// <summary>Per cell: x heat, y humidity, z sunlight, w air quality.</summary>
        public FixedList4096Bytes<float4> Ground;
        /// <summary>Per cell: ground elevation above sea level.</summary>
        public FixedList4096Bytes<float> Elevation;
        public float2 Origin;
        /// <summary>The way the wind blows (toward, unit xz).</summary>
        public float2 WindDirection;
        /// <summary>The current moment (set every frame by Refresh), for Climate.At without a time.</summary>
        public ClimateTime Now;
        public int Ready;
        /// <summary>Set by At: someone read the grid since the last refresh, so keep it filled.</summary>
        public int Wanted;
    }

    /// <summary>
    /// The world's climate and weather as one sample (W-116 row 3). See the design note at the top of this file.
    /// </summary>
    public static class Climate
    {
        struct GridKey { }
        public static readonly SharedStatic<ClimateGrid> Grid = SharedStatic<ClimateGrid>.GetOrCreate<GridKey>();
        const int Size = ClimateGrid.Size;

        /// <summary>The exact weather at the camera this frame (WeatherSystem.Local), for camera-side effects.</summary>
        public static WeatherValues Camera { get; private set; }
        /// <summary>The wind at the camera: the way it blows (toward, xz) times its strength.</summary>
        public static float2 CameraWind { get; private set; }

        static float nextRefresh;
        // The fill in progress: cells are computed into `back` a slice per frame, then copied to the grid at once.
        static ClimateGrid back;
        static int filled = -1;
        static double fillMs, sliceMs;
        /// <summary>Main-thread milliseconds the last full grid fill took, summed over its slices (for probes).</summary>
        public static float LastRefreshMs { get; private set; }
        /// <summary>The longest single frame's share of the last fill, milliseconds.</summary>
        public static float LastSliceMs { get; private set; }
        /// <summary>Main-thread budget per frame for a refill once the grid is up, milliseconds.</summary>
        const double SliceBudgetMs = 0.4;

        /// <summary>WeatherSystem, every frame: the weather at the camera.</summary>
        public static void Publish(in WeatherValues local, Vector2 windDirection)
        {
            Camera = local;
            CameraWind = new float2(windDirection.x, windDirection.y) * local.Wind;
        }

        /// <summary>
        /// Every frame from the main thread (AmbienceSystem): records the moment, and once a second, if anything read
        /// the grid since, refills it round <paramref name="centre"/>. The first fill is done at once; later fills are
        /// spread over frames (a few cells each, within SliceBudgetMs) and swapped in whole when done. Without weather
        /// the sky stays clear; the climate still comes from the terrain.
        /// </summary>
        public static void Refresh(in TerrainSource terrain, float2 centre, WeatherSystem weather, DayNightCycle cycle)
        {
            ref var g = ref Grid.Data;
            g.Now = cycle != null ? ClimateTime.At(cycle.TimeOfDay, cycle.Night) : default;
            if (nextRefresh > Time.time + 1f)
            {
                // A new play: the clock went back, and statics outlive a play with domain reload off. Refill at once.
                nextRefresh = 0f;
                g.Ready = 0;
                filled = -1;
            }
            if (filled < 0)
            {
                if (Time.time < nextRefresh || (g.Wanted == 0 && g.Ready != 0))
                    return;
                nextRefresh = Time.time + 1f;
                g.Wanted = 0;
                back.Sky.Clear();
                back.Ground.Clear();
                back.Elevation.Clear();
                back.Origin = centre - (Size - 1) * 0.5f * ClimateGrid.Spacing;
                filled = 0;
                fillMs = sliceMs = 0.0;
            }
            var started = Time.realtimeSinceStartupAsDouble;
            var field = weather != null && weather.Field != null && weather.Field.Ready ? weather.Field : null;
            var budget = g.Ready == 0 ? double.MaxValue : SliceBudgetMs;
            do
            {
                var cell = new int2(filled % Size, filled / Size);
                Cell(terrain, field, back.Origin + (float2)cell * ClimateGrid.Spacing, out var sky, out var ground, out var elevation);
                back.Sky.Add(sky);
                back.Ground.Add(ground);
                back.Elevation.Add(elevation);
                filled++;
            } while (filled < Size * Size && (Time.realtimeSinceStartupAsDouble - started) * 1000.0 < budget);
            var ms = (Time.realtimeSinceStartupAsDouble - started) * 1000.0;
            fillMs += ms;
            sliceMs = math.max(sliceMs, ms);
            if (filled < Size * Size)
                return;
            g.Sky = back.Sky;
            g.Ground = back.Ground;
            g.Elevation = back.Elevation;
            g.Origin = back.Origin;
            var wind = weather != null ? (Vector2)weather.WindDirection : Vector2.right;
            g.WindDirection = new float2(wind.x, wind.y);
            g.Ready = 1;
            filled = -1;
            LastRefreshMs = (float)fillMs;
            LastSliceMs = (float)sliceMs;
        }

        /// <summary>The climate at a point now (the moment Refresh last recorded). Burst-safe.</summary>
        public static ClimateSample At(float3 position) => At(position, Grid.Data.Now);

        /// <summary>
        /// The climate at a point and moment, from the grid's nearest cell (the edge cell beyond it). Clear and mild
        /// before the first fill. Burst-safe. (The position's height is not used yet: elevation is the ground's.)
        /// </summary>
        public static ClimateSample At(float3 position, in ClimateTime time)
        {
            ref var g = ref Grid.Data;
            g.Wanted = 1;
            if (g.Ready == 0 || g.Sky.Length < Size * Size)
                return Finish(new float4(0f), new float4(0.45f, 0.5f, 1f, 1f), 0f, new float2(1f, 0f), time);
            var c = math.clamp((int2)math.round((position.xz - g.Origin) / ClimateGrid.Spacing), 0, Size - 1);
            var i = c.y * Size + c.x;
            return Finish(g.Sky[i], g.Ground[i], g.Elevation[i], g.WindDirection, time);
        }

        /// <summary>The same sample computed straight from the terrain and the weather at any point (main thread).</summary>
        public static ClimateSample Exact(in TerrainSource terrain, WeatherSystem weather, float3 position, in ClimateTime time)
        {
            var field = weather != null && weather.Field != null && weather.Field.Ready ? weather.Field : null;
            Cell(terrain, field, position.xz, out var sky, out var ground, out var elevation);
            var wind = weather != null ? (Vector2)weather.WindDirection : Vector2.right;
            return Finish(sky, ground, elevation, new float2(wind.x, wind.y), time);
        }

        /// <summary>What a grid cell holds: the weather (sky), the climate (ground) and the ground's elevation.</summary>
        static void Cell(in TerrainSource terrain, WeatherField field, float2 xz, out float4 sky, out float4 ground, out float elevation)
        {
            var climate = BiomeSampling.Climate(terrain.BiomeNoise, xz);
            var heat = HeatAt(terrain, xz, climate);
            var sun = 1f;
            var dust = 0f;
            var fog = 0f;
            sky = float4.zero;
            if (field != null)
            {
                var v = field.Sample(new Vector2(xz.x, xz.y));
                var rain = field.RainFraction(new Vector2(xz.x, xz.y)) * (1f - v.AlwaysSnow);
                sky = new float4(v.Precipitation * rain, v.Precipitation * (1f - rain), math.saturate(v.Wind),
                                 math.saturate(math.max(v.Lightning / 3f, v.Hail)));
                sun = math.saturate(v.Sunlight * (1f - v.Overcast * 0.6f));
                dust = v.Dust;
                fog = v.Fog;
            }
            // Air: dust and thick fog (past the haze every day has) and fumes round volcanic hotspots.
            var air = 1f - math.saturate(dust * 1.2f + math.max(fog - 1.5f, 0f) * 0.25f + climate.w * 0.35f);
            ground = new float4(heat.x, heat.y, sun, air);
            elevation = terrain.SampleGround(xz).Height - terrain.World.SeaLevel;
        }

        /// <summary>Adds the moment to a cell: the felt temperature (CreatureComfort.FeltHeat outdoors, cooler up high).</summary>
        static ClimateSample Finish(float4 sky, float4 ground, float elevation, float2 windDirection, in ClimateTime time)
        {
            var day = 1f - time.Night;
            var heat = ground.x + (day - 0.5f) * 0.2f * (1.2f - ground.y) + ground.z * 0.06f * day
                       - (sky.x + sky.y) * 0.05f - sky.z * 0.04f;
            if (time.Known)
            {
                // A hot climate's heat peaks at midday (A-75): up to 0.35 cooler away from about 11-16 h.
                var hot = math.saturate((ground.x - 0.55f) / 0.25f);
                var midday = math.smoothstep(0f, 1f, 1f - math.abs(time.Hour - 13.5f) / 3.5f);
                heat -= 0.35f * hot * (1f - midday);
            }
            // High ground is colder than its climate cell says (as the ambience's cold reads it).
            heat -= math.saturate((elevation - 90f) / 160f) * 0.25f;
            return new ClimateSample
            {
                Temperature = heat, Heat = ground.x, Humidity = ground.y, AirQuality = ground.w,
                Wind = windDirection * sky.z, WindStrength = sky.z,
                Rain = sky.x, Snow = sky.y, Storm = sky.w, Sunlight = ground.z,
                Elevation = elevation, Season = 0f
            };
        }

        /// <summary>
        /// Heat and humidity at a point: the climate field's, pulled halfway toward the middle of the ground's biome's
        /// own ranges where those are narrow (a glacier is cold wherever the noise put it), and warmed by volcanism.
        /// (The same as CreatureComfort's HeatAt, A-71.)
        /// </summary>
        public static float2 HeatAt(in TerrainSource terrain, float2 xz, float4 climate)
        {
            var heat = climate.y;
            var humid = climate.z;
            if (terrain.Biomes.IsCreated && terrain.Biomes.Value.Biomes.Length > 0)
            {
                var biomes = terrain.SampleGround(xz).Biomes;
                ref var set = ref terrain.Biomes.Value;
                float hSum = 0f, hW = 0f, uSum = 0f, uW = 0f;
                for (int i = 0; i < 4; i++)
                {
                    var id = biomes.Ids[i];
                    var w = biomes.Weights[i];
                    if (w <= 0f || id < 0 || id >= set.Biomes.Length)
                        continue;
                    ref var b = ref set.Biomes[id];
                    var tw = w * math.saturate(1f - (b.ClimateMax.y - b.ClimateMin.y));
                    hSum += (b.ClimateMin.y + b.ClimateMax.y) * 0.5f * tw;
                    hW += tw;
                    var uw = w * math.saturate(1f - (b.ClimateMax.z - b.ClimateMin.z));
                    uSum += (b.ClimateMin.z + b.ClimateMax.z) * 0.5f * uw;
                    uW += uw;
                }
                if (hW > 1e-3f)
                    heat = math.lerp(heat, hSum / hW, 0.5f * math.saturate(hW));
                if (uW > 1e-3f)
                    humid = math.lerp(humid, uSum / uW, 0.5f * math.saturate(uW));
            }
            return new float2(math.saturate(heat + climate.w * 0.4f), humid);
        }
    }
}
