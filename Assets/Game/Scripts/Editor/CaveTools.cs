using System.Collections.Generic;
using System.Linq;
using System.Text;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// Shortcuts for working on caves in play mode, so a change can be seen and checked in seconds rather than after a
    /// full re-bake and restart (Punchy/Caves menu; also callable from scripts):
    /// - Rebake Caves: rebuilds only the cave and anchor blobs from the running terrain and swaps them in, after
    ///   editing a CaveStyle's shape or a cave landmark. (Looks already apply live: the style table uploads every frame.)
    /// - Visit: puts the player in the biggest chamber of a cave of a given style (or outside its mouth), the same
    ///   spot every time, for before/after shots.
    /// - Check: logs the cave counts, anchor placement and lookups, and floor-finding against a plain scan.
    /// </summary>
    public static class CaveTools
    {
        static readonly List<BlobAssetReference<CaveSetBlob>> madeCaves = new List<BlobAssetReference<CaveSetBlob>>();
        static readonly List<BlobAssetReference<AnchorSetBlob>> madeAnchors = new List<BlobAssetReference<AnchorSetBlob>>();

        static bool TryWorld(out EntityManager em, out Entity terrain, out TerrainSource source, out BiomePalette palette)
        {
            em = default; terrain = Entity.Null; source = default; palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (!Application.isPlaying || world == null || !world.IsCreated)
                return false;
            em = world.EntityManager;
            using var q = em.CreateEntityQuery(typeof(TerrainSource));
            using var pq = em.CreateEntityQuery(typeof(BiomePalette));
            if (q.IsEmpty || pq.IsEmpty)
                return false;
            terrain = q.GetSingletonEntity();
            source = em.GetComponentData<TerrainSource>(terrain);
            palette = em.GetComponentObject<BiomePalette>(pq.GetSingletonEntity());
            return true;
        }

        [MenuItem("Punchy/Caves/Rebake Caves (play mode)")]
        public static string RebakeCaves()
        {
            if (!TryWorld(out var em, out var terrain, out var source, out var palette))
                return Log("Rebake Caves: enter play mode first.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var types = palette.Landmarks.ToList();
            var bare = source;
            bare.Caves = default;
            bare.Anchors = default;
            var caves = CaveBaking.Build(bare, palette.Biomes, types, Allocator.Persistent);
            var withCaves = bare;
            withCaves.Caves = caves;
            var anchors = AnchorBaking.Build(withCaves, types, Allocator.Persistent);
            withCaves.Anchors = anchors;
            em.SetComponentData(terrain, withCaves);
            // The blobs this tool made last time are no longer referenced (the baked ones belong to the subscene).
            foreach (var b in madeCaves) if (b.IsCreated) b.Dispose();
            foreach (var b in madeAnchors) if (b.IsCreated) b.Dispose();
            madeCaves.Clear();
            madeAnchors.Clear();
            if (caves.IsCreated) madeCaves.Add(caves);
            if (anchors.IsCreated) madeAnchors.Add(anchors);
            return Log($"Rebake Caves: {(caves.IsCreated ? caves.Value.Caves.Length : 0)} caves, " +
                       $"{(anchors.IsCreated ? anchors.Value.Anchors.Length : 0)} anchors in {watch.ElapsedMilliseconds} ms");
        }

        /// <summary>The biggest chamber of the biggest cave of a style (skipping the very biggest cave if `second`).</summary>
        public static string Visit(string style, bool second = false, bool outside = false)
        {
            if (!TryWorld(out var em, out _, out var source, out var palette) || !source.Caves.IsCreated)
                return Log("Visit: enter play mode first.");
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            ref var set = ref source.Caves.Value;
            var order = new List<int>();
            for (int i = 0; i < set.Caves.Length; i++)
                if (set.Caves[i].Style > 0 && styles[set.Caves[i].Style - 1].name == style)
                    order.Add(i);
            var counts = new int[set.Caves.Length];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = set.Caves[i].NodeCount;
            order.Sort((a, b) => counts[b].CompareTo(counts[a]));
            if (order.Count <= (second ? 1 : 0))
                return Log($"Visit: no{(second ? " second" : "")} cave in style {style}");
            var cave = order[second ? 1 : 0];
            ref var c = ref set.Caves[cave];
            float3 stand, look;
            if (outside)
            {
                var inward = math.normalizesafe((set.Nodes[c.FirstNode + 1].Position - c.Mouth).xz);
                var at = c.Mouth.xz - inward * 30f;
                stand = new float3(at.x, source.SampleHeight(at), at.y);
                look = new float3(inward.x, 0f, inward.y);
            }
            else
            {
                var best = -1;
                for (int n = c.FirstNode; n < c.FirstNode + c.NodeCount; n++)
                    if (set.Kinds[n] == CaveNodeKind.Chamber && (best < 0 || set.Nodes[n].Radius > set.Nodes[best].Radius))
                        best = n;
                var centre = set.Nodes[best].Position;
                var dir = math.normalizesafe((centre - c.Mouth).xz, new float2(1f, 0f));
                var at = centre.xz - dir * set.Nodes[best].Radius * 0.4f;
                stand = new float3(at.x, source.FloorUnder(new float3(at.x, centre.y, at.y), out _), at.y);
                look = new float3(dir.x, 0f, dir.y);
            }
            using var player = em.CreateEntityQuery(typeof(Punchy.Player.PlayerTag), typeof(LocalTransform));
            var e = player.GetSingletonEntity();
            var t = em.GetComponentData<LocalTransform>(e);
            t.Position = stand + new float3(0f, 0.05f, 0f);
            t.Rotation = quaternion.LookRotationSafe(look, math.up());
            em.SetComponentData(e, t);
            return Log($"Visit {style}{(second ? " (second)" : "")}{(outside ? " outside" : "")}: cave #{cave} at {t.Position}");
        }

        [MenuItem("Punchy/Caves/Hide Debug Readout")]
        public static string HideReadout()
        {
            foreach (var d in Object.FindObjectsByType<Punchy.Debugging.DebugControls>(FindObjectsSortMode.None))
                d.ShowReadout = false;
            return "readout hidden";
        }

        [MenuItem("Punchy/Caves/Check")]
        public static string Check()
        {
            if (!TryWorld(out _, out _, out var source, out var palette) || !source.Caves.IsCreated)
                return Log("Check: enter play mode first.");
            var sb = new StringBuilder();
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            ref var set = ref source.Caves.Value;
            var byStyle = new Dictionary<string, int>();
            for (int c = 0; c < set.Caves.Length; c++)
            {
                var name = set.Caves[c].Style > 0 ? styles[set.Caves[c].Style - 1].name : "none";
                byStyle[name] = byStyle.TryGetValue(name, out var k) ? k + 1 : 1;
            }
            sb.Append($"{set.Caves.Length} caves, {set.Segments.Length} segments; by style:");
            foreach (var kv in byStyle) sb.Append($" {kv.Key}={kv.Value}");
            sb.AppendLine();

            // Anchors: each where it should be.
            if (source.Anchors.IsCreated)
            {
                ref var anchors = ref source.Anchors.Value;
                var misplaced = 0;
                for (int i = 0; i < anchors.Anchors.Length; i++)
                {
                    var a = anchors.Anchors[i];
                    var ok = a.Kind switch
                    {
                        AnchorKind.Roost => source.CaveDistance(a.Position + new float3(0, 0.3f, 0)) > -0.2f && source.CaveDistance(a.Position - new float3(0, 1f, 0)) < 0f,
                        AnchorKind.Burrow or AnchorKind.Pool => math.abs(source.FloorUnder(a.Position + new float3(0, 0.5f, 0), out _) - a.Position.y) < 0.2f,
                        _ => source.CaveDistance(a.Position) < 0f && source.CaveDistance(a.Position) > -0.6f
                    };
                    if (!ok) misplaced++;
                }
                sb.AppendLine($"{anchors.Anchors.Length} anchors, {misplaced} misplaced");
            }

            // Floors: FloorUnder against a plain 5 cm scan down from where it starts looking (a little above the
            // point), at points in cave air.
            var rng = new Unity.Mathematics.Random(99);
            int tested = 0, off = 0;
            for (int k = 0; k < 3000; k++)
            {
                var node = set.Nodes[rng.NextInt(set.Nodes.Length)];
                var p = node.Position + rng.NextFloat3(-node.Radius, node.Radius) * 0.7f;
                var y = math.min(p.y + 0.6f, source.SampleHeight(p.xz) + 0.05f);
                if (source.CaveDistance(new float3(p.x, y, p.z)) >= 0f || p.y > source.SampleHeight(p.xz) - 1f)
                    continue;
                tested++;
                while (y > p.y - 60f && source.CaveDistance(new float3(p.x, y - 0.05f, p.z)) < 0f)
                    y -= 0.05f;
                if (math.abs(source.FloorUnder(p, out _) - y) > 0.1f)
                    off++;
            }
            sb.AppendLine($"floors: {tested} points, {off} differ from a 5 cm scan by more than 10 cm");
            return Log(sb.ToString().TrimEnd());
        }

        static string Log(string s)
        {
            Debug.Log("CaveTools: " + s);
            return s;
        }
    }
}
