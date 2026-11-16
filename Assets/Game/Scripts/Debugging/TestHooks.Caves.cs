#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for cave layout (W-176): audits the built caves for passages that cut pits through each other and for
    /// floors that drop off a ledge between two joined nodes, the faults CaveBaking's Cuts and Grade exist to prevent.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>What CaveLayoutProblems last measured: caves, passages, pits, ledges and the steepest floor grade.</summary>
        public static string CaveLayoutTrail;

        /// <summary>
        /// W-176: every pit and ledge in the built caves ("" when none). A pit is two passages of one cave (not joined
        /// and not one link apart) that pass within their radii of each other with floors a metre or more apart and less
        /// than 1.5 m of rock between them (CaveBaking's Cuts rule). A ledge is a passage or chamber link whose floor
        /// grade is steeper than CaveBaking.MaxGrade. Tunnels, shafts and rooms have their own rules and are skipped.
        /// The counts go to CaveLayoutTrail.
        /// </summary>
        public static string CaveLayoutProblems()
        {
            CaveLayoutTrail = "";
            if (!Source(out var s) || !s.Caves.IsCreated) return "no caves";
            ref var set = ref s.Caves.Value;
            var problems = new List<string>();
            int caves = 0, links = 0, pits = 0, ledges = 0;
            float steepest = 0f;
            var near = new Dictionary<int, HashSet<int>>();
            var used = new List<int>();
            for (int cave = 0; cave < set.Caves.Length; cave++)
            {
                var data = set.Caves[cave];
                if (data.Tunnel != 0) continue;
                caves++;
                near.Clear();
                used.Clear();
                for (int i = data.FirstSegment; i < data.FirstSegment + data.SegmentCount; i++)
                {
                    var seg = set.Segments[i];
                    if (!CaveWalkable(set.Kinds[seg.A]) || !CaveWalkable(set.Kinds[seg.B])) continue;
                    used.Add(i);
                    CaveLink(near, seg.A, seg.B);
                    CaveLink(near, seg.B, seg.A);
                }
                float Floor(float3 at, float r) => at.y - CaveBaking.FloorDropOf(r, data.Profile, data.Rock);
                float Top(float3 at, float r) => at.y + r * data.Profile.y + CaveSampling.JaggedReach * data.Rock.x;
                foreach (var i in used)
                {
                    links++;
                    var seg = set.Segments[i];
                    var na = set.Nodes[seg.A];
                    var nb = set.Nodes[seg.B];
                    var grade = math.abs(Floor(na.Position, na.Radius) - Floor(nb.Position, nb.Radius))
                        / math.max(math.distance(na.Position.xz, nb.Position.xz), 1f);
                    steepest = math.max(steepest, grade);
                    if (grade > CaveBaking.MaxGrade + 0.02f)
                    {
                        ledges++;
                        if (problems.Count < 8) problems.Add($"cave {cave} ledge at {na.Position.x:F0},{na.Position.y:F0},{na.Position.z:F0}: grade {grade:F2}");
                    }
                }
                for (int x = 0; x < used.Count; x++)
                for (int y = 0; y < x; y++)
                {
                    var a = set.Segments[used[x]];
                    var b = set.Segments[used[y]];
                    if (CaveClose(near, a.A, b) || CaveClose(near, a.B, b)) continue;
                    var ma = set.Nodes[b.A];
                    var mb = set.Nodes[b.B];
                    for (int k = 0; k <= 2; k++)
                    {
                        var at = math.lerp(set.Nodes[a.A].Position, set.Nodes[a.B].Position, k * 0.5f);
                        var ar = math.lerp(set.Nodes[a.A].Radius, set.Nodes[a.B].Radius, k * 0.5f);
                        var ab = mb.Position.xz - ma.Position.xz;
                        var t = math.saturate(math.dot(at.xz - ma.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                        var mp = math.lerp(ma.Position, mb.Position, t);
                        var mr = math.lerp(ma.Radius, mb.Radius, t);
                        if (math.distance(at.xz, mp.xz) > (ar + mr) * data.Profile.x + 1f) continue;
                        float fa = Floor(at, ar), fm = Floor(mp, mr);
                        if (math.abs(fa - fm) < 1f) continue;
                        var clear = fa > fm ? fa - Top(mp, mr) : fm - Top(at, ar);
                        if (clear >= 1.5f) continue;
                        pits++;
                        if (problems.Count < 8)
                            problems.Add($"cave {cave} pit at {at.x:F0},{at.y:F0},{at.z:F0}: floors {math.abs(fa - fm):F1} m apart, {clear:F1} m rock "
                                + $"(links {used[x] - data.FirstSegment} {Describe(ref set, a)} and {used[y] - data.FirstSegment} {Describe(ref set, b)})");
                        break;
                    }
                }
            }
            CaveLayoutTrail = $"{caves} caves, {links} links: {pits} pits, {ledges} ledges, steepest grade {steepest:F2}";
            return pits + ledges == 0 ? "" : $"{pits} pits, {ledges} ledges: " + string.Join("; ", problems);
        }

        /// <summary>A link's nodes for a problem line: kind initial and radius at each end.</summary>
        static string Describe(ref CaveSetBlob set, CaveSegment link)
            => $"{set.Kinds[link.A].ToString()[0]}{set.Nodes[link.A].Radius:F1}-{set.Kinds[link.B].ToString()[0]}{set.Nodes[link.B].Radius:F1}";

        static bool CaveWalkable(CaveNodeKind kind) => kind == CaveNodeKind.Passage || kind == CaveNodeKind.Chamber;

        static void CaveLink(Dictionary<int, HashSet<int>> near, int from, int to)
        {
            if (!near.TryGetValue(from, out var list)) near[from] = list = new HashSet<int>();
            list.Add(to);
        }

        /// <summary>Whether node n is an end of segment b or joined to one by a link (a branch leaves its parent there).</summary>
        static bool CaveClose(Dictionary<int, HashSet<int>> near, int n, CaveSegment b)
            => n == b.A || n == b.B || near.TryGetValue(n, out var list) && (list.Contains(b.A) || list.Contains(b.B));
    }
}
#endif
