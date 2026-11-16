using System.Collections.Generic;
using Punchy.Settings;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Part = Punchy.Terrain.CaveRoomKit.Part;

namespace Punchy.Fauna
{
    /// <summary>
    /// #24 phase 2 row 4 (rule 20): the dungeon props are drawn, not kept as GameObjects. Each prop is a record (its
    /// content, box and place) and is drawn every frame by how big it looks (<see cref="DetailPolicy.ScreenSize"/> of
    /// its box's largest side, <see cref="DetailPolicy.Tier"/> with hysteresis, x Detail): all its parts while bigger
    /// than <see cref="Tiers"/>.Near, only its biggest part (the crate's planked box, the barrel's drum) while bigger
    /// than Mid, else nothing. At the reference view a 1 m prop keeps every part to about 47 m and its biggest part to
    /// about 310 m. Mesh sets are shared per content and box size, as before. Drawn through Graphics.RenderMesh, so the
    /// SRP batcher takes them (the cave material has no GPU instancing to batch them further). The collision boxes
    /// (DungeonPropBox) are untouched: that is simulation, not drawing.
    /// </summary>
    public static partial class DungeonProps
    {
        /// <summary>Reference-pixel boundaries: tier 0 every part, 1 the biggest part alone, 2 or 3 not drawn.</summary>
        public static readonly DetailPolicy.Thresholds Tiers = new DetailPolicy.Thresholds(20f, 3f, 0f);

        struct Placed
        {
            public (DungeonContent, int) Key;
            public float3 Size;
            public Matrix4x4 Matrix;
            public float3 Centre;
            public float Extent;
            public Bounds Bounds;
            public int Tier;
        }

        static readonly List<Placed> placed = new();
        static readonly Dictionary<(DungeonContent, int), (Mesh mesh, int group)> Lows = new();
        static int full, low, hidden;
        static DungeonPropDrawer drawer;

        /// <summary>Adds a prop to draw: its content and the box <see cref="DungeonSpawner.PropPlace"/> gave it.</summary>
        public static void Place(DungeonContent c, float3 centre, float3 size, quaternion rot)
        {
            var extent = math.cmax(size);
            placed.Add(new Placed
            {
                Key = KeyOf(c, size),
                Size = size,
                Matrix = Matrix4x4.TRS(centre, rot, Vector3.one),
                Centre = centre,
                Extent = extent,
                Bounds = new Bounds(centre, Vector3.one * math.length(size)),
                Tier = -1
            });
            if (drawer == null)
            {
                var go = new GameObject("Dungeon prop drawer") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                drawer = go.AddComponent<DungeonPropDrawer>();
            }
        }

        /// <summary>Forgets every prop (the world that placed them is going).</summary>
        public static void Clear()
        {
            placed.Clear();
            full = low = hidden = 0;
        }

        /// <summary>How many props there are and how each was drawn last frame, for logs and tests.</summary>
        public static string Describe() =>
            $"{placed.Count} props: {full} with every part, {low} as their biggest part, {hidden} too small to draw";

        /// <summary>The props placed, and how many were drawn whole, as their biggest part, or not at all last frame.</summary>
        public static (int placed, int full, int low, int hidden) Counts => (placed.Count, full, low, hidden);

        /// <summary>
        /// The tier a prop of this size (m, its box's largest side) gets at this distance (m, to its nearest point) at
        /// the reference view and Detail 1, from scratch: 0 every part, 1 the biggest part, 2 or 3 not drawn.
        /// </summary>
        public static int TierAt(float extent, float distance) =>
            DetailPolicy.Tier(DetailPolicy.ScreenSize(extent, distance, DetailPolicy.PixelsPerMetreAt(DetailPolicy.ReferenceFov)), Tiers, -1);

        static (DungeonContent, int) KeyOf(DungeonContent c, float3 size) =>
            (c, (int)math.round(size.x * 100f) | (int)math.round(size.y * 100f) << 10 | (int)math.round(size.z * 100f) << 20);

