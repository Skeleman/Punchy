using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Seats: Sit anchors (benches, logs set out to sit on), each split into a place per sitter along it, as townsfolk
    /// use them. A player sitting down near one takes its nearest place; every machine finds the same place from the
    /// anchors alone, so a remote player's body sits at the seat's height too, with nothing extra sent.
    /// </summary>
    public static class Seats
    {
        /// <summary>The nearest seat place within reach of a point: where the sitter's hips go, and the way it faces.</summary>
        public static bool Nearest(ref AnchorSetBlob set, float3 at, float within, out float3 seat, out float2 facing)
        {
            seat = default;
            facing = default;
            var near = new NativeList<int>(8, Allocator.Temp);
            Anchors.Near(ref set, at, within + 2f, AnchorKind.Sit, ref near);
            var best = within * within;
            var found = false;
            foreach (var i in near)
            {
                ref var a = ref set.Anchors[i];
                if (a.InCave)
                    continue;
                var across = new float2(-a.Facing.y, a.Facing.x);
                var slots = math.max((int)a.Capacity, 1);
                for (int k = 0; k < slots; k++)
                {
                    var spot = a.Position.xz + across * ((k - (slots - 1) * 0.5f) * a.Size);
                    var d = math.distancesq(spot, at.xz);
                    if (d >= best)
                        continue;
                    best = d;
                    seat = new float3(spot.x, a.Position.y, spot.y);
                    facing = a.Facing;
                    found = true;
                }
            }
            near.Dispose();
            return found;
        }
    }
}
