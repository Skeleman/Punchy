using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Clay view (launch flag -clay): the terrain and the scarp skins take one flat grey surface, so their shape
    /// can be judged under plain light without any material helping. Off by default; no shader or variant.
    /// </summary>
    public static class ScarpClay
    {
        static int state = -1;

        public static bool On
        {
            get
            {
                if (state < 0)
                    state = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-clay") >= 0 ? 1 : 0;
                return state == 1;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => state = -1;

        public static SurfaceProps Surface() => new SurfaceProps { Albedo = 0.5f, Smoothness = 0.1f };
    }

    /// <summary>
    /// Builds one scarp's skin mesh (W-155): a grid of columns along the rim and rows down the face, in world space,
    /// in the terrain's vertex layout so the terrain's own material draws it. Each column's first face row sits on
    /// the rim and its last on the foot, at the terrain's own height and normal there, so skin and ground meet
    /// without a seam; a tuck row beyond each runs under the ground. Between them the rows follow
    /// <see cref="ScarpSet.Face"/>, the same function the ground was cut with, which is where the recesses and
    /// overhangs a height field can't hold come from.
    /// </summary>
    [BurstCompile]
    struct ScarpSkinJob : IJobParallelFor
    {
        /// <summary>Columns per rim node spacing (about half a metre apart), and the height of a face row (m).</summary>
        public const int ColumnsPerNode = 4;
        public const float RowHeight = 0.5f;
        /// <summary>How far the tuck rows run past the seams, and how far under the ground.</summary>
        const float TuckOut = 0.7f, TuckDown = 0.35f;

        [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Meshes;
        public TerrainSource Source;
        public float NormalEpsilon;
        public bool Clay;

        public static int Columns(ref Scarp sc) => (sc.Nodes.Length - 1) * ColumnsPerNode + 1;
        /// <summary>Rows: a top tuck, the face rows from rim to foot, a bottom tuck.</summary>
        public static int Rows(ref Scarp sc) => (int)math.clamp(math.ceil(sc.MaxDrop / RowHeight), 4f, 200f) + 3;
        public static int VertexCount(ref Scarp sc) => Columns(ref sc) * Rows(ref sc);
        public static int IndexCount(ref Scarp sc) => (Columns(ref sc) - 1) * (Rows(ref sc) - 1) * 6;

        public void Execute(int index)
        {
            ref var sc = ref Source.Scarps.Value.Scarps[index];
            var mesh = Meshes[index];
            var verts = mesh.GetVertexData<TerrainVertex>();
            var indices = mesh.GetIndexData<uint>();
            int cols = Columns(ref sc), rows = Rows(ref sc), face = rows - 2;
            var wide = new NativeArray<bool>(cols, Allocator.Temp);
            var columns = new NativeArray<ScarpAt>(cols, Allocator.Temp);
            var columnCracks = new NativeArray<float3>(cols, Allocator.Temp);
            var rimTones = new NativeArray<float3>(cols, Allocator.Temp);
            var footTones = new NativeArray<SurfaceProps>(cols, Allocator.Temp);

            for (int c = 0; c < cols; c++)
            {
                int seg = math.min(c / ColumnsPerNode, sc.Nodes.Length - 2);
                float t = (c - seg * ColumnsPerNode) / (float)ColumnsPerNode;
                ScarpSet.Column(ref sc, seg, t, out var at, out var rim, out var dir);
                // The skin starts where the nearest terrain mesh leaves the strip to it.
                wide[c] = at.W >= ScarpSet.ClipSteps * NormalEpsilon;
                var cracks = ScarpSet.Cracks(ref sc, at.S);
                columns[c] = at;
                columnCracks[c] = cracks;
                // The ground's own colour a little behind the rim and a little past the foot (as flat ground), which
                // the face's beds are tinted from so the face sits in the colours of the land round it (row 2).
                rimTones[c] = Tone(rim - dir * ToneOut, new float3(0f, 1f, 0f));
                footTones[c] = Ground(rim + dir * (at.W + ToneOut), new float3(0f, 1f, 0f));
                float drop = at.RimY - at.FootY;
                for (int k = 0; k < face; k++)
                {
                    float y = at.RimY - drop * k / (face - 1);
                    float reach = k == 0 ? 0f : k == face - 1 ? at.W : ScarpSet.Face(ref sc, at, cracks, y);
                    var xz = rim + dir * reach;
                    var seam = k == 0 || k == face - 1;
                    // The seam rows are the terrain's own: its height and its normal at that point.
                    if (seam)
                        y = Source.SampleHeight(xz);
                    var vertex = new TerrainVertex { Position = new float3(xz.x, y, xz.y) };
                    if (seam)
                        vertex.Normal = Source.SampleNormal(xz, NormalEpsilon);
                    verts[(k + 1) * cols + c] = vertex;
                }
                // Tuck rows: past each seam and just under the ground, so no gap can open along the edge.
                var top = verts[cols + c];
                top.Position += new float3(-dir.x * TuckOut, -TuckDown, -dir.y * TuckOut);
                verts[c] = top;
                var bottom = verts[face * cols + c];
                bottom.Position += new float3(dir.x * TuckOut, -sc.Apron * TuckOut - TuckDown, dir.y * TuckOut);
                verts[(rows - 1) * cols + c] = bottom;
            }

            // Which way the rows and columns wind, from which side of the rim is down.
            var along = sc.Nodes[1].P - sc.Nodes[0].P;
            bool flip = math.dot(sc.Nodes[0].N, new float2(along.y, -along.x)) > 0f;

            // Normals of the face rows from the grid itself (so ledges, recesses and overhangs light as what they
            // are), then the surface each vertex carries.
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                var vertex = verts[i];
                bool seam = r <= 1 || r >= rows - 2;
                var at = columns[c];
                float y = vertex.Position.y, ground01 = 1f;
                if (!seam)
                {
                    var down = verts[(r + 1) * cols + c].Position - verts[(r - 1) * cols + c].Position;
                    var side = verts[r * cols + math.min(c + 1, cols - 1)].Position - verts[r * cols + math.max(c - 1, 0)].Position;
                    var n = math.normalizesafe(math.cross(down, side), new float3(0f, 1f, 0f));
                    n = flip ? -n : n;
                    // Row 2b: near each seam and towards the strip ends the face is still partly ground, over a wide
                    // ragged band: its normal turns from the face's own to the seam's (the ground's there) and,
                    // towards an end, to the ground's own under it, so light and surface hand over smoothly instead
                    // of switching from one row to the next.
                    float seamW = ScarpPaint.Seam(ref sc, at, y, out bool top);
                    float endW = ScarpPaint.End(ref sc, at, ScarpSet.ClipSteps * NormalEpsilon);
                    var seamN = verts[(top ? 1 : face) * cols + c].Normal;
                    n = math.normalizesafe(math.lerp(n, seamN, seamW * seamW * (3f - 2f * seamW)), n);
                    n = math.normalizesafe(math.lerp(n, Source.SampleNormal(vertex.Position.xz, NormalEpsilon), endW), n);
                    vertex.Normal = n;
                    ground01 = math.max(seamW, endW);
                }
                var xz = vertex.Position.xz;
                var ground = Source.SampleGround(xz);
                ground.Height = y;
                var surface = Clay ? ScarpClay.Surface() : Source.SampleSurface(xz, ground, vertex.Normal);
                if (!Clay && !seam)
                {
                    // The face's own colour, fading out over the same band towards each seam and each strip end,
                    // so the skin meets the ground in the ground's colour.
                    float fade = 1f - ground01;
                    surface = ScarpPaint.Paint(ref sc, at, columnCracks[c], y, vertex.Normal, fade, rimTones[c], footTones[c], surface);
                }
                vertex.SetSurface(surface);
                // No geomorph (the skin is one mesh at every distance) and no cave cut.
                vertex.Morph = new float4(1e9f, 2e9f, surface.Bump, 100f);
                verts[i] = vertex;
            }

            int w = 0;
            for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                uint a = (uint)(r * cols + c), b = a + 1, d = a + (uint)cols, e = d + 1;
                // Where the strip is too thin for the terrain to leave it (the last metres at each end) the stepped
                // ground is the face, and there is none to draw.
                if (!wide[c] || !wide[c + 1])
                    b = d = e = a;
                if (flip)
                {
                    indices[w++] = a; indices[w++] = b; indices[w++] = d;
                    indices[w++] = b; indices[w++] = e; indices[w++] = d;
                }
                else
                {
                    indices[w++] = a; indices[w++] = d; indices[w++] = b;
                    indices[w++] = b; indices[w++] = d; indices[w++] = e;
                }
            }
            wide.Dispose();
            columns.Dispose();
            columnCracks.Dispose();
            rimTones.Dispose();
            footTones.Dispose();
        }

        /// <summary>How far behind the rim and past the foot the neighbouring ground's colour is read (m).</summary>
        const float ToneOut = 2.5f;

        float3 Tone(float2 xz, float3 normal) => Ground(xz, normal).Albedo;

        SurfaceProps Ground(float2 xz, float3 normal)
        {
            var ground = Source.SampleGround(xz);
            return Source.SampleSurface(xz, ground, normal);
        }
    }
}
