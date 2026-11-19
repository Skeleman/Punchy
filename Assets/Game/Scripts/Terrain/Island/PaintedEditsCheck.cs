using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// M-07 r2b (docs/agents/map-editor.md, 4): after the bake, lists painted tiles that land on a settlement plot, a
    /// landmark's garden bed, a cave socket or a river channel, where the generator may level, carve or hollow the edit
    /// away (an edit painted before a generator change can end up somewhere new). Warnings only: it reads the baked
    /// blobs and changes nothing, so the bake is the same with or without it.
    /// </summary>
    public static class PaintedEditsCheck
    {
        /// <summary>A tile counts as painted where some offset is at least this big (m).</summary>
        const float Painted = 0.05f;
        /// <summary>At most this many tiles are named in the log; the counts cover all of them.</summary>
        const int MaxListed = 24;

        /// <summary>The last check's lines ("[map-edits] ..."), for the map editor window; empty without edits.</summary>
        public static string LastLog = "";

        /// <summary>Runs the check on a baked source and logs one warning when anything overlaps. Main thread.</summary>
        public static void Run(in TerrainSource source)
        {
            LastLog = "";
            if (!source.Edits.IsCreated)
                return;
            ref var e = ref source.Edits.Value;
            const int n = PaintedEditsBlob.TileVerts * PaintedEditsBlob.TileVerts;
            const float tileSize = PaintedEditsBlob.TileVerts * PaintedEditsBlob.Spacing;
            int painted = 0, hit = 0, towns = 0, beds = 0, sockets = 0, rivers = 0;
            var listed = new List<string>();
            var what = new List<string>();
            for (int tz = 0; tz < e.TileCount.y; tz++)
            for (int tx = 0; tx < e.TileCount.x; tx++)
            {
                int slot = e.Index[tz * e.TileCount.x + tx];
                if (slot < 0) continue;
                float most = 0f;
                for (int i = 0; i < n; i++) most = math.max(most, math.abs(e.Heights[slot * n + i]));
                if (most < Painted) continue;
                painted++;
                var tile = new int2(tx, tz) + e.TileMin;
                // The bilinear step reaches one vertex past the tile's last, so the tile shapes a full tileSize square.
                var lo = (float2)tile * tileSize;
                var hi = lo + tileSize;
                what.Clear();
                if (Town(source, lo, hi, out var town)) { towns++; what.Add($"settlement {town}"); }
                if (Bed(source, lo, hi, out var landmark)) { beds++; what.Add($"landmark {landmark} bed"); }
                if (Socket(source, lo, hi, out var cave)) { sockets++; what.Add($"cave {cave} socket"); }
                if (River(source, lo, hi)) { rivers++; what.Add("river channel"); }
                if (what.Count > 0) hit++;
                if (what.Count > 0 && listed.Count < MaxListed)
                    listed.Add($"[map-edits]   tile {tile.x},{tile.y} (x {lo.x:F0}..{hi.x:F0}, z {lo.y:F0}..{hi.y:F0}, up to {most:F1} m): {string.Join(", ", what)}");
            }
            if (towns + beds + sockets + rivers == 0)
            {
                LastLog = $"[map-edits] {painted} painted tiles, no overlaps";
                Debug.Log(LastLog);
                return;
            }
            var sb = new StringBuilder();
            sb.Append($"[map-edits] {hit} of {painted} painted tiles overlap places the generator reshapes: {towns} settlement, {beds} landmark bed, {sockets} cave socket, {rivers} river channel (the edit may be levelled or carved away there)");
            foreach (var line in listed) sb.Append('\n').Append(line);
            if (hit > listed.Count) sb.Append($"\n[map-edits]   (first {listed.Count} of {hit} tiles listed)");
            LastLog = sb.ToString();
            Debug.LogWarning(LastLog);
        }

        static bool Overlaps(float2 aLo, float2 aHi, float2 bLo, float2 bHi) => math.all(aLo < bHi) && math.all(bLo < aHi);

        static bool Town(in TerrainSource source, float2 lo, float2 hi, out int settlement)
        {
            settlement = -1;
            if (!source.Settlements.IsCreated) return false;
            ref var set = ref source.Settlements.Value;
            for (int p = 0; p < set.Plots.Length; p++)
            {
                ref var plot = ref set.Plots[p];
                if (Overlaps(lo, hi, plot.Min, plot.Max)) { settlement = plot.Settlement; return true; }
            }
            return false;
        }

        static bool Bed(in TerrainSource source, float2 lo, float2 hi, out int landmark)
        {
            landmark = -1;
            if (!source.Landmarks.IsCreated) return false;
            ref var set = ref source.Landmarks.Value;
            if (set.Beds.Length == 0) return false;
            for (int k = 0; k < set.Instances.Length; k++)
            {
                ref var inst = ref set.Instances[k];
                ref var type = ref set.Types[inst.Type];
                for (int b = 0; b < type.BedCount; b++)
                {
                    ref var bed = ref set.Beds[type.FirstBed + b];
                    var centre = LandmarkSampling.ToWorld(inst, bed.Offset, type.Radius * inst.Scale);
                    // Circle against the tile's square: the nearest point of the square to the bed's centre.
                    if (math.distance(math.clamp(centre, lo, hi), centre) < bed.Radius) { landmark = k; return true; }
                }
            }
            return false;
        }

        static bool Socket(in TerrainSource source, float2 lo, float2 hi, out int cave)
        {
            cave = -1;
            if (!source.CaveSockets.IsCreated) return false;
            ref var set = ref source.CaveSockets.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var range = ref set.Caves[c];
                for (int s = range.First; s < range.First + range.Count; s++)
                {
                    var xz = set.Sockets[s].Pos.xz;
                    if (math.all(xz >= lo) && math.all(xz < hi)) { cave = c; return true; }
                }
            }
            return false;
        }

        static bool River(in TerrainSource source, float2 lo, float2 hi)
        {
            if (!source.Rivers.IsCreated) return false;
            ref var map = ref source.Rivers.Value;
            if (map.Width.Length == 0 || map.CellSize <= 0f) return false;
            // Every river cell whose channel (centre plus half its width) reaches into the tile.
            var c0 = (int2)math.floor((lo - map.Origin) / map.CellSize) - 2;
            var c1 = (int2)math.floor((hi - map.Origin) / map.CellSize) + 2;
            c0 = math.max(c0, 0); c1 = math.min(c1, map.Resolution - 1);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                float width = map.Width[y * map.Resolution + x];
                if (width <= 0f) continue;
                var centre = map.Origin + (new float2(x, y) + 0.5f) * map.CellSize;
                if (math.distance(math.clamp(centre, lo, hi), centre) < width * 0.5f) return true;
            }
            return false;
        }
    }
}
