using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>Converts between height arrays and 16-bit grayscale PNG bytes.</summary>
    public static class HeightmapIO
    {
        /// <summary>Encodes heights (row-major, row 0 = southmost) as a 16-bit grayscale PNG normalised to [min, max].</summary>
        public static byte[] EncodePng(float[] heights, int resolution, float min, float max)
        {
            var range = math.max(max - min, 1e-5f);
            var pixels = new NativeArray<ushort>(heights.Length, Allocator.Temp);
            for (int i = 0; i < heights.Length; i++)
                pixels[i] = (ushort)math.round(math.saturate((heights[i] - min) / range) * 65535f);

            var tex = new Texture2D(resolution, resolution, TextureFormat.R16, false, true);
            tex.SetPixelData(pixels, 0);
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            pixels.Dispose();
            return png;
        }

        /// <summary>
        /// Decodes PNG bytes into heights in metres. Works for 16-bit or 8-bit, grayscale or colour
        /// (red channel), so an image re-saved by an external editor still loads.
        /// </summary>
        public static float[] DecodePng(byte[] png, float min, float max, out int resolution)
        {
            var tex = new Texture2D(2, 2, TextureFormat.R16, false, true);
            if (!tex.LoadImage(png, false))
            {
                Object.DestroyImmediate(tex);
                resolution = 0;
                return null;
            }

            if (tex.width != tex.height)
                Debug.LogWarning($"Heightmap is {tex.width}x{tex.height}; only square images are supported, cropping.");

            resolution = math.min(tex.width, tex.height);
            var colors = tex.GetPixels();
            var heights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
                heights[y * resolution + x] = math.lerp(min, max, colors[y * tex.width + x].r);

            Object.DestroyImmediate(tex);
            return heights;
        }
    }
}
