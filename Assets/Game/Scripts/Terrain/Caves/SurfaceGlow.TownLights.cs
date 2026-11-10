using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-10 row 4b (Zach 11-10: "I was hoping to see the glow more directly where the town is"): every lit house's
    /// windows and every street light seen from afar as a small warm point of light, so at night a distant town reads as
    /// a bright cluster of lights on the land rather than a lit patch of sky (the dome, now off by default).
    ///
    /// One camera-facing quad a light (6 vertices from SV_VertexID, TownLights.shader), its points from the world bake
    /// (<see cref="TownPoints"/>). Each is a bright core at least a few pixels across, so it still shows from 3 km, with
    /// a faint wide halo so a far town's lights run together into one glowing cluster (HDR, so the bloom adds to it).
    /// Hidden by hills like the land, thinned by the haze much less than the land (it's a light), and faded in past a
    /// couple of hundred metres, where the town's own lights and emissive windows take over. Lit with the windows (on
    /// after dark, off by day, off underground).
    ///
    /// Static and cosmetic: no light, nothing sent.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>How bright a town light is (HDR; over 1 blooms).</summary>
        public static float TownLightStrength = 8f;
        static readonly Vector4 TownWindowColor = new Vector4(1f, 0.62f, 0.3f, 0f);
        static readonly Vector4 TownLampColor = new Vector4(1f, 0.78f, 0.48f, 0f);

        GraphicsBuffer townLightBuffer;
        int townLightCount;
        Material townLightMaterial;
        MaterialPropertyBlock townLightProps;
        bool townLightsMissing;

        static class TownLightIds
        {
            public static readonly int Points = Shader.PropertyToID("_TownLights");
            public static readonly int Params = Shader.PropertyToID("_TownLightParams");
            public static readonly int Window = Shader.PropertyToID("_TownWindowColor");
            public static readonly int Lamp = Shader.PropertyToID("_TownLampColor");
        }

        /// <summary>Draws the town lights for this frame: <paramref name="on"/> the windows' night light (0 by day).
        /// Uploads the bake's points the first frame after each bake (<paramref name="dirty"/>).</summary>
        void DrawTownLights(float on, Vector3 camPos, bool dirty)
        {
            if (townLightsMissing)
                return;
            if (townLightMaterial == null)
            {
                townLightMaterial = Resources.Load<Material>("TownLights");
                townLightProps = new MaterialPropertyBlock();
                if (townLightMaterial == null)
                {
                    townLightsMissing = true;   // looked for once, not every frame
                    return;
                }
            }
            if (dirty)
            {
                townLightCount = TownPoints.Count;
                if (townLightCount > 0)
                {
                    if (townLightBuffer == null || townLightBuffer.count < townLightCount)
                    {
                        townLightBuffer?.Release();
                        townLightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(townLightCount, 64), 16);
                    }
                    townLightBuffer.SetData(TownPoints);
                }
            }
            if (townLightCount == 0)
                return;
            townLightProps.SetBuffer(TownLightIds.Points, townLightBuffer);
            townLightProps.SetVector(TownLightIds.Params, new Vector4(on * TownLightStrength, 0f, 0f, 0f));
            townLightProps.SetVector(TownLightIds.Window, TownWindowColor);
            townLightProps.SetVector(TownLightIds.Lamp, TownLampColor);
            var rp = new RenderParams(townLightMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (WorldExtent * 2f)),
                matProps = townLightProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6 * townLightCount, 1);
        }

        void ReleaseTownLights()
        {
            townLightBuffer?.Release();
            townLightBuffer = null;
            townLightCount = 0;
        }
    }
}
