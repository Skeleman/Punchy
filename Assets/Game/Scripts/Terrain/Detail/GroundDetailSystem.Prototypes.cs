using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// R-31 row 2 (#325): four grass prototypes to compare against today's tufts for a continuous field, picked with
    /// -grassproto a|b|c|d (none: today's grass). Zach picks one from the sheet; the pick then becomes the only grass
    /// and this switch goes away.
    ///   a: many more, cheaper blades: 9 vertices (a quad and a tip) at three times the count.
    ///   b: wider, taller, overlapping blades in wider tufts, roots the ground's colour and tips lighter.
    ///   c: today's blades over a short shell layer: four alpha-cut layers laid on the tile's ground samples fill
    ///      between the tufts.
    ///   d: cubic Bezier blades (GrassBezier.hlsl) in Voronoi clumps that share a facing, height band and colour; no
    ///      tufts, three times the count, 15 vertices near and 9 farther out by screen size.
    /// </summary>
    public partial class GroundDetailSystem
    {
        /// <summary>The prototype in use, 0 for none, 1-4 for a-d (-grassproto, or SetProto for the sheet).</summary>
        static int Proto = ReadProto();

        /// <summary>Switches prototype in play (debug, for the R-31 sheet): every tile is rebuilt with the new counts.</summary>
        public void SetProto(int proto)
        {
            Proto = math.clamp(proto, 0, 4);
            DropTiles();
        }

        // Play mode starts without a domain reload: read the switch afresh each run (it can come from Temp/PlayArgs.txt).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetProto() => Proto = ReadProto();

        static int ReadProto()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-grassproto" && args[i + 1].Length == 1 && args[i + 1][0] >= 'a' && args[i + 1][0] <= 'd')
                    return args[i + 1][0] - 'a' + 1;
            return 0;
        }

        /// <summary>Blades per instance count, as a multiple of today's.</summary>
        static float ProtoCount => Proto == 1 || Proto == 4 ? 3f : 1f;
        static float ProtoHeight => Proto == 2 ? 1.35f : 1f;
        static float ProtoWidth => Proto == 1 ? 0.8f : Proto == 2 ? 2.2f : Proto == 4 ? 1.6f : 1f;
        /// <summary>Tuft spread, as a multiple of today's (GroundDetail.hlsl ClumpMember).</summary>
        static float ProtoTuft => Proto == 2 ? 1.6f : 1f;
        /// <summary>Roots take the ground's colour, so a tuft's base doesn't read as a dark spot on lighter ground (b);
        /// half way for d, whose dark root to bright tip is part of its look but sits in a field dense enough to hide it.</summary>
        static float ProtoRootMatch => Proto == 2 ? 1f : Proto == 4 ? 0.5f : 0f;

        const int BladeVerticesNear = 15, BladeVerticesFar = 9;

        /// <summary>Vertices per blade for a tile whose nearest point is this far: the cheap blade for prototype a
        /// everywhere, and for d past where a blade is a few pixels wide (screen size, rule 20).</summary>
        int BladeVertices(float nearest)
        {
            if (Proto == 1)
                return BladeVerticesFar;
            if (Proto == 4 && nearest > BezierNearRange)
                return BladeVerticesFar;
            return BladeVerticesNear;
        }

        // Where the Bezier blade drops to its 9-vertex form: a blade about as wide on screen as two FullPixels' worth of grass.
        float BezierNearRange => Punchy.Settings.DetailPolicy.GroundReach(Punchy.Settings.DetailPolicy.GrassSize,
                                                                          Punchy.Settings.DetailPolicy.GrassFullPixels * 2f);

        static readonly int ProtoId = Shader.PropertyToID("_GrassProto");
        static readonly int ProtoShapeId = Shader.PropertyToID("_GrassProtoShape");
        static readonly int BladeVertsId = Shader.PropertyToID("_GrassBladeVerts");

        void SetProtoGlobals()
        {
            Shader.SetGlobalFloat(ProtoId, Proto);
            Shader.SetGlobalVector(ProtoShapeId, new Vector4(ProtoTuft, ProtoRootMatch, ShellHeight, ShellLayers));
        }

        // ------------------------------------------------------------------------------------------------ c: shells

        const int ShellLayers = 4;
        const float ShellHeight = 0.12f;
        static readonly int ShellPointsId = Shader.PropertyToID("_ShellPoints");
        static readonly int ShellGridId = Shader.PropertyToID("_ShellGrid");
        static readonly int ShellModeId = Shader.PropertyToID("_ShellMode");

        /// <summary>One ground sample for the shell layer, on the same 1 m lattice and triangle split as the terrain mesh.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ShellPoint
        {
            public float Height;
            public float Grass;
            public float3 Albedo;
            public float Pad;
        }

        /// <summary>Prototype c: the tile's ground samples as a shell buffer (none for tiles without grass).</summary>
        void BuildShells(Tile tile, NativeArray<GroundPoint> samples, int first, int grid, float2 origin)
        {
            if (Proto != 3 || tile.GrassCount == 0)
                return;
            var points = new NativeArray<ShellPoint>(grid * grid, Allocator.Temp);
            for (int s = 0; s < points.Length; s++)
            {
                var g = samples[first + s];
                var under = g.Water > g.Height + 0.02f || g.Bare > 0.5f;
                points[s] = new ShellPoint
                {
                    Height = g.Height,
                    Grass = under ? 0f : g.Grass * math.saturate(1f - g.Snow * 3f) * math.smoothstep(0.78f, 0.9f, g.Slope),
                    Albedo = g.Albedo
                };
            }
            tile.Shell = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Length, Marshal.SizeOf<ShellPoint>());
            tile.Shell.SetData(points);
            points.Dispose();
            tile.ShellProps = new MaterialPropertyBlock();
            tile.ShellProps.SetBuffer(ShellPointsId, tile.Shell);
            tile.ShellProps.SetVector(ShellGridId, new Vector4(origin.x, origin.y, grid, TileSize / (grid - 1)));
            tile.ShellProps.SetFloat(ShellModeId, 1f);
        }

        /// <summary>Prototype c: the shell layers of one tile, out to the full-density range only (they sink into the
        /// ground before it, GrassShellVertex), so the field past it is today's.</summary>
        void DrawShells(Tile tile, float nearest)
        {
            if (tile.Shell == null || nearest > FullDensityRange)
                return;
            var cells = (GridSamples - 1) * (GridSamples - 1);
            var rp = new RenderParams(GrassMaterial)
            {
                worldBounds = tile.Bounds,
                matProps = tile.ShellProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, cells * 6, ShellLayers);
        }

        // ------------------------------------------------------------------------------------------------ row 1 probe

        /// <summary>R-31 row 1: the blades in the tile under a point, as stored (before the shader's tuft fan-out):
        /// instances and blades per m², mean height, width, bend. Reads the tile's buffer back, so debug only.</summary>
        public string BladeStats(float2 xz)
        {
            var key = (int2)math.floor(xz / TileSize);
            if (!tiles.TryGetValue(key, out var tile) || tile.Grass == null)
                return "no grass tile there";
            var data = new Blade[tile.GrassCount];
            tile.Grass.GetData(data);
            double h = 0, w = 0, b = 0;
            foreach (var blade in data)
            {
                h += blade.Height;
                w += blade.Width;
                b += blade.Bend;
            }
            var n = math.max(data.Length, 1);
            var area = TileSize * TileSize;
            return $"[grass stats] proto {Proto}, tile {key}: {data.Length / area:F1} instances/m2, {data.Length * ClumpBlades / area:F1} blades/m2, " +
                   $"stored height {h / n:F3} m, half-width {w / n:F4} m, bend {b / n:F2}; tufts of {ClumpBlades} " +
                   $"(the shader scales members' height by about 0.95 and width by 1.0 on average)";
        }
    }
}
