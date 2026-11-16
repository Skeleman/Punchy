using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-10 row 4 (Zach 11-09: a distant town should glow at night): a soft light dome over each lit town, the warm
    /// light of its windows and street lights caught in the air above it, so from a kilometre or more a town reads as a
    /// glow on the horizon as well as a warm patch on the ground (the town map, Glow.hlsl).
    ///
    /// One box a town round a flattened half-ellipsoid (TownDome.shader): each pixel adds how much of the ellipsoid its
    /// view ray passes through in front of the scene, so hills hide it and it shows against the sky. Its size and
    /// brightness come from the world bake's town list (<see cref="Towns"/>); it's lit with the windows (on after dark,
    /// off by day), brighter in fog and rain, and fades out as the camera comes near or into the town, where its real
    /// lights and glow slots take over.
    ///
    /// R-10 row 4b (Zach 11-10: the dome lit too much of the sky): off by default ("-towndome 1" turns it on), and when on
    /// a low, small halo just over the town, barely thicker in fog or rain. The far lights (SurfaceGlow.FarLights.cs)
    /// show a distant town instead.
    ///
    /// Static and cosmetic: no light, nothing sent.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>The dome's brightness (added light at its thickest, for a town of <see cref="DomeLightHalf"/>).</summary>
        public static float DomeStrength = 0.3f;
        /// <summary>A town's summed light at which its dome is half as bright as the brightest (one lit house is about 1).</summary>
        const float DomeLightHalf = 40f;
        /// <summary>"-townglow 0" turns the town map, town lights and domes off, for comparing shots with and without
        /// them.</summary>
        public static bool TownGlowEnabled = true;
        /// <summary>"-towndome 1" turns the domes on (row 4b: off by default).</summary>
        public static bool DomeEnabled;
        static readonly Vector3 DomeColor = new Vector3(1f, 0.58f, 0.26f);

        struct Dome
        {
            public float4 At;      // xyz its middle on the ground, w its radius (m)
            public float4 Light;   // rgb its light (brightness folded in), w its height (m)
        }

        readonly List<Dome> domes = new List<Dome>();
        GraphicsBuffer domeBuffer;
        bool domesDirty = true, domeShapesDirty = true;
        Material domeMaterial;
        MaterialPropertyBlock domeProps;

        static class DomeIds
        {
            public static readonly int Domes = Shader.PropertyToID("_Domes");
            public static readonly int Params = Shader.PropertyToID("_DomeParams");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadTownGlowFlag()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-townglow");
            if (at >= 0 && at + 1 < args.Length && args[at + 1] == "0")
                TownGlowEnabled = false;
            at = System.Array.IndexOf(args, "-towndome");
            if (at >= 0 && at + 1 < args.Length)
                DomeEnabled = args[at + 1] == "1";
        }

        /// <summary>Draws (with "-towndome 1") the domes for this frame: <paramref name="on"/> the
        /// windows' night light (0 by day), <paramref name="fog"/> the weather's fog at the camera (1 clear).</summary>
        public void DrawDomes(float on, float fog, Vector3 camPos)
        {
            if (!TownGlowEnabled || on <= 0f || !WorldReady)
                return;
            var dirty = domesDirty;
            domesDirty = false;
            if (dirty)
                domeShapesDirty = true;
            if (!DomeEnabled || Towns.Count == 0)
                return;
            if (domeMaterial == null)
            {
                domeMaterial = Resources.Load<Material>("TownDome");
                domeProps = new MaterialPropertyBlock();
                if (domeMaterial == null)
                {
                    DomeEnabled = false;
                    return;
                }
            }
            if (domeShapesDirty)
            {
                domeShapesDirty = false;
                domes.Clear();
                foreach (var t in Towns)
                {
                    // Row 4b: just over the town, and low (was R x 2.5 + 120, up to 300 m high).
                    var radius = t.Radius * 1.3f + 40f;
                    var light = DomeColor * (t.Light / (t.Light + DomeLightHalf));
                    domes.Add(new Dome
                    {
                        At = new float4(t.Centre, radius),
                        Light = new float4(light.x, light.y, light.z, math.min(radius * 0.3f, 50f))
                    });
                }
                if (domes.Count == 0)
                    return;
                if (domeBuffer == null || domeBuffer.count < domes.Count)
                {
                    domeBuffer?.Release();
                    domeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(domes.Count, 16), 32);
                }
                domeBuffer.SetData(domes);
            }
            if (domes.Count == 0)
                return;
            // Fog and rain catch a little more of it (clear 1, overcast 1.5, rain 2, marsh fog 4).
            var thick = math.lerp(0.9f, 1.15f, math.saturate((fog - 1f) / 1.5f));
            domeProps.SetBuffer(DomeIds.Domes, domeBuffer);
            domeProps.SetVector(DomeIds.Params, new Vector4(on * DomeStrength * thick, 0f, 0f, 0f));
            var rp = new RenderParams(domeMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (WorldExtent * 2f)),
                matProps = domeProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 36 * domes.Count, 1);
        }

        void ReleaseDomes()
        {
            domeBuffer?.Release();
            domeBuffer = null;
            domes.Clear();
            domesDirty = true;
            ReleaseFarLights();
        }
    }
}
