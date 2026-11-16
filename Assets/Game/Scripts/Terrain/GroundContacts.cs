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
        /// <summary>Every model's feet, for the chunk job.</summary>
        public struct Table
        {
            /// <summary>Per model: its feet in Feet (first, count).</summary>
            public BlobArray<int2> Models;
            /// <summary>Per model: how far from its base its darkening can reach (unscaled m).</summary>
            public BlobArray<float> Extents;
            /// <summary>Model-space feet (ContactShade.ModelFeet), each with its Kind.</summary>
            public BlobArray<ContactShade.Contact> Feet;
            /// <summary>How far outside a chunk a plant can stand and still darken it (m): shrub layer biome-grown, shrub
            /// layer any (towns, landmarks), tree layer biome-grown, tree layer any.</summary>
            public float4 Margins;
            /// <summary>The widest contact on the shrub and tree layers (m, at the biggest scale), for the screen gate.</summary>
            public float2 LayerWidths;
        }

        /// <summary>A contact narrower than this on screen at a chunk's nearest distance isn't drawn into it.</summary>
        public const float ContactPixels = 2f;
        /// <summary>The published placement and contacts (not created until FloraSystem has built).</summary>
        public static BlobAssetReference<FloraBlob> Flora;
        public static BlobAssetReference<Table> Contacts;
        /// <summary>Bumped on every publish and withdraw: the terrain re-meshes its chunks when it changes.</summary>
        public static int Version;
        /// <summary>The chunk build reading the tables, completed before they're freed.</summary>
        public static JobHandle Readers;

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

            var feet = new List<ContactShade.Contact>();
            var ranges = new int2[count];
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
                if (layer == 0 || m.Species.Form is FloraForm.FloatingRock or FloraForm.Bridge)
                    continue;
                var first = feet.Count;
                ContactShade.ModelFeet(m, feet);
                ranges[i] = new int2(first, feet.Count - first);
                var extent = 0f;
                var widest = 0f;
                for (int k = first; k < feet.Count; k++)
                {
                    var foot = feet[k];
                    extent = math.max(extent, math.length(foot.Base.xz) + foot.Extent);
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
            var extentArray = builder.Allocate(ref root.Extents, count);
            for (int i = 0; i < count; i++)
            {
                modelArray[i] = ranges[i];
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
            for (int k = feet.x; k < feet.x + feet.y; k++)
            {
                var foot = t.Feet[k].Placed(plant.Position, plant.Yaw, plant.Scale);
                if (DetailPolicy.ScreenSize(foot.Width, near, pixelsPerMetre) < ContactPixels)
                    continue;
                // Spread over at least a vertex spacing (ContactShade.Weigh).
                if (!ContactShade.Weigh(foot.Width, foot.Kind, strength, reachScale, step, out var spread, out var weight))
                    continue;
                var size = math.length(foot.Half) + foot.Radius + spread;
                var lo = (int2)math.max(0f, math.ceil((foot.Base.xz - size - origin) / step));
                var hi = (int2)math.min(v - 1f, math.floor((foot.Base.xz + size - origin) / step));
                for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var d = foot.Distance(origin + new float2(x, z) * step);
                    if (d >= spread)
                        continue;
                    var i = z * v + x;
                    shade[i] = math.min(shade[i], ContactShade.Darken(d, spread, weight));
                }
            }
        }
    }
}
