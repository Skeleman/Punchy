using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The sea folk settlement (#37): one settlement on the bed of the island's largest deep lake. It is two landmarks:
    /// the village (Resources/SeaFolkVillage), whose placed props are shell domes, a spiral spire tall enough to break
    /// the surface, glow stones and lake-bed details (kelp gardens, shell mosaics, bubble vents, sunken trade goods, coral arches,
    /// glowing anemones), all StructureKit in the sea folk's trim; and its landing (Resources/SeaFolkLanding) on the
    /// nearest bank, a pier running out towards the village so it reads from the shore. Both are appended after every
    /// other place (turtle settlements included), each on its own spot clear of the landmarks so far. Neither stamps
    /// the ground or the water. Island v2 and v3. W-138: their spots are found first in SettlementBaking (from the
    /// rivers and the landmarks so far), so a road can lead to the landing, and the later pass keeps those spots.
    /// </summary>
    public static class SeaFolkBaking
    {
        public const string VillageName = "SeaFolkVillage";
        public const string LandingName = "SeaFolkLanding";
        /// <summary>The shallowest lake (m, surface to deepest bed) that can hold a settlement.</summary>
        public const float MinDepth = 8f;
        /// <summary>The village middle's depth range (m): deep enough to hide the domes, shallow enough for spires to break the surface.</summary>
        const float Shallowest = 9f, Deepest = 16f;

        public static string LastLog = "";

        public static LandmarkType LoadVillage() => UnityEngine.Resources.Load<LandmarkType>(VillageName);
        public static LandmarkType LoadLanding() => UnityEngine.Resources.Load<LandmarkType>(LandingName);

        /// <summary>
        /// The sea folk village and its landing, as landmark instances to append (none when no lake is deep enough).
        /// W-138: given <paramref name="spots"/> (an earlier Place's pair), it keeps those spots and only re-reads their ground.
        /// </summary>
        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<LandmarkType> types,
                                                   IReadOnlyList<LandmarkInstance> spots = null)
        {
            var placed = new List<LandmarkInstance>();
            LastLog = "";
            if (spots != null && spots.Count == 2)
            {
                placed.Add(Make(source, spots[0].Position, spots[0].Facing, spots[0].Type, 0x5EAu));
                placed.Add(Make(source, spots[1].Position, spots[1].Facing, spots[1].Type, 0x5EBu));
                LastLog = $"[sea folk] village at {spots[0].Position.x:F0},{spots[0].Position.y:F0}; landing at {spots[1].Position.x:F0},{spots[1].Position.y:F0} (as found for the roads)";
                UnityEngine.Debug.Log(LastLog);
                return placed;
            }
            int tv = -1, tl = -1;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] == null) continue;
                if (types[i].name == VillageName) tv = i;
                if (types[i].name == LandingName) tl = i;
            }
            if (tv < 0 || tl < 0 || !source.Rivers.IsCreated || !source.Landmarks.IsCreated)
            {
                LastLog = "[sea folk] none: no SeaFolkVillage/SeaFolkLanding type or no rivers";
                UnityEngine.Debug.Log(LastLog);
                return placed;
            }
            ref var m = ref source.Rivers.Value;
            ref var set = ref source.Landmarks.Value;
            var res = m.Resolution;

            // Connected lake cells (as RiverSampling: LakeDepth > 0.3); the biggest lake at least MinDepth deep wins,
            // its lowest cell index breaking ties, so the choice is fixed.
            // W-76: the bed under every lake cell first, in parallel rows (sampling only reads blobs); the fill reads it.
            var bed = new float[res * res];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, res, row =>
            {
                ref var rv = ref src.Rivers.Value;
                for (int c = row * res; c < (row + 1) * res; c++)
                    if (rv.LakeDepth[c] > 0.3f)
                        bed[c] = src.SampleHeight(Centre(ref rv, c));
            });
            var label = new int[res * res];
            var stack = new Stack<int>();
            int best = 0, bestCells = 0, bestFirst = 0, next = 0;
            float bestDepth = 0f;
            for (int i = 0; i < res * res; i++)
            {
                if (label[i] != 0 || m.LakeDepth[i] <= 0.3f) continue;
                next++; label[i] = next; stack.Push(i);
                int cells = 0; float deep = 0f;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    cells++;
                    deep = math.max(deep, m.LakeLevel[c] - bed[c]);
                    int cx = c % res, cy = c / res;
                    for (int q = 0; q < 4; q++)
                    {
                        if ((q == 0 && cx == 0) || (q == 1 && cx == res - 1) || (q == 2 && cy == 0) || (q == 3 && cy == res - 1)) continue;
                        var k = q == 0 ? c - 1 : q == 1 ? c + 1 : q == 2 ? c - res : c + res;
                        if (label[k] == 0 && m.LakeDepth[k] > 0.3f) { label[k] = next; stack.Push(k); }
                    }
                }
                if (deep >= MinDepth && cells > bestCells)
                {
                    best = next; bestCells = cells; bestFirst = i; bestDepth = deep;
                }
            }
            if (best == 0)
            {
                LastLog = $"[sea folk] none: no lake at least {MinDepth} m deep";
                UnityEngine.Debug.Log(LastLog);
                return placed;
            }
            var level = m.LakeLevel[bestFirst];
            float2 sum = 0f;
            for (int i = 0; i < res * res; i++)
                if (label[i] == best) sum += Centre(ref m, i);
            var middle = sum / bestCells;

            // The village middle: the lake cell nearest the lake's middle that is Shallowest..Deepest deep, with its whole
            // building ring under water and clear of every landmark.
            var village = types[tv];
            var candidates = new List<(float d, int c)>();
            for (int i = 0; i < res * res; i++)
                if (label[i] == best) candidates.Add((math.distancesq(Centre(ref m, i), middle), i));
            candidates.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.c.CompareTo(b.c));
            var found = false;
            float2 centre = 0f, shore = 0f, toShore = 0f;
            foreach (var (_, c) in candidates)
            {
                centre = Centre(ref m, c);
                var d = level - source.SampleHeight(centre);
                if (d < Shallowest || d > Deepest || !Clear(ref set, types, village.Radius, centre)) continue;
                var wet = true;
                for (int k = 0; k < 8 && wet; k++)
                {
                    var spot = centre + new float2(math.cos(k * math.PI / 4f), math.sin(k * math.PI / 4f)) * village.Radius * 0.85f;
                    wet = level - source.SampleHeight(spot) > 3f;
                }
                if (!wet || !Shore(source, ref set, types, types[tl].Radius, centre, level, out shore, out toShore)) continue;
                found = true;
                break;
            }
            if (!found)
            {
                LastLog = $"[sea folk] none: lake of {bestCells} cells ({bestDepth:F1} m deep) has no spot {Shallowest}-{Deepest} m deep with a bank";
                UnityEngine.Debug.Log(LastLog);
                return placed;
            }
            placed.Add(Make(source, centre, toShore, tv, 0x5EAu));
            placed.Add(Make(source, shore, -toShore, tl, 0x5EBu));
            LastLog = $"[sea folk] lake {bestCells} cells ({bestCells * m.CellSize * m.CellSize / 1e4f:F1} ha), level {level:F1}, {bestDepth:F1} m deep, middle {middle.x:F0},{middle.y:F0}\n" +
                      $"[sea folk] village at {centre.x:F0},{centre.y:F0} bed {source.SampleHeight(centre):F1} ({level - source.SampleHeight(centre):F1} m down); landing at {shore.x:F0},{shore.y:F0} ground {source.SampleHeight(shore):F1}, {math.distance(centre, shore):F0} m away";
            UnityEngine.Debug.Log(LastLog);
            return placed;
        }

        static float2 Centre(ref RiverMapBlob m, int c) => m.Origin + (new float2(c % m.Resolution, c / m.Resolution) + 0.5f) * m.CellSize;

        static LandmarkInstance Make(in TerrainSource source, float2 at, float2 dir, int type, uint salt)
        {
            var key = math.hash(new uint3(math.asuint(at.x), math.asuint(at.y), salt)) | 1u;
            return new LandmarkInstance
            {
                Position = at,
                Facing = dir,
                Scale = 1f,
                Type = type,
                BaseHeight = source.SampleHeight(at),
                Seed = (BiomeSampling.Scramble(key) >> 8) * (1f / 16777216f),
                Key = key
            };
        }

        /// <summary>The nearest bank from the village (64 ways round, 2 m steps): the first dry spot, 2 m up the bank, on low, river-free land.</summary>
        static bool Shore(in TerrainSource source, ref LandmarkSetBlob set, IReadOnlyList<LandmarkType> types, float radius,
                          float2 centre, float level, out float2 shore, out float2 dir)
        {
            shore = 0f; dir = 0f;
            var bestD = float.MaxValue;
            for (int k = 0; k < 64; k++)
            {
                var a = k * math.PI * 2f / 64f;
                var d = new float2(math.cos(a), math.sin(a));
                for (float r = 8f; r < 400f && r < bestD; r += 2f)
                {
                    var p = centre + d * r;
                    var h = source.SampleHeight(p);
                    if (h < level - 0.1f) continue;
                    p += d * 2f;
                    h = source.SampleHeight(p);
                    if (h > level - 0.1f && h < level + 2f && source.OceanFactor(p) < 0.5f && !InRiver(source, p) && Clear(ref set, types, radius, p))
                    {
                        bestD = r; shore = p; dir = d;
                    }
                    break;
                }
            }
            return bestD < float.MaxValue;
        }

        static bool Clear(ref LandmarkSetBlob set, IReadOnlyList<LandmarkType> types, float radius, float2 at)
        {
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var other = set.Instances[i];
                var reach = radius + (other.Type < types.Count && types[other.Type] != null ? types[other.Type].Radius * other.Scale : 30f) + 20f;
                if (math.distance(other.Position, at) < reach)
                    return false;
            }
            return true;
        }

        static bool InRiver(in TerrainSource source, float2 xz)
        {
            ref var rivers = ref source.Rivers.Value;
            var cell = (int2)math.floor((xz - rivers.Origin) / rivers.CellSize);
            if (math.any(cell < 0) || math.any(cell >= rivers.Resolution))
                return false;
            return rivers.Width[cell.y * rivers.Resolution + cell.x] > 0f;
        }
    }
}
