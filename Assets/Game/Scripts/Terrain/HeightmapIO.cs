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
        /// Decodes PNG bytes into normalised 16-bit values (0 = min height, 65535 = max). Works for 16-bit or
        /// 8-bit, grayscale or colour (red channel), so an image re-saved by an external editor still loads.
        /// </summary>
        public static ushort[] DecodePng(byte[] png, out int resolution)
        {
            // Our own reader keeps 16-bit precision; Unity's LoadImage (below) truncates to 8 bits, so it's only the
            // fallback for unusual PNGs (palette-based or interlaced).
            if (PngReader.TryReadFirstChannel(png, out var full, out var width16, out var height16))
            {
                if (width16 != height16)
                    Debug.LogWarning($"Heightmap is {width16}x{height16}; only square images are supported, cropping.");
                resolution = math.min(width16, height16);
                if (width16 == resolution)
                    return full;
                var cropped = new ushort[resolution * resolution];
                for (int y = 0; y < resolution; y++)
                    System.Array.Copy(full, y * width16, cropped, y * resolution, resolution);
                return cropped;
            }

            Debug.LogWarning("Heightmap PNG uses a format the 16-bit reader doesn't support (palette or interlaced?); " +
                             "loading it at 8-bit precision.");
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
            var values = new ushort[resolution * resolution];
            var width = tex.width;

            // (bytes per channel, channels per pixel, index of the red channel)
            var (wide, channels, red) = tex.format switch
            {
                TextureFormat.R16 => (true, 1, 0),
                TextureFormat.RGB48 => (true, 3, 0),
                TextureFormat.RGBA64 => (true, 4, 0),
                TextureFormat.R8 or TextureFormat.Alpha8 => (false, 1, 0),
                TextureFormat.RGB24 => (false, 3, 0),
                TextureFormat.RGBA32 => (false, 4, 0),
                TextureFormat.ARGB32 => (false, 4, 1),
                TextureFormat.BGRA32 => (false, 4, 2),
                _ => (false, 0, 0)
            };

            if (channels == 0)
            {
                // Unusual format: fall back to the (slow, memory-hungry) generic path.
                var colors = tex.GetPixels32();
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    values[y * resolution + x] = (ushort)(colors[y * width + x].r * 257);
            }
            else if (wide)
            {
                var data = tex.GetPixelData<ushort>(0);
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    values[y * resolution + x] = data[(y * width + x) * channels + red];
            }
            else
            {
                var data = tex.GetPixelData<byte>(0);
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    values[y * resolution + x] = (ushort)(data[(y * width + x) * channels + red] * 257);
            }

            Object.DestroyImmediate(tex);
            return values;
        }
    }
}
