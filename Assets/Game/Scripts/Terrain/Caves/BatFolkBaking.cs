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
        // W-61: details. W-96: the W-61 kit houses are no longer placed (bat folk live only in pods); the two kinds stay
        // so the numbering of the rest does not move.
        /// <summary>No longer placed (W-96). Was a kit house on a roost deck against the wall.</summary>
        WallHouse,
        /// <summary>No longer placed (W-96). Was a kit house hung from the ceiling on four cords.</summary>
        CeilingHouse,
        /// <summary>A roost frame hung from the ceiling: two cords and bars to hang from. Position: the anchor; Size: x width, y frame height, z cord length.</summary>
        RoostFrame,
        /// <summary>A heap of guano-white stones on the floor. Size: x radius, y height.</summary>
        GuanoStone,
        /// <summary>Echo chimes hung from the ceiling: a ring of tubes of different lengths. Position: the anchor; Size: x ring radius, y longest tube, z cord length.</summary>
        EchoChime,
        /// <summary>A woven sling hammock between two posts on the floor. Size: x length, y post height.</summary>
        Hammock,
        /// <summary>A peg post on the floor with goggles hung on its pegs. Size: x pegs (1-3), y height.</summary>
        GogglePeg,
        // W-96: homes (each also listed in BatFolkSetBlob.Homes). Woven or hide, on a cord or a root, from a hash of Position.
        /// <summary>A bat folk home pod hung from the ceiling, open below. Position: the anchor; Size: x pod radius, y pod length, z hanger length.</summary>
        Pod,
        /// <summary>
        /// A blind pygmy home: a shallow niche cut into the chamber wall, with a bedroll and a glow-pool or a fire ring.
        /// Position: the middle of the niche's back edge on its floor; Size: x width, y depth (out from the wall), z
        /// ledge height above the cave floor (0 = at walking height). Yaw: the opening (+Z) faces into the chamber.
        /// </summary>
        Niche,
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
        /// <summary>W-96: every home (pods for the bat folk, wall niches for the pygmies), in site order. See BatFolkHomes.</summary>
        public BlobArray<BatFolkHome> Homes;
    }

    /// <summary>
    /// Picks bat folk caves among the baked caves (BATFOLK-PLAN step 1). Runs after CaveBaking and only reads the
    /// caves. A cave qualifies with a chamber of radius >= <see cref="MinChamber"/> at least <see cref="MinDepth"/>
    /// nodes in from the mouth (out of daylight) and a water pool, in that chamber if it has one, else the nearest in
    /// the cave (that pool is the one turned white). Up to one in <see cref="PerCaves"/> qualifying
    /// caves (at least one if any qualify) is picked, lowest first by a scrambled hash of its mouth cell and its own
    /// seed stream, so neither list order nor any other cave's changes reshuffle the picks.
    /// </summary>
    public static partial class BatFolkBaking
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
            // W-61: the settlements take the largest caves (biggest settlement chamber first, the scrambled mouth hash
            // breaking ties), up to MaxSites, so the bat folk live where there is room to hang houses.
            var picks = math.min(qualifying.Count, MaxSites);
            var chosen = new List<(int cave, int node, int pool, uint rank)>(qualifying);
            var radii = new Dictionary<int, float>();
            foreach (var q in chosen)
                radii[q.cave] = set.Nodes[q.node].Radius * set.Caves[q.cave].Profile.x;
            chosen.Sort((a, b) => radii[a.cave] != radii[b.cave] ? radii[b.cave].CompareTo(radii[a.cave])
                : a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.cave.CompareTo(b.cave));
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
            var siteList = new BatFolkSite[chosen.Count];
            for (int i = 0; i < chosen.Count; i++)
            {
                var (c, n, p, _) = chosen[i];
                siteList[i] = new BatFolkSite
                {
                    Cave = c, Key = set.Caves[c].Key, Chamber = n, Centre = set.Nodes[n].Position,
                    Radius = set.Nodes[n].Radius * set.Caves[c].Profile.x, Pool = p
                };
                sites[i] = siteList[i];
                white[p] = 1;
            }
            // W-76: each site's props come from its own RNG and only test against its own props, so the sites place in
            // parallel and are joined in site order (bit-identical to placing them one after another).
            var perSite = new List<BatFolkProp>[chosen.Count];
            var perSiteHomes = new List<BatFolkHome>[chosen.Count];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, chosen.Count, i =>
            {
                ref var caves = ref src.Caves.Value;
                var own = new List<BatFolkProp>();
                var homes = new List<BatFolkHome>();
                PlaceProps(src, ref caves, siteList[i], i, stream, own);
                PlaceKit(src, ref caves, siteList[i], i, stream, own, 0);
                PlaceHomes(src, ref caves, siteList[i], i, stream, own, homes);
                perSite[i] = own;
                perSiteHomes[i] = homes;
            });
            var props = new List<BatFolkProp>();
            var allHomes = new List<BatFolkHome>();
            for (int i = 0; i < perSite.Length; i++)
            {
                // A home's Prop is an index into its own site's list until here: shift it to the joined list.
                foreach (var h in perSiteHomes[i])
                    allHomes.Add(new BatFolkHome { Kind = h.Kind, Site = h.Site, Prop = h.Prop + props.Count, Stand = h.Stand, Door = h.Door, Face = h.Face });
                props.AddRange(perSite[i]);
            }
            var propArray = builder.Allocate(ref root.Props, props.Count);
            for (int i = 0; i < props.Count; i++)
                propArray[i] = props[i];
            var homeArray = builder.Allocate(ref root.Homes, allHomes.Count);
            for (int i = 0; i < allHomes.Count; i++)
                homeArray[i] = allHomes[i];
            var blob = builder.CreateBlobAssetReference<BatFolkSetBlob>(allocator);
            builder.Dispose();
            LastLog = Report(source, ref blob.Value, set.Caves.Length);
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

            // Huts: a pod hung on a silk stalk from the ceiling, its bottom 3-6 m above the floor (W-20: low enough for
            // the lantern to reach, high enough to walk under), clear of the walls round the pod and along the stalk.
            // W-103: each loop runs as speculative batches (Attempts); the tests below read the caves only.
            var src = source;
            var st = site;
            Attempts(ref rng, 60, wantHuts, props, first, null, (ref Random r) =>
            {
                var angle = r.NextFloat(math.PI * 2f);
                var dist = st.Radius * math.sqrt(r.NextFloat(0.04f, 0.6f));
                // W-96: bigger, so they read as woven nests a bat folk can sleep in (drawn as BatFolkMeshes.Pod).
                var radius = r.NextFloat(0.75f, 1.05f);
                var length = radius * r.NextFloat(2.1f, 2.5f);
                var lift = r.NextFloat(3f, 6f);
                var yaw = r.NextFloat(math.PI * 2f);
                var xz = st.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                return (xz, radius, length, lift, yaw);
            }, t =>
            {
                var (xz, radius, length, lift, yaw) = t;
                ref var caves = ref src.Caves.Value;
                var start = new float3(xz.x, st.Centre.y, xz.y);
                if (src.CaveDistance(start) > -0.5f || NearPool(ref caves, caves.Caves[st.Cave], xz, radius + 0.6f))
                    return default;
                var ceiling = Ceiling(src, start);
                var floor = src.FloorUnder(start, out var inCave);
                // A low ceiling lifts the pod to just under it (short stalk), but never below 2.6 m of headroom.
                var stalk = math.max(ceiling - floor - lift - length, 0.35f);
                if (!inCave || ceiling - floor < length + stalk + 2.6f)
                    return default;
                // The pod's top, middle and bottom, each with room for its girth (the top narrows to the stalk).
                var top = ceiling - stalk;
                if (src.CaveDistance(new float3(xz.x, top - 0.15f, xz.y)) > -0.35f ||
                    src.CaveDistance(new float3(xz.x, top - length * 0.55f, xz.y)) > -(radius + 0.2f) ||
                    src.CaveDistance(new float3(xz.x, top - length + 0.1f, xz.y)) > -0.4f)
                    return default;
                // The stalk runs through air all the way (a long one must not pass through a ledge).
                var blocked = false;
                for (float y = ceiling - 0.5f; y > top && !blocked; y -= 1f)
                    blocked = src.CaveDistance(new float3(xz.x, y, xz.y)) > -0.1f;
                if (blocked)
                    return default;
                return new Placed
                {
                    Ok = true, At = xz, Reach = radius,
                    Prop = new BatFolkProp
                    {
                        Position = new float3(xz.x, ceiling, xz.y), Size = new float3(radius, length, stalk), Yaw = yaw,
                        Kind = BatFolkPropKind.Hut, Site = siteIndex
                    }
                };
            });

            // Floor props: a ring round the chamber, on floor flat enough to stand on, with headroom.
            Attempts(ref rng, 80, wantProps, props, first, null, (ref Random r) =>
            {
                var angle = r.NextFloat(math.PI * 2f);
                var dist = st.Radius * r.NextFloat(0.3f, 0.85f);
                var rack = r.NextFloat() < 0.4f;
                var size = rack
                    ? new float3(r.NextFloat(1.3f, 1.8f), r.NextFloat(1.2f, 1.6f), 0.35f)
                    : new float3(r.NextFloat(0.32f, 0.55f), r.NextFloat(0.4f, 0.7f), 0f);
                var yaw = r.NextFloat(math.PI * 2f);
                var xz = st.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                return (xz, rack, size, yaw);
            }, t =>
            {
                var (xz, rack, size, yaw) = t;
                ref var caves = ref src.Caves.Value;
                var reach = rack ? size.x * 0.5f + 0.15f : size.x;
                if (NearPool(ref caves, caves.Caves[st.Cave], xz, reach + 0.8f) || OnPath(ref caves, st.Chamber, xz, reach + PathClear))
                    return default;
                var floor = src.FloorUnder(new float3(xz.x, st.Centre.y + 1f, xz.y), out var inCave);
                if (!inCave || src.CaveDistance(new float3(xz.x, floor + 0.3f, xz.y)) > -0.2f)
                    return default;
                // Flat: the floor under each corner of its footprint within 30 cm, each with air above it.
                var flat = true;
                for (int k = 0; k < 4 && flat; k++)
                {
                    var o = new float2(math.cos(yaw + k * math.PI * 0.5f), math.sin(yaw + k * math.PI * 0.5f)) * reach;
                    var f = src.FloorUnder(new float3(xz.x + o.x, floor + 1f, xz.y + o.y), out var inside);
                    flat = inside && math.abs(f - floor) < 0.3f &&
                           src.CaveDistance(new float3(xz.x + o.x, f + size.y * 0.6f, xz.y + o.y)) < -0.15f;
                }
                if (!flat || src.CaveDistance(new float3(xz.x, floor + size.y + 0.6f, xz.y)) > -0.5f)
                    return default;
                return new Placed
                {
                    Ok = true, At = xz, Reach = reach,
                    Prop = new BatFolkProp
                    {
                        // Sunk a little, so a gentle slope never shows a gap under it.
                        Position = new float3(xz.x, floor - 0.08f, xz.y), Size = size, Yaw = yaw,
                        Kind = rack ? BatFolkPropKind.Rack : BatFolkPropKind.Basket, Site = siteIndex
                    }
                };
            });
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
            var high = High(kind);
            for (int i = first; i < props.Count; i++)
            {
                var p = props[i];
                if (High(p.Kind) != high)
                    continue;
                if (math.distance(p.Position.xz, xz) < reach + Reach(p) + (high ? 0.9f : 0.7f))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------ W-103 speculative attempts

        /// <summary>One attempt's outcome: the thing to place (and its home, if it is one) if every rock test passed.</summary>
        struct Placed
        {
            public bool Ok, HasHome;
            public BatFolkProp Prop;
            public BatFolkHome Home;
            /// <summary>Where and how far it reaches for the crowding test (a niche crowds from its middle, not its origin).</summary>
            public float2 At;
            public float Reach;
        }

        delegate T Draw<T>(ref Random rng);

        /// <summary>Attempts that run ahead in parallel: this many are drawn (sequentially, from the one RNG), tested at once.</summary>
        const int Speculate = 24;

        /// <summary>
        /// W-103 (#102): a placement loop, "up to <paramref name="attempts"/> tries until <paramref name="want"/> are
        /// placed", run faster with the same result. Every try draws all its numbers first, then runs rock and pool
        /// tests that read only the caves, and only then the crowding test against what is already placed. So a
        /// batch of tries is drawn in order, their rock tests run in parallel, and the crowding test and placing run
        /// in order again. When enough are placed mid-batch the RNG goes back to where that try began, so the next
        /// loop on the same RNG draws exactly what it drew before. Bit-identical to the one-at-a-time loop.
        /// </summary>
        static void Attempts<T>(ref Random rng, int attempts, int want, List<BatFolkProp> props, int first, List<BatFolkHome> homes,
            Draw<T> draw, System.Func<T, Placed> test)
        {
            var tries = new T[Speculate];
            var before = new Random[Speculate];
            var results = new Placed[Speculate];
            for (int attempt = 0, n = 0; attempt < attempts && n < want;)
            {
                var k = math.min(Speculate, attempts - attempt);
                var r = rng;
                for (int i = 0; i < k; i++)
                {
                    before[i] = r;
                    tries[i] = draw(ref r);
                }
                System.Threading.Tasks.Parallel.For(0, k, i => results[i] = test(tries[i]));
                rng = r;
                for (int i = 0; i < k; i++, attempt++)
                {
                    if (n >= want)
                    {
                        rng = before[i];
                        return;
                    }
                    var p = results[i];
                    if (!p.Ok || Crowded(props, first, p.At, p.Reach, p.Prop.Kind))
                        continue;
                    if (p.HasHome)
                    {
                        var h = p.Home;
                        h.Prop = props.Count;
                        homes.Add(h);
                    }
                    props.Add(p.Prop);
                    n++;
                }
            }
        }

        /// <summary>Things up off the floor (hung or on the wall) only crowd each other; floor things likewise.</summary>
        static bool High(BatFolkPropKind k) => k == BatFolkPropKind.Hut || k == BatFolkPropKind.WallHouse ||
                                               k == BatFolkPropKind.CeilingHouse || k == BatFolkPropKind.RoostFrame || k == BatFolkPropKind.EchoChime ||
                                               k == BatFolkPropKind.Pod;

        /// <summary>How far a placed thing reaches out from its Position in plan (m).</summary>
        static float Reach(in BatFolkProp p) => p.Kind switch
        {
            BatFolkPropKind.Rack => p.Size.x * 0.5f + 0.15f,
            BatFolkPropKind.WallHouse or BatFolkPropKind.CeilingHouse => 0.5f * math.max(p.Size.x, p.Size.y) + 0.6f,
            BatFolkPropKind.RoostFrame or BatFolkPropKind.Hammock => p.Size.x * 0.5f + 0.1f,
            BatFolkPropKind.EchoChime => p.Size.x + 0.2f,
            BatFolkPropKind.GogglePeg => 0.45f,
            BatFolkPropKind.Niche => p.Size.x * 0.5f + NicheJamb,
            _ => p.Size.x
        };

        // ------------------------------------------------------------------------------------ W-61 details

        public const int MaxSites = 3, MinDetails = 6, MaxDetails = 12;

        /// <summary>
        /// Which small things a bat folk settlement has and how often (data: add a kind here, a case in PlaceDetail and
        /// in BatFolkMeshes). Each settlement scales every weight by its own draw, so one is full of chimes and
        /// hammocks and the next of roost frames and guano heaps.
        /// </summary>
        public static readonly (BatFolkPropKind Kind, float Weight)[] Details =
        {
            // W-96: no more hammocks; the bat folk sleep in their ceiling pods (PlaceHomes).
            (BatFolkPropKind.RoostFrame, 1f), (BatFolkPropKind.GuanoStone, 1.4f), (BatFolkPropKind.EchoChime, 0.8f),
            (BatFolkPropKind.GogglePeg, 0.7f),
        };

        /// <summary>
        /// W-61 (#35): details, after the huts and floor props and from the site's second RNG, so the W-19 huts and
        /// props come out the same: 6-12 picked by the settlement's own weights. No light sources of any kind.
        /// W-96: the W-61 kit houses are gone (Zach: bat folk homes must not look like houses); they live in pods.
        /// </summary>
        static void PlaceKit(in TerrainSource source, ref CaveSetBlob set, in BatFolkSite site, int siteIndex, uint stream,
            List<BatFolkProp> props, int first)
        {
            var rng = new Random(BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble(site.Key * 0x2C1B3C6Du ^ 0x61u)) | 1u);
            ref var cave = ref set.Caves[site.Cave];

            // Details: this settlement's weights, then 6-12 picks.
            var weights = new float[Details.Length];
            var total = 0f;
            for (int i = 0; i < Details.Length; i++)
                total += weights[i] = Details[i].Weight * rng.NextFloat(0.3f, 1.7f);
            var want = rng.NextInt(MinDetails, MaxDetails + 1);
            var src = source;
            var st = site;
            Attempts(ref rng, 240, want, props, first, null, (ref Random r) =>
            {
                var pick = r.NextFloat(total);
                var kind = Details[Details.Length - 1].Kind;
                for (int i = 0; i < Details.Length; i++)
                    if ((pick -= weights[i]) < 0f)
                    {
                        kind = Details[i].Kind;
                        break;
                    }
                return DrawDetail(st, kind, ref r);
            }, d => TestDetail(src, st, siteIndex, d));
        }

        /// <summary>A detail try's numbers, all drawn up front (W-103: so tries can be tested ahead in parallel).</summary>
        struct DetailTry
        {
            public BatFolkPropKind Kind;
            public float2 Xz;
            public float3 Size;
            public float Yaw, Lift;
        }

        /// <summary>The numbers for one detail of this kind: hung from the ceiling or on flat floor.</summary>
        static DetailTry DrawDetail(in BatFolkSite site, BatFolkPropKind kind, ref Random rng)
        {
            var angle = rng.NextFloat(math.PI * 2f);
            var yaw = rng.NextFloat(math.PI * 2f);
            if (kind == BatFolkPropKind.RoostFrame || kind == BatFolkPropKind.EchoChime)
            {
                var size = kind == BatFolkPropKind.RoostFrame
                    ? new float3(rng.NextFloat(1.2f, 2f), rng.NextFloat(0.9f, 1.5f), 0f)
                    : new float3(rng.NextFloat(0.25f, 0.45f), rng.NextFloat(0.6f, 1.1f), 0f);
                var lift = rng.NextFloat(2.8f, 5f);
                var xz = site.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * site.Radius * math.sqrt(rng.NextFloat(0.03f, 0.7f));
                return new DetailTry { Kind = kind, Xz = xz, Size = size, Yaw = yaw, Lift = lift };
            }
            {
                var size = kind switch
                {
                    BatFolkPropKind.GuanoStone => new float3(rng.NextFloat(0.4f, 0.9f), rng.NextFloat(0.2f, 0.45f), 0f),
                    BatFolkPropKind.Hammock => new float3(rng.NextFloat(2f, 2.6f), rng.NextFloat(1.3f, 1.6f), 0f),
                    _ => new float3(1 + rng.NextInt(3), rng.NextFloat(1.4f, 1.8f), 0f)
                };
                var xz = site.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * site.Radius * rng.NextFloat(0.25f, 0.9f);
                return new DetailTry { Kind = kind, Xz = xz, Size = size, Yaw = yaw };
            }
        }

        /// <summary>Whether a detail fits where it was drawn (rock, pools and the path only; crowding is tested after).</summary>
        static Placed TestDetail(in TerrainSource source, in BatFolkSite site, int siteIndex, DetailTry d)
        {
            ref var set = ref source.Caves.Value;
            ref var cave = ref set.Caves[site.Cave];
            var (kind, xz, size, yaw) = (d.Kind, d.Xz, d.Size, d.Yaw);
            if (kind == BatFolkPropKind.RoostFrame || kind == BatFolkPropKind.EchoChime)
            {
                var start = new float3(xz.x, site.Centre.y, xz.y);
                var reach = kind == BatFolkPropKind.RoostFrame ? size.x * 0.5f + 0.1f : size.x + 0.2f;
                if (source.CaveDistance(start) > -0.5f || NearPool(ref set, cave, xz, reach + 0.3f))
                    return default;
                var ceiling = Ceiling(source, start);
                var floor = source.FloorUnder(start, out var inCave);
                var stalk = math.max(ceiling - floor - d.Lift - size.y, 0.3f);
                if (!inCave || ceiling - floor < size.y + stalk + 2.5f)
                    return default;
                // The cord needs only a thin column of air; the frame or ring at its end needs its full reach.
                for (float y = ceiling - 0.4f; y > ceiling - stalk - size.y; y -= 0.5f)
                    if (source.CaveDistance(new float3(xz.x, y, xz.y)) > (y > ceiling - stalk + 0.3f ? -0.15f : -(reach + 0.05f)))
                        return default;
                size.z = stalk;
                return new Placed
                {
                    Ok = true, At = xz, Reach = reach,
                    Prop = new BatFolkProp { Position = new float3(xz.x, ceiling, xz.y), Size = size, Yaw = yaw, Kind = kind, Site = siteIndex }
                };
            }
            {
                var reach = kind == BatFolkPropKind.Hammock ? size.x * 0.5f + 0.1f : kind == BatFolkPropKind.GogglePeg ? 0.45f : size.x;
                if (NearPool(ref set, cave, xz, reach + 0.8f) || OnPath(ref set, site.Chamber, xz, reach + 1.2f))
                    return default;
                var floor = source.FloorUnder(new float3(xz.x, site.Centre.y + 1f, xz.y), out var inCave);
                if (!inCave || source.CaveDistance(new float3(xz.x, floor + 0.3f, xz.y)) > -0.2f)
                    return default;
                for (int k = 0; k < 4; k++)
                {
                    var o = new float2(math.cos(yaw + k * math.PI * 0.5f), math.sin(yaw + k * math.PI * 0.5f)) * reach;
                    var f = source.FloorUnder(new float3(xz.x + o.x, floor + 1f, xz.y + o.y), out var inside);
                    if (!inside || math.abs(f - floor) > 0.45f || source.CaveDistance(new float3(xz.x + o.x, f + size.y * 0.6f, xz.y + o.y)) > -0.15f)
                        return default;
                }
                if (source.CaveDistance(new float3(xz.x, floor + size.y + 0.5f, xz.y)) > -0.4f)
                    return default;
                return new Placed
                {
                    Ok = true, At = xz, Reach = reach,
                    Prop = new BatFolkProp { Position = new float3(xz.x, floor - 0.08f, xz.y), Size = size, Yaw = yaw, Kind = kind, Site = siteIndex }
                };
            }
        }

        /// <summary>Whether a box (building space: +Z turned by <paramref name="yaw"/>, y up from <paramref name="origin"/>) is all in cave air, on a 4x4x4 grid.</summary>
        static bool BoxInAir(in TerrainSource source, float3 origin, float yaw, float width, float height, float depth)
        {
            var turn = quaternion.RotateY(yaw);
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < 4; k++)
                    {
                        var local = new float3((i / 3f - 0.5f) * width, j / 3f * height, (k / 3f - 0.5f) * depth);
                        if (source.CaveDistance(origin + math.mul(turn, local)) > -0.1f)
                            return false;
                    }
            return true;
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

        static string Report(in TerrainSource source, ref BatFolkSetBlob b, int caves)
        {
            var sb = new StringBuilder($"[batfolk] caves {b.Qualifying}, picked {b.Sites.Length} (of {caves} caves; biggest deep chamber {b.BiggestChamber:F1} m)");
            for (int i = 0; i < b.Sites.Length; i++)
            {
                ref var s = ref b.Sites[i];
                int huts = 0, props = 0, houses = 0, details = 0, pods = 0, niches = 0, ledges = 0;
                // Each hut's pod bottom above the floor under it (m), so its height can be checked from the log.
                var heights = new StringBuilder();
                for (int k = 0; k < b.Props.Length; k++)
                    if (b.Props[k].Site == i)
                    {
                        ref var p = ref b.Props[k];
                        // W-96: none are placed any more; still counted so the log proves it.
                        if (p.Kind == BatFolkPropKind.WallHouse || p.Kind == BatFolkPropKind.CeilingHouse) { houses++; continue; }
                        if (p.Kind == BatFolkPropKind.Pod) { pods++; continue; }
                        if (p.Kind == BatFolkPropKind.Niche) { niches++; if (p.Size.z > 0f) ledges++; continue; }
                        if (p.Kind >= BatFolkPropKind.RoostFrame) { details++; continue; }
                        if (p.Kind != BatFolkPropKind.Hut) { props++; continue; }
                        huts++;
                        var bottom = p.Position.y - p.Size.z - p.Size.y;
                        var floor = source.FloorUnder(new float3(p.Position.x, bottom - 0.1f, p.Position.z), out _);
                        heights.Append(heights.Length == 0 ? "" : "/").Append((bottom - floor).ToString("F1"));
                    }
                sb.Append($"; cave {s.Cave} chamber {s.Centre.x:F0},{s.Centre.y:F0},{s.Centre.z:F0} r {s.Radius:F1} pool {s.Pool} huts {huts} props {props} houses {houses} details {details} pods {pods} niches {niches} (ledged {ledges}) hut bottoms {heights} m");
            }
            sb.Append($"; homes {b.Homes.Length}");
            return sb.ToString();
        }

        /// <summary>The log line for a baked world (island v1 bakes in the editor, so the player logs it from the blob).</summary>
        public static string Report(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated)
                return "[batfolk] none baked";
            return Report(source, ref source.BatFolk.Value,source.Caves.IsCreated ? source.Caves.Value.Caves.Length : 0);
        }
    }
}
