using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The banks of tall river notches as scarp records (W-153 row 3, rule 33: one kit for every cut bank). Where a
    /// later pass raised ground beside a river by more than <see cref="WallHeight"/>, the notch TerrainSource cuts
    /// leaves a wall that tall; each such run gets a <see cref="ScarpKind.RiverCut"/> record along each raised bank,
    /// so it carries the same beds, undercut, debris and skin as the hand-placed river cut bank. The records are
    /// <see cref="ScarpRecord.Late"/>: they are shaped after the plots that raised the ground.
    /// </summary>
    public static class RiverNotches
    {
        /// <summary>Raised ground lower than this (m) is left to the plain notch cut.</summary>
        public const float WallHeight = 2f;
        /// <summary>Spacing of the checks along each river segment (m).</summary>
        const float Step = 4f;
        /// <summary>How far each bank runs on past the raised stretch, so its tapered ends fall outside the wall (m).</summary>
        const float RunOn = 8f;
        /// <summary>A river cut bank's width from rim to foot (as <see cref="ScarpBake.Style"/> sets it).</summary>
        const float BankWidth = 3.2f;

        static readonly ScarpLayer[] Layers =
        {
            new ScarpLayer { Thickness = 0.6f, Hardness = 0.6f }, new ScarpLayer { Thickness = 0.4f, Hardness = 0.2f },
            new ScarpLayer { Thickness = 0.9f, Hardness = 0.5f }, new ScarpLayer { Thickness = 0.3f, Hardness = 0.3f }
        };

        public static List<ScarpRecord> Banks(in TerrainSource source)
        {
            var banks = new List<ScarpRecord>();
            if (!source.Rivers.IsCreated)
                return banks;
            // The ground as the later passes left it, before the notch.
            var raw = source;
            raw.RiverNotchOff = true;
            raw.Scarps = default;
            // ...and as the river left it, so natural valley sides (which also stand over the bed) are not taken for walls.
            var bare = raw;
            bare.Landmarks = default; bare.Paddies = default; bare.GrandFalls = default; bare.Settlements = default;
            ref var map = ref source.Rivers.Value;
            var rim = new List<float2>();
            var drop = new List<float>();
            for (int i = 0; i < map.Width.Length; i++)
            {
                int down = map.Down[i];
                if (map.Width[i] <= 0f || down < 0 || map.LakeDepth[i] > 0f || map.LakeDepth[down] > 0f)
                    continue;
                float2 a = map.Point(i), b = map.Point(down);
                var length = math.distance(a, b);
                if (length < 1f)
                    continue;
                var along = (b - a) / length;
                var side = new float2(along.y, -along.x);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    rim.Clear(); drop.Clear();
                    for (float s = 0f; s <= length + 0.01f; s += Step)
                    {
                        var centre = a + along * s;
                        var q = RiverSampling.Query(ref map, source.RiverSettings, centre, source.RiverRoughOn);
                        if (!q.Found)
                        {
                            Flush(banks, rim, drop, along, side * sgn, i, sgn);
                            continue;
                        }
                        // The rim stands where the scarp's foot meets the notch's shoulder beside the channel.
                        var off = q.Width * 0.5f + TerrainSource.NotchShoulder + BankWidth;
                        var p = centre + side * sgn * off;
                        var top = raw.SampleHeight(p);
                        var wall = top - q.Carve;
                        if (top - bare.SampleHeight(p) > WallHeight && wall > WallHeight + TerrainSource.NotchSlope * TerrainSource.NotchShoulder)
                        {
                            if (rim.Count == 0)
                            {
                                rim.Add(p - along * RunOn);
                                drop.Add(wall);
                            }
                            rim.Add(p);
                            drop.Add(wall);
                        }
                        else
                            Flush(banks, rim, drop, along, side * sgn, i, sgn);
                    }
                    Flush(banks, rim, drop, along, side * sgn, i, sgn);
                }
            }
            return banks;
        }

        /// <summary>Ends a raised run on one bank: a record if it has a stretch to it.</summary>
        static void Flush(List<ScarpRecord> banks, List<float2> rim, List<float> drop, float2 along, float2 outward, int node, int sgn)
        {
            if (rim.Count >= 2)
            {
                rim.Add(rim[rim.Count - 1] + along * RunOn);
                drop.Add(drop[drop.Count - 1]);
                banks.Add(new ScarpRecord
                {
                    Name = $"river notch bank {banks.Count}", Kind = ScarpKind.RiverCut, Late = true, Age = 0.15f,
                    Seed = math.hash(new int2(node, sgn)) | 1u, Down = -outward, Rim = rim.ToArray(), Drop = drop.ToArray(),
                    Layers = Layers
                });
            }
            rim.Clear();
            drop.Clear();
        }
    }
}
