using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>
    /// Leaves fall from tall broadleaf crowns, needles from tall conifers, flakes from giant mushroom caps, and wisps rise from vents. Every pick hashes a tree or a world cell, so the
    /// same spots show on every visit without placing anything in the world.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        const float TallTree = 12f;
        const float LeafTreeShare = 0.55f;
        const int LeafParticles = 18;
        const float LeafSize = 0.13f;
        static readonly Color[] Autumn = { new Color(0.86f, 0.66f, 0.18f), new Color(0.8f, 0.42f, 0.12f), new Color(0.62f, 0.7f, 0.2f) };
        static readonly Color[] DeadNeedles = { new Color(0.78f, 0.5f, 0.2f), new Color(0.88f, 0.64f, 0.3f), new Color(0.7f, 0.46f, 0.22f) };
        static readonly Color[] CapFlakes = { new Color(0.92f, 0.9f, 0.82f), new Color(0.85f, 0.8f, 0.7f) };
        const float GiantCap = 8f;

        struct FallSpec
        {
            public Color A, B;
            public Color[] Tint;
            public float Size, Width, Glow, Share, Spread, Below;
        }
        const float VentCell = 12f;
        const float VentShare = 0.07f;
        const float GeoVentShare = 0.35f;
        const float VentRise = 3f;
        const float VentMaxRange = 140f;

        public static int LastLeafTrees, LastVents;

        readonly object ventOwner = new object();
        readonly HashSet<Punchy.Flora.FloraSpecies> tallSpecies = new HashSet<Punchy.Flora.FloraSpecies>();
        readonly Dictionary<Punchy.Flora.FloraSpecies, FallSpec> fallOf = new Dictionary<Punchy.Flora.FloraSpecies, FallSpec>();
        readonly HashSet<Punchy.Flora.FloraSpecies> oneSpecies = new HashSet<Punchy.Flora.FloraSpecies>();
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> leafTrees = new List<(float3, float3, float, float, int)>();
        readonly List<Kit.Plume> leaves = new List<Kit.Plume>(), ventSmoke = new List<Kit.Plume>(), ventSteam = new List<Kit.Plume>();
        readonly HashSet<int> ventLandmarkTypes = new HashSet<int>();
        BiomePalette ambientFrom;
        bool[] ventBiome;
        float3 leavesAt = new float3(float.MaxValue);
        float nextLeaves;
        int2 ventCellAt = new int2(int.MinValue);
        float ventRangeAt;

        void AmbientBits(in TerrainSource source, BiomePalette palette, Vector3 eye)
        {
            if (ambientFrom != palette)
            {
                ambientFrom = palette;
                AmbientTables(palette);
                SeepTables(palette);
                CeilingTables(palette);
                leavesAt = new float3(float.MaxValue);
                ventCellAt = new int2(int.MinValue);
            }
            FallingLeaves(eye);
            Vents(source, palette, eye);
            Seeps(source, eye);
            CeilingFalls(source, eye);
            RiverSpray(source, eye);
            RiverMist(source, eye);
            RiverLeaves(source, palette, eye);
        }

        void AmbientTables(BiomePalette palette)
        {
            tallSpecies.Clear();
            fallOf.Clear();
            ventBiome = new bool[palette.Biomes.Length];
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                var biome = palette.Biomes[b];
                if (biome == null)
                    continue;
                ventBiome[b] = biome.HasTag(BiomeTags.Vents);
                foreach (var f in biome.Flora)
                {
                    var s = f.Species;
                    if (s != null && FallOf(s) is { } spec)
                    {
                        tallSpecies.Add(s);
                        fallOf[s] = spec;
                    }
                }
            }
            ventLandmarkTypes.Clear();
            for (int t = 0; t < palette.Landmarks.Length; t++)
            {
                var type = palette.Landmarks[t];
                if (type == null || FallType(type))
                    continue;
                foreach (var effect in type.Effects)
                    if (effect.Kind == LandmarkEffectKind.Plume && Kit.FamilyOf(effect.Color, effect.Glow) is var family && (family == Kit.Steam || family == Kit.Smoke))
                    {
                        ventLandmarkTypes.Add(t);
                        break;
                    }
            }
        }

        static FallSpec? FallOf(Punchy.Flora.FloraSpecies s)
        {
            var spec = new FallSpec { A = s.Foliage, B = s.FoliageVariation, Tint = Autumn, Size = LeafSize, Share = LeafTreeShare, Spread = 0.8f, Below = 0.35f };
            switch (s.Form)
            {
                case Punchy.Flora.FloraForm.BroadleafTree:
                case Punchy.Flora.FloraForm.HiveTree:
                    return s.Height.y >= TallTree ? spec : null;
                case Punchy.Flora.FloraForm.Conifer:
                    if (s.Height.y < TallTree)
                        return null;
                    spec.Tint = DeadNeedles;
                    spec.Size = 0.16f;
                    spec.Width = 0.3f;
                    spec.Share = 0.4f;
                    // A cone's tiers widen downward and hide needles shed from inside its crown.
                    spec.Spread = 1.3f;
                    return spec;
                case Punchy.Flora.FloraForm.Mushroom:
                    if (s.Height.y < GiantCap)
                        return null;
                    spec.Tint = CapFlakes;
                    spec.Size = 0.07f;
                    spec.Width = 0.9f;
                    spec.Share = 0.8f;
                    spec.Spread = 0.6f;
                    spec.Below = 0.12f;
                    spec.Glow = s.Finish == Punchy.Flora.FoliageFinish.Glowing ? 1.5f : 0f;
                    return spec;
            }
            return null;
        }

        void FallingLeaves(Vector3 eye)
        {
            if (tallSpecies.Count == 0)
                return;
            if (flora == null)
                flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
            if (flora == null)
                return;
            if (Time.unscaledTime < nextLeaves && math.distancesq(leavesAt, (float3)eye) < 36f)
                return;
            nextLeaves = Time.unscaledTime + 3f;
            leavesAt = eye;
            var gone = Kit.Leaves.Effect != null ? Kit.Leaves.Effect.GonePixels : 2f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(LeafSize, gone), 80f) + 8f;
            leaves.Clear();
            var count = 0;
            foreach (var species in tallSpecies)
            {
                oneSpecies.Clear();
                oneSpecies.Add(species);
                leafTrees.Clear();
                flora.TreesNear(((float3)eye).xz, range, oneSpecies, leafTrees);
                var spec = fallOf[species];
                foreach (var t in leafTrees)
                {
                    var h = Scramble((uint)t.seed ^ 0x1EAF5u);
                    if ((h & 0xFFFF) / 65536f >= spec.Share || t.height < species.Height.y * 0.5f)
                        continue;
                    var r = Scramble(h);
                    var crown = math.max(t.crown * spec.Spread, 1.5f);
                    var below = t.height * spec.Below;
                    var green = Color.Lerp(spec.A, spec.B, (r & 0xFF) / 255f);
                    // Leaves in the canopy's own green vanish against it, so each tree sheds a warmer tint.
                    var col = Color.Lerp(green, spec.Tint[(h >> 22) % (uint)spec.Tint.Length], 0.45f + 0.4f * ((r >> 20) & 0xF) / 15f).linear;
                    count++;
                    leaves.Add(new Kit.Plume
                    {
                        Position = t.top - new float3(0f, below, 0f),
                        Seed = ((r >> 8) & 0xFFFF) / 65536f,
                        Color = new float4(col.r, col.g, col.b, spec.Glow),
                        Shape = new float4(below * 0.6f, crown, spec.Size * (0.85f + 0.3f * ((r >> 24) & 0xF) / 15f), 8f + 4f * ((r >> 28) & 0xF) / 15f),
                        Timing = new float4(0f, 0f, 0.95f, LeafParticles - (int)((h >> 16) % 6u)),
                        Drop = t.height - below + 0.3f,
                        Fall = new float4(spec.Width, 0f, 0f, 0f)
                    });
                }
            }
            LastLeafTrees = count;
            Kit.Leaves.Set(this, leaves);
        }

        void Vents(in TerrainSource source, BiomePalette palette, Vector3 eye)
        {
            var gone = Kit.Smoke.Effect != null ? Kit.Smoke.Effect.GonePixels : 16.8f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(VentRise, gone), VentMaxRange);
            var cell = (int2)math.floor(((float3)eye).xz / VentCell);
            if (math.all(cell == ventCellAt) && math.abs(range - ventRangeAt) < VentCell)
                return;
            ventCellAt = cell;
            ventRangeAt = range;
            ventSmoke.Clear();
            ventSteam.Clear();
            var n = (int)math.ceil(range / VentCell);
            var landmarks = source.Landmarks.IsCreated;
            for (int z = -n; z <= n; z++)
            for (int x = -n; x <= n; x++)
            {
                var c = cell + new int2(x, z);
                var h = Scramble(math.hash(new int3(c, 0x7E47)));
                var roll = (h & 0xFFFF) / 65536f;
                if (roll >= GeoVentShare)
                    continue;
                var r = Scramble(h ^ 0x9E3779B9u);
                var xz = ((float2)c + new float2((r & 0xFFFF) / 65536f, (r >> 16) / 65536f)) * VentCell;
                if (math.distancesq(xz, ((float3)eye).xz) > range * range)
                    continue;
                var geo = landmarks && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out var hit) && hit.Distance < 0.9f && ventLandmarkTypes.Contains(hit.Type);
                if (!geo && (roll >= VentShare || !VentBiome(source.SampleGround(xz).Biomes)))
                    continue;
                var ground = source.SampleGround(xz);
                if (ground.HasWater && ground.WaterSurface > ground.Height - 0.05f)
                    continue;
                var s = Scramble(r);
                var a = (s & 0xFF) / 255f;
                var b = ((s >> 8) & 0xFF) / 255f;
                var intermittent = ((s >> 16) & 3u) == 0u;
                var plume = new Kit.Plume
                {
                    Position = new float3(xz.x, ground.Height - 0.1f, xz.y),
                    Seed = ((s >> 18) & 0x3FFF) / 16384f,
                    Shape = new float4(VentRise * (0.6f + 0.8f * a), 0.25f + 0.35f * b, 0.45f + 0.4f * a, 4f + 3f * b),
                    Timing = new float4(intermittent ? 9f + 8f * b : 0f, 0.45f, geo ? 0.4f : 0.45f, 7 + (int)((s >> 24) % 4u))
                };
                if (geo)
                {
                    plume.Color = new float4(0.86f, 0.87f, 0.88f, 0f);
                    ventSteam.Add(plume);
                }
                else
                {
                    var grey = 0.5f + 0.14f * b;
                    plume.Color = new float4(grey, grey * 0.97f, grey * 0.93f, 0f);
                    ventSmoke.Add(plume);
                }
            }
            LastVents = ventSmoke.Count + ventSteam.Count;
            Kit.Smoke.Set(ventOwner, ventSmoke);
            Kit.Steam.Set(ventOwner, ventSteam);
        }

        bool VentBiome(in BiomeWeights biomes)
        {
            var w = 0f;
            for (int k = 0; k < 4; k++)
                if (biomes.Weights[k] > 0f && biomes.Ids[k] >= 0 && biomes.Ids[k] < ventBiome.Length && ventBiome[biomes.Ids[k]])
                    w += biomes.Weights[k];
            return w > 0.6f;
        }

        void ClearAmbientBits()
        {
            Kit.Leaves.Set(this, null);
            Kit.Smoke.Set(ventOwner, null);
            Kit.Steam.Set(ventOwner, null);
            ClearRiverSpray();
            ClearRiverMist();
            ClearRiverLeaves();
            ClearSeeps();
            ClearCeilingFalls();
            leavesAt = new float3(float.MaxValue);
            ventCellAt = new int2(int.MinValue);
        }
    }
}
