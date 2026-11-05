using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Fishing villages (#36): a settlement type at river mouths, where a river meets the sea. A village is a landmark
    /// (Resources/FishingVillage) whose placed props are its buildings, all from StructureKit in the fishing folk's
    /// coastal trim: stilt houses round the middle, docks running out to sea from the waterline, shore details (boats,
    /// nets, fish racks, crab pots, driftwood, lanterns) seeded per prop, so no two villages look alike.
    /// Villages go in after every other place (landmarks, settlements, roads, the salt tower and the caves), each on
    /// its own new spot, so nothing that was placed before moves. No roads lead to them yet. Island v2 only.
    /// </summary>
    public static class FishingVillageBaking
    {
        public const string TypeName = "FishingVillage";
        const int Most = 4;
        const float Apart = 350f;
        /// <summary>How far inland of the waterline the middle sits: the docks' ring (0.6 radius) lands on the shore.</summary>
        const float Inland = 24f;
        /// <summary>The longest dock (m, StructureKit.Dock at the biggest species size): its far end must be over water.</summary>
        const float DockReach = 19f;
        /// <summary>How far a dock's run out is checked for river water (FloraPlacement.RiverDockReach).</summary>
        const float RiverDockReach = 30f;
        /// <summary>Shore angles tried in turn (degrees off straight out to sea) until the docks clear the river.</summary>
        static readonly float[] Turns = { 0f, 30f, -30f, 60f, -60f };
        /// <summary>W-105: stilt house spots a village needs out over the shallows (the type offers more; flora drops the rest).</summary>
        const int StiltHousesAtLeast = 1;

        public static string LastLog = "";

        /// <summary>The fishing village type (a Resources asset, so island v2's runtime bake has it too).</summary>
        public static LandmarkType LoadType() => UnityEngine.Resources.Load<LandmarkType>(TypeName);

        /// <summary>Up to four fishing villages at the widest river mouths, as landmark instances to append.</summary>
        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<LandmarkType> types)
        {
            var placed = new List<LandmarkInstance>();
            LastLog = "";
            var t = -1;
            for (int i = 0; i < types.Count; i++)
                if (types[i] != null && types[i].name == TypeName)
                    t = i;
            if (t < 0 || !source.Rivers.IsCreated || !source.Landmarks.IsCreated)
            {
                LastLog = "[villages] none: no FishingVillage type or no rivers";
                return placed;
            }
            var type = types[t];
            ref var rivers = ref source.Rivers.Value;
            ref var set = ref source.Landmarks.Value;
            var sea = source.World.SeaLevel;
            var res = rivers.Resolution;
            var origin = rivers.Origin;
            var cellSize = rivers.CellSize;
            float2 At(int c) => origin + (new float2(c % res, c / res) + 0.5f) * cellSize;

            // Mouths: the last river cell on land before the sea, widest first (index breaks ties, so the order is fixed).
            // W-76: the cells are tested in parallel rows (sampling only reads blobs) and gathered in index order.
            var isMouth = new bool[rivers.Width.Length];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, (isMouth.Length + res - 1) / res, row =>
            {
                ref var rv = ref src.Rivers.Value;
                for (int c = row * res; c < math.min(isMouth.Length, (row + 1) * res); c++)
                {
                    if (rv.Width[c] <= 0f || src.OceanFactor(At(c)) > 0.5f)
                        continue;
                    var down = rv.Down[c];
                    isMouth[c] = down < 0 || src.OceanFactor(At(down)) > 0.5f || src.SampleHeight(At(down)) < sea;
                }
            });
            var mouths = new List<int>();
            for (int c = 0; c < isMouth.Length; c++)
                if (isMouth[c])
                    mouths.Add(c);
            var widths = new Dictionary<int, float>();
            foreach (var m in mouths)
                widths[m] = rivers.Width[m];
            mouths.Sort((a, b) => widths[a] != widths[b] ? widths[b].CompareTo(widths[a]) : a.CompareTo(b));

            var tried = 0;
            foreach (var m in mouths)
            {
                if (placed.Count >= Most)
                    break;
                tried++;
                var mouth = At(m);
                // Seaward: towards the most open sea of the eight neighbours a few cells out.
                var dir = float2.zero;
                var best = -1f;
                for (int k = 0; k < 8; k++)
                {
                    var a = k * math.PI / 4f;
                    var d = new float2(math.cos(a), math.sin(a));
                    var o = source.OceanFactor(mouth + d * 60f) - math.max(0f, source.SampleHeight(mouth + d * 60f) - sea) * 0.01f;
                    if (o > best) { best = o; dir = d; }
                }
                var side0 = (BiomeSampling.Scramble((uint)m * 0x9E3779B9u) & 1) == 0 ? 1f : -1f;
                for (int s = 0; s < 2; s++)
                {
                    var side = s == 0 ? side0 : -side0;
                    var bank = mouth + new float2(-dir.y, dir.x) * side * (rivers.Width[m] * 0.5f + 36f);
                    if (!Shore(source, bank, dir, sea, out var waterline))
                        continue;
                    // W-105 (#36): if the straight-out layout puts a dock on river water, try the shore angle turned by
                    // 30 then 60 degrees each way, so the village keeps its wharf. The straight-out try comes first, so a
                    // village that fitted before stays where it was.
                    var fits = false;
                    var facing = dir;
                    var centre = waterline;
                    foreach (var turn in Turns)
                    {
                        math.sincos(math.radians(turn), out var sn, out var cs);
                        facing = new float2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                        centre = waterline - facing * Inland;
                        if (fits = Fits(source, ref set, types, type, placed, centre, facing, sea))
                            break;
                    }
                    if (!fits)
                        continue;
                    var key = math.hash(new uint3(math.asuint(centre.x), math.asuint(centre.y), 0xF15Au)) | 1u;
                    placed.Add(new LandmarkInstance
                    {
                        Position = centre,
                        Facing = facing,
                        Scale = 1f,
                        Type = t,
                        BaseHeight = source.SampleHeight(centre),
                        Seed = (BiomeSampling.Scramble(key) >> 8) * (1f / 16777216f),
                        Key = key
                    });
                    LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[villages] fishing village {placed.Count} at {centre.x:F0},{centre.y:F0} facing {math.degrees(math.atan2(facing.y, facing.x)):F0} deg, mouth width {rivers.Width[m]:F1}";
                    break;
                }
            }
            // W-91 (#36): count each village's building plots on river water (flora leaves those plots empty).
            for (int v = 0; v < placed.Count; v++)
            {
                int plots = 0, river = 0;
                foreach (var prop in type.Props)
                {
                    if (prop.Species == null || prop.Species.Form != Punchy.Flora.FloraForm.KitBuilding)
                        continue;
                    var shape = prop.Species.KitShape;
                    var dock = shape == KitShape.Dock || shape == KitShape.Wharf;
                    if (!dock && shape != KitShape.House && shape != KitShape.StiltHouse)
                        continue;
                    for (int k = 0; k < math.max(1, prop.Count); k++)
                    {
                        var spot = LandmarkSampling.ToWorld(placed[v], LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / math.max(1, prop.Count)), type.Radius);
                        plots++;
                        if (LandmarkSampling.OnRiver(source, spot, placed[v].Facing, dock ? 30f : 0f))
                            river++;
                    }
                }
                LastLog += $"\n[villages] village {v + 1}: {river} of {plots} building plots on river water, dropped";
            }
            LastLog = $"[villages] {placed.Count} fishing villages from {mouths.Count} mouth cells ({tried} tried)" + (LastLog.Length > 0 ? "\n" + LastLog : "");
            UnityEngine.Debug.Log(LastLog);
            return placed;
        }

        /// <summary>The waterline seaward of <paramref name="bank"/>: land behind, sea ahead, within 120 m.</summary>
        static bool Shore(in TerrainSource source, float2 bank, float2 dir, float sea, out float2 waterline)
        {
            waterline = bank;
            var wasLand = false;
            for (var d = -60f; d <= 120f; d += 2f)
            {
                var p = bank + dir * d;
                var land = source.SampleHeight(p) > sea + 0.2f;
                if (wasLand && !land)
                {
                    waterline = p;
                    return true;
                }
                wasLand = land;
            }
            return false;
        }

        /// <summary>
        /// Dry, gentle ground for the middle, a low bank with shallows out past it for every stilt house (W-105), out of
        /// the river and clear of every landmark and village.
        /// </summary>
        static bool Fits(in TerrainSource source, ref LandmarkSetBlob set, IReadOnlyList<LandmarkType> types, LandmarkType type,
                         List<LandmarkInstance> placed, float2 centre, float2 dir, float sea)
        {
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var other = set.Instances[i];
                var reach = type.Radius + (other.Type < types.Count && types[other.Type] != null ? types[other.Type].Radius * other.Scale : 30f) + 40f;
                if (math.distance(other.Position, centre) < reach)
                    return false;
            }
            foreach (var v in placed)
                if (math.distance(v.Position, centre) < Apart)
                    return false;
            var mid = source.SampleHeight(centre);
            if (mid < sea + 0.8f || InRiver(source, centre))
                return false;
            // W-105: the type offers more stilt house spots than it needs; enough of them must have a low bank for the
            // plot and shallows out past it for the house (LandmarkSampling.StiltHouseFits). Flora leaves the rest empty.
            var inst = new LandmarkInstance { Position = centre, Facing = dir, Scale = 1f };
            var stilts = 0;
            foreach (var prop in type.Props)
            {
                if (prop.Species == null || prop.Species.KitShape != KitShape.StiltHouse)
                    continue;
                for (int k = 0; k < math.max(1, prop.Count); k++)
                    if (LandmarkSampling.StiltHouseFits(source, centre, LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / math.max(1, prop.Count)), type.Radius), out _))
                        stilts++;
            }
            if (stilts < StiltHousesAtLeast)
                return false;
            // Every dock runs out along the facing (FloraRule.FaceLandmark): its far end must stand in water, not on a flat beach.
            foreach (var prop in type.Props)
            {
                if (prop.Species == null || (prop.Species.KitShape != KitShape.Dock && prop.Species.KitShape != KitShape.Wharf))
                    continue;
                for (int k = 0; k < math.max(1, prop.Count); k++)
                {
                    var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / math.max(1, prop.Count)), type.Radius);
                    // W-105 (#36): nor on river water along its run out, where flora leaves the plot empty (W-91): the
                    // village at -3144,-1596 lost its wharf that way. The other bank or the next mouth gets it instead.
                    if (source.SampleHeight(spot + dir * DockReach) > sea - 0.6f || LandmarkSampling.OnRiver(source, spot, dir, RiverDockReach))
                        return false;
                }
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
