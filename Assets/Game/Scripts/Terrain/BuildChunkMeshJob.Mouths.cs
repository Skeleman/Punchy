using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The mesh side of cave mouths and the temple cut: DropSpecks clears stray islands of the entrance cut, and
    /// MouthCutAt gives the mouth cut the #301 patch (MouthPatch) and the scarp debris test against.
    /// </summary>
    partial struct BuildChunkMeshJob
    {
        /// <summary>The largest stray island (m^2 of grid) DropSpecks removes: a hole speck beside a mouth, or a scrap of
        /// ground left inside one.</summary>
        public const float SpeckArea = 6f;

        /// <summary>
        /// W-174 r3 (#365): finds the small islands of the entrance cut on this chunk's v x v grid (cut &lt; 0 hole, &gt; 0
        /// ground; 8-connected) that touch no chunk edge and cover at most SpeckArea, and flips each one's sign so the
        /// mesh drops it (a hole speck is filled, a ground scrap is opened). Works at grid resolution, after the cut is
        /// sampled, so the cut field itself stays as smooth as the grid can follow; an island on a chunk edge is kept, so
        /// both chunks on the edge agree. Returns whether any island was flipped (flip[i] marks its vertices).
        /// </summary>
        public static bool DropSpecks(NativeArray<float> cut, int v, float step, NativeArray<bool> flip)
        {
            var label = new NativeArray<byte>(v * v, Allocator.Temp);   // in this flood
            var seen = new NativeArray<bool>(v * v, Allocator.Temp);    // in any flood (rim vertices may be in several)
            var stack = new NativeList<int>(64, Allocator.Temp);
            var members = new NativeList<int>(64, Allocator.Temp);
            var maxCount = math.max(1, (int)(SpeckArea / (step * step)));
            var any = false;
            for (int i = 0; i < flip.Length; i++)
                flip[i] = false;
            for (int start = 0; start < v * v; start++)
            {
                if (cut[start] == 0f)
                    continue;
                var hole = cut[start] < 0f;
                var edge = false;
                stack.Clear();
                members.Clear();
                if (seen[start])
                    continue;
                stack.Add(start);
                label[start] = 1;
                while (stack.Length > 0)
                {
                    var i = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);
                    members.Add(i);
                    if (cut[i] != 0f)
                        seen[i] = true;
                    int x = i % v, z = i / v;
                    edge |= x == 0 || z == 0 || x == v - 1 || z == v - 1;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= v || nz >= v)
                            continue;
                        var j = nz * v + nx;
                        if (label[j] != 0 || (cut[j] != 0f && cut[j] < 0f != hole))
                            continue;   // rim vertices (0) join either side, so a hole tied to its mouth only by rim is no speck
                        label[j] = 1;
                        stack.Add(j);
                    }
                }
                // The flood stops only at the other side, so a small interior ground island is one ringed by hole.
                for (int k = 0; k < members.Length; k++)
                    label[members[k]] = 0;
                if (edge || members.Length > maxCount)
                    continue;
                for (int k = 0; k < members.Length; k++)
                    flip[members[k]] = cut[members[k]] != 0f;
                any = true;
            }
            label.Dispose();
            seen.Dispose();
            stack.Dispose();
            members.Dispose();
            return any;
        }

        /// <summary>The mesh's mouth cut at xz for a grid of this step (the 5-tap MouthCut; 100 away from caves).</summary>
        public static float MouthCutAt(in TerrainSource source, float2 xz, float step)
        {
            if (!source.Caves.IsCreated || !CaveSampling.NearColumn(ref source.Caves.Value, xz))
                return 100f;
            return MouthCut(source, xz, source.SampleHeight(xz), step);
        }
    }
}
