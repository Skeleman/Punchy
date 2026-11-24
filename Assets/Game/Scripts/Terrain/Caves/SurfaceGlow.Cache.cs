using System.Collections.Generic;
using Punchy.Flora;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The glow scans' cell cache. Each scan (near and far) keeps the glowing plants it found, so the next one places only
    /// the cells newly inside its disc: the camera moves 4 m between near scans and 24 m between far ones, so nearly all of
    /// each disc was placed last time (the near scan's ground layer alone is ~10,000 placements, and both scans kept a
    /// worker busy most of a walk). Placement is seed-derived, so a kept cell holds the plant a full scan would place, as
    /// long as what placement reads hasn't changed: the world (TerrainSource, whose Edits carry digging: any change starts
    /// the cache afresh) and the colossi's keep-out circles (cells near a circle that moved are placed again). The kept
    /// list is sorted back into a full scan's order (layer, row, column), so the lights, the spill and the far maps' sums
    /// come out exactly as before. "-oldglow" turns the cache off; "-glowcheck" runs the full scan beside it and logs any
    /// difference.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>A/B: every scan places its whole disc, as before (-oldglow).</summary>
        public static bool CacheOff;
        /// <summary>Testing: each scan also places its whole disc and counts cells that differ from the cache (-glowcheck).</summary>
        public static bool CacheCheck;

        public struct GlowCell
        {
            public int2 Cell;
            public int Layer;
            public FloraSystem.FloraInstance Plant;
        }

        /// <summary>A full scan's order: layer, then row, then column.</summary>
        struct CellOrder : IComparer<GlowCell>
        {
            public int Compare(GlowCell a, GlowCell b) =>
                a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Cell.y != b.Cell.y ? a.Cell.y.CompareTo(b.Cell.y) : a.Cell.x.CompareTo(b.Cell.x);
        }

        /// <summary>One scan's cache, kept on the main thread: the disc its cells were found in and what placement read.</summary>
        class CellCache
        {
            public NativeList<GlowCell> Cells;
            /// <summary>x cells that differ from a full scan, y cells placed, z cells kept in all, w full scans' cells (-glowcheck).</summary>
            public NativeReference<int4> Report;
            public float2 Centre;
            public float3 Disc;
            public bool Valid;
            public TerrainSource Source;
            public FixedList512Bytes<float4> KeepOut;
            public int Scans;

            public void Create()
            {
                Cells = new NativeList<GlowCell>(256, Allocator.Persistent);
                Report = new NativeReference<int4>(Allocator.Persistent);
                Valid = false;
            }

            public void Dispose()
            {
                if (Cells.IsCreated)
                    Cells.Dispose();
                if (Report.IsCreated)
                    Report.Dispose();
                Valid = false;
            }

            /// <summary>
            /// For a scan about to start over this disc: whether the cells kept can be used, and the keep-out circles that
            /// changed since (each slot's old and new circle). Then records this scan as the one the cells will be from.
            /// </summary>
            public bool Begin(in TerrainSource source, float2 centre, float3 disc, out float2 oldCentre, out float3 oldDisc,
                              out FixedList512Bytes<float4> moved)
            {
                moved = default;
                var keepOut = FloraPlacement.ColossusKeepOut.Data;
                var valid = Valid && !CacheOff && Cells.IsCreated && source.Equals(Source);
                if (valid)
                    for (int i = 0; i < math.max(keepOut.Length, KeepOut.Length); i++)
                    {
                        var was = i < KeepOut.Length ? KeepOut[i] : default;
                        var now = i < keepOut.Length ? keepOut[i] : default;
                        if (was.Equals(now))
                            continue;
                        if (moved.Length + 2 > moved.Capacity)
                        {
                            valid = false;
                            break;
                        }
                        moved.Add(was);
                        moved.Add(now);
                    }
                oldCentre = Centre;
                oldDisc = Disc;
                Centre = centre;
                Disc = disc;
                Source = source;
                KeepOut = keepOut;
                Valid = Cells.IsCreated;
                Scans++;
                return valid;
            }

            /// <summary>Logs the last scan's check (-glowcheck): any difference, and every 20th scan's counts.</summary>
            public void Note(string name)
            {
                if (!CacheCheck || !Report.IsCreated)
                    return;
                var r = Report.Value;
                if (r.x != 0 || Scans % 20 == 1)
                    UnityEngine.Debug.Log($"SurfaceGlow: glowcheck {name} scan {Scans}: {r.x} cells differ, placed {r.y}, kept {r.z}, full scan {r.w}");
            }
        }

        readonly CellCache nearCache = new CellCache(), farCache = new CellCache();

        /// <summary>
        /// Brings a scan's cells up to its new disc (each layer's cells within disc[layer] of the centre, every stride[layer]
        /// on the island-wide grid): keeps the old cells still inside it and places the rest. Burst-callable, from the
        /// scan jobs.
        /// </summary>
        static void Refresh(ref FloraBlob f, in TerrainSource source, NativeArray<float4> modelGlow, NativeList<GlowCell> cells,
                            bool valid, float2 oldCentre, float3 oldDisc, float2 centre, float3 disc, float3 cellSizes, int3 stride,
                            in FixedList512Bytes<float4> moved, bool check, NativeReference<int4> report)
        {
            var kept = new NativeList<GlowCell>(cells.Length + 64, Allocator.Temp);
            if (valid)
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = cells[i];
                    var size = cellSizes[c.Layer];
                    var at = ((float2)c.Cell + 0.5f) * size;
                    var r = disc[c.Layer];
                    if (math.distancesq(at, centre) > r * r || NearMoved(moved, at, size))
                        continue;
                    kept.Add(c);
                }
            var placed = Gather(ref f, source, modelGlow, kept, valid, oldCentre, oldDisc, centre, disc, cellSizes, stride, moved);
            kept.Sort(new CellOrder());
            cells.Clear();
            cells.AddRange(kept.AsArray());
            if (!check)
                return;
            var full = new NativeList<GlowCell>(cells.Length + 64, Allocator.Temp);
            Gather(ref f, source, modelGlow, full, false, oldCentre, oldDisc, centre, disc, cellSizes, stride, moved);
            var differ = math.abs(full.Length - cells.Length);
            for (int i = 0; i < math.min(full.Length, cells.Length); i++)
            {
                GlowCell a = full[i], b = cells[i];
                if (!a.Cell.Equals(b.Cell) || a.Layer != b.Layer || a.Plant.Model != b.Plant.Model || !a.Plant.Position.Equals(b.Plant.Position) ||
                    a.Plant.Scale != b.Plant.Scale || a.Plant.Yaw != b.Plant.Yaw)
                    differ++;
            }
            report.Value = new int4(differ, placed, cells.Length, full.Length);
        }

        /// <summary>Places the disc's glowing cells not placed last time into the list (every cell when not valid); returns how many it placed.</summary>
        static int Gather(ref FloraBlob f, in TerrainSource source, NativeArray<float4> modelGlow, NativeList<GlowCell> into,
                          bool valid, float2 oldCentre, float3 oldDisc, float2 centre, float3 disc, float3 cellSizes, int3 stride,
                          in FixedList512Bytes<float4> moved)
        {
            var placed = 0;
            for (int layer = 0; layer < 3; layer++)
            {
                var size = cellSizes[layer];
                var r = disc[layer];
                var or = oldDisc[layer];
                var step = stride[layer];
                // Steps on the whole-island grid of cells (not from the corner), so scans agree where they overlap.
                var lo = (int2)math.floor((centre - r) / size);
                lo -= ((lo % step) + step) % step;
                var hi = (int2)math.floor((centre + r) / size);
                for (int y = lo.y; y <= hi.y; y += step)
                for (int x = lo.x; x <= hi.x; x += step)
                {
                    var cell = new int2(x, y);
                    var at = ((float2)cell + 0.5f) * size;
                    if (math.distancesq(at, centre) > r * r)
                        continue;
                    // Inside last scan's disc, and no keep-out circle moved near it: already in the list (or empty).
                    if (valid && !(math.distancesq(at, oldCentre) > or * or) && !NearMoved(moved, at, size))
                        continue;
                    placed++;
                    var plant = FloraPlacement.Place(ref f, source, cell, layer);
                    if (plant.Model < 0 || plant.Model >= modelGlow.Length || modelGlow[plant.Model].w == 0f)
                        continue;
                    into.Add(new GlowCell { Cell = cell, Layer = layer, Plant = plant });
                }
            }
            return placed;
        }

        /// <summary>Whether a cell (its centre, its size) could hold a spot inside one of the changed keep-out circles.</summary>
        static bool NearMoved(in FixedList512Bytes<float4> moved, float2 at, float size)
        {
            for (int i = 0; i < moved.Length; i++)
            {
                var c = moved[i];
                var reach = c.z + size;
                if (math.distancesq(at, c.xy) < reach * reach)
                    return true;
            }
            return false;
        }
    }
}
