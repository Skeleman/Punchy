using Punchy.Settings;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-10 rows 4b-4c: every static light seen from afar at night as a small point of light in its own colour: a lit
    /// house's windows, a street light, a burning tree or beacon fire, a glowing plant, a crystal. So at night a distant
    /// town or Ember Forest reads as a bright cluster of lights on the land, and a fungal forest or a crystal field as a
    /// fainter one (Zach 11-10: lights aren't equal, fire is the brightest).
    ///
    /// One camera-facing quad a light (6 vertices from SV_VertexID, FarLights.shader), its points from the world bake
    /// (<see cref="FarPoints"/>). How bright, big, hazed and faded in each kind is comes from one table,
    /// <see cref="FarLightPolicy"/>; the fade-in is by screen size (rule 20), where the near lights and glow maps hand
    /// over. Windows and street lights are lit with the windows and off with "-townglow 0"; fires with tonight's forest
    /// fire, in its colour; plants and crystals after dark. All off underground.
    ///
    /// Static and cosmetic: no light, nothing sent.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>One far light: xyz where it's seen and w its light; rgb its colour and w its <see cref="FarLightKind"/>.</summary>
        public struct FarPoint
        {
            public float4 At;
            public float4 Color;
        }

        static readonly float4 FarWindowColor = new float4(1f, 0.62f, 0.3f, 0f);
        static readonly float4 FarLampColor = new float4(1f, 0.78f, 0.48f, 0f);

        GraphicsBuffer farLightBuffer;
        int farLightCount;
        Material farLightMaterial;
        MaterialPropertyBlock farLightProps;
        bool farLightsMissing, farLightsDirty = true;
        readonly Vector4[] farBright = new Vector4[FarLightPolicy.KindCount];
        readonly Vector4[] farShape = new Vector4[FarLightPolicy.KindCount];
        readonly Vector4[] farFade = new Vector4[FarLightPolicy.KindCount];

        // Shader ids live in their own class: the world bake job reads SurfaceGlow's statics, and Burst can't run a
        // static constructor that calls Shader.PropertyToID (BC1091).
        static class FarLightIds
        {
            public static readonly int ForestFire = Shader.PropertyToID("_PunchyGlowWorldFire");
            public static readonly int Points = Shader.PropertyToID("_FarLights");
            public static readonly int Bright = Shader.PropertyToID("_FarLightBright");
            public static readonly int Shape = Shader.PropertyToID("_FarLightShape");
            public static readonly int Fade = Shader.PropertyToID("_FarLightFade");
            public static readonly int Params = Shader.PropertyToID("_FarLightParams");
            public static readonly int Fire = Shader.PropertyToID("_FarFireColor");
        }

        /// <summary>Draws the far lights for this frame: <paramref name="on"/> the windows' night light (0 by day, and
        /// underground). Uploads the bake's points the first frame after each bake.</summary>
        public void DrawFarLights(float on, Vector3 camPos)
        {
            if (farLightsMissing || on <= 0f || FarLightPolicy.Brightness <= 0f || !WorldReady)
                return;
            if (farLightMaterial == null)
            {
                farLightMaterial = Resources.Load<Material>("FarLights");
                farLightProps = new MaterialPropertyBlock();
                if (farLightMaterial == null)
                {
                    farLightsMissing = true;   // looked for once, not every frame
                    return;
                }
            }
            if (farLightsDirty)
            {
                farLightsDirty = false;
                farLightCount = FarPoints.Count;
                if (farLightCount > 0)
                {
                    if (farLightBuffer == null || farLightBuffer.count < farLightCount)
                    {
                        farLightBuffer?.Release();
                        farLightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(farLightCount, 64), 32);
                    }
                    farLightBuffer.SetData(FarPoints);
                }
            }
            if (farLightCount == 0)
                return;
            // Tonight's forest fire: its colour (brightest channel 1), and how lit it is (0 by day or with the fires off).
            var fire = (float4)Shader.GetGlobalVector(FarLightIds.ForestFire);
            var fireMax = math.cmax(fire.xyz);
            var fireColor = fireMax > 1e-4f ? fire.xyz / fireMax : new float3(1f, 0.56f, 0.24f);
            var fireOn = math.saturate(fireMax * 2f);
            for (int k = 0; k < FarLightPolicy.KindCount; k++)
            {
                var kind = (FarLightKind)k;
                var kindOn = kind == FarLightKind.Window || kind == FarLightKind.StreetLight ? (TownGlowEnabled ? 1f : 0f)
                           : kind == FarLightKind.Fire ? fireOn : 1f;
                farBright[k] = new Vector4(on * kindOn * FarLightPolicy.FarBrightness(kind, 1f), 0f, 0f, 0f);
            }
            var view = DetailPolicy.Current.Data;
            FarLightPolicy.Pack(farShape, farFade, view.Detail);
            farLightProps.SetBuffer(FarLightIds.Points, farLightBuffer);
            farLightProps.SetVectorArray(FarLightIds.Bright, farBright);
            farLightProps.SetVectorArray(FarLightIds.Shape, farShape);
            farLightProps.SetVectorArray(FarLightIds.Fade, farFade);
            farLightProps.SetVector(FarLightIds.Params, new Vector4(view.PixelsPerMetre > 0f ? view.PixelsPerMetre : DetailPolicy.PixelsPerMetreAt(60f), 0f, 0f, 0f));
            farLightProps.SetVector(FarLightIds.Fire, new Vector4(fireColor.x, fireColor.y, fireColor.z, 0f));
            var rp = new RenderParams(farLightMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (WorldExtent * 2f)),
                matProps = farLightProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6 * farLightCount, 1);
        }

        static FarPoint NewFarPoint(float3 at, float light, float4 color, FarLightKind kind)
            => new FarPoint { At = new float4(at, light), Color = new float4(color.xyz, (float)kind) };

        /// <summary>A glow's colour with its brightest channel 1 (how bright it reads is the table's, not the colour's).</summary>
        static float4 FarColor(float3 glow)
        {
            var top = math.cmax(glow);
            return new float4(top > 1e-4f ? glow / top : new float3(1f), 0f);
        }

        void ReleaseFarLights()
        {
            farLightBuffer?.Release();
            farLightBuffer = null;
            farLightCount = 0;
            farLightsDirty = true;
        }
    }
}
