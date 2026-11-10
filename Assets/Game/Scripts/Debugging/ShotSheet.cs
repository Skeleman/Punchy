using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// T-1 row 3: one small labelled contact sheet per shot run, the only picture an agent opens. Each shot is scaled to a
    /// 320x180 tile, four to a row (1280 wide), with a label bar under it: green for a good shot, red for a BAD one (its
    /// reason in the label). Written as a quality-75 JPG, well under 300 KB for a dozen shots. The full-resolution PNGs
    /// stay where they are, for people. Labels use a built-in 3x5 pixel font (letters, digits, - . : and space).
    /// </summary>
    public static class ShotSheet
    {
        public const int TileWidth = 320, TileHeight = 180, Columns = 4, BarHeight = 14, Scale = 2;

        /// <summary>Writes the sheet of these shots to path (a .jpg); labels[i] names shots[i] (the file name if null). Returns its size in bytes.</summary>
        public static long Write(string path, IList<ShotTool.Taken> shots, IList<string> labels = null)
        {
            var rows = Mathf.Max(1, (shots.Count + Columns - 1) / Columns);
            var width = TileWidth * Columns;
            var height = rows * (TileHeight + BarHeight);
            var canvas = new Color32[width * height];
            for (int i = 0; i < canvas.Length; i++)
                canvas[i] = new Color32(24, 24, 24, 255);
            var tile = new RenderTexture(TileWidth, TileHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false);
            for (int s = 0; s < shots.Count; s++)
            {
                int left = s % Columns * TileWidth, top = s / Columns * (TileHeight + BarHeight);
                var shot = shots[s];
                if (System.IO.File.Exists(shot.Path))
                {
                    // Scaled on the GPU (a bilinear blit), read back as one tile.
                    var full = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    full.LoadImage(System.IO.File.ReadAllBytes(shot.Path));
                    full.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(full, tile);
                    var active = RenderTexture.active;
                    RenderTexture.active = tile;
                    read.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), 0, 0);
                    read.Apply();
                    RenderTexture.active = active;
                    Object.Destroy(full);
                    var px = read.GetPixels32();
                    for (int y = 0; y < TileHeight; y++)
                    for (int x = 0; x < TileWidth; x++)
                        Set(canvas, width, height, left + x, top + y, px[(TileHeight - 1 - y) * TileWidth + x]);
                }
                var bar = shot.Bad != null ? new Color32(150, 30, 30, 255) : new Color32(30, 110, 45, 255);
                for (int y = 0; y < BarHeight; y++)
                for (int x = 0; x < TileWidth; x++)
                    Set(canvas, width, height, left + x, top + TileHeight + y, bar);
                var label = labels != null && s < labels.Count && labels[s] != null ? labels[s] : System.IO.Path.GetFileNameWithoutExtension(shot.Path);
                if (shot.Bad != null)
                    label = $"{label} BAD {shot.Bad}";
                Text(canvas, width, height, left + 3, top + TileHeight + 2, $"{s + 1} {label}", TileWidth - 6);
            }
            Object.Destroy(read);
            tile.Release();
            Object.Destroy(tile);
            var sheet = new Texture2D(width, height, TextureFormat.RGB24, false);
            sheet.SetPixels32(canvas);
            sheet.Apply();
            var bytes = sheet.EncodeToJPG(75);
            Object.Destroy(sheet);
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(path, bytes);
            Debug.Log($"[shot] contact sheet {System.IO.Path.GetFileName(path)}: {shots.Count} shots, {width}x{height}, {bytes.Length / 1024} KB");
            return bytes.Length;
        }

        // (x, y from the top left; the texture's rows run bottom up.)
        static void Set(Color32[] canvas, int width, int height, int x, int y, Color32 c)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
                canvas[(height - 1 - y) * width + x] = c;
        }

        static void Text(Color32[] canvas, int width, int height, int x, int y, string text, int room)
        {
            var white = new Color32(240, 240, 240, 255);
            var advance = 4 * Scale;
            foreach (var ch in text.ToUpperInvariant())
            {
                if (room < advance)
                    break;
                if (Glyphs.TryGetValue(ch, out var rows))
                    for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                        if (rows[r * 3 + c] == '#')
                            for (int dy = 0; dy < Scale; dy++)
                            for (int dx = 0; dx < Scale; dx++)
                                Set(canvas, width, height, x + c * Scale + dx, y + r * Scale + dy, white);
                x += advance;
                room -= advance;
            }
        }

        // Each glyph: 5 rows of 3, top row first ('#' lit). Characters not here are drawn as a space.
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A'] = ".#.#.#####.##.#", ['B'] = "##.#.###.#.###.", ['C'] = ".###..#..#...##", ['D'] = "##.#.##.##.###.",
            ['E'] = "####..##.#..###", ['F'] = "####..##.#..#..", ['G'] = ".###..#.##.#.##", ['H'] = "#.##.#####.##.#",
            ['I'] = "###.#..#..#.###", ['J'] = "..#..#..##.#.#.", ['K'] = "#.##.###.#.##.#", ['L'] = "#..#..#..#..###",
            ['M'] = "#.########.##.#", ['N'] = "##.#.##.##.##.#", ['O'] = ".#.#.##.##.#.#.", ['P'] = "##.#.###.#..#..",
            ['Q'] = ".#.#.##.###..##", ['R'] = "##.#.###.#.##.#", ['S'] = ".###...#...###.", ['T'] = "###.#..#..#..#.",
            ['U'] = "#.##.##.##.####", ['V'] = "#.##.##.##.#.#.", ['W'] = "#.##.########.#", ['X'] = "#.##.#.#.#.##.#",
            ['Y'] = "#.##.#.#..#..#.", ['Z'] = "###..#.#.#..###", ['0'] = "####.##.##.####", ['1'] = ".#.##..#..#.###",
            ['2'] = "##...#.#.#..###", ['3'] = "##...#.#...###.", ['4'] = "#.##.####..#..#", ['5'] = "####..##...###.",
            ['6'] = ".###..####.####", ['7'] = "###..#.#..#..#.", ['8'] = "####.#####.####", ['9'] = "####.####..###.",
            ['-'] = "......###......", ['.'] = ".............#.", [':'] = "....#.....#....",
        };
    }
}
