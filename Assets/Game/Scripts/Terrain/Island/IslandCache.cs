using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using BinaryWriter = Unity.Entities.Serialization.BinaryWriter;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-140: the island cache. The island bake (v2/v3, ~14 s) is saved to a file after a fresh bake and loaded at the
    /// next launch when its key matches, so playtests and the profiler skip the bake. The first step of #260's world file.
    /// The key covers everything the bake reads: the island version, the bake flags (lake shelf, springs), the subscene's
    /// inputs byte for byte, and a stamp of the code and assets (editor: the game assembly, the Unity version, the package
    /// lock and every asset the world scene and the landmark types depend on; player: the stamp the build wrote next to
    /// the file). Any doubt (no stamp, an unreadable file, a changed key, a bad checksum) means a fresh bake and a
    /// rewrite, never a failure. <c>-freshisland</c> / PUNCHY_FRESHISLAND=1 always bakes fresh and writes nothing.
    /// Every blob the island bake writes into <see cref="TerrainSource"/> is in one list, <see cref="Each{V}"/>, so the
    /// file, its size report and its tests all walk the same fields (a test fails on any TerrainSource blob that is
    /// neither listed there nor in <see cref="NotCached"/>).
    /// </summary>
    public static class IslandCache
    {
        /// <summary>Bumped whenever the file layout changes (old files then miss and are rewritten).</summary>
        public const int Format = 1;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PICF");
        const int KeyBytes = 32, SumBytes = 32, BlobHeaderBytes = 32;

        /// <summary>TerrainSource blobs the island bake does not write: the subscene's own (heightmap, biome table) and
        /// the v1 biome cells (the bake sets them to default).</summary>
        public static readonly string[] NotCached = { nameof(TerrainSource.Heightmap), nameof(TerrainSource.Biomes), nameof(TerrainSource.BiomeCells) };

        /// <summary>The source as the subscene gave it, before the last bake or load changed it (for the tests).</summary>
        public static TerrainSource LastInputs;

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

        // ---- the switch ----

        /// <summary><c>-freshisland</c> or PUNCHY_FRESHISLAND=1: bake fresh, read and write no cache.</summary>
        public static bool FreshRequested() =>
            Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-freshisland") >= 0
            || Environment.GetEnvironmentVariable("PUNCHY_FRESHISLAND") == "1";

        // ---- the key ----

        /// <summary>
        /// The cache key (hex) for a bake of this source (as the subscene gave it), palette, landmark types and version,
        /// or null with the reason when no cache may be used (the switch, a debug bake, no stamp).
        /// </summary>
        public static string Key(ref TerrainSource source, BiomePalette palette, List<LandmarkType> types, int version, out string reason)
        {
            if (FreshRequested()) { reason = "bypass (-freshisland)"; return null; }
            // #72: the test campsite is a debug-only extra place: never cached, never loaded.
            if (CampsiteTest.Requested() != null) { reason = "bypass (-campsitetest)"; return null; }
            string stamp = Stamp(types, out reason);
            LastStamp = stamp;
            if (stamp == null) return null;
            try
            {
                using var ms = new MemoryStream();
                void Text(string s) { var b = Encoding.UTF8.GetBytes(s ?? "-"); ms.Write(BitConverter.GetBytes(b.Length), 0, 4); ms.Write(b, 0, b.Length); }
                Text("Punchy island cache"); Text(Format.ToString()); Text(version.ToString());
                // Bake flags read inside the bake steps (rule 22: these should become world settings).
                Text(GrandFalls.LakeShelfOn() ? "lakeshelf" : "-");
                Text(Environment.GetEnvironmentVariable("PUNCHY_SPRINGS"));
                Text(stamp);
                // The subscene's inputs, byte for byte.
                Struct(ms, ref source.World); Struct(ms, ref source.BiomeNoise); Struct(ms, ref source.RiverSettings);
                Struct(ms, ref source.SpawnCell); Struct(ms, ref source.HeightKind); Struct(ms, ref source.BiomeKind);
                Struct(ms, ref source.PondsOn); Struct(ms, ref source.RiverRoughOn);
                foreach (var bytes in new[] { BlobBytes(source.Biomes), BlobBytes(source.Heightmap), BlobBytes(source.BiomeMap), BlobBytes(source.BiomeCells) })
                {
                    if (bytes == null) { reason = "blob unreadable"; return null; }
                    ms.Write(BitConverter.GetBytes(bytes.Length), 0, 4); ms.Write(bytes, 0, bytes.Length);
                }
                // The palette by name (its contents are in the stamp: the assets' dependency hashes, or the build's).
                foreach (var b in palette.Biomes ?? Array.Empty<BiomeDefinition>()) Text(b != null ? b.name : null);
                foreach (var t in types) Text(t != null ? t.name : null);
                Text($"{palette.Landmarks?.Length} {palette.Placed?.Count} {palette.Roadside?.Count} {palette.BiomeRealms?.Length}");
                ms.Position = 0;
                using var sha = SHA256.Create();
                reason = "key";
                return Hex(sha.ComputeHash(ms));
            }
            catch (Exception e)
            {
                reason = "key failed: " + e.GetType().Name;
                return null;
            }
        }

        static void Struct<T>(MemoryStream ms, ref T value) where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).ToArray();
            ms.Write(bytes, 0, bytes.Length);
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static byte[] Unhex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        /// <summary>The stamp the last key was made with (the editor saves it beside the file, for the build step).</summary>
        public static string LastStamp;

        /// <summary>The editor's record of the stamp an island file was baked under: its path plus this.</summary>
        public const string StampSuffix = ".stamp";

        /// <summary>The player's stamp file, written by the build step next to the shipped island file.</summary>
        public const string StampFile = "stamp.txt";
        public const string Folder = "IslandCache";

        /// <summary>What the code and assets the bake reads look like (editor: hashed now; player: the build's stamp).</summary>
        static string Stamp(List<LandmarkType> types, out string reason)
        {
#if UNITY_EDITOR
            return EditorStamp(types, out reason);
#else
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, Folder, StampFile);
                if (!File.Exists(path)) { reason = "no stamp (a build without the island cache)"; return null; }
                reason = "stamp";
                return File.ReadAllText(path).Trim();
            }
            catch (Exception e) { reason = "stamp unreadable: " + e.GetType().Name; return null; }
