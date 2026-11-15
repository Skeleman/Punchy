using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// A written check on how varied a scarp's ledges are (W-155 row 1b): walks each face in columns half a metre
    /// apart, finds the ledges in each column (stretches where the face runs out faster than it drops), and reports
    /// per scarp how long a ledge carries on along the rim before it breaks, how wide ledges are, and the longest
    /// straight band (a ledge that keeps its width and height). Not used by the game; called from tests and probes.
    /// </summary>
    public static class ScarpLedges
    {
        /// <summary>Spacing of the columns along the rim and of the samples down the face (m).</summary>
        const float Along = 0.5f, Down = 0.1f;

        /// <summary>A ledge runs out at least this much per metre of drop (about 34 degrees from level or flatter).</summary>
        const float Flat = 1.5f;

        /// <summary>One ledge in one column: its bed, width out from the face and height.</summary>
        struct Ledge
        {
            public int Bed;
            public float Width, Y;
        }

        /// <summary>The ledge report for every scarp in the set, one block per scarp.</summary>
        public static string Log(ref ScarpSetBlob set)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < set.Scarps.Length; i++)
                Log(ref set.Scarps[i], i, sb);
            return sb.ToString();
        }

        static void Log(ref Scarp sc, int index, StringBuilder sb)
        {
            // Columns along the whole rim, in order.
            var columns = new List<List<Ledge>>();
            for (int seg = 0; seg < sc.Nodes.Length - 1; seg++)
            {
                float len = math.distance(sc.Nodes[seg].P, sc.Nodes[seg + 1].P);
                int n = math.max(1, (int)math.round(len / Along));
                for (int k = 0; k < n; k++)
                {
                    ScarpSet.Column(ref sc, seg, (k + 0.5f) / n, out var at, out _, out _);
                    columns.Add(Column(ref sc, at));
                }
            }

            // Per bed: runs of columns that hold a ledge on it, and bands that also keep their width and height.
            var runs = new List<float>();
            var widths = new List<float>();
            float longestBand = 0f;
            int bandBed = -1, bandsOver8 = 0;
            var open = new Dictionary<int, (int run, int band, float w0, float y0)>();
            var seen = new HashSet<int>();
            for (int c = 0; c <= columns.Count; c++)
            {
                seen.Clear();
                if (c < columns.Count)
                {
                    foreach (var l in columns[c])
                    {
                        if (!seen.Add(l.Bed))
                            continue;
                        widths.Add(l.Width);
                        if (open.TryGetValue(l.Bed, out var o))
                        {
                            bool same = math.abs(l.Width - o.w0) <= 0.2f * o.w0 + 0.05f && math.abs(l.Y - o.y0) <= 0.3f;
                            if (!same)
                                CloseBand(o.band, l.Bed, ref longestBand, ref bandBed, ref bandsOver8);
                            open[l.Bed] = same ? (o.run + 1, o.band + 1, o.w0, o.y0) : (o.run + 1, 1, l.Width, l.Y);
                        }
                        else
                            open[l.Bed] = (1, 1, l.Width, l.Y);
                    }
                }
                // Beds with no ledge in this column end their run and band.
                var ended = new List<int>();
                foreach (var kv in open)
                    if (!seen.Contains(kv.Key))
                        ended.Add(kv.Key);
                foreach (var b in ended)
                {
                    var o = open[b];
                    runs.Add(o.run * Along);
                    CloseBand(o.band, b, ref longestBand, ref bandBed, ref bandsOver8);
                    open.Remove(b);
                }
            }

            runs.Sort();
            widths.Sort();
            sb.Append($"[ledges] scarp {index} {sc.Kind} seed {sc.Seed}: {columns.Count} columns over {sc.Length:F0} m, ");
            if (widths.Count == 0)
            {
                sb.AppendLine("no ledges");
                return;
            }
            sb.Append($"{widths.Count / (float)columns.Count:F1} ledges per column; ");
            sb.Append($"run along rim m: median {Pick(runs, 0.5f):F1}, p90 {Pick(runs, 0.9f):F1}, max {runs[runs.Count - 1]:F1} ({runs.Count} runs); ");
            sb.Append($"width m: min {widths[0]:F2}, p10 {Pick(widths, 0.1f):F2}, median {Pick(widths, 0.5f):F2}, p90 {Pick(widths, 0.9f):F2}, max {widths[widths.Count - 1]:F2}; ");
            sb.AppendLine($"longest straight band {longestBand:F1} m (bed {bandBed}), bands over 8 m: {bandsOver8}");
        }

        static void CloseBand(int columns, int bed, ref float longest, ref int longestBed, ref int over8)
        {
            float m = columns * Along;
            if (m > 8f)
                over8++;
            if (m > longest)
            {
                longest = m;
                longestBed = bed;
            }
        }

        static float Pick(List<float> sorted, float q) => sorted[math.clamp((int)(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

        /// <summary>The ledges down one column of the face, skipping the toe (scree or wash) at the foot.</summary>
        static List<Ledge> Column(ref Scarp sc, in ScarpAt at)
        {
            var list = new List<Ledge>();
            float drop = at.RimY - at.FootY;
            if (drop < 1f)
                return list;
            var cracks = ScarpSet.Cracks(ref sc, at.S);
            float roll = 0.4f + 1.2f * sc.Age;
            float prev = ScarpSet.Face(ref sc, at, cracks, at.RimY);
            float start = -1f, gain = 0f, top = 0f;
            for (float u = Down; u <= drop + 1e-3f; u += Down)
            {
                float y = at.RimY - u, f = ScarpSet.Face(ref sc, at, cracks, y);
                float toe = (at.W - (y - at.FootY) / sc.Toe) * math.smoothstep(0f, roll, u);
                bool shelf = f - prev >= Flat * Down && f > toe + 1e-3f && f < at.W - 1e-3f && u > roll;
                if (shelf)
                {
                    if (start < 0f)
                    {
                        start = u;
                        top = y + Down;
                        gain = 0f;
                    }
                    gain += f - prev;
                }
                else if (start >= 0f)
                {
                    Add(ref sc, at, cracks, top, y, gain, list);
                    start = -1f;
                }
                prev = f;
            }
            if (start >= 0f)
                Add(ref sc, at, cracks, top, at.FootY, gain, list);
            return list;
        }

        static void Add(ref Scarp sc, in ScarpAt at, float3 cracks, float top, float bottom, float width, List<Ledge> list)
        {
            if (width < 0.1f)
                return;
            float mid = 0.5f * (top + bottom);
            int bed = (int)ScarpSet.Bed(ref sc, ScarpSet.BedY(ref sc, at.S, mid, cracks));
            list.Add(new Ledge { Bed = bed, Width = width, Y = top });
        }
    }
}
