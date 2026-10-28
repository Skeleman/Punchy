using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>
    /// Flames anyone can light for a frame: a torch carried by a guard, a cave torch on a wall, a campfire. Callers add
    /// each flame every frame (position of its base, height, a stable seed, a palette, and how far its tip trails); the
    /// LandmarkEffectSystem draws them all at once with the Flame shader, which shapes and flickers them on the shared
    /// clock, and the list is cleared. A flame's lean is eased between frames by its seed, so a jerky carrier doesn't
    /// snap it about. Light on their surroundings is separate (CaveSystem.MovingLights): flames are emissive only.
    /// </summary>
    public static class FireSystem
    {
        /// <summary>Palettes: 0 wood fire, 1 blue, 2 green, 3 violet.</summary>
        public const int Wood = 0, Blue = 1, Green = 2, Violet = 3;

        struct Flame
        {
            public Vector3 Position, Lean;
            public float Size;
            public int Seed, Palette;
        }

        struct FlameVertex
        {
            public float3 Position;
            public float4 Corner;   // x -1..1, y 0..1, seed 0..1, palette (+10 for a spark)
            public float4 Shape;    // lean xyz, size

            public static readonly VertexAttributeDescriptor[] Layout =
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4)
            };
        }

        static readonly List<Flame> flames = new List<Flame>();
        static Dictionary<int, Vector3> eased = new Dictionary<int, Vector3>(), easedNext = new Dictionary<int, Vector3>();
        static readonly List<FlameVertex> vertices = new List<FlameVertex>();
        static readonly List<int> indices = new List<int>();
        static Mesh mesh;

        /// <summary>A flame this frame. Main thread only; add it again every frame it burns.</summary>
        /// <param name="position">The base of the flame (a torch head, the logs of a fire).</param>
        /// <param name="size">Its height (m): a torch about 0.35, a campfire 0.8–1.2.</param>
        /// <param name="seed">Anything that stays the same while it burns (an entity index), so its flicker is its own.</param>
        /// <param name="palette">Wood, Blue, Green or Violet.</param>
        public static void AddFlame(Vector3 position, float size, int seed, int palette = Wood) =>
            AddFlame(position, size, seed, palette, Vector3.zero);

        /// <param name="lean">Where the tip trails, in world metres (added to the wind's lean): e.g. a carrier's
        /// velocity × -0.05, clamped to about 0.3. Eased between frames.</param>
        public static void AddFlame(Vector3 position, float size, int seed, int palette, Vector3 lean)
        {
            if (flames.Count < 4096)
                flames.Add(new Flame { Position = position, Size = size, Seed = seed, Palette = palette, Lean = lean });
        }

        /// <summary>Draws this frame's flames and clears the list (LandmarkEffectSystem, once a frame).</summary>
        public static void Draw(Material material)
        {
            if (flames.Count == 0 || material == null)
            {
                flames.Clear();
                eased.Clear();
                return;
            }
            var dt = math.min(Time.deltaTime, 0.1f);
            var follow = 1f - math.exp(-dt / 0.12f);
            vertices.Clear();
            indices.Clear();
            easedNext.Clear();
            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            foreach (var f in flames)
            {
                var lean = eased.TryGetValue(f.Seed, out var was) ? Vector3.Lerp(was, f.Lean, follow) : f.Lean;
                easedNext[f.Seed] = lean;
                var seed01 = (math.hash(new int2(f.Seed, 0x5EED)) & 0xFFFF) / 65535f;
                // The flame, then its spark.
                for (int quad = 0; quad < 2; quad++)
                {
                    var first = vertices.Count;
                    for (int k = 0; k < 4; k++)
                        vertices.Add(new FlameVertex
                        {
                            Position = f.Position,
                            Corner = new float4(k == 0 || k == 3 ? -1f : 1f, k < 2 ? 0f : 1f, seed01, f.Palette + quad * 10),
                            Shape = new float4(lean.x, lean.y, lean.z, f.Size)
                        });
                    indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                    indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
                }
                min = math.min(min, (float3)f.Position);
                max = math.max(max, (float3)f.Position);
            }
            (eased, easedNext) = (easedNext, eased);
            flames.Clear();

            if (mesh == null)
            {
                mesh = new Mesh { name = "Flames" };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            mesh.SetVertexBufferParams(vertices.Count, FlameVertex.Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
            mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Count);
            mesh.subMeshCount = 1;
            // Bounds round every base, with room for the tallest flame and its sparks.
            var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min + 8f));
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Count), MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = bounds;
            Graphics.RenderMesh(new RenderParams(material) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                                mesh, 0, Matrix4x4.identity);
        }
    }
}
