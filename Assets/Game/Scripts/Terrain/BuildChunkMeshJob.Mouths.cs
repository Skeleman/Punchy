using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-174 r6 row 2 (#405): cave mouths in the chunk mesh, the way the temple's entrance is cut (Collars). The terrain
    /// is split on the mouth's rim (the zero line of the mesh's mouth cut, MouthCutAt), the triangles inside are dropped,
    /// and a collar hangs from the rim down to its foot: the line where the cave walls' own field (CaveSampling.WallField,
    /// with the surface and slope CaveSystem builds the walls from) crosses zero CollarDepth under the ground. So the
    /// collar ends on the wall, not on a guess at it, and ground, collar and wall meet with no slot.
    /// </summary>
    partial struct BuildChunkMeshJob
    {
        /// <summary>The coarsest LOD that cuts cave mouths (the walls are built only near; coarser ground covers them).</summary>
        const int MouthMaxLod = 1;
        /// <summary>How deep under the ground (m, across the slope, as the walls measure it) a mouth collar's foot lies.</summary>
        public const float CollarDepth = 0.4f;
        /// <summary>Spacing (m) of a mouth collar's foot knots along a rim edge, so its foot follows the wall's curve.</summary>
        const float MouthKnot = 0.25f;
        /// <summary>How far (m) from a rim point its foot is looked for, each way along the foot sheet's gradient.</summary>
        const float FootReach = 2f;
        /// <summary>March step (m) while looking for a foot, before the bisection.</summary>
        const float FootMarch = 0.125f;
        /// <summary>Bisection steps for a foot (well under a millimetre).</summary>
        const int FootSteps = 16;
        /// <summary>How far (m of the walls' field) the middle of an edge between two feet may lie off the walls before
        /// the foot under the rim between them is added.</summary>
        const float FootBow = 0.01f;
        /// <summary>The closest (as a fraction of the rim edge) two feet are split, and the most feet one rim edge gets.</summary>
        const float FootSplitMin = 1f / 64f;
        const int FootLineMax = 96;
        /// <summary>The cut value a collar's foot vertices hold (kept, so never clipped; the seam guard tells them by it).</summary>
        public const float CollarFoot = 0.002f;

        /// <summary>The mesh's mouth cut at xz for a grid of this step (the 5-tap MouthCut; 100 away from caves).</summary>
        public static float MouthCutAt(in TerrainSource source, float2 xz, float step)
        {
            if (!source.Caves.IsCreated || !CaveSampling.NearColumn(ref source.Caves.Value, xz))
                return 100f;
            return MouthCut(source, xz, source.SampleHeight(xz), step);
        }

        /// <summary>The cave walls' field at p, from the surface the near grid draws (gridStep) and the slope there, as
        /// CaveSystem's ColumnJob and FieldJob build the walls.</summary>
        public static float WallFieldAt(in TerrainSource source, float3 p, float gridStep) =>
            CaveSampling.WallField(p.y, source.MeshHeight(p.xz, gridStep), source.AcrossSlope(p.xz),
                                   CaveSampling.Distance(ref source.Caves.Value, p, out _));

        /// <summary>Shallower sheets a foot is looked for on, in turn, where the walls cross none within FootReach at
        /// CollarDepth (the cave's roof rises above that depth at the mouth's edge).</summary>
        const float FootDepthStep = 0.1f, FootDepthMin = 0.1f;

        /// <summary>The point depth under the walls' surface at xz (on the sheet a collar's foot lies on).</summary>
        static float3 FootSheet(in TerrainSource source, float2 xz, float gridStep, float depth) =>
            new float3(xz.x, source.MeshHeight(xz, gridStep) - depth / source.AcrossSlope(xz), xz.y);

        /// <summary>The walls' field on the foot sheet at xz (positive in the cave's air; WallFieldAt at FootSheet, with
        /// the surface and slope read once).</summary>
        static float FootField(in TerrainSource source, float2 xz, float gridStep, float depth)
        {
            var surface = source.MeshHeight(xz, gridStep);
            var scale = source.AcrossSlope(xz);
            var y = surface - depth / scale;
            return CaveSampling.WallField(y, surface, scale, CaveSampling.Distance(ref source.Caves.Value, new float3(xz.x, y, xz.y), out _));
        }

        /// <summary>
        /// The foot of a collar under rim point xz: the nearest point on the foot sheet (CollarDepth down, or shallower
        /// in FootDepthStep steps where that sheet has none) where the walls' field is zero, along the sheet field's
        /// gradient (towards the cave's air, or back from it). Only xz is read, so the chunks on both sides of a shared
        /// rim point find the same foot. False when none lies within FootReach on any sheet.
        /// </summary>
        public static bool MouthFoot(in TerrainSource source, float2 xz, float gridStep, out float3 foot, out float3 normal)
        {
            foot = default;
            normal = default;
            if (!source.Caves.IsCreated)
                return false;
            for (var depth = CollarDepth; depth >= FootDepthMin - 1e-4f; depth -= FootDepthStep)
                if (FootOnSheet(source, xz, gridStep, depth, out foot, out normal))
                    return true;
            // At the mouth's shallow edge the walls' skin runs just under the ground: drop straight down onto it.
            var top = new float3(xz.x, source.MeshHeight(xz, gridStep), xz.y);
            var bottom = top - new float3(0f, CollarDepth / source.AcrossSlope(xz), 0f);
            var air = WallFieldAt(source, top, gridStep) >= 0f;
            if (air == WallFieldAt(source, bottom, gridStep) >= 0f)
                return false;
            for (int k = 0; k < FootSteps; k++)
            {
                var m = 0.5f * (top + bottom);
                if (WallFieldAt(source, m, gridStep) >= 0f == air)
                    top = m;
                else
                    bottom = m;
            }
            foot = 0.5f * (top + bottom);
            normal = WallNormal(source, foot, gridStep, new float3(0f, 1f, 0f));
            return true;
        }

        /// <summary>How close (m of the walls' field) OntoWall must bring a point, and how far it may move it.</summary>
        const float WallSnap = 0.002f, WallSnapReach = 0.5f;

        /// <summary>
        /// Moves p onto the cave walls' field along its gradient (a few Newton steps), with the wall's normal there.
        /// False where the field steps or folds near p and no point within WallSnapReach is found (p is then unchanged).
        /// </summary>
        public static bool OntoWall(in TerrainSource source, ref float3 p, float gridStep, out float3 normal)
        {
            normal = default;
            const float n = 0.02f;
            var q = p;
            for (int k = 0; k < 6; k++)
            {
                var f = WallFieldAt(source, q, gridStep);
                if (math.distance(q, p) > WallSnapReach)
                    return false;
                var g = new float3(WallFieldAt(source, q + new float3(n, 0f, 0f), gridStep) - f,
                                   WallFieldAt(source, q + new float3(0f, n, 0f), gridStep) - f,
                                   WallFieldAt(source, q + new float3(0f, 0f, n), gridStep) - f) / n;
                if (math.lengthsq(g) < 1e-8f)
                    return false;
                if (math.abs(f) <= WallSnap)
                {
                    normal = math.normalize(g);
                    p = q;
                    return true;
                }
                q -= f * g / math.lengthsq(g);
            }
            // Newton wandered (the field steps or folds near p): march along the walls' normal at p, both ways, to the
            // nearest sign change and bisect it.
            var dir = WallNormal(source, p, gridStep, float3.zero);
            if (math.lengthsq(dir) == 0f)
                return false;
            var f0 = WallFieldAt(source, p, gridStep);
            for (var s = FootMarch; s <= WallSnapReach + 1e-4f; s += FootMarch)
                for (var side = -1f; side <= 1f; side += 2f)
                {
                    var far = p + dir * (side * s);
                    if (WallFieldAt(source, far, gridStep) >= 0f == f0 >= 0f)
                        continue;
                    var near = p + dir * (side * (s - FootMarch));
                    for (int k = 0; k < FootSteps; k++)
                    {
                        var m = 0.5f * (near + far);
                        if (WallFieldAt(source, m, gridStep) >= 0f == f0 >= 0f)
                            near = m;
                        else
                            far = m;
                    }
                    p = 0.5f * (near + far);
                    normal = WallNormal(source, p, gridStep, dir);
                    return true;
                }
            return false;
        }

        /// <summary>The walls' normal at p (the field rises into the air), or fallback where it is flat.</summary>
        static float3 WallNormal(in TerrainSource source, float3 p, float gridStep, float3 fallback)
        {
            const float n = 0.05f;
            var at = WallFieldAt(source, p, gridStep);
            var grad = new float3(WallFieldAt(source, p + new float3(n, 0f, 0f), gridStep) - at,
                                  WallFieldAt(source, p + new float3(0f, n, 0f), gridStep) - at,
                                  WallFieldAt(source, p + new float3(0f, 0f, n), gridStep) - at);
            return math.lengthsq(grad) > 1e-12f ? math.normalize(grad) : fallback;
        }

        /// <summary>MouthFoot on the one sheet depth down.</summary>
        static bool FootOnSheet(in TerrainSource source, float2 xz, float gridStep, float depth, out float3 foot, out float3 normal)
        {
            foot = default;
            normal = default;
            const float e = 0.1f;
            var f0 = FootField(source, xz, gridStep, depth);
            var g = new float2(FootField(source, xz + new float2(e, 0f), gridStep, depth) - FootField(source, xz - new float2(e, 0f), gridStep, depth),
                               FootField(source, xz + new float2(0f, e), gridStep, depth) - FootField(source, xz - new float2(0f, e), gridStep, depth));
            if (math.lengthsq(g) < 1e-12f)
                return false;
            // In rock, towards the air; in air, back towards the rock.
            var dir = math.normalize(g) * (f0 < 0f ? 1f : -1f);
            float lo = 0f, hi = -1f;
            for (var t = FootMarch; t <= FootReach + 1e-4f; t += FootMarch)
            {
                if (FootField(source, xz + dir * t, gridStep, depth) < 0f != f0 < 0f)
                {
                    hi = t;
                    break;
                }
                lo = t;
            }
            if (hi < 0f)
                return false;
            for (int k = 0; k < FootSteps; k++)
            {
                var m = 0.5f * (lo + hi);
                if (FootField(source, xz + dir * m, gridStep, depth) < 0f == f0 < 0f)
                    lo = m;
                else
                    hi = m;
            }
            foot = FootSheet(source, xz + dir * (0.5f * (lo + hi)), gridStep, depth);
            // The sheet's crossing is on the field to well under a millimetre unless the field steps there; then a few
            // steps along its gradient may still find the wall close by.
            if (math.abs(WallFieldAt(source, foot, gridStep)) > WallSnap)
            {
                var snapped = foot;
                if (OntoWall(source, ref snapped, gridStep, out normal) && math.distance(snapped, foot) < 0.1f)
                {
                    foot = snapped;
                    return true;
                }
            }
            normal = WallNormal(source, foot, gridStep, new float3(-dir.x, 0f, -dir.y) * (f0 < 0f ? -1f : 1f));
            return true;
        }
    }
}