#endif
        }

#if UNITY_EDITOR
        /// <summary>The scene that holds the island's subscene (its TerrainAuthoring and palette).</summary>
        public const string WorldScene = "Assets/Game/Scenes/World.unity";

        /// <summary>
        /// Editor: a hash of the game assembly, the Unity version, the package lock, and the dependency hash of every asset
        /// the world scene or any landmark type depends on (biomes, landmarks, surfaces, prefabs, scripts: all landmark
        /// types, not just the ones the bake read, so the build step can stamp a player without play mode). The bake's
        /// own types must all be among them. Anything missing gives null (no cache).
        /// </summary>
        public static string EditorStamp(List<LandmarkType> types, out string reason)
        {
            try
            {
                var root = Path.GetDirectoryName(Application.dataPath);
                var dll = Path.Combine(root, "Library", "ScriptAssemblies", "Assembly-CSharp.dll");
                var lockFile = Path.Combine(root, "Packages", "packages-lock.json");
                if (!File.Exists(dll)) { reason = "no stamp (no game assembly)"; return null; }
                if (!File.Exists(lockFile)) { reason = "no stamp (no package lock)"; return null; }
                var roots = new List<string> { WorldScene };
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(LandmarkType)))
                    roots.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                var paths = new SortedSet<string>(UnityEditor.AssetDatabase.GetDependencies(roots.ToArray(), true), StringComparer.Ordinal);
                if (!paths.Contains(WorldScene)) { reason = "no stamp (no world scene)"; return null; }
                if (types != null)
                    foreach (var t in types)
                    {
                        var p = t != null ? UnityEditor.AssetDatabase.GetAssetPath(t) : null;
                        if (string.IsNullOrEmpty(p) || !paths.Contains(p)) { reason = $"no stamp (landmark type {(t != null ? t.name : "null")} outside the stamp)"; return null; }
                    }
                using var sha = SHA256.Create();
                using var ms = new MemoryStream();
                void Text(string s) { var b = Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); ms.WriteByte(0); }
                Text(Application.unityVersion);
                Text(Hex(sha.ComputeHash(File.ReadAllBytes(dll))));
                Text(File.ReadAllText(lockFile));
                foreach (var p in paths) { Text(p); Text(UnityEditor.AssetDatabase.GetAssetDependencyHash(p).ToString()); }
                ms.Position = 0;
                reason = "stamp";
                return Hex(sha.ComputeHash(ms));
            }
            catch (Exception e)
            {
                reason = "no stamp (" + e.GetType().Name + ")";
                return null;
            }
        }
