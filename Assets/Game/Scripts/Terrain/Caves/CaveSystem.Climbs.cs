using System.Collections.Generic;
using Punchy.Settings;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-144 row 1 (#267): draws the cave climb routes (CaveSetBlob.Climbs and their ClimbPoints, baked by
    /// CaveBaking.PlaceClimbs) so each reads as climbable at a glance: stone shelves and knobs, or chockstones beside a
    /// dark crack, in the cave's own rock (the cave material in the cave's style slot); roots, vines and ropes as thin
    /// tubes in copies of the cave material tinted brown, green and tan, so the lantern and glow lights light them as they
    /// light the walls. One mesh per route and material, built once per world; each route is drawn while it keeps a few
    /// pixels on screen (DetailPolicy screen size, rule 20). Seed-derived and cosmetic: nothing is sent.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Drawn down to this many reference pixels for a route's height (a whole band of holds; its pieces are
        /// a few pixels each by then).</summary>
        const float ClimbPixels = 24f;
        /// <summary>Root, vine and rope tones for the cave shader's rock and dark colours.</summary>
        static readonly Color RootRock = new Color(0.40f, 0.29f, 0.19f), RootDark = new Color(0.2f, 0.14f, 0.09f);
        static readonly Color VineRock = new Color(0.27f, 0.36f, 0.17f), VineDark = new Color(0.13f, 0.18f, 0.08f);
        static readonly Color RopeRock = new Color(0.66f, 0.56f, 0.4f), RopeDark = new Color(0.4f, 0.33f, 0.22f);

        Unity.Entities.BlobAssetReference<CaveSetBlob> climbsFrom;
        /// <summary>Per route: its rock mesh and its strand mesh (either may be null), the material for the strands, its
        /// middle and height (for its screen size), and whether it was drawn last frame (hysteresis).</summary>
        readonly List<(Mesh rock, Mesh strands, Material strandMaterial, float3 middle, float size)> climbMeshes =
            new List<(Mesh, Mesh, Material, float3, float)>();
        bool[] climbShown = System.Array.Empty<bool>();
        Material climbRoots, climbVines, climbRope;

        void DrawClimbs(in TerrainSource source, float3 camPos)
        {
            if (!source.Caves.IsCreated)
                return;
            if (climbsFrom != source.Caves)
            {
                ReleaseClimbs();
                climbsFrom = source.Caves;
                BuildClimbs(ref source.Caves.Value);
            }
            for (int i = 0; i < climbMeshes.Count; i++)
            {
                var (rock, strands, strandMaterial, middle, size) = climbMeshes[i];
                climbShown[i] = DetailPolicy.Within(math.distance(middle, camPos), DetailPolicy.ExtraReach(size, ClimbPixels), climbShown[i]);
                if (!climbShown[i])
                    continue;
                if (rock != null)
                    Graphics.RenderMesh(new RenderParams(Material) { worldBounds = rock.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, rock, 0, Matrix4x4.identity);
                if (strands != null)
                    Graphics.RenderMesh(new RenderParams(strandMaterial) { worldBounds = strands.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, strands, 0, Matrix4x4.identity);
            }
        }

        /// <summary>The meshes of every route, once per world.</summary>
        void BuildClimbs(ref CaveSetBlob set)
        {
            climbRoots = Tinted("Climb roots", RootRock, RootDark);
            climbVines = Tinted("Climb vines", VineRock, VineDark);
            climbRope = Tinted("Climb rope", RopeRock, RopeDark);
            for (int i = 0; i < set.Climbs.Length; i++)
            {
                ref var c = ref set.Climbs[i];
                var style = c.Cave >= 0 && c.Cave < set.Caves.Length ? set.Caves[c.Cave].Style : 0;
                var stone = new BatFolkMeshes.Builder { Slot = style };
                var strand = new BatFolkMeshes.Builder();
                var rng = new Unity.Mathematics.Random(math.hash(new uint2(c.Id, 0x5107u)) | 1u);
                var prev = default(ClimbPoint);
                for (int p = c.FirstPoint; p < c.FirstPoint + c.PointCount; p++)
                {
                    var pt = set.ClimbPoints[p];
                    switch (c.Kind)
                    {
                        case ClimbKind.Crag:
                            Shelf(stone, pt, ref rng);
                            break;
                        case ClimbKind.Crack:
                            if (pt.Join && prev.Join)
                                stone.Tube(prev.Position - prev.Normal * 0.01f, pt.Position - pt.Normal * 0.01f, pt.Size, pt.Size, pt.Tint, 5);
                            else if (!pt.Join)
                                Shelf(stone, pt, ref rng);
                            break;
                        case ClimbKind.Rope:
                            if (!pt.Join)
                                stone.Box(pt.Position + pt.Normal * 0.04f, new float3(0.03f, 0.03f, 0.09f),
                                          quaternion.LookRotationSafe(pt.Normal, new float3(0f, 1f, 0f)), 0.5f);   // the peg
                            else
                                strand.Tube(prev.Position, pt.Position, prev.Size, pt.Size, pt.Tint, 6);
                            break;
                        default:
                            if (pt.Join)
                                strand.Tube(prev.Position, pt.Position, prev.Size, pt.Size, pt.Tint, 6);
                            break;
                    }
                    prev = pt;
                }
                var material = c.Kind == ClimbKind.Vines ? climbVines : c.Kind == ClimbKind.Rope ? climbRope : climbRoots;
                var middle = (c.Base + c.Top) * 0.5f;
                climbMeshes.Add((stone.ToMesh("Climb holds"), strand.ToMesh("Climb strands"), material, middle,
                                 math.max(c.Height, c.HalfWidth * 2f)));
            }
            climbShown = new bool[climbMeshes.Count];
        }

        /// <summary>A copy of the cave material with its own rock tones.</summary>
        Material Tinted(string name, Color rock, Color dark)
        {
            var m = new Material(Material) { name = name };
            m.SetColor(RockColorId, rock);
            m.SetColor(DarkColorId, dark);
            return m;
        }

        /// <summary>A stone hold: a flat-topped shelf sticking out of the rock, wider than deep, turned a little.</summary>
        static void Shelf(BatFolkMeshes.Builder b, in ClimbPoint pt, ref Unity.Mathematics.Random rng)
        {
            var outward = math.normalizesafe(new float3(pt.Normal.x, 0f, pt.Normal.z), new float3(0f, 0f, 1f));
            var rotation = math.mul(quaternion.LookRotationSafe(outward, new float3(0f, 1f, 0f)),
                                    quaternion.Euler(rng.NextFloat(-0.25f, 0.1f), 0f, rng.NextFloat(-0.35f, 0.35f)));
            var half = new float3(pt.Size * rng.NextFloat(1f, 1.8f), pt.Size * rng.NextFloat(0.35f, 0.6f), pt.Size * rng.NextFloat(0.8f, 1.2f));
            // Half sunk in the rock so it grows out of the wall.
            b.Box(pt.Position + outward * half.z * 0.3f, half, rotation, pt.Tint);
        }

        void ReleaseClimbs()
        {
            foreach (var (rock, strands, _, _, _) in climbMeshes)
            {
                if (rock != null) Destroy(rock);
                if (strands != null) Destroy(strands);
            }
            climbMeshes.Clear();
            climbShown = System.Array.Empty<bool>();
            if (climbRoots != null) Destroy(climbRoots);
            if (climbVines != null) Destroy(climbVines);
            if (climbRope != null) Destroy(climbRope);
            climbRoots = climbVines = climbRope = null;
            climbsFrom = default;
        }
    }
}
