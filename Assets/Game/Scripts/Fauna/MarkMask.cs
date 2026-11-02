using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A coarse map round the camera of where marks drawn by a shader loop are (worms' bumps and craters for the
    /// terrain, swimmers' ripples for the water): Size × Size cells over Range either way, each 1 where some mark reaches
    /// into it. The shader reads its one cell and runs the loop over the marks only there, so the rest of the screen,
    /// most of it, pays for one texel read instead. Set as a texture global, with (x, z of the map's corner, cells per
    /// metre, Size) beside it.
    /// </summary>
    public class MarkMask
    {
        public const int Size = 64;
        readonly Texture2D texture;
        readonly byte[] cells = new byte[Size * Size];
        readonly int textureId, placeId;
        float2 corner;
        float perMetre;

        public MarkMask(string textureName, string placeName)
        {
            texture = new Texture2D(Size, Size, TextureFormat.R8, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = textureName
            };
            textureId = Shader.PropertyToID(textureName);
            placeId = Shader.PropertyToID(placeName);
        }

        /// <summary>Cleared and centred on the camera (snapped to whole cells, so the cells don't swim as it moves).</summary>
        public void Begin(float2 camera, float range)
        {
            System.Array.Clear(cells, 0, cells.Length);
            var cell = range * 2f / Size;
            perMetre = 1f / cell;
            corner = math.floor(camera / cell) * cell - range;
        }

        /// <summary>Every cell a mark at this point reaches into, out to reach metres.</summary>
        public void Mark(float2 at, float reach)
        {
            var lo = (int2)math.floor((at - reach - corner) * perMetre);
            var hi = (int2)math.floor((at + reach - corner) * perMetre);
            lo = math.max(lo, 0);
            hi = math.min(hi, Size - 1);
            for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                    cells[z * Size + x] = 255;
        }

        public void Upload()
        {
            texture.SetPixelData(cells, 0);
            texture.Apply(false);
            Shader.SetGlobalTexture(textureId, texture);
            Shader.SetGlobalVector(placeId, new Vector4(corner.x, corner.y, perMetre, Size));
        }
    }
}
