using System;
using System.IO;
using System.IO.Compression;

namespace Punchy.Terrain
{
    /// <summary>
    /// Minimal PNG decoder that keeps full 16-bit precision (Unity's ImageConversion.LoadImage silently converts
    /// 16-bit PNGs to 8 bits per channel). Supports non-interlaced grayscale, grayscale+alpha, RGB and RGBA at 8 or
    /// 16 bits; returns the first channel (gray or red) of every pixel, scaled to 0..65535, bottom row first.
    /// </summary>
    public static class PngReader
    {
        public static bool TryReadFirstChannel(byte[] png, out ushort[] values, out int width, out int height)
        {
            values = null;
            width = height = 0;
            if (png.Length < 33 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G')
                return false;

            int bitDepth = 0, colorType = 0, interlace = 0;
            using var compressed = new MemoryStream();
            var pos = 8;
            while (pos + 8 <= png.Length)
            {
                var length = ReadInt(png, pos);
                var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                var data = pos + 8;
                if (type == "IHDR")
                {
                    width = ReadInt(png, data);
                    height = ReadInt(png, data + 4);
                    bitDepth = png[data + 8];
                    colorType = png[data + 9];
                    interlace = png[data + 12];
                }
                else if (type == "IDAT")
                    compressed.Write(png, data, length);
                else if (type == "IEND")
                    break;
                pos = data + length + 4; // skip CRC
            }

            var channels = colorType switch { 0 => 1, 4 => 2, 2 => 3, 6 => 4, _ => 0 };
            if (channels == 0 || interlace != 0 || (bitDepth != 8 && bitDepth != 16) || width <= 0 || height <= 0)
                return false;

            var bytesPerSample = bitDepth / 8;
            var bpp = channels * bytesPerSample;
            var stride = width * bpp;
            var raw = new byte[height * (stride + 1)];

            // zlib stream = 2-byte header + raw deflate data (+ checksum, ignored).
            compressed.Position = 2;
            using (var inflate = new DeflateStream(compressed, CompressionMode.Decompress))
            {
                var read = 0;
                while (read < raw.Length)
                {
                    var n = inflate.Read(raw, read, raw.Length - read);
                    if (n <= 0)
                        return false;
                    read += n;
                }
            }

            values = new ushort[width * height];
            var previous = new byte[stride];
            var current = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                var rowStart = y * (stride + 1);
                var filter = raw[rowStart];
                Buffer.BlockCopy(raw, rowStart + 1, current, 0, stride);
                Unfilter(filter, current, previous, bpp);

                // PNG rows run top to bottom; heightmaps are stored bottom row first (south = row 0).
                var outRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    var i = x * bpp;
                    values[outRow + x] = bytesPerSample == 2
                        ? (ushort)((current[i] << 8) | current[i + 1])
                        : (ushort)(current[i] * 257);
                }
                (previous, current) = (current, previous);
            }
            return true;
        }

        static void Unfilter(byte filter, byte[] row, byte[] prior, int bpp)
        {
            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= bpp ? row[i - bpp] : 0;
                int up = prior[i];
                int upLeft = i >= bpp ? prior[i - bpp] : 0;
                row[i] = filter switch
                {
                    1 => (byte)(row[i] + left),
                    2 => (byte)(row[i] + up),
                    3 => (byte)(row[i] + ((left + up) >> 1)),
                    4 => (byte)(row[i] + Paeth(left, up, upLeft)),
                    _ => row[i]
                };
            }
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static int ReadInt(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
    }
}
