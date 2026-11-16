using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-07 (#260): the map editor's height edits, a sparse delta layer over the island (docs/agents/map-editor.md).
    /// A grid of height offsets at <see cref="Spacing"/> metres, kept in tiles of <see cref="TileVerts"/> x TileVerts
    /// vertices (64 m); only tiles with a non-zero offset exist, so an untouched island is an empty file. Tiles don't
    /// overlap: sampling near a tile edge reads the neighbouring tile's vertices. Saved as half floats with a SHA-256
    /// trailer, which is also the content hash the island cache key and WorldSync will take. Editor-only for now: no
    /// game code reads it until the bake hook lands (row 2, after Zach's and W-174's answers).
    /// </summary>
    public sealed class IslandEdits
    {
        public const int Format = 1;
        public const int TileVerts = 32;
        public const float Spacing = 2f;
        public const float TileSize = TileVerts * Spacing;
        /// <summary>Offsets smaller than this count as zero (a tile of only these is dropped on save).</summary>
        public const float Epsilon = 1e-3f;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PIED");

        readonly Dictionary<long, float[]> tiles = new Dictionary<long, float[]>();

        /// <summary>The island version the edits were painted on (orphan checks warn on another).</summary>
        public int IslandVersion;

        public int TileCount => tiles.Count;
        public bool IsEmpty => tiles.Count == 0;

        static long Key(int tx, int tz) => ((long)tx << 32) | (uint)tz;
        static int2 Unkey(long key) => new int2((int)(key >> 32), (int)(uint)key);
        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>Every tile's coordinate (tile units: multiply by <see cref="TileSize"/> for metres).</summary>
        public IEnumerable<int2> Tiles()
        {
            foreach (var key in tiles.Keys)
                yield return Unkey(key);
        }

        /// <summary>The offset at grid vertex (gx, gz), world position (gx, gz) * Spacing.</summary>
        public float Vertex(int gx, int gz)
        {
            int tx = FloorDiv(gx, TileVerts), tz = FloorDiv(gz, TileVerts);
            if (!tiles.TryGetValue(Key(tx, tz), out var tile))
                return 0f;
            return tile[(gz - tz * TileVerts) * TileVerts + gx - tx * TileVerts];
        }

        /// <summary>Sets the offset at a grid vertex, making its tile when the value isn't zero.</summary>
        public void SetVertex(int gx, int gz, float value)
        {
            int tx = FloorDiv(gx, TileVerts), tz = FloorDiv(gz, TileVerts);
            var key = Key(tx, tz);
            if (!tiles.TryGetValue(key, out var tile))
            {
                if (math.abs(value) < Epsilon)
                    return;
                tiles[key] = tile = new float[TileVerts * TileVerts];
            }
            tile[(gz - tz * TileVerts) * TileVerts + gx - tx * TileVerts] = value;
        }

        /// <summary>The offset at a world position, bilinear between the four grid vertices around it.</summary>
        public float Sample(float2 xz)
        {
            if (tiles.Count == 0)
                return 0f;
            var g = xz / Spacing;
            var cell = (int2)math.floor(g);
            var f = g - cell;
            float a = Vertex(cell.x, cell.y), b = Vertex(cell.x + 1, cell.y);
            float c = Vertex(cell.x, cell.y + 1), d = Vertex(cell.x + 1, cell.y + 1);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }

        public void Clear() => tiles.Clear();

        /// <summary>Drops tiles whose every offset is below <see cref="Epsilon"/>.</summary>
        public int Prune()
        {
            var empty = new List<long>();
            foreach (var pair in tiles)
            {
                bool any = false;
                foreach (var v in pair.Value)
                    if (math.abs(v) >= Epsilon) { any = true; break; }
                if (!any) empty.Add(pair.Key);
            }
            foreach (var key in empty)
                tiles.Remove(key);
            return empty.Count;
        }

        // ---- undo: copies of the tiles a stroke may touch ----

        /// <summary>Copies of the tiles covering the square [min, max] (null for a tile that doesn't exist yet).</summary>
        public Dictionary<long, float[]> Snapshot(float2 min, float2 max)
        {
            var snap = new Dictionary<long, float[]>();
            var lo = (int2)math.floor(min / TileSize);
            var hi = (int2)math.floor(max / TileSize);
            for (int tz = lo.y; tz <= hi.y; tz++)
                for (int tx = lo.x; tx <= hi.x; tx++)
                {
                    var key = Key(tx, tz);
                    snap[key] = tiles.TryGetValue(key, out var tile) ? (float[])tile.Clone() : null;
                }
            return snap;
        }

        /// <summary>Puts back tiles from <see cref="Snapshot"/>.</summary>
        public void Restore(Dictionary<long, float[]> snap)
        {
            foreach (var pair in snap)
            {
                if (pair.Value == null) tiles.Remove(pair.Key);
                else tiles[pair.Key] = (float[])pair.Value.Clone();
            }
        }

        /// <summary>Folds a snapshot taken before a stroke into an earlier one (keeping the earlier copy of each tile).</summary>
        public static void Merge(Dictionary<long, float[]> into, Dictionary<long, float[]> later)
        {
            foreach (var pair in later)
                if (!into.ContainsKey(pair.Key))
                    into[pair.Key] = pair.Value;
        }

        // ---- the file ----

        /// <summary>The file's bytes: header, tiles in key order (half floats), then a SHA-256 of all that.</summary>
        public byte[] ToBytes()
        {
            Prune();
            var keys = new List<long>(tiles.Keys);
            keys.Sort();
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(Format);
                w.Write(IslandVersion);
                w.Write(Spacing);
                w.Write(TileVerts);
                w.Write(keys.Count);
                foreach (var key in keys)
                {
                    var t = Unkey(key);
                    w.Write(t.x);
                    w.Write(t.y);
                    foreach (var v in tiles[key])
                        w.Write(Mathf.FloatToHalf(v));
                }
            }
            using var sha = SHA256.Create();
            var sum = sha.ComputeHash(ms.GetBuffer(), 0, (int)ms.Length);
            ms.Write(sum, 0, sum.Length);
            return ms.ToArray();
        }

        /// <summary>The content hash (hex SHA-256 of the file body); the same for the same edits however they were made.</summary>
        public string ContentHash()
        {
            var bytes = ToBytes();
            var sb = new StringBuilder(64);
            for (int i = bytes.Length - 32; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, ToBytes());
        }

        /// <summary>Reads a file written by <see cref="Save"/>; null with the reason for anything unexpected.</summary>
        public static IslandEdits Load(string path, out string reason)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) { reason = "unreadable: " + e.GetType().Name; return null; }
            return FromBytes(bytes, out reason);
        }

        public static IslandEdits FromBytes(byte[] bytes, out string reason)
        {
            const int header = 4 + 4 * 5;
            if (bytes == null || bytes.Length < header + 32) { reason = "too short"; return null; }
            for (int i = 0; i < Magic.Length; i++)
                if (bytes[i] != Magic[i]) { reason = "not an edits file"; return null; }
            using (var sha = SHA256.Create())
            {
                var sum = sha.ComputeHash(bytes, 0, bytes.Length - 32);
                for (int i = 0; i < 32; i++)
                    if (sum[i] != bytes[bytes.Length - 32 + i]) { reason = "bad checksum"; return null; }
            }
            using var r = new BinaryReader(new MemoryStream(bytes, 0, bytes.Length - 32));
            r.ReadBytes(4);
            int format = r.ReadInt32();
            if (format != Format) { reason = $"format {format}, expected {Format}"; return null; }
            var edits = new IslandEdits { IslandVersion = r.ReadInt32() };
            float spacing = r.ReadSingle();
            int verts = r.ReadInt32();
            if (spacing != Spacing || verts != TileVerts) { reason = $"grid {spacing} m x {verts}, expected {Spacing} m x {TileVerts}"; return null; }
            int count = r.ReadInt32();
            long need = header + (long)count * (8 + TileVerts * TileVerts * 2);
            if (count < 0 || need != bytes.Length - 32) { reason = "size mismatch"; return null; }
            for (int n = 0; n < count; n++)
            {
                int tx = r.ReadInt32(), tz = r.ReadInt32();
                var tile = new float[TileVerts * TileVerts];
                for (int i = 0; i < tile.Length; i++)
                    tile[i] = Mathf.HalfToFloat(r.ReadUInt16());
                edits.tiles[Key(tx, tz)] = tile;
            }
            reason = $"{count} tiles";
            return edits;
        }
    }
}