        /// <summary>Draws every prop by its tier (once a frame, after the camera has moved: <see cref="DungeonPropDrawer"/>).</summary>
        internal static void Draw()
        {
            full = low = hidden = 0;
            if (placed.Count == 0)
                return;
            var cam = Camera.main;
            var mats = Materials();
            if (cam == null || mats == null)
                return;
            // (Not DetailPolicy.SetView: the first caller each frame sets it for everyone, and it is set by now. Only
            // the camera is read here, so a prop's tier never lags a frame behind it.)
            var camPos = (float3)cam.transform.position;
            var perMetre = DetailPolicy.PixelsPerMetreAt(cam.fieldOfView);
            var detail = DetailPolicy.Current.Data.Detail;
            var tiers = Tiers.Scaled(detail > 0f ? math.max(detail, 0.1f) : 1f);
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                var distance = math.max(math.distance(camPos, p.Centre) - p.Extent * 0.5f, 0.01f);
                p.Tier = DetailPolicy.Tier(DetailPolicy.ScreenSize(p.Extent, distance, perMetre), tiers, p.Tier);
                placed[i] = p;
                if (p.Tier >= 2)
                {
                    hidden++;
                    continue;
                }
                if (p.Tier == 0)
                {
                    full++;
                    var meshes = FullMeshes(p.Key, p.Size);
                    for (int g = 0; g < meshes.Length; g++)
                        if (meshes[g] != null)
                            Graphics.RenderMesh(Params(mats[g], p.Bounds), meshes[g], 0, p.Matrix);
                }
                else
                {
                    low++;
                    var (mesh, group) = LowMesh(p.Key, p.Size);
                    if (mesh != null)
                        Graphics.RenderMesh(Params(mats[group], p.Bounds), mesh, 0, p.Matrix);
                }
            }
        }

        static RenderParams Params(Material mat, Bounds bounds) => new RenderParams(mat)
        {
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true
        };

        /// <summary>The prop's whole mesh set, one mesh per material (null where it has none of it).</summary>
        static Mesh[] FullMeshes((DungeonContent c, int size) key, float3 size)
        {
            // (Rebuilt if Unity dropped the cached meshes, e.g. play mode ended without a domain reload.)
            if (!Meshes.TryGetValue(key, out var meshes) || !Alive(meshes))
            {
                var kit = new Punchy.Terrain.CaveRoomKit.Kit();
                Build(kit.Parts, key.c, size, Seed(key));
                meshes = Punchy.Terrain.CaveRoomKit.BuildMeshes(kit);
                Meshes[key] = meshes;
            }
            return meshes;
        }

        /// <summary>The prop's biggest part (by volume) alone, in its material: what still reads a few pixels across.</summary>
        static (Mesh, int) LowMesh((DungeonContent c, int size) key, float3 size)
        {
            if (Lows.TryGetValue(key, out var lowMesh) && lowMesh.mesh != null)
                return lowMesh;
            var parts = new List<Part>();
            Build(parts, key.c, size, Seed(key));
            if (parts.Count == 0)
                return (null, 0);
            var biggest = parts[0];
            foreach (var part in parts)
                if (Volume(part) > Volume(biggest))
                    biggest = part;
            var kit = new Punchy.Terrain.CaveRoomKit.Kit();
            kit.Parts.Add(biggest);
            var meshes = Punchy.Terrain.CaveRoomKit.BuildMeshes(kit);
            lowMesh = (meshes[biggest.Group], biggest.Group);
            Lows[key] = lowMesh;
            return lowMesh;
        }

        static float Volume(in Part p) => p.Half.x * p.Half.y * p.Half.z;

        static uint Seed((DungeonContent c, int size) key) => (uint)key.size ^ (uint)key.c * 0x9E3779B1u;
    }
}
