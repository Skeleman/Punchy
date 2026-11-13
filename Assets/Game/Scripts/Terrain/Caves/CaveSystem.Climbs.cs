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
    /// Row 1b: so a route reads as climbable from 15 m in cave light, strands are drawn thicker than baked, vines carry
    /// pale leaves, stone holds get worn chalky tops with scuffs above them, and a small pale cairn marks a route's foot.
    /// The chalk is its own pale mesh with its own, larger screen size, so it drops out before the route does. In a cave
    /// whose walls glow, strands and chalk take a share of that glow as light (the cave shader's _WallLight), so they
    /// read in front of the wall instead of as black lines.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Drawn down to this many reference pixels for a route's height (a whole band of holds; its pieces are
        /// a few pixels each by then).</summary>
        const float ClimbPixels = 24f;
        /// <summary>The chalk (worn tops, scuffs, cairn) is drawn down to this many reference pixels for the route.</summary>
        const float ChalkPixels = 60f;
        /// <summary>Strands are drawn this many times their baked thickness (vines, roots, rope), so they keep a pixel or
        /// two at 15 m. Drawing only: the baked points, which climbing reads, are unchanged.</summary>
        const float VineThicken = 2.2f, RootThicken = 1.3f, RopeThicken = 1.5f;
        /// <summary>How much of the cave style's glow lights the strands and chalk (a glowing wall lights what hangs in
        /// front of it; the rock itself shows its glow as its own colour). Veins count for at least WallLightVeins of an
        /// all-over glow, and the share is capped at 1.</summary>
        const float WallLightShare = 6f, WallLightVeins = 0.1f;
        /// <summary>A faint grey every route gets, so a strand never reads as a black line against lit rock.</summary>
        const float WallLightBase = 0.1f;
        static readonly int WallLightId = Shader.PropertyToID("_WallLight");
        /// <summary>Root, vine and rope tones for the cave shader's rock and dark colours, and the chalk's.</summary>
        static readonly Color RootRock = new Color(0.40f, 0.29f, 0.19f), RootDark = new Color(0.2f, 0.14f, 0.09f);
        static readonly Color VineRock = new Color(0.42f, 0.55f, 0.24f), VineDark = new Color(0.22f, 0.3f, 0.12f);
        static readonly Color RopeRock = new Color(0.66f, 0.56f, 0.4f), RopeDark = new Color(0.4f, 0.33f, 0.22f);
        static readonly Color ChalkRock = new Color(0.8f, 0.77f, 0.7f), ChalkDark = new Color(0.6f, 0.57f, 0.52f);

        Unity.Entities.BlobAssetReference<CaveSetBlob> climbsFrom;
        /// <summary>Per route: its rock, strand and chalk meshes (any may be null), the material for the strands, its
        /// middle and height (for its screen size); and whether the route and its chalk were drawn last frame
        /// (hysteresis).</summary>
        readonly List<(Mesh rock, Mesh strands, Mesh chalk, Material strandMaterial, float3 middle, float size)> climbMeshes =
            new List<(Mesh, Mesh, Mesh, Material, float3, float)>();
        /// <summary>Per route: its cave's style slot and its strands' and chalk's light from glowing walls.</summary>
        readonly List<(int slot, MaterialPropertyBlock light)> climbLight = new List<(int, MaterialPropertyBlock)>();
        bool[] climbShown = System.Array.Empty<bool>(), chalkShown = System.Array.Empty<bool>();
        Material climbRoots, climbVines, climbRope, climbChalk;

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
                var (rock, strands, chalk, strandMaterial, middle, size) = climbMeshes[i];
                var distance = math.distance(middle, camPos);
                climbShown[i] = DetailPolicy.Within(distance, DetailPolicy.ExtraReach(size, ClimbPixels), climbShown[i]);
                if (!climbShown[i])
                    continue;
                // The glow is read every draw, so a style edited in play mode shows at once (as the walls do).
                var (slot, light) = climbLight[i];
                light.SetVector(WallLightId, WallLight(slot));
                if (rock != null)
                    Graphics.RenderMesh(new RenderParams(Material) { worldBounds = rock.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, rock, 0, Matrix4x4.identity);
                if (strands != null)
                    Graphics.RenderMesh(new RenderParams(strandMaterial) { matProps = light, worldBounds = strands.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true }, strands, 0, Matrix4x4.identity);
                chalkShown[i] = DetailPolicy.Within(distance, DetailPolicy.ExtraReach(size, ChalkPixels), chalkShown[i]);
                if (chalk != null && chalkShown[i])
                    Graphics.RenderMesh(new RenderParams(climbChalk) { matProps = light, worldBounds = chalk.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true }, chalk, 0, Matrix4x4.identity);
            }
        }

        /// <summary>The meshes of every route, once per world.</summary>
        void BuildClimbs(ref CaveSetBlob set)
        {
            climbRoots = Tinted("Climb roots", RootRock, RootDark);
            climbVines = Tinted("Climb vines", VineRock, VineDark);
            climbRope = Tinted("Climb rope", RopeRock, RopeDark);
            climbChalk = Tinted("Climb chalk", ChalkRock, ChalkDark);
            for (int i = 0; i < set.Climbs.Length; i++)
            {
                ref var c = ref set.Climbs[i];
                var style = c.Cave >= 0 && c.Cave < set.Caves.Length ? set.Caves[c.Cave].Style : 0;
                var stone = new BatFolkMeshes.Builder { Slot = style };
                var strand = new BatFolkMeshes.Builder();
                var chalk = new BatFolkMeshes.Builder();
                var thicken = c.Kind == ClimbKind.Vines ? VineThicken : c.Kind == ClimbKind.Rope ? RopeThicken : RootThicken;
                var rng = new Unity.Mathematics.Random(math.hash(new uint2(c.Id, 0x5107u)) | 1u);
                // The row 1b details draw from their own stream, so the holds keep their row 1 shapes.
                var detail = new Unity.Mathematics.Random(math.hash(new uint2(c.Id, 0xC4A1u)) | 1u);
                var prev = default(ClimbPoint);
                for (int p = c.FirstPoint; p < c.FirstPoint + c.PointCount; p++)
                {
                    var pt = set.ClimbPoints[p];
                    switch (c.Kind)
                    {
                        case ClimbKind.Crag:
                            Shelf(stone, chalk, pt, ref rng, ref detail);
                            break;
                        case ClimbKind.Crack:
                            if (pt.Join && prev.Join)
                                stone.Tube(prev.Position - prev.Normal * 0.01f, pt.Position - pt.Normal * 0.01f, pt.Size, pt.Size, pt.Tint, 5);
                            else if (!pt.Join)
                                Shelf(stone, chalk, pt, ref rng, ref detail);
                            break;
                        case ClimbKind.Rope:
                            if (!pt.Join)
                                stone.Box(pt.Position + pt.Normal * 0.04f, new float3(0.03f, 0.03f, 0.09f),
                                          quaternion.LookRotationSafe(pt.Normal, new float3(0f, 1f, 0f)), 0.5f);   // the peg
                            else
                                strand.Tube(prev.Position, pt.Position, prev.Size * thicken, pt.Size * thicken, pt.Tint, 6);
                            break;
                        default:
                            if (pt.Join)
                            {
                                strand.Tube(prev.Position, pt.Position, prev.Size * thicken, pt.Size * thicken, pt.Tint, 6);
                                if (c.Kind == ClimbKind.Vines && detail.NextFloat() < 0.4f)
                                    Leaf(strand, pt, ref detail);
                            }
                            break;
                    }
                    prev = pt;
                }
                if (c.Kind != ClimbKind.Rope)
                    Cairn(chalk, c, ref detail);   // a rope's own hanging end marks its foot
                var material = c.Kind == ClimbKind.Vines ? climbVines : c.Kind == ClimbKind.Rope ? climbRope : climbRoots;
                var middle = (c.Base + c.Top) * 0.5f;
                climbMeshes.Add((stone.ToMesh("Climb holds"), strand.ToMesh("Climb strands"), chalk.ToMesh("Climb chalk"),
                                 material, middle, math.max(c.Height, c.HalfWidth * 2f)));
                climbLight.Add((style, new MaterialPropertyBlock()));
            }
            climbShown = new bool[climbMeshes.Count];
            chalkShown = new bool[climbMeshes.Count];
        }

        /// <summary>The light a cave style's all-over glow throws on what hangs in front of its walls (a faint grey for a
        /// dark cave, or slot 0).</summary>
        Vector4 WallLight(int slot)
        {
            var lift = new Vector4(WallLightBase, WallLightBase, WallLightBase, 0f);
            if (slot <= 0 || styles == null || slot > styles.Count || styles[slot - 1] == null)
                return lift;
            var s = styles[slot - 1];
            var share = math.saturate(s.Glow * math.max(s.GlowEverywhere, WallLightVeins) * WallLightShare);
            return lift + new Vector4(s.GlowColor.r * share, s.GlowColor.g * share, s.GlowColor.b * share, 0f);
        }

        /// <summary>A copy of the cave material with its own rock tones.</summary>
        Material Tinted(string name, Color rock, Color dark)
        {
            var m = new Material(Material) { name = name };
            m.SetColor(RockColorId, rock);
            m.SetColor(DarkColorId, dark);
            return m;
        }

        /// <summary>A stone hold: a flat-topped shelf sticking out of the rock, wider than deep, turned a little; its top
        /// worn pale where hands land and, on most, a pale scuff on the rock above it.</summary>
        static void Shelf(BatFolkMeshes.Builder b, BatFolkMeshes.Builder chalk, in ClimbPoint pt,
                          ref Unity.Mathematics.Random rng, ref Unity.Mathematics.Random detail)
        {
            var outward = math.normalizesafe(new float3(pt.Normal.x, 0f, pt.Normal.z), new float3(0f, 0f, 1f));
            var rotation = math.mul(quaternion.LookRotationSafe(outward, new float3(0f, 1f, 0f)),
                                    quaternion.Euler(rng.NextFloat(-0.25f, 0.1f), 0f, rng.NextFloat(-0.35f, 0.35f)));
            var half = new float3(pt.Size * rng.NextFloat(1f, 1.8f), pt.Size * rng.NextFloat(0.35f, 0.6f), pt.Size * rng.NextFloat(0.8f, 1.2f));
            // Half sunk in the rock so it grows out of the wall.
            var centre = pt.Position + outward * half.z * 0.3f;
            b.Box(centre, half, rotation, pt.Tint);
            // The worn top: a thin pale slab just over the shelf's outer part.
            var up = math.mul(rotation, new float3(0f, 1f, 0f));
            var forward = math.mul(rotation, new float3(0f, 0f, 1f));
            chalk.Box(centre + up * (half.y + 0.004f) + forward * half.z * 0.3f,
                      new float3(half.x * 0.85f, 0.006f, half.z * 0.65f), rotation, detail.NextFloat(0.85f, 1.05f));
            // The worn lip: a pale strip along the top of the shelf's outer face, the part seen from below.
            chalk.Box(centre + forward * (half.z + 0.004f) + up * half.y * 0.55f,
                      new float3(half.x * 0.9f, half.y * 0.45f, 0.006f), rotation, detail.NextFloat(0.85f, 1.05f));
            if (detail.NextFloat() < 0.7f)
            {
                // A scuff: a flat pale smear pressed onto the rock a little above the hold, tilted at random.
                var smear = quaternion.LookRotationSafe(outward, new float3(detail.NextFloat(-0.5f, 0.5f), 1f, 0f));
                var s = pt.Size * detail.NextFloat(1.4f, 2.4f);
                chalk.Box(pt.Position + new float3(0f, pt.Size * detail.NextFloat(1.5f, 3f), 0f) + outward * 0.012f,
                          new float3(s, s * 0.45f, 0.006f), smear, detail.NextFloat(0.7f, 0.95f));
            }
        }

        /// <summary>A vine leaf: a small flat blade standing off the strand, tilted up and paler than the vine so it
        /// catches the lantern.</summary>
        static void Leaf(BatFolkMeshes.Builder b, in ClimbPoint pt, ref Unity.Mathematics.Random rng)
        {
            var outward = math.normalizesafe(new float3(pt.Normal.x, 0f, pt.Normal.z), new float3(0f, 0f, 1f));
            var side = math.cross(new float3(0f, 1f, 0f), outward) * rng.NextFloat(-1f, 1f);
            var facing = math.normalizesafe(outward + new float3(0f, rng.NextFloat(0.3f, 0.9f), 0f) + side * 0.5f, outward);
            var rotation = quaternion.LookRotationSafe(facing, new float3(rng.NextFloat(-0.6f, 0.6f), 1f, 0f));
            var size = rng.NextFloat(0.035f, 0.06f);
            b.Box(pt.Position + outward * 0.02f + side * 0.04f, new float3(size, size * 0.6f, 0.004f), rotation,
                  rng.NextFloat(1.25f, 1.5f));
        }

        /// <summary>A small cairn of three pale stones just out from a route's foot: a cue that someone climbed here.</summary>
        static void Cairn(BatFolkMeshes.Builder b, in CaveClimb c, ref Unity.Mathematics.Random rng)
        {
            var outward = math.normalizesafe(new float3(c.Out.x, 0f, c.Out.y), new float3(0f, 0f, 1f));
            var side = math.cross(new float3(0f, 1f, 0f), outward);
            var at = c.Base + outward * rng.NextFloat(0.5f, 0.8f) + side * rng.NextFloat(-0.4f, 0.4f);
            var y = 0f;
            for (int k = 0; k < 3; k++)
            {
                var r = 0.11f - k * 0.025f;
                var half = new float3(r, r * 0.5f, r * 0.85f);
                b.Box(at + new float3(0f, y + half.y * 0.8f, 0f), half,
                      quaternion.Euler(rng.NextFloat(-0.15f, 0.15f), rng.NextFloat(0f, 6.28f), rng.NextFloat(-0.15f, 0.15f)),
                      rng.NextFloat(0.8f, 1f));
                y += half.y * 1.6f;
            }
        }

        void ReleaseClimbs()
        {
            foreach (var (rock, strands, chalk, _, _, _) in climbMeshes)
            {
                if (rock != null) Destroy(rock);
                if (strands != null) Destroy(strands);
                if (chalk != null) Destroy(chalk);
            }
            climbMeshes.Clear();
            climbLight.Clear();
            climbShown = chalkShown = System.Array.Empty<bool>();
            if (climbRoots != null) Destroy(climbRoots);
            if (climbVines != null) Destroy(climbVines);
            if (climbRope != null) Destroy(climbRope);
            if (climbChalk != null) Destroy(climbChalk);
            climbRoots = climbVines = climbRope = climbChalk = null;
            climbsFrom = default;
        }
    }
}
