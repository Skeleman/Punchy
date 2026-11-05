using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>What is making a creature uncomfortable (A-71), worst first in CreatureMind.DiscomfortKind.</summary>
    public enum ComfortKind : byte { None, Hot, Dry, Cold, Rain, Snow, Storm, Bright, Wind }

    /// <summary>Which folk a comfort profile and its lines belong to (A-71); None for animals.</summary>
    public enum ComfortRace : byte { None, Human, Elf, Dwarf, Goblin, Ogre, BatFolk, FishFolk, Pygmy, Hermit, Ghost }

    /// <summary>The kinds of shelter a creature looks for when uncomfortable (A-71 row 2).</summary>
    [System.Flags]
    public enum ShelterNeed : byte { None = 0, Roof = 1, Tree = 2, Water = 4, Fire = 8, Shade = 16, Cave = 32 }

    /// <summary>
    /// A species' taste in weather (A-71): the heat it is happy in (on the climate's 0..1 temperature scale, with the
    /// day's swing added), how much rain, snow, storms, bright sun, wind and dry heat bother it (0 not at all, 1 a lot,
    /// below 0 it likes it), how far out of range it shrugs off, and what shelter it seeks. Built once per species
    /// (ComfortProfiles.For); each individual is a little hardier or fussier (Pressure).
    /// </summary>
    public struct ComfortProfile
    {
        public float2 Heat;
        public float Rain, Snow, Storm, Bright, Wind, Dry;
        public float Tolerance;
        public ShelterNeed Seeks;
        public ComfortRace Race;
        /// <summary>False for anything that never minds the weather (fish, ghosts, rooted plants).</summary>
        public bool Minds;
    }

    /// <summary>The weather and climate at one point, as comfort reads it (Comfort.WeatherAt).</summary>
    public struct ComfortWeather
    {
        /// <summary>Climate heat 0..1 (before the day's swing), humidity 0..1.</summary>
        public float Heat, Humidity;
        /// <summary>0..1 each: rain, snow, wind strength, storm (lightning, hail), sunlight (1 a clear day).</summary>
        public float Rain, Snow, Wind, Storm, Sunlight;
    }

    /// <summary>
    /// A coarse grid of weather and climate round the players (A-71), sampled on the main thread about once a second
    /// (WeatherField.Sample is managed) and read by the brain in Burst. Cells are 32 m: weather cells are hundreds of
    /// metres across and climate fields regions across, so nothing finer is needed.
    /// </summary>
    public struct ComfortGrid
    {
        public const int Size = 15;
        public const float Spacing = 32f;
        /// <summary>Per cell: x rain, y snow, z wind, w storm.</summary>
        public FixedList4096Bytes<float4> Sky;
        /// <summary>Per cell: x heat, y humidity, z sunlight.</summary>
        public FixedList4096Bytes<float4> Ground;
        public float2 Origin;
        /// <summary>The way the wind blows (toward, unit xz; A-72 row 6, for a rock's lee side).</summary>
        public float2 WindDirection;
        public int Ready;
    }

    /// <summary>
    /// Comfort (A-71): the weather grid and the Burst maths that turns a creature's profile and the weather where it
    /// stands into how uncomfortable it is and why. The meter itself lives in CreatureMind (CreatureBrainSystem.Comfort).
    /// </summary>
    public static class Comfort
    {
        struct GridKeyV2 { }   // (V2: A-72 row 6 added WindDirection; a SharedStatic's key can't change size in one editor run)
        public static readonly SharedStatic<ComfortGrid> Grid = SharedStatic<ComfortGrid>.GetOrCreate<GridKeyV2>();
        struct OffKey { }
        /// <summary>Test flag "-comfort off": no creature minds the weather.</summary>
        public static readonly SharedStatic<int> Off = SharedStatic<int>.GetOrCreate<OffKey>();
        struct HeatShiftKey { }
        /// <summary>Test flag "-comfortheat x": added to the grid's heat everywhere (a forced cold snap or heat wave).</summary>
        public static readonly SharedStatic<float> HeatShift = SharedStatic<float>.GetOrCreate<HeatShiftKey>();

        static Punchy.Weather.WeatherSystem weather;
        static float nextRefresh;

        /// <summary>
        /// Samples the grid round the players' middle once a second (FaunaDirector.Update). Without weather (none in
        /// the scene yet) the sky stays clear; climate still comes from the terrain.
        /// </summary>
        public static void Refresh(List<float3> players, in TerrainSource terrain)
        {
            if (nextRefresh > Time.time + 1f)
                nextRefresh = 0f; // (a new play: the clock went back, and statics outlive a play with domain reload off)
            if (players.Count == 0 || Time.time < nextRefresh)
                return;
            nextRefresh = Time.time + 1f;
            if (weather == null)
                weather = Object.FindFirstObjectByType<Punchy.Weather.WeatherSystem>();
            var field = weather != null ? weather.Field : null;
            var ready = field != null && field.Ready;
            var centre = float2.zero;
            foreach (var p in players)
                centre += p.xz;
            centre /= players.Count;
            var origin = centre - (Size - 1) * 0.5f * ComfortGrid.Spacing;
            ref var g = ref Grid.Data;
            g.Sky.Clear();
            g.Ground.Clear();
            g.Origin = origin;
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    var xz = origin + new float2(x, z) * ComfortGrid.Spacing;
                    var climate = HeatAt(terrain, xz);
                    var sky = float4.zero;
                    var sun = 1f;
                    if (ready)
                    {
                        var v = field.Sample(xz);
                        var rain = field.RainFraction(xz) * (1f - v.AlwaysSnow);
                        sky = new float4(v.Precipitation * rain, v.Precipitation * (1f - rain), math.saturate(v.Wind),
                                         math.saturate(math.max(v.Lightning / 3f, v.Hail)));
                        sun = math.saturate(v.Sunlight * (1f - v.Overcast * 0.6f));
                    }
                    g.Sky.Add(sky);
                    g.Ground.Add(new float4(math.saturate(climate.x + HeatShift.Data), climate.y, sun, 0f));
                }
            var wind = weather != null ? (Vector2)weather.WindDirection : Vector2.right;
            g.WindDirection = new float2(wind.x, wind.y);
            g.Ready = 1;
        }

        const int Size = ComfortGrid.Size;

        /// <summary>
        /// Heat and humidity at a point: the climate field's, pulled halfway toward the middle of the ground's biome's
        /// own ranges where those are narrow (a glacier is cold wherever the noise put it), and warmed by volcanism.
        /// </summary>
        static float2 HeatAt(in TerrainSource terrain, float2 xz)
        {
            var climate = BiomeSampling.Climate(terrain.BiomeNoise, xz);
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

        /// <summary>The grid's weather at a point (the nearest cell; the edge cell beyond it). Clear and mild before the
        /// first sample.</summary>
        public static ComfortWeather WeatherAt(float2 xz)
        {
            ref var g = ref Grid.Data;
            if (g.Ready == 0 || g.Sky.Length < Size * Size)
                return new ComfortWeather { Heat = 0.45f, Humidity = 0.5f, Sunlight = 1f };
            var c = math.clamp((int2)math.round((xz - g.Origin) / ComfortGrid.Spacing), 0, Size - 1);
            var i = c.y * Size + c.x;
            var sky = g.Sky[i];
            var ground = g.Ground[i];
            return new ComfortWeather { Rain = sky.x, Snow = sky.y, Wind = sky.z, Storm = sky.w, Heat = ground.x, Humidity = ground.y, Sunlight = ground.z };
        }

        /// <summary>
        /// The heat a creature feels: the climate's, warmer by day and colder by night (more so where it's dry, as in a
        /// desert), warmer in the sun, cooler in rain and wind. In a cave or under rock it's mild whatever the weather.
        /// </summary>
        public static float FeltHeat(in ComfortWeather w, float night, bool underground)
        {
            var day = 1f - night;
            var heat = w.Heat + (day - 0.5f) * 0.2f * (1.2f - w.Humidity) + w.Sunlight * 0.06f * day
                     - (w.Rain + w.Snow) * 0.05f - w.Wind * 0.04f;
            return underground ? math.lerp(heat, 0.45f, 0.8f) : heat;
        }

        /// <summary>
        /// How uncomfortable one individual is right now, 0..1, and the worst reason. Each individual's tolerance and
        /// heat range are its own (seeded), so some are hardier or fussier than others. Cover (row 2) is the shelter it
        /// stands in: a roof or a cave takes off the whole sky (and a cave's air is mild); a tree most of the rain and
        /// sun and half the wind; shade the sun; water the dry heat and much of the heat; a fire warms it.
        /// </summary>
        public static float Pressure(in ComfortProfile p, uint individual, in ComfortWeather w, float night, bool underground,
                                     ShelterNeed cover, out ComfortKind kind)
        {
            kind = ComfortKind.None;
            if (!p.Minds || Off.Data != 0)
                return 0f;
            var tol = math.max(p.Tolerance * (1f + 0.4f * CreatureBlob.Trait(individual, 7101)), 0f);
            var shift = 0.05f * CreatureBlob.Trait(individual, 7102);
            var day = 1f - night;
            var roofed = underground || (cover & (ShelterNeed.Roof | ShelterNeed.Cave)) != 0;
            var tree = !roofed && (cover & ShelterNeed.Tree) != 0;
            var shaded = roofed || (cover & (ShelterNeed.Tree | ShelterNeed.Shade)) != 0;
            var heat = FeltHeat(w, night, underground || (cover & ShelterNeed.Cave) != 0) - (shaded ? w.Sunlight * 0.06f * day : 0f);
            if (!roofed && (cover & ShelterNeed.Shade) != 0)
                heat -= 0.04f;
            var watered = (cover & ShelterNeed.Water) != 0;
            if (watered)
                heat = math.min(heat - 0.12f, math.lerp(heat, 0.45f, 0.6f)); // (a dip, or a well's wet stones, cools a lot)
            if ((cover & ShelterNeed.Fire) != 0)
                heat = math.max(heat, math.min(heat + 0.35f, 0.6f));
            // How much of each kind of sky still reaches it.
            var wetness = roofed ? 0f : tree ? 0.35f : 1f;
            var storminess = roofed ? 0f : tree ? 0.6f : 1f;
            var glare = roofed ? 0f : shaded ? 0.2f : 1f;
            var windiness = roofed ? 0f : tree ? 0.5f : 1f;
            var best = 0f;
            static void Consider(float v, ComfortKind k, ref float best, ref ComfortKind kind)
            {
                if (v > best)
                {
                    best = v;
                    kind = k;
                }
            }
            // (Dry first: on a tie with Hot, a fish folk's complaint is the dryness.)
            // (Rain on it is no dry heat, whatever the air's humidity; nor is a dip in the water.)
            if (!watered)
                Consider(math.saturate(p.Dry * math.saturate((heat - 0.55f) / 0.3f) * (1f - w.Humidity) * (1f - w.Rain * wetness) * 1.5f - tol),
                         ComfortKind.Dry, ref best, ref kind);
            Consider(math.saturate((heat - (p.Heat.y + shift) - tol) / 0.45f), ComfortKind.Hot, ref best, ref kind);
            Consider(math.saturate(((p.Heat.x + shift) - tol - heat) / 0.45f), ComfortKind.Cold, ref best, ref kind);
            Consider(math.saturate(p.Rain * w.Rain * wetness - tol), ComfortKind.Rain, ref best, ref kind);
            Consider(math.saturate(p.Snow * w.Snow * wetness - tol), ComfortKind.Snow, ref best, ref kind);
            Consider(math.saturate(p.Storm * w.Storm * storminess - tol), ComfortKind.Storm, ref best, ref kind);
            Consider(math.saturate(p.Bright * day * w.Sunlight * glare - tol), ComfortKind.Bright, ref best, ref kind);
            Consider(math.saturate(p.Wind * math.saturate((w.Wind - 0.35f) / 0.65f) * windiness - tol), ComfortKind.Wind, ref best, ref kind);
            return best;
        }

        /// <summary>The kinds of shelter that help with a complaint (A-71 row 2), before the creature's own tastes.</summary>
        public static ShelterNeed Helps(ComfortKind kind) => kind switch
        {
            ComfortKind.Hot => ShelterNeed.Shade | ShelterNeed.Tree | ShelterNeed.Roof | ShelterNeed.Cave | ShelterNeed.Water,
            ComfortKind.Dry => ShelterNeed.Water | ShelterNeed.Shade | ShelterNeed.Tree | ShelterNeed.Roof,
            ComfortKind.Cold => ShelterNeed.Fire | ShelterNeed.Roof | ShelterNeed.Cave,
            ComfortKind.Bright => ShelterNeed.Shade | ShelterNeed.Tree | ShelterNeed.Cave | ShelterNeed.Roof,
            ComfortKind.None => ShelterNeed.None,
            _ => ShelterNeed.Roof | ShelterNeed.Cave | ShelterNeed.Tree   // rain, snow, storm, wind
        };
    }

    /// <summary>
    /// Builds each species' ComfortProfile (A-71 row 1): defaults read from its traits (cold-blooded, cave dweller,
    /// shuns light, near water, flies, swims, size and the climate of the biomes it lives in), then the folk races' own
    /// tastes from the card (bat folk hate the sun, fish folk dry heat, humans rain and cold, ogres cold...).
    /// </summary>
    public static class ComfortProfiles
    {
        /// <param name="home">The middle of the temperature and humidity of the biomes it lives in (x below 0: unknown).</param>
        public static ComfortProfile For(CreatureSpecies variant, Vector2 home)
        {
            var s = variant.Body;
            var p = new ComfortProfile
            {
                Heat = new float2(0.25f, 0.7f), Rain = 0.25f, Snow = 0.25f, Storm = 0.5f, Bright = 0f, Wind = 0.15f, Dry = 0f,
                Tolerance = 0.1f, Seeks = ShelterNeed.Tree | ShelterNeed.Shade, Minds = true
            };
            // Its home's climate moves its heat range: desert animals like it hot, snow animals cold.
            if (home.x >= 0f)
            {
                var centre = math.lerp(0.47f, home.x, 0.7f);
                p.Heat = new float2(centre - 0.24f, centre + 0.24f);
                p.Dry = home.y < 0.35f ? 0f : p.Dry;
            }
            if (s.ColdBlooded)
                p.Heat += new float2(0.12f, 0.06f);
            if (s.CaveDweller || s.ShunsLight)
            {
                p.Bright = 0.9f;
                p.Seeks |= ShelterNeed.Cave | ShelterNeed.Shade;
            }
            if (s.NearWater || s.Wades > 0f)
            {
                p.Dry = math.max(p.Dry, 0.5f);
                p.Rain = 0f;
                p.Seeks |= ShelterNeed.Water;
            }
            // Big furry beasts pant in the heat; small ones mind the rain more.
            if (!s.ColdBlooded && s.Weight > 150f)
                p.Heat.y -= 0.06f;
            if (s.Weight < 2f && !s.ColdBlooded)
                p.Rain += 0.15f;
            // Birds and fliers roost out of wind and rain.
            if (s.FlightHeight.y > 0f || s.Roosts)
            {
                p.Wind = 0.6f;
                p.Rain = math.max(p.Rain, 0.5f);
                p.Seeks |= ShelterNeed.Tree;
            }
            var name = variant.name;
            var body = s.name;
            bool Is(string n) => name == n || body == n;
            if (Is("Cat"))
                p.Rain = 0.9f;
            if (Is("Frog") || Is("ToxicFrog") || Is("SporeToad"))
            {
                p.Rain = -0.5f;
                p.Dry = 0.7f;
            }
            // Folk races.
            // (Hermit first: its body is the Human's.)
            if (Is("Hermit"))
                Folk(ref p, ComfortRace.Hermit, new float2(0.2f, 0.78f), rain: 0.3f, snow: 0.3f, storm: 0.5f, bright: 0f, wind: 0.15f, dry: 0.1f,
                     ShelterNeed.Roof | ShelterNeed.Tree | ShelterNeed.Fire);
            else if (Is("Human") || Is("HumanChild"))
                Folk(ref p, ComfortRace.Human, new float2(0.28f, 0.72f), rain: 0.55f, snow: 0.5f, storm: 0.8f, bright: 0f, wind: 0.25f, dry: 0.1f,
                     ShelterNeed.Roof | ShelterNeed.Tree | ShelterNeed.Fire);
            else if (Is("Elf"))
                Folk(ref p, ComfortRace.Elf, new float2(0.3f, 0.68f), rain: 0.25f, snow: 0.4f, storm: 0.6f, bright: 0f, wind: 0.2f, dry: 0.35f,
                     ShelterNeed.Tree | ShelterNeed.Roof | ShelterNeed.Shade);
            else if (Is("Dwarf"))
                Folk(ref p, ComfortRace.Dwarf, new float2(0.2f, 0.62f), rain: 0.35f, snow: 0.2f, storm: 0.5f, bright: 0.3f, wind: 0.2f, dry: 0.1f,
                     ShelterNeed.Roof | ShelterNeed.Cave | ShelterNeed.Fire);
            else if (Is("Goblin"))
                Folk(ref p, ComfortRace.Goblin, new float2(0.3f, 0.78f), rain: 0.45f, snow: 0.6f, storm: 0.7f, bright: 0.35f, wind: 0.3f, dry: 0f,
                     ShelterNeed.Roof | ShelterNeed.Cave | ShelterNeed.Shade);
            else if (Is("Ogre"))
                Folk(ref p, ComfortRace.Ogre, new float2(0.42f, 0.9f), rain: 0.3f, snow: 0.7f, storm: 0.4f, bright: 0f, wind: 0.35f, dry: 0f,
                     ShelterNeed.Fire | ShelterNeed.Roof);
            else if (Is("BatFolk"))
                Folk(ref p, ComfortRace.BatFolk, new float2(0.2f, 0.7f), rain: 0.3f, snow: 0.4f, storm: 0.5f, bright: 1f, wind: 0.2f, dry: 0.2f,
                     ShelterNeed.Shade | ShelterNeed.Cave | ShelterNeed.Tree | ShelterNeed.Roof);
            else if (Is("FishFolk"))
                Folk(ref p, ComfortRace.FishFolk, new float2(0.15f, 0.7f), rain: -0.5f, snow: 0.3f, storm: 0.3f, bright: 0.2f, wind: 0.15f, dry: 1f,
                     ShelterNeed.Water | ShelterNeed.Shade);
            else if (Is("CavePygmy"))
                Folk(ref p, ComfortRace.Pygmy, new float2(0.3f, 0.7f), rain: 0.35f, snow: 0.5f, storm: 1f, bright: 0.4f, wind: 0.3f, dry: 0.1f,
                     ShelterNeed.Cave | ShelterNeed.Roof | ShelterNeed.Tree);
            else if (Is("Ghost"))
                p = new ComfortProfile { Race = ComfortRace.Ghost };
            // Nothing that swims, never moves or is a ghost minds the weather.
            if (s.Habitat == Habitat.Swims || s.Rooted || s.Disguised || s.Haunts || s.Tunnels || s.LavaDweller)
                p.Minds = false;
            return p;
        }

        static void Folk(ref ComfortProfile p, ComfortRace race, float2 heat, float rain, float snow, float storm, float bright, float wind,
                         float dry, ShelterNeed seeks)
        {
            p = new ComfortProfile
            {
                Race = race, Heat = heat, Rain = rain, Snow = snow, Storm = storm, Bright = bright, Wind = wind, Dry = dry,
                Tolerance = 0.08f, Seeks = seeks, Minds = true
            };
        }
    }
}
