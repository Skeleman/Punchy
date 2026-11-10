using Punchy.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Flora
{
    // T-1: what the shot tool's ready gate asks the flora before it takes a picture. Read only; nothing here changes a draw.
    public partial class FloraSystem
    {
        /// <summary>
        /// Whether the plants round this camera point are in: built, every tile in each layer's range placed and uploaded,
        /// no placement batch in flight, and (with cached far shadows) no shadow-cache redraw half done. Otherwise false, with
        /// what it is still waiting on (e.g. "flora tiles 3 missing (layer 2)").
        /// </summary>
        public bool ShotReady(Vector3 camera, out string waiting)
        {
            waiting = null;
            if (!built)
            {
                waiting = "flora not built";
                return false;
            }
            if (Punchy.Terrain.CaveSystem.SurfaceHidden >= 0.99f)
                return true;   // deep in a cave nothing on the surface is placed or drawn
            var camXZ = new float2(camera.x, camera.z);
            foreach (var l in layers)
            {
                if (l == null || l.ModelCount == 0)
                    continue;
                if (l.PlacingTiles.IsCreated)
                {
                    waiting = $"flora placing (layer {l.Index})";
                    return false;
                }
                if (l.Dirty)
                {
                    waiting = $"flora upload (layer {l.Index})";
                    return false;
                }
                int missing = 0;
                var size = l.Settings.TileSize;
                var r = (int)math.ceil(l.Radius / size) + 1;
                var centre = (int2)math.floor(camXZ / size);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    var c = centre + new int2(x, z);
                    var min = (float2)c * size;
                    if (!l.Tiles.ContainsKey(c) && math.distance(math.clamp(camXZ, min, min + size), camXZ) < l.Radius)
                        missing++;
                }
                if (missing > 0)
                {
                    waiting = $"flora tiles {missing} missing (layer {l.Index})";
                    return false;
                }
            }
            if (FloraShadowOptions.CachedFarShadows && (cacheStep >= 0 || !cacheReady))
            {
                waiting = "flora shadow cache redraw";
                return false;
            }
            return true;
        }
    }
}
