using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Punchy.Debugging
{
    /// <summary>
    /// R-31 row 1: how much of the ground the grass blades hide, by distance. Renders the main camera once into a
    /// render texture with post-processing off and the blades flat magenta (Grass.shader, _PunchyGrassProbe), then for
    /// ground distances of 3, 8, 15 and 25 m counts the magenta share of the screen rows that look at that distance on
    /// flat ground (rows from the camera's height over the ground and its pitch; the middle 60% of each row, so trees at
    /// the sides don't count). Called from a script eval on flat meadow; prints one line and returns it.
    /// </summary>
    public static class GrassCoverageProbe
    {
        static readonly int ProbeId = Shader.PropertyToID("_PunchyGrassProbe");
        public static readonly float[] Distances = { 3f, 8f, 15f, 25f };

        /// <summary>Coverage at each of <see cref="Distances"/>, from the main camera as it stands. groundY is the
        /// ground height under the camera (NaN: a raycast down finds it).</summary>
        public static string Measure(float groundY = float.NaN, int width = 1920, int height = 1080)
        {
            var cam = Camera.main;
            if (cam == null)
                return "no camera";
            var camPos = cam.transform.position;
            if (float.IsNaN(groundY))
                groundY = Physics.Raycast(camPos + Vector3.up * 2f, Vector3.down, out var hit, 500f) ? hit.point.y : camPos.y - 1.7f;
            var eye = camPos.y - groundY;

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var extra = cam.GetUniversalAdditionalCameraData();
            var post = extra.renderPostProcessing;
            var oldTarget = cam.targetTexture;
            extra.renderPostProcessing = false;
            cam.targetTexture = rt;
            Shader.SetGlobalFloat(ProbeId, 1f);
            cam.Render();
            Shader.SetGlobalFloat(ProbeId, 0f);
            cam.targetTexture = oldTarget;
            extra.renderPostProcessing = post;

            var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels32();
            Object.Destroy(read);

            // Each row's ray, at the middle column: where it meets the flat ground plane eye metres below.
            var sb = new StringBuilder($"[grass probe] eye {eye:F2} m, fov {cam.fieldOfView:F0}, at {camPos.x:F0},{camPos.z:F0}:");
            int x0 = (int)(width * 0.2f), x1 = (int)(width * 0.8f);
            foreach (var d in Distances)
            {
                long covered = 0, total = 0;
                for (int y = 0; y < height; y++)
                {
                    var ray = cam.ViewportPointToRay(new Vector3(0.5f, (y + 0.5f) / height, 0f));
                    if (ray.direction.y >= -1e-4f)
                        continue;
                    var along = eye / -ray.direction.y;
                    var flat = math.length(new float2(ray.direction.x, ray.direction.z)) * along;
                    if (flat < d * 0.9f || flat > d * 1.1f)
                        continue;
                    for (int x = x0; x < x1; x++)
                    {
                        var c = px[y * width + x];
                        total++;
                        if (c.r > 230 && c.g < 25 && c.b > 230)
                            covered++;
                    }
                }
                sb.Append(total > 0 ? $" {d:F0} m {100.0 * covered / total:F1}% ({total / (x1 - x0)} rows)" : $" {d:F0} m n/a");
            }
            var line = sb.ToString();
            Debug.Log(line);
            return line;
        }
    }
}
