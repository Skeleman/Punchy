using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>One bat folk settlement (#35): a chamber deep in an existing cave, and the pool in it.</summary>
    public struct BatFolkSite
    {
        /// <summary>Index into CaveSetBlob.Caves, and that cave's landmark Key.</summary>
        public int Cave;
        public uint Key;
        /// <summary>The settlement's chamber node (index into CaveSetBlob.Nodes), its centre and radius (with the cave's width).</summary>
        public int Chamber;
        public float3 Centre;
        public float Radius;
        /// <summary>The chamber's water pool (index into CaveSetBlob.Pools), drawn as white pool water.</summary>
        public int Pool;
    }

    /// <summary>What a bat folk prop is (placeholder shapes until real assets exist).</summary>
    public enum BatFolkPropKind : byte
    {
        /// <summary>A roost hut: a pale pod hanging on a silk stalk from the chamber ceiling.</summary>
        Hut,
        /// <summary>A drying rack on the floor: two posts, a bar and a hide hung over it.</summary>
        Rack,
        /// <summary>A squat woven basket on the floor.</summary>
        Basket,
    }

    /// <summary>One placed bat folk thing (BATFOLK-PLAN step 3).</summary>
    public struct BatFolkProp
    {
        /// <summary>Hut: the anchor on the ceiling. Floor props: the middle of the base, on the floor.</summary>
        public float3 Position;
        /// <summary>Hut: x pod radius, y pod length, z stalk length. Rack: x width, y height, z depth. Basket: x radius, y height.</summary>
        public float3 Size;
        public float Yaw;
        public BatFolkPropKind Kind;
        /// <summary>Index into BatFolkSetBlob.Sites.</summary>
        public int Site;
    }

    /// <summary>
    /// Bat folk settlements, kept apart from the caves: nothing else reads this, so landmark, settlement and cave
    /// output (and their hashes) are the same with or without it.
    /// </summary>
    public struct BatFolkSetBlob
    {
        public BlobArray<BatFolkSite> Sites;
        /// <summary>Per cave pool (same order as CaveSetBlob.Pools): 1 = white pool water.</summary>
        public BlobArray<byte> WhitePools;
        /// <summary>How many caves qualified (for the log).</summary>
        public int Qualifying;
        public float BiggestChamber;
        /// <summary>Huts and floor props of every site, in site order (W-19).</summary>
        public BlobArray<BatFolkProp> Props;
    }

    /// <summary>
    /// Picks bat folk caves among the baked caves (BATFOLK-PLAN step 1). Runs after CaveBaking and only reads the
    /// caves. A cave qualifies with a chamber of radius >= <see cref="MinChamber"/> at least <see cref="MinDepth"/>
    /// nodes in from the mouth (out of daylight) and a water pool, in that chamber if it has one, else the nearest in
    /// the cave (that pool is the one turned white). Up to one in <see cref="PerCaves"/> qualifying
    /// caves (at least one if any qualify) is picked, lowest first by a scrambled hash of its mouth cell and its own
    /// seed stream, so neither list order nor any other cave's changes reshuffle the picks.
    /// </summary>
    public static class BatFolkBaking
    {
        public const float MinChamber = 9f;
        public const int MinDepth = 3;
        public const int PerCaves = 6;
        const float MouthCell = 16f;

        /// <summary>The stage's own stream: the world seed with "batfolk" mixed in.</summary>
        static uint StreamSeed(uint seed) => BiomeSampling.Scramble(seed ^ 0xBA7F01C5u);

        public static string LastLog = "";

        public static BlobAssetReference<BatFolkSetBlob> Build(in TerrainSource source, Allocator allocator)
        {
            if (!source.Caves.IsCreated)
            {
                LastLog = "[batfolk] caves 0, picked 0 (no caves)";
                return default;
            }
            ref var set = ref source.Caves.Value;
            var stream = StreamSeed(source.World.Seed);
            // (cave, chamber node, pool, rank)
            var qualifying = new List<(int cave, int node, int pool, uint rank)>();
            var biggest = 0f;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                int bestNode = -1, bestPool = -1;
                var bestR = 0f;
                var bestOwn = false;
                for (int n = cave.FirstNode + MinDepth; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var r = set.Nodes[n].Radius * cave.Profile.x;
                    biggest = math.max(biggest, r);
                    if (r < MinChamber)
                        continue;
                    // A chamber holding its own pool wins over a bigger one without; otherwise the biggest.
                    var pool = PoolIn(ref set, cave, set.Nodes[n].Position, r);
                    var own = pool >= 0;
                    if (own == bestOwn ? r <= bestR : !own)
                        continue;
                    if (!own)
                        pool = PoolIn(ref set, cave, set.Nodes[n].Position, 1e6f);
                    if (pool < 0)
                        continue;
                    bestR = r; bestNode = n; bestPool = pool; bestOwn = own;
                }
                if (bestNode < 0)
                    continue;
                var cell = (int2)math.floor(cave.Mouth.xz / MouthCell);
                var rank = BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble((uint)cell.x * 0x85EBCA6Bu ^ (uint)cell.y * 0xC2B2AE35u));
                qualifying.Add((c, bestNode, bestPool, rank));
            }
            var picks = qualifying.Count == 0 ? 0 : math.max(1, qualifying.Count / PerCaves);
            var chosen = new List<(int cave, int node, int pool, uint rank)>(qualifying);
            chosen.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.cave.CompareTo(b.cave));
            chosen.RemoveRange(picks, chosen.Count - picks);
            chosen.Sort((a, b) => a.cave.CompareTo(b.cave));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BatFolkSetBlob>();
            var sites = builder.Allocate(ref root.Sites, chosen.Count);
            root.Qualifying = qualifying.Count;
            root.BiggestChamber = biggest;
            var white = builder.Allocate(ref root.WhitePools, set.Pools.Length);
            for (int i = 0; i < set.Pools.Length; i++)
                white[i] = 0;
            var props = new List<BatFolkProp>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var (c, n, p, _) = chosen[i];
                sites[i] = new BatFolkSite
                {
                    Cave = c, Key = set.Caves[c].Key, Chamber = n, Centre = set.Nodes[n].Position,
                    Radius = set.Nodes[n].Radius * set.Caves[c].Profile.x, Pool = p
                };
                white[p] = 1;
                PlaceProps(source, ref set, sites[i], i, stream, props);
            }
            var propArray = builder.Allocate(ref root.Props, props.Count);
            for (int i = 0; i < props.Count; i++)
                propArray[i] = props[i];
            var blob = builder.CreateBlobAssetReference<BatFolkSetBlob>(allocator);
            builder.Dispose();
            LastLog = Report(ref blob.Value, set.Caves.Length);
            return blob;
        }

        public const int MinHuts = 3, MaxHuts = 6, MinProps = 4, MaxProps = 8;
        /// <summary>Floor props keep this far (m) off the cave's path through the chamber, so the way through stays open.</summary>
        const float PathClear = 2.2f;

        /// <summary>
        /// BATFOLK-PLAN step 3: 3-6 roost huts hanging from the chamber's ceiling and 4-8 floor props round it, drawn
        /// from the site's own RNG (the stage stream and the cave's Key). Every spot is tested against the cave field
        /// (the walls, speleothems and rubble are all in it), so nothing is placed in or through rock; huts and props
        /// keep clear of the cave's pools, and props of the path through the chamber. Reads the caves only.
        /// </summary>
        static void PlaceProps(in TerrainSource source, ref CaveSetBlob set, in BatFolkSite site, int siteIndex, uint stream,
            List<BatFolkProp> props)
        {
            var rng = new Random(BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble(site.Key * 0x9E3779B1u)) | 1u);
            var wantHuts = rng.NextInt(MinHuts, MaxHuts + 1);
            var wantProps = rng.NextInt(MinProps, MaxProps + 1);
            ref var cave = ref set.Caves[site.Cave];
            var first = props.Count;

            // Huts: a spot under the ceiling, high enough above the floor to walk under, clear of the walls round the pod.
            var huts = 0;
            for (int attempt = 0; attempt < 60 && huts < wantHuts; attempt++)
            {
                var angle = rng.NextFloat(math.PI * 2f);
                var dist = site.Radius * math.sqrt(rng.NextFloat(0.04f, 0.6f));
                var radius = rng.NextFloat(0.55f, 0.85f);
                var length = radius * rng.NextFloat(2.3f, 2.9f);
                var stalk = rng.NextFloat(0.35f, 1.1f);
                var yaw = rng.NextFloat(math.PI * 2f);
                var xz = site.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                var start = new float3(xz.x, site.Centre.y, xz.y);
                if (source.CaveDistance(start) > -0.5f || NearPool(ref set, cave, xz, radius + 0.6f))
                    continue;
                var ceiling = Ceiling(source, start);
                var floor = source.FloorUnder(start, out var inCave);
                if (!inCave || ceiling - floor < length + stalk + 2.6f)
                    continue;
                // The pod's top, middle and bottom, each with room for its girth (the top narrows to the stalk).
                var top = ceiling - stalk;
                if (source.CaveDistance(new float3(xz.x, top - 0.15f, xz.y)) > -0.35f ||
                    source.CaveDistance(new float3(xz.x, top - length * 0.55f, xz.y)) > -(radius + 0.2f) ||
                    source.CaveDistance(new float3(xz.x, top - length + 0.1f, xz.y)) > -0.4f)
                    continue;
                if (Crowded(props, first, xz, radius, BatFolkPropKind.Hut))
                    continue;
                props.Add(new BatFolkProp
                {
                    Position = new float3(xz.x, ceiling, xz.y), Size = new float3(radius, length, stalk), Yaw = yaw,
                    Kind = BatFolkPropKind.Hut, Site = siteIndex
                });
                huts++;
            }

            // Floor props: a ring round the chamber, on floor flat enough to stand on, with headroom.
            var placed = 0;
            for (int attempt = 0; attempt < 80 && placed < wantProps; attempt++)
            {
                var angle = rng.NextFloat(math.PI * 2f);
                var dist = site.Radius * rng.NextFloat(0.3f, 0.85f);
                var rack = rng.NextFloat() < 0.4f;
                var size = rack
                    ? new float3(rng.NextFloat(1.3f, 1.8f), rng.NextFloat(1.2f, 1.6f), 0.35f)
                    : new float3(rng.NextFloat(0.32f, 0.55f), rng.NextFloat(0.4f, 0.7f), 0f);
                var reach = rack ? size.x * 0.5f + 0.15f : size.x;
                var yaw = rng.NextFloat(math.PI * 2f);
                var xz = site.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                if (NearPool(ref set, cave, xz, reach + 0.8f) || OnPath(ref set, site.Chamber, xz, reach + PathClear))
                    continue;
                var floor = source.FloorUnder(new float3(xz.x, site.Centre.y + 1f, xz.y), out var inCave);
                if (!inCave || source.CaveDistance(new float3(xz.x, floor + 0.3f, xz.y)) > -0.2f)
                    continue;
                // Flat: the floor under each corner of its footprint within 30 cm, each with air above it.
                var flat = true;
                for (int k = 0; k < 4 && flat; k++)
                {
                    var o = new float2(math.cos(yaw + k * math.PI * 0.5f), math.sin(yaw + k * math.PI * 0.5f)) * reach;
                    var f = source.FloorUnder(new float3(xz.x + o.x, floor + 1f, xz.y + o.y), out var inside);
                    flat = inside && math.abs(f - floor) < 0.3f &&
                           source.CaveDistance(new float3(xz.x + o.x, f + size.y * 0.6f, xz.y + o.y)) < -0.15f;
                }
                if (!flat || source.CaveDistance(new float3(xz.x, floor + size.y + 0.6f, xz.y)) > -0.5f)
                    continue;
                if (Crowded(props, first, xz, reach, rack ? BatFolkPropKind.Rack : BatFolkPropKind.Basket))
                    continue;
                props.Add(new BatFolkProp
                {
                    // Sunk a little, so a gentle slope never shows a gap under it.
                    Position = new float3(xz.x, floor - 0.08f, xz.y), Size = size, Yaw = yaw,
                    Kind = rack ? BatFolkPropKind.Rack : BatFolkPropKind.Basket, Site = siteIndex
                });
                placed++;
            }
        }

        /// <summary>The ceiling above a point in cave air: marches up through the air by part of the distance to rock.</summary>
        static float Ceiling(in TerrainSource source, float3 p)
        {
            for (int i = 0; i < 200; i++)
            {
                var d = source.CaveDistance(p);
                if (d > -0.05f)
                    break;
                p.y += math.max(-d * 0.4f, 0.05f);
            }
            return p.y;
        }

        /// <summary>Whether a spot is within <paramref name="margin"/> of the rim of any of the cave's pools.</summary>
        static bool NearPool(ref CaveSetBlob set, in CaveData cave, float2 xz, float margin)
        {
            for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                if (math.distance(set.Pools[i].Centre.xz, xz) < set.Pools[i].Radius + margin)
                    return true;
            return false;
        }

        /// <summary>Whether a spot is within <paramref name="margin"/> of the passage axis through the chamber node.</summary>
        static bool OnPath(ref CaveSetBlob set, int chamber, float2 xz, float margin)
        {
            for (int s = 0; s < set.Segments.Length; s++)
            {
                ref var seg = ref set.Segments[s];
                if (seg.A != chamber && seg.B != chamber)
                    continue;
                var a = set.Nodes[seg.A].Position.xz;
                var ab = set.Nodes[seg.B].Position.xz - a;
                var t = math.saturate(math.dot(xz - a, ab) / math.max(math.dot(ab, ab), 1e-4f));
                if (math.distance(a + ab * t, xz) < margin)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a new thing of this kind would crowd one already placed in the site (huts against huts, props against props).</summary>
        static bool Crowded(List<BatFolkProp> props, int first, float2 xz, float reach, BatFolkPropKind kind)
        {
            var hut = kind == BatFolkPropKind.Hut;
            for (int i = first; i < props.Count; i++)
            {
                var p = props[i];
                if ((p.Kind == BatFolkPropKind.Hut) != hut)
                    continue;
                var other = p.Kind == BatFolkPropKind.Rack ? p.Size.x * 0.5f + 0.15f : p.Size.x;
                if (math.distance(p.Position.xz, xz) < reach + other + (hut ? 0.9f : 0.7f))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The cave's nearest water pool whose centre lies inside a chamber's radius, or -1. With a huge radius, the
        /// nearest water pool anywhere in the cave (the plan allows the pool of the next chamber along).
        /// </summary>
        static int PoolIn(ref CaveSetBlob set, in CaveData cave, float3 centre, float radius)
        {
            int best = -1;
            var bestD = radius;
            for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f)
                    continue;
                var d = math.distance(pool.Centre.xz, centre.xz);
                if (d <= bestD && (radius > 1e5f || math.abs(pool.Centre.y - centre.y) < radius * 2f))
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        static string Report(ref BatFolkSetBlob b, int caves)
        {
            var sb = new StringBuilder($"[batfolk] caves {b.Qualifying}, picked {b.Sites.Length} (of {caves} caves; biggest deep chamber {b.BiggestChamber:F1} m)");
            for (int i = 0; i < b.Sites.Length; i++)
            {
                ref var s = ref b.Sites[i];
                int huts = 0, props = 0;
                for (int k = 0; k < b.Props.Length; k++)
                    if (b.Props[k].Site == i)
                    {
                        if (b.Props[k].Kind == BatFolkPropKind.Hut) huts++;
                        else props++;
                    }
                sb.Append($"; cave {s.Cave} chamber {s.Centre.x:F0},{s.Centre.y:F0},{s.Centre.z:F0} r {s.Radius:F1} pool {s.Pool} huts {huts} props {props}");
            }
            return sb.ToString();
        }

        /// <summary>The log line for a baked world (island v1 bakes in the editor, so the player logs it from the blob).</summary>
        public static string Report(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated)
                return "[batfolk] none baked";
            return Report(ref source.BatFolk.Value, source.Caves.IsCreated ? source.Caves.Value.Caves.Length : 0);
        }
    }
}
