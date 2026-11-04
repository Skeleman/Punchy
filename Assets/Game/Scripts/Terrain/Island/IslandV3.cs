using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Generator version 3, water first (brief v3-island-design.md). Opt-in: <c>-island v3</c>, PUNCHY_ISLAND=v3, or the
    /// editor menu Punchy/Island/v3. <see cref="IslandV2System"/> runs it in place of version 2 and ends every v3 bake
    /// with <see cref="V3Audit"/> and the range checks.
    /// Steps are replaced one card at a time, in bake order: ranges (B2, done), biome field, water sources, rivers, the
    /// hydrology table, carve, surfaces and flora, places, audit. No step changes anything version 2 does.
    /// </summary>
    public static class IslandV3
    {
        public const int Version = 3;

        /// <summary>The central lake's centre (world xz) from the last v3 bake; the lake shelf (GrandFalls) starts from it.</summary>
        public static float2 LakeCentre;

        /// <summary>
        /// Biome map and height offsets. B2: the height offsets are the range field (<see cref="V3Ranges"/>, 1024 a
        /// side) plus the v2 lake bowl; the v2 border ridge is gone. The biome map is still version 2's until B3.
        /// </summary>
        public static IslandV2.Result Generate(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, Allocator allocator)
        {
            // W-73: each step's time goes to the "[bake] v3 generate ms" log line.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var steps = new System.Text.StringBuilder("[bake] v3 generate ms:");
            void Step(string name) { steps.Append($" {name} {watch.ElapsedMilliseconds}"); watch.Restart(); }
            var gen = IslandV2.Generate(palette, hasOcean, allocator);
            gen.Island.Dispose();
            Step("v2base");

            const float coastWobble = 0.14f, lakeRadius = 0.13f;
            IslandV2.PickRealms(out var sites, out var fam, out uint seed, out _);
            const float PX = IslandV2.PX; const float W = IslandV2.WorldSizeM;
            float cx0 = PX * 0.5f + IslandV2.LakeShiftM / W * PX + (IslandV2.H01(seed, 8, 8) - 0.5f) * 40f, cy0 = PX * 0.5f + (IslandV2.H01(seed, 9, 9) - 0.5f) * 40f;
            float lakeR = lakeRadius * PX;
            var lakeWorld = new Vector2(cx0 / PX * W - W * 0.5f, cy0 / PX * W - W * 0.5f);
            LakeCentre = new float2(lakeWorld.x, lakeWorld.y);

            var field = V3Ranges.Build(sites, fam, seed, coastWobble, lakeWorld, lakeR / PX * W);
            Step("ranges");

            // B3: the biome map follows the ranges (no border strips, blend radius, bleed through passes); B4's
            // geology rides along in the ground blob.
            gen.Map.Dispose();
            gen.Map = V3Ground.Build(palette, hasOcean, (float[])field.Clone(), allocator, out gen.V3Ground, out var biomeLog);
            Debug.Log(biomeLog);
            Step("ground");

            // Same lake bowl as version 2, on the finer grid.
            const int R = V3Ranges.Res;
            System.Threading.Tasks.Parallel.For(0, R, y =>
            {
                for (int x = 0; x < R; x++)
                {
                    float px = (x + 0.5f) / R * PX, py = (y + 0.5f) / R * PX;
                    float rl = Mathf.Sqrt((px - cx0) * (px - cx0) + (py - cy0) * (py - cy0)) * (0.85f + 0.3f * IslandV2.Fbm(seed + 53, px / 120f, py / 120f));
                    field[y * R + x] -= 38f * (1f - IslandV2.Smooth(Mathf.Clamp01(rl / (lakeR * 1.8f))));
                }
            });

            var b = new BlobBuilder(Allocator.Temp);
            ref var hm = ref b.ConstructRoot<HeightmapBlob>();
            hm.Resolution = R; hm.Origin = new float2(-W * 0.5f); hm.WorldSize = W; hm.MinHeight = -60f; hm.HeightStep = 400f / 65535f;
            var arr = b.Allocate(ref hm.Heights, R * R);
            for (int i = 0; i < R * R; i++) arr[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt((field[i] + 60f) / hm.HeightStep), 0, 65535);
            gen.Island = b.CreateBlobAssetReference<HeightmapBlob>(allocator); b.Dispose();
            Step("bowl");
            Debug.Log(steps.ToString());

            Debug.Log(V3Ranges.LastLog);
            gen.Report = "v3 (B2 ranges, B3 biomes, B4 rock) " + gen.Report;
            return gen;
        }

        /// <summary>River settings. B5-B7 (hydro table, vector rivers, carve) replace this step.</summary>
        public static RiverSettings Rivers(RiverSettings v1) => IslandV2.Rivers(v1);
    }
}
