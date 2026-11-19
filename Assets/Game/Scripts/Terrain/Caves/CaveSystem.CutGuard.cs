using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    public partial class CaveSystem
    {
        /// <summary>
        /// The surface skip (GatherOpenings) runs in Update, for the camera pose Update sees. A cut or teleport moves
        /// the camera after that, so the first frame drew a cave mouth with no passage behind it. When the camera
        /// renders this far (or this turned) from the pose the skip was gathered for, the chunks it hid are drawn too,
        /// for that frame only.
        /// </summary>
        const float CutDistance = 3f;
        const float CutAngle = 25f;

        readonly List<Built> skippedChunks = new List<Built>();
        float3 gatheredPos;
        quaternion gatheredRot;
        int gatheredFrame = -1;

        void NoteGathered(Camera cam)
        {
            gatheredPos = cam.transform.position;
            gatheredRot = cam.transform.rotation;
            gatheredFrame = Time.frameCount;
        }

        void DrawSkippedOnCut(ScriptableRenderContext context, Camera cam)
        {
            if (skippedChunks.Count == 0 || gatheredFrame != Time.frameCount || cam != Camera.main || Material == null)
                return;
            if (math.distance((float3)cam.transform.position, gatheredPos) < CutDistance
                && Quaternion.Angle(cam.transform.rotation, gatheredRot) < CutAngle)
                return;
            for (int i = 0; i < skippedChunks.Count; i++)
            {
                var b = skippedChunks[i];
                if (b.Mesh == null)
                    continue;
                var rp = new RenderParams(Material)
                {
                    worldBounds = b.Mesh.bounds,
                    camera = cam,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, b.Mesh, 0, Matrix4x4.identity);
            }
        }
    }
}
