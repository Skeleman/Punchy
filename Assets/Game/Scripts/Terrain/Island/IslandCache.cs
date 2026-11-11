using System.Text;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-140: the island cache. Every blob the island bake writes into <see cref="TerrainSource"/>, in one list, so the
    /// cache, its size report and its tests all walk the same fields. A new island blob must be added to
    /// <see cref="Each{V}"/> (a test fails on any TerrainSource blob that is neither listed here nor in
    /// <see cref="NotCached"/>).
    /// </summary>
    public static class IslandCache
    {
        /// <summary>TerrainSource blobs the island bake does not write: the subscene's own (heightmap, biome table) and
        /// the v1 biome cells (the bake sets them to default).</summary>
        public static readonly string[] NotCached = { nameof(TerrainSource.Heightmap), nameof(TerrainSource.Biomes), nameof(TerrainSource.BiomeCells) };

        public interface IBlobVisitor
        {
            void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged;
        }

        /// <summary>Visits every blob the island bake writes, in a fixed order (the file order).</summary>
        public static void Each<V>(ref TerrainSource s, ref V v) where V : IBlobVisitor
        {
            v.Visit(nameof(s.BiomeMap), ref s.BiomeMap);
            v.Visit(nameof(s.Island), ref s.Island);
            v.Visit(nameof(s.V3Ground), ref s.V3Ground);
            v.Visit(nameof(s.Rivers), ref s.Rivers);
            v.Visit(nameof(s.Landmarks), ref s.Landmarks);
            v.Visit(nameof(s.Settlements), ref s.Settlements);
            v.Visit(nameof(s.Caves), ref s.Caves);
            v.Visit(nameof(s.BatFolk), ref s.BatFolk);
            v.Visit(nameof(s.Icebergs), ref s.Icebergs);
            v.Visit(nameof(s.GrandFalls), ref s.GrandFalls);
            v.Visit(nameof(s.Archipelago), ref s.Archipelago);
            v.Visit(nameof(s.Anchors), ref s.Anchors);
            v.Visit(nameof(s.PondMask), ref s.PondMask);
        }

        struct Sizer : IBlobVisitor
        {
            public StringBuilder Text;
            public long Total;

            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged
            {
                long bytes = 0;
                if (blob.IsCreated)
                {
                    var writer = new MemoryBinaryWriter();
                    writer.Write(blob);
                    bytes = writer.Length;
                    writer.Dispose();
                }
                Total += bytes;
                Text.Append(' ').Append(name).Append(' ').Append((bytes / 1024.0).ToString("F0")).Append('K');
            }
        }

        /// <summary>Each island blob's serialised size ("[island] blob sizes ..."), for the cache's file size.</summary>
        public static string SizeReport(ref TerrainSource source)
        {
            var sizer = new Sizer { Text = new StringBuilder() };
            Each(ref source, ref sizer);
            return $"[island] blob sizes {sizer.Total / (1024.0 * 1024.0):F2} MB:{sizer.Text}";
        }
    }
}
