using System;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// M-07 r2b (#260): the map editor's painted height edits as the bake reads them. A sparse grid of offsets at
    /// <see cref="Spacing"/> m in tiles of <see cref="TileVerts"/>^2 vertices (64 m), with a flat index over the tiles'
    /// bounding box. <see cref="TerrainSource.ShapeHeight"/> adds <see cref="Sample"/> to the base height, so rivers,
    /// places, roads, ponds, scarps and W-174's ground edits all fit the edited ground (docs/agents/map-editor.md, 2).
    /// No file (or an empty one) leaves the blob unset, so the bake is byte-identical and the landmark pins hold.
    /// </summary>
    public struct PaintedEditsBlob
    {
        public const int TileVerts = 32;
        public const float Spacing = 2f;

        /// <summary>The first 16 bytes of the file's SHA-256 trailer (its content hash), for WorldSync.Signature.</summary>
        public uint4 Content;
        /// <summary>The first tile of the index box (tile units) and its size in tiles.</summary>
        public int2 TileMin, TileCount;
        /// <summary>Per index cell (row-major, z then x): the tile's slot in <see cref="Heights"/>, or -1.</summary>
        public BlobArray<int> Index;
        /// <summary>Slot s holds TileVerts^2 offsets (metres), row-major, z then x.</summary>
        public BlobArray<float> Heights;

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>The offset at grid vertex (gx, gz), world position (gx, gz) * Spacing; 0 outside every tile.</summary>
        public static float Vertex(ref PaintedEditsBlob e, int gx, int gz)
        {
            int tx = FloorDiv(gx, TileVerts) - e.TileMin.x, tz = FloorDiv(gz, TileVerts) - e.TileMin.y;
            if ((uint)tx >= (uint)e.TileCount.x || (uint)tz >= (uint)e.TileCount.y) return 0f;
            int slot = e.Index[tz * e.TileCount.x + tx];
            if (slot < 0) return 0f;
            int lx = gx - (tx + e.TileMin.x) * TileVerts, lz = gz - (tz + e.TileMin.y) * TileVerts;
            return e.Heights[slot * TileVerts * TileVerts + lz * TileVerts + lx];
        }

        /// <summary>The offset at a world position, bilinear between the four grid vertices around it (as the editor's preview).</summary>
        public static float Sample(ref PaintedEditsBlob e, float2 xz)
        {
            var g = xz / Spacing;
            var cell = (int2)math.floor(g);
            // Outside the tiles' box (plus one vertex for the bilinear step) there is nothing to read.
            var lo = e.TileMin * TileVerts - 1;
            var hi = (e.TileMin + e.TileCount) * TileVerts;
            if (math.any(cell < lo) || math.any(cell >= hi)) return 0f;
            var f = g - cell;
            float a = Vertex(ref e, cell.x, cell.y), b = Vertex(ref e, cell.x + 1, cell.y);
            float c = Vertex(ref e, cell.x, cell.y + 1), d = Vertex(ref e, cell.x + 1, cell.y + 1);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }
    }

    /// <summary>
    /// M-07 r2b: finds and reads the edits file for the bake. The map editor saves it as a TextAsset at
    /// <see cref="AssetPath"/>, so play mode, builds and every multiplayer peer read the same bytes. Format: the map
    /// editor's IslandEdits file (magic "PIED", format 1, island version, spacing, tile size, tiles as half floats,
    /// SHA-256 trailer). Anything unexpected logs a "[map-edits]" warning and bakes without edits; it never fails.
    /// </summary>
    public static class PaintedEdits
    {
        public const string ResourcePath = "WorldEdits/IslandEdits";
        public const string AssetPath = "Assets/Game/Resources/" + ResourcePath + ".bytes";
        public const int Format = 1;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PIED");
        const int HeaderBytes = 4 + 4 * 5, SumBytes = 32;

        /// <summary>Set by the map editor while it loads its island: it paints over the unedited ground, so it bakes without edits.</summary>
        public static bool Suppress;

        /// <summary>The bytes of the edits file, or null when there is none (or <see cref="Suppress"/> is set). Main thread.</summary>
        public static byte[] FileBytes()
        {
            if (Suppress) return null;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) return null;
            var bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        /// <summary>The file's content hash (hex SHA-256 trailer) when it holds at least one tile, else null: an empty or
        /// unreadable file bakes as no file, so it must not change the island cache key either.</summary>
        public static string Hash(byte[] bytes)
        {
            if (!Check(bytes, out _, out int tiles, out _) || tiles == 0) return null;
            var sb = new StringBuilder(64);
            for (int i = bytes.Length - SumBytes; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Header, size and checksum checks; the reason when it fails.</summary>
        static bool Check(byte[] bytes, out int islandVersion, out int tiles, out string reason)
        {
            islandVersion = 0; tiles = 0;
            if (bytes == null) { reason = "no file"; return false; }
            if (bytes.Length < HeaderBytes + SumBytes) { reason = "too short"; return false; }
            for (int i = 0; i < Magic.Length; i++)
                if (bytes[i] != Magic[i]) { reason = "not an edits file"; return false; }
            using (var sha = SHA256.Create())
            {
                var sum = sha.ComputeHash(bytes, 0, bytes.Length - SumBytes);
                for (int i = 0; i < SumBytes; i++)
                    if (sum[i] != bytes[bytes.Length - SumBytes + i]) { reason = "bad checksum"; return false; }
            }
            int format = BitConverter.ToInt32(bytes, 4);
            if (format != Format) { reason = $"format {format}, expected {Format}"; return false; }
            islandVersion = BitConverter.ToInt32(bytes, 8);
            float spacing = BitConverter.ToSingle(bytes, 12);
            int verts = BitConverter.ToInt32(bytes, 16);
            if (spacing != PaintedEditsBlob.Spacing || verts != PaintedEditsBlob.TileVerts)
            { reason = $"grid {spacing} m x {verts}, expected {PaintedEditsBlob.Spacing} m x {PaintedEditsBlob.TileVerts}"; return false; }
            tiles = BitConverter.ToInt32(bytes, 20);
            long need = HeaderBytes + (long)tiles * (8 + PaintedEditsBlob.TileVerts * PaintedEditsBlob.TileVerts * 2);
            if (tiles < 0 || need != bytes.Length - SumBytes) { reason = "size mismatch"; return false; }
            reason = $"{tiles} tiles";
            return true;
        }

        /// <summary>The last <see cref="Build"/>'s "[map-edits]" line.</summary>
        public static string LastLog = "[map-edits] none";

        /// <summary>
        /// The edits blob for a bake of island <paramref name="version"/>, or default (unset) for no file, an empty file
        /// or one that fails its checks. Main thread (reads a TextAsset).
        /// </summary>
        public static BlobAssetReference<PaintedEditsBlob> Build(int version, Allocator allocator) => Build(FileBytes(), version, allocator);

        public static BlobAssetReference<PaintedEditsBlob> Build(byte[] bytes, int version, Allocator allocator)
        {
            if (bytes == null) { LastLog = Suppress ? "[map-edits] suppressed (map editor base island)" : "[map-edits] none"; return default; }
            if (!Check(bytes, out int painted, out int count, out string reason))
            {
                LastLog = $"[map-edits] {AssetPath} ignored: {reason}";
                Debug.LogWarning(LastLog);
                return default;
            }
            if (count == 0) { LastLog = "[map-edits] empty file"; return default; }
            const int n = PaintedEditsBlob.TileVerts * PaintedEditsBlob.TileVerts;
            const int tileBytes = 8 + n * 2;
            var min = new int2(int.MaxValue); var max = new int2(int.MinValue);
            for (int t = 0; t < count; t++)
            {
                int at = HeaderBytes + t * tileBytes;
                var tile = new int2(BitConverter.ToInt32(bytes, at), BitConverter.ToInt32(bytes, at + 4));
                min = math.min(min, tile); max = math.max(max, tile);
            }
            var size = max - min + 1;
            // A sane file covers the island (a 9.2 km island is 144 tiles across); anything wider is damage.
            if (size.x > 4096 || size.y > 4096)
            {
                LastLog = $"[map-edits] {AssetPath} ignored: tiles span {size.x}x{size.y}";
                Debug.LogWarning(LastLog);
                return default;
            }
            var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<PaintedEditsBlob>();
            root.TileMin = min; root.TileCount = size;
            int sumAt = bytes.Length - SumBytes;
            root.Content = new uint4(BitConverter.ToUInt32(bytes, sumAt), BitConverter.ToUInt32(bytes, sumAt + 4), BitConverter.ToUInt32(bytes, sumAt + 8), BitConverter.ToUInt32(bytes, sumAt + 12));
            var index = b.Allocate(ref root.Index, size.x * size.y);
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            var heights = b.Allocate(ref root.Heights, count * n);
            float lo = 0f, hi = 0f;
            for (int t = 0; t < count; t++)
            {
                int at = HeaderBytes + t * tileBytes;
                var tile = new int2(BitConverter.ToInt32(bytes, at), BitConverter.ToInt32(bytes, at + 4)) - min;
                index[tile.y * size.x + tile.x] = t;
                for (int i = 0; i < n; i++)
                {
                    float v = Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, at + 8 + i * 2));
                    if (!math.isfinite(v)) v = 0f;
                    heights[t * n + i] = v;
                    lo = math.min(lo, v); hi = math.max(hi, v);
                }
            }
            var blob = b.CreateBlobAssetReference<PaintedEditsBlob>(allocator);
            b.Dispose();
            LastLog = $"[map-edits] {reason} ({count * 64 * 64 / 1e6f:F2} km2 painted), offsets {lo:F1}..{hi:F1} m";
            if (painted != version)
            {
                LastLog += $"; painted on island v{painted}, baking v{version}";
                Debug.LogWarning(LastLog);
            }
            return blob;
        }

        /// <summary>For tests: the bake's offsets at (xs[i], zs[i]) for a file's bytes; null when the file bakes as none.</summary>
        public static float[] Probe(byte[] bytes, int version, float[] xs, float[] zs)
        {
            var blob = Build(bytes, version, Allocator.Temp);
            if (!blob.IsCreated) return null;
            var result = new float[xs.Length];
            for (int i = 0; i < xs.Length; i++) result[i] = PaintedEditsBlob.Sample(ref blob.Value, new float2(xs[i], zs[i]));
            blob.Dispose();
            return result;
        }
    }
}
