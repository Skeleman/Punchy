using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using Part = Punchy.Terrain.CaveRoomKit.Part;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-91 row 2 (#24): the dungeon props, built from parts as the room kit is (boxes, drums, crystals) and drawn
    /// with copies of the cave material, so they are lit like the walls round them and wear the same house finish:
    /// dressed stone, wood grain, rusted iron (Cave.shader _Dressed). Each prop fills the box
    /// <see cref="DungeonSpawner.PropPlace"/> gives it (local space, centred, +z facing the room), so what you bump
    /// into is what you see. One mesh set per content and box size, shared by every prop of that kind.
    /// </summary>
    public static class DungeonProps
    {
        /// <summary>Prop materials (the kit's mesh groups are reused as slots).</summary>
        const int Stone = 0, Wood = 1, Iron = 2, Bone = 3, Cloth = 4, Clay = 5;

        static readonly Dictionary<(DungeonContent, int), Mesh[]> Meshes = new();
        static Material[] materials;
        static Material source;

        /// <summary>The props' material copies (null when there is no cave material to copy yet).</summary>
        static Material[] Materials()
        {
            var cave = Object.FindFirstObjectByType<CaveSystem>();
            if (cave == null || cave.Material == null)
                return null;
            if (materials != null && source == cave.Material && materials[0] != null)
                return materials;
            source = cave.Material;
            materials = new Material[CaveRoomKit.Groups];
            for (int g = 0; g < materials.Length; g++)
            {
                var (rock, dark, dressed) = g switch
                {
                    Stone => (new Color(0.62f, 0.58f, 0.52f), new Color(0.33f, 0.3f, 0.27f), 1f),
                    Wood => (new Color(0.46f, 0.31f, 0.19f), new Color(0.2f, 0.12f, 0.07f), 2f),
                    Iron => (new Color(0.3f, 0.3f, 0.32f), new Color(0.12f, 0.12f, 0.13f), 3f),
                    Bone => (new Color(0.86f, 0.82f, 0.7f), new Color(0.55f, 0.5f, 0.4f), -1f),
                    Cloth => (new Color(0.62f, 0.14f, 0.1f), new Color(0.3f, 0.06f, 0.05f), -1f),
                    _ => (new Color(0.7f, 0.42f, 0.26f), new Color(0.38f, 0.2f, 0.12f), -1f),
                };
                var mat = new Material(source) { name = "Dungeon prop " + g };
                mat.SetColor("_RockColor", rock);
                mat.SetColor("_DarkColor", dark);
                mat.SetFloat("_Dressed", dressed);
                materials[g] = mat;
            }
            return materials;
        }

        /// <summary>
        /// The prop for a pick, one child per material under a root at the box's centre; null when there is no cave
        /// material (the caller then falls back to a plain box).
        /// </summary>
        public static GameObject Make(DungeonContent c, float3 size)
        {
            var mats = Materials();
            if (mats == null)
                return null;
            var key = (c, (int)math.round(size.x * 100f) | (int)math.round(size.y * 100f) << 10 | (int)math.round(size.z * 100f) << 20);
            // (Rebuilt if Unity dropped the cached meshes, e.g. play mode ended without a domain reload.)
            if (!Meshes.TryGetValue(key, out var meshes) || !Alive(meshes))
            {
                var kit = new CaveRoomKit.Kit();
                Build(kit.Parts, c, size, (uint)key.Item2 ^ (uint)c * 0x9E3779B1u);
                meshes = CaveRoomKit.BuildMeshes(kit);
                Meshes[key] = meshes;
            }
            var root = new GameObject($"Dungeon {c}");
            for (int g = 0; g < meshes.Length; g++)
            {
                if (meshes[g] == null)
                    continue;
                var child = new GameObject(mats[g].name);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[g];
                child.AddComponent<MeshRenderer>().sharedMaterial = mats[g];
            }
            return root;
        }

        static bool Alive(Mesh[] meshes)
        {
            foreach (var m in meshes)
                if (!ReferenceEquals(m, null))
                    return m != null;
            return true;
        }

        static void Box(List<Part> parts, int group, float3 centre, float3 half, float tint = 1f, quaternion? rot = null) =>
            parts.Add(new Part { Centre = centre, Half = half, Rotation = rot ?? quaternion.identity, Tint = tint, Group = (byte)group });

        static void Drum(List<Part> parts, int group, float3 centre, float radius, float halfHeight, float tint = 1f, quaternion? rot = null) =>
            parts.Add(new Part { Centre = centre, Half = new float3(radius, halfHeight, radius), Rotation = rot ?? quaternion.identity, Tint = tint, Group = (byte)group, Shape = 2 });

        /// <summary>The parts of one prop inside a box of the given size (centred on 0, floor at -size.y/2, +z out).</summary>
        static void Build(List<Part> p, DungeonContent c, float3 size, uint seed)
        {
            var h = size * 0.5f;
            var rng = new Random(seed | 1u);
            var floor = -h.y;
            switch (c)
            {
                case DungeonContent.Crate:
                {
                    // Planked box with a frame of darker boards on its edges.
                    Box(p, Wood, 0, h * 0.94f, 0.95f);
                    for (int i = 0; i < 4; i++)
                    {
                        var x = (i & 1) == 0 ? -1f : 1f;
                        var z = (i & 2) == 0 ? -1f : 1f;
                        Box(p, Wood, new float3(x * (h.x - 0.03f), 0, z * (h.z - 0.03f)), new float3(0.035f, h.y, 0.035f), 0.7f);
                    }
                    foreach (var y in new[] { -1f, 1f })
                    {
                        Box(p, Wood, new float3(0, y * (h.y - 0.03f), h.z - 0.03f), new float3(h.x, 0.035f, 0.035f), 0.7f);
                        Box(p, Wood, new float3(0, y * (h.y - 0.03f), -h.z + 0.03f), new float3(h.x, 0.035f, 0.035f), 0.7f);
                        Box(p, Wood, new float3(h.x - 0.03f, y * (h.y - 0.03f), 0), new float3(0.035f, 0.035f, h.z), 0.7f);
                        Box(p, Wood, new float3(-h.x + 0.03f, y * (h.y - 0.03f), 0), new float3(0.035f, 0.035f, h.z), 0.7f);
                    }
                    // A diagonal brace on the front.
                    var diag = math.atan2(h.y * 2f, h.x * 2f);
                    Box(p, Wood, new float3(0, 0, h.z - 0.02f), new float3(math.length(h.xy) * 0.92f, 0.03f, 0.03f), 0.75f, quaternion.RotateZ(diag));
                    break;
                }
                case DungeonContent.Barrel:
                {
                    var r = math.min(h.x, h.z);
                    Drum(p, Wood, new float3(0, floor + h.y * 0.12f, 0), r * 0.86f, h.y * 0.12f, 0.9f);
                    Drum(p, Wood, new float3(0, 0, 0), r * 0.97f, h.y * 0.78f, 1f);
                    Drum(p, Wood, new float3(0, h.y - h.y * 0.12f, 0), r * 0.86f, h.y * 0.12f, 0.9f);
                    foreach (var y in new[] { -0.8f, -0.3f, 0.3f, 0.8f })
                        Drum(p, Iron, new float3(0, y * h.y, 0), r * (math.abs(y) > 0.5f ? 0.9f : 1f), 0.025f);
                    break;
                }
                case DungeonContent.Urn:
                {
                    // Clay urn: foot, belly, shoulder, neck, lip; sometimes a chipped lid.
                    var r = math.min(h.x, h.z);
                    Drum(p, Clay, new float3(0, floor + 0.04f, 0), r * 0.45f, 0.04f, 0.8f);
                    Drum(p, Clay, new float3(0, floor + h.y * 0.55f, 0), r * 0.8f, h.y * 0.42f, 1f);
                    Drum(p, Clay, new float3(0, floor + h.y * 1.1f, 0), r * 0.95f, h.y * 0.22f, 1f);
                    Drum(p, Clay, new float3(0, floor + h.y * 1.45f, 0), r * 0.62f, h.y * 0.18f, 0.95f);
                    Drum(p, Clay, new float3(0, floor + h.y * 1.68f, 0), r * 0.4f, h.y * 0.12f, 0.9f);
                    Drum(p, Clay, new float3(0, floor + h.y * 1.86f, 0), r * 0.5f, h.y * 0.06f, 0.85f);
                    Drum(p, Cloth, new float3(0, floor + h.y * 1.1f, 0), r * 0.97f, 0.02f, 0.6f);
                    break;
                }
                case DungeonContent.RockPile:
                {
                    // Fallen blocks and rubble, the bigger ones at the bottom.
                    for (int i = 0; i < 7; i++)
                    {
                        var s = math.lerp(0.55f, 0.25f, i / 6f) * math.min(h.x, h.z);
                        var at = new float3(rng.NextFloat(-1f, 1f) * (h.x - s), floor + s * 0.8f + (i > 3 ? s * 1.2f : 0f), rng.NextFloat(-1f, 1f) * (h.z - s));
                        var rot = quaternion.Euler(rng.NextFloat(-0.5f, 0.5f), rng.NextFloat(3.14f), rng.NextFloat(-0.5f, 0.5f));
                        Box(p, Stone, at, new float3(s * 1.3f, s * 0.8f, s), rng.NextFloat(0.75f, 1.05f), rot);
                    }
                    break;
                }
                case DungeonContent.Bones:
                {
                    // A skull and a scatter of long bones on the floor.
                    var s = math.min(h.x, h.z);
                    Box(p, Bone, new float3(-s * 0.3f, floor + s * 0.22f, s * 0.2f), new float3(s * 0.22f, s * 0.2f, s * 0.26f), 1f, quaternion.RotateY(rng.NextFloat(6.28f)));
                    for (int i = 0; i < 5; i++)
                    {
                        var at = new float3(rng.NextFloat(-0.6f, 0.6f) * h.x, floor + 0.025f + i * 0.012f, rng.NextFloat(-0.6f, 0.6f) * h.z);
                        Drum(p, Bone, at, 0.025f, s * rng.NextFloat(0.4f, 0.8f), rng.NextFloat(0.8f, 1f),
                             math.mul(quaternion.RotateY(rng.NextFloat(6.28f)), quaternion.RotateX(1.5708f)));
                    }
                    Box(p, Bone, new float3(s * 0.3f, floor + 0.05f, -s * 0.3f), new float3(s * 0.25f, 0.04f, s * 0.12f), 0.85f, quaternion.RotateY(rng.NextFloat(6.28f)));
                    break;
                }
                case DungeonContent.Cobweb:
                {
                    // Strands strung in a fan, and a dusty clot at the middle (on the wall's face when it hangs on one).
                    var s = math.min(h.x, h.y);
                    var mid = new float3(0, floor + h.y * 1.2f, h.z < 0.1f ? h.z : 0f);
                    for (int i = 0; i < 6; i++)
                    {
                        var a = i * 0.5236f;
                        var dir = new float3(math.cos(a), math.sin(a), 0.2f);
                        Box(p, Bone, mid + dir * s * 0.45f, new float3(s * 0.5f, 0.004f, 0.004f), 0.9f, quaternion.LookRotationSafe(math.cross(dir, new float3(0, 0, 1)), dir));
                    }
                    Drum(p, Bone, mid, s * 0.12f, 0.01f, 0.8f, quaternion.RotateX(1.5708f));
                    break;
                }
                case DungeonContent.Coffer:
                {
                    Box(p, Wood, new float3(0, floor + h.y * 0.7f, 0), new float3(h.x, h.y * 0.7f, h.z) * 0.95f);
                    Drum(p, Wood, new float3(0, floor + h.y * 1.4f, 0), h.z * 0.95f, h.x * 0.95f, 0.9f, quaternion.RotateZ(1.5708f));
                    foreach (var x in new[] { -0.6f, 0.6f })
                        Box(p, Iron, new float3(x * h.x, floor + h.y * 0.9f, 0), new float3(0.03f, h.y * 0.9f, h.z * 0.97f));
                    Box(p, Iron, new float3(0, floor + h.y * 1.25f, h.z * 0.97f), new float3(0.05f, 0.06f, 0.02f));
                    break;
                }
                case DungeonContent.OreCart:
                {
                    // A wooden mine cart on four iron wheels, ore heaped in it, a push bar at the back.
                    var wheelR = h.y * 0.22f;
                    var bodyBottom = floor + wheelR * 1.3f;
                    var bodyHalf = new float3(h.x * 0.85f, h.y * 0.35f, h.z * 0.95f);
                    var body = new float3(0, bodyBottom + bodyHalf.y, 0);
                    Box(p, Wood, body - new float3(0, bodyHalf.y - 0.03f, 0), new float3(bodyHalf.x, 0.03f, bodyHalf.z), 0.85f);
                    Box(p, Wood, body + new float3(bodyHalf.x, 0, 0), new float3(0.03f, bodyHalf.y, bodyHalf.z));
                    Box(p, Wood, body - new float3(bodyHalf.x, 0, 0), new float3(0.03f, bodyHalf.y, bodyHalf.z));
                    Box(p, Wood, body + new float3(0, 0, bodyHalf.z), new float3(bodyHalf.x, bodyHalf.y, 0.03f), 0.9f);
                    Box(p, Wood, body - new float3(0, 0, bodyHalf.z), new float3(bodyHalf.x, bodyHalf.y, 0.03f), 0.9f);
                    foreach (var y in new[] { -0.7f, 0.85f })
                        Box(p, Iron, body + new float3(0, y * bodyHalf.y, 0), new float3(bodyHalf.x + 0.02f, 0.025f, bodyHalf.z + 0.02f));
                    for (int i = 0; i < 4; i++)
                    {
                        var x = (i & 1) == 0 ? -1f : 1f;
                        var z = (i & 2) == 0 ? -0.6f : 0.6f;
                        Drum(p, Iron, new float3(x * (bodyHalf.x + 0.04f), floor + wheelR, z * bodyHalf.z), wheelR, 0.035f, 1f, quaternion.RotateZ(1.5708f));
                    }
                    foreach (var z in new[] { -0.6f, 0.6f })
                        Drum(p, Iron, new float3(0, floor + wheelR, z * bodyHalf.z), 0.025f, bodyHalf.x + 0.04f, 0.8f, quaternion.RotateZ(1.5708f));
                    for (int i = 0; i < 6; i++)
                    {
                        var s = rng.NextFloat(0.1f, 0.16f) * h.x * 2f;
                        var at = body + new float3(rng.NextFloat(-0.6f, 0.6f) * bodyHalf.x, bodyHalf.y * 0.6f, rng.NextFloat(-0.7f, 0.7f) * bodyHalf.z);
                        Box(p, Stone, at, new float3(s, s * 0.8f, s), rng.NextFloat(0.5f, 0.8f), quaternion.Euler(rng.NextFloat(3f), rng.NextFloat(3f), 0));
                    }
                    break;
                }
                case DungeonContent.Well:
                {
                    // A ring of cut stone with a dark water disc, two posts, a beam with a winch and a bucket.
                    var r = math.min(h.x, h.z) * 0.92f;
                    var wallTop = floor + h.y * 0.75f;
                    for (int i = 0; i < 10; i++)
                    {
                        var a = i * 6.2832f / 10f;
                        Box(p, Stone, new float3(math.sin(a) * r * 0.85f, (floor + wallTop) * 0.5f, math.cos(a) * r * 0.85f),
                            new float3(r * 0.29f, (wallTop - floor) * 0.5f, r * 0.13f), rng.NextFloat(0.85f, 1.05f), quaternion.RotateY(a));
                    }
                    Drum(p, Iron, new float3(0, wallTop - 0.15f, 0), r * 0.72f, 0.01f, 0.25f);
                    foreach (var x in new[] { -1f, 1f })
                        Box(p, Wood, new float3(x * r * 0.85f, (wallTop + h.y) * 0.5f, 0), new float3(0.05f, (h.y - wallTop) * 0.5f + 0.05f, 0.05f), 0.8f);
                    Drum(p, Wood, new float3(0, h.y - 0.1f, 0), 0.05f, r * 0.9f, 0.9f, quaternion.RotateZ(1.5708f));
                    Box(p, Bone, new float3(0, (wallTop + h.y) * 0.5f + 0.1f, 0), new float3(0.006f, (h.y - wallTop) * 0.35f, 0.006f), 0.7f);
                    Drum(p, Wood, new float3(0, wallTop + 0.12f, 0), 0.09f, 0.08f, 0.8f);
                    Drum(p, Iron, new float3(0, wallTop + 0.17f, 0), 0.095f, 0.012f);
                    break;
                }
                case DungeonContent.Altar:
                {
                    // A stepped stone block with a slab top, a red runner and two candles.
                    Box(p, Stone, new float3(0, floor + h.y * 0.08f, 0), new float3(h.x, h.y * 0.08f, h.z), 0.85f);
                    Box(p, Stone, new float3(0, floor + h.y * 0.5f, 0), new float3(h.x * 0.8f, h.y * 0.36f, h.z * 0.75f), 0.95f);
                    Box(p, Stone, new float3(0, floor + h.y * 0.92f, 0), new float3(h.x * 0.95f, h.y * 0.07f, h.z * 0.9f), 1.05f);
                    Box(p, Cloth, new float3(0, floor + h.y * 1.0f, 0), new float3(h.x * 0.25f, 0.01f, h.z * 0.92f));
                    Box(p, Cloth, new float3(0, floor + h.y * 0.75f, h.z * 0.91f), new float3(h.x * 0.25f, h.y * 0.25f, 0.01f));
                    foreach (var x in new[] { -0.7f, 0.7f })
                        Drum(p, Bone, new float3(x * h.x, floor + h.y * 1.0f + 0.08f, 0), 0.035f, 0.08f, 0.95f);
                    break;
                }
                case DungeonContent.Lever:
                {
                    // A stone housing with a slot, an iron arm tilted one way, a wooden grip.
                    Box(p, Stone, new float3(0, floor + h.y * 0.18f, 0), new float3(h.x * 0.6f, h.y * 0.18f, h.z * 0.45f), 0.9f);
                    Box(p, Iron, new float3(0, floor + h.y * 0.37f, 0), new float3(h.x * 0.4f, 0.02f, 0.04f), 0.6f);
                    var tilt = quaternion.RotateX(0.5f);
                    var arm = h.y * 0.5f;
                    var top = new float3(0, floor + h.y * 0.36f, 0) + math.mul(tilt, new float3(0, arm * 2f, 0));
                    Drum(p, Iron, new float3(0, floor + h.y * 0.36f, 0) + math.mul(tilt, new float3(0, arm, 0)), 0.025f, arm, 1f, tilt);
                    Drum(p, Wood, top, 0.045f, 0.09f, 1f, tilt);
                    break;
                }
                case DungeonContent.Sconce:
                case DungeonContent.MineLamp:
                {
                    // A wall plate, an iron arm, a cup; a candle (sconce) or a caged lamp (mine lamp).
                    // (Wall decor's box ends at the wall's face, its front: the arm reaches out of it into the room.)
                    var back = h.z - 0.02f;
                    Box(p, Iron, new float3(0, 0, back), new float3(h.x * 0.3f, h.y * 0.4f, 0.02f));
                    Box(p, Iron, new float3(0, -h.y * 0.05f, back + 0.11f), new float3(0.02f, 0.02f, 0.11f));
                    var cup = new float3(0, 0, back + 0.22f);
                    if (c == DungeonContent.Sconce)
                    {
                        Drum(p, Iron, cup, h.x * 0.25f, 0.03f);
                        Drum(p, Bone, cup + new float3(0, 0.1f, 0), 0.035f, 0.08f, 0.95f);
                    }
                    else
                    {
                        Drum(p, Iron, cup + new float3(0, -0.05f, 0), h.x * 0.25f, 0.02f);
                        Drum(p, Clay, cup + new float3(0, 0.05f, 0), h.x * 0.18f, 0.08f, 1.3f);
                        for (int i = 0; i < 4; i++)
                        {
                            var a = i * 1.5708f + 0.785f;
                            Box(p, Iron, cup + new float3(math.sin(a) * h.x * 0.2f, 0.05f, math.cos(a) * h.x * 0.2f), new float3(0.008f, 0.1f, 0.008f));
                        }
                        Drum(p, Iron, cup + new float3(0, 0.15f, 0), h.x * 0.22f, 0.015f);
                    }
                    break;
                }
                case DungeonContent.Banner:
                {
                    // An iron rod on two pins, a red cloth hanging from it with a pale band and a cut tail.
                    var back = h.z + 0.03f;
                    Drum(p, Iron, new float3(0, h.y - 0.03f, back), 0.018f, h.x * 0.95f, 1f, quaternion.RotateZ(1.5708f));
                    Box(p, Cloth, new float3(0, 0.02f, back), new float3(h.x * 0.8f, h.y * 0.9f, 0.01f));
                    Box(p, Bone, new float3(0, h.y * 0.45f, back + 0.012f), new float3(h.x * 0.8f, 0.03f, 0.004f), 0.9f);
                    Box(p, Bone, new float3(0, -h.y * 0.1f, back + 0.012f), new float3(h.x * 0.25f, h.y * 0.25f, 0.004f), 0.9f, quaternion.RotateZ(0.785f));
                    break;
                }
                case DungeonContent.SkullNiche:
                {
                    // A recess framed in stone, a skull looking out of it.
                    var front = h.z - 0.03f;
                    Box(p, Stone, new float3(0, 0, -h.z + 0.02f), new float3(h.x, h.y, 0.02f), 0.45f);
                    Box(p, Stone, new float3(0, h.y - 0.05f, front), new float3(h.x, 0.05f, 0.04f), 1f);
                    Box(p, Stone, new float3(0, -h.y + 0.05f, front), new float3(h.x, 0.05f, 0.05f), 1f);
                    Box(p, Stone, new float3(h.x - 0.04f, 0, front), new float3(0.04f, h.y, 0.04f), 0.95f);
                    Box(p, Stone, new float3(-h.x + 0.04f, 0, front), new float3(0.04f, h.y, 0.04f), 0.95f);
                    Box(p, Bone, new float3(0, -h.y + 0.17f, 0), new float3(0.09f, 0.08f, 0.1f), 1f);
                    Box(p, Bone, new float3(0, -h.y + 0.12f, 0.05f), new float3(0.06f, 0.03f, 0.06f), 0.85f);
                    break;
                }
                default:
                    Box(p, Stone, 0, h);
                    break;
            }
        }
    }
}
