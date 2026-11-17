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
    /// slot and no lip. W-174 r6 row 2: cave mouths are cut the same way (BuildChunkMeshJob.Mouths), their collars
    /// hanging to the cave walls' field (MouthCollar).
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
        /// <summary>A temple collar lower than this (m) at every point is left out.</summary>
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
        /// Rebuilds a chunk mesh whose grid an entrance (the temple's, or a cave mouth's) cuts (see the struct's summary):
        /// verts and the index buffer as Execute wrote them, worlds and cut each grid vertex's world xz and entrance cut
        /// (EntranceAt; 0 on the outline).
        /// </summary>
        void Collars(ref Mesh.MeshData mesh, NativeArray<TerrainVertex> verts, NativeArray<float2> worlds, NativeArray<float> cut,
                     in ChunkBuildRequest request, float step, float depth, ref float minY, ref float maxY)
        {
            SunkenTempleLayout l = default;
            var hasTemple = request.Lod <= TempleMaxLod && Source.Temple(request.Origin + ChunkSize * 0.5f, out l);
            var mouths = request.Lod <= MouthMaxLod && Source.Caves.IsCreated;
            if (!hasTemple && !mouths)
                return;
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
            var feet = new NativeHashMap<int, float4>(64, Allocator.Temp);   // a rim vertex's mouth-collar foot: world xyz, vertex w (-1: none)
            // Local functions in a struct can't capture ref or in parameters or this: work on copies.
            var origin = request.Origin;
            var source = Source;
            var normalEpsilon = NormalEpsilon;
            float low = minY, high = maxY;

            for (int z = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                int i0 = z * v + x, i1 = i0 + 1, i2 = i0 + v, i3 = i2 + 1;
                Triangle(i0, i2, i1);
                Triangle(i1, i2, i3);
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
                    var r = Crossing(a, b);
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
            feet.Dispose();
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
            void Triangle(int a, int b, int c)
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
                        poly.Add(Crossing(p, n));
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
                    var probe = mid + side * 0.02f;
                    if (hasTemple && SunkenTempleShape.Cut(ref source.Landmarks.Value, l, probe) < 0f)
                        Collar(poly[k], poly[n]);
                    // Every rim-to-rim edge is a chord of the cut; it is a mouth's when the mouth's cut, not the temple's,
                    // is the one near zero along it (a 2 cm probe can land outside a rim that bows between grid edges).
                    else if (mouths && (!hasTemple || math.abs(MouthCutKept(source, mid, step)) <
                                                      math.abs(SunkenTempleShape.Cut(ref source.Landmarks.Value, l, mid))))
                        MouthCollar(poly[k], poly[n]);
                }
            }

            // A collar under rim edge a-b: from the edge down to the wall top, read at a, b and each half-metre knot between.
            void Collar(int a, int b)
            {
                ref var blob = ref source.Landmarks.Value;
                var lay = l;
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
                    Face3(a, start + k, start + k + 1, new float3(into.x, 0f, into.y));
                for (int k = split; k < n - 1; k++)
                    Face3(b, start + k, start + k + 1, new float3(into.x, 0f, into.y));
                Face3(a, start + split, b, new float3(into.x, 0f, into.y));
            }

            // W-174 r6 row 2: a mouth collar under rim edge a-b, from the edge down to its foot on the cave walls' field
            // (MouthFoot), read under a, b and each MouthKnot between. The feet under a and b depend only on where those
            // rim points lie, so the chunk across a shared grid edge hangs its collar from the same foot.
            void MouthCollar(int a, int b)
            {
                int fa = MouthFootVertex(a, out var wa), fb = MouthFootVertex(b, out var wb);
                if (fa < 0 || fb < 0)
                    return;
                float2 pa = wl[a], pb = wl[b];
                // Built from the end with the lower world x (then z), so a chunk walking the shared edge the other way
                // rounds every knot and midpoint the same; turned back round before the faces go in.
                var flip = pb.x < pa.x || (pb.x == pa.x && pb.y < pa.y);
                if (flip)
                {
                    (a, b, fa, fb, wa, wb, pa, pb) = (b, a, fb, fa, wb, wa, pb, pa);
                }
                var knots = math.max(0, (int)math.ceil(math.distance(pa, pb) / MouthKnot) - 1);
                // The foot line in order from a: a's foot, the knots that found theirs, b's foot (with where along a-b).
                // Each foot's world point too (vertex positions are chunk-local, and the chunk across a shared edge
                // must refine its line from the very same numbers).
                var line = new FixedList512Bytes<int>();
                var along = new FixedList512Bytes<float>();
                var world = new FixedList4096Bytes<float3>();
                line.Add(fa);
                along.Add(0f);
                world.Add(wa);
                for (int k = 1; k <= knots; k++)
                {
                    var t = (float)k / (knots + 1);
                    if (MouthFoot(source, math.lerp(pa, pb, t), normalEpsilon, out var foot, out var normal))
                    {
                        line.Add(AddFoot(t < 0.5f ? a : b, foot, normal));
                        along.Add(t);
                        world.Add(foot);
                    }
                }
                line.Add(fb);
                along.Add(1f);
                world.Add(wb);
                // Where the wall curves between two feet, a straight edge would leave it: add a foot there (the edge's
                // middle moved onto the walls' field, else the foot under the rim's midpoint) until the edge's middle
                // lies on the field, or the feet are a few centimetres apart.
                // Only edges bowing into the cave's air need it (one bowing into the rock is hidden behind the wall), and
                // each pass splits every edge at most once, so a creased stretch cannot spend the whole line's feet.
                for (var more = true; more && line.Length < FootLineMax;)
                {
                    more = false;
                    for (int k = 0; k + 1 < line.Length && line.Length < FootLineMax; k++)
                    {
                        var foot = 0.5f * (world[k] + world[k + 1]);
                        var t = 0.5f * (along[k] + along[k + 1]);
                        var normal = float3.zero;
                        if (along[k + 1] - along[k] > FootSplitMin &&
                            WallFieldAt(source, foot, normalEpsilon) > FootBow &&
                            (OntoWall(source, ref foot, normalEpsilon, out normal) ||
                             MouthFoot(source, math.lerp(pa, pb, t), normalEpsilon, out foot, out normal)))
                        {
                            line.Insert(k + 1, AddFoot(t < 0.5f ? a : b, foot, normal));
                            along.Insert(k + 1, t);
                            world.Insert(k + 1, foot);
                            more = true;
                            k++;
                        }
                    }
                }
                var n = line.Length;
                if (flip)
                {
                    for (int i = 0, j = n - 1; i < j; i++, j--)
                        (line[i], line[j]) = (line[j], line[i]);
                    (a, b) = (b, a);
                }
                var split = (n - 1) / 2;
                for (int k = 0; k < n - 1; k++)
                    Face3(k < split ? a : b, line[k], line[k + 1], vl[line[k]].Normal + vl[line[k + 1]].Normal);
                Face3(a, line[split], b, vl[line[split]].Normal);
            }

            // The foot vertex under rim vertex r (made once per rim vertex), or -1 where the walls' field has no foot near it.
            int MouthFootVertex(int r, out float3 world)
            {
                if (!feet.TryGetValue(r, out var found))
                {
                    var index = MouthFoot(source, wl[r], normalEpsilon, out var foot, out var normal) ? AddFoot(r, foot, normal) : -1;
                    found = new float4(foot, index);
                    feet.Add(r, found);
                }
                world = found.xyz;
                return (int)found.w;
            }

            // A collar foot vertex at world point foot, with the wall's normal there and the rest from rim vertex r.
            int AddFoot(int r, float3 foot, float3 normal)
            {
                var inner = vl[r];
                inner.Position = new float3(foot.x - origin.x, foot.y, foot.z - origin.y);
                inner.Normal = normal;
                var strata = (float4)inner.Strata;
                strata.x = 1f;
                inner.Strata = new half4(strata);
                var emission = (float4)inner.Emission;
                emission.w = 0f;
                inner.Emission = new half4(emission);
                var extra = (float4)inner.Extra;
                extra.w = 0f;
                inner.Extra = new half4(extra);
                inner.Morph.w = CollarFoot;
                var index = vl.Length;
                vl.Add(inner);
                wl.Add(foot.xz);
                low = math.min(low, foot.y);
                return index;
            }

            // A collar triangle, wound to face along into (into the entrance, or out of the walls' rock into the air).
            void Face3(int a, int b, int c, float3 into)
            {
                float3 p0 = vl[a].Position, p1 = vl[b].Position, p2 = vl[c].Position;
                var n = math.cross(p1 - p0, p2 - p0);
                il.Add((uint)a);
                if (math.dot(n, into) >= 0f) { il.Add((uint)b); il.Add((uint)c); }
                else { il.Add((uint)c); il.Add((uint)b); }
            }

            // The entrance cut at p, as Execute reads it per vertex (EntranceCut).
            float EntranceAt(float2 p) =>
                math.min(hasTemple ? SunkenTempleShape.Cut(ref source.Landmarks.Value, l, p) : 100f,
                         mouths ? MouthCutKept(source, p, step) : 100f);

            // Whether the raw mouth cut has the kept cut's sign at p.
            bool RawAgrees(float2 p) => MouthCutAt(source, p, step) < 0f == MouthCutKept(source, p, step) < 0f;

            // The rim vertex on grid edge i-j (one inside, one outside): bisected from the edge's lower-z (then lower-x)
            // end, so the chunk across a shared edge finds the very same point.
            int Crossing(int i, int j)
            {
                var key = new int2(math.min(i, j), math.max(i, j));
                if (rims.TryGetValue(key, out var found))
                    return found;
                float2 wi = wl[i], wj = wl[j];
                var swap = wj.y < wi.y || wj.y == wi.y && wj.x < wi.x;
                int a = swap ? j : i, b = swap ? i : j;
                float2 pa = wl[a], pb = wl[b];
                var inA = cut[a] < 0f;
                // W-174 row 3: where the raw cut agrees with the kept one at both ends, its zero is bisected (the
                // kept cut reads up to 17 cuts a point); only edges beside a removed slot or speck bisect the kept cut.
                var cheap = !hasTemple && mouths && RawAgrees(pa) && RawAgrees(pb);
                float lo = 0f, hi = 1f;
                for (int k = 0; k < RimSteps; k++)
                {
                    var m = 0.5f * (lo + hi);
                    var pm = pa + (pb - pa) * m;
                    if ((cheap ? MouthCutAt(source, pm, step) : EntranceAt(pm)) < 0f == inA)
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
