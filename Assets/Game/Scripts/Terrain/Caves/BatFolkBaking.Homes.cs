using System;
using System.Collections.Generic;
using Punchy.Flora;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    /// <summary>Whose home a BatFolkHome is (W-96, #35): the two folk who share a bat folk chamber.</summary>
    public enum BatFolkHomeKind : byte
    {
        /// <summary>A hollow woven pod hung from the ceiling (BatFolkPropKind.Pod), entered through the opening in its side.</summary>
        BatFolkPod,
        /// <summary>A niche cut into the chamber wall (BatFolkPropKind.Niche), at walking height or up a short ledge.</summary>
        PygmyNiche,
    }

    /// <summary>
    /// One home in a bat folk chamber (W-96, #35): the anchor the animal lane sends a folk to at night. Seed-derived and
    /// baked with the props, so every machine has the same homes in the same order.
    /// </summary>
    public struct BatFolkHome
    {
        public BatFolkHomeKind Kind;
        /// <summary>Index into BatFolkSetBlob.Sites, and the home's prop (index into BatFolkSetBlob.Props).</summary>
        public int Site, Prop;
        /// <summary>
        /// Where its folk sleeps: on a pod's woven floor inside (up in the air: a bat folk flies or climbs up to it and
        /// in through the side opening, which faces along Face), or the middle of a niche's bedroll on its floor.
        /// </summary>
        public float3 Stand;
        /// <summary>On the cave floor in front of the home, where a walker arrives (under the pod; in front of the niche).</summary>
        public float3 Door;
        /// <summary>The way the home faces in plan (unit): a niche's opening, into the chamber; a pod's side opening.</summary>
        public float2 Face;
    }

    /// <summary>
    /// The bat folk chamber homes for other systems (W-96, #35): find a species' home by kind, and the pygmy niches'
    /// solid boxes, which FloraSystem adds to the kit collision near the players (the pods are decorative, no collision).
    /// The niche shell is rebuilt from its prop (no extra baked data), and BatFolkMeshes draws the same boxes.
    /// </summary>
    public static class BatFolkHomes
    {
        /// <summary>A niche's rock either side of its opening (m), and how far its shell runs back into the wall.</summary>
        public const float Jamb = 0.55f, Back = 1.1f, Lintel = 0.6f;
        /// <summary>Room inside a niche from its floor to the underside of its lintel (m).</summary>
        public static float Headroom(in BatFolkProp niche) => 1.55f + 0.4f * Unit(Style(niche.Position), 8);
        public const int ShellBoxes = 5;
        /// <summary>W-96: a pod's woven floor inside, as a share of its length down from the top (where a bat folk lies).</summary>
        public const float PodFloor = 0.93f;

        /// <summary>The home's look, from a hash of its spot (nothing extra baked): bits as Woven/Root/Fire/Left read them.</summary>
        public static uint Style(float3 position) =>
            BiomeSampling.Scramble(math.hash(new int3(math.floor(position * 16f))) ^ 0x96B0C0DEu);

        /// <summary>A pod woven of dark reeds (else stitched of pale hide); one hung on a root (else on a cord).</summary>
        public static bool Woven(uint style) => (style & 1u) != 0;
        public static bool Root(uint style) => (style & 2u) != 0;
        /// <summary>A niche with a fire ring (else a glow-pool); its bedroll on the left of the opening (else the right).</summary>
        public static bool Fire(uint style) => (style & 4u) != 0;
        public static bool Left(uint style) => (style & 8u) != 0;
        /// <summary>A share in [0, 1) from 8 bits of the style, starting at bit <paramref name="shift"/>.</summary>
        public static float Unit(uint style, int shift) => ((style >> shift) & 0xFFu) / 256f;

        /// <summary>How many homes of this kind a site has.</summary>
        public static int Count(in TerrainSource source, BatFolkHomeKind kind, int site)
        {
            if (!source.BatFolk.IsCreated)
                return 0;
            ref var homes = ref source.BatFolk.Value.Homes;
            var n = 0;
            for (int i = 0; i < homes.Length; i++)
                if (homes[i].Kind == kind && homes[i].Site == site)
                    n++;
            return n;
        }

        /// <summary>
        /// The <paramref name="index"/>th home of this kind in a site (wrapping round, so a band bigger than the homes
        /// shares them), or false if the site has none.
        /// </summary>
        public static bool Get(in TerrainSource source, BatFolkHomeKind kind, int site, int index, out BatFolkHome home)
        {
            home = default;
            var n = Count(source, kind, site);
            if (n == 0)
                return false;
            index = ((index % n) + n) % n;
            ref var homes = ref source.BatFolk.Value.Homes;
            for (int i = 0; i < homes.Length; i++)
                if (homes[i].Kind == kind && homes[i].Site == site && index-- == 0)
                {
                    home = homes[i];
                    return true;
                }
            return false;
        }

        /// <summary>The home of this kind whose door is nearest a point (any site), or false if there are none.</summary>
        public static bool Nearest(in TerrainSource source, BatFolkHomeKind kind, float3 from, out BatFolkHome home)
        {
            home = default;
            if (!source.BatFolk.IsCreated)
                return false;
            ref var homes = ref source.BatFolk.Value.Homes;
            var best = float.MaxValue;
            for (int i = 0; i < homes.Length; i++)
            {
                var d = math.distancesq(homes[i].Door, from);
                if (homes[i].Kind == kind && d < best)
                {
                    best = d;
                    home = homes[i];
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// A niche's solid rock in world space: the jamb either side, the lintel over the opening, the back wall and the
        /// floor slab (the ledge, when it has one). Returns how many it wrote (ShellBoxes).
        /// </summary>
        public static int NicheShell(in BatFolkProp niche, Span<KitBox> boxes)
        {
            var w = niche.Size.x * 0.5f;
            var depth = niche.Size.y;
            var ledge = niche.Size.z;
            var h = Headroom(niche);
            var low = -ledge - 0.35f;   // every box runs down into the cave floor, so a slope never shows under it
            var n = 0;
            for (int s = -1; s <= 1; s += 2)
                boxes[n++] = Local(niche, new float3(s * (w + Jamb * 0.5f), (low + h + 0.1f) * 0.5f, (depth - Back) * 0.5f),
                                   new float3(Jamb * 0.5f, (h + 0.1f - low) * 0.5f, (depth + Back) * 0.5f));
            boxes[n++] = Local(niche, new float3(0f, h + Lintel * 0.5f, (depth + 0.12f - Back) * 0.5f),
                               new float3(w + Jamb, Lintel * 0.5f, (depth + 0.12f + Back) * 0.5f));
            boxes[n++] = Local(niche, new float3(0f, (low + h) * 0.5f, -Back * 0.5f), new float3(w, (h - low) * 0.5f, Back * 0.5f));
            boxes[n++] = Local(niche, new float3(0f, low * 0.5f, depth * 0.5f), new float3(w, -low * 0.5f, depth * 0.5f));
            return n;
        }

        /// <summary>A box in a niche's own space (+Z out of the wall, y up from its floor) placed in the world.</summary>
        public static KitBox Local(in BatFolkProp niche, float3 centre, float3 half)
        {
            var turn = quaternion.RotateY(niche.Yaw);
            return new KitBox { Centre = niche.Position + math.mul(turn, centre), Half = half, Rotation = turn, Reach = math.length(half) };
        }

        /// <summary>A point in a niche's own space in the world.</summary>
        public static float3 At(in BatFolkProp niche, float3 local) => niche.Position + math.mul(quaternion.RotateY(niche.Yaw), local);

        /// <summary>
        /// W-96: adds the shells of the pygmy niches within <paramref name="range"/> of any of the players (plan
        /// positions) to the kit collision boxes. Called by FloraSystem with each refresh of the kit boxes.
        /// </summary>
        public static void AddKitBoxes(in TerrainSource source, List<float2> centres, DynamicBuffer<KitBox> boxes, float range = 16f)
        {
            if (!source.BatFolk.IsCreated)
                return;
            ref var bat = ref source.BatFolk.Value;
            Span<KitBox> shell = stackalloc KitBox[ShellBoxes];
            for (int i = 0; i < bat.Props.Length; i++)
            {
                ref var p = ref bat.Props[i];
                if (p.Kind != BatFolkPropKind.Niche)
                    continue;
                var near = false;
                foreach (var c in centres)
                    near |= math.distancesq(c, p.Position.xz) < range * range;
                if (!near)
                    continue;
                var n = NicheShell(p, shell);
                for (int k = 0; k < n; k++)
                    boxes.Add(shell[k]);
            }
        }
    }

    public static partial class BatFolkBaking
    {
        public const int MinPods = 3, MaxPods = 6, MinNiches = 3, MaxNiches = 5;
        const float NicheJamb = BatFolkHomes.Jamb;
        /// <summary>The pods' three size classes: (radius low, radius high), a few big family pods and many small ones.</summary>
        /// W-96: larger (Zach), so each reads as a hollow woven nest with room inside.
        static readonly float2[] PodSizes = { new float2(0.8f, 0.95f), new float2(1.05f, 1.25f), new float2(1.3f, 1.5f) };

        /// <summary>
        /// W-96 (#35): the homes, after the huts, props and kit and from the site's own third RNG, so everything placed
        /// before comes out the same. 3-6 pods hang from the ceiling (three sizes, their openings 2.6-5 m over the floor)
        /// for the bat folk; 3-5 niches are cut into the chamber wall for the blind pygmies, at walking height or up a
        /// short ledge, each with its interior in cave air, a flat floor and a clear way in. Each home is also listed
        /// in <paramref name="homes"/> (Prop indexes this site's list) for the animal lane.
        /// </summary>
        static void PlaceHomes(in TerrainSource source, ref CaveSetBlob set, in BatFolkSite site, int siteIndex, uint stream,
            List<BatFolkProp> props, List<BatFolkHome> homes)
        {
            var rng = new Random(BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble(site.Key * 0x7FEB352Du ^ 0x96u)) | 1u);
            ref var cave = ref set.Caves[site.Cave];
            const int first = 0;

            // Pods: hung like the huts (checked the same way: the pod's girth and the hanger in air), in three sizes.
            var want = rng.NextInt(MinPods, MaxPods + 1);
            for (int attempt = 0, n = 0; attempt < 120 && n < want; attempt++)
            {
                var angle = rng.NextFloat(math.PI * 2f);
                var dist = site.Radius * math.sqrt(rng.NextFloat(0.03f, 0.7f));
                var size = PodSizes[rng.NextFloat() < 0.45f ? 0 : rng.NextFloat() < 0.65f ? 1 : 2];
                var radius = rng.NextFloat(size.x, size.y);
                var length = radius * rng.NextFloat(2.1f, 2.5f);
                var lift = rng.NextFloat(2.6f, 5f);
                var yaw = rng.NextFloat(math.PI * 2f);
                var xz = site.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                var start = new float3(xz.x, site.Centre.y, xz.y);
                if (source.CaveDistance(start) > -0.5f || NearPool(ref set, cave, xz, radius + 0.4f))
                    continue;
                var ceiling = Ceiling(source, start);
                var floor = source.FloorUnder(start, out var inCave);
                var hanger = math.max(ceiling - floor - lift - length, 0.3f);
                if (!inCave || ceiling - floor < length + hanger + 2.6f)
                    continue;
                var top = ceiling - hanger;
                if (source.CaveDistance(new float3(xz.x, top - 0.15f, xz.y)) > -0.35f ||
                    source.CaveDistance(new float3(xz.x, top - length * 0.5f, xz.y)) > -(radius + 0.2f) ||
                    source.CaveDistance(new float3(xz.x, top - length * 0.72f, xz.y)) > -(radius + 0.1f) ||
                    source.CaveDistance(new float3(xz.x, top - length + 0.1f, xz.y)) > -0.4f)
                    continue;
                var blocked = false;
                for (float y = ceiling - 0.5f; y > top && !blocked; y -= 1f)
                    blocked = source.CaveDistance(new float3(xz.x, y, xz.y)) > -0.1f;
                if (blocked || Crowded(props, first, xz, radius, BatFolkPropKind.Pod))
                    continue;
                homes.Add(new BatFolkHome
                {
                    Kind = BatFolkHomeKind.BatFolkPod, Site = siteIndex, Prop = props.Count,
                    Stand = new float3(xz.x, top - length * BatFolkHomes.PodFloor, xz.y), Door = new float3(xz.x, floor, xz.y),
                    Face = new float2(math.sin(yaw), math.cos(yaw))
                });
                props.Add(new BatFolkProp
                {
                    Position = new float3(xz.x, ceiling, xz.y), Size = new float3(radius, length, hanger), Yaw = yaw,
                    Kind = BatFolkPropKind.Pod, Site = siteIndex
                });
                n++;
            }

            // Niches: march out from the chamber's middle at waist height to the wall, and cut the niche into it facing in.
            var floorC = source.FloorUnder(site.Centre + new float3(0f, 1f, 0f), out _);
            want = rng.NextInt(MinNiches, MaxNiches + 1);
            for (int attempt = 0, n = 0; attempt < 200 && n < want; attempt++)
            {
                var angle = rng.NextFloat(math.PI * 2f);
                var width = rng.NextFloat(1.6f, 2.2f);
                var depth = rng.NextFloat(1.1f, 1.5f);
                var ledge = rng.NextFloat() < 0.5f ? 0f : rng.NextFloat(0.25f, 0.4f);
                var dir = new float2(math.cos(angle), math.sin(angle));
                var y = floorC + 1f;
                var hit = -1f;
                for (var d = 1f; d < site.Radius * 2.5f; d += 0.2f)
                {
                    var probe = new float3(site.Centre.x + dir.x * d, y, site.Centre.z + dir.y * d);
                    // Follow the floor out (a chamber's floor rises and falls), keeping the probe at waist height.
                    var f = source.FloorUnder(probe + new float3(0f, 0.5f, 0f), out var inside);
                    if (inside && math.abs(f + 1f - y) < 0.6f)
                        y = f + 1f;
                    if (source.CaveDistance(new float3(probe.x, y, probe.z)) > -0.25f)
                    {
                        hit = d;
                        break;
                    }
                }
                if (hit < depth + 2f)
                    continue;
                var w = new float3(site.Centre.x + dir.x * hit, y, site.Centre.z + dir.y * hit);
                var g = new float2(source.CaveDistance(w + new float3(0.3f, 0f, 0f)) - source.CaveDistance(w - new float3(0.3f, 0f, 0f)),
                    source.CaveDistance(w + new float3(0f, 0f, 0.3f)) - source.CaveDistance(w - new float3(0f, 0f, 0.3f)));
                var outward = math.normalizesafe(g, dir);
                if (math.dot(outward, dir) < 0.4f)
                    continue;   // a wall seen edge-on (a buttress or a passage mouth): no flat face to cut into
                var yaw = math.atan2(-outward.x, -outward.y);   // the opening (+Z) faces into the chamber
                // The niche's back edge sits a little in from the wall, so its shell runs back into the rock.
                var back = w.xz - outward * 0.15f;
                var mid = back - outward * depth * 0.5f;
                var floor = source.FloorUnder(new float3(mid.x, y + 0.5f, mid.y), out var inCave);
                if (!inCave || math.abs(floor + 1f - y) > 0.7f)
                    continue;
                // Flat: the cave floor under the niche's front corners and its door within 25 cm, never above its floor.
                var turn = quaternion.RotateY(yaw);
                var nicheFloor = floor + ledge;
                var flat = true;
                for (int k = 0; k < 3 && flat; k++)
                {
                    var o = math.mul(turn, k == 2 ? new float3(0f, 0f, depth + 0.8f) : new float3((k * 2 - 1) * width * 0.5f, 0f, depth));
                    var f = source.FloorUnder(new float3(back.x + o.x, floor + 1f, back.y + o.z), out var inside);
                    flat = inside && math.abs(f - floor) < 0.25f;
                }
                if (!flat)
                    continue;
                var origin = new float3(back.x, nicheFloor, back.y);
                var probeProp = new BatFolkProp { Position = origin, Size = new float3(width, depth, ledge), Yaw = yaw, Kind = BatFolkPropKind.Niche };
                var h = BatFolkHomes.Headroom(probeProp);
                // The inside of the niche, and the way in before it, in cave air (the back half-metre may touch the wall).
                if (!BoxInAir(source, BatFolkHomes.At(probeProp, new float3(0f, 0.1f, depth * 0.5f + 0.25f)), yaw, width, h - 0.2f, depth - 0.5f) ||
                    !BoxInAir(source, new float3(back.x, floor, back.y) + math.mul(turn, new float3(0f, 0.15f, depth + 0.7f)), yaw, width, 1.6f, 1f))
                    continue;
                var reach = width * 0.5f + NicheJamb;
                if (OnPath(ref set, site.Chamber, mid, reach + 1.2f) || NearPool(ref set, cave, mid, reach + 0.6f) ||
                    Crowded(props, first, mid, reach, BatFolkPropKind.Niche))
                    continue;
                var style = BatFolkHomes.Style(origin);
                var side = BatFolkHomes.Left(style) ? -1f : 1f;
                homes.Add(new BatFolkHome
                {
                    Kind = BatFolkHomeKind.PygmyNiche, Site = siteIndex, Prop = props.Count,
                    Stand = BatFolkHomes.At(probeProp, new float3(side * (width * 0.5f - 0.42f), 0.1f, depth * 0.5f)),
                    Door = new float3(back.x, floor, back.y) + math.mul(turn, new float3(0f, 0f, depth + 0.7f)),
                    Face = math.mul(turn, new float3(0f, 0f, 1f)).xz
                });
                probeProp.Site = siteIndex;
                props.Add(probeProp);
                n++;
            }
        }
    }
}
