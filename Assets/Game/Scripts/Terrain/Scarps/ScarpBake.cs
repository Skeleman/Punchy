using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Bakes the scarp records (<see cref="ScarpRecords"/>) into the blob TerrainSource cuts the ground with (W-155).
    /// Runs after every place and river is baked, on the ground as it stands without scarps: it smooths each rim,
    /// reads the rim's height off that ground, tapers the strip and the drop to nothing at both ends, and holds each
    /// foot above water and above the level where its floor can run out onto the ground again.
    /// </summary>
    public static class ScarpBake
    {
        /// <summary>Distance between rim nodes (m).</summary>
        const float NodeStep = 2f;

        public static string LastLog = "";

        /// <summary>The proportions of each kind of scarp.</summary>
        public static void Style(ScarpKind kind, ref Scarp sc)
        {
            if (kind == ScarpKind.Canyon)
            {
                sc.Width = 32f; sc.Reach = 80f; sc.Apron = 0.35f; sc.Setback = 6f; sc.Lean = 0.05f; sc.Bench = 1f; sc.Dip = 0.09f;
                sc.Push = 1.4f; sc.Crack = 1f; sc.Toe = 0.7f; sc.Notch = 0f;
            }
            else
            {
                // Row 2: beds stand out further and benches come and go more, so the low bank breaks into ledges
                // like the canyon wall does instead of reading as one soft bulge.
                sc.Width = 3.2f; sc.Reach = 14f; sc.Apron = 0.05f; sc.Setback = 0.3f; sc.Lean = 0.12f; sc.Bench = 0.6f; sc.Dip = 0.02f;
                sc.Push = 1.1f; sc.Crack = 0.45f; sc.Toe = 0.12f; sc.Notch = 0.45f;
            }
        }

        public static BlobAssetReference<ScarpSetBlob> Build(in TerrainSource source, Allocator allocator)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var records = ScarpRecords.All;
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ScarpSetBlob>();
            var scarps = builder.Allocate(ref root.Scarps, records.Length);
            var all = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            var text = new System.Text.StringBuilder($"[scarps] {records.Length} scarps");
            var points = new List<float2>();
            var drops = new List<float>();
            for (int k = 0; k < records.Length; k++)
            {
                var rec = records[k];
                ref var sc = ref scarps[k];
                Style(rec.Kind, ref sc);
                sc.Kind = rec.Kind; sc.Seed = rec.Seed; sc.Age = math.saturate(rec.Age);
                Resample(rec, points, drops);
                int n = points.Count;
                var nodes = builder.Allocate(ref sc.Nodes, n);
                float along = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) along += math.distance(points[i], points[i - 1]);
                    var tangent = math.normalizesafe(points[math.min(i + 1, n - 1)] - points[math.max(i - 1, 0)]);
                    var normal = new float2(tangent.y, -tangent.x);
                    nodes[i] = new ScarpNode { P = points[i], N = math.dot(normal, rec.Down) < 0f ? -normal : normal, S = along };
                }
                sc.Length = along;
                // Each end is an eased cap at least as long as the wall is high: the strip and the drop die away
                // together, so the foot line curves back round to the rim and the floor past it grows in step,
                // with no steep side to the cut along the end of the rim.
                float peak = 0f;
                for (int i = 0; i < n; i++)
                    peak = math.max(peak, drops[i]);
                float low = math.min(math.max(math.max(0.25f * along, peak), 12f), 0.45f * along), thin = low;
                var bounds = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                var foot = new float[n];
                var ground = new float[n];
                var wet = new float[n];
                for (int i = 0; i < n; i++)
                {
                    ref var node = ref nodes[i];
                    node.RimY = source.SampleHeight(node.P);
                    node.W = sc.Width * math.smoothstep(2f, 2f + thin, node.S) * math.smoothstep(2f, 2f + thin, along - node.S);
                    var drop = drops[i] * math.smoothstep(0f, low, node.S) * math.smoothstep(0f, low, along - node.S);
                    // The floor past the foot falls away at the apron's slope; it must come out onto the ground
                    // somewhere within reach, so the foot can't be lower than the lowest such line.
                    float daylight = float.MaxValue;
                    wet[i] = float.MinValue;
                    for (float r = 0f; r <= sc.Reach * 0.75f; r += 2f)
                    {
                        var g = source.SampleGround(node.P + node.N * (node.W + r));
                        if (r == 0f) ground[i] = g.Height;
                        daylight = math.min(daylight, g.Height + sc.Apron * r);
                        if (g.HasWater && g.WaterSurface > GroundSample.NoWater * 0.5f)
                            wet[i] = math.max(wet[i], g.WaterSurface + sc.Apron * r + 1f);
                    }
                    foot[i] = math.max(node.RimY - drop, daylight);
                    bounds.xy = math.min(bounds.xy, math.min(node.P - node.N * ScarpSet.TopRamp, node.P + node.N * (sc.Width + sc.Reach)));
                    bounds.zw = math.max(bounds.zw, math.max(node.P - node.N * ScarpSet.TopRamp, node.P + node.N * (sc.Width + sc.Reach)));
                }
                // Even out the feet, then hold each above water, and no higher than the ground it stands on or the rim.
                for (int pass = 0; pass < 2; pass++)
                {
                    float before = foot[0];
                    for (int i = 1; i < n - 1; i++)
                    {
                        float here = foot[i];
                        foot[i] = 0.25f * before + 0.5f * here + 0.25f * foot[i + 1];
                        before = here;
                    }
                }
                sc.MinY = float.MaxValue; sc.MaxY = float.MinValue; sc.MaxDrop = 0f;
                for (int i = 0; i < n; i++)
                {
                    ref var node = ref nodes[i];
                    node.FootY = math.min(math.min(math.max(foot[i], wet[i]), ground[i]), node.RimY);
                    sc.MaxDrop = math.max(sc.MaxDrop, node.RimY - node.FootY);
                    sc.MinY = math.min(sc.MinY, node.FootY);
                    sc.MaxY = math.max(sc.MaxY, node.RimY);
                }
                sc.Bounds = bounds + new float4(-6f, -6f, 6f, 6f);
                all.xy = math.min(all.xy, sc.Bounds.xy);
                all.zw = math.max(all.zw, sc.Bounds.zw);
                var layers = builder.Allocate(ref sc.Layers, rec.Layers.Length);
                sc.LayerSpan = 0f;
                for (int i = 0; i < layers.Length; i++)
                {
                    layers[i] = rec.Layers[i];
                    sc.LayerSpan += rec.Layers[i].Thickness;
                }
                text.Append($"; {rec.Name}: rim {along:F0} m, {n} nodes, drop up to {sc.MaxDrop:F1} m, heights {sc.MinY:F1}..{sc.MaxY:F1}");
            }
            root.Bounds = all;
            var blob = builder.CreateBlobAssetReference<ScarpSetBlob>(allocator);
            builder.Dispose();
            text.Append($"; {watch.Elapsed.TotalMilliseconds:F1} ms");
            LastLog = text.ToString();
            return blob;
        }

        /// <summary>The record's rim as a smooth curve through its points (Catmull-Rom), stepped out at even
        /// distances, with the wanted drop at each step.</summary>
        static void Resample(in ScarpRecord rec, List<float2> points, List<float> drops)
        {
            var fine = new List<float2>();
            var fineDrop = new List<float>();
            int m = rec.Rim.Length;
            for (int k = 0; k < m - 1; k++)
            {
                float2 p0 = rec.Rim[math.max(k - 1, 0)], p1 = rec.Rim[k], p2 = rec.Rim[k + 1], p3 = rec.Rim[math.min(k + 2, m - 1)];
                int sub = math.max(2, (int)math.ceil(math.distance(p1, p2) / 0.5f));
                for (int j = 0; j < sub || k == m - 2 && j == sub; j++)
                {
                    float t = (float)j / sub, t2 = t * t, t3 = t2 * t;
                    fine.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                    fineDrop.Add(math.lerp(rec.Drop[k], rec.Drop[k + 1], t));
                }
            }
            var at = new float[fine.Count];
            for (int i = 1; i < fine.Count; i++)
                at[i] = at[i - 1] + math.distance(fine[i], fine[i - 1]);
            float length = at[fine.Count - 1];
            int n = math.max(2, (int)math.round(length / NodeStep)) + 1;
            points.Clear();
            drops.Clear();
            for (int i = 0, j = 0; i < n; i++)
            {
                float want = length * i / (n - 1);
                while (j < fine.Count - 2 && at[j + 1] < want) j++;
                float t = math.saturate((want - at[j]) / math.max(at[j + 1] - at[j], 1e-5f));
                points.Add(math.lerp(fine[j], fine[j + 1], t));
                drops.Add(math.lerp(fineDrop[j], fineDrop[j + 1], t));
            }
        }
    }
}
