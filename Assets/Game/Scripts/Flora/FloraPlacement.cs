using System;
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Punchy.Flora
{
    /// <summary>A species' placement rules, flattened for Burst.</summary>
    public struct FloraRules
    {
        public int FirstModel, Variants;
        public float MaxSlope;
        public float2 Elevation;
        public float GrassAffinity;
        public int ToleratesSnow;
        public float PatchFrequency, PatchCoverage;
        public float2 PatchOffset;
        public float NearWater;
        public int OnlyNearWater;
        public float2 SizeRange;
        public int Aquatic, Floats;
        public float2 WaterDepth;
        public float MinRapids;
        /// <summary>How far it lies over onto the slope it stands on (rocks 0.85; upright plants 0).</summary>
        public float Tilt;
        /// <summary>1 for built things (FloraGenerator.Founded): their bases take a band of the ground's colour.</summary>
        public int Dirt;
        /// <summary>1 for hive trees: placed in a landmark, they face its way, so their comb is on a known side.</summary>
        public int FaceLandmark;
    }

    public struct FloraBiomeEntry
    {
        public int Species;
        public float Density;
    }

    /// <summary>A landmark prop, flattened for Burst.</summary>
    public struct FloraPropEntry
    {
        public int Species;
        public float Density;
        public float2 Ring, Scale;
        public int Centrepiece, FaceCentre, Count;
        public float Angle, Clearance;
    }

    public enum TreeKind : byte { None, Broadleaf, Conifer, Palm }

    /// <summary>What a plant variant is like to climb or perch in (unscaled metres).</summary>
    public struct FloraShape
    {
        public TreeKind Kind;
        public float Height, TrunkRadius;
        /// <summary>Where the crown starts (the first branches), and how far it spreads from the trunk.</summary>
        public float CrownBase, CrownRadius;
        /// <summary>A building's walls, for creatures to walk round: its cylinders in FloraBlob.Obstacles.</summary>
        public int FirstObstacle, ObstacleCount;
    }

    /// <summary>
    /// Everything that decides where plants grow, for any world and any job: the per-species rules, each biome's and
    /// landmark's lists, and the grids' cell sizes. Placement is a pure function of a cell, so FloraSystem (drawing)
    /// and the animals (climbing, perching, spinning webs) agree on where every tree is without sharing anything else.
    /// </summary>
    public struct FloraBlob
    {
        public BlobArray<FloraRules> Rules;
        public BlobArray<FloraBiomeEntry> Entries;
        /// <summary>Per biome × layer: (first entry, count).</summary>
        public BlobArray<int2> Ranges;
        /// <summary>Per biome, its realm (BiomePalette.BiomeRealms), or -1 (no realms, or the ocean).</summary>
        public BlobArray<int> BiomeRealm;
        public BlobArray<FloraPropEntry> Props;
        /// <summary>Per landmark type × layer.</summary>
        public BlobArray<int2> PropRanges;
        /// <summary>Per settlement building (SettlementBaking.Buildings order): species index and its layer (-1 none).</summary>
        public BlobArray<int2> SettlementSpecies;
        /// <summary>
        /// Per species: the light a glowing plant gives its surroundings (rgb, linear) and how far it reaches (w, m);
        /// all zero for plants that don't glow. See <see cref="FloraPlacement.GlowLight"/>.
        /// </summary>
        public BlobArray<float4> Glow;
        public int BiomeCount, LandmarkTypeCount;
        /// <summary>Every model (species variants, in FirstModel order).</summary>
        public BlobArray<FloraShape> Models;
        /// <summary>
        /// Buildings' collision cylinders (x, z offset from the base, radius, top), unturned and unscaled, as the player
        /// collides with them (FloraModel.Colliders). Only built for the animals' tables (see Build).
        /// </summary>
        public BlobArray<float4> Obstacles;
        /// <summary>Grid spacing of the ground, shrub and tree layers.</summary>
        public float3 CellSizes;
    }

    /// <summary>The placement tables, for the animals' world (set up by FaunaDirector).</summary>
    public struct FloraLookup : IComponentData
    {
        public BlobAssetReference<FloraBlob> Blob;
    }

    /// <summary>A tree an animal can use.</summary>
    public struct TreeSpot
    {
        public float3 Base;
        public float Height, TrunkRadius, CrownBase, CrownRadius;
        public TreeKind Kind;
    }

    public static class FloraPlacement
    {
        public const int TreeLayer = 2;
        static int Gcd(int a, int b)
        {
            while (b != 0)
                (a, b) = (b, a % b);
            return a;
        }

        /// <summary>The most tree cells one FindTree looks at.</summary>
        const int MaxTreeCells = 24;

        /// <summary>
        /// Builds the tables from a palette. <paramref name="species"/> receives the species in model order (sorted by
        /// layer, then name): species i's variants are models Rules[i].FirstModel onwards. With
        /// <paramref name="obstacles"/>, buildings' walls too (each building variant is grown once for its colliders,
        /// so only the animals' tables pay for it).
        /// </summary>
        public static BlobAssetReference<FloraBlob> Build(BiomePalette palette, float3 cellSizes, List<FloraSpecies> species, bool obstacles = false)
        {
            var found = new HashSet<FloraSpecies>();
            foreach (var biome in palette.Biomes)
                if (biome != null)
                    foreach (var f in biome.Flora)
                        if (f.Species != null)
                            found.Add(f.Species);
            var landmarks = palette.Landmarks ?? Array.Empty<LandmarkType>();
            foreach (var landmark in landmarks)
                if (landmark != null)
                    foreach (var p in landmark.Props)
                        if (p.Species != null)
                            found.Add(p.Species);
            var buildings = SettlementBaking.Buildings(landmarks);
            foreach (var b in buildings)
                if (b.Species != null)
                    found.Add(b.Species);
            species.Clear();
            species.AddRange(found);
            species.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : string.CompareOrdinal(a.name, b.name));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<FloraBlob>();
            root.CellSizes = cellSizes;

            var rules = builder.Allocate(ref root.Rules, species.Count);
            var shapes = new List<FloraShape>();
            var walls = new List<float4>();
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                var h = math.hash(new int2(s.Seed, i));
                rules[i] = new FloraRules
                {
                    FirstModel = shapes.Count,
                    Variants = s.Variants,
                    MaxSlope = s.MaxSlope,
                    Elevation = s.Elevation,
                    GrassAffinity = s.GrassAffinity,
                    ToleratesSnow = s.ToleratesSnow ? 1 : 0,
                    PatchFrequency = 1f / math.max(s.PatchSize, 1f),
                    PatchCoverage = s.PatchCoverage,
                    PatchOffset = new float2(h & 0xFFFF, h >> 16) / 65535f * 300f,
                    NearWater = s.NearWater,
                    OnlyNearWater = s.OnlyNearWater ? 1 : 0,
                    SizeRange = s.SizeRange,
                    Aquatic = s.Aquatic ? 1 : 0,
                    Floats = s.Floats ? 1 : 0,
                    WaterDepth = s.WaterDepth,
                    MinRapids = s.MinRapids,
                    Tilt = s.Form == FloraForm.Boulder ? 0.85f : 0f,
                    Dirt = FloraGenerator.Founded(s.Form) ? 1 : 0,
                    FaceLandmark = s.Form == FloraForm.HiveTree ? 1 : 0
                };
                for (int v = 0; v < s.Variants; v++)
                {
                    var shape = FloraGenerator.Shape(s, v);
                    if (obstacles && IsBuilding(s))
                    {
                        var model = FloraGenerator.Grow(s, v);
                        shape.FirstObstacle = walls.Count;
                        foreach (var c in model.Colliders)
                            walls.Add(new float4(c.x, c.y, c.z, c.w));
                        if (model.Colliders.Count == 0 && model.TrunkRadius > 0f)
                            walls.Add(new float4(0f, 0f, model.TrunkRadius, model.Height));
                        shape.ObstacleCount = walls.Count - shape.FirstObstacle;
                        model.Destroy();
                    }
                    else if (obstacles && IsAnchorRock(s))
                    {
                        // (Its size, for spiders to tie webs to: a rock's shape is otherwise left empty.)
                        var model = FloraGenerator.Grow(s, v);
                        shape.Height = model.Height;
                        shape.TrunkRadius = model.TrunkRadius;
                        model.Destroy();
                    }
                    shapes.Add(shape);
                }
            }
            var obstacleArray = builder.Allocate(ref root.Obstacles, walls.Count);
            for (int i = 0; i < walls.Count; i++)
                obstacleArray[i] = walls[i];
            var glow = builder.Allocate(ref root.Glow, species.Count);
            for (int i = 0; i < species.Count; i++)
                glow[i] = GlowLight(species[i]);
            var models = builder.Allocate(ref root.Models, shapes.Count);
            for (int m = 0; m < shapes.Count; m++)
                models[m] = shapes[m];

            root.BiomeCount = palette.Biomes.Length;
            var list = new List<FloraBiomeEntry>();
            var ranges = builder.Allocate(ref root.Ranges, root.BiomeCount * 3);
            for (int b = 0; b < root.BiomeCount; b++)
            for (int layer = 0; layer < 3; layer++)
            {
                var start = list.Count;
                if (palette.Biomes[b] != null)
                    foreach (var f in palette.Biomes[b].Flora)
                        if (f.Species != null && (int)f.Species.Layer == layer && f.Density > 0f)
                            list.Add(new FloraBiomeEntry { Species = species.IndexOf(f.Species), Density = f.Density });
                ranges[b * 3 + layer] = new int2(start, list.Count - start);
            }
            var entries = builder.Allocate(ref root.Entries, list.Count);
            for (int i = 0; i < list.Count; i++)
                entries[i] = list[i];
            var biomeRealm = builder.Allocate(ref root.BiomeRealm, root.BiomeCount);
            for (int b = 0; b < root.BiomeCount; b++)
                biomeRealm[b] = palette.BiomeRealms != null && b < palette.BiomeRealms.Length ? palette.BiomeRealms[b] : -1;

            root.LandmarkTypeCount = landmarks.Length;
            var propList = new List<FloraPropEntry>();
            var propRanges = builder.Allocate(ref root.PropRanges, math.max(1, landmarks.Length * 3));
            for (int t = 0; t < landmarks.Length; t++)
            for (int layer = 0; layer < 3; layer++)
            {
                var start = propList.Count;
                if (landmarks[t] != null)
                    foreach (var p in landmarks[t].Props)
                        if (p.Species != null && (int)p.Species.Layer == layer && (p.Density > 0f || p.Centrepiece || p.Count > 0))
                            propList.Add(new FloraPropEntry
                            {
                                Species = species.IndexOf(p.Species),
                                Density = p.Density,
                                Ring = p.Ring,
                                Scale = p.Scale,
                                Centrepiece = p.Centrepiece ? 1 : 0,
                                FaceCentre = p.FaceCentre ? 1 : 0,
                                Count = p.Count,
                                Angle = p.Angle,
                                Clearance = p.Clearance
                            });
                propRanges[t * 3 + layer] = new int2(start, propList.Count - start);
            }
            var props = builder.Allocate(ref root.Props, propList.Count);
            for (int i = 0; i < propList.Count; i++)
                props[i] = propList[i];
            var settlementSpecies = builder.Allocate(ref root.SettlementSpecies, buildings.Count);
            for (int i = 0; i < buildings.Count; i++)
                settlementSpecies[i] = buildings[i].Species != null
                    ? new int2(species.IndexOf(buildings[i].Species), (int)buildings[i].Species.Layer)
                    : new int2(-1, -1);

            var blob = builder.CreateBlobAssetReference<FloraBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        /// <summary>
        /// For built things (FloraRules.Dirt), the ground's colour round the footprint: the middle and four points
        /// <paramref name="reach"/> out, averaged; grass turns it towards soil and snow pale grey. Packed RGBA8, the
        /// alpha carrying how high the dirt reaches (<paramref name="band"/> m at scale 1, in centimetres, at least 1:
        /// a house's walls take more than a bench's feet); 0 for anything else.
        /// </summary>
        static uint DirtColor(in TerrainSource source, in FloraRules rule, float2 at, float reach, float band)
        {
            if (rule.Dirt == 0)
                return 0u;
            var sum = float3.zero;
            for (int k = 0; k < 5; k++)
            {
                var p = at + (k == 0 ? float2.zero : new float2(k == 1 ? 1f : k == 2 ? -1f : 0f, k == 3 ? 1f : k == 4 ? -1f : 0f) * reach);
                var ground = source.SampleGround(p);
                var surface = source.SampleSurface(p, ground, source.SampleNormal(p, 1f));
                // Under grass it's the soil that splashes up, not the green; under snow, pale grey.
                var soil = math.lerp(surface.Albedo, new float3(0.2f, 0.14f, 0.09f), math.saturate(surface.Grass) * 0.8f);
                sum += math.lerp(soil, new float3(0.62f, 0.64f, 0.68f), math.saturate(surface.Snow));
            }
            var c = (uint3)math.round(math.saturate(sum / 5f) * 255f);
            return c.x | (c.y << 8) | (c.z << 16) | ((uint)math.clamp(math.round(band * 100f), 1f, 255f) << 24);
        }

        /// <summary>The yaw that turns a model's front (+Z) to face a direction (yaw maps +Z to (-sin, cos)).</summary>
        static float FacingYaw(float2 facing) => math.atan2(-facing.x, facing.y);

        /// <summary>
        /// The floor a plot's building stands at: the average ground over its footprint (the middle and four points
        /// towards its corners), raised to the ground at its door if that's higher so the door is never buried. Its
        /// foundation skirt (FloraGenerator.FoundationDepth) reaches down past the lower ground.
        /// </summary>
        public static float FloorHeight(in TerrainSource source, in SettlementPlot plot)
        {
            var across = new float2(-plot.Facing.y, plot.Facing.x) * plot.Half.x * 0.7f;
            var along = plot.Facing * plot.Half.y * 0.7f;
            var sum = source.SampleHeight(plot.Centre);
            for (int k = 0; k < 4; k++)
                sum += source.SampleHeight(plot.Centre + across * (k < 2 ? 1f : -1f) + along * (k % 2 == 0 ? 1f : -1f));
            var door = source.SampleHeight(plot.Centre + plot.Facing * plot.Half.y * 0.75f);
            return math.max(sum / 5f, door);
        }

        /// <summary>
        /// Solid structures creatures walk round. Not pens or dens: animals live in those.
        /// </summary>
        static bool IsBuilding(FloraSpecies s) => s.Solid && s.Form switch
        {
            FloraForm.Hut or FloraForm.StoneHouse or FloraForm.Tent or FloraForm.Tower or FloraForm.Stall or FloraForm.Inn or
            FloraForm.Smithy or FloraForm.Forum or FloraForm.Fountain or FloraForm.Woodpile or FloraForm.Well or
            FloraForm.Hall or FloraForm.Shrine or FloraForm.Columns or FloraForm.Lodge or FloraForm.Palisade or
            FloraForm.Bench or FloraForm.Log or FloraForm.Stump or FloraForm.FirePit => true,
            _ => false
        };

        /// <summary>Solid rocks and columns that can hold up a web (boulders, standing stones and tors, saguaros).</summary>
        static bool IsAnchorRock(FloraSpecies s) => s.Solid && s.Form is FloraForm.Boulder or FloraForm.Spire or FloraForm.Saguaro;

        /// <summary>Something solid a web can be tied to: its foot, how thick, and its top (world height).</summary>
        public struct WebAnchor
        {
            public float3 Base;
            public float Radius, Top;
            /// <summary>A tree's trunk (rather than a rock or a wall).</summary>
            public bool Trunk;
        }

        /// <summary>
        /// Everything round a point solid and tall enough to tie a web to (at least <paramref name="minHeight"/>
        /// high and 6 cm thick): tree trunks, big rocks and columns, and buildings' walls, posts and palisades, placed
        /// as they're drawn. No small plants. Burst-safe; the list is filled until it's full.
        /// </summary>
        public static void NearAnchors(ref FloraBlob f, in TerrainSource source, float2 near, float radius, float minHeight,
                                       ref FixedList512Bytes<WebAnchor> anchors)
        {
            var cellSize = f.CellSizes[TreeLayer];
            var min = (int2)math.floor((near - radius) / cellSize);
            var max = (int2)math.floor((near + radius) / cellSize);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                var plant = Place(ref f, source, new int2(x, y), TreeLayer);
                if (plant.Model < 0 || plant.Model >= f.Models.Length)
                    continue;
                var shape = f.Models[plant.Model];
                if (shape.ObstacleCount > 0)
                    continue;   // a building: its walls below
                var height = shape.Height * plant.Scale;
                var thick = shape.TrunkRadius * plant.Scale;
                if (height < minHeight || thick < 0.06f || math.distance(plant.Position.xz, near) > radius)
                    continue;
                if (anchors.Length == anchors.Capacity)
                    return;
                anchors.Add(new WebAnchor { Base = plant.Position, Radius = thick, Top = plant.Position.y + height, Trunk = shape.Kind != TreeKind.None });
            }
            if (f.Obstacles.Length == 0)
                return;
            if (source.Settlements.IsCreated && f.SettlementSpecies.Length > 0)
            {
                ref var towns = ref source.Settlements.Value;
                var townCell = SettlementSampling.Cell(ref towns, near);
                if (townCell >= 0)
                {
                    var plots = towns.PlotCells[townCell];
                    for (int k = plots.x; k < plots.x + plots.y; k++)
                    {
                        ref var plot = ref towns.Plots[towns.PlotItems[k]];
                        if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length ||
                            math.any(near < plot.Min - radius) || math.any(near > plot.Max + radius))
                            continue;
                        var building = f.SettlementSpecies[plot.Building];
                        if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].ObstacleCount == 0)
                            continue;
                        var cell = (int2)math.floor(plot.Centre / f.CellSizes[building.y]);
                        Walls(ref f, Place(ref f, source, cell, building.y), near, radius, minHeight, ref anchors);
                    }
                }
            }
            if (source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, near, out var landmark) && landmark.Type < f.LandmarkTypeCount)
            {
                ref var set = ref source.Landmarks.Value;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[landmark.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Count == 0 || f.Models[f.Rules[prop.Species].FirstModel].ObstacleCount == 0)
                            continue;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            if (math.distance(spot, near) > 15f + radius)
                                continue;
                            Walls(ref f, Place(ref f, source, (int2)math.floor(spot / f.CellSizes[l]), l), near, radius, minHeight, ref anchors);
                        }
                    }
                }
            }
        }

        /// <summary>A placed building's wall cylinders near a point, as web anchors (see NearAnchors).</summary>
        static void Walls(ref FloraBlob f, in FloraSystem.FloraInstance plant, float2 near, float radius, float minHeight,
                          ref FixedList512Bytes<WebAnchor> anchors)
        {
            if (plant.Model < 0 || plant.Model >= f.Models.Length)
                return;
            var shape = f.Models[plant.Model];
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            for (int i = shape.FirstObstacle; i < shape.FirstObstacle + shape.ObstacleCount; i++)
            {
                var col = f.Obstacles[i];
                var at = plant.Position.xz + new float2(col.x * c - col.y * sn, col.x * sn + col.y * c) * plant.Scale;
                if (col.w * plant.Scale < minHeight || col.z * plant.Scale < 0.06f || math.distance(at, near) > radius)
                    continue;
                if (anchors.Length == anchors.Capacity)
                    return;
                anchors.Add(new WebAnchor
                {
                    Base = new float3(at.x, plant.Position.y, at.y),
                    Radius = col.z * plant.Scale,
                    Top = plant.Position.y + col.w * plant.Scale
                });
            }
        }

        /// <summary>
        /// Whether a body of this radius at <paramref name="xz"/> would be inside a building's walls: a settlement's
        /// (on its plots) or a landmark's placed props. Each is placed as it's drawn (Place), so creatures and players
        /// meet the same walls. Burst-safe; cheap away from settlements and landmarks.
        /// </summary>
        public static bool Blocked(ref FloraBlob f, in TerrainSource source, float2 xz, float radius)
        {
            if (f.Obstacles.Length == 0)
                return false;
            if (source.Settlements.IsCreated && f.SettlementSpecies.Length > 0)
            {
                ref var towns = ref source.Settlements.Value;
                var townCell = SettlementSampling.Cell(ref towns, xz);
                if (townCell >= 0)
                {
                    var plots = towns.PlotCells[townCell];
                    for (int k = plots.x; k < plots.x + plots.y; k++)
                    {
                        ref var plot = ref towns.Plots[towns.PlotItems[k]];
                        if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length ||
                            math.any(xz < plot.Min - radius) || math.any(xz > plot.Max + radius))
                            continue;
                        var building = f.SettlementSpecies[plot.Building];
                        if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].ObstacleCount == 0)
                            continue;
                        var cell = (int2)math.floor(plot.Centre / f.CellSizes[building.y]);
                        if (Inside(ref f, Place(ref f, source, cell, building.y), xz, radius))
                            return true;
                    }
                }
            }
            if (source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out var landmark) && landmark.Type < f.LandmarkTypeCount)
            {
                ref var set = ref source.Landmarks.Value;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[landmark.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Count == 0 || f.Models[f.Rules[prop.Species].FirstModel].ObstacleCount == 0)
                            continue;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            if (math.distance(spot, xz) > 15f + radius)
                                continue;
                            if (Inside(ref f, Place(ref f, source, (int2)math.floor(spot / f.CellSizes[l]), l), xz, radius))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Inside one placed plant's walls (turned and scaled as FloraSystem turns them for the player).</summary>
        static bool Inside(ref FloraBlob f, in FloraSystem.FloraInstance plant, float2 xz, float radius)
        {
            if (plant.Model < 0 || plant.Model >= f.Models.Length)
                return false;
            var shape = f.Models[plant.Model];
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            for (int i = shape.FirstObstacle; i < shape.FirstObstacle + shape.ObstacleCount; i++)
            {
                var col = f.Obstacles[i];
                var offset = new float2(col.x * c - col.y * sn, col.x * sn + col.y * c) * plant.Scale;
                if (math.distance(xz, plant.Position.xz + offset) < col.z * plant.Scale + radius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The light a glowing plant casts round itself: rgb (linear) and reach (w, m), zero if it doesn't glow. A
        /// glowing finish shines with its foliage colour, glowing accents with the first accent's; brighter and further
        /// the more of the plant glows and the bigger it is.
        /// </summary>
        public static float4 GlowLight(FloraSpecies s)
        {
            Color c;
            float share;
            if (s.Finish == FoliageFinish.Glowing)
            {
                c = s.Foliage;
                share = 1f;
            }
            else if (s.Finish == FoliageFinish.Glass)
            {
                // Glass crystals glow faintly from inside.
                c = s.Foliage;
                share = 0.45f;
            }
            else if (s.GlowingAccents && s.Accents.Count > 0)
            {
                c = s.Accents[0];
                share = math.saturate(0.3f + s.AccentAmount);
            }
            else
                return float4.zero;
            var lin = c.linear;
            var size = math.max(s.Height.y, 0.3f);
            var strength = share * math.clamp(1.3f + size * 0.4f, 1.3f, 3f);
            var reach = math.clamp(5f + size * 3f, 6f, 14f) * (0.6f + 0.4f * share);
            // Buildings' lit windows: only at night, marked by a negative reach.
            if (s.Form >= FloraForm.Hut && s.Form <= FloraForm.Well || s.Form == FloraForm.Hall)
                return new float4(lin.r * 0.6f, lin.g * 0.6f, lin.b * 0.6f, -math.min(reach, 9f));
            return new float4(lin.r * strength, lin.g * strength, lin.b * strength, reach);
        }

        /// <summary>
        /// The grid spacing a layer really uses: its tiles hold a whole number of cells, so the cell size is rounded
        /// to fit (FloraSystem's tile streaming relies on it).
        /// </summary>
        public static float CellSize(float tileSize, float cellSize) => tileSize / math.round(tileSize / cellSize);

        /// <summary>The plant growing in one cell of a layer's grid (Model -1 if none).</summary>
        public static FloraSystem.FloraInstance Place(ref FloraBlob f, in TerrainSource source, int2 cell, int layer)
        {
            var none = new FloraSystem.FloraInstance { Model = -1 };
            var cellSize = f.CellSizes[layer];
            var rng = new Random(math.hash(new int3(cell, layer * 7919 + 13)) | 1u);
            var xz = ((float2)cell + rng.NextFloat2()) * cellSize;
            var roll = rng.NextFloat();
            var area = cellSize * cellSize / 100f;
            // Room for 31 (a four-biome border plus a landmark's props; a fuller list would silently drop species).
            var candidates = new FixedList128Bytes<int>();   // species index, or -(prop entry + 1)
            var weights = new FixedList128Bytes<float>();
            var bound = 0f;
            var paved = false;

            // Settlements: each plot's building takes the cell holding the plot's centre, in its species' layer,
            // facing its street; nothing else grows on the streets or on built plots.
            if (source.Settlements.IsCreated && f.SettlementSpecies.Length > 0)
            {
                ref var towns = ref source.Settlements.Value;
                var townCell = SettlementSampling.Cell(ref towns, ((float2)cell + 0.5f) * cellSize);
                if (townCell >= 0)
                {
                    var plots = towns.PlotCells[townCell];
                    for (int k = plots.x; k < plots.x + plots.y; k++)
                    {
                        ref var plot = ref towns.Plots[towns.PlotItems[k]];
                        if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length)
                            continue;
                        var building = f.SettlementSpecies[plot.Building];
                        if (building.x < 0 || building.y != layer || math.any((int2)math.floor(plot.Centre / cellSize) != cell))
                            continue;
                        var buildingRule = f.Rules[building.x];
                        // A bridge span sits at its deck height over the water, at its own size so spans meet end to end.
                        var bridge = plot.Role == PlotRole.Bridge;
                        return new FloraSystem.FloraInstance
                        {
                            Position = new float3(plot.Centre.x, bridge ? plot.Pad : FloorHeight(source, plot), plot.Centre.y),
                            Yaw = math.atan2(-plot.Facing.x, plot.Facing.y),
                            // Sized for its folk (the layout's BuildingScale: ogre tents bigger, goblin huts smaller).
                            Scale = bridge ? 1f : rng.NextFloat(0.95f, 1.05f) * towns.Layouts[towns.Settlements[plot.Settlement].Layout].BuildingScale,
                            Model = buildingRule.FirstModel + rng.NextInt(buildingRule.Variants),
                            Random = rng.NextFloat(),
                            Ground = bridge ? 0u : DirtColor(source, buildingRule, plot.Centre, math.cmax(plot.Half), 0.9f)
                        };
                    }
                    // Streets and built plots grow nothing, though a landmark's placed props (benches on the square,
                    // logs round a camp's yard) still stand there: decided after them, below.
                    paved = SettlementSampling.Keep(ref towns, xz) < 0.5f;
                }
            }

            // Inside a landmark: its centrepiece takes the cell holding the centre, the biome's plants thin out
            // towards the middle, and its props join the candidates.
            var keep = 1f;
            var towardsCentre = float2.zero;
            if (source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out var landmark) && landmark.Type < f.LandmarkTypeCount)
            {
                ref var set = ref source.Landmarks.Value;
                var clear = set.Types[landmark.Type].ClearRadius;
                keep = clear > 0f ? math.smoothstep(clear * 0.8f, clear, landmark.Distance) : 1f;
                towardsCentre = set.Instances[landmark.Instance].Position - xz;

                // Placed props (an inn, a pen): each takes the cell holding its spot, in its own layer, and keeps
                // everything else in every layer clear of it.
                var blocked = false;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[landmark.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            if (l == layer && math.all((int2)math.floor(spot / cellSize) == cell))
                            {
                                var placedRule = f.Rules[prop.Species];
                                var toCentre = set.Instances[landmark.Instance].Position - spot;
                                var placedYaw = prop.FaceCentre != 0 ? math.atan2(-toCentre.x, toCentre.y) : rng.NextFloat(math.PI * 2f);
                                if (placedRule.FaceLandmark != 0)
                                    placedYaw = FacingYaw(set.Instances[landmark.Instance].Facing);
                                return new FloraSystem.FloraInstance
                                {
                                    Position = new float3(spot.x, source.SampleHeight(spot), spot.y),
                                    Yaw = placedYaw,
                                    Scale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y)),
                                    Model = placedRule.FirstModel + rng.NextInt(placedRule.Variants),
                                    Random = rng.NextFloat(),
                                    Ground = DirtColor(source, placedRule, spot, 1f, 0.25f)
                                };
                            }
                            blocked |= math.distance(xz, spot) < prop.Clearance;
                        }
                    }
                }
                if (blocked)
                    return none;
                var propRange = f.PropRanges[landmark.Type * 3 + layer];
                for (int e = propRange.x; e < propRange.x + propRange.y; e++)
                {
                    var prop = f.Props[e];
                    if (prop.Count > 0)
                        continue;
                    if (prop.Centrepiece != 0)
                    {
                        var centre = set.Instances[landmark.Instance].Position;
                        if (math.all((int2)math.floor(centre / cellSize) == cell))
                        {
                            var centreRule = f.Rules[prop.Species];
                            var centreYaw = rng.NextFloat(math.PI * 2f);   // drawn even when unused, so later draws don't shift
                            if (centreRule.FaceLandmark != 0)
                                centreYaw = FacingYaw(set.Instances[landmark.Instance].Facing);
                            return new FloraSystem.FloraInstance
                            {
                                Position = new float3(centre.x, source.SampleHeight(centre), centre.y),
                                Yaw = centreYaw,
                                Scale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y)),
                                Model = centreRule.FirstModel + rng.NextInt(centreRule.Variants),
                                Random = rng.NextFloat(),
                                Ground = DirtColor(source, centreRule, centre, 2f, 0.5f)
                            };
                        }
                        continue;
                    }
                    var ring = math.smoothstep(prop.Ring.x - 0.05f, prop.Ring.x, landmark.Distance) *
                               (1f - math.smoothstep(prop.Ring.y, prop.Ring.y + 0.05f, landmark.Distance));
                    var d = prop.Density * area * ring;
                    if (d <= 0f || candidates.Length >= candidates.Capacity)
                        continue;
                    candidates.Add(-(e + 1));
                    weights.Add(d);
                    bound += d;
                }
            }

            if (paved)
                return none;

            // Cheap first pass: which species could grow here, at most how likely.
            source.SampleBiomes(xz, out var biomes, out _);
            // Realms meet with a hard edge: a biome's plants only grow where its realm has most of the ground (its
            // biomes' summed weight, from the same sample). Inside a realm, biomes still blend.
            var realmOf = int4.zero - 1;
            for (int b = 0; b < 4; b++)
                if (biomes.Weights[b] > 0f && biomes.Ids[b] < f.BiomeCount)
                    realmOf[b] = f.BiomeRealm[biomes.Ids[b]];
            for (int b = 0; b < 4; b++)
            {
                var w = biomes.Weights[b];
                if (w <= 0f || biomes.Ids[b] >= f.BiomeCount)
                    continue;
                if (realmOf[b] >= 0 && math.csum(math.select(float4.zero, biomes.Weights, realmOf == realmOf[b])) < 0.5f)
                    continue;
                var range = f.Ranges[biomes.Ids[b] * 3 + layer];
                for (int e = range.x; e < range.x + range.y; e++)
                {
                    var entry = f.Entries[e];
                    var d = entry.Density * w * area * keep;
                    if (d <= 0f)
                        continue;
                    var at = candidates.IndexOf(entry.Species);
                    if (at < 0)
                    {
                        if (candidates.Length >= candidates.Capacity)
                            continue;
                        candidates.Add(entry.Species);
                        weights.Add(d);
                    }
                    else
                        weights[at] += d;
                    bound += d * math.max(1f, f.Rules[entry.Species].NearWater);
                }
            }
            if (roll >= bound)
                return none;

            // Full sample of the ground; nothing over or at the edge of a cave entrance (a wide plant or rock standing
            // just outside the cut would overhang it and float).
            var ground = source.SampleGround(xz);
            if (source.CaveClearance(xz, ground.Height) < 1.5f)
                return none;
            // Land plants keep out of the water; aquatic ones grow only in it, at their depths.
            var depth = ground.HasWater ? ground.WaterSurface - ground.Height : -1f;
            var underwater = depth > -0.15f;
            var normal = source.SampleNormal(xz, 1f);
            var surface = source.SampleSurface(xz, ground, normal);
            var slope = math.degrees(math.acos(math.saturate(normal.y)));
            var elevation = ground.Height - source.World.SeaLevel;
            var nearWater = 1f - math.smoothstep(1.5f, 8f, -ground.WaterDepth);
            var dry = 1f - math.saturate(ground.River * 1.5f);

            var total = 0f;
            var shrink = new FixedList128Bytes<float>();
            for (int c = 0; c < candidates.Length; c++)
            {
                // Props go where the landmark puts them, whatever the ground, except too steep for the species
                // (rocks would jut out of cliff faces).
                if (candidates[c] < 0)
                {
                    var propRule = f.Rules[f.Props[-candidates[c] - 1].Species];
                    weights[c] *= 1f - math.smoothstep(propRule.MaxSlope - 4f, propRule.MaxSlope, slope);
                    if (underwater && propRule.Aquatic == 0)
                        weights[c] = 0f;
                    shrink.Add(1f);
                    total += weights[c];
                    continue;
                }
                var r = f.Rules[candidates[c]];
                if (r.Aquatic != 0)
                {
                    // Water plants: just the right depth (and patches, if the species has them).
                    var inDepth = depth >= r.WaterDepth.x && depth <= r.WaterDepth.y ? 1f : 0f;
                    var fw = weights[c] * inDepth;
                    // Floating plants drift away in a current: they keep to still water.
                    if (r.Floats != 0)
                        fw *= 1f - 0.9f * math.saturate(math.length(ground.Flow));
                    if (r.MinRapids > 0f)
                        fw *= math.smoothstep(r.MinRapids - 0.1f, r.MinRapids + 0.1f, ground.Rapids);
                    if (r.PatchCoverage < 1f)
                    {
                        var pn = noise.snoise(xz * r.PatchFrequency + r.PatchOffset) * 0.5f + 0.5f;
                        fw *= math.smoothstep(1f - r.PatchCoverage - 0.12f, 1f - r.PatchCoverage + 0.12f, pn);
                    }
                    weights[c] = fw;
                    shrink.Add(1f);
                    total += fw;
                    continue;
                }
                if (underwater)
                {
                    weights[c] = 0f;
                    shrink.Add(1f);
                    continue;
                }
                var fl = weights[c] * dry;
                fl *= 1f - math.smoothstep(r.MaxSlope - 4f, r.MaxSlope, slope);
                var upper = 1f - math.smoothstep(r.Elevation.y - 25f, r.Elevation.y, elevation);
                fl *= math.smoothstep(r.Elevation.x - 1f, r.Elevation.x + 1f, elevation) * upper;
                fl *= math.lerp(1f, math.saturate(surface.Grass * 1.6f), r.GrassAffinity);
                if (r.ToleratesSnow == 0)
                    fl *= math.saturate(1f - surface.Snow * 3f);
                if (r.PatchCoverage < 1f)
                {
                    var n = noise.snoise(xz * r.PatchFrequency + r.PatchOffset) * 0.5f + 0.5f;
                    fl *= math.smoothstep(1f - r.PatchCoverage - 0.12f, 1f - r.PatchCoverage + 0.12f, n);
                }
                fl *= math.lerp(1f, r.NearWater, nearWater);
                if (r.OnlyNearWater != 0)
                    fl *= nearWater;
                weights[c] = fl;
                shrink.Add(upper);
                total += fl;
            }
            if (roll >= total)
                return none;

            var pick = 0;
            for (var cumulative = weights[0]; pick < candidates.Length - 1 && roll >= cumulative; cumulative += weights[++pick]) { }
            var picked = candidates[pick];
            var sizes = f.Rules[picked < 0 ? f.Props[-picked - 1].Species : picked].SizeRange;
            var scale = rng.NextFloat(sizes.x, math.max(sizes.x, sizes.y));
            var yaw = rng.NextFloat(math.PI * 2f);
            if (picked < 0)
            {
                var prop = f.Props[-picked - 1];
                picked = prop.Species;
                scale *= rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y));
                // Models face +Z, which yaw turns to (-sin, cos).
                if (prop.FaceCentre != 0)
                    yaw = math.atan2(-towardsCentre.x, towardsCentre.y) + rng.NextFloat(-0.15f, 0.15f);
            }
            var rule = f.Rules[picked];
            return new FloraSystem.FloraInstance
            {
                Position = new float3(xz.x, rule.Floats != 0 && ground.HasWater ? ground.WaterSurface + 0.01f : ground.Height, xz.y),
                Yaw = yaw,
                // Plants grow smaller towards the top of their range (a tree line).
                Scale = scale * (0.55f + 0.45f * shrink[pick]),
                Model = rule.FirstModel + rng.NextInt(rule.Variants),
                Random = rng.NextFloat(),
                Snow = surface.Snow,
                // Rocks lie over onto the slope they stand on.
                Tilt = normal.xz * rule.Tilt,
                Ground = DirtColor(source, rule, xz, 1f, 0.25f)
            };
        }

        /// <summary>
        /// A tree (broadleaf, conifer or palm, at least <paramref name="minHeight"/> tall) within
        /// <paramref name="radius"/> of a point: a random one, so animals don't all pick the same.
        /// </summary>
        public static bool FindTree(ref FloraBlob f, in TerrainSource source, float2 near, float radius, float minHeight,
                                    ref Random rng, out TreeSpot tree)
        {
            tree = default;
            var cellSize = f.CellSizes[TreeLayer];
            var min = (int2)math.floor((near - radius) / cellSize);
            var max = (int2)math.floor((near + radius) / cellSize);
            var found = 0;
            // A wide search samples some of the cells rather than all (many animals choose a tree at once at dusk).
            var span = max - min + 1;
            var cells = span.x * span.y;
            var tries = math.min(cells, MaxTreeCells);
            // Each cell at most once: a random start, then a stride that shares no factor with the cell count.
            var start = rng.NextInt(cells);
            var stride = 1;
            if (cells > MaxTreeCells)
                for (stride = cells / MaxTreeCells + 1; Gcd(stride, cells) != 1; stride++) { }
            for (int t = 0; t < tries; t++)
            {
                var index = (start + t * stride) % cells;
                var plant = Place(ref f, source, min + new int2(index % span.x, index / span.x), TreeLayer);
                if (plant.Model < 0 || plant.Model >= f.Models.Length)
                    continue;
                var shape = f.Models[plant.Model];
                var height = shape.Height * plant.Scale;
                var distance = math.distance(plant.Position.xz, near);
                if (shape.Kind == TreeKind.None || height < minHeight || distance > radius)
                    continue;
                // Reservoir pick: each suitable tree equally likely.
                found++;
                if (rng.NextFloat() * found >= 1f)
                    continue;
                tree = new TreeSpot
                {
                    Base = plant.Position,
                    Height = height,
                    TrunkRadius = shape.TrunkRadius * plant.Scale,
                    CrownBase = shape.CrownBase * plant.Scale,
                    CrownRadius = shape.CrownRadius * plant.Scale,
                    Kind = shape.Kind
                };
            }
            return found > 0;
        }
    }
}
