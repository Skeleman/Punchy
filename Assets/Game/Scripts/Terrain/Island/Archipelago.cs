using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>One islet of the archipelago (W-30): a ragged sandy disc with a dune crest and a shallow shelf round it.</summary>
    public struct ArchipelagoIslet
    {
        public float2 Centre;
        /// <summary>Mean radius of the dry land, its crest height above the sea, the width of its shallow shelf and how far the shelf takes to fall to the sea floor.</summary>
        public float Radius, Crest, Shelf, Fall;
        public float Seed;
    }

    /// <summary>A sandbar between two near islets: a capsule whose crest sits just under the sea, so it can be waded.</summary>
    public struct ArchipelagoBar
    {
        public float2 A, B;
        public float HalfWidth, Crest;
    }

    /// <summary>
    /// The archipelago (W-30, v2 only): a loose arc of islets trailing out to sea from the dune coast, big sandy islands
    /// near the shore and rocks further out, with sandbars between the near ones. Like the grand falls it is an overlay
    /// baked after every place, so no landmark, cave or settlement sees it, and it only ever raises the sea floor: ground
    /// already near or above the sea is left as it is. Over the islets and their shelves the surface turns to dunes, so
    /// their sand, grass, palms and scrub come from the dunes biome; the shape weights (and so the height) stay the ocean's.
    /// </summary>
    public struct ArchipelagoBlob
    {
        public BlobArray<ArchipelagoIslet> Islets;
        public BlobArray<ArchipelagoBar> Bars;
        /// <summary>(min x, min z, max x, max z) of everything the islets and bars touch.</summary>
        public float4 Bounds;
        public int Dunes;
        public float Sea;
    }

    public static class Archipelago
    {
        public static string LastLog = "";

        /// <summary>
        /// The islets' and bars' own height at a point (float.MinValue outside them all), and how strongly the surface
        /// there is the islets' sand (1 on land, fading out across the shelf).
        /// </summary>
        public static float Field(ref ArchipelagoBlob set, float2 xz, out float mask)
        {
            mask = 0f;
            var p = float.MinValue;
            if (math.any(xz < set.Bounds.xy) || math.any(xz > set.Bounds.zw))
                return p;
            for (int i = 0; i < set.Islets.Length; i++)
            {
                ref var it = ref set.Islets[i];
                var reach = it.Radius * 1.35f + it.Shelf + it.Fall;
                var d2 = math.distancesq(xz, it.Centre);
                if (d2 > reach * reach)
                    continue;
                var dist = math.sqrt(d2);
                // Ragged outline: two octaves of noise on the radius, so an island isn't a disc.
                var r = it.Radius * (1f + 0.22f * noise.snoise(xz * (1.6f / it.Radius) + it.Seed)
                                        + 0.08f * noise.snoise(xz * (5f / it.Radius) + it.Seed * 1.7f));
                var e = dist - r;
                float h;
                if (e < 0f)
                {
                    // A beach rising ~0.9 m, then dunes up to the crest, rippled so the top isn't a dome.
                    var beach = 0.9f * math.saturate(-e / math.min(14f, 0.3f * it.Radius));
                    var dune = it.Crest * math.smoothstep(0.9f, 0.2f, dist / r) * (0.75f + 0.25f * noise.snoise(xz * 0.025f + it.Seed * 0.3f));
                    h = math.max(beach, dune + 0.5f * beach);
                }
                else
                    h = -2.6f * math.smoothstep(0f, it.Shelf, e) - 40f * math.smoothstep(it.Shelf, it.Shelf + it.Fall, e);
                p = math.max(p, set.Sea + h);
                mask = math.max(mask, 1f - math.smoothstep(0f, it.Shelf * 0.7f, e));
            }
            for (int i = 0; i < set.Bars.Length; i++)
            {
                ref var bar = ref set.Bars[i];
                var ab = bar.B - bar.A;
                var t = math.saturate(math.dot(xz - bar.A, ab) / math.max(math.lengthsq(ab), 1e-3f));
                var dl = math.distance(xz, bar.A + ab * t);
                if (dl > bar.HalfWidth + 80f)
                    continue;
                var crest = bar.Crest + 0.3f * noise.snoise(xz * 0.03f + 11.7f);
                var h = crest - 2f * math.smoothstep(0f, bar.HalfWidth, dl) - 40f * math.smoothstep(bar.HalfWidth, bar.HalfWidth + 80f, dl);
                p = math.max(p, set.Sea + h);
                mask = math.max(mask, 1f - math.smoothstep(bar.HalfWidth * 0.5f, bar.HalfWidth * 1.5f, dl));
            }
            return p;
        }

        /// <summary>The ground with the islets raised out of it; only sea floor rises, never ground near or above the sea.</summary>
        public static float Shape(ref ArchipelagoBlob set, float2 xz, float h)
        {
            var p = Field(ref set, xz, out _);
            if (p <= h)
                return h;
            return math.lerp(h, p, math.saturate((set.Sea - 0.5f - h) / 3f));
        }

        /// <summary>The surface weights turned towards the dunes over the islets and their shelves.</summary>
        public static BiomeWeights Surface(ref ArchipelagoBlob set, float2 xz, BiomeWeights surface)
        {
            if (math.any(xz < set.Bounds.xy) || math.any(xz > set.Bounds.zw))
                return surface;
            Field(ref set, xz, out var mask);
            if (mask <= 0f)
                return surface;
            surface.Weights *= 1f - mask;
            surface.Add(set.Dunes, mask);
            surface.Normalize();
            return surface;
        }

        /// <summary>
        /// Bakes the archipelago from the ground as it is now (call it before setting <see cref="TerrainSource.Archipelago"/>):
        /// it surveys rays round the island for the dune coast with the longest run of open sea out to the world's edge
        /// and lays the chain along that run, on its own seed stream.
        /// </summary>
        public static BlobAssetReference<ArchipelagoBlob> Build(in TerrainSource source, int dunes, Allocator allocator)
        {
            var src = source;
            var sea = src.World.SeaLevel;
            // Terrain and sea are drawn out to the coast square; keep clear of its edge like the icebergs do.
            var limit = src.World.WorldSize * 0.5f + src.World.CoastWidth * 0.5f - 60f;
            if (dunes < 0 || !src.BiomeMap.IsCreated)
            {
                LastLog = "[archipelago] none: no dunes biome or no biome map";
                return default;
            }
            float bestRun = 0f;
            float2 coastAt = 0f, dir = 0f;
            for (float a = 0f; a < 360f; a += 3f)
            {
                var d = new float2(math.cos(math.radians(a)), math.sin(math.radians(a)));
                var edge = limit / math.max(math.abs(d.x), math.abs(d.y));
                float coast = -1f;
                for (float r = edge; r > 1500f; r -= 16f)
                    if (src.SampleHeight(d * r) > sea) { coast = r; break; }
                if (coast < 0f)
                    continue;
                src.SampleBiomes(d * (coast - 30f), out var s, out _);
                if (s.Dominant() != dunes || edge - coast <= bestRun)
                    continue;
                bestRun = edge - coast; coastAt = d * coast; dir = d;
            }
            if (bestRun < 900f)
            {
                LastLog = $"[archipelago] none: best dune coast has only {bestRun:F0} m of sea";
                return default;
            }

            var rng = new Random(BiomeSampling.Scramble((uint)src.World.Seed ^ 0x5A17A1E5u) | 1u);
            var length = math.min(bestRun - 400f, 1700f);
            var bend = math.radians(rng.NextFloat(15f, 30f)) * (rng.NextBool() ? 1f : -1f);
            var heading = math.atan2(dir.y, dir.x);
            var start = coastAt + dir * 260f;
            // The chain's spine, as a gentle arc: point and side direction at distance s along it.
            float2 Spine(float s, out float2 side)
            {
                var p = start;
                var step = s / 32f;
                float2 f = dir;
                for (int i = 0; i < 32; i++)
                {
                    var th = heading + bend * ((i + 0.5f) / 32f) * (s / length);
                    f = new float2(math.cos(th), math.sin(th));
                    p += f * step;
                }
                side = new float2(-f.y, f.x);
                return p;
            }

            var islets = new List<ArchipelagoIslet>();
            var main = new List<int>();
            bool Fits(float2 c, float radius, float shelf, float fall)
            {
                var reach = radius * 1.35f + shelf + fall * 0.5f;
                if (math.any(math.abs(c) > limit - reach - 20f))
                    return false;
                foreach (var o in islets)
                    if (math.distance(c, o.Centre) < (radius + o.Radius) * 1.35f + 12f)
                        return false;
                // Only open sea: the ground under the whole footprint must lie well below the surface.
                if (src.SampleHeight(c) > sea - 4f)
                    return false;
                for (int k = 0; k < 8; k++)
                {
                    var a = k * math.PI / 4f;
                    if (src.SampleHeight(c + new float2(math.cos(a), math.sin(a)) * (radius * 1.35f + shelf)) > sea - 3f)
                        return false;
                }
                return true;
            }
            ArchipelagoIslet Make(float2 c, float radius, float crest, float shelf) => new ArchipelagoIslet
            {
                Centre = c, Radius = radius, Crest = crest, Shelf = shelf, Fall = 40f + 0.3f * radius, Seed = rng.NextFloat(0f, 500f)
            };

            float along = 0f, prev = 0f;
            int big = 0;
            while (islets.Count < 20)
            {
                var t = along / length;
                float radius, gap, crest, shelf;
                if (big < 4 && t < 0.45f) { radius = rng.NextFloat(55f, 145f) * (1f - 0.4f * t); gap = rng.NextFloat(25f, 90f); crest = rng.NextFloat(4f, 9f); shelf = 30f + 0.25f * radius; }
                else if (t < 0.75f) { radius = rng.NextFloat(16f, 42f); gap = rng.NextFloat(60f, 150f); crest = rng.NextFloat(2f, 4.5f); shelf = 18f; }
                else { radius = rng.NextFloat(3.5f, 8f); gap = rng.NextFloat(50f, 170f); crest = rng.NextFloat(1.5f, 4f); shelf = 6f; }
                along += islets.Count == 0 ? radius : prev + gap + radius;
                if (along > length)
                    break;
                var c = Spine(along, out var side) + side * rng.NextFloat(-0.5f, 0.5f) * (60f + radius);
                if (!Fits(c, radius, shelf, 40f + 0.3f * radius))
                    continue;
                if (radius > 50f) big++;
                main.Add(islets.Count);
                islets.Add(Make(c, radius, crest, shelf));
                prev = radius;
                // A satellite or two beside it, off the spine, so the chain is loose rather than a row.
                var sats = rng.NextFloat() < 0.65f ? (radius > 50f ? 2 : 1) : 0;
                for (int k = 0; k < sats && islets.Count < 20; k++)
                {
                    var sr = rng.NextFloat(4f, radius > 50f ? 14f : 8f);
                    var a = rng.NextFloat(0f, 2f * math.PI);
                    var sc = c + new float2(math.cos(a), math.sin(a)) * (radius * 1.35f + sr + rng.NextFloat(20f, 60f));
                    if (Fits(sc, sr, 8f, 40f + 0.3f * sr))
                        islets.Add(Make(sc, sr, rng.NextFloat(1.5f, 3.5f), 8f));
                }
            }
            if (islets.Count < 3)
            {
                LastLog = $"[archipelago] none: only {islets.Count} islets fit off the dune coast at ({coastAt.x:F0},{coastAt.y:F0})";
                return default;
            }

            // Sandbars between consecutive near islands on the spine (both sizeable, gap under 170 m).
            var bars = new List<ArchipelagoBar>();
            for (int i = 0; i + 1 < main.Count; i++)
            {
                var a = islets[main[i]]; var b = islets[main[i + 1]];
                if (a.Radius < 15f || b.Radius < 15f || math.distance(a.Centre, b.Centre) - a.Radius - b.Radius > 170f)
                    continue;
                bars.Add(new ArchipelagoBar { A = a.Centre, B = b.Centre, HalfWidth = rng.NextFloat(10f, 18f), Crest = rng.NextFloat(-0.7f, -0.3f) });
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ArchipelagoBlob>();
            var isletArr = builder.Allocate(ref root.Islets, islets.Count);
            float2 lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < islets.Count; i++)
            {
                var it = islets[i];
                isletArr[i] = it;
                var reach = it.Radius * 1.35f + it.Shelf + it.Fall;
                lo = math.min(lo, it.Centre - reach); hi = math.max(hi, it.Centre + reach);
            }
            var barArr = builder.Allocate(ref root.Bars, bars.Count);
            for (int i = 0; i < bars.Count; i++) barArr[i] = bars[i];
            root.Bounds = new float4(lo, hi);
            root.Dunes = dunes;
            root.Sea = sea;
            var blob = builder.CreateBlobAssetReference<ArchipelagoBlob>(allocator);
            builder.Dispose();

            float2 clo = float.MaxValue, chi = float.MinValue;
            foreach (var it in islets) { clo = math.min(clo, it.Centre - it.Radius); chi = math.max(chi, it.Centre + it.Radius); }
            var far = 0f;
            foreach (var it in islets) far = math.max(far, math.distance(it.Centre, coastAt) + it.Radius);
            var text = new System.Text.StringBuilder();
            text.Append($"[archipelago] {islets.Count} islets, {bars.Count} sandbars, off the dune coast at ({coastAt.x:F0},{coastAt.y:F0}) heading {math.degrees(heading):F0} deg, " +
                        $"out to {far:F0} m from it; land spans x {clo.x:F0}..{chi.x:F0}, z {clo.y:F0}..{chi.y:F0} (limit {limit:F0}):");
            foreach (var it in islets)
                text.Append($" ({it.Centre.x:F0},{it.Centre.y:F0}) {2f * it.Radius:F0} m wide, crest {it.Crest:F1};");
            LastLog = text.ToString();
            return blob;
        }
    }
}
