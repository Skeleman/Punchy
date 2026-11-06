using System;
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
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
        /// <summary>W-63: the FloraSpecies.RiverSpot it is dressed onto (0 = none; see RiverFit).</summary>
        public int RiverSpot;
        /// <summary>How far it lies over onto the slope it stands on (rocks 0.85; upright plants 0).</summary>
        public float Tilt;
        /// <summary>1 for rocks that keep their own colour wherever they lie (ice), not the biome's rock (RockTint).</summary>
        public int OwnColour;
        /// <summary>1 for built things (FloraGenerator.Founded): their bases take a band of the ground's colour.</summary>
        public int Dirt;
        /// <summary>1 for hive trees, 2 for docks: placed in a landmark, they face its way (comb on a known side, docks out to
        /// sea). A dock also slides along that way to the waterline (DockShore), so its land end starts on the bank.</summary>
        public int FaceLandmark;
        /// <summary>1 for fishing villages' shore clutter (#36, W-59d): kept off the walk lane up each of the village's docks.</summary>
        public int ShoreKit;
        /// <summary>W-63 (#36): 1 for a kit house, 2 for a stilt house: a placed one never stands on a dock's walk lane.</summary>
        public int House;
        /// <summary>W-62c (#37): for a landing pier, its deck length (m, the species' Height); 0 for everything else.</summary>
        public float PierReach;
        /// <summary>W-62c (#37): 1 for sea folk spires: none stands within PierEndClear of a landing pier's far end, so the
        /// village reads from the deck (a spire stood 3.8 m off the end and filled the view).</summary>
        public int PierClear;
        /// <summary>W-65b (#36): 1 for a fishing village's wharf: its variant follows the village, so no two villages share a layout.</summary>
        public int PerVillage;
        /// <summary>W-101: a runed spire's colour patterns (FloraSpecies.RunePatterns, 0 for everything else): the
        /// variants split into that many runs, and one landmark's stones all come from the same run.</summary>
        public int Patterns;
        /// <summary>
        /// W-61b (#38): for igloos, how far out the porch mouth is (0 for everything else). On a settlement's slope an
        /// igloo set at its centre's ground had its porch floor up to 1 m over a dip at the mouth, or buried in a knoll;
        /// PorchSeat turns it a little off the centre and seats it so the floor meets the ground at the mouth.
        /// </summary>
        public float Porch;
        /// <summary>
        /// How far its footprint reaches at scale 1 (m): the trunk for trees, about half its spread otherwise. Settlement
        /// plots (buildings, fences, palisades, pens) win over anything whose footprint would overlap them (OnBuiltPlot).
        /// </summary>
        public float Footprint;
        /// <summary>
        /// For arches: how far each foot stands from the middle along the model's X at scale 1 (m), 0 otherwise. An
        /// arch is only placed where the ground under both feet is about as high as in the middle (F-44).
        /// </summary>
        public float Span;
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
        /// <summary>Where birds can sit on top of it (roof peaks, ridges, chimneys, posts): its points in FloraBlob.Perches.</summary>
        public int FirstPerch, PerchCount;
        /// <summary>Its chimneys' flue tops (W-43c, for smoke): its points in FloraBlob.Chimneys.</summary>
        public int FirstChimney, ChimneyCount;
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
        /// <summary>Per settlement layout (Layouts order): its Kit Building species and layer, or -1 (#24).</summary>
        public BlobArray<int2> LayoutKit;
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
        /// <summary>
        /// Perches on buildings, posts and signposts (FloraModel.Perches): points a bird sits on, unturned and unscaled.
        /// Only built for the animals' tables, like Obstacles.
        /// </summary>
        public BlobArray<float3> Perches;
        /// <summary>Chimneys' flue tops (FloraModel.Chimneys), unturned and unscaled. Only built for the animals' tables, like Perches.</summary>
        public BlobArray<float3> Chimneys;
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

        /// <summary>The species a palette's flora, landmark props and settlements use, in model order (by layer, then name).</summary>
        public static void CollectSpecies(BiomePalette palette, List<FloraSpecies> species)
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
            var layouts = SettlementBaking.Layouts(landmarks);
            foreach (var l in layouts)
                if (l.KitBuilding != null)
                    found.Add(l.KitBuilding);
            species.Clear();
            species.AddRange(found);
            species.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>
        /// Builds the tables from a palette. <paramref name="species"/> receives the species in model order (sorted by
        /// layer, then name): species i's variants are models Rules[i].FirstModel onwards. With
        /// <paramref name="obstacles"/>, buildings' walls too (each building variant is grown once for its colliders,
        /// so only the animals' tables pay for it).
        /// </summary>
        /// <summary>
        /// A-55 (#102): starts on worker threads the shape-only grows that Build(obstacles: true) reads (buildings' walls,
        /// perches, chimneys), so Build can take them finished instead of growing them all in one frame. Main thread only
        /// (it reads the assets' names).
        /// </summary>
        public static Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>> StartObstacleShapes(BiomePalette palette)
        {
            var list = new List<FloraSpecies>();
            CollectSpecies(palette, list);
            var tasks = new Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>>();
            foreach (var s in list)
                if (IsBuilding(s) || HasPerches(s) || IsAnchorRock(s))
                {
                    var name = s.name;
                    for (int v = 0; v < s.Variants; v++)
                    {
                        var variant = v;
                        tasks[(s, v)] = System.Threading.Tasks.Task.Run(() => FloraGenerator.GrowShape(s, variant, name));
                    }
                }
            return tasks;
        }

        public static BlobAssetReference<FloraBlob> Build(BiomePalette palette, float3 cellSizes, List<FloraSpecies> species, bool obstacles = false,
                                                          Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>> grownShapes = null)
        {
            CollectSpecies(palette, species);
            var landmarks = palette.Landmarks ?? Array.Empty<LandmarkType>();
            var buildings = SettlementBaking.Buildings(landmarks);
            var layouts = SettlementBaking.Layouts(landmarks);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<FloraBlob>();
            root.CellSizes = cellSizes;

            var rules = builder.Allocate(ref root.Rules, species.Count);
            var shapes = new List<FloraShape>();
            var walls = new List<float4>();
            var perches = new List<float3>();
            var chimneys = new List<float3>();
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
                    RiverSpot = (int)s.RiverSpot,
                    Tilt = s.Form == FloraForm.Boulder ? 0.85f : 0f,
                    OwnColour = s.KeepOwnColour ? 1 : 0,
                    Dirt = FloraGenerator.Founded(s.Form) ? 1 : 0,
                    // Docks (#36) face the landmark's way too: a fishing village faces open water, so they run out to sea.
                    // A sea folk pier (#37) faces its landing's way too, but sits on its bank as placed: DockShore seats on the sea.
                    FaceLandmark = s.Form == FloraForm.HiveTree || s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Pier ? 1 : s.Form == FloraForm.KitBuilding && (s.KitShape == Punchy.Terrain.KitShape.Dock || s.KitShape == Punchy.Terrain.KitShape.Wharf) ? 2 : 0,
                    ShoreKit = s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Shore ? 1 : 0,
                    House = s.Form == FloraForm.KitBuilding && Punchy.Terrain.StructureKit.HouseLike(s.KitShape) ? (s.KitShape == Punchy.Terrain.KitShape.StiltHouse ? 2 : 1) : 0,
                    PierReach = s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Pier ? s.Height.y : 0f,
                    PierClear = s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Spire ? 1 : 0,
                    PerVillage = s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Wharf ? 1 : 0,
                    Patterns = s.Form == FloraForm.Spire && s.RunePatterns > 0 ? math.min(s.RunePatterns, s.Variants) : 0,
                    // StructureKit.Igloo (W-64): the tunnel ends 1-1.8 m past the dome's foot (the radius), plus the 0.8 m path out.
                    Porch = s.Form == FloraForm.KitBuilding && s.KitShape == Punchy.Terrain.KitShape.Igloo ? (s.Height.x + s.Height.y) * 0.5f * s.Spread * 0.5f + 2.2f : 0f,
                    Footprint = Footprint(s),
                    Span = s.Form == FloraForm.IceArch ? (s.Height.x + s.Height.y) * 0.5f * math.max(s.Spread, 0.5f) * 0.5f :
                           // W-63: a log across a narrows reaches half its shortest length each way from the middle.
                           s.Form == FloraForm.Driftwood && s.RiverSpot == RiverSpot.LogNarrows ? s.Height.x * 0.5f :
                           // W-65b: a stepping-stone row reaches half its length each way.
                           // (W-65d: the longest; FloraPlacement fits each row's variant to its narrows.)
                           s.Form == FloraForm.Boulder && s.RiverSpot == RiverSpot.Narrows ? FloraGenerator.StoneRowLength(FloraGenerator.StoneRowVariants - 1) * 0.5f : 0f
                };
                for (int v = 0; v < s.Variants; v++)
                {
                    var shape = FloraGenerator.Shape(s, v);
                    if (obstacles && (IsBuilding(s) || HasPerches(s)))
                    {
                        var model = grownShapes != null && grownShapes.TryGetValue((s, v), out var grown) && grown.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                            ? grown.Result : FloraGenerator.Grow(s, v, shapeOnly: true);
                        if (IsBuilding(s))
                        {
                            shape.FirstObstacle = walls.Count;
                            foreach (var c in model.Colliders)
                                walls.Add(new float4(c.x, c.y, c.z, c.w));
                            if (model.Colliders.Count == 0 && model.TrunkRadius > 0f)
                                walls.Add(new float4(0f, 0f, model.TrunkRadius, model.Height));
                            shape.ObstacleCount = walls.Count - shape.FirstObstacle;
                        }
                        shape.FirstPerch = perches.Count;
                        foreach (var p in model.Perches)
                            perches.Add(p);
                        shape.PerchCount = perches.Count - shape.FirstPerch;
                        shape.FirstChimney = chimneys.Count;
                        foreach (var c in model.Chimneys)
                            chimneys.Add(c);
                        shape.ChimneyCount = chimneys.Count - shape.FirstChimney;
                        model.Destroy();
                    }
                    else if (obstacles && IsAnchorRock(s))
                    {
                        // (Its size, for spiders to tie webs to: a rock's shape is otherwise left empty.)
                        var model = grownShapes != null && grownShapes.TryGetValue((s, v), out var grown) && grown.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                            ? grown.Result : FloraGenerator.Grow(s, v, shapeOnly: true);
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
            var perchArray = builder.Allocate(ref root.Perches, perches.Count);
            for (int i = 0; i < perches.Count; i++)
                perchArray[i] = perches[i];
            var chimneyArray = builder.Allocate(ref root.Chimneys, chimneys.Count);
            for (int i = 0; i < chimneys.Count; i++)
                chimneyArray[i] = chimneys[i];
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
            var layoutKit = builder.Allocate(ref root.LayoutKit, layouts.Count);
            for (int i = 0; i < layouts.Count; i++)
                layoutKit[i] = layouts[i].KitBuilding != null
                    ? new int2(species.IndexOf(layouts[i].KitBuilding), (int)layouts[i].KitBuilding.Layer)
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

        /// <summary>The ground colour a placed plant carries (FloraInstance.Ground): a rock's tint, or a built thing's dirt.</summary>
        static uint GroundFor(in TerrainSource source, in FloraRules rule, float2 at, float reach, float band) =>
            rule.Tilt > 0f && rule.OwnColour == 0 ? RockTint(source, at) : DirtColor(source, rule, at, reach, band);

        /// <summary>
        /// A rock's colour where it lies (G-36): on dry ground, the biome's own rock (the surface its cliffs take, so a rock
        /// in the desert is sandstone, not grey and mossy), packed as DirtColor is with the alpha at its top value (the
        /// shader's sign for a rock tint); on grassy, wet ground 0, so the rock keeps its own grey and moss.
        /// </summary>
        public const uint RockTintMark = 255u;
        static uint RockTint(in TerrainSource source, float2 at)
        {
            var ground = source.SampleGround(at);
            var flat = source.SampleSurface(at, ground, source.SampleNormal(at, 1f));
            if (flat.Grass > 0.3f || ground.HasWater && ground.WaterSurface > ground.Height)
                return 0u;
            var cliff = source.SampleSurface(at, ground, math.normalize(new float3(1f, 0.2f, 0f)));
            var c = (uint3)math.round(math.saturate(math.lerp(cliff.Albedo, new float3(0.62f, 0.64f, 0.68f), math.saturate(flat.Snow))) * 255f);
            return c.x | (c.y << 8) | (c.z << 16) | (RockTintMark << 24);
        }

        /// <summary>FloraRules.Footprint for a species (from its settings, without growing it).</summary>
        static float Footprint(FloraSpecies s)
        {
            var height = (s.Height.x + s.Height.y) * 0.5f;
            var tree = s.Form == FloraForm.BroadleafTree || s.Form == FloraForm.Conifer || s.Form == FloraForm.Palm || s.Form == FloraForm.HiveTree;
            return tree ? height * s.Thickness : math.min(height * s.Spread * 0.5f, 6f);
        }

        /// <summary>
        /// Whether a footprint of this radius (plus half a metre) at <paramref name="xz"/> overlaps a settlement plot that
        /// is built on (a building, a fence or palisade run, a pen; not a bridge), in the plot's own turned rectangle.
        /// Buildings and fences win: whatever else would stand in them isn't placed (scattered plants and rocks, a
        /// neighbouring landmark's props, road clutter), rather than the fence giving way.
        /// </summary>
        /// <summary>
        /// What stands on a plot: its layout building's species and layer, except that the island's biggest settlement
        /// (largest radius, lowest index on a tie) builds its house, seat, inn, smithy, store and workshop plots from its
        /// layout's Kit Building (#24), each a variant drawn as before. Plots, roles and placement draws are unchanged.
        /// </summary>
        public static int2 PlotSpecies(ref FloraBlob f, ref SettlementSetBlob towns, in SettlementPlot plot)
        {
            var species = f.SettlementSpecies[plot.Building];
            if (f.LayoutKit.Length == 0 || species.x < 0 || plot.Settlement < 0 || plot.Settlement >= towns.Settlements.Length)
                return species;
            if (plot.Role != PlotRole.House && plot.Role != PlotRole.Seat && plot.Role != PlotRole.Inn &&
                plot.Role != PlotRole.Smithy && plot.Role != PlotRole.Store && plot.Role != PlotRole.Workshop)
                return species;
            ref var town = ref towns.Settlements[plot.Settlement];
            if (town.Layout < 0 || town.Layout >= f.LayoutKit.Length || f.LayoutKit[town.Layout].x < 0)
                return species;
            for (int i = 0; i < towns.Settlements.Length; i++)
            {
                var r = towns.Settlements[i].Radius;
                if (r > town.Radius || (r == town.Radius && i < plot.Settlement))
                    return species;
            }
            return f.LayoutKit[town.Layout];
        }

        static bool OnBuiltPlot(ref FloraBlob f, in TerrainSource source, float2 xz, float radius)
        {
            if (!source.Settlements.IsCreated || f.SettlementSpecies.Length == 0)
                return false;
            ref var towns = ref source.Settlements.Value;
            var townCell = SettlementSampling.Cell(ref towns, xz);
            if (townCell < 0)
                return false;
            radius += 0.5f;
            var plots = towns.PlotCells[townCell];
            for (int k = plots.x; k < plots.x + plots.y; k++)
            {
                ref var plot = ref towns.Plots[towns.PlotItems[k]];
                if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length || plot.Role == PlotRole.Bridge)
                    continue;
                var d = xz - plot.Centre;
                // The plot's own building stands at its centre: that's not an overlap.
                if (math.lengthsq(d) < 1e-4f)
                    continue;
                if (math.abs(math.dot(d, new float2(-plot.Facing.y, plot.Facing.x))) < plot.Half.x + radius &&
                    math.abs(math.dot(d, plot.Facing)) < plot.Half.y + radius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a footprint of this radius at <paramref name="xz"/> lies on a road or a road's spur (streets with Road
        /// set): a landmark's placed props give way to them (Z-13: a roadside hermit's cabbage patch with a road through
        /// it), as its scattered props already do to every street. Town streets and squares don't count, so benches and
        /// stalls on a square stay.
        /// </summary>
        static bool OnRoad(in TerrainSource source, float2 xz, float radius)
        {
            if (!source.Settlements.IsCreated)
                return false;
            ref var towns = ref source.Settlements.Value;
            var cell = SettlementSampling.Cell(ref towns, xz);
            if (cell < 0)
                return false;
            var range = towns.StreetCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var street = ref towns.Streets[towns.StreetItems[k]];
                if (street.Road == 0 || math.any(xz < street.Min - radius) || math.any(xz > street.Max + radius))
                    continue;
                if (SettlementSampling.Across(street, xz, out _) < street.Width * 0.5f + radius)
                    return true;
            }
            return false;
        }

        /// <summary>The yaw that turns a model's front (+Z) to face a direction (yaw maps +Z to (-sin, cos)).</summary>
        static float FacingYaw(float2 facing) => math.atan2(-facing.x, facing.y);

        /// <summary>
        /// W-61b (#38): an igloo facing its settlement's centre, turned up to 30 degrees either way (W-65b; was 60) to the heading whose
        /// porch mouth (<paramref name="porch"/> m out) is nearest its centre's height (straight on wins near-ties), then
        /// lowered (up to 3.5 m since W-65d, was 1.4; the dome sinking into the slope) or raised (up to 0.5 m, within its foundation skirt) so
        /// the porch floor meets the ground at the mouth, not a ledge over a dip. No random draws, so nothing else moves.
        /// </summary>
        static void PorchSeat(in TerrainSource source, float2 spot, float porch, ref float yaw, ref float y)
        {
            var best = yaw;
            var bestCost = float.MaxValue;
            var bestY = y;
            // W-65b (#38): up to 30 degrees now (was 60): a mouth turned 60 degrees faced a neighbour, and the walk
            // between two mouths ran into a dome.
            // W-65d (#38): on a steep slope no turn within 30 degrees seats the mouth (three igloos kept a 1.8 m step up
            // onto the path, or a tunnel buried 2-4 m), so the igloo may now sink up to 3.5 m into the slope, and as a
            // last resort it turns its back on the centre (150-210 degrees: a mouth facing out never faces a neighbour)
            // to face downhill. A heading is judged by what the walker meets: the step from the ground up onto the
            // path at the mouth (at most 0.3) and the ground in the tunnel under a 1.8 m head (roof 4 m over the floor).
            for (int k = 0; k < 6; k++)
            {
                var turn = math.radians(k < 3 ? 30f * (k == 0 ? 0 : k == 1 ? 1 : -1) : 180f + 30f * (k - 4));
                var a = yaw + turn;
                var fwd = new float2(-math.sin(a), math.cos(a));
                var mouth = source.SampleHeight(spot + fwd * porch);
                var tunnel = math.max(source.SampleHeight(spot + fwd * (porch - 1.5f)), source.SampleHeight(spot + fwd * (porch - 3f)));
                var seat = math.clamp(mouth, y - 3.5f, y + 0.5f);
                var miss = math.max(0f, seat - 0.3f - mouth) + math.max(0f, math.max(mouth, tunnel) - seat - 2f);
                var cost = miss * 10f + (k < 3 ? 0.25f * math.abs(turn) : 3f) + 0.05f * math.abs(seat - y);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = a;
                    bestY = seat;
                }
            }
            yaw = best;
            y = bestY;
        }

        /// <summary>
        /// Where a dock (#36, W-59c) starts: its ring spot slid along the village's facing (up to 14 m out, 30 m back) to
        /// just above the waterline, so the land end and its short stair stand on the bank and the deck (0.8 m up) rides
        /// clear of the sea; never below the sea itself. (W-59b's docks started anywhere from 1.7 m under water to 2.5 m
        /// up the bank, and the player snagged at the land end.)
        /// </summary>
        /// <summary>How far DockShore can slide a dock's spot out along its facing (on the bank) and back (in the water).</summary>
        const float ShoreSlideOut = 14f, ShoreSlideBack = 30f;

        static float3 DockShore(in TerrainSource source, float2 spot, float2 facing)
        {
            var sea = source.World.SeaLevel;
            const float above = 0.25f;
            var dir = math.normalizesafe(facing, new float2(1f, 0f));
            var best = spot;
            if (source.SampleHeight(spot) > sea + above)
            {
                // On the bank: walk out to the last dry step before the water.
                for (var d = 0.5f; d <= ShoreSlideOut; d += 0.5f)
                {
                    if (source.SampleHeight(spot + dir * d) <= sea + above)
                        break;
                    best = spot + dir * d;
                }
            }
            else
            {
                // In the water: walk back up to the first dry step. W-63 (#36): up to 30 m. A ring spot more than 14 m out
                // (dock -3296.5,-1656.1) used to stop in the water at sea + 0.1, so the ramp foot sank about 1 m into the
                // steep bank behind it and the walker met a 1.3 m wall; now every dock's base sits on its bank.
                for (var d = 0.5f; d <= ShoreSlideBack; d += 0.5f)
                {
                    best = spot - dir * d;
                    if (source.SampleHeight(best) > sea + above)
                        break;
                }
            }
            return new float3(best.x, math.max(source.SampleHeight(best), sea + 0.1f), best.y);
        }

        /// <summary>
        /// W-63 river dressing (Zach: cattails on a bend, driftwood, stepping stones, a fallen log): how well
        /// <paramref name="xz"/> suits a <see cref="RiverSpot"/>, 0..1, from the river map alone (so it is seeded, the
        /// same for every player). Slow water only (rapids under 0.3). The bend is the turn of the flow between a river
        /// width and a half upstream and downstream; the inner side is the side it turns towards.
        /// </summary>
        static float RiverFit(in TerrainSource source, float2 xz, in GroundSample ground, int kind)
        {
            if (!source.Rivers.IsCreated || ground.Rapids > 0.3f)
                return 0f;
            var q = RiverSampling.Query(ref source.Rivers.Value, source.RiverSettings, xz);
            if (!q.Found || q.Width <= 0.5f || q.Distance > q.Width * 0.5f + 4f)
                return 0f;
            var half = q.Width * 0.5f;
            var edge = q.Distance - half;   // negative in the channel, positive up the bank
            var flow = math.normalizesafe(q.Flow, new float2(0f, 1f));
            var depth = ground.HasWater ? ground.WaterSurface - ground.Height : -1f;
            if (kind == (int)RiverSpot.Narrows)
            {
                // Shallow narrows, in bands about 5 m wide across the water every 40 m along it.
                if (q.Width > 14f || depth < 0.05f || depth > 0.9f)
                    return 0f;
                var band = math.frac(math.dot(xz, flow) / 40f + (math.hash(new int2((int2)math.floor(xz / 240f))) & 1023) / 1024f);
                return band < 0.12f ? 1f : 0f;
            }
            if (kind == (int)RiverSpot.LogNarrows)
                return q.Width <= 10f && q.Distance < 2f ? 1f : 0f;
            var reach = math.max(10f, q.Width * 1.5f);
            var up = RiverSampling.Query(ref source.Rivers.Value, source.RiverSettings, xz - flow * reach);
            var down = RiverSampling.Query(ref source.Rivers.Value, source.RiverSettings, xz + flow * reach);
            if (!up.Found || !down.Found)
                return 0f;
            var a = math.normalizesafe(up.Flow);
            var b = math.normalizesafe(down.Flow);
            var turn = a.x * b.y - a.y * b.x;   // > 0 turning left
            var left = new float2(-flow.y, flow.x);
            var sideways = RiverSampling.Query(ref source.Rivers.Value, source.RiverSettings, xz + left);
            var onLeft = sideways.Distance > q.Distance ? 1f : -1f;
            var bend = math.saturate(math.abs(turn) / 0.3f);
            var inner = turn * onLeft > 0f;
            if (kind == (int)RiverSpot.InnerBend)
                return inner && edge > -half * 0.6f && edge < 3f ? math.smoothstep(0.15f, 0.6f, bend) : 0f;
            if (kind == (int)RiverSpot.GravelBar)
                return edge > -1.2f && edge < 2.5f ? (inner ? math.max(bend, 0.3f) : 0.08f) : 0f;
            return 0f;
        }

        /// <summary>W-63: dry ground (no water over it).</summary>
        static bool Dry(in GroundSample g) => !g.HasWater || g.Height > g.WaterSurface + 0.05f;

        /// <summary>W-65f: how far from <paramref name="p"/> along <paramref name="dir"/> the first ground above
        /// <paramref name="water"/> - 0.3 is (0.25 m steps out to 7.5 m, then bisected to ~3 cm so near-equal narrows rarely
        /// tie); -1 when there is none.</summary>
        static float RowBank(in TerrainSource source, float2 p, float2 dir, float water)
        {
            for (var d = 0.25f; d <= 7.5f; d += 0.25f)
                if (source.SampleHeight(p + dir * d) > water - 0.3f)
                {
                    float lo = d - 0.25f, hi = d;
                    for (var i = 0; i < 3; i++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (source.SampleHeight(p + dir * mid) > water - 0.3f) hi = mid; else lo = mid;
                    }
                    return hi;
                }
            return -1f;
        }

        /// <summary>W-65f: the narrowest spot a stepping-stone row fits, on a 1 m world lattice within 25 m along
        /// <paramref name="flow"/> of <paramref name="xz"/>; false when none fits.</summary>
        static bool NarrowestSpot(in TerrainSource source, in FloraRules rule, float2 xz, float2 flow, out float2 bestXz,
            out float2 bestAcross, out float bestA, out float bestB, out float water)
        {
            bestXz = default; bestAcross = default; bestA = 0f; bestB = 0f; water = 0f;
            var best = float.MaxValue;
            for (var s = -25; s <= 25; s++)
            {
                var p = math.round(xz + flow * s);
                var g = source.SampleGround(p);
                if (!g.HasWater)
                    continue;
                var gAcross = math.normalizesafe(g.Flow, flow);
                gAcross = new float2(-gAcross.y, gAcross.x);
                var a = RowBank(source, p, gAcross, g.WaterSurface);
                var b = RowBank(source, p, -gAcross, g.WaterSurface);
                if (a < 0f || b < 0f)
                    continue;
                // Only spots a row fits count: a variant spans it and both of its ends land on the bank.
                var v = FloraGenerator.StoneRowVariant(a + b);
                if (v < 0 || v >= rule.Variants)
                    continue;
                var mid = p + gAcross * (a - b) * 0.5f;
                var half = gAcross * FloraGenerator.StoneRowLength(v) * 0.5f;
                if (!NearBank(source.SampleGround(mid + half), g.WaterSurface) || !NearBank(source.SampleGround(mid - half), g.WaterSurface))
                    continue;
                var w = a + b + (math.hash((int2)p) & 1023) * 1e-6f;   // a world-fixed tie-break under 1 mm
                if (w < best) { best = w; bestXz = p; bestAcross = gAcross; bestA = a; bestB = b; water = g.WaterSurface; }
            }
            return best != float.MaxValue;
        }

        /// <summary>W-65b: an end stone's spot is dry bank no more than 0.8 m above the water, or water under 0.3 m deep.</summary>
        static bool NearBank(in GroundSample g, float surface) => g.Height > surface - 0.3f && g.Height < surface + 0.8f;

        /// <summary>
        /// How far <paramref name="xz"/> is from the walk lane of the nearest dock of this landmark (#36, W-59d): the
        /// line from the foot of its plank ramp (4.6 m inland of the dock's base) out along the facing to its far end.
        /// Shore clutter (a boat pulled up, a net frame) and the biome's plants stay off it, so the lane up the ramp and
        /// down the deck is clear. float.MaxValue when the landmark has no docks.
        /// </summary>
        static float DockLaneDistance(ref FloraBlob f, in TerrainSource source, in LandmarkHit landmark, float2 xz,
                                      float limit = float.MaxValue)
        {
            ref var set = ref source.Landmarks.Value;
            var instance = set.Instances[landmark.Instance];
            var dir = math.normalizesafe(instance.Facing, new float2(1f, 0f));
            var best = float.MaxValue;
            for (int l = 0; l < 3; l++)
            {
                var placed = f.PropRanges[landmark.Type * 3 + l];
                for (int e = placed.x; e < placed.x + placed.y; e++)
                {
                    var prop = f.Props[e];
                    if (f.Rules[prop.Species].FaceLandmark != 2)
                        continue;
                    for (int k = 0; k < prop.Count; k++)
                    {
                        var spot = LandmarkSampling.ToWorld(instance, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                        // #132: DockShore only slides the spot along the facing (ShoreSlideOut out, ShoreSlideBack in), so
                        // the lane lies on that line's slid stretch: no nearer than limit means it can't decide the answer,
                        // and its shore (dozens of height samples) isn't measured.
                        if (OldWalls.Data == 0)
                        {
                            var reach = math.clamp(math.dot(xz - spot, dir), -ShoreSlideBack - DockRampRun, ShoreSlideOut + DockMaxLength);
                            if (math.distance(xz, spot + dir * reach) >= limit)
                                continue;
                        }
                        var start = DockShore(source, spot, instance.Facing).xz;
                        var along = math.clamp(math.dot(xz - start, dir), -DockRampRun, DockMaxLength);
                        best = math.min(best, math.distance(xz, start + dir * along));
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// W-65c (#36): how far <paramref name="xz"/> is from the whole deck of the nearest wharf of this landmark: its
        /// main lane, the T or L across its end and the side branches (StructureKit.Wharf builds them within about 14 m
        /// on its -X side, 7 m on its +X side, out to the lane's length plus the end walk). A box in the wharf's frame,
        /// 0 inside it. float.MaxValue when the landmark has no wharf.
        /// </summary>
        static float WharfDistance(ref FloraBlob f, in TerrainSource source, in LandmarkHit landmark, float2 xz,
                                   float limit = float.MaxValue)
        {
            ref var set = ref source.Landmarks.Value;
            var instance = set.Instances[landmark.Instance];
            var dir = math.normalizesafe(instance.Facing, new float2(1f, 0f));
            var side = new float2(dir.y, -dir.x);   // the wharf's local +X (FacingYaw turns local +Z onto the facing)
            float2 lo = new float2(-14.5f, -DockRampRun), hi = new float2(7.5f, DockMaxLength + 3.5f);
            var best = float.MaxValue;
            for (int l = 0; l < 3; l++)
            {
                var placed = f.PropRanges[landmark.Type * 3 + l];
                for (int e = placed.x; e < placed.x + placed.y; e++)
                {
                    var prop = f.Props[e];
                    if (f.Rules[prop.Species].FaceLandmark != 2 || f.Rules[prop.Species].PerVillage == 0)
                        continue;
                    for (int k = 0; k < prop.Count; k++)
                    {
                        var spot = LandmarkSampling.ToWorld(instance, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                        // #132: as in DockLaneDistance, its box slides only along the facing, so first the box stretched
                        // over that slide: no nearer than limit and the shore isn't measured.
                        if (OldWalls.Data == 0)
                        {
                            var d0 = xz - spot;
                            var local0 = new float2(math.dot(d0, side), math.dot(d0, dir));
                            var out0 = math.max(math.max(lo - new float2(0f, ShoreSlideBack) - local0,
                                                         local0 - hi - new float2(0f, ShoreSlideOut)), 0f);
                            if (math.length(out0) >= limit)
                                continue;
                        }
                        var d = xz - DockShore(source, spot, instance.Facing).xz;
                        var local = new float2(math.dot(d, side), math.dot(d, dir));
                        var outside = math.max(math.max(lo - local, local - hi), 0f);
                        best = math.min(best, math.length(outside));
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// W-65b (#36): which variant a per-village prop (a wharf) takes at landmark <paramref name="instance"/>: the
        /// village's rank among the landmarks of its type, plus a scrambled offset from the type. Ranks differ, so up to
        /// <paramref name="variants"/> villages all get different layouts (the cell's own draw gave v0 and v2 the same one).
        /// </summary>
        /// <summary>
        /// W-101: a runed spire's model: the shape the draw picked, within the run of variants for the landmark's own
        /// colour pattern (a scrambled hash of the instance, so every stone of one circle glows alike). Unchanged for a
        /// species without patterns.
        /// </summary>
        static int PatternModel(in FloraRules rule, int instance, int model)
        {
            if (rule.Patterns <= 0)
                return model;
            var shapes = math.max(1, rule.Variants / rule.Patterns);
            var pattern = (int)(BiomeSampling.Scramble((uint)instance * 0x9E3779B9u ^ 0x51ED270Bu) % (uint)rule.Patterns);
            return rule.FirstModel + pattern * shapes + (model - rule.FirstModel) % shapes;
        }

        static int VillageVariant(ref LandmarkSetBlob set, int instance, int variants)
        {
            var type = set.Instances[instance].Type;
            var rank = 0;
            for (int i = 0; i < instance; i++)
                if (set.Instances[i].Type == type)
                    rank++;
            var offset = (int)(BiomeSampling.Scramble((uint)type * 0x9E3779B9u ^ 0x3A4F0B1Du) % (uint)math.max(1, variants));
            return (rank + offset) % math.max(1, variants);
        }

        /// <summary>
        /// W-62c (#37): how far <paramref name="xz"/> is from the far end of the nearest landing pier on the island (a
        /// placed prop with a PierReach, standing at its spot and running PierReach along its landmark's facing).
        /// float.MaxValue when there is none. Only spires ask, so the scan over the landmarks is rare.
        /// </summary>
        static float PierEndDistance(ref FloraBlob f, in TerrainSource source, float2 xz)
        {
            ref var set = ref source.Landmarks.Value;
            var best = float.MaxValue;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var instance = set.Instances[i];
                if (instance.Type >= f.LandmarkTypeCount)
                    continue;
                var radius = set.Types[instance.Type].Radius * instance.Scale;
                var dir = math.normalizesafe(instance.Facing, new float2(1f, 0f));
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[instance.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        var reach = f.Rules[prop.Species].PierReach;
                        if (reach <= 0f)
                            continue;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(instance, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), radius);
                            best = math.min(best, math.distance(xz, spot + dir * reach));
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>W-62c: how far a spire stays from a landing pier's far end (m, at scale 1).</summary>
        const float PierEndClear = 6.5f;

        /// <summary>The dock's plank ramp run (StructureKit.Dock) and a length past its longest deck, for the walk lane.</summary>
        const float DockRampRun = 4.6f, DockMaxLength = 20f;
        /// <summary>How far a shore cluster (StructureKit.Shore, spread 2.5) reaches from its spot, plus half the lane (m).</summary>
        const float ShoreReach = 5.6f;
        /// <summary>W-91 (#36): how far out from its ring spot a dock is checked for river water (it slides up to 14 m
        /// out to the waterline, then its deck runs up to ~19 m further).</summary>
        const float RiverDockReach = 30f;
        /// <summary>W-109: a dock's or wharf's real run from its DockShore start: the longest deck plus a wharf's end walk.</summary>
        const float DockDeckReach = DockMaxLength + 3.5f;

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
            FloraForm.Bench or FloraForm.Log or FloraForm.Stump or FloraForm.FirePit or FloraForm.Ruin or FloraForm.RailFence or
            FloraForm.HazardShelter or FloraForm.KitBuilding or FloraForm.KitRuin => true,
            _ => false
        };

        /// <summary>Forms whose models carry perches (FloraModel.Perches), solid or not.</summary>
        static bool HasPerches(FloraSpecies s) => s.Form switch
        {
            FloraForm.Hut or FloraForm.StoneHouse or FloraForm.Tower or FloraForm.Inn or FloraForm.Smithy or FloraForm.Well or
            FloraForm.Hall or FloraForm.Shrine or FloraForm.Garden or FloraForm.Pen or FloraForm.Signpost or FloraForm.Palisade or
            FloraForm.Ruin or FloraForm.RailFence or FloraForm.HazardShelter or FloraForm.KitBuilding or FloraForm.KitRuin => true,
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
                        var building = PlotSpecies(ref f, ref towns, plot);
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

        /// <summary>
        /// Every perch within <paramref name="radius"/> of a point (world positions, where a bird's feet go): the tops of
        /// towns' buildings, fences, pens, wells and signposts, and of buildings landmarks place (ruins, camps' palisades),
        /// placed as they're drawn. Burst-safe; the list is filled until it's full. Cheap away from towns and landmarks.
        /// </summary>
        public static void NearPerches(ref FloraBlob f, in TerrainSource source, float2 near, float radius, ref FixedList512Bytes<float3> perches)
        {
            if (f.Perches.Length == 0)
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
                        var building = PlotSpecies(ref f, ref towns, plot);
                        if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].PerchCount == 0)
                            continue;
                        var cell = (int2)math.floor(plot.Centre / f.CellSizes[building.y]);
                        Perches(ref f, Place(ref f, source, cell, building.y), near, radius, ref perches);
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
                        if (prop.Count == 0 || f.Models[f.Rules[prop.Species].FirstModel].PerchCount == 0)
                            continue;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            if (math.distance(spot, near) > 15f + radius)
                                continue;
                            Perches(ref f, Place(ref f, source, (int2)math.floor(spot / f.CellSizes[l]), l), near, radius, ref perches);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// W-43c: the chimney tops (world) of the settlement building on one plot, if it has any. Placement is the
        /// same pure function the drawing uses, so the smoke sits on the drawn flue.
        /// </summary>
        public static int PlotChimneys(ref FloraBlob f, in TerrainSource source, int plotIndex, ref FixedList128Bytes<float3> tops)
        {
            tops.Clear();
            if (f.Chimneys.Length == 0 || !source.Settlements.IsCreated)
                return 0;
            ref var plot = ref source.Settlements.Value.Plots[plotIndex];
            if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length)
                return 0;
            var building = PlotSpecies(ref f, ref source.Settlements.Value, plot);
            if (building.x < 0 || building.y < 0 || f.Models[f.Rules[building.x].FirstModel].ChimneyCount == 0)
                return 0;
            var plant = Place(ref f, source, (int2)math.floor(plot.Centre / f.CellSizes[building.y]), building.y);
            if (plant.Model < 0 || plant.Model >= f.Models.Length)
                return 0;
            var shape = f.Models[plant.Model];
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            for (int i = shape.FirstChimney; i < shape.FirstChimney + shape.ChimneyCount && tops.Length < tops.Capacity; i++)
            {
                var p = f.Chimneys[i];
                var at = plant.Position.xz + new float2(p.x * c - p.z * sn, p.x * sn + p.z * c) * plant.Scale;
                tops.Add(new float3(at.x, plant.Position.y + p.y * plant.Scale, at.y));
            }
            return tops.Length;
        }

        /// <summary>A placed plant's perches near a point, in the world (see NearPerches).</summary>
        static void Perches(ref FloraBlob f, in FloraSystem.FloraInstance plant, float2 near, float radius, ref FixedList512Bytes<float3> perches)
        {
            if (plant.Model < 0 || plant.Model >= f.Models.Length)
                return;
            var shape = f.Models[plant.Model];
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            for (int i = shape.FirstPerch; i < shape.FirstPerch + shape.PerchCount; i++)
            {
                var p = f.Perches[i];
                var at = plant.Position.xz + new float2(p.x * c - p.z * sn, p.x * sn + p.z * c) * plant.Scale;
                if (math.distance(at, near) > radius)
                    continue;
                if (perches.Length == perches.Capacity)
                    return;
                perches.Add(new float3(at.x, plant.Position.y + p.y * plant.Scale, at.y));
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
        public static bool Blocked(ref FloraBlob f, in TerrainSource source, float2 xz, float radius) =>
            Blocked(ref f, source, xz, radius, out _);

        /// <summary>
        /// Blocked, and how tall what blocks it stands: its model's height times its scale, above its own base (0 when
        /// nothing does), for something deciding whether to go over it (Z-28: a worm leaping a building or a placed rock).
        /// From the same footprint placement, so it samples no more ground.
        /// </summary>
        public static bool Blocked(ref FloraBlob f, in TerrainSource source, float2 xz, float radius, out float top)
        {
            top = 0f;
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
                        var building = PlotSpecies(ref f, ref towns, plot);
                        if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].ObstacleCount == 0)
                            continue;
                        var cell = (int2)math.floor(plot.Centre / f.CellSizes[building.y]);
                        var plant = Place(ref f, source, cell, building.y, footprint: true);
                        if (Inside(ref f, plant, xz, radius))
                        {
                            top = f.Models[plant.Model].Height * plant.Scale;
                            return true;
                        }
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
                        // #132: its walls reach no further than this from its spot (any variant, at its biggest), so a
                        // spot further off can't block and skips Place, which for a house measures every dock's shore
                        // (dozens of height samples each). Same answers, a fraction of the cost in a fishing village.
                        var reach = OldWalls.Data != 0 ? float.MaxValue
                                  : WallReach(ref f, prop.Species) * math.max(prop.Scale.x, prop.Scale.y) + radius + 0.01f;
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                                LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), landmark.Radius);
                            var away = math.distance(spot, xz);
                            if (away > 15f + radius || away >= reach)
                                continue;
                            var plant = Place(ref f, source, (int2)math.floor(spot / f.CellSizes[l]), l, footprint: true);
                            if (Inside(ref f, plant, xz, radius))
                            {
                                top = f.Models[plant.Model].Height * plant.Scale;
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Scattered rocks round a point (Z-28: a worm going round or over a boulder field): whether the walls of any solid
        /// plant that isn't a tree reach it (boulders, tors, standing stones, hoodoos; in a town also its buildings), and
        /// the tallest one's top, its model's height times its scale above its own base (0 when none). Worked out from
        /// Place() on the tree layer's cells round the point, where they all grow, so it needs no streamed state and runs
        /// in the simulation world too. Each cell is a full placement: call it at a few points, not every step.
        /// </summary>
        public static bool RockAt(ref FloraBlob f, in TerrainSource source, float2 xz, float radius, out float top)
        {
            top = 0f;
            if (f.Obstacles.Length == 0)
                return false;
            // The biggest boulders' walls reach a few metres from where they're placed.
            const float reach = 6f;
            var size = f.CellSizes[TreeLayer];
            var c0 = (int2)math.floor((xz - radius - reach) / size);
            var c1 = (int2)math.floor((xz + radius + reach) / size);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var plant = Place(ref f, source, new int2(x, y), TreeLayer, footprint: true);
                if (plant.Model < 0 || plant.Model >= f.Models.Length || f.Models[plant.Model].Kind != TreeKind.None)
                    continue;
                if (Inside(ref f, plant, xz, radius))
                    top = math.max(top, f.Models[plant.Model].Height * plant.Scale);
            }
            return top > 0f;
        }

        /// <summary>
        /// #132: how far a species' walls reach from where it's placed, at scale 1, over all its variants (each wall
        /// cylinder's offset plus its radius): nothing further off is ever Inside it.
        /// </summary>
        static float WallReach(ref FloraBlob f, int species)
        {
            var rule = f.Rules[species];
            var reach = 0f;
            for (int m = rule.FirstModel; m < rule.FirstModel + math.max(rule.Variants, 1) && m < f.Models.Length; m++)
            {
                var shape = f.Models[m];
                for (int i = shape.FirstObstacle; i < shape.FirstObstacle + shape.ObstacleCount; i++)
                {
                    var col = f.Obstacles[i];
                    reach = math.max(reach, math.length(col.xy) + col.z);
                }
            }
            return reach;
        }

        /// <summary>A-70: WallReach for callers that cache placed walls (Fauna's BuildingFootprints).</summary>
        public static float WallReachOf(ref FloraBlob f, int species) => WallReach(ref f, species);

        /// <summary>A-70: Inside for callers that cache placed walls (Fauna's BuildingFootprints).</summary>
        public static bool InsideWalls(ref FloraBlob f, in FloraSystem.FloraInstance plant, float2 xz, float radius) =>
            Inside(ref f, plant, xz, radius);

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

        /// <summary>
        /// A-81 row 1: circles (x, z, radius, on) where no flora is placed, one per stone colossus slot (#40), set at
        /// runtime by StoneColossi and moved by ColossusWalker. Room for 31, so a new colossus needs no new static.
        /// </summary>
        public static readonly SharedStatic<FixedList512Bytes<float4>> ColossusKeepOut = SharedStatic<FixedList512Bytes<float4>>.GetOrCreate<ColossusKeepOutKey>();
        // Keyed apart from the old fixed four-circle statics: a live editor can't resize a SharedStatic it already made.
        class ColossusKeepOutKey { }

        /// <summary>Sets one colossus slot's keep-out circle (z radius, w on), growing the list to reach it.</summary>
        public static void SetKeepOut(int slot, float4 circle)
        {
            ref var list = ref ColossusKeepOut.Data;
            while (list.Length <= slot)
                list.Add(default);
            list[slot] = circle;
        }

        /// <summary>#132: 1 turns Blocked's wall-reach skip off (DebugControls "-oldwalls"), for A/B timing.</summary>
        public static readonly SharedStatic<int> OldWalls = SharedStatic<int>.GetOrCreate<OldWallsKey>();
        class OldWallsKey { }

        /// <summary>
        /// The plant growing in one cell of a layer's grid (Model -1 if none). With <paramref name="footprint"/>, a town's
        /// building or a landmark's placed prop comes back with only what its walls need (Position.xz, Yaw, Scale, Model,
        /// exactly as without), leaving out its floor height and ground colour, which sample the ground round it and cost
        /// most of the call (Blocked asks on every step of every animal in town). Other plants are placed in full.
        /// </summary>
        public static FloraSystem.FloraInstance Place(ref FloraBlob f, in TerrainSource source, int2 cell, int layer, bool footprint = false)
        {
            var none = new FloraSystem.FloraInstance { Model = -1 };
            var cellSize = f.CellSizes[layer];
            var rng = new Random(math.hash(new int3(cell, layer * 7919 + 13)) | 1u);
            var xz = ((float2)cell + rng.NextFloat2()) * cellSize;
            // Nothing grows through a stone colossus (#40): each one keeps a clear circle round it.
            ref var keepOut = ref ColossusKeepOut.Data;
            for (int k = 0; k < keepOut.Length; k++)
            {
                var c = keepOut[k];
                if (c.w > 0f && math.distancesq(xz, c.xy) < c.z * c.z)
                    return none;
            }
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
                        var building = PlotSpecies(ref f, ref towns, plot);
                        if (building.x < 0 || building.y != layer || math.any((int2)math.floor(plot.Centre / cellSize) != cell))
                            continue;
                        var buildingRule = f.Rules[building.x];
                        // A bridge span sits at its deck height over the water, at its own size so spans meet end to end.
                        var bridge = plot.Role == PlotRole.Bridge;
                        return new FloraSystem.FloraInstance
                        {
                            Position = new float3(plot.Centre.x, footprint ? 0f : bridge ? plot.Pad : FloorHeight(source, plot), plot.Centre.y),
                            Yaw = math.atan2(-plot.Facing.x, plot.Facing.y),
                            // Sized for its folk (the layout's BuildingScale: ogre tents bigger, goblin huts smaller); bridge
                            // spans and fence lengths at their own size, so they meet end to end.
                            Scale = bridge || plot.Role == PlotRole.Fence ? 1f : rng.NextFloat(0.95f, 1.05f) * towns.Layouts[towns.Settlements[plot.Settlement].Layout].BuildingScale,
                            Model = buildingRule.FirstModel + rng.NextInt(buildingRule.Variants),
                            Random = rng.NextFloat(),
                            Snow = bridge || footprint ? 0f : source.SampleBiomeSnow(plot.Centre),   // #82: built plots too
                            Ground = bridge || footprint ? 0u : DirtColor(source, buildingRule, plot.Centre, math.cmax(plot.Half), 0.9f)
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
            var landmarkInstance = -1;
            if (source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out var landmark) && landmark.Type < f.LandmarkTypeCount)
            {
                ref var set = ref source.Landmarks.Value;
                landmarkInstance = landmark.Instance;
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
                                if (OnBuiltPlot(ref f, source, spot, placedRule.Footprint * prop.Scale.y) ||
                                    OnRoad(source, spot, placedRule.Footprint * prop.Scale.y))
                                    return none;
                                // W-59d: a shore cluster reaches about 5 m (2.5 m spread plus half a pulled-up boat):
                                // none within that of a dock's walk lane, so nothing stands on the ramp or deck.
                                if (placedRule.ShoreKit != 0 && DockLaneDistance(ref f, source, landmark, spot, ShoreReach * prop.Scale.y) < ShoreReach * prop.Scale.y)
                                    return none;
                                // W-63 (#36): nor a house. A stilt house at -3294.1,-1653.6 stood across a dock's deck and
                                // the walker met a 0.9-1.6 m wall there (no draws moved: the cell just stays empty).
                                // W-65c: and off every branch of a wharf, not just its main lane.
                                var houseClear = placedRule.Footprint * prop.Scale.y + 1.5f;
                                if (placedRule.House != 0 && math.min(DockLaneDistance(ref f, source, landmark, spot, houseClear), WharfDistance(ref f, source, landmark, spot, houseClear)) < houseClear)
                                    return none;
                                // W-105: a stilt house stands out over the shallows from its plot: none where the bank or
                                // the water doesn't suit it (LandmarkSampling.StiltHouseFits), nor with the house on a dock's
                                // or wharf's lane (no draws moved: the cell just stays empty).
                                if (placedRule.House == 2 && !footprint &&
                                    (!LandmarkSampling.StiltHouseFits(source, set.Instances[landmark.Instance].Position, spot, out var stilt) ||
                                     math.min(DockLaneDistance(ref f, source, landmark, stilt, houseClear), WharfDistance(ref f, source, landmark, stilt, houseClear)) < houseClear))
                                    return none;
                                // W-91 (#36): houses, stilt houses, docks and wharfs stand by the sea or a lake, never in a
                                // river: a plot on river water (a dock along its whole run out) stays empty (no draws moved).
                                // W-109: the 30 m reach from the ring spot is a cautious bound; a dock or wharf it flags
                                // is still built when its real deck (from DockShore's start out to the longest deck plus
                                // the wharf's end walk) stays off the river. The island bake checks before the rough
                                // riverbanks are on (TerrainAuthoring), so a mouth that widens later took village 1's
                                // wharf at -2172,3743. Only adds: every dock or wharf the old test kept stays put.
                                if (!footprint && (placedRule.House != 0 || placedRule.FaceLandmark == 2) &&
                                    LandmarkSampling.OnRiver(source, spot, set.Instances[landmark.Instance].Facing, placedRule.FaceLandmark == 2 ? RiverDockReach : 0f) &&
                                    (placedRule.FaceLandmark != 2 ||
                                     LandmarkSampling.OnRiver(source, DockShore(source, spot, set.Instances[landmark.Instance].Facing).xz, set.Instances[landmark.Instance].Facing, DockDeckReach)))
                                    return none;
                                // W-62c: no spire near a landing pier's far end (no draws moved: the cell just stays empty).
                                if (placedRule.PierClear != 0 && PierEndDistance(ref f, source, spot) < PierEndClear * prop.Scale.y)
                                    return none;
                                var toCentre = set.Instances[landmark.Instance].Position - spot;
                                var placedYaw = prop.FaceCentre != 0 ? math.atan2(-toCentre.x, toCentre.y) : rng.NextFloat(math.PI * 2f);
                                if (placedRule.FaceLandmark != 0)
                                    placedYaw = FacingYaw(set.Instances[landmark.Instance].Facing);
                                var at = new float3(spot.x, footprint ? 0f : source.SampleHeight(spot), spot.y);
                                if (placedRule.Porch > 0f && !footprint)
                                    PorchSeat(source, spot, placedRule.Porch, ref placedYaw, ref at.y);
                                if (placedRule.FaceLandmark == 2 && !footprint)
                                    at = DockShore(source, spot, set.Instances[landmark.Instance].Facing);
                                var placedScale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y));
                                var placedModel = placedRule.FirstModel + rng.NextInt(placedRule.Variants);
                                if (placedRule.PerVillage != 0)
                                    placedModel = placedRule.FirstModel + VillageVariant(ref set, landmark.Instance, placedRule.Variants);
                                if (placedRule.Patterns > 0)
                                    placedModel = PatternModel(placedRule, landmark.Instance, placedModel);
                                return new FloraSystem.FloraInstance
                                {
                                    Position = at,
                                    Yaw = placedYaw,
                                    Scale = placedScale,
                                    Model = placedModel,
                                    Random = rng.NextFloat(),
                                    Snow = footprint ? 0f : source.SampleBiomeSnow(spot),   // #82: stalls on a snowy square
                                    Ground = footprint ? 0u : GroundFor(source, placedRule, spot, 1f, 0.25f)
                                };
                            }
                            blocked |= math.distance(xz, spot) < prop.Clearance;
                        }
                    }
                }
                if (blocked)
                    return none;
                // W-59d: the biome's plants (bushes, rocks) stay off the docks' walk lanes too.
                if (DockLaneDistance(ref f, source, landmark, xz, 2f) < 2f)
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
                            if (OnBuiltPlot(ref f, source, centre, centreRule.Footprint * prop.Scale.y))
                                return none;
                            if (centreRule.FaceLandmark != 0)
                                centreYaw = FacingYaw(set.Instances[landmark.Instance].Facing);
                            return new FloraSystem.FloraInstance
                            {
                                Position = new float3(centre.x, footprint ? 0f : source.SampleHeight(centre), centre.y),
                                Yaw = centreYaw,
                                Scale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y)),
                                Model = PatternModel(centreRule, landmark.Instance, centreRule.FirstModel + rng.NextInt(centreRule.Variants)),
                                Random = rng.NextFloat(),
                                Snow = footprint ? 0f : source.SampleBiomeSnow(centre),   // #82: the camp's big tent
                                Ground = footprint ? 0u : GroundFor(source, centreRule, centre, 2f, 0.5f)
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
                    // Floating plants drift away in a current: they keep to still water, never a river (F-24).
                    if (r.Floats != 0 && (ground.River > 0.02f || math.lengthsq(ground.Flow) > 1e-4f))
                        fw = 0f;
                    if (r.MinRapids > 0f)
                        fw *= math.smoothstep(r.MinRapids - 0.1f, r.MinRapids + 0.1f, ground.Rapids);
                    // W-63 river dressing: only on its kind of river spot (asked only once the rest allows it).
                    if (r.RiverSpot != 0 && fw > 0f)
                        fw *= RiverFit(source, xz, ground, r.RiverSpot);
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
            if (OnBuiltPlot(ref f, source, xz, rule.Footprint * scale))
                return none;
            var fromProp = picked != candidates[pick];   // a landmark prop's density ring (its runed stones share a pattern)
            if (rule.RiverSpot == (int)RiverSpot.LogNarrows)
            {
                // W-63: a fallen log lies across the river (its X across the flow), both ends on dry bank no more than
                // 1.2 m apart in height; it rests just under the lower bank's top.
                var flow = math.normalizesafe(ground.Flow, new float2(0f, 1f));
                yaw = math.atan2(flow.x, -flow.y) + rng.NextFloat(-0.2f, 0.2f);
                var reach = new float2(math.cos(yaw), math.sin(yaw)) * rule.Span * scale;
                var endA = source.SampleGround(xz + reach);
                var endB = source.SampleGround(xz - reach);
                if (!Dry(endA) || !Dry(endB) || math.abs(endA.Height - endB.Height) > 1.2f)
                    return none;
                return new FloraSystem.FloraInstance
                {
                    Position = new float3(xz.x, math.min(endA.Height, endB.Height) - 0.12f, xz.y),
                    Yaw = yaw,
                    Scale = scale,
                    Model = rule.FirstModel + rng.NextInt(rule.Variants),
                    Random = rng.NextFloat(),
                    Ground = GroundFor(source, rule, xz, 1f, 0.25f)
                };
            }
            if (rule.RiverSpot == (int)RiverSpot.Narrows && rule.Span > 0f)
            {
                // W-65b: a stepping-stone row crosses the river (its X across the flow), both end stones on or just off
                // the bank; the model's origin sits on the water surface, so every top is 4-12 cm above the water.
                var flow = math.normalizesafe(ground.Flow, new float2(0f, 1f));
                yaw = math.atan2(flow.x, -flow.y) + rng.NextFloat(-0.15f, 0.15f);
                // W-65d: the row fits the narrows it is in (a fixed 12 m row found no narrows on either island): walk
                // out each way to the first bank (or water under 0.3 m), take the variant whose length (4-14 m, every 2 m,
                // FloraGenerator.StoneRowLength) just spans it and centre the row between the two banks.
                if (!ground.HasWater)
                    return none;
                // W-65f: one row per narrows. Every spot on a 1 m world lattice along the flow within 25 m is measured
                // the same way (its own flow, no jitter) and only the narrowest one gets a row; so rows are at least ~25 m apart and each sits where the river is tightest. Every cell
                // whose roll lands a row in this stretch makes the same row (spot, yaw, size and look come from the spot,
                // not the cell's draws), so the copies coincide exactly and show as one.
                // W-65g: windows of nearby cells differ, so they can pick neighbouring spots (rows 1.3 m apart); keep a
                // spot only when it is also the narrowest in its own window (two such spots are then 25 m+ apart).
                if (!NarrowestSpot(source, rule, xz, flow, out var bestXz, out var bestAcross, out var bestA, out var bestB, out var water) ||
                    !NarrowestSpot(source, rule, bestXz, new float2(bestAcross.y, -bestAcross.x), out var againXz, out _, out _, out _, out _) ||
                    math.distancesq(againXz, bestXz) > 0.25f)
                    return none;
                var across = bestAcross;
                float outA = bestA, outB = bestB;
                xz = bestXz + across * (outA - outB) * 0.5f;
                var spotRng = Random.CreateFromIndex(math.hash((int2)math.round(bestXz * 4f)) | 1u);
                yaw = math.atan2(across.y, across.x) + spotRng.NextFloat(-0.15f, 0.15f);
                scale = 1f;
                var variant = FloraGenerator.StoneRowVariant((outA + outB) / scale);
                if (variant < 0 || variant >= rule.Variants)
                    return none;
                var reach = across * FloraGenerator.StoneRowLength(variant) * 0.5f * scale;
                var endA = source.SampleGround(xz + reach);
                var endB = source.SampleGround(xz - reach);
                if (!NearBank(endA, water) || !NearBank(endB, water))
                    return none;
                rng.NextInt(rule.Variants);   // the draw the fixed row's variant took, so the rest of the cell stays put
                return new FloraSystem.FloraInstance
                {
                    Position = new float3(xz.x, water, xz.y),
                    Yaw = yaw,
                    Scale = scale,
                    Model = rule.FirstModel + variant,
                    Random = spotRng.NextFloat(),
                    Ground = GroundFor(source, rule, xz, 1f, 0.25f)
                };
            }
            if (rule.RiverSpot == (int)RiverSpot.GravelBar)
            {
                // Driftwood lies roughly along the flow, as the water left it.
                var flow = math.normalizesafe(ground.Flow, new float2(0f, 1f));
                yaw = math.atan2(flow.y, flow.x) + rng.NextFloat(-0.6f, 0.6f);
            }
            if (rule.Span > 0f)
            {
                // Both feet on ground within a metre of the middle's, so neither hangs in the air (models face +Z, so
                // their X turns to (cos, sin)).
                var foot = new float2(math.cos(yaw), math.sin(yaw)) * rule.Span * scale;
                if (math.abs(source.SampleHeight(xz + foot) - ground.Height) > 1f || math.abs(source.SampleHeight(xz - foot) - ground.Height) > 1f)
                    return none;
            }
            var baseHeight = rule.Floats != 0 && ground.HasWater ? ground.WaterSurface + 0.01f : ground.Height;
            if (rule.Tilt > 0f)
            {
                // Rocks sink by how much the ground falls away all round (a peak or a ridge's crest), which tilting onto
                // the slope can't follow, so their sides don't hang in the air (F-49). On an even slope this is 0.
                var ring = rule.Footprint * scale * 2f;
                var mean = 0f;
                for (int k = 0; k < 6; k++)
                {
                    var a = k * math.PI / 3f;
                    mean += source.SampleHeight(xz + new float2(math.cos(a), math.sin(a)) * ring);
                }
                baseHeight -= math.clamp(ground.Height - mean / 6f, 0f, rule.Footprint * scale * 1.2f);
            }
            return new FloraSystem.FloraInstance
            {
                Position = new float3(xz.x, baseHeight, xz.y),
                Yaw = yaw,
                // Plants grow smaller towards the top of their range (a tree line).
                Scale = scale * (0.55f + 0.45f * shrink[pick]),
                Model = fromProp && landmarkInstance >= 0 ? PatternModel(rule, landmarkInstance, rule.FirstModel + rng.NextInt(rule.Variants))
                                                          : rule.FirstModel + rng.NextInt(rule.Variants),
                Random = rng.NextFloat(),
                Snow = surface.Snow,
                // Rocks lie over onto the slope they stand on.
                Tilt = normal.xz * rule.Tilt,
                Ground = GroundFor(source, rule, xz, 1f, 0.25f)
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