#endif

        // ---- blob bytes (through Entities' internal file writer, found by reflection: this assembly has no unsafe code) ----

        static Type writerType;

        static BinaryWriter OpenWriter(string path)
        {
            writerType ??= typeof(BinaryWriter).Assembly.GetType("Unity.Entities.Serialization.StreamBinaryWriter");
            if (writerType == null) return null;
            return (BinaryWriter)Activator.CreateInstance(writerType,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { path, 1 << 20, default(EntityManager) }, null);
        }

        static string TempPath(string tag) =>
            Path.Combine(Application.temporaryCachePath, $"island-cache-{tag}-{System.Diagnostics.Process.GetCurrentProcess().Id}.tmp");

        /// <summary>One blob as Entities serialises it (32-byte header, then the data); empty for a blob that isn't
        /// created; null if it can't be read.</summary>
        public static byte[] BlobBytes<T>(BlobAssetReference<T> blob) where T : unmanaged
        {
            if (!blob.IsCreated) return Array.Empty<byte>();
            var path = TempPath("blob");
            try
            {
                using (var writer = OpenWriter(path))
                {
                    if (writer == null) return null;
                    writer.Write(blob);
                }
                return File.ReadAllBytes(path);
            }
            catch (Exception) { return null; }
            finally { try { File.Delete(path); } catch (Exception) { } }
        }

        // ---- the file ----

        struct BlobWriter : IBlobVisitor
        {
            public BinaryWriter Writer;

            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged
            {
                var n = Encoding.UTF8.GetBytes(name);
                Writer.Write(n.Length);
                Writer.Write(n);
                Writer.Write(blob.IsCreated ? 1 : 0);
                if (blob.IsCreated) Writer.Write(blob);
            }
        }

        static int BlobCount()
        {
            var counter = new Counter();
            var none = default(TerrainSource);
            Each(ref none, ref counter);
            return counter.N;
        }

        struct Counter : IBlobVisitor
        {
            public int N;
            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged => N++;
        }

        /// <summary>
        /// Writes the island file for a baked source: header (magic, format, version, key, blob count), the scalars the
        /// bake sets (biome kind, river settings), every <see cref="Each{V}"/> blob in order, then a SHA-256 of all of it.
        /// </summary>
        public static bool WriteFile(string path, ref TerrainSource source, int version, string key)
        {
            using (var writer = OpenWriter(path))
            {
                if (writer == null) return false;
                writer.Write(Magic);
                writer.Write(Format);
                writer.Write(version);
                writer.Write(Unhex(key));
                writer.Write(BlobCount());
                writer.Write((int)source.BiomeKind);
                var river = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref source.RiverSettings, 1)).ToArray();
                writer.Write(river.Length);
                writer.Write(river);
                var blobs = new BlobWriter { Writer = writer };
                Each(ref source, ref blobs);
            }
            byte[] sum;
            using (var sha = SHA256.Create())
            using (var read = File.OpenRead(path))
                sum = sha.ComputeHash(read);
            using (var append = new FileStream(path, FileMode.Append, FileAccess.Write))
                append.Write(sum, 0, sum.Length);
            return true;
        }

        /// <summary>Where this process reads the island file from, first match wins (editor: the project's Library;
        /// player: the build's StreamingAssets, then the player's own data folder).</summary>
        public static List<string> ReadPaths(int version)
        {
            var name = $"island-v{version}.bin";
#if UNITY_EDITOR
            return new List<string> { Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", Folder, name) };
#else
            return new List<string> { Path.Combine(Application.streamingAssetsPath, Folder, name), Path.Combine(Application.persistentDataPath, Folder, name) };
#endif
        }

        /// <summary>Where this process writes the island file after a fresh bake.</summary>
        public static string WritePath(int version)
        {
            var name = $"island-v{version}.bin";
#if UNITY_EDITOR
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", Folder, name);
#else
            return Path.Combine(Application.persistentDataPath, Folder, name);
#endif
        }

        /// <summary>Saves a fresh bake: to a temp file named for this process, then moved into place, so a reader never
        /// sees half a file. IO errors (a second game writing the same file) only mean no cache this time.</summary>
        public static void Save(ref TerrainSource source, int version, string key)
        {
            var path = WritePath(version);
            var temp = $"{path}.{System.Diagnostics.Process.GetCurrentProcess().Id}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!WriteFile(temp, ref source, version, key))
                {
                    Debug.Log("[island] cache not saved (no blob writer)");
                    return;
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
#if UNITY_EDITOR
                // The build step ships this file only while the editor's stamp still reads the same.
                File.WriteAllText(path + StampSuffix, LastStamp ?? "");
#endif
            }
            catch (Exception e)
            {
                Debug.Log($"[island] cache not saved ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
            }
        }

        /// <summary>Loads the island file over the source when one matches the key; false (and the source untouched)
        /// otherwise, with the reason.</summary>
        public static bool TryLoad(ref TerrainSource source, int version, string key, out string reason)
        {
            reason = "no file";
            foreach (var path in ReadPaths(version))
            {
                if (!File.Exists(path)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (Exception e) { reason = "unreadable (" + e.GetType().Name + ")"; continue; }
                if (Parse(bytes, ref source, version, key, out reason))
                {
                    reason = $"{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))}/{Folder}/{Path.GetFileName(path)} {bytes.Length / (1024.0 * 1024.0):F2} MB";
                    return true;
                }
            }
            return false;
        }

        struct Clearer : IBlobVisitor
        {
            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged => blob = default;
        }

        struct Disposer : IBlobVisitor
        {
            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged
            {
                if (blob.IsCreated) blob.Dispose();
                blob = default;
            }
        }

        /// <summary>Reads one blob after another out of the file's bytes, each length checked against what is left.</summary>
        struct BlobReader : IBlobVisitor
        {
            public byte[] Bytes;
            public int At, End;
            public string Failed;

            public bool Int(out int value)
            {
                value = 0;
                if (At + 4 > End) return false;
                value = BitConverter.ToInt32(Bytes, At);
                At += 4;
                return true;
            }

            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged
            {
                if (Failed != null) return;
                if (!Int(out int nameLength) || nameLength < 0 || At + nameLength > End) { Failed = "truncated at " + name; return; }
                var stored = Encoding.UTF8.GetString(Bytes, At, nameLength);
                At += nameLength;
                if (stored != name) { Failed = $"blob {stored} where {name} belongs"; return; }
                if (!Int(out int created) || (created != 0 && created != 1)) { Failed = "bad flag at " + name; return; }
                if (created == 0) return;
                if (At + BlobHeaderBytes > End) { Failed = "truncated at " + name; return; }
                int length = BitConverter.ToInt32(Bytes, At + 8);
                ulong hash = BitConverter.ToUInt64(Bytes, At + 16);
                At += BlobHeaderBytes;
                if (length <= 0 || length > End - At) { Failed = "bad length at " + name; return; }
                var data = new byte[length];
                Buffer.BlockCopy(Bytes, At, data, 0, length);
                At += length;
                blob = BlobAssetReference<T>.Create(data);
                KeepHash(ref blob, length, hash);
            }
        }

        /// <summary>
        /// Gives a loaded blob the header hash the baked one had. Create hashes the data, but a blob changed in place after
        /// it was built (the rivers) keeps the hash of its first contents, and the loaded island must be the baked one byte
        /// for byte. The header sits 32 bytes before the data (hash at +16); its address comes from the reference's
        /// internal 64-bit union by reflection, and Marshal writes it (this assembly has no unsafe code). Nothing at
        /// runtime reads the hash, so if Entities ever moves these fields this only leaves the round-trip test failing.
        /// </summary>
        static void KeepHash<T>(ref BlobAssetReference<T> blob, int length, ulong hash) where T : unmanaged
        {
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var dataField = typeof(BlobAssetReference<T>).GetField("m_data", Any);
                object data = dataField?.GetValue(blob);
                var union = data?.GetType().GetField("m_Align8Union", Any);
                if (union == null || union.FieldType != typeof(long)) return;
                long address = (long)union.GetValue(data);
                if (address == 0 || Marshal.ReadInt32(new IntPtr(address - 24)) != length) return;
                if ((ulong)Marshal.ReadInt64(new IntPtr(address - 16)) != hash) Marshal.WriteInt64(new IntPtr(address - 16), (long)hash);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Checks the file's bytes (checksum, magic, format, version, key, blob count, every length) and, only if all of
        /// it holds, writes the loaded blobs and scalars into the source. On any failure the source is untouched and the
        /// partly loaded blobs are freed.
        /// </summary>
        public static bool Parse(byte[] bytes, ref TerrainSource source, int version, string key, out string reason)
        {
            int headerBytes = Magic.Length + 4 + 4 + KeyBytes + 4;
            if (bytes.Length < headerBytes + SumBytes) { reason = "file too short"; return false; }
            int end = bytes.Length - SumBytes;
            using (var sha = SHA256.Create())
            {
                var sum = sha.ComputeHash(bytes, 0, end);
                for (int i = 0; i < SumBytes; i++)
                    if (sum[i] != bytes[end + i]) { reason = "bad checksum"; return false; }
            }
            for (int i = 0; i < Magic.Length; i++)
                if (bytes[i] != Magic[i]) { reason = "not an island file"; return false; }
            int at = Magic.Length;
            if (BitConverter.ToInt32(bytes, at) != Format) { reason = "old file format"; return false; }
            if (BitConverter.ToInt32(bytes, at + 4) != version) { reason = "other island version"; return false; }
            at += 8;
            var want = Unhex(key);
            for (int i = 0; i < KeyBytes; i++)
                if (bytes[at + i] != want[i]) { reason = "key changed"; return false; }
            at += KeyBytes;
            if (BitConverter.ToInt32(bytes, at) != BlobCount()) { reason = "blob list changed"; return false; }
            at += 4;

            var reader = new BlobReader { Bytes = bytes, At = at, End = end };
            if (!reader.Int(out int biomeKind)) { reason = "truncated"; return false; }
            int riverSize = UnsafeUtility.SizeOf<RiverSettings>();
            if (!reader.Int(out int riverLength) || riverLength != riverSize || reader.At + riverSize > end) { reason = "river settings changed"; return false; }
            var river = MemoryMarshal.Read<RiverSettings>(new ReadOnlySpan<byte>(bytes, reader.At, riverSize));
            reader.At += riverSize;

            var loaded = source;
            var clear = new Clearer();
            Each(ref loaded, ref clear);
            try { Each(ref loaded, ref reader); }
            catch (Exception e) { reader.Failed = "unreadable (" + e.GetType().Name + ")"; }
            if (reader.Failed == null && reader.At != end) reader.Failed = "extra bytes";
            if (reader.Failed != null)
            {
                var free = new Disposer();
                Each(ref loaded, ref free);
                reason = reader.Failed;
                return false;
            }
            loaded.BiomeKind = (BiomeSourceKind)biomeKind;
            loaded.RiverSettings = river;
            loaded.BiomeCells = default;
            source = loaded;
            reason = "loaded";
            return true;
        }

        /// <summary>Frees every island blob in a source this code loaded or baked (tests only: the live island's blobs
        /// are kept for the process).</summary>
        public static void Free(ref TerrainSource source)
        {
            var free = new Disposer();
            Each(ref source, ref free);
        }
    }
}
