using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-136: the ground darkens where placed things meet it (trunks, rocks, walls, posts, a stall's feet), baked into
    /// the terrain chunks' albedo (SurfaceProps), so the seam reads at every terrain LOD instead of as a hard cut. Not a
    /// shadow: each contact is sized from the width where it touches the ground (a trunk, never its canopy), darkest at
    /// the seam (ContactShade.Seam, as the cave rooms' W-135 contacts) and gone within about a width.
    /// FloraSystem publishes its placement and every model's feet (FloraModel.Feet); BuildChunkMeshJob re-places the
    /// shrub and tree cells round each chunk (FloraPlacement.Place, footprint only) and rasterises their feet into its
    /// vertices. Seed-derived and cosmetic: every peer computes the same from the same placement.
    /// </summary>
    public static class GroundContacts
    {
        /// <summary>Which strength and reach a contact takes (row 2 tunables).</summary>
        public enum Kind : byte { Plant, Rock, Building, Prop }

        /// <summary>One contact: a capsule on the ground plane (model space, before the plant's rotation and scale), and
        /// the width of the part it belongs to, which sets how far the darkening reaches.</summary>
        public struct Foot
        {
            public float2 A, B;
            public float Radius, Width;
        }

        /// <summary>Every model's feet, for the chunk job.</summary>
        public struct Table
        {
            /// <summary>Per model: its feet in Feet (first, count).</summary>
            public BlobArray<int2> Models;
            /// <summary>Per model: its Kind.</summary>
            public BlobArray<byte> Kinds;
            /// <summary>Per model: how far from its base its darkening can reach (unscaled m).</summary>
            public BlobArray<float> Extents;
            public BlobArray<Foot> Feet;
            /// <summary>How far outside a chunk a plant can stand and still darken it (m): shrub layer biome-grown, shrub
            /// layer any (towns, landmarks), tree layer biome-grown, tree layer any.</summary>
            public float4 Margins;
            /// <summary>The widest contact on the shrub and tree layers (m, at the biggest scale), for the screen gate.</summary>
            public float2 LayerWidths;
        }

        /// <summary>Where the models are cut for their feet: just above the base, under any plinth's top or step.</summary>
        public const float SliceHeight = 0.08f;
        /// <summary>A contact narrower than this on screen at a chunk's nearest distance isn't drawn into it.</summary>
        public const float ContactPixels = 2f;
        /// <summary>Darkening fainter than this at its seam (after spreading over the vertex spacing) is skipped.</summary>
        public const float Faint = 0.04f;
        /// <summary>The most a tunable reach can grow a contact (the chunk margins allow for this much).</summary>
        public const float MaxReachScale = 2f;
        /// <summary>The most feet one model keeps (its fit coarsens until it fits).</summary>
        const int MaxFeet = 160;

        /// <summary>Row 2 tunables, per Kind (x plants and trees, y rocks, z buildings, w props): how dark the seam gets
        /// (1 = ContactShade.Seam) and how far it reaches (1 = about its width). Read when chunks are built.</summary>
        public static float4 Strength = new float4(1f, 1f, 1f, 1f), ReachScale = new float4(1f, 1f, 1f, 1f);

        /// <summary>The published placement and contacts (not created until FloraSystem has built).</summary>
        public static BlobAssetReference<FloraBlob> Flora;
        public static BlobAssetReference<Table> Contacts;
        /// <summary>Bumped on every publish and withdraw: the terrain re-meshes its chunks when it changes.</summary>
        public static int Version;
        /// <summary>The chunk build reading the tables, completed before they're freed.</summary>
        public static JobHandle Readers;

        public static Kind KindOf(DetailPolicy.FloraClass c) => c switch
        {
            DetailPolicy.FloraClass.Tree or DetailPolicy.FloraClass.Plant => Kind.Plant,
            DetailPolicy.FloraClass.Rock => Kind.Rock,
            DetailPolicy.FloraClass.Structure => Kind.Building,
            _ => Kind.Prop
        };

        /// <summary>Publishes FloraSystem's placement and its models' feet (main thread, after its build).</summary>
        public static void Publish(BlobAssetReference<FloraBlob> flora, List<FloraModel> models)
        {
            Withdraw();
            if (!flora.IsCreated || models == null || models.Count != flora.Value.Models.Length)
                return;
            ref var f = ref flora.Value;
            var count = models.Count;
            // Each model's biggest scale, and whether a biome grows it (the rest only stand in towns and landmarks).
            var maxScale = new float[count];
            var natural = new bool[count];
            for (int s = 0; s < f.Rules.Length; s++)
            {
                var rule = f.Rules[s];
                for (int v = 0; v < rule.Variants && rule.FirstModel + v < count; v++)
                    maxScale[rule.FirstModel + v] = math.max(1f, rule.SizeRange.y) * 1.3f;   // props and folk scale on top
            }
            for (int e = 0; e < f.Entries.Length; e++)
            {
                var rule = f.Rules[f.Entries[e].Species];
                for (int v = 0; v < rule.Variants && rule.FirstModel + v < count; v++)
                    natural[rule.FirstModel + v] = true;
            }

            var feet = new List<Foot>();
            var ranges = new int2[count];
            var kinds = new byte[count];
            var extents = new float[count];
            var margins = float4.zero;
            var widths = float2.zero;
            for (int i = 0; i < count; i++)
            {
                var m = models[i];
                ranges[i] = new int2(feet.Count, 0);
                if (m == null || m.Species == null)
                    continue;
                var layer = (int)m.Species.Layer;
                kinds[i] = (byte)KindOf(m.DetailClass());
                if (layer == 0 || m.Species.Form is FloraForm.FloatingRock or FloraForm.Bridge)
                    continue;
                var first = feet.Count;
                // A tree stands on its trunk alone, never its roots or canopy: a disc of the trunk's width.
                var trunkTree = m.TrunkRadius > 0f && m.Colliders.Count == 0 && m.Boxes.Count == 0 && !FloraGenerator.Standable(m.Species.Form);
                if (!trunkTree && m.Feet.Count > 0)
                    feet.AddRange(m.Feet);
                else if (!trunkTree && m.Colliders.Count > 0)
                {
                    for (int c = 0; c < m.Colliders.Count; c++)
                        if ((c < m.ColliderBase.Count ? m.ColliderBase[c] : 0f) < 0.05f)
                        {
                            var col = m.Colliders[c];
                            feet.Add(new Foot { A = new float2(col.x, col.y), B = new float2(col.x, col.y), Radius = col.z, Width = col.z * 2f });
                        }
                }
                else if (m.TrunkRadius > 0f)
                    feet.Add(new Foot { Radius = m.TrunkRadius, Width = m.TrunkRadius * 2f });
                ranges[i] = new int2(first, feet.Count - first);
                var extent = 0f;
                var widest = 0f;
                for (int k = first; k < feet.Count; k++)
                {
                    var foot = feet[k];
                    extent = math.max(extent, math.max(math.length(foot.A), math.length(foot.B)) + foot.Radius +
                                              ContactShade.Reach(foot.Width) * MaxReachScale);
                    widest = math.max(widest, foot.Width);
                }
                extents[i] = extent;
                if (layer < 1 || layer > 2 || feet.Count == first)
                    continue;
                var reach = extent * maxScale[i];
                var slot = (layer - 1) * 2;
                if (natural[i])
                    margins[slot] = math.max(margins[slot], reach);
                margins[slot + 1] = math.max(margins[slot + 1], reach);
                widths[layer - 1] = math.max(widths[layer - 1], widest * maxScale[i]);
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<Table>();
            var modelArray = builder.Allocate(ref root.Models, count);
            var kindArray = builder.Allocate(ref root.Kinds, count);
            var extentArray = builder.Allocate(ref root.Extents, count);
            for (int i = 0; i < count; i++)
            {
                modelArray[i] = ranges[i];
                kindArray[i] = kinds[i];
                extentArray[i] = extents[i];
            }
            var footArray = builder.Allocate(ref root.Feet, feet.Count);
            for (int k = 0; k < feet.Count; k++)
                footArray[k] = feet[k];
            root.Margins = margins;
            root.LayerWidths = widths;
            Contacts = builder.CreateBlobAssetReference<Table>(Allocator.Persistent);
            builder.Dispose();
            Flora = flora;
            Version++;
        }

        /// <summary>Takes the tables back before <paramref name="flora"/> is freed (FloraSystem's teardown).</summary>
        public static void Withdraw(BlobAssetReference<FloraBlob> flora)
        {
            if (Flora.IsCreated && Flora.Equals(flora))
                Withdraw();
        }

        static void Withdraw()
        {
            Readers.Complete();
            Readers = default;
            if (Contacts.IsCreated)
                Contacts.Dispose();
            Contacts = default;
            if (Flora.IsCreated)
                Version++;
            Flora = default;
        }

        /// <summary>
        /// Fits capsules to a model's cross-section (FloraMeshBuilder.Slice segments): the segments join into loops
        /// (a post, a wall, a boulder's outline), each loop's width is 4 × area / perimeter (a disc's diameter, a square's
        /// side, about twice a thin wall's thickness), and its outline is cut into short pieces, each a capsule along it.
        /// Main thread or worker; no Unity objects.
        /// </summary>
        public static void FitFeet(List<Vector4> segments, List<Foot> into)
        {
            into.Clear();
            if (segments.Count == 0)
                return;
            // Join segments that share an end (quantised to 2 mm) into loops.
            var ids = new Dictionary<long, int>();
            var parent = new List<int>();
            int Id(float x, float z)
            {
                var key = ((long)math.round(x * 500f) << 32) ^ ((long)math.round(z * 500f) & 0xFFFFFFFFL);
                if (!ids.TryGetValue(key, out var id))
                {
                    id = parent.Count;
                    ids[key] = id;
                    parent.Add(id);
                }
                return id;
            }
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }
            var ends = new int[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                int a = Find(Id(s.x, s.y)), b = Find(Id(s.z, s.w));
                if (a != b)
                    parent[b] = a;
                ends[i] = a;
            }
            var area = new Dictionary<int, float>();
            var length = new Dictionary<int, float>();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var loop = Find(ends[i]);
                ends[i] = loop;
                area.TryGetValue(loop, out var a);
                length.TryGetValue(loop, out var l);
                area[loop] = a + (s.x * s.w - s.z * s.y) * 0.5f;
                length[loop] = l + math.distance(new float2(s.x, s.y), new float2(s.z, s.w));
            }

            for (float bin = 0.4f; ; bin *= 1.6f)
            {
                into.Clear();
                var pieces = new Dictionary<(int, int, int), List<float2>>();
                for (int i = 0; i < segments.Count; i++)
                {
                    var s = segments[i];
                    float2 p = new float2(s.x, s.y), q = new float2(s.z, s.w);
                    var steps = math.max(1, (int)math.ceil(math.distance(p, q) / 0.1f));
                    for (int k = 0; k <= steps; k++)
                    {
                        var pt = math.lerp(p, q, k / (float)steps);
                        var c = (int2)math.floor(pt / bin);
                        var key = (ends[i], c.x, c.y);
                        if (!pieces.TryGetValue(key, out var list))
                            pieces[key] = list = new List<float2>();
                        list.Add(pt);
                    }
                }
                if (pieces.Count > MaxFeet && bin < 50f)
                    continue;
                foreach (var piece in pieces)
                {
                    var pts = piece.Value;
                    var loop = piece.Key.Item1;
                    var width = math.clamp(4f * math.abs(area[loop]) / math.max(length[loop], 1e-4f), 0.1f, 3f);
                    var mean = float2.zero;
                    foreach (var pt in pts)
                        mean += pt;
                    mean /= pts.Count;
                    float sxx = 0f, szz = 0f, sxz = 0f;
                    foreach (var pt in pts)
                    {
                        var d = pt - mean;
                        sxx += d.x * d.x;
                        szz += d.y * d.y;
                        sxz += d.x * d.y;
                    }
                    var angle = 0.5f * math.atan2(2f * sxz, sxx - szz);
                    var u = new float2(math.cos(angle), math.sin(angle));
                    var v = new float2(-u.y, u.x);
                    float tMin = float.MaxValue, tMax = float.MinValue, sMin = float.MaxValue, sMax = float.MinValue;
                    foreach (var pt in pts)
                    {
                        var d = pt - mean;
                        var t = math.dot(d, u);
                        var s = math.dot(d, v);
                        tMin = math.min(tMin, t); tMax = math.max(tMax, t);
                        sMin = math.min(sMin, s); sMax = math.max(sMax, s);
                    }
                    var r = (sMax - sMin) * 0.5f;
                    var half = math.max(0f, (tMax - tMin) * 0.5f - r);
                    var mid = (tMax + tMin) * 0.5f;
                    var side = v * (sMax + sMin) * 0.5f;
                    into.Add(new Foot
                    {
                        A = mean + u * (mid - half) + side,
                        B = mean + u * (mid + half) + side,
                        Radius = math.max(r, 0.005f),
                        Width = width
                    });
                }
                return;
            }
        }

        /// <summary>
        /// Darkens a chunk's vertex grid (<paramref name="shade"/>, v × v, 1 = untouched) by the contacts of every shrub-
        /// and tree-layer plant whose darkening reaches it. <paramref name="near"/> is the nearest a chunk of this LOD gets
        /// to the camera: contacts too small to see there are left out, and each one is spread over at least a vertex
        /// spacing (keeping its overall darkness), so a coarse LOD shows a soft patch rather than a missed or sharp one.
        /// Burst (called from BuildChunkMeshJob).
        /// </summary>
        public static void Rasterise(ref FloraBlob f, ref Table t, in TerrainSource source, float2 origin, int v, float step,
                                     float near, float pixelsPerMetre, float4 strength, float4 reachScale, NativeArray<float> shade)
        {
            var size = step * (v - 1);
            var chunkMax = origin + size;
            for (int layer = 1; layer <= 2; layer++)
            {
                // Nothing on this layer is wide enough to see at this LOD's nearest distance.
                if (DetailPolicy.ScreenSize(t.LayerWidths[layer - 1], near, pixelsPerMetre) < ContactPixels)
                    continue;
                var natural = t.Margins[(layer - 1) * 2];
                var built = t.Margins[(layer - 1) * 2 + 1];
                if (built <= 0f)
                    continue;
                var cellSize = f.CellSizes[layer];
                var lo = (int2)math.floor((origin - built) / cellSize);
                var hi = (int2)math.floor((chunkMax + built) / cellSize);
                for (int cy = lo.y; cy <= hi.y; cy++)
                for (int cx = lo.x; cx <= hi.x; cx++)
                {
                    var cell = new int2(cx, cy);
                    var cellMin = (float2)cell * cellSize;
                    var gap = math.cmax(math.max(origin - (cellMin + cellSize), cellMin - chunkMax));
                    if (gap > built)
                        continue;
                    // Out past where biome plants reach, only a town's buildings or a landmark's props can.
                    if (gap > natural && !Built(ref f, source, cell, layer, cellSize))
                        continue;
                    var plant = FloraPlacement.Place(ref f, source, cell, layer, footprint: true);
                    if (plant.Model < 0 || plant.Model >= t.Models.Length || plant.Snow < 0f)   // floating plants: no ground
                        continue;
                    Stamp(ref t, plant, origin, chunkMax, v, step, near, pixelsPerMetre, strength, reachScale, shade);
                }
            }
        }

        /// <summary>Whether a cell's plant could be a town's or a landmark's (as Place decides it).</summary>
        static bool Built(ref FloraBlob f, in TerrainSource source, int2 cell, int layer, float cellSize)
        {
            if (source.Settlements.IsCreated && f.SettlementSpecies.Length > 0 &&
                SettlementSampling.Cell(ref source.Settlements.Value, ((float2)cell + 0.5f) * cellSize) >= 0)
                return true;
            var rng = new Random(math.hash(new int3(cell, layer * 7919 + 13)) | 1u);
            var xz = ((float2)cell + rng.NextFloat2()) * cellSize;
            return source.Landmarks.IsCreated && LandmarkSampling.Find(ref source.Landmarks.Value, xz, out _);
        }

        static void Stamp(ref Table t, in FloraSystem.FloraInstance plant, float2 origin, float2 chunkMax, int v, float step,
                          float near, float pixelsPerMetre, float4 strength, float4 reachScale, NativeArray<float> shade)
        {
            var feet = t.Models[plant.Model];
            if (feet.y == 0)
                return;
            var at = plant.Position.xz;
            var extent = t.Extents[plant.Model] * plant.Scale;
            if (math.any(at < origin - extent) || math.any(at > chunkMax + extent))
                return;
            var kind = t.Kinds[plant.Model];
            var dark = strength[kind];
            var stretch = math.clamp(reachScale[kind], 0f, MaxReachScale);
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            for (int k = feet.x; k < feet.x + feet.y; k++)
            {
                var foot = t.Feet[k];
                var width = foot.Width * plant.Scale;
                if (DetailPolicy.ScreenSize(width, near, pixelsPerMetre) < ContactPixels)
                    continue;
                var reach = ContactShade.Reach(width) * stretch;
                if (reach <= 0f)
                    continue;
                // Spread over at least a vertex spacing, keeping its overall darkness: a 6 cm post on a 1 m grid gives a
                // faint patch, not a missed one or a vertex-sized blot.
                var spread = math.max(reach, step);
                var weight = math.min(1f, reach / step) * dark;
                if ((1f - ContactShade.Seam) * weight < Faint)
                    continue;
                var a = at + new float2(foot.A.x * c - foot.A.y * sn, foot.A.x * sn + foot.A.y * c) * plant.Scale;
                var b = at + new float2(foot.B.x * c - foot.B.y * sn, foot.B.x * sn + foot.B.y * c) * plant.Scale;
                var radius = foot.Radius * plant.Scale;
                var lo = (int2)math.max(0f, math.ceil((math.min(a, b) - radius - spread - origin) / step));
                var hi = (int2)math.min(v - 1f, math.floor((math.max(a, b) + radius + spread - origin) / step));
                var ab = b - a;
                var abLen = math.max(math.lengthsq(ab), 1e-8f);
                for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var p = origin + new float2(x, z) * step;
                    var along = math.saturate(math.dot(p - a, ab) / abLen);
                    var d = math.distance(p, a + ab * along) - radius;
                    if (d >= spread)
                        continue;
                    var s = 1f - (1f - ContactShade.Shade(math.max(d, 0f), spread)) * weight;
                    var i = z * v + x;
                    shade[i] = math.min(shade[i], math.max(s, 0f));
                }
            }
        }
    }
}
