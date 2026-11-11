using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-9 row 5: the fine sheet on a river fall. The chunk water pours over a fall as a few upright metre quads, too
    /// coarse to move, so once a fall looks big enough (rule 20: <see cref="Punchy.Settings.DetailPolicy.WaterFallOn"/>,
    /// by the fall's own size and distance, not the player's square) its run-in, drop, plunge pool and outflow are drawn
    /// again here, each quad cut by how big it looks along x and along z apart, so the upright ones get many rows down the
    /// drop. The shader (WaterField.hlsl, PunchyWaterFallOffset) moves them along the flow, as the wave field can't move
    /// an upright sheet: the lip curls out, ripples run down the curtain, and the pool churns at the foot, all fading in
    /// with how big the fall looks so nothing pops. The chunk water and the player's sheet leave the fall's cells to it.
    /// One fall at a time (the one that looks biggest), cosmetic and per machine, from seed data every machine has.
    /// W-135 row 3: a cave's waterfall has no chunk water, so its sheet is made here (CaveQuads): a short run-in at the
    /// lip and the curtain down into its pool, in a few quads cut the same way. It leaves the chunk water uncut.
    /// </summary>
    public partial class WaterPatchSystem
    {
        /// <summary>The fall's rectangle (cells across, at most), the finest cut of a quad either way, and the most indices
        /// its sheet may have per unit of Detail.</summary>
        const int FallWidth = 64, FallMaxCut = 32, FallBudget = 32000;
        /// <summary>Cave waterfalls are chosen as this plus their index (river falls are their own index).</summary>
        const int CaveFallBase = 1 << 20;
        /// <summary>A cave waterfall's water colour and murk (as CaveSystem's pools).</summary>
        static readonly float4 CaveFallLook = new float4(0.1f, 0.28f, 0.3f, 0.1f);

        static readonly int FallRectId = Shader.PropertyToID("_PunchyWaterFallRect");
        static readonly int FallMaskId = Shader.PropertyToID("_PunchyWaterFallMask");
        static readonly int FallLipId = Shader.PropertyToID("_PunchyWaterFallLip");
        static readonly int FallAxisId = Shader.PropertyToID("_PunchyWaterFallAxis");
        static readonly int FallSpanId = Shader.PropertyToID("_PunchyWaterFallSpan");

        /// <summary>One fall's sheet: the rectangle round it, and what the shader is told of it (its lip and drop, which
        /// way it pours and its half width, and how far along its flow the sheet starts, the foot is, and it ends).</summary>
        struct FallShape
        {
            public int2 Corner, Extent;
            public float3 Middle;
            public float4 Lip, Axis, Span;
        }

        Mesh fallMesh;
        Texture2D fallMask;
        NativeList<ParentQuad> fallQuads;
        readonly byte[] fallMaskCut = new byte[FallWidth * FallWidth], fallMaskShown = new byte[FallWidth * FallWidth];
        /// <summary>Each cell's cut last time (log2 across | log2 along z &lt;&lt; 4), so a cell coarsens only well past the
        /// boundary.</summary>
        readonly Dictionary<int2, byte> fallLevels = new Dictionary<int2, byte>(), fallNextLevels = new Dictionary<int2, byte>();
        /// <summary>The fall chosen (held while it still looks big enough), the one on show, and the one being cut.</summary>
        int fallChosen = -1, fallShown = -1, fallCut = -1;
        FallShape fallShape, shownFallShape, cutFallShape;
        int shownFallBuilds, cutFallBuilds;
        float shownFallCamera, cutFallCamera, shownFallDetail, cutFallDetail;
        Bounds shownFallBounds, cutFallBounds;
        NativeArray<byte> fallGrid, fallRows;
        NativeArray<int2> fallStarts;
        Mesh.MeshDataArray fallData;
        JobHandle fallCutting;
        bool fallPending;
        int fallIndexCount;
        /// <summary>The fall's cells changed: the player's sheet recuts round them.</summary>
        bool fallMaskChanged;

        /// <summary>Indices in the fall's sheet on show, and how far it has faded in (for probes and the R-9 bench).</summary>
        public int FallIndices { get; private set; }
        public float FallFade { get; private set; }
        public int FallShown => fallShown;

        void CreateFall()
        {
            fallQuads = new NativeList<ParentQuad>(512, Allocator.Persistent);
            Shader.SetGlobalVector(FallRectId, NoRect);
            Shader.SetGlobalVector(FallAxisId, Vector4.zero);
        }

        void DestroyFall()
        {
            FinishFallCut(false);
            Shader.SetGlobalVector(FallRectId, NoRect);
            Shader.SetGlobalVector(FallAxisId, Vector4.zero);
            if (fallQuads.IsCreated) fallQuads.Dispose();
            Object.Destroy(fallMesh);
            Object.Destroy(fallMask);
        }

        /// <summary>Stops drawing the fall's sheet (the chunk water draws the fall again).</summary>
        void HideFall()
        {
            fallChosen = -1;
            if (fallShown < 0 && !fallPending)
                return;
            FinishFallCut(false);
            fallShown = -1;
            FallIndices = 0;
            FallFade = 0f;
            fallLevels.Clear();
            System.Array.Clear(fallMaskShown, 0, fallMaskShown.Length);
            fallMaskChanged = true;
            Shader.SetGlobalVector(FallRectId, NoRect);
            Shader.SetGlobalVector(FallAxisId, Vector4.zero);
        }

        /// <summary>Whether a cell is the fall sheet's (so the player's sheet leaves it whole).</summary>
        bool InFall(int2 cell)
        {
            if (fallShown < 0)
                return false;
            var c = cell - shownFallShape.Corner;
            return math.all(c >= 0) && math.all(c < FallWidth) && fallMaskShown[c.y * FallWidth + c.x] != 0;
        }

        /// <summary>Chooses the fall that looks biggest, recuts its sheet when the view or the chunks under it change,
        /// fades it in by how big it looks, and draws it.</summary>
        void UpdateFall(TerrainStreamingSystem streaming, TerrainConfig config, bool hasCamera, Punchy.Settings.DetailPolicy.View view)
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            var rivers = source.Rivers.IsCreated && source.RiverSettings.FallDrop > 0f;
            if (!hasCamera || !rivers && !source.Caves.IsCreated)
            {
                HideFall();
                return;
            }
            int best = -1;
            float bestPixels = 0f, heldPixels = 0f;
            if (rivers)
            {
                ref var falls = ref source.Rivers.Value.Falls;
                for (int k = 0; k < falls.Length; k++)
                {
                    var f = falls[k];
                    var middle = f.Lip + new float3(f.Out.x, -f.Drop * 0.5f, f.Out.y);
                    Consider(k, math.max(f.Drop, f.Width), middle, view, ref best, ref bestPixels, ref heldPixels);
                }
            }
            if (source.Caves.IsCreated)
            {
                ref var caveFalls = ref source.Caves.Value.Waterfalls;
                for (int k = 0; k < caveFalls.Length; k++)
                {
                    var f = caveFalls[k];
                    var middle = f.Lip + new float3(f.Out.x * CaveWaterfall.Reach * 0.5f, -f.Drop * 0.5f, f.Out.y * CaveWaterfall.Reach * 0.5f);
                    Consider(CaveFallBase + k, math.max(f.Drop, f.Width), middle, view, ref best, ref bestPixels, ref heldPixels);
                }
            }
            // (The fall already chosen stays until it looks well under the switch-on, or another looks much bigger.)
            if (fallChosen >= 0 && Punchy.Settings.DetailPolicy.Holds(heldPixels, Punchy.Settings.DetailPolicy.WaterFallOn, true) &&
                bestPixels < heldPixels * 1.25f)
            {
                best = fallChosen;
                bestPixels = heldPixels;
            }
            else if (!Punchy.Settings.DetailPolicy.Holds(bestPixels, Punchy.Settings.DetailPolicy.WaterFallOn, false))
                best = -1;
            if (best < 0)
            {
                HideFall();
                return;
            }
            var cave = best >= CaveFallBase;
            if (best != fallChosen || fallShape.Lip.w <= 0f)
                fallShape = cave ? CaveShape(source.Caves.Value.Waterfalls[best - CaveFallBase]) : Shape(ref source, source.Rivers.Value.Falls[best]);
            fallChosen = best;

            // (A cave fall's sheet is its own, not the chunk water's: nothing to wait for.)
            var builds = 23;
            if (cave)
                builds = 0;
            else if (!Gather(streaming, config, fallShape.Corner, fallShape.Extent, ref builds, false))
            {
                // (Not all fine and loaded under it yet: wait, or, if it was on show, let the chunk water have it back.)
                if (fallShown >= 0)
                    HideFall();
                fallChosen = best;
                return;
            }
            var camera = math.max(math.distance(view.Camera, fallShape.Middle), 1f);
            if (!fallPending && (best != fallShown || builds != shownFallBuilds || camera > shownFallCamera * CameraBand ||
                                 camera * CameraBand < shownFallCamera || view.Detail != shownFallDetail))
                StartFallCut(streaming, config, best, builds, camera, view);
            if (fallShown < 0)
                return;

            FallFade = fallShown == best
                ? Punchy.Settings.DetailPolicy.Crossfade(bestPixels, Punchy.Settings.DetailPolicy.WaterFallOn, Punchy.Settings.DetailPolicy.WaterFallFull)
                : 0f;
            var axis = shownFallShape.Axis;
            Shader.SetGlobalVector(FallAxisId, new Vector4(axis.x, axis.y, axis.z, FallFade));
            Graphics.RenderMesh(new RenderParams(material)
            {
                worldBounds = shownFallBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                layer = 0
            }, fallMesh, 0, Matrix4x4.identity);
        }

        /// <summary>Weighs one fall: how big it looks (its size seen from the camera at its middle), keeping the biggest and
        /// noting the one already chosen.</summary>
        void Consider(int fall, float size, float3 middle, Punchy.Settings.DetailPolicy.View view, ref int best, ref float bestPixels,
            ref float heldPixels)
        {
            var pixels = Punchy.Settings.DetailPolicy.ScreenSize(size, math.max(math.distance(view.Camera, middle), 1f), view.PixelsPerMetre);
            if (fall == fallChosen)
                heldPixels = pixels;
            if (pixels > bestPixels)
            {
                best = fall;
                bestPixels = pixels;
            }
        }

        /// <summary>A cave waterfall's sheet: its run-in from just inside the notch, and its curtain out and down to the pool
        /// (the foot where it lands). Its rectangle is centred on the lip, only so the shader's rectangle holds it: its cells
        /// are the sheet's own (CaveQuads), not the ground's.</summary>
        static FallShape CaveShape(CaveWaterfall f)
        {
            var o = math.normalizesafe(f.Out, new float2(0f, 1f));
            float reach = CaveWaterfall.Reach;
            return new FallShape
            {
                Corner = (int2)math.floor(f.Lip.xz) - FallWidth / 2,
                Extent = new int2(FallWidth, FallWidth),
                Middle = f.Lip + new float3(o.x * reach * 0.5f, -f.Drop * 0.5f, o.y * reach * 0.5f),
                Lip = new float4(f.Lip, f.Drop),
                Axis = new float4(o, f.Width * 0.5f + 2f, 0f),
                Span = new float4(-3.5f, reach, reach + 5f, 0f)
            };
        }

        /// <summary>A cave waterfall's quads: across its width in metre columns, and along it a short run-in at the lip
        /// (0.4 m) then the curtain in rows of about 2 m from the lip to where it lands, all standing in for chunk water
        /// cells of the shape's rectangle (columns along x, rows along z, so they wind as the ground's do).</summary>
        void CaveQuads(CaveWaterfall f, in FallShape shape)
        {
            var o = math.normalizesafe(f.Out, new float2(0f, 1f));
            var across = new float3(o.y, 0f, -o.x);
            var outward = new float3(o.x, 0f, o.y);
            var reach = CaveWaterfall.Reach;
            int columns = math.max(1, (int)math.ceil(f.Width)), drops = math.max(1, (int)math.ceil(f.Drop / 2f));
            var data = new float4(o, 0.3f, 0.5f);
            // (Row r's near edge: r 0 is the run-in's start, r 1 the lip, r 1 + drops the landing.)
            float3 RowAt(int r) => r == 0
                ? f.Lip - outward * 0.4f
                : f.Lip + outward * (reach * (r - 1) / drops) - new float3(0f, f.Drop * (r - 1) / drops, 0f);
            WaterVertex At(float3 row, int i) => new WaterVertex
            {
                Position = row + across * (f.Width * (i / (float)columns - 0.5f)),
                Data = data,
                Look = CaveFallLook
            };
            for (int r = 0; r < 1 + drops; r++)
            {
                float3 near = RowAt(r), far = RowAt(r + 1);
                for (int i = 0; i < columns; i++)
                    fallQuads.Add(new ParentQuad
                    {
                        Cell = shape.Corner + new int2(1 + i, 1 + r),
                        A = At(near, i), B = At(near, i + 1), C = At(far, i), D = At(far, i + 1)
                    });
            }
        }

        /// <summary>A fall's sheet: from 5 m above the lip to 10 m past the foot (where the water first lies at the pool's
        /// level), and its width and 2 m more either side, in a rectangle of cells round that.</summary>
        static FallShape Shape(ref TerrainSource source, RiverFall f)
        {
            var o = math.normalizesafe(f.Out, new float2(0f, 1f));
            var across = new float2(-o.y, o.x);
            var pool = f.Lip.y - f.Drop + 0.6f;
            var foot = 4f;
            for (float s = 0f; s < 12f; s += 0.5f)
                if (source.SampleGround(f.Lip.xz + o * s).WaterSurface < pool)
                {
                    foot = s;
                    break;
                }
            float start = -5f, end = foot + 10f, half = f.Width * 0.5f + 2f;
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var p = f.Lip.xz + o * ((i & 1) == 0 ? start : end) + across * ((i & 2) == 0 ? -half : half);
                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            // (Too wide a fall keeps the rectangle's middle; the sheet fades out at its edges.)
            var corner = (int2)math.floor(lo);
            var extent = math.min((int2)math.ceil(hi) - corner, FallWidth);
            var spare = (int2)math.ceil(hi) - corner - extent;
            corner += spare / 2;
            return new FallShape
            {
                Corner = corner,
                Extent = extent,
                Middle = f.Lip + new float3(o.x * foot * 0.5f, -f.Drop * 0.5f, o.y * foot * 0.5f),
                Lip = new float4(f.Lip, f.Drop),
                Axis = new float4(o, f.Width * 0.5f, 0f),
                Span = new float4(start, foot, end, 0f)
            };
        }

        /// <summary>Copies the chunk water's wet quads round the fall and starts cutting them (applied next frame): each by
        /// how big its x and z edges look from the camera, a piece to about WaterFallPiece pixels.</summary>
        void StartFallCut(TerrainStreamingSystem streaming, TerrainConfig config, int fall, int builds, float camera,
            Punchy.Settings.DetailPolicy.View view)
        {
            var shape = fallShape;
            var cave = fall >= CaveFallBase;
            var check = 23;
            if (!cave)
                Gather(streaming, config, shape.Corner, shape.Extent, ref check, true);
            fallQuads.Clear();
            if (cave)
            {
                var source = SystemAPI.GetSingleton<TerrainSource>();
                CaveQuads(source.Caves.Value.Waterfalls[fall - CaveFallBase], shape);
            }
            else if (gatherMeshes.Count > 0)
            {
                using var read = Mesh.AcquireReadOnlyMeshData(gatherMeshes);
                for (int m = 0; m < read.Length; m++)
                    new GatherJob
                    {
                        Vertices = read[m].GetVertexData<WaterVertex>(), Indices = read[m].GetIndexData<uint>(),
                        Row = config.Lod0Quads + 1, Origin = gatherOrigins[m], RectMin = shape.Corner,
                        RectMax = shape.Corner + shape.Extent, Quads = fallQuads
                    }.Run();
            }
            if (fall != fallShown)
                fallLevels.Clear();
            fallGrid = new NativeArray<byte>(FallWidth * FallWidth, Allocator.TempJob);
            fallRows = new NativeArray<byte>(FallWidth * FallWidth, Allocator.TempJob);
            System.Array.Clear(fallMaskCut, 0, fallMaskCut.Length);

            // Only the quads over the fall's own strip are the sheet's; the rest of the rectangle stays the chunk water's
            // (cut 1, so the sheet's edge meets it straight).
            var o = shape.Axis.xy;
            var across = new float2(-o.y, o.x);
            var kept = 0;
            for (int i = 0; i < fallQuads.Length; i++)
            {
                var q = fallQuads[i];
                var c = q.Cell - shape.Corner;
                fallGrid[c.y * FallWidth + c.x] = 1;
                fallRows[c.y * FallWidth + c.x] = 1;
                var rel = (float2)q.Cell + 0.5f - shape.Lip.xz;
                float s = math.dot(rel, o), side = math.abs(math.dot(rel, across));
                if (!cave && (s < shape.Span.x - 0.5f || s > shape.Span.z + 0.5f || side > shape.Axis.z + 0.5f))
                    continue;
                fallQuads[kept++] = q;
            }
            fallQuads.Length = kept;

            float lowY = float.MaxValue, highY = float.MinValue;
            var levels = new int2[kept];
            var budget = (int)(FallBudget * math.max(view.Detail, 0.1f));
            var piece = Punchy.Settings.DetailPolicy.WaterFallPiece / math.max(view.Detail, 0.1f);
            var shrink = 1f;
            for (int attempt = 0; attempt < 24; attempt++, shrink *= 0.85f)
            {
                var total = 0;
                for (int i = 0; i < kept; i++)
                {
                    var q = fallQuads[i];
                    var centre = (q.A.Position + q.B.Position + q.C.Position + q.D.Position) * 0.25f;
                    var distance = math.max(math.distance(view.Camera, centre), 0.5f);
                    var alongX = math.max(math.distance(q.A.Position, q.B.Position), math.distance(q.C.Position, q.D.Position));
                    var alongZ = math.max(math.distance(q.A.Position, q.C.Position), math.distance(q.B.Position, q.D.Position));
                    fallLevels.TryGetValue(q.Cell, out var was);
                    var had = fallLevels.ContainsKey(q.Cell);
                    var level = new int2(Level(alongX, distance, piece * (1f / shrink), view.PixelsPerMetre, had ? was & 15 : -1),
                        Level(alongZ, distance, piece * (1f / shrink), view.PixelsPerMetre, had ? was >> 4 : -1));
                    levels[i] = level;
                    total += (1 << level.x) * (1 << level.y) * 6;
                }
                // (A cave fall's quads are cut alike: their cells aren't the ground's, so a seam between two cuts couldn't
                // be closed the chunk water's way, along x or z.)
                if (cave && kept > 0)
                {
                    var most = int2.zero;
                    for (int i = 0; i < kept; i++)
                        most = math.max(most, levels[i]);
                    for (int i = 0; i < kept; i++)
                        levels[i] = most;
                    total = kept * (1 << most.x) * (1 << most.y) * 6;
                }
                if (total <= budget)
                    break;
            }
            fallStarts = new NativeArray<int2>(kept, Allocator.TempJob);
            fallNextLevels.Clear();
            int vertexCount = 0, indexCount = 0;
            for (int i = 0; i < kept; i++)
            {
                var q = fallQuads[i];
                var c = q.Cell - shape.Corner;
                int nx = 1 << levels[i].x, nz = 1 << levels[i].y;
                fallGrid[c.y * FallWidth + c.x] = (byte)nx;
                fallRows[c.y * FallWidth + c.x] = (byte)nz;
                if (!cave)
                    fallMaskCut[c.y * FallWidth + c.x] = 255;
                fallNextLevels[q.Cell] = (byte)(levels[i].x | levels[i].y << 4);
                fallStarts[i] = new int2(vertexCount, indexCount);
                vertexCount += (nx + 1) * (nz + 1);
                indexCount += nx * nz * 6;
                lowY = math.min(lowY, math.min(math.min(q.A.Position.y, q.B.Position.y), math.min(q.C.Position.y, q.D.Position.y)));
                highY = math.max(highY, math.max(math.max(q.A.Position.y, q.B.Position.y), math.max(q.C.Position.y, q.D.Position.y)));
            }
            fallLevels.Clear();
            foreach (var pair in fallNextLevels)
                fallLevels[pair.Key] = pair.Value;
            if (kept == 0)
                lowY = highY = shape.Lip.y;
            var pad = WaterSwells.BoundsPad + new float3(1f, 1f, 1f);
            var lo = new float3(shape.Corner.x, lowY, shape.Corner.y) - pad;
            var hi = new float3(shape.Corner.x + shape.Extent.x, highY, shape.Corner.y + shape.Extent.y) + pad;
            if (cave)
            {
                // (Round the sheet itself, not its rectangle.)
                var near = shape.Lip.xz - (float2)(WaterSwells.BoundsPad.xz + 3f);
                var far = shape.Lip.xz + (float2)(WaterSwells.BoundsPad.xz + 3f);
                lo = new float3(near.x, lo.y, near.y);
                hi = new float3(far.x, hi.y, far.y);
            }
            cutFallBounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            cutFallShape = shape;
            cutFallBuilds = builds;
            cutFallCamera = camera;
            cutFallDetail = view.Detail;
            fallCut = fall;
            fallIndexCount = indexCount;

            fallData = Mesh.AllocateWritableMeshData(1);
            var data = fallData[0];
            data.SetVertexBufferParams(vertexCount, Layout);
            data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            fallCutting = new CutJob
            {
                Quads = fallQuads.AsArray(), Grid = fallGrid, Rows = fallRows, Starts = fallStarts, RectMin = shape.Corner,
                Width = FallWidth, Vertices = data.GetVertexData<PatchVertex>(), Indices = data.GetIndexData<uint>()
            }.Schedule(kept, 8);
            fallPending = true;
        }

        /// <summary>How many times (log2) to halve an edge of the given length: until a piece looks about
        /// <paramref name="piece"/> pixels, at most <see cref="FallMaxCut"/>; a cell coarsens only once it is well past
        /// the boundary (rule 2).</summary>
        static int Level(float length, float distance, float piece, float pixelsPerMetre, int previous)
        {
            var raw = math.log2(math.max(Punchy.Settings.DetailPolicy.ScreenSize(length, distance, pixelsPerMetre) / piece, 1f));
            var level = math.clamp((int)math.ceil(raw - 0.05f), 0, math.ceillog2(FallMaxCut));
            if (previous > level && raw > previous - 1.2f)
                level = math.min(previous, math.ceillog2(FallMaxCut));
            return level;
        }

        /// <summary>Puts last frame's cut fall sheet on show (or, with <paramref name="apply"/> false, drops it).</summary>
        void FinishFallCut(bool apply)
        {
            if (!fallPending)
                return;
            fallCutting.Complete();
            fallPending = false;
            fallGrid.Dispose();
            fallRows.Dispose();
            fallStarts.Dispose();
            if (!apply)
            {
                fallData.Dispose();
                return;
            }
            if (fallMesh == null)
            {
                fallMesh = new Mesh { name = "Water fall sheet" };
                fallMesh.MarkDynamic();
                fallMask = new Texture2D(FallWidth, FallWidth, TextureFormat.R8, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Water fall sheet mask" };
            }
            var data = fallData[0];
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, fallIndexCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.ApplyAndDisposeWritableMeshData(fallData, fallMesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            fallMesh.bounds = cutFallBounds;
            shownFallBounds = cutFallBounds;
            shownFallBuilds = cutFallBuilds;
            shownFallCamera = cutFallCamera;
            shownFallDetail = cutFallDetail;
            FallIndices = fallIndexCount;
            if (fallShown != fallCut || !shownFallShape.Corner.Equals(cutFallShape.Corner) ||
                !System.Linq.Enumerable.SequenceEqual(fallMaskShown, fallMaskCut))
                fallMaskChanged = true;
            fallShown = fallCut;
            shownFallShape = cutFallShape;
            System.Array.Copy(fallMaskCut, fallMaskShown, fallMaskShown.Length);
            fallMask.SetPixelData(fallMaskShown, 0);
            fallMask.Apply(false, false);
            var corner = shownFallShape.Corner;
            Shader.SetGlobalTexture(FallMaskId, fallMask);
            Shader.SetGlobalVector(FallRectId, new Vector4(corner.x, corner.y, corner.x + FallWidth, corner.y + FallWidth));
            Shader.SetGlobalVector(FallLipId, shownFallShape.Lip);
            Shader.SetGlobalVector(FallSpanId, shownFallShape.Span);
        }
    }
}
