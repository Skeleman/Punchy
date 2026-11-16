using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Ambience
{
    /// <summary>
    /// Bench 74: a coarse grid over the open falls' boxes (RiverCurtains.hlsl), each cell listing the boxes whose reach
    /// touches it as 32 bits. The water and ground shaders look up their point's cell and test only those boxes, so a
    /// pixel near one fall of a chain no longer loops over every fall in view. Built on the CPU whenever the boxes
    /// change (a few hundred cells, at most 32 boxes).
    /// </summary>
    public static class FallCells
    {
        /// <summary>Cells per side; matches PUNCHY_FALL_GRID.</summary>
        public const int Grid = 16;
        /// <summary>How far round a box's foot its ripples reach (m); matches PUNCHY_FALL_RIPPLE_REACH.</summary>
        const float RippleReach = 12f;
        /// <summary>How far above its lip a box's water dips (m), as PunchyFallWater's last six metres.</summary>
        const float LipReach = 6f;

        static readonly int CellsId = Shader.PropertyToID("_PunchyRiverCurtainCells");
        static readonly int GridId = Shader.PropertyToID("_PunchyRiverCurtainGrid");
        static readonly Vector4[] cells = new Vector4[Grid * Grid / 2];
        static readonly uint[] sets = new uint[Grid * Grid];

        /// <summary>
        /// Fills the grid from the boxes as RiverCurtains.hlsl reads them (lip, pour, foot; count of them) and hands it
        /// to the shaders. Each box's reach is the widest of the three tests: up to LipReach (or its left-out face)
        /// behind the lip, the ripples past its foot and to either side, widened by how far the lip line bends.
        /// </summary>
        public static void Set(Vector4[] lip, Vector4[] pour, Vector4[] foot, int count)
        {
            System.Array.Clear(sets, 0, sets.Length);
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            for (int k = 0; k < count; k++)
            {
                Reach(lip[k], pour[k], foot[k], out var back, out var ahead, out var half);
                var o = new float2(pour[k].x, pour[k].y);
                var side = new float2(o.y, -o.x);
                var l = new float2(lip[k].x, lip[k].z);
                var extent = math.abs(o) * (back + ahead) * 0.5f + math.abs(side) * half;
                var mid = l + o * (ahead - back) * 0.5f;
                lo = math.min(lo, mid - extent);
                hi = math.max(hi, mid + extent);
            }
            var size = count > 0 ? math.max(hi - lo, 1f) : new float2(1f);
            if (count == 0)
                lo = float2.zero;
            var cell = size / Grid;
            var halfDiagonal = math.length(cell) * 0.5f;
            for (int k = 0; k < count; k++)
            {
                Reach(lip[k], pour[k], foot[k], out var back, out var ahead, out var half);
                var o = new float2(pour[k].x, pour[k].y);
                var side = new float2(o.y, -o.x);
                var l = new float2(lip[k].x, lip[k].z);
                for (int y = 0; y < Grid; y++)
                    for (int x = 0; x < Grid; x++)
                    {
                        // (The cell's middle in the box's frame, with the cell's half diagonal as slack.)
                        var d = lo + cell * new float2(x + 0.5f, y + 0.5f) - l;
                        var along = math.dot(d, o);
                        if (along > -back - halfDiagonal && along < ahead + halfDiagonal && math.abs(math.dot(d, side)) < half + halfDiagonal)
                            sets[y * Grid + x] |= 1u << k;
                    }
            }
            for (int i = 0; i < cells.Length; i++)
            {
                uint a = sets[2 * i], b = sets[2 * i + 1];
                cells[i] = new Vector4(a & 0xFFFF, a >> 16, b & 0xFFFF, b >> 16);
            }
            Shader.SetGlobalVectorArray(CellsId, cells);
            Shader.SetGlobalVector(GridId, new Vector4(lo.x, lo.y, Grid / size.x, Grid / size.y));
        }

        /// <summary>A box's reach behind its lip, ahead of it and to either side of its middle (m).</summary>
        static void Reach(Vector4 lip, Vector4 pour, Vector4 foot, out float back, out float ahead, out float half)
        {
            var bend = math.abs(foot.x) * 0.5f + math.abs(foot.y);
            back = math.max(LipReach, foot.z) + bend + 1f;
            ahead = pour.z + RippleReach + bend + 1f;
            half = lip.w + RippleReach;
        }
    }
}
