using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds the anchor blob after caves (and, later, settlements) are baked. Everything is derived from the seed and
    /// the baked shapes, so every machine builds the same anchors.
    ///
    /// Caves: every chamber tall enough gets a Roost on its ceiling (bats hang there; more fit in bigger chambers), a
    /// Nest on a wall ledge, and, where the style's floor is soft, a Burrow at the foot of a wall. Near each mouth, in
    /// the daylight, a Nest on the wall (swallows, wrens). All cave anchors are sheltered.
    /// </summary>
    public static class AnchorBaking
    {
        const float GridCell = 64f;

        public static BlobAssetReference<AnchorSetBlob> Build(in TerrainSource source, IReadOnlyList<LandmarkType> types, Allocator allocator)
        {
            var anchors = new List<Anchor>();
            if (source.Caves.IsCreated)
            {
                AddCaveAnchors(source, CaveStyleTable.Collect(types), anchors);
                AddGlowAnchors(source, CaveFloraTable.Collect(types), anchors);
            }
            if (source.Settlements.IsCreated)
                AddTownAnchors(source, types, anchors);
            if (source.Landmarks.IsCreated)
                AddHiveAnchors(source, types, anchors);
            if (anchors.Count == 0)
                return default;
            return BuildBlob(anchors, allocator);
        }

        /// <summary>
        /// Town anchors, owned by their settlement: a Door just in front of every building on a plot (facing out, so
        /// residents can come and go through it), a Seat before the leader's hall, an Altar facing each shrine, a Bench in
        /// each garden, and a Well at each well or fountain the landmark places.
        /// </summary>
        static void AddTownAnchors(in TerrainSource source, IReadOnlyList<LandmarkType> types, List<Anchor> anchors)
        {
            ref var set = ref source.Settlements.Value;
            var src = source;
            var buildings = SettlementBaking.Buildings(types);
            void Add(AnchorKind kind, float2 at, float2 facing, float size, int capacity, int settlement) =>
                AddUp(kind, at, 0f, facing, size, capacity, settlement);
            // At a height above the ground there (a seat).
            void AddUp(AnchorKind kind, float2 at, float up, float2 facing, float size, int capacity, int settlement)
            {
                anchors.Add(new Anchor
                {
                    Position = new float3(at.x, src.SampleHeight(at) + up, at.y),
                    Facing = facing,
                    Size = size,
                    Owner = Anchor.OwnerSettlement(settlement),
                    Kind = kind,
                    Capacity = (byte)capacity,
                    Biome = (byte)Dominant(src.SampleGround(at).Biomes),
                    Tags = 0
                });
            }
            // Seats round a fountain's rim (0.7 m up at scale 1), facing out.
            void RimSeats(float2 centre, Punchy.Flora.FloraSpecies fountain, float scale, int settlement)
            {
                var rim = ((fountain.Height.x + fountain.Height.y) * 0.25f * fountain.Spread + 0.1f) * scale;
                for (int k = 0; k < 4; k++)
                {
                    var a = k * math.PI * 0.5f + 0.4f;
                    var dir = new float2(math.cos(a), math.sin(a));
                    AddUp(AnchorKind.Sit, centre + dir * rim, 0.7f * scale, dir, 0.6f, 2, settlement);
                }
            }
            // Gates: where each road leaves a settlement (by its signpost, which stands outside the town's own plots).
            for (int p = 0; p < set.Plots.Length; p++)
            {
                var plot = set.Plots[p];
                if (plot.Role == PlotRole.Signpost)
                    Add(AnchorKind.Gate, plot.Centre + plot.Facing * 2f, plot.Facing, 1.5f, 0, plot.Settlement);
            }
            for (int t = 0; t < set.Settlements.Length; t++)
            {
                var town = set.Settlements[t];
                for (int p = town.FirstPlot; p < town.FirstPlot + town.PlotCount; p++)
                {
                    var plot = set.Plots[p];
                    // Plots without a building are left green (and only greens use that).
                    if (plot.Building < 0 && plot.Role != PlotRole.Landscaping)
                        continue;
                    // The building's front is about three quarters of the way to the plot's street edge.
                    var door = plot.Centre + plot.Facing * (plot.Half.y * 0.75f + 0.6f);
                    switch (plot.Role)
                    {
                        case PlotRole.Well:
                        {
                            Add(AnchorKind.Well, plot.Centre + plot.Facing * (plot.Half.y + 0.6f), -plot.Facing, 0.8f, 2, t);
                            var feature = plot.Building < buildings.Count ? buildings[plot.Building].Species : null;
                            if (feature != null && feature.Form == Punchy.Flora.FloraForm.Fountain)
                                RimSeats(plot.Centre, feature, set.Layouts[town.Layout].BuildingScale, t);
                            break;
                        }
                        case PlotRole.Landscaping:
                        {
                            // Open greens only: fenced kitchen gardens and pens aren't for lounging in.
                            var green = plot.Building >= 0 && plot.Building < buildings.Count ? buildings[plot.Building].Species : null;
                            if (green == null || green.Form != Punchy.Flora.FloraForm.Garden && green.Form != Punchy.Flora.FloraForm.Pen)
                                Add(AnchorKind.Green, plot.Centre, plot.Facing, math.cmin(plot.Half), 4, t);
                            break;
                        }
                        case PlotRole.Shrine:
                            // Standing before the altar, facing it.
                            Add(AnchorKind.Altar, plot.Centre + plot.Facing * 1.6f, -plot.Facing, 0.8f, 2, t);
                            break;
                        case PlotRole.Seat:
                        {
                            Add(AnchorKind.Door, door + plot.Facing * 0.8f, plot.Facing, 0.7f, 1, t);
                            Add(AnchorKind.Seat, door + plot.Facing * 2.5f, plot.Facing, 0.6f, 1, t);
                            // The hall's lower door step (see FloraGenerator.Hall: 0.4 m up at scale 1, 0.8 m deep and
                            // 3.2 m wide, starting 0.35 m out from the front wall, half the hall's depth being 0.275 of
                            // height × spread): a place either side of the way in, facing out.
                            var hall = plot.Building < buildings.Count ? buildings[plot.Building].Species : null;
                            if (hall != null && hall.Form == Punchy.Flora.FloraForm.Hall)
                            {
                                var across = new float2(-plot.Facing.y, plot.Facing.x);
                                var scale = set.Layouts[town.Layout].BuildingScale;
                                var front = (hall.Height.x + hall.Height.y) * 0.5f * hall.Spread * 0.275f;
                                var hallBase = Punchy.Flora.FloraPlacement.FloorHeight(src, plot);
                                for (int side = -1; side <= 1; side += 2)
                                {
                                    // The step stands on the hall's base height, not the ground in front of it.
                                    var at = plot.Centre + plot.Facing * (front + 0.7f) * scale + across * side * 1.1f * scale;
                                    AddUp(AnchorKind.Sit, at, hallBase + 0.4f * scale - src.SampleHeight(at), plot.Facing, 0.6f, 1, t);
                                }
                            }
                            break;
                        }
                        default:
                            Add(AnchorKind.Door, door, plot.Facing, 0.6f, 1, t);
                            break;
                    }
                }
                // Wells and fountains: wherever the landmark type places one.
                ref var inst = ref source.Landmarks.Value.Instances[town.Landmark];
                var type = inst.Type < types.Count ? types[inst.Type] : null;
                if (type == null)
                    continue;
                foreach (var prop in type.Props)
                {
                    if (prop.Species == null)
                        continue;
                    // Seats the landmark places (benches round the square, logs round a camp's yard): one Sit each, on
                    // the seat, facing the way the prop faces (towards the middle).
                    var seat = prop.Species.Form switch
                    {
                        Punchy.Flora.FloraForm.Bench => Punchy.Flora.FloraGenerator.BenchSeat,
                        Punchy.Flora.FloraForm.Log => Punchy.Flora.FloraGenerator.LogSeat,
                        Punchy.Flora.FloraForm.Stump => Punchy.Flora.FloraGenerator.StumpSeat,
                        _ => 0f
                    };
                    if (seat > 0f)
                    {
                        for (int k = 0; k < prop.Count; k++)
                        {
                            var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), town.Radius);
                            var toCentre = math.normalizesafe(inst.Position - spot);
                            AddUp(AnchorKind.Sit, spot, seat * prop.Scale.x, prop.FaceCentre ? toCentre : -toCentre, 0.6f,
                                  prop.Species.Form == Punchy.Flora.FloraForm.Stump ? 1 : 2, t);
                        }
                        continue;
                    }
                    if (prop.Species.Form == Punchy.Flora.FloraForm.Fountain && prop.Centrepiece)
                        RimSeats(inst.Position, prop.Species, prop.Scale.x, t);
                    // A fire pit at the heart of a town: a hearth to gather round, a pace out from its stones.
                    if (prop.Species.Form == Punchy.Flora.FloraForm.FirePit && prop.Centrepiece)
                    {
                        var ring = (prop.Species.Height.x + prop.Species.Height.y) * 0.5f * prop.Scale.x;
                        Add(AnchorKind.Hearth, inst.Position, inst.Facing, ring + 1f, 6, t);
                    }
                    if (prop.Species.Form != Punchy.Flora.FloraForm.Well && prop.Species.Form != Punchy.Flora.FloraForm.Fountain)
                        continue;
                    if (prop.Centrepiece)
                        Add(AnchorKind.Well, inst.Position + inst.Facing * 1.6f, -inst.Facing, 0.8f, 2, t);
                    for (int k = 0; k < prop.Count; k++)
                    {
                        var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), town.Radius);
                        var toCentre = math.normalizesafe(inst.Position - spot);
                        Add(AnchorKind.Well, spot + toCentre * 1.4f, -toCentre, 0.6f, 2, t);
                    }
                }
            }
        }

        /// <summary>
        /// A Hive anchor at every wild bees' comb: round the trunk of each hive tree a landmark has as its centrepiece or
        /// places at a spot on its ring (oak groves, ancient trees, fairy rings), at the comb's height, owned by the landmark. The comb faces some way round the trunk, so the anchor reaches round
        /// it.
        /// </summary>
        static void AddHiveAnchors(in TerrainSource source, IReadOnlyList<LandmarkType> types, List<Anchor> anchors)
        {
            ref var set = ref source.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var type = inst.Type < types.Count ? types[inst.Type] : null;
                if (type == null)
                    continue;
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                foreach (var prop in type.Props)
                {
                    var tree = prop.Species;
                    if (tree == null || tree.Form != Punchy.Flora.FloraForm.HiveTree || !prop.Centrepiece && prop.Count == 0)
                        continue;
                    var scale = (prop.Scale.x + math.max(prop.Scale.x, prop.Scale.y)) * 0.5f * (tree.SizeRange.x + tree.SizeRange.y) * 0.5f;
                    var trunk = (tree.Height.x + tree.Height.y) * 0.5f * tree.Thickness * scale;
                    // The centrepiece stands at the middle; placed props at their spots round the ring.
                    var count = prop.Centrepiece ? 1 : prop.Count;
                    for (int k = 0; k < count; k++)
                    {
                        var at = prop.Centrepiece ? inst.Position
                            : LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), radius);
                        var ground = source.SampleHeight(at);
                        anchors.Add(new Anchor
                        {
                            Position = new float3(at.x, ground + Punchy.Flora.FloraGenerator.HiveHeight(tree) * scale, at.y),
                            Facing = inst.Facing,
                            Size = trunk + 1f,
                            Owner = Anchor.OwnerLandmark(i),
                            Kind = AnchorKind.Hive,
                            Capacity = 12,
                            Biome = (byte)Dominant(source.SampleGround(at).Biomes),
                            Tags = 0
                        });
                    }
                }
            }
        }

        /// <summary>
        /// A Glow anchor at every glowing cave plant (creatures drawn to light gather there), and a Nest anchor at every
        /// clutch of eggs, owned by its cave.
        /// </summary>
        static void AddGlowAnchors(in TerrainSource source, List<Punchy.Flora.FloraSpecies> flora, List<Anchor> anchors)
        {
            ref var set = ref source.Caves.Value;
            for (int i = 0; i < set.Plants.Length; i++)
            {
                var plant = set.Plants[i];
                if (plant.Species < 0 || plant.Species >= flora.Count)
                    continue;
                var species = flora[plant.Species];
                // A clutch of eggs is a nest on the floor (spiders, lizards and salamanders tend it), one creature at a time.
                var eggs = species.Form == Punchy.Flora.FloraForm.Eggs;
                if (!eggs && species.Finish != Punchy.Flora.FoliageFinish.Glowing && species.Finish != Punchy.Flora.FoliageFinish.Glass &&
                    !species.GlowingAccents)
                    continue;
                CaveSampling.Distance(ref set, plant.Position + new float3(0f, 0.5f, 0f), out var cave);
                if (cave < 0)
                    continue;
                anchors.Add(new Anchor
                {
                    Position = plant.Position,
                    Facing = new float2(math.cos(plant.Yaw), math.sin(plant.Yaw)),
                    Size = eggs ? 0.6f : 1.5f,
                    Owner = Anchor.OwnerCave(cave),
                    Kind = eggs ? AnchorKind.Nest : AnchorKind.Glow,
                    Capacity = (byte)(eggs ? 1 : 0),
                    Biome = (byte)Dominant(source.SampleGround(plant.Position.xz).Biomes),
                    Tags = (byte)(Anchor.Shelter | (set.Caves[cave].Style & 0x7F))
                });
            }
        }

        static void AddCaveAnchors(in TerrainSource source, List<CaveStyle> styles, List<Anchor> anchors)
        {
            ref var set = ref source.Caves.Value;
            var terrain = source;   // local functions can't capture an in parameter
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                var style = cave.Style > 0 && cave.Style - 1 < styles.Count ? styles[cave.Style - 1] : null;
                var tags = (byte)(Anchor.Shelter | (cave.Style & 0x7F));
                var rng = new Random(math.hash(new int2(c, 0x5EED)) | 1u);
                var owner = Anchor.OwnerCave(c);

                Anchor Make(AnchorKind kind, float3 position, float2 facing, float size, int capacity) => new Anchor
                {
                    Position = position,
                    Facing = math.normalizesafe(facing, new float2(1f, 0f)),
                    Size = size,
                    Owner = owner,
                    Kind = kind,
                    Capacity = (byte)math.clamp(capacity, 0, 255),
                    Biome = (byte)Dominant(terrain.SampleGround(position.xz).Biomes),
                    Tags = tags
                };

                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var centre = set.Nodes[n].Position;
                    var radius = set.Nodes[n].Radius;
                    var floor = source.FloorUnder(centre, out var inCave);
                    if (!inCave || source.CaveDistance(centre) >= 0f)
                        continue;
                    var ceiling = source.CeilingOver(centre);
                    // The way into the chamber: from the node before it, so anchors face along the cave.
                    var along = n > cave.FirstNode ? (centre - set.Nodes[n - 1].Position).xz : new float2(1f, 0f);

                    if (ceiling - floor > 2.5f)
                        anchors.Add(Make(AnchorKind.Roost, new float3(centre.x, ceiling, centre.z), along, radius * 0.5f, (int)math.round(radius * 3f)));

                    // A ledge part-way up a wall, and the foot of a wall, in random directions from the middle.
                    var dir = Direction(ref rng);
                    if (WallPoint(source, new float3(centre.x, math.lerp(floor, ceiling, 0.65f), centre.z), dir, out var ledge))
                        anchors.Add(Make(AnchorKind.Nest, ledge, -dir, 0.4f, 1));
                    // Mines: an ore face to work at in every chamber (miners stand a pace off the wall, facing it).
                    if (style != null && style.Frame != null)
                    {
                        var face = Direction(ref rng);
                        if (WallPoint(source, new float3(centre.x, floor + 1f, centre.z), face, out var ore))
                        {
                            var stand = ore - new float3(face.x, 0f, face.y) * 0.8f;
                            var standFloor = source.FloorUnder(stand + new float3(0f, 0.5f, 0f), out _);
                            anchors.Add(Make(AnchorKind.Work, new float3(stand.x, standFloor + 0.3f, stand.z), face, 1f, 2));
                        }
                    }
                    if (style == null || style.Burrows)
                    {
                        dir = Direction(ref rng);
                        if (WallPoint(source, new float3(centre.x, floor + 0.4f, centre.z), dir, out var foot))
                        {
                            var ground = source.FloorUnder(foot + new float3(0f, 0.3f, 0f), out _);
                            anchors.Add(Make(AnchorKind.Burrow, new float3(foot.x, ground, foot.z), -dir, 0.6f, rng.NextInt(1, 4)));
                        }
                    }
                }

                // Drinking spots: round each water pool's rim and along a stream's banks, facing the water. None at lava.
                for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                {
                    var pool = set.Pools[i];
                    if (pool.Lava > 0.5f)
                        continue;
                    var start = rng.NextFloat(math.PI * 2f);
                    for (int k = 0; k < 3; k++)
                    {
                        var a = start + k * math.PI * 2f / 3f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var spot = pool.Centre.xz + dir * (pool.Radius * 1.2f + 0.3f);
                        var ground = source.FloorUnder(new float3(spot.x, pool.Centre.y + 1f, spot.y), out var inCave);
                        if (inCave && source.CaveDistance(new float3(spot.x, ground + 0.5f, spot.y)) < -0.3f)
                            anchors.Add(Make(AnchorKind.Pool, new float3(spot.x, ground, spot.y), -dir, 0.8f, 3));
                    }
                }
                for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
                {
                    var stream = set.Streams[s];
                    for (int k = 2; k < stream.PointCount - 1; k += 5)
                    {
                        var p = set.StreamPoints[stream.FirstPoint + k];
                        var next = set.StreamPoints[stream.FirstPoint + k + 1].Position;
                        var along = math.normalizesafe((next - p.Position).xz, new float2(1f, 0f));
                        var bank = (rng.NextBool() ? 1f : -1f) * new float2(-along.y, along.x);
                        var spot = p.Position.xz + bank * (p.Width * 0.65f + 0.3f);
                        var ground = source.FloorUnder(new float3(spot.x, p.Position.y + 1f, spot.y), out var inCave);
                        if (inCave && source.CaveDistance(new float3(spot.x, ground + 0.5f, spot.y)) < -0.3f)
                            anchors.Add(Make(AnchorKind.Pool, new float3(spot.x, ground, spot.y), -bank, 0.8f, 2));
                    }
                }

                // Mines: a work spot just inside the mouth, facing out (where ore is carried out and timber in).
                if (style != null && style.Frame != null && cave.NodeCount > 1)
                {
                    var inside = set.Nodes[cave.FirstNode + 1].Position;
                    var insideFloor = source.FloorUnder(inside + new float3(0f, 0.5f, 0f), out var underRock);
                    if (underRock)
                        anchors.Add(Make(AnchorKind.Work, new float3(inside.x, insideFloor + 0.3f, inside.z), (cave.Mouth - inside).xz, 1.5f, 3));
                }

                // A nest on the wall just inside the mouth, where the daylight still reaches.
                var mouth = cave.Mouth;
                var inward = cave.NodeCount > 1 ? math.normalizesafe((set.Nodes[cave.FirstNode + 1].Position - mouth).xz) : new float2(1f, 0f);
                var side = rng.NextBool() ? new float2(-inward.y, inward.x) : new float2(inward.y, -inward.x);
                var at = mouth + new float3(inward.x, 0f, inward.y) * 3f + new float3(0f, 1.5f, 0f);
                if (source.CaveDistance(at) < 0f && WallPoint(source, at, side, out var mouthNest))
                    anchors.Add(Make(AnchorKind.Nest, mouthNest, -side, 0.4f, 1));
            }
        }

        /// <summary>The wall reached by going horizontally from a point in cave air, pulled back 0.3 m into the air.</summary>
        static bool WallPoint(in TerrainSource source, float3 from, float2 dir, out float3 wall)
        {
            var d3 = new float3(dir.x, 0f, dir.y);
            float t = 0f, previous = 0f;
            for (int i = 0; i < 100; i++)
            {
                var d = source.CaveDistance(from + d3 * t);
                if (d >= -0.3f)
                {
                    // Wall noise can carry a step past the mark; settle back between the last two steps.
                    for (int k = 0; k < 8; k++)
                    {
                        var mid = (previous + t) * 0.5f;
                        if (source.CaveDistance(from + d3 * mid) >= -0.3f) t = mid; else previous = mid;
                    }
                    wall = from + d3 * previous;
                    return previous > 0.5f;
                }
                previous = t;
                t += math.max(0.1f, -d * 0.8f);
            }
            wall = default;
            return false;
        }

        static float2 Direction(ref Random rng)
        {
            var a = rng.NextFloat(math.PI * 2f);
            return new float2(math.cos(a), math.sin(a));
        }

        static int Dominant(in BiomeWeights b)
        {
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (b.Weights[k] > b.Weights[best])
                    best = k;
            return b.Ids[best];
        }

        static BlobAssetReference<AnchorSetBlob> BuildBlob(List<Anchor> anchors, Allocator allocator)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<AnchorSetBlob>();
            var anchorArray = builder.Allocate(ref root.Anchors, anchors.Count);
            float2 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                anchorArray[i] = anchors[i];
                min = math.min(min, anchors[i].Position.xz);
                max = math.max(max, anchors[i].Position.xz);
            }
            root.GridOrigin = min;
            root.CellSize = GridCell;
            var size = (int2)math.floor((max - min) / GridCell) + 1;
            root.GridSize = size;
            var cells = new List<int>[size.x * size.y];
            for (int i = 0; i < anchors.Count; i++)
            {
                var c = (int2)math.floor((anchors[i].Position.xz - min) / GridCell);
                (cells[c.y * size.x + c.x] ??= new List<int>()).Add(i);
            }
            var cellArray = builder.Allocate(ref root.Cells, cells.Length);
            var items = new List<int>();
            for (int c = 0; c < cells.Length; c++)
            {
                cellArray[c] = new int2(items.Count, cells[c]?.Count ?? 0);
                if (cells[c] != null)
                    items.AddRange(cells[c]);
            }
            var itemArray = builder.Allocate(ref root.Items, items.Count);
            for (int i = 0; i < items.Count; i++)
                itemArray[i] = items[i];
            var blob = builder.CreateBlobAssetReference<AnchorSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }
    }
}
