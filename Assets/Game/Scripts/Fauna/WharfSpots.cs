using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>What a fishing folk does at a wharf spot (A-73 row 3).</summary>
    public enum WharfSpotKind : byte
    {
        None,
        /// <summary>The far end of the main lane, looking out to sea: fishing.</summary>
        End,
        /// <summary>A branch tip, the loop walkway or the T's arm: fishing off the side.</summary>
        Side,
        /// <summary>By the crates near the lane's start: mending nets.</summary>
        Nets,
        /// <summary>Where a crate carried out from the shore is set down.</summary>
        Crate,
    }

    /// <summary>One spot on a wharf, in the wharf model's own frame (unturned, unscaled).</summary>
    public struct WharfSpot
    {
        public float2 At;
        /// <summary>The way it faces there (radians, model frame; forward is (sin, cos)).</summary>
        public float Yaw;
        public WharfSpotKind Kind;
        /// <summary>Its walk out, in WharfSpotSet.Points: from the ramp foot on the shore, corner to corner, to At.</summary>
        public int2 Path;
    }

    /// <summary>
    /// A-73: where fishing folk can stand on each wharf model, and how to walk there along the deck. Creatures walk on
    /// the terrain's floor, which knows nothing of props, and the kit's support grids exist only near players, so the
    /// deck is measured here once, from the wharf's own kit boxes (FloraModel.Boxes, the same art the player stands on),
    /// and the walk follows these paths at the deck's height. Gull perches on the rail posts come from the same boxes.
    /// Built by FaunaDirector beside the flora tables; not part of the fauna fingerprint (it's derived from the art).
    /// </summary>
    public struct WharfSpotSet
    {
        /// <summary>Per flora model (FloraBlob.Models order): its spots (start, count); (0, 0) for everything but wharves.</summary>
        public BlobArray<int2> ModelSpots;
        public BlobArray<WharfSpot> Spots;
        public BlobArray<float2> Points;
        /// <summary>Per flora model: its rail-post tops (start, count) in Perches.</summary>
        public BlobArray<int2> ModelPerches;
        public BlobArray<float3> Perches;

        /// <summary>The deck's height above the wharf's base at a model-frame point: up the shore treads, then level.</summary>
        public static float DeckHeight(float2 local) => math.saturate((local.y + RampRun) / RampRun) * Deck;

        /// <summary>A model-frame point placed in the world (models turn model +Z onto (-sin yaw, cos yaw)).</summary>
        public static float2 ToWorld(float2 local, float3 origin, float yaw, float scale)
        {
            float c = math.cos(yaw), s = math.sin(yaw);
            return origin.xz + new float2(local.x * c - local.y * s, local.x * s + local.y * c) * scale;
        }

        /// <summary>A model-frame heading as a world one.</summary>
        public static float ToWorldYaw(float localYaw, float yaw) => localYaw - yaw;

        /// <summary>The kit's deck top (StructureKit.Wharf) and how far back up the shore its treads start.</summary>
        public const float Deck = 1f, RampRun = 6.5f;
    }

    /// <summary>The wharf spots, for the animals' world (on the Flora Lookup entity).</summary>
    public struct WharfSpotLookup : IComponentData
    {
        public BlobAssetReference<WharfSpotSet> Blob;
    }

    /// <summary>Measures every wharf model's deck into a WharfSpotSet.</summary>
    public static class WharfSpots
    {
        const float Cell = 0.25f;
        const int MaxFishing = 6;

        struct Built
        {
            public List<WharfSpot> Spots;
            public List<float2> Points;
            public List<float3> Perches;
        }

        public static BlobAssetReference<WharfSpotSet> Build(List<FloraSpecies> species, BlobAssetReference<FloraBlob> flora,
                                                             Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>> grown)
        {
            ref var f = ref flora.Value;
            var models = f.Models.Length;
            var spots = new List<WharfSpot>();
            var points = new List<float2>();
            var perches = new List<float3>();
            var modelSpots = new int2[models];
            var modelPerches = new int2[models];
            for (int i = 0; i < species.Count && i < f.Rules.Length; i++)
            {
                var s = species[i];
                if (s == null || s.Form != FloraForm.KitBuilding || s.KitShape != Punchy.Terrain.KitShape.Wharf)
                    continue;
                for (int v = 0; v < f.Rules[i].Variants; v++)
                {
                    var m = f.Rules[i].FirstModel + v;
                    if (m >= models)
                        continue;
                    FloraModel model = null;
                    if (grown != null && grown.TryGetValue((s, v), out var task) && task.IsCompletedSuccessfully)
                        model = task.Result;
                    model ??= FloraGenerator.Grow(s, v, shapeOnly: true);
                    var start = spots.Count;
                    var perchStart = perches.Count;
                    Measure(model, spots, points, perches);
                    modelSpots[m] = new int2(start, spots.Count - start);
                    modelPerches[m] = new int2(perchStart, perches.Count - perchStart);
                }
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<WharfSpotSet>();
            Fill(ref builder, ref root.ModelSpots, modelSpots);
            Fill(ref builder, ref root.Spots, spots.ToArray());
            Fill(ref builder, ref root.Points, points.ToArray());
            Fill(ref builder, ref root.ModelPerches, modelPerches);
            Fill(ref builder, ref root.Perches, perches.ToArray());
            var blob = builder.CreateBlobAssetReference<WharfSpotSet>(Allocator.Persistent);
            builder.Dispose();
            LastLog = $"[wharf spots] {spots.Count} spots, {perches.Count} perches";
            return blob;
        }

        public static string LastLog = "";

        static void Fill<T>(ref BlobBuilder builder, ref BlobArray<T> into, T[] from) where T : unmanaged
        {
            var a = builder.Allocate(ref into, from.Length);
            for (int i = 0; i < from.Length; i++)
                a[i] = from[i];
        }

        static float Top(in Punchy.Flora.KitBox box, out float bottom)
        {
            // (The box's vertical extent, turned.)
            var r = new float3x3(box.Rotation);
            var up = math.abs(r.c0.y) * box.Half.x + math.abs(r.c1.y) * box.Half.y + math.abs(r.c2.y) * box.Half.z;
            bottom = box.Centre.y - up;
            return box.Centre.y + up;
        }

        static bool Inside(in Punchy.Flora.KitBox box, float2 xz)
        {
            var local = math.mul(math.inverse(box.Rotation), new float3(xz.x, box.Centre.y, xz.y) - box.Centre);
            return math.abs(local.x) <= box.Half.x + 0.01f && math.abs(local.z) <= box.Half.z + 0.01f;
        }

        /// <summary>One wharf model: rasterise its deck, walk it outward from the shore, pick the spots, keep the post tops.</summary>
        static void Measure(FloraModel model, List<WharfSpot> spots, List<float2> points, List<float3> perches)
        {
            var lo = new float2(-18f, -WharfSpotSet.RampRun - 1f);
            var hi = new float2(14f, 34f);
            foreach (var box in model.Boxes)
            {
                lo = math.min(lo, box.Centre.xz - box.Reach);
                hi = math.max(hi, box.Centre.xz + box.Reach);
            }
            var size = (int2)math.ceil((hi - lo) / Cell) + 1;
            var deck = new bool[size.x * size.y];
            var high = new bool[size.x * size.y];
            float2 Centre(int x, int z) => lo + (new float2(x, z) + 0.5f) * Cell;
            foreach (var box in model.Boxes)
            {
                var top = Top(box, out var bottom);
                // The deck: planks, the bed and the shore treads (flat, at or below the deck top). Anything that stands
                // higher (rails, houses, stacked gear) is in the way; skiffs and piles below the deck aren't.
                var flat = top - bottom < 0.5f && top < WharfSpotSet.Deck + 0.15f && top > -0.1f;
                var standing = top > WharfSpotSet.Deck + 0.3f && bottom < WharfSpotSet.Deck + 1.2f && !flat;
                if (!flat && !standing)
                    continue;
                var a = (int2)math.floor((box.Centre.xz - box.Reach - lo) / Cell);
                var b = (int2)math.ceil((box.Centre.xz + box.Reach - lo) / Cell);
                a = math.clamp(a, 0, size - 1);
                b = math.clamp(b, 0, size - 1);
                for (int z = a.y; z <= b.y; z++)
                    for (int x = a.x; x <= b.x; x++)
                    {
                        var c = Centre(x, z);
                        if (!Inside(box, c))
                            continue;
                        if (flat && (c.y < 0f || top > WharfSpotSet.Deck - 0.15f))
                            deck[z * size.x + x] = true;
                        if (standing && Half(box).x > 0.1f && Half(box).z > 0.1f)
                            high[z * size.x + x] = true;   // (thin rail posts don't block a walk)
                    }
            }
            // Houses' walls (their cylinders) aren't deck either.
            foreach (var c in model.Colliders)
            {
                if (c.w < WharfSpotSet.Deck + 0.5f)
                    continue;
                var a = math.clamp((int2)math.floor((new float2(c.x, c.y) - c.z - lo) / Cell), 0, size - 1);
                var b = math.clamp((int2)math.ceil((new float2(c.x, c.y) + c.z - lo) / Cell), 0, size - 1);
                for (int z = a.y; z <= b.y; z++)
                    for (int x = a.x; x <= b.x; x++)
                        if (math.distance(Centre(x, z), new float2(c.x, c.y)) < c.z + 0.3f)
                            high[z * size.x + x] = true;
            }
            bool Deck(int x, int z) => x >= 0 && z >= 0 && x < size.x && z < size.y && deck[z * size.x + x] && !high[z * size.x + x];
            bool Solid(int x, int z) => x >= 0 && z >= 0 && x < size.x && z < size.y && (deck[z * size.x + x] || high[z * size.x + x]);
            // A cell a body can stand on: deck with deck all round it (feet clear of the edge).
            bool Stand(int x, int z)
            {
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (!Deck(x + dx, z + dz))
                            return false;
                return true;
            }

            // Walk the deck outward from the lane's start (breadth first, eight ways).
            var startCell = (int2)math.floor((new float2(0f, 0.5f) - lo) / Cell);
            if (!Stand(startCell.x, startCell.y))
                return;
            var parent = new int[size.x * size.y];
            var dist = new int[size.x * size.y];
            for (int i = 0; i < dist.Length; i++)
                dist[i] = -1;
            var queue = new Queue<int>();
            var first = startCell.y * size.x + startCell.x;
            dist[first] = 0;
            parent[first] = -1;
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % size.x, z = i / size.x;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = x + dx, nz = z + dz;
                        if (!Stand(nx, nz) || dist[nz * size.x + nx] >= 0)
                            continue;
                        // (No cutting a corner past an edge.)
                        if (dx != 0 && dz != 0 && (!Stand(x + dx, z) || !Stand(x, z + dz)))
                            continue;
                        var n = nz * size.x + nx;
                        dist[n] = dist[i] + 1;
                        parent[n] = i;
                        queue.Enqueue(n);
                    }
            }

            // Edge spots: standing room with open water straight out one side (no deck, wall or skiff for 3 m).
            var candidates = new List<(int Cell, float2 Out, int Dist)>();
            var outs = new[] { new int2(0, 1), new int2(-1, 0), new int2(1, 0) };
            for (int i = 0; i < dist.Length; i++)
            {
                if (dist[i] < 0)
                    continue;
                int x = i % size.x, z = i / size.x;
                if (Centre(x, z).y < 2f)
                    continue;
                foreach (var o in outs)
                {
                    if (Stand(x + o.x, z + o.y))
                        continue;
                    var open = true;
                    for (int step = 2; step <= 12 && open; step++)
                        open = !Solid(x + o.x * (step + 1), z + o.y * (step + 1));
                    if (open)
                    {
                        candidates.Add((i, o, dist[i]));
                        break;
                    }
                }
            }
            candidates.Sort((p, q) => q.Dist.CompareTo(p.Dist));
            var chosen = new List<(int Cell, float2 Out)>();
            foreach (var c in candidates)
            {
                var at = Centre(c.Cell % size.x, c.Cell / size.x);
                var near = false;
                foreach (var k in chosen)
                    near |= math.distance(at, Centre(k.Cell % size.x, k.Cell / size.x)) < 4.5f;
                if (near)
                    continue;
                chosen.Add((c.Cell, c.Out));
                if (chosen.Count >= MaxFishing)
                    break;
            }

            var lane = 0f;
            for (int k = 0; k < chosen.Count; k++)
            {
                var at = Centre(chosen[k].Cell % size.x, chosen[k].Cell / size.x);
                var kind = math.abs(at.x) < 1.3f && chosen[k].Out.y > 0.5f ? WharfSpotKind.End : WharfSpotKind.Side;
                Add(spots, points, kind, chosen[k].Cell, math.atan2(chosen[k].Out.x, chosen[k].Out.y), parent, size, Centre);
                if (kind == WharfSpotKind.End)
                    lane = math.max(lane, at.y);
            }
            // The crate drop: a step back from the farthest fishing spot.
            if (chosen.Count > 0)
            {
                var far = chosen[0];
                var back = (int2)(-far.Out * 4f);
                var cell = new int2(far.Cell % size.x + back.x, far.Cell / size.x + back.y);
                var index = cell.y * size.x + cell.x;
                if (Stand(cell.x, cell.y) && dist[index] >= 0)
                    Add(spots, points, WharfSpotKind.Crate, index, math.atan2(far.Out.x, far.Out.y), parent, size, Centre);
            }
            // Nets: by the crates stacked on the lane near its start (StructureKit puts them at x 0.8, z 3 on), facing them.
            var nets = (int2)math.floor((new float2(-0.2f, 3.6f) - lo) / Cell);
            var netIndex = nets.y * size.x + nets.x;
            if (Stand(nets.x, nets.y) && dist[netIndex] >= 0)
                Add(spots, points, WharfSpotKind.Nets, netIndex, math.PI * 0.5f, parent, size, Centre);

            // Gull perches: the rail posts' tops (thin upright boxes standing on the deck), a post or two apart.
            var perchStart = perches.Count;
            foreach (var box in model.Boxes)
            {
                var h = box.Half;
                if (h.x > 0.1f || h.z > 0.1f || h.y < 0.3f || h.y > 0.8f)
                    continue;
                var top = Top(box, out var bottom);
                if (bottom < WharfSpotSet.Deck - 0.2f || top > WharfSpotSet.Deck + 1.6f)
                    continue;
                var p = new float3(box.Centre.x, top, box.Centre.z);
                var near = false;
                for (int k = perchStart; k < perches.Count && !near; k++)
                    near = math.distance(perches[k].xz, p.xz) < 1.8f;
                if (!near)
                    perches.Add(p);
            }
        }

        static float3 Half(in Punchy.Flora.KitBox box) => box.Half;

        /// <summary>A spot and its walk out: back along the walk's parents to the start, kept only where it turns.</summary>
        static void Add(List<WharfSpot> spots, List<float2> points, WharfSpotKind kind, int cell, float yaw, int[] parent, int2 size,
                        System.Func<int, int, float2> centre)
        {
            var trail = new List<int>();
            for (int i = cell; i >= 0; i = parent[i])
                trail.Add(i);
            trail.Reverse();
            var start = points.Count;
            points.Add(new float2(0f, -WharfSpotSet.RampRun));
            var last = new int2(int.MinValue);
            for (int k = 0; k < trail.Count; k++)
            {
                var here = new int2(trail[k] % size.x, trail[k] / size.x);
                var keep = k == 0 || k == trail.Count - 1;
                if (!keep)
                {
                    var next = new int2(trail[k + 1] % size.x, trail[k + 1] / size.x);
                    var prev = new int2(trail[k - 1] % size.x, trail[k - 1] / size.x);
                    keep = math.any(next - here != here - prev);
                }
                if (keep && math.any(here != last))
                {
                    points.Add(centre(here.x, here.y));
                    last = here;
                }
            }
            spots.Add(new WharfSpot { At = centre(cell % size.x, cell / size.x), Yaw = yaw, Kind = kind, Path = new int2(start, points.Count - start) });
        }

        /// <summary>
        /// A-73 row 4: gull and fish spots on the island's wharves (the critter tier's, from the seed): about half the rail-post
        /// tops of each fishing village's wharf, placed as the art is (FloraPlacement.Place on the landmark's Wharf prop), and a
        /// fish or two in the water off each end.
        /// Keyed by the critter cell they're in. Each perch is its own draw, so a wharf's gulls are the same on every machine.
        /// </summary>
        public static void GullSpots(ref FloraBlob f, in TerrainSource terrain, ref WharfSpotSet w,
                                     Dictionary<int2, List<AmbientLife.CritterSpot>> into)
        {
            into.Clear();
            if (!terrain.Landmarks.IsCreated)
                return;
            ref var set = ref terrain.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                if (inst.Type >= f.LandmarkTypeCount)
                    continue;
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[inst.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Count == 0 || f.Rules[prop.Species].PerVillage == 0 || f.Rules[prop.Species].FaceLandmark != 2)
                            continue;
                        var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle), radius);
                        var wharf = FloraPlacement.Place(ref f, terrain, (int2)math.floor(spot / f.CellSizes[l]), l);
                        if (wharf.Model < 0 || wharf.Model >= w.ModelPerches.Length)
                            continue;
                        var range = w.ModelPerches[wharf.Model];
                        for (int k = range.x; k < range.x + range.y; k++)
                        {
                            var local = w.Perches[k];
                            var xz = WharfSpotSet.ToWorld(local.xz, wharf.Position, wharf.Yaw, wharf.Scale);
                            var at = new float3(xz.x, wharf.Position.y + local.y * wharf.Scale, xz.y);
                            var rng = new Unity.Mathematics.Random(math.hash(new int3((int3)math.round(at * 10f) + 0x6011)) | 1u);
                            rng.NextUInt();
                            if (rng.NextFloat() >= 0.5f)
                                continue;
                            var cell = (int2)math.floor(at.xz / AmbientLife.CellSize);
                            if (!into.TryGetValue(cell, out var list))
                                into[cell] = list = new List<AmbientLife.CritterSpot>();
                            list.Add(new AmbientLife.CritterSpot { Kind = AmbientLife.CritterKind.Gull, Id = rng.NextUInt() | 1u, Position = at });
                        }
                        // A fish or two in the water off each end of the lane, leaping now and then (the odd jump off the wharf).
                        var spots = wharf.Model < w.ModelSpots.Length ? w.ModelSpots[wharf.Model] : int2.zero;
                        for (int k = spots.x; k < spots.x + spots.y; k++)
                        {
                            if (w.Spots[k].Kind != WharfSpotKind.End)
                                continue;
                            var end = w.Spots[k];
                            var rng = new Unity.Mathematics.Random(math.hash(new int4((int3)math.round(wharf.Position * 10f), k)) | 1u);
                            var fish = 1 + rng.NextInt(2);
                            for (int n = 0; n < fish; n++)
                            {
                                var off = end.At + new float2(math.sin(end.Yaw), math.cos(end.Yaw)) * rng.NextFloat(4f, 9f) +
                                          (rng.NextFloat2() - 0.5f) * 5f;
                                var xz = WharfSpotSet.ToWorld(off, wharf.Position, wharf.Yaw, wharf.Scale);
                                var ground = terrain.SampleGround(xz);
                                var id = rng.NextUInt() | 1u;
                                // (Only where there's water to swim in.)
                                if (!ground.HasWater || ground.WaterSurface - ground.Height < 1.2f)
                                    continue;
                                var at = new float3(xz.x, ground.WaterSurface - 0.6f, xz.y);
                                var cell = (int2)math.floor(xz / AmbientLife.CellSize);
                                if (!into.TryGetValue(cell, out var list))
                                    into[cell] = list = new List<AmbientLife.CritterSpot>();
                                list.Add(new AmbientLife.CritterSpot { Kind = AmbientLife.CritterKind.Fish, Id = id, Position = at });
                            }
                        }
                    }
                }
            }
        }
    }
}
