using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-174 r6 (#405): the sunken temple's entrance in the chunk mesh. The terrain is cut exactly on the entrance's
    /// outline (its open rooms, SunkenTempleShape.Cut) instead of clipped per pixel between grid points: the outline's
    /// corners take the nearest grid vertex (TempleCorners), every triangle the outline crosses is split on it (the rim
    /// crossings sit on the grid's edges, found the same way by both chunks that share an edge, so they meet exactly), the
    /// triangles inside are dropped, and a collar hangs from the rim down to the top of the entrance walls
    /// (SunkenTempleShape.WallTop, the same line SunkenTempleSystem builds the walls to), so ground and wall meet with no
    /// slot and no lip. Cave mouths keep the seam snap (Seam) until their own collar.
    /// </summary>
    partial struct BuildChunkMeshJob
    {
        /// <summary>The coarsest LOD that cuts the temple's entrance (coarser ones draw the ground over it; the temple
        /// draws its entrance to LandmarkLod 2, which covers them).</summary>
        const int TempleMaxLod = 2;
        /// <summary>How close to the outline (m) a vertex counts as on it.</summary>
        const float RimOn = 1e-4f;
        /// <summary>Bisection steps for a rim crossing (to well under a millimetre on a 4 m edge).</summary>
        const int RimSteps = 24;
        /// <summary>A collar lower than this (m) at every point is left out.</summary>
        const float CollarMin = 0.005f;

        /// <summary>The corners of the entrance's outline (world xz) within a quad of this chunk, for the vertex snap.</summary>
        void TempleCorners(float2 origin, float step, ref NativeList<float2> corners)
        {
            if (!Source.Temple(origin + ChunkSize * 0.5f, out var l))
                return;
            ref var set = ref Source.Landmarks.Value;
            float2 lo = origin - step, hi = origin + ChunkSize + step;
            for (int i = 0; i < set.RuinRooms.Length; i++)
            {
                var a = set.RuinRooms[i];
                if (a.Kind != 1)
                    continue;
                for (int j = i; j < set.RuinRooms.Length; j++)
                {
                    var b = set.RuinRooms[j];
                    if (b.Kind != 1)
                        continue;
                    // A room's own corners (j == i), or where one room's u faces cross another's v faces.
                    for (int k = 0; k < 8; k++)
                    {
                        if (j == i && k >= 4)
                            break;
                        var first = k < 4 ? a : b;
                        var second = k < 4 ? b : a;
                        var u = (k & 1) == 0 ? first.U0 : first.U1;
                        var v = (k & 2) == 0 ? second.V0 : second.V1;
                        var p = SunkenTempleShape.World(l, new float3(u, 0f, v)).xz;
                        if (math.any(p < lo) || math.any(p > hi) || !OutlineCorner(ref set, l, p))
                            continue;
                        var known = false;
                        for (int c = 0; c < corners.Length && !known; c++)
                            known = math.distancesq(corners[c], p) < 1e-6f;
                        if (!known)
                            corners.Add(p);
                    }
                }
            }
        }

        /// <summary>Whether p is a corner of the outline: on it, with one or three of four diagonal probes inside.</summary>
        static bool OutlineCorner(ref LandmarkSetBlob set, in SunkenTempleLayout l, float2 p)
        {
            if (math.abs(SunkenTempleShape.Cut(ref set, l, p)) > 1e-3f)
                return false;
            var inside = 0;
            for (int k = 0; k < 4; k++)
            {
                var d = (((k & 1) == 0 ? 1f : -1f) * l.F + ((k & 2) == 0 ? 1f : -1f) * l.R) * 0.01f;
                if (SunkenTempleShape.Cut(ref set, l, p + d) < 0f)
                    inside++;
            }
            return inside == 1 || inside == 3;
        }

        /// <summary>The corner a grid point takes (the nearest in its own cell, half-open so each corner has one point).</summary>
        static bool SnapCorner(NativeList<float2> corners, float2 grid, float step, out float2 corner)
        {
            corner = grid;
            var best = float.MaxValue;
            for (int c = 0; c < corners.Length; c++)
            {
                var off = (corners[c] - grid) / step;
                if (math.any(off < -0.5f) || math.any(off >= 0.5f))
                    continue;
                var d = math.lengthsq(off);
                if (d < best)
                {
                    best = d;
                    corner = corners[c];
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Rebuilds a chunk mesh whose grid the entrance cuts (see the struct's summary): verts and the index buffer as
        /// Execute wrote them, worlds and cut each grid vertex's world xz and entrance cut (0 on the outline).
        /// </summary>
        void Collars(ref Mesh.MeshData mesh, NativeArray<TerrainVertex> verts, NativeArray<float2> worlds, NativeArray<float> cut,
                     in ChunkBuildRequest request, float step, float depth, ref float minY, ref float maxY)
        {
            if (!Source.Temple(request.Origin + ChunkSize * 0.5f, out var l))
                return;
            ref var set = ref Source.Landmarks.Value;
            int q = request.Quads, v = q + 1, grid = v * v;
            var vl = new NativeList<TerrainVertex>(verts.Length + 256, Allocator.Temp);
            var wl = new NativeList<float2>(grid + 128, Allocator.Temp);   // world xz of grid and rim vertices
            for (int i = 0; i < verts.Length; i++)
                vl.Add(verts[i]);
            for (int i = 0; i < grid; i++)
                wl.Add(worlds[i]);
            for (int i = grid; i < verts.Length; i++)   // skirts, so rim indices line up with vl
                wl.Add(verts[i].Position.xz + request.Origin);
            var il = new NativeList<uint>(IndexCount(q) + 512, Allocator.Temp);
            var rims = new NativeHashMap<int2, int>(64, Allocator.Temp);
            var poly = new NativeList<int>(8, Allocator.Temp);
            var rim = new NativeList<bool>(8, Allocator.Temp);
            // Local functions in a struct can't capture ref or in parameters or this: work on copies.
            var origin = request.Origin;
            var source = Source;
            var normalEpsilon = NormalEpsilon;
            float low = minY, high = maxY;

            for (int z = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                int i0 = z * v + x, i1 = i0 + 1, i2 = i0 + v, i3 = i2 + 1;
                Triangle(i0, i2, i1, ref l, ref set);
                Triangle(i1, i2, i3, ref l, ref set);
            }

            // Skirts: none under the entrance; one reaching the rim where an edge crosses it.
            int skirtBase = grid;
            for (int edge = 0; edge < 4; edge++)
            for (int k = 0; k < q; k++)
            {
                int a = EdgeVertex(edge, k, q), b = EdgeVertex(edge, k + 1, q);
                int sa = skirtBase + edge * v + k, sb = sa + 1;
                if (cut[a] < 0f && cut[b] < 0f)
                    continue;
                if (cut[a] < 0f || cut[b] < 0f)
                {
                    var kept = cut[a] < 0f ? b : a;
                    if (cut[kept] == 0f)
                        continue;
                    var r = Crossing(a, b, ref l, ref set);
                    var hang = vl[r];
                    var drop = SkirtOverCave(wl[r], hang.Position.y, depth);
                    hang.Position.y -= drop;
                    hang.Extra.w = (half)drop;
                    var sr = vl.Length;
                    vl.Add(hang);
                    wl.Add(wl[r]);
                    low = math.min(low, hang.Position.y);
                    if (cut[a] < 0f) { a = r; sa = sr; } else { b = r; sb = sr; }
                }
                Skirt((uint)a, (uint)b, (uint)sa, (uint)sb);
            }

            mesh.SetVertexBufferParams(vl.Length, TerrainLayout);
            mesh.SetIndexBufferParams(il.Length, IndexFormat.UInt32);
            mesh.GetVertexData<TerrainVertex>().CopyFrom(vl.AsArray());
            mesh.GetIndexData<uint>().CopyFrom(il.AsArray());
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, il.Length), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            vl.Dispose();
            wl.Dispose();
            il.Dispose();
            rims.Dispose();
            poly.Dispose();
            rim.Dispose();
            minY = low;
            maxY = high;
            return;

            void Skirt(uint a, uint b, uint sa, uint sb)
            {
                il.Add(a); il.Add(b); il.Add(sa);
                il.Add(b); il.Add(sb); il.Add(sa);
                il.Add(a); il.Add(sa); il.Add(b);
                il.Add(b); il.Add(sa); il.Add(sb);
            }

            // The part of triangle (a, b, c) outside the entrance (clipped on the cut's sign), fanned, and a collar on
            // each of its edges that runs along the outline with the entrance beyond it.
            void Triangle(int a, int b, int c, ref SunkenTempleLayout lay, ref LandmarkSetBlob blob)
            {
                if (cut[a] > 0f && cut[b] > 0f && cut[c] > 0f)
                {
                    il.Add((uint)a); il.Add((uint)b); il.Add((uint)c);
                    return;
                }
                if (cut[a] <= 0f && cut[b] <= 0f && cut[c] <= 0f)
                    return;
                poly.Clear();
                rim.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int p = k == 0 ? a : k == 1 ? b : c, n = k == 0 ? b : k == 1 ? c : a;
                    if (cut[p] >= 0f)
                    {
                        poly.Add(p);
                        rim.Add(cut[p] == 0f);
                    }
                    if (cut[p] < 0f && cut[n] > 0f || cut[p] > 0f && cut[n] < 0f)
                    {
                        poly.Add(Crossing(p, n, ref lay, ref blob));
                        rim.Add(true);
                    }
                }
                if (poly.Length < 3)
                    return;
                for (int k = 1; k + 1 < poly.Length; k++)
                {
                    il.Add((uint)poly[0]); il.Add((uint)poly[k]); il.Add((uint)poly[k + 1]);
                }
                var centre = float2.zero;
                for (int k = 0; k < poly.Length; k++)
                    centre += wl[poly[k]];
                centre /= poly.Length;
                for (int k = 0; k < poly.Length; k++)
                {
                    var n = (k + 1) % poly.Length;
                    if (!rim[k] || !rim[n])
                        continue;
                    float2 pa = wl[poly[k]], pb = wl[poly[n]], d = pb - pa;
                    var len = math.length(d);
                    if (len < 1e-4f)
                        continue;
                    var mid = 0.5f * (pa + pb);
                    var side = new float2(-d.y, d.x) / len;
                    if (math.dot(side, mid - centre) < 0f)
                        side = -side;
                    if (SunkenTempleShape.Cut(ref blob, lay, mid + side * 0.02f) < 0f)
                        Collar(poly[k], poly[n], ref lay, ref blob);
                }
            }

            // A collar under rim edge a-b: from the edge down to the wall top, read at a, b and each half-metre knot between.
            void Collar(int a, int b, ref SunkenTempleLayout lay, ref LandmarkSetBlob blob)
            {
                float2 pa = wl[a], pb = wl[b];
                var mid = SunkenTempleShape.Local(lay, new float3(0.5f * (pa.x + pb.x), 0f, 0.5f * (pa.y + pb.y)));
                if (!SunkenTempleShape.Face(ref blob, mid.x, mid.z, out var e, out var c, out _))
                    return;
                var la = SunkenTempleShape.Local(lay, new float3(pa.x, 0f, pa.y));
                var lb = SunkenTempleShape.Local(lay, new float3(pb.x, 0f, pb.y));
                float sa = e < 2 ? la.z : la.x, sb = e < 2 ? lb.z : lb.x;
                float ya = vl[a].Position.y, yb = vl[b].Position.y;
                var into = e == 0 ? lay.F : e == 1 ? -lay.F : e == 2 ? lay.R : -lay.R;
                // Knots strictly between sa and sb, in order from a.
                var dir = sb >= sa ? 1f : -1f;
                var first = dir > 0f ? math.floor(sa * 2f) * 0.5f + 0.5f : math.ceil(sa * 2f) * 0.5f - 0.5f;
                var count = 0;
                for (var s = first; dir * (sb - s) > 1e-4f; s += 0.5f * dir)
                    count++;
                var n = count + 2;
                var start = vl.Length;
                var drop = 0f;
                for (int k = 0; k < n; k++)
                {
                    var s = k == 0 ? sa : k == n - 1 ? sb : first + (k - 1) * 0.5f * dir;
                    var t = math.abs(sb - sa) > 1e-6f ? math.saturate((s - sa) / (sb - sa)) : (float)k / (n - 1);
                    var xz = math.lerp(pa, pb, t);
                    var edgeY = math.lerp(ya, yb, t);
                    var y = math.min(edgeY, SunkenTempleShape.WallTop(source, lay, e, c, s));
                    drop = math.max(drop, edgeY - y);
                    var inner = vl[t < 0.5f ? a : b];
                    inner.Position = new float3(xz.x - origin.x, y, xz.y - origin.y);
                    inner.Normal = new float3(into.x, 0f, into.y);
                    var strata = (float4)inner.Strata;
                    strata.x = 1f;
                    inner.Strata = new half4(strata);
                    var emission = (float4)inner.Emission;
                    emission.w = 0f;
                    inner.Emission = new half4(emission);
                    var extra = (float4)inner.Extra;
                    extra.w = 0f;
                    inner.Extra = new half4(extra);
                    inner.Morph.w = SeamEpsilon;
                    vl.Add(inner);
                    wl.Add(xz);
                }
                if (drop < CollarMin)
                {
                    vl.Resize(start, NativeArrayOptions.UninitializedMemory);
                    wl.Resize(start, NativeArrayOptions.UninitializedMemory);
                    return;
                }
                for (int k = 0; k < n; k++)
                    low = math.min(low, vl[start + k].Position.y);
                var split = (n - 1) / 2;
                for (int k = 0; k < split; k++)
                    Face3(a, start + k, start + k + 1, into);
                for (int k = split; k < n - 1; k++)
                    Face3(b, start + k, start + k + 1, into);
                Face3(a, start + split, b, into);
            }

            // A collar triangle, wound to face into the entrance.
            void Face3(int a, int b, int c, float2 into)
            {
                float3 p0 = vl[a].Position, p1 = vl[b].Position, p2 = vl[c].Position;
                var n = math.cross(p1 - p0, p2 - p0);
                il.Add((uint)a);
                if (math.dot(n.xz, into) >= 0f) { il.Add((uint)b); il.Add((uint)c); }
                else { il.Add((uint)c); il.Add((uint)b); }
            }

            // The rim vertex on grid edge i-j (one inside, one outside): bisected from the edge's lower-z (then lower-x)
            // end, so the chunk across a shared edge finds the very same point.
            int Crossing(int i, int j, ref SunkenTempleLayout lay, ref LandmarkSetBlob blob)
            {
                var key = new int2(math.min(i, j), math.max(i, j));
                if (rims.TryGetValue(key, out var found))
                    return found;
                float2 wi = wl[i], wj = wl[j];
                var swap = wj.y < wi.y || wj.y == wi.y && wj.x < wi.x;
                int a = swap ? j : i, b = swap ? i : j;
                float2 pa = wl[a], pb = wl[b];
                var inA = cut[a] < 0f;
                float lo = 0f, hi = 1f;
                for (int k = 0; k < RimSteps; k++)
                {
                    var m = 0.5f * (lo + hi);
                    if (SunkenTempleShape.Cut(ref blob, lay, pa + (pb - pa) * m) < 0f == inA)
                        lo = m;
                    else
                        hi = m;
                }
                var t = 0.5f * (lo + hi);
                var p = pa + (pb - pa) * t;
                var va = vl[a];
                var vb = vl[b];
                var vertex = Mix(va, vb, t);
                var y = source.SampleHeight(p);
                vertex.Position = new float3(p.x - origin.x, y, p.y - origin.y);
                vertex.Normal = source.SampleNormal(p, normalEpsilon);
                var emission = (float4)vertex.Emission;
                emission.w = math.lerp(va.Position.y + va.Emission.w, vb.Position.y + vb.Emission.w, t) - y;
                vertex.Emission = new half4(emission);
                vertex.Morph.w = SeamEpsilon;
                var index = vl.Length;
                vl.Add(vertex);
                wl.Add(p);
                rims.Add(key, index);
                low = math.min(low, y);
                high = math.max(high, y);
                return index;
            }
        }

        /// <summary>A vertex between two others (every attribute straight between).</summary>
        static TerrainVertex Mix(in TerrainVertex a, in TerrainVertex b, float t) => new TerrainVertex
        {
            Position = math.lerp(a.Position, b.Position, t),
            Normal = math.normalizesafe(math.lerp(a.Normal, b.Normal, t)),
            Surface = new half4(math.lerp((float4)a.Surface, (float4)b.Surface, t)),
            Extra = new half4(math.lerp((float4)a.Extra, (float4)b.Extra, t)),
            Emission = new half4(math.lerp((float4)a.Emission, (float4)b.Emission, t)),
            Morph = math.lerp(a.Morph, b.Morph, t),
            Strata = new half4(math.lerp((float4)a.Strata, (float4)b.Strata, t)),
            Soak = new half2(math.lerp((float2)a.Soak, (float2)b.Soak, t))
        };
    }
}
