using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// A far shadow proxy for one huge landmark (W-77b, #84). Real shadows stop at the pipeline's shadow distance
    /// (40 m), so from afar the salt tower stood with no shadow at all. Raising the distance for everything costs
    /// every frame; this instead lays one ground-hugging strip from the landmark's foot away from the sun, as long as
    /// its height casts, drawn with Punchy/FarShadow (darkens past the shadow distance only, fades with the haze).
    /// The strip follows the ground (TerrainSource) and is rebuilt only when the sun has moved about a degree.
    /// Cosmetic and local: every client lays its own.
    /// </summary>
    public sealed class FarShadow
    {
        const int Along = 24, Across = 4;
        const float MaxLength = 700f;

        /// <summary>Toggle for every far shadow strip (W-82, #84): -nofarshadow on the command line turns them off.</summary>
        public static bool Enabled = !System.Environment.CommandLine.Contains("-nofarshadow");
        /// <summary>Strips drawn this session, for probes and logs.</summary>
        public static readonly System.Collections.Generic.List<FarShadow> Live = new();

        /// <summary>Last lay: the sun's elevation (degrees), the strip's length (m) and the middle of the strip.</summary>
        public float SunElevation, Length;
        public Vector3 Middle;
        public string Name => mesh.name;

        readonly Mesh mesh;
        readonly Vector3[] verts = new Vector3[(Along + 1) * (Across + 1)];
        Vector3 lastSun;
        Material material;
        bool warned;

        public FarShadow(string name)
        {
            mesh = new Mesh { name = name };
            var colors = new Color[verts.Length];
            var idx = new int[Along * Across * 6];
            int k = 0;
            for (int i = 0; i <= Along; i++)
                for (int j = 0; j <= Across; j++)
                {
                    // Fades towards the tip, and to nothing at both sides, so its edge reads as a penumbra. The core is
                    // kept near full (W-82: cubic along, quartic across) so a far strip isn't faint all over.
                    float u = i / (float)Along, s = math.abs(j / (float)Across * 2f - 1f);
                    colors[i * (Across + 1) + j] = new Color(0, 0, 0, (1f - u * u * u) * (1f - s * s * s * s));
                }
            for (int i = 0; i < Along; i++)
                for (int j = 0; j < Across; j++)
                {
                    int a = i * (Across + 1) + j, b = a + Across + 1;
                    idx[k++] = a; idx[k++] = b; idx[k++] = a + 1;
                    idx[k++] = a + 1; idx[k++] = b; idx[k++] = b + 1;
                }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = idx;
            Live.Add(this);
        }

        /// <summary>Lays (when the sun moved) and draws the strip for a landmark standing at foot, this tall and wide.</summary>
        public void Draw(in TerrainSource source, Vector3 foot, float height, float radius)
        {
            var sun = RenderSettings.sun;
            if (!Enabled || sun == null || !sun.isActiveAndEnabled)
                return;
            var toward = sun.transform.forward; // the direction the light travels
            if (toward.y > -0.05f)
                return; // sun at or under the horizon: nothing to cast
            if (material == null)
            {
                material = Resources.Load<Material>("FarShadow");
                if (material == null)
                {
                    if (!warned) Debug.LogWarning("[landmarks] FarShadow material missing from Resources");
                    warned = true;
                    return;
                }
            }
            if (Vector3.Angle(toward, lastSun) > 1f)
            {
                lastSun = toward;
                Lay(source, foot, height, radius, toward);
            }
            var rp = new RenderParams(material) { worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
        }

        void Lay(in TerrainSource source, Vector3 foot, float height, float radius, Vector3 toward)
        {
            var flat = new float2(toward.x, toward.z);
            var horizontal = math.length(flat);
            var dir = horizontal > 1e-4f ? flat / horizontal : new float2(1f, 0f);
            var side = new float2(-dir.y, dir.x);
            // Length on level ground: height over the tangent of the sun's elevation.
            var length = math.min(MaxLength, height * horizontal / -toward.y) + radius;
            var start = new float2(foot.x, foot.z) - dir * radius;
            SunElevation = math.degrees(math.atan2(-toward.y, horizontal));
            Length = length;
            var mid = start + dir * (0.5f * length);
            Middle = new Vector3(mid.x, source.SampleHeight(mid), mid.y);
            for (int i = 0; i <= Along; i++)
                for (int j = 0; j <= Across; j++)
                {
                    float u = i / (float)Along, s = j / (float)Across * 2f - 1f;
                    // A little narrower at the tip, as the summit slab is all that casts that far.
                    var p = start + dir * (u * length) + side * (s * radius * (1.15f - 0.35f * u));
                    verts[i * (Across + 1) + j] = new Vector3(p.x, source.SampleHeight(p) + 0.4f, p.y);
                }
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            Live.Remove(this);
            Object.Destroy(mesh);
        }
    }
}
