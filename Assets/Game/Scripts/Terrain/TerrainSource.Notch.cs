using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// River notches (W-153 row 3). Rivers are baked on bare ground, and the passes after them (landmark mounds,
    /// stepped pools, settlement plots, scarps) can raise ground back over a channel, leaving the river climbing a
    /// rise it never routed over. After those passes the river's carved channel is laid in again: the ground is
    /// capped at the carved bed plus a steep wall rising from just past the bank, so the river cuts a short, steep
    /// notch through whatever was raised. The cap only takes back what a later pass added (never below the carved
    /// height), so routing, levels and water are unchanged. Walls tall enough to show get a river cut bank from the
    /// shared scarp kit (<see cref="RiverNotches"/>), shaped here too because it has to come after the plots.
    /// </summary>
    public partial struct TerrainSource
    {
        /// <summary>Rise of the notch wall per metre out from the bank (about 63 deg).</summary>
        public const float NotchSlope = 2f;
        /// <summary>Flat ground left beside the channel before the wall starts (m).</summary>
        public const float NotchShoulder = 1f;
        /// <summary>How far past the bank the cap can reach (m); taller walls stay as tall as the cap allows there.</summary>
        const float NotchReach = 14f;

        /// <summary>The ground after the later passes, with the river's channel cut back through it.</summary>
        float Notch(float2 xz, in RiverQuery river, float carved, float h)
        {
            if (UsesHeightmap || RiverNotchOff)
                return h;
            if (Scarps.IsCreated)
                h = ScarpSet.Shape(ref Scarps.Value, xz, h, late: true);
            if (!river.Found || h <= carved)
                return h;
            var off = river.Distance - river.Width * 0.5f - NotchShoulder;
            if (off > NotchReach)
                return h;
            var cap = river.Carve + NotchSlope * math.max(0f, off);
            return math.min(h, math.max(carved, cap));
        }
    }
}
