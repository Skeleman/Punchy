using System.Collections.Generic;
using Punchy.Flora;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-127: window spill. After dark each lit window throws a soft warm pool on the ground below it and the walls
    /// facing it (WindowSpill.shader). The world glow map is far too coarse for that (18 m a texel), so it's drawn
    /// near the camera only: the near scan also lists the houses with lit windows within <see cref="SpillRadius"/>,
    /// and each of their panes (FloraModel.Windows, from the generator) becomes a box drawn over the scene.
    ///
    /// Static and cosmetic: no real light, no glow slot, nothing sent. It follows the indoor Fixture's lit/unlit
    /// choice (on with the windows after dark, off by day) and fades out before the scan's edge.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>How far round the camera houses' panes are listed (m). The spill fades out by how big a pane's pool
        /// looks (DetailPolicy.SpillFromPixels to SpillToPixels: 60 m to 85 m at the reference view), never past
        /// SpillEdge, inside the scan.</summary>
        const float SpillRadius = 90f, SpillEdge = 85f; // streaming: the near scan's reach
        const int MaxPanes = 2048;
        /// <summary>The pool's brightness (added light at its brightest, before facing and fall-off).</summary>
        public static float SpillStrength = 0.6f;
        /// <summary>"-windowspill 0" turns the spill off, for comparing shots with and without it.</summary>
        public static bool SpillEnabled = true;

        struct Pane
        {
            public float4 At;      // xyz the middle, w the angle it faces out along
            public float4 Light;   // rgb colour, w the house's scale
        }

        NativeList<float4> spillAt;
        NativeList<float2> spillModel;
        readonly List<Pane> panes = new List<Pane>();
        GraphicsBuffer paneBuffer;
        bool panesDirty;
        Material spillMaterial;
        MaterialPropertyBlock spillProps;
        /// <summary>Lit-window houses and panes the last scan found, for the report.</summary>
        public int SpillHouses, SpillPanes;

        /// <summary>(Its own class: SurfaceGlow's jobs read its static fields, and Burst can't run a static constructor that
        /// calls into the engine.)</summary>
        static class SpillIds
        {
            public static readonly int Panes = Shader.PropertyToID("_Panes");
            public static readonly int Params = Shader.PropertyToID("_SpillParams");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadSpillFlag()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-windowspill");
            if (at >= 0 && at + 1 < args.Length && args[at + 1] == "0")
                SpillEnabled = false;
        }

        /// <summary>Turns the last scan's lit-window houses into their panes in the world (main thread, on pick-up).</summary>
        void GatherSpill(FloraSystem flora)
        {
            panes.Clear();
            SpillHouses = spillAt.Length;
            for (int i = 0; i < spillAt.Length && flora != null; i++)
            {
                var model = (int)spillModel[i].x;
                var windows = flora.ModelWindows(model);
                if (windows == null || windows.Count == 0 || model >= modelGlow.Length)
                    continue;
                var at = spillAt[i];
                var scale = spillModel[i].y;
                float c = math.cos(at.w), sn = math.sin(at.w);
                // Its colour from the house's own night light (lamp-warm for most), at full brightness.
                var glow = modelGlow[model].xyz;
                var colour = glow / math.max(math.cmax(glow), 1e-3f);
                foreach (var w in windows)
                {
                    if (panes.Count >= MaxPanes)
                        break;
                    var xz = at.xz + new float2(w.x * c - w.z * sn, w.x * sn + w.z * c) * scale;
                    panes.Add(new Pane
                    {
                        At = new float4(xz.x, at.y + w.y * scale, xz.y, w.w + at.w),
                        Light = new float4(colour, scale)
                    });
                }
            }
            SpillPanes = panes.Count;
            panesDirty = true;
        }

        /// <summary>Draws the spill for this frame; <paramref name="on"/> is the windows' night light (0 by day).</summary>
        public void DrawSpill(float on, Vector3 camPos)
        {
            if (!SpillEnabled || on <= 0f || panes.Count == 0)
                return;
            if (spillMaterial == null)
            {
                spillMaterial = Resources.Load<Material>("WindowSpill");
                spillProps = new MaterialPropertyBlock();
                if (spillMaterial == null)
                {
                    SpillEnabled = false;
                    return;
                }
            }
            if (panesDirty)
            {
                panesDirty = false;
                if (paneBuffer == null || paneBuffer.count < panes.Count)
                {
                    paneBuffer?.Release();
                    paneBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(panes.Count, 64), 32);
                }
                paneBuffer.SetData(panes);
            }
            spillProps.SetBuffer(SpillIds.Panes, paneBuffer);
            var fade = Punchy.Settings.DetailPolicy.FadeReach(Punchy.Settings.DetailPolicy.SpillSize, Punchy.Settings.DetailPolicy.SpillFromPixels,
                                                              Punchy.Settings.DetailPolicy.SpillToPixels, SpillEdge);
            spillProps.SetVector(SpillIds.Params, new Vector4(on, SpillStrength, fade.x, fade.y));
            var rp = new RenderParams(spillMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (SpillRadius * 2f + 40f)),
                matProps = spillProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 36 * panes.Count, 1);
        }

        void ReleaseSpill()
        {
            if (spillAt.IsCreated)
                spillAt.Dispose();
            if (spillModel.IsCreated)
                spillModel.Dispose();
            paneBuffer?.Release();
            paneBuffer = null;
            panes.Clear();
            SpillHouses = SpillPanes = 0;
        }
    }
}
