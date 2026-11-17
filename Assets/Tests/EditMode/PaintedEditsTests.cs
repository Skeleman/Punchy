using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// M-07 r2b: the bake's reading of the edits file (<c>Punchy.Terrain.PaintedEdits</c>) must give the same offsets as
    /// the map editor's own <c>IslandEdits.Sample</c> everywhere, tile edges and negative coordinates included, and an
    /// empty file must bake as no file (no blob, no cache key change). Both are reached by reflection: this assembly
    /// can't reference Assembly-CSharp or Assembly-CSharp-Editor.
    /// </summary>
    public class PaintedEditsTests
    {
        static Type Find(string assembly, string type) =>
            AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == assembly).GetType(type);

        static Type Edits => Find("Assembly-CSharp-Editor", "Punchy.EditorTools.IslandEdits");
        static Type Painted => Find("Assembly-CSharp", "Punchy.Terrain.PaintedEdits");

        static object NewEdits(int islandVersion)
        {
            var e = Activator.CreateInstance(Edits);
            Edits.GetField("IslandVersion").SetValue(e, islandVersion);
            return e;
        }

        static byte[] Bytes(object e) => (byte[])Edits.GetMethod("ToBytes").Invoke(e, null);

        [Test]
        public void BakeSamplesMatchTheEditor()
        {
            Assert.IsNotNull(Edits, "IslandEdits not found");
            Assert.IsNotNull(Painted, "PaintedEdits not found");
            var e = NewEdits(3);
            var set = Edits.GetMethod("SetVertex");
            // A hill across the corner of four tiles at the origin, and a dip far off in negative space.
            for (int gz = -20; gz <= 20; gz++)
                for (int gx = -20; gx <= 20; gx++)
                {
                    float r = MathF.Sqrt(gx * gx + gz * gz) / 20f;
                    if (r < 1f) set.Invoke(e, new object[] { gx, gz, 12f * (1f - r * r) });
                }
            for (int gz = -1210; gz <= -1190; gz++)
                for (int gx = 700; gx <= 735; gx++)
                    set.Invoke(e, new object[] { gx, gz, -3.5f });
            var bytes = Bytes(e);
            var rng = new Random(7);
            int n = 4000;
            var xs = new float[n]; var zs = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool far = i % 2 == 1;
                xs[i] = far ? 1390f + (float)rng.NextDouble() * 100f : -50f + (float)rng.NextDouble() * 100f;
                zs[i] = far ? -2430f + (float)rng.NextDouble() * 60f : -50f + (float)rng.NextDouble() * 100f;
            }
            // Exact tile corners and edges too.
            xs[0] = 0f; zs[0] = 0f; xs[2] = 64f; zs[2] = -0.001f; xs[4] = -64f; zs[4] = 63.999f;
            var baked = (float[])Painted.GetMethod("Probe").Invoke(null, new object[] { bytes, 3, xs, zs });
            Assert.IsNotNull(baked, "a painted file baked as none");
            // The editor's own copy, read back from the same bytes (half floats), is the reference.
            var loaded = Edits.GetMethod("FromBytes").Invoke(null, new object[] { bytes, null });
            var sample = Edits.GetMethod("Sample");
            var float2 = sample.GetParameters()[0].ParameterType;   // Unity.Mathematics.float2 (not referenced here)
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                float want = (float)sample.Invoke(loaded, new object[] { Activator.CreateInstance(float2, xs[i], zs[i]) });
                worst = MathF.Max(worst, MathF.Abs(want - baked[i]));
            }
            Assert.Less(worst, 1e-5f, $"bake and editor differ by {worst} m");
        }

        [Test]
        public void EmptyFileBakesAsNone()
        {
            Assert.IsNotNull(Painted, "PaintedEdits not found");
            var bytes = Bytes(NewEdits(3));
            Assert.IsNull(Painted.GetMethod("Hash").Invoke(null, new object[] { bytes }), "an empty file changes the cache key");
            Assert.IsNull(Painted.GetMethod("Probe").Invoke(null, new object[] { bytes, 3, new[] { 0f }, new[] { 0f } }), "an empty file made a blob");
            bytes[30] ^= 1;   // damaged: the checksum fails, so it bakes as none too
            Assert.IsNull(Painted.GetMethod("Probe").Invoke(null, new object[] { bytes, 3, new[] { 0f }, new[] { 0f } }), "a damaged file made a blob");
        }
    }
}
