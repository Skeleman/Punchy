using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>W-172 row 7: the kinds of cave wall detail, one mesh and one material per kind per cave.</summary>
    public enum CaveWallKind : byte
    {
        Moss, Fungi, GlowFungi, Carving, Fossil, Ossuary, Bones, Frost, Icicles, Salt, Flowstone, TideLine, Soot
    }

    /// <summary>What a cave's wall details are chosen from: the climate over its mouth, its rock, what is built in it and
    /// the folk who live nearest.</summary>
    public struct CaveWallSite
    {
        public float Cold, Heat, Rain;
        public bool Forest, Volcanic, Mine, Water, Streams, Camps;
        /// <summary>The nearest settlement's folk within CultureReach (0: none).</summary>
        public FolkCulture Culture;
        public CaveRockKind Rock;
        public int Tunnel;
        /// <summary>A ruin room (index into the cave set's Rooms), -1 with none.</summary>
        public int Ruin;
        /// <summary>How far the cave's furthest node is from its mouth (m).</summary>
        public float Depth;
    }

    /// <summary>
    /// W-172 row 7 (#262): small details on cave walls that fit the place: moss sheets near mouths and water, bracket
    /// fungi in damp caves (a glowing kind deep down), carvings in the nearest folk's style in tunnels, mines and ruins,
    /// fossils and bones in sedimentary rock and badlands, ossuary niches by ruin doors, frost and icicles in the cold,
    /// salt crust in hot deserts, flowstone in limestone, a tide line along cave streams and soot over tunnel camps.
    /// Each cave rolls its own few kinds from its key (never every kind, never every cave), and every place is found
    /// on the cave's own walls from the cave field, so host and clients build the same details. Drawn only
    /// (CaveSystem.WallDetails: no collision), built one placement at a time.
    /// </summary>
    public static class CaveWallDetails
    {
        public const int Kinds = 13;
        /// <summary>The most kinds one cave takes.</summary>
        public const int MaxKinds = 4;
        /// <summary>How far a settlement's folk carve (m from its edge).</summary>
        public const float CultureReach = 3000f;
        /// <summary>The least distance between two places of one kind in a cave (m).</summary>
        const float Spacing = 5f;
        /// <summary>How far from the mouth daylight reaches a detail (m: the colour alpha the shader reads).</summary>
        const float DaylightReach = 18f;

        public static readonly string[] Names =
            { "Moss", "Fungi", "GlowFungi", "Carving", "Fossil", "Ossuary", "Bones", "Frost", "Icicles", "Salt", "Flowstone", "TideLine", "Soot" };
        /// <summary>Each kind's size for its screen-size reach (m, DetailPolicy.ExtraReach with Pixels).</summary>
        public static readonly float[] Size = { 2f, 0.5f, 0.5f, 1f, 0.6f, 1.5f, 0.5f, 2f, 0.6f, 1.5f, 1.5f, 2f, 2.5f };
        public const float Pixels = 3f;
        /// <summary>How likely an eligible kind is taken in a cave.</summary>
        static readonly float[] Chance = { 0.8f, 0.75f, 0.6f, 0.5f, 0.6f, 0.7f, 0.5f, 0.9f, 0.8f, 0.7f, 0.8f, 0.6f, 0.7f };
        /// <summary>Places per cave by kind (min, max).</summary>
        internal static readonly int2[] Places =
        {
            new int2(3, 6), new int2(2, 4), new int2(2, 4), new int2(1, 2), new int2(1, 3), new int2(1, 1), new int2(2, 4),
            new int2(3, 6), new int2(2, 4), new int2(3, 5), new int2(2, 4), new int2(0, 0), new int2(0, 0)
        };
        /// <summary>The light of a glowing fungus cluster (rgb, w reach in m).</summary>
        public static readonly Vector4 GlowLight = new Vector4(0.25f, 0.85f, 0.75f, 5f);

        public static void Colours(CaveWallKind kind, out Color rock, out Color dark)
        {
            switch (kind)
            {
                case CaveWallKind.Moss: rock = new Color(0.24f, 0.38f, 0.13f); dark = new Color(0.1f, 0.19f, 0.06f); break;
                case CaveWallKind.Fungi: rock = new Color(0.86f, 0.73f, 0.53f); dark = new Color(0.6f, 0.47f, 0.3f); break;
                case CaveWallKind.GlowFungi: rock = new Color(0.5f, 1f, 0.92f); dark = new Color(0.22f, 0.75f, 0.68f); break;
                case CaveWallKind.Carving: rock = new Color(0.56f, 0.53f, 0.48f); dark = new Color(0.3f, 0.28f, 0.25f); break;
                case CaveWallKind.Fossil: rock = new Color(0.82f, 0.76f, 0.62f); dark = new Color(0.5f, 0.45f, 0.35f); break;
                case CaveWallKind.Ossuary: rock = new Color(0.8f, 0.74f, 0.62f); dark = new Color(0.45f, 0.4f, 0.32f); break;
                case CaveWallKind.Bones: rock = new Color(0.86f, 0.81f, 0.69f); dark = new Color(0.56f, 0.5f, 0.4f); break;
                case CaveWallKind.Frost: rock = new Color(0.86f, 0.93f, 1f); dark = new Color(0.62f, 0.72f, 0.84f); break;
                case CaveWallKind.Icicles: rock = new Color(0.76f, 0.88f, 1f); dark = new Color(0.5f, 0.66f, 0.86f); break;
                case CaveWallKind.Salt: rock = new Color(0.93f, 0.91f, 0.87f); dark = new Color(0.72f, 0.7f, 0.66f); break;
                case CaveWallKind.Flowstone: rock = new Color(0.84f, 0.71f, 0.52f); dark = new Color(0.56f, 0.42f, 0.28f); break;
                case CaveWallKind.TideLine: rock = new Color(0.27f, 0.22f, 0.15f); dark = new Color(0.15f, 0.12f, 0.08f); break;
                default: rock = new Color(0.07f, 0.065f, 0.06f); dark = new Color(0.03f, 0.03f, 0.03f); break;
            }
        }

        /// <summary>
        /// Light a kind gives off everywhere (Cave.shader's _OwnLight). Fungi grow deep (more than 8 m in), where only
        /// the cave's dim ambient reaches: without a soft glow of their own their caps read black against lit rock.
        /// </summary>
        public static Color OwnLight(CaveWallKind kind) =>
            kind == CaveWallKind.Fungi ? new Color(0.34f, 0.27f, 0.17f) : Color.clear;

        // ------------------------------------------------------------------------------------------- choosing

        public static CaveWallSite SiteOf(in TerrainSource source, BiomePalette palette, List<CaveStyle> styles, int caveIndex)
        {
            ref var set = ref source.Caves.Value;
            var cave = set.Caves[caveIndex];
            var site = new CaveWallSite { Tunnel = cave.Tunnel, Ruin = -1 };
            source.SampleBiomes(cave.Mouth.xz, out var surface, out _);
            float total = 0f, forest = 0f, volcanic = 0f;
            for (int k = 0; k < 4; k++)
            {
                var id = surface.Ids[k];
                var w = surface.Weights[k];
                if (w <= 0f || palette == null || id < 0 || id >= palette.Biomes.Length || palette.Biomes[id] == null)
                    continue;
                var b = palette.Biomes[id];
                site.Cold += b.Cold * w;
                site.Heat += b.Heat * w;
                site.Rain += b.Rainfall * w;
                if (b.HasTag("forest"))
                    forest += w;
                if (b.HasTag("ridge-volcano") || b.HasTag("burning"))
                    volcanic += w;
                total += w;
            }
            if (total > 0f)
            {
                site.Cold /= total; site.Heat /= total; site.Rain /= total;
                site.Forest = forest / total > 0.4f;
                site.Volcanic = volcanic / total > 0.4f;
            }
            var style = cave.Style > 0 && styles != null && cave.Style - 1 < styles.Count ? styles[cave.Style - 1] : null;
            if (style != null)
            {
                site.Rock = style.RockKind;
                site.Mine = style.Frame != null;
            }
            if (site.Rock == CaveRockKind.Volcanic)
                site.Volcanic = true;
            for (int r = cave.FirstRoom; r < cave.FirstRoom + cave.RoomCount; r++)
                if (set.Rooms[r].Type == (int)CaveRoomType.Ruin)
                    site.Ruin = r;
            site.Streams = cave.StreamCount > 0;
            site.Water = site.Streams;
            for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount; p++)
                if (set.Pools[p].Lava <= 0f)
                    site.Water = true;
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                site.Depth = math.max(site.Depth, math.distance(set.Nodes[n].Position, cave.Mouth));
            site.Culture = NearestCulture(source, palette, cave.Mouth.xz);
            return site;
        }

        static FolkCulture NearestCulture(in TerrainSource source, BiomePalette palette, float2 xz)
        {
            if (!source.Settlements.IsCreated || !source.Landmarks.IsCreated || palette == null || palette.Landmarks == null)
                return 0;
            ref var towns = ref source.Settlements.Value.Settlements;
            ref var marks = ref source.Landmarks.Value.Instances;
            FolkCulture best = 0;
            var bestDistance = CultureReach;
            for (int i = 0; i < towns.Length; i++)
            {
                var d = math.distance(towns[i].Centre, xz) - towns[i].Radius;
                if (d >= bestDistance || towns[i].Landmark < 0 || towns[i].Landmark >= marks.Length)
                    continue;
                var type = marks[towns[i].Landmark].Type;
                if (type < 0 || type >= palette.Landmarks.Length || palette.Landmarks[type] == null || palette.Landmarks[type].Settlement == null)
                    continue;
                best = palette.Landmarks[type].Settlement.Culture;
                bestDistance = d;
            }
            return best;
        }

        /// <summary>Whether a kind suits a site at all (before the cave's roll).</summary>
        public static bool Eligible(in CaveWallSite s, CaveWallKind kind)
        {
            var natural = s.Tunnel == 0;
            var cold = s.Cold >= 0.5f || s.Rock == CaveRockKind.Ice;
            var hot = s.Heat >= 0.7f && s.Rain <= 0.2f;
            var badlands = s.Heat >= 0.6f && s.Rain <= 0.2f;
            var damp = (s.Rain >= 0.6f || s.Forest) && !cold && !s.Volcanic;
            switch (kind)
            {
                case CaveWallKind.Moss: return !cold && !s.Volcanic && !hot && (s.Rain >= 0.45f && s.Heat < 0.6f || s.Water);
                case CaveWallKind.Fungi: return damp && natural;
                case CaveWallKind.GlowFungi: return damp && natural && s.Depth >= 45f;
                case CaveWallKind.Carving: return s.Tunnel > 0 || s.Ruin >= 0 || s.Mine;
                case CaveWallKind.Fossil: return natural && (s.Rock == CaveRockKind.Sedimentary || badlands);
                case CaveWallKind.Ossuary: return s.Ruin >= 0;
                case CaveWallKind.Bones: return badlands || s.Rock == CaveRockKind.Sedimentary || s.Ruin >= 0;
                case CaveWallKind.Frost: return cold && !s.Volcanic;
                case CaveWallKind.Icicles: return cold && !s.Volcanic;
                case CaveWallKind.Salt: return hot && s.Rock != CaveRockKind.Ice;
                case CaveWallKind.Flowstone: return natural && s.Rock == CaveRockKind.Limestone;
                case CaveWallKind.TideLine: return natural && s.Streams;
                case CaveWallKind.Soot: return s.Tunnel > 0 && s.Camps;
            }
            return false;
        }

        /// <summary>The kinds a cave takes (a bit per kind): each eligible kind rolls from the cave's key, and only the
        /// MaxKinds rolling furthest under their chance stay.</summary>
        public static int Choose(in CaveWallSite s, uint key)
        {
            var picks = new List<(float margin, int kind)>();
            for (int k = 0; k < Kinds; k++)
            {
                if (!Eligible(s, (CaveWallKind)k))
                    continue;
                var roll = U(Hash(key, k, 0, 0), 0);
                if (roll < Chance[k])
                    picks.Add((roll / Chance[k], k));
            }
            picks.Sort((a, b) => a.margin.CompareTo(b.margin));
            var mask = 0;
            for (int i = 0; i < picks.Count && i < MaxKinds; i++)
                mask |= 1 << picks[i].kind;
            return mask;
        }

        internal static uint Hash(uint key, int kind, int index, int attempt) =>
            BiomeSampling.Scramble(key ^ BiomeSampling.Scramble((uint)kind * 0x9E3779B9u + (uint)index * 0x85EBCA6Bu + (uint)attempt * 0xC2B2AE35u + 0x5bd1e995u));

        internal static float U(uint h, int k) => (BiomeSampling.Scramble(h + (uint)k * 0x27D4EB2Du) & 0xFFFFFF) / 16777216f;

        // ------------------------------------------------------------------------------------------- counting

        /// <summary>
        /// Plans every cave's details (no drawing) and tallies them: per kind, the places and the caves that have it, and
        /// the hanging roots (W-172 row 5's ceiling flora) for the table. For reports and tests.
        /// </summary>
        public static string Tally(in TerrainSource source, BiomePalette palette, out int[] places, out int[] caves)
        {
            places = new int[Kinds];
            caves = new int[Kinds];
            if (!source.Caves.IsCreated)
                return "no caves";
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            ref var set = ref source.Caves.Value;
            for (int i = 0; i < set.Caves.Length; i++)
            {
                var build = new CaveWallBuild(source, palette, styles, i);
                while (!build.Step(source, false)) { }
                for (int k = 0; k < Kinds; k++)
                {
                    places[k] += build.Counts[k];
                    if (build.Counts[k] > 0)
                        caves[k]++;
                }
            }
            var species = CaveFloraTable.Collect(palette.Landmarks);
            var hanging = new HashSet<int>();
            foreach (var style in styles)
                foreach (var f in style.Flora)
                    if (f != null && f.Hanging && f.Species != null)
                        hanging.Add(species.IndexOf(f.Species));
            var roots = 0;
            for (int i = 0; i < set.Plants.Length; i++)
                if (hanging.Contains(set.Plants[i].Species))
                    roots++;
            var text = new System.Text.StringBuilder($"{set.Caves.Length} caves;");
            for (int k = 0; k < Kinds; k++)
                text.Append($" {Names[k]} {places[k]} in {caves[k]};");
            text.Append($" Roots {roots} plants");
            return text.ToString();
        }
    }

    /// <summary>
    /// One cave's wall details being built: Step runs one placement a frame or so, then turns each kind's geometry into
    /// a mesh. Placements and their order come only from the cave set and the cave's key.
    /// </summary>
    public class CaveWallBuild
    {
        public readonly int Cave;
        public readonly CaveWallSite Site;
        public readonly int Mask;
        public readonly Mesh[] Meshes = new Mesh[CaveWallDetails.Kinds];
        public readonly int[] Counts = new int[CaveWallDetails.Kinds];
        /// <summary>Glowing fungus clusters, for their lights.</summary>
        public readonly List<Vector3> Glows = new List<Vector3>();
        /// <summary>Every place made: kind, where and the wall's normal (for shots and tests).</summary>
        public readonly List<(CaveWallKind kind, float3 position, float3 normal)> Placed = new List<(CaveWallKind, float3, float3)>();

        readonly List<(CaveWallKind kind, int index)> jobs = new List<(CaveWallKind, int)>();
        readonly CaveWallMesh[] geometry = new CaveWallMesh[CaveWallDetails.Kinds];
        readonly List<int> nodes = new List<int>();
        readonly List<float4> camps = new List<float4>();
        readonly uint key;
        readonly float3 mouth;
        int next;
        bool done;

        public CaveWallBuild(in TerrainSource source, BiomePalette palette, List<CaveStyle> styles, int cave)
        {
            Cave = cave;
            ref var set = ref source.Caves.Value;
            var c = set.Caves[cave];
            key = c.Key;
            mouth = c.Mouth;
            for (int n = c.FirstNode; n < c.FirstNode + c.NodeCount; n++)
                nodes.Add(n);
            var site = CaveWallDetails.SiteOf(source, palette, styles, cave);
            if (c.Tunnel > 0)
                foreach (var camp in CaveBaking.TunnelCamps(ref set))
                {
                    CaveSampling.Distance(ref set, camp.xyz + new float3(0f, 0.8f, 0f), out var owner);
                    if (owner == cave)
                        camps.Add(camp);
                }
            site.Camps = camps.Count > 0;
            Site = site;
            Mask = CaveWallDetails.Choose(Site, key);
            for (int k = 0; k < CaveWallDetails.Kinds; k++)
            {
                if ((Mask & (1 << k)) == 0)
                    continue;
                var kind = (CaveWallKind)k;
                var range = PlacesOf(kind, ref set, c);
                var count = range.x + (int)(CaveWallDetails.U(CaveWallDetails.Hash(key, k, 0, 1), 0) * (range.y - range.x + 1));
                for (int i = 0; i < count; i++)
                    jobs.Add((kind, i));
            }
        }

        int2 PlacesOf(CaveWallKind kind, ref CaveSetBlob set, in CaveData c)
        {
            switch (kind)
            {
                case CaveWallKind.TideLine: return new int2(c.StreamCount, c.StreamCount);
                case CaveWallKind.Soot: return new int2(camps.Count, camps.Count);
                default:
                {
                    var r = CaveWallDetails.Places[(int)kind];
                    // Short caves take fewer.
                    if (c.NodeCount < 6)
                        r = math.max(1, r / 2);
                    return r;
                }
            }
        }

        /// <summary>Runs the next placement (or finishes); true once done. `meshes` false skips the meshes (counting).</summary>
        public bool Step(in TerrainSource source, bool meshes = true)
        {
            if (done)
                return true;
            if (next < jobs.Count)
            {
                var (kind, index) = jobs[next++];
                if (Place(source, kind, index))
                    Counts[(int)kind]++;
                return false;
            }
            done = true;
            if (meshes)
                for (int k = 0; k < CaveWallDetails.Kinds; k++)
                    if (geometry[k] != null)
                        Meshes[k] = geometry[k].ToMesh("Cave wall " + CaveWallDetails.Names[k] + " " + Cave);
            for (int k = 0; k < geometry.Length; k++)
                geometry[k] = null;
            return true;
        }

        public void Release()
        {
            for (int k = 0; k < Meshes.Length; k++)
                if (Meshes[k] != null)
                {
                    Object.Destroy(Meshes[k]);
                    Meshes[k] = null;
                }
        }

        CaveWallMesh Geometry(CaveWallKind kind) => geometry[(int)kind] ??= new CaveWallMesh(mouth);

        // ------------------------------------------------------------------------------------------- places

        enum Aim { Wall, Ceiling, Floor, LowWall }

        bool Place(in TerrainSource source, CaveWallKind kind, int index)
        {
            ref var set = ref source.Caves.Value;
            var g = Geometry(kind);
            var h = CaveWallDetails.Hash(key, (int)kind, index + 1, 7);
            switch (kind)
            {
                case CaveWallKind.TideLine:
                    return TideLine(source, set.Caves[Cave].FirstStream + index, g);
                case CaveWallKind.Soot:
                {
                    var camp = camps[index];
                    if (!Cast(source, camp.xyz + new float3(0f, 1f, 0f), new float3(0f, 1f, 0f), 12f, out var hit, out var n) || n.y > -0.3f)
                        return false;
                    g.Sheet(source, Cave, hit, n, math.max(2f, camp.w * 0.8f), 1f, h, 0.05f);
                    Placed.Add((kind, hit, n));
                    return true;
                }
            }
            var aim = kind switch
            {
                CaveWallKind.Icicles => Aim.Ceiling,
                CaveWallKind.Bones => Aim.Floor,
                CaveWallKind.Salt => Aim.LowWall,
                _ => Aim.Wall
            };
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var ha = CaveWallDetails.Hash(key, (int)kind, index + 1, attempt + 11);
                if (!From(source, kind, ha, out var from))
                    continue;
                var dir = Direction(aim, ha, from, kind);
                if (!Cast(source, from, dir, 25f, out var hit, out var n))
                    continue;
                if (aim == Aim.Ceiling && n.y > -0.4f || aim == Aim.Floor && n.y < 0.6f || (aim == Aim.Wall || aim == Aim.LowWall) && math.abs(n.y) > 0.65f)
                    continue;
                if (!Spaced(kind, hit))
                    continue;
                Make(source, kind, g, hit, n, ha);
                Placed.Add((kind, hit, n));
                return true;
            }
            return false;
        }

        /// <summary>Where a place's ray starts: a node of the cave suited to the kind (near the mouth, near water, deep
        /// down, by a ruin's door), or false.</summary>
        bool From(in TerrainSource source, CaveWallKind kind, uint h, out float3 from)
        {
            ref var set = ref source.Caves.Value;
            from = default;
            if (kind == CaveWallKind.Ossuary || kind == CaveWallKind.Carving && Site.Ruin >= 0 && Site.Tunnel == 0 && !Site.Mine)
            {
                // By a ruin's door, in the passage just outside it.
                var room = set.Rooms[Site.Ruin];
                from = room.World(new float3((CaveWallDetails.U(h, 1) - 0.5f) * 1.2f, 1.4f, -1.2f - CaveWallDetails.U(h, 2) * 2f));
                return CaveSampling.Distance(ref set, from, out var c) < -0.3f && c == Cave;
            }
            var near = Site.Depth;
            int pick = -1;
            for (int tries = 0; tries < 8 && pick < 0; tries++)
            {
                var n = nodes[(int)(CaveWallDetails.U(h, 10 + tries) * nodes.Count) % nodes.Count];
                var depth = math.distance(set.Nodes[n].Position, mouth);
                var ok = kind switch
                {
                    CaveWallKind.Moss => depth < 25f || NearWater(ref set, set.Nodes[n].Position),
                    CaveWallKind.Frost or CaveWallKind.Icicles => depth < 30f,
                    CaveWallKind.Fungi => depth > 8f,
                    CaveWallKind.GlowFungi => depth > math.max(30f, near * 0.45f),
                    CaveWallKind.Salt => depth < 40f,
                    _ => depth > 5f
                };
                if (ok)
                    pick = n;
            }
            if (pick < 0)
                return false;
            var node = set.Nodes[pick];
            var jitter = new float3(CaveWallDetails.U(h, 3) - 0.5f, (CaveWallDetails.U(h, 4) - 0.5f) * 0.5f, CaveWallDetails.U(h, 5) - 0.5f) * node.Radius * 0.6f;
            from = node.Position + jitter;
            return CaveSampling.Distance(ref set, from, out var cave) < -0.3f && cave == Cave;
        }

        bool NearWater(ref CaveSetBlob set, float3 p)
        {
            var c = set.Caves[Cave];
            for (int i = c.FirstPool; i < c.FirstPool + c.PoolCount; i++)
                if (set.Pools[i].Lava <= 0f && math.distance(set.Pools[i].Centre, p) < set.Pools[i].Radius + 8f)
                    return true;
            for (int s = c.FirstStream; s < c.FirstStream + c.StreamCount; s++)
            {
                var stream = set.Streams[s];
                for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount; i += 3)
                    if (math.distance(set.StreamPoints[i].Position, p) < 8f)
                        return true;
            }
            return false;
        }

        float3 Direction(Aim aim, uint h, float3 from, CaveWallKind kind)
        {
            var a = CaveWallDetails.U(h, 6) * math.PI * 2f;
            var flat = new float3(math.cos(a), 0f, math.sin(a));
            switch (aim)
            {
                case Aim.Ceiling: return math.normalize(new float3(0f, 1f, 0f) + flat * 0.35f * CaveWallDetails.U(h, 7));
                case Aim.Floor: return math.normalize(new float3(0f, -1f, 0f) + flat * 0.5f * CaveWallDetails.U(h, 7));
                case Aim.LowWall: return math.normalize(flat + new float3(0f, -0.45f, 0f));
                default:
                {
                    var up = kind == CaveWallKind.Frost || kind == CaveWallKind.Moss ? 0.5f : 0.25f;
                    return math.normalize(flat + new float3(0f, (CaveWallDetails.U(h, 7) - 0.4f) * up * 2f, 0f));
                }
            }
        }

        bool Spaced(CaveWallKind kind, float3 p)
        {
            foreach (var (k, q, _) in Placed)
                if (k == kind && math.distance(p, q) < Spacing)
                    return false;
            return true;
        }

        const float Spacing = 5f;

        /// <summary>
        /// A ray from inside this cave's air to its wall: marched along the cave field, then bisected; false if it
        /// runs out, lands in another cave, too near the open ground or inside a destination room (whose own kit
        /// has its walls).
        /// </summary>
        internal bool Cast(in TerrainSource source, float3 from, float3 dir, float range, out float3 hit, out float3 normal)
        {
            ref var set = ref source.Caves.Value;
            hit = default;
            normal = default;
            if (!CaveWallMesh.March(ref set, from, dir, range, out hit))
                return false;
            normal = CaveWallMesh.Normal(ref set, hit);
            CaveSampling.Distance(ref set, hit + normal * 0.25f, out var owner);
            if (owner != Cave || hit.y > source.SampleHeight(hit.xz) - 1f || InRoom(ref set, hit))
                return false;
            return true;
        }

        bool InRoom(ref CaveSetBlob set, float3 p)
        {
            var c = set.Caves[Cave];
            for (int r = c.FirstRoom; r < c.FirstRoom + c.RoomCount; r++)
            {
                var room = set.Rooms[r];
                var l = room.Local(p);
                if (math.abs(l.x) < room.HX + 1f && l.z > -0.6f && l.z < room.HZ * 2f + 1f && l.y > -1.5f && l.y < room.Height + 1.5f)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------- shapes

        void Make(in TerrainSource source, CaveWallKind kind, CaveWallMesh g, float3 p, float3 n, uint h)
        {
            float R(int k) => CaveWallDetails.U(h, 40 + k);
            switch (kind)
            {
                case CaveWallKind.Moss:
                    g.Sheet(source, Cave, p, n, 1.2f + R(0) * 1.4f, 1f, h, 0.04f);
                    break;
                case CaveWallKind.Frost:
                    g.Sheet(source, Cave, p, n, 1.1f + R(0) * 1.3f, 1f, h, 0.04f);
                    break;
                case CaveWallKind.Salt:
                {
                    g.Sheet(source, Cave, p, n, 1f + R(0) * 1.2f, 1f, h, 0.04f);
                    var count = 5 + (int)(R(1) * 6f);
                    for (int i = 0; i < count; i++)
                    {
                        var hc = CaveWallDetails.Hash(h, 3, i, 0);
                        if (OnWall(source, g, p, n, (CaveWallDetails.U(hc, 0) - 0.5f) * 1.6f, (CaveWallDetails.U(hc, 1) - 0.5f) * 1.6f, out var q, out var qn))
                        {
                            var s = 0.04f + CaveWallDetails.U(hc, 2) * 0.07f;
                            g.Box(q + qn * s * 0.5f, new float3(s, s, s), math.mul(quaternion.LookRotationSafe(qn, Perp(qn)), quaternion.RotateZ(CaveWallDetails.U(hc, 3) * 3f)), 1.05f);
                        }
                    }
                    break;
                }
                case CaveWallKind.Fungi:
                case CaveWallKind.GlowFungi:
                {
                    var glow = kind == CaveWallKind.GlowFungi;
                    var count = 3 + (int)(R(0) * 5f);
                    for (int i = 0; i < count; i++)
                    {
                        var hc = CaveWallDetails.Hash(h, 4, i, 0);
                        if (!OnWall(source, g, p, n, (CaveWallDetails.U(hc, 0) - 0.5f) * 0.9f, (CaveWallDetails.U(hc, 1) - 0.5f) * 1.1f, out var q, out var qn))
                            continue;
                        var out_ = math.normalizesafe(new float3(qn.x, 0f, qn.z), new float3(1f, 0f, 0f));
                        var radius = (glow ? 0.08f : 0.12f) + CaveWallDetails.U(hc, 2) * (glow ? 0.1f : 0.2f);
                        g.Shelf(q - out_ * 0.03f, out_, radius, 0.035f + radius * 0.12f, i % 2 == 0 ? 1.05f : 0.9f);
                    }
                    if (glow)
                        Glows.Add(p + n * 0.3f);
                    break;
                }
                case CaveWallKind.Carving:
                    Carving(source, g, p, n, h);
                    break;
                case CaveWallKind.Fossil:
                    Fossil(source, g, p, n, h);
                    break;
                case CaveWallKind.Ossuary:
                    Ossuary(g, p, n, h);
                    break;
                case CaveWallKind.Bones:
                    Bones(g, p, n, h);
                    break;
                case CaveWallKind.Icicles:
                {
                    var count = 6 + (int)(R(0) * 9f);
                    for (int i = 0; i < count; i++)
                    {
                        var hc = CaveWallDetails.Hash(h, 5, i, 0);
                        var off = new float3(CaveWallDetails.U(hc, 0) - 0.5f, 0f, CaveWallDetails.U(hc, 1) - 0.5f) * 1.8f;
                        if (!Cast(source, p + new float3(0f, -0.4f, 0f) + off, new float3(0f, 1f, 0f), 1.5f, out var q, out var qn) || qn.y > -0.2f)
                            continue;
                        var length = 0.25f + CaveWallDetails.U(hc, 2) * CaveWallDetails.U(hc, 3) * 1.3f;
                        g.Spike(q + new float3(0f, 0.08f, 0f), new float3(0f, -1f, 0f), 0.035f + length * 0.06f, length + 0.08f, 1f);
                    }
                    break;
                }
                case CaveWallKind.Flowstone:
                {
                    var radius = 1f + R(0) * 1f;
                    g.Sheet(source, Cave, p, n, radius, 1f, h, 0.04f);
                    // A curtain along the sheet's top edge.
                    var t = Perp(n);
                    var count = 5 + (int)(R(1) * 6f);
                    for (int i = 0; i < count; i++)
                    {
                        var hc = CaveWallDetails.Hash(h, 6, i, 0);
                        var along = (i + 0.5f) / count * 2f - 1f;
                        var top = p + t * along * radius * 0.8f + new float3(0f, radius * (0.45f + CaveWallDetails.U(hc, 0) * 0.3f), 0f);
                        if (!Cast(source, top + n * 0.6f, -n, 1.5f, out var q, out var qn))
                            continue;
                        var length = 0.25f + CaveWallDetails.U(hc, 1) * 0.6f;
                        g.Spike(q + qn * 0.04f, new float3(0f, -1f, 0f), 0.06f + CaveWallDetails.U(hc, 2) * 0.05f, length, 0.95f);
                    }
                    break;
                }
            }
        }

        static float3 Perp(float3 n)
        {
            var t = math.cross(new float3(0f, 1f, 0f), n);
            return math.lengthsq(t) < 1e-4f ? new float3(1f, 0f, 0f) : math.normalize(t);
        }

        /// <summary>The wall at an offset (u along the wall, v up it) from a place: cast in from the air side.</summary>
        bool OnWall(in TerrainSource source, CaveWallMesh g, float3 p, float3 n, float u, float v, out float3 q, out float3 qn)
        {
            var t = Perp(n);
            var b = math.normalize(math.cross(n, t));
            return Cast(source, p + t * u + b * v + n * 0.6f, -n, 1.6f, out q, out qn);
        }

        /// <summary>Strokes (2D segments in the wall's plane, metres) laid as thin boxes on the wall, each cast on.</summary>
        void Strokes(in TerrainSource source, CaveWallMesh g, float3 p, float3 n, List<float4> strokes, float width, float tint)
        {
            var t = Perp(n);
            var b = math.normalize(math.cross(n, t));
            foreach (var s in strokes)
            {
                var mid = (s.xy + s.zw) * 0.5f;
                var d = s.zw - s.xy;
                var length = math.length(d);
                if (length < 1e-3f)
                    continue;
                if (!Cast(source, p + t * mid.x + b * mid.y + n * 0.5f, -n, 1.2f, out var q, out var qn))
                    continue;
                var along = math.normalize(t * d.x + b * d.y);
                var side = math.normalizesafe(math.cross(qn, along));
                along = math.normalizesafe(math.cross(side, qn));
                var rot = new quaternion(new float3x3(side, qn, along));
                g.Box(q + qn * 0.015f, new float3(width * 0.5f, 0.025f, length * 0.5f + width * 0.3f), rot, tint);
            }
        }

        void Carving(in TerrainSource source, CaveWallMesh g, float3 p, float3 n, uint h)
        {
            // A smoothed panel, then the folk's motif cut darker into it.
            g.Sheet(source, Cave, p, n, 0.75f, 1.1f, h, 0.035f, ragged: false);
            var s = new List<float4>();
            var c = Site.Culture;
            float R(int k) => CaveWallDetails.U(h, 60 + k);
            if ((c & (FolkCulture.Human)) != 0)
            {
                // A sun: a ring and rays.
                for (int i = 0; i < 8; i++)
                {
                    float a0 = i * math.PI / 4f, a1 = (i + 1) * math.PI / 4f;
                    s.Add(new float4(math.cos(a0), math.sin(a0), math.cos(a1), math.sin(a1)) * 0.16f);
                    s.Add(new float4(math.cos(a0) * 0.24f, math.sin(a0) * 0.24f, math.cos(a0) * 0.42f, math.sin(a0) * 0.42f));
                }
            }
            else if ((c & (FolkCulture.Elf | FolkCulture.MarshFolk)) != 0)
            {
                // A vine with leaves.
                var prev = new float2(-0.5f, 0f);
                for (int i = 1; i <= 10; i++)
                {
                    var x = -0.5f + i * 0.1f;
                    var q = new float2(x, math.sin(x * 9f + R(0) * 6f) * 0.12f);
                    s.Add(new float4(prev, q));
                    if (i % 2 == 0)
                        s.Add(new float4(q, q + new float2(0.04f, (i % 4 == 0 ? 1f : -1f) * 0.12f)));
                    prev = q;
                }
            }
            else if ((c & (FolkCulture.FishFolk | FolkCulture.SeaFolk)) != 0)
            {
                // Waves in three rows.
                for (int row = 0; row < 3; row++)
                {
                    var y = (row - 1) * 0.2f;
                    var prev = new float2(-0.5f, y);
                    for (int i = 1; i <= 8; i++)
                    {
                        var x = -0.5f + i * 0.125f;
                        var q = new float2(x, y + math.sin(i * math.PI / 2f + row) * 0.06f);
                        s.Add(new float4(prev, q));
                        prev = q;
                    }
                }
            }
            else if ((c & (FolkCulture.TurtleFolk | FolkCulture.SnowFolk)) != 0)
            {
                // Two nested hexagons.
                foreach (var r in new[] { 0.4f, 0.22f })
                    for (int i = 0; i < 6; i++)
                    {
                        float a0 = i * math.PI / 3f, a1 = (i + 1) * math.PI / 3f;
                        s.Add(new float4(math.cos(a0), math.sin(a0), math.cos(a1), math.sin(a1)) * r);
                    }
            }
            else if ((c & (FolkCulture.Goblin | FolkCulture.BatFolk)) != 0)
            {
                // Zigzags and a spiked triangle.
                for (int row = 0; row < 2; row++)
                    for (int i = 0; i < 6; i++)
                    {
                        float x0 = -0.45f + i * 0.15f, x1 = x0 + 0.15f, y = 0.1f + row * 0.22f;
                        s.Add(new float4(x0, y + (i % 2) * 0.1f, x1, y + ((i + 1) % 2) * 0.1f));
                    }
                s.Add(new float4(-0.3f, -0.4f, 0f, -0.05f)); s.Add(new float4(0f, -0.05f, 0.3f, -0.4f)); s.Add(new float4(0.3f, -0.4f, -0.3f, -0.4f));
            }
            else
            {
                // Runes (dwarves, ogres, or old work no folk claims): stems with branches in a row.
                var glyphs = 3 + (int)(R(1) * 3f);
                for (int i = 0; i < glyphs; i++)
                {
                    var x = (i - (glyphs - 1) * 0.5f) * 0.2f;
                    s.Add(new float4(x, -0.2f, x, 0.2f));
                    var hg = CaveWallDetails.Hash(h, 9, i, 0);
                    var y0 = (CaveWallDetails.U(hg, 0) - 0.5f) * 0.3f;
                    s.Add(new float4(x, y0, x + (CaveWallDetails.U(hg, 1) < 0.5f ? 0.08f : -0.08f), y0 + 0.1f));
                    if (CaveWallDetails.U(hg, 2) < 0.6f)
                        s.Add(new float4(x, y0 - 0.1f, x + 0.08f, y0 - 0.16f));
                }
            }
            Strokes(source, g, p, n, s, 0.03f, 0.35f);
        }

        void Fossil(in TerrainSource source, CaveWallMesh g, float3 p, float3 n, uint h)
        {
            var s = new List<float4>();
            var scale = 0.8f + CaveWallDetails.U(h, 70) * 0.8f;
            if (CaveWallDetails.U(h, 71) < 0.55f)
            {
                // An ammonite: a log spiral with ribs across it.
                var prev = float2.zero;
                for (int i = 1; i <= 28; i++)
                {
                    var a = i * 0.45f;
                    var r = 0.02f * math.exp(0.2f * a) * scale;
                    var q = new float2(math.cos(a), math.sin(a)) * r;
                    s.Add(new float4(prev, q));
                    if (i % 2 == 0 && i > 8)
                        s.Add(new float4(q, q * 0.78f));
                    prev = q;
                }
            }
            else
            {
                // A fish: spine, ribs, a head and a tail.
                var l = 0.35f * scale;
                s.Add(new float4(-l, 0f, l, 0f));
                for (int i = 0; i < 7; i++)
                {
                    var x = -l * 0.6f + i * l * 0.2f;
                    var rib = 0.12f * scale * (1f - math.abs(i - 3f) / 5f);
                    s.Add(new float4(x, 0f, x - 0.04f, rib));
                    s.Add(new float4(x, 0f, x - 0.04f, -rib));
                }
                s.Add(new float4(l, 0f, l - 0.1f * scale, 0.07f * scale)); s.Add(new float4(l, 0f, l - 0.1f * scale, -0.07f * scale));
                s.Add(new float4(-l, 0f, -l - 0.1f * scale, 0.09f * scale)); s.Add(new float4(-l, 0f, -l - 0.1f * scale, -0.09f * scale));
            }
            Strokes(source, g, p, n, s, 0.025f, 1f);
        }

        void Ossuary(CaveWallMesh g, float3 p, float3 n, uint h)
        {
            // A niche: a dark recess in a stone frame, two shelves of skulls with bones stacked under them.
            var t = Perp(new float3(n.x, 0f, n.z));
            var fn = math.normalizesafe(new float3(n.x, 0f, n.z), n);
            var rot = new quaternion(new float3x3(t, new float3(0f, 1f, 0f), fn));
            var c = p + fn * 0.06f;
            const float w = 0.7f, hh = 0.6f;
            g.Box(c, new float3(w, hh, 0.04f), rot, 0.18f);
            g.Box(c + new float3(0f, hh + 0.06f, 0f) + fn * 0.06f, new float3(w + 0.12f, 0.07f, 0.1f), rot, 0.7f);
            g.Box(c - new float3(0f, hh + 0.06f, 0f) + fn * 0.06f, new float3(w + 0.12f, 0.07f, 0.1f), rot, 0.7f);
            g.Box(c + t * (w + 0.06f) + fn * 0.06f, new float3(0.07f, hh, 0.1f), rot, 0.7f);
            g.Box(c - t * (w + 0.06f) + fn * 0.06f, new float3(0.07f, hh, 0.1f), rot, 0.7f);
            for (int row = 0; row < 2; row++)
            {
                var y = row == 0 ? 0.18f : -0.32f;
                g.Box(c + new float3(0f, y - 0.12f, 0f) + fn * 0.08f, new float3(w, 0.025f, 0.1f), rot, 0.6f);
                for (int i = 0; i < 5; i++)
                {
                    var x = (i - 2f) * 0.26f + (CaveWallDetails.U(h, 80 + i + row * 5) - 0.5f) * 0.05f;
                    var skull = c + t * x + new float3(0f, y, 0f) + fn * 0.1f;
                    g.Box(skull, new float3(0.08f, 0.085f, 0.075f), rot, 1f);
                    g.Box(skull + fn * 0.075f + new float3(0f, 0.01f, 0f) + t * 0.035f, new float3(0.02f, 0.02f, 0.006f), rot, 0.15f);
                    g.Box(skull + fn * 0.075f + new float3(0f, 0.01f, 0f) - t * 0.035f, new float3(0.02f, 0.02f, 0.006f), rot, 0.15f);
                }
                if (row == 1)
                    for (int i = 0; i < 3; i++)
                        g.Box(c + new float3(0f, -0.5f + i * 0.045f, 0f) + fn * 0.1f, new float3(w * 0.85f, 0.02f, 0.02f), rot, 0.95f);
            }
        }

        void Bones(CaveWallMesh g, float3 p, float3 n, uint h)
        {
            var t = Perp(n);
            var b = math.normalize(math.cross(n, t));
            var count = 3 + (int)(CaveWallDetails.U(h, 90) * 4f);
            for (int i = 0; i < count; i++)
            {
                var hc = CaveWallDetails.Hash(h, 8, i, 0);
                var at = p + (t * (CaveWallDetails.U(hc, 0) - 0.5f) + b * (CaveWallDetails.U(hc, 1) - 0.5f)) * 1.2f;
                var a = CaveWallDetails.U(hc, 2) * math.PI * 2f;
                var along = t * math.cos(a) + b * math.sin(a);
                var side = math.normalize(math.cross(n, along));
                var rot = new quaternion(new float3x3(side, n, along));
                var length = 0.12f + CaveWallDetails.U(hc, 3) * 0.2f;
                g.Box(at + n * 0.02f, new float3(0.018f, 0.018f, length), rot, 1f);
                g.Box(at + n * 0.03f + along * length, new float3(0.035f, 0.03f, 0.03f), rot, 1f);
                g.Box(at + n * 0.03f - along * length, new float3(0.035f, 0.03f, 0.03f), rot, 1f);
            }
            if (CaveWallDetails.U(h, 91) < 0.6f)
            {
                var a = CaveWallDetails.U(h, 92) * math.PI * 2f;
                var along = t * math.cos(a) + b * math.sin(a);
                var side = math.normalize(math.cross(n, along));
                var rot = new quaternion(new float3x3(side, n, along));
                g.Box(p + n * 0.08f, new float3(0.08f, 0.07f, 0.1f), rot, 1f);
                g.Box(p + n * 0.09f + along * 0.1f + side * 0.035f, new float3(0.022f, 0.022f, 0.005f), rot, 0.15f);
                g.Box(p + n * 0.09f + along * 0.1f - side * 0.035f, new float3(0.022f, 0.022f, 0.005f), rot, 0.15f);
            }
        }

        /// <summary>A stream's tide line: a dark band with a pale rim a little above the water on both side walls.</summary>
        bool TideLine(in TerrainSource source, int streamIndex, CaveWallMesh g)
        {
            ref var set = ref source.Caves.Value;
            var stream = set.Streams[streamIndex];
            if (stream.PointCount < 3)
                return false;
            var any = false;
            for (int side = -1; side <= 1; side += 2)
            {
                var have = false;
                float3 last = default, lastN = default;
                for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount; i++)
                {
                    var sp = set.StreamPoints[i];
                    var a = set.StreamPoints[math.max(stream.FirstPoint, i - 1)].Position;
                    var b = set.StreamPoints[math.min(stream.FirstPoint + stream.PointCount - 1, i + 1)].Position;
                    var flow = math.normalizesafe(new float3(b.x - a.x, 0f, b.z - a.z), new float3(1f, 0f, 0f));
                    var across = new float3(-flow.z, 0f, flow.x) * side;
                    var start = sp.Position + new float3(0f, 0.68f, 0f);
                    if (CaveSampling.Distance(ref set, start, out _) > -0.1f || !Cast(source, start, across, sp.Width * 0.5f + 6f, out var hit, out var n) || math.abs(n.y) > 0.6f)
                    {
                        have = false;
                        continue;
                    }
                    if (have && math.distance(hit, last) < 4f)
                    {
                        g.Band(last, lastN, hit, n, -0.17f, 0.12f, 0.04f, 1f);
                        g.Band(last, lastN, hit, n, 0.12f, 0.17f, 0.045f, 3.2f);
                        any = true;
                        if (Spaced(CaveWallKind.TideLine, hit))
                            Placed.Add((CaveWallKind.TideLine, hit, n));
                    }
                    last = hit;
                    lastN = n;
                    have = true;
                }
            }
            return any;
        }
    }

    /// <summary>One kind's geometry for one cave, in the cave material's vertex layout (as CaveRoomKit's builder).</summary>
    public class CaveWallMesh
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector4> data = new List<Vector4>();
        readonly List<Color> colours = new List<Color>();
        readonly List<int> indices = new List<int>();
        readonly float3 mouth;
        static readonly float NoRib = CaveSystem.SeamRib(0f, 1f);

        public CaveWallMesh(float3 mouth) { this.mouth = mouth; }

        int Vertex(float3 p, float3 n, float tint)
        {
            positions.Add(p);
            normals.Add(math.normalizesafe(n, new float3(0f, 1f, 0f)));
            data.Add(new Vector4(0f, tint, 0f, NoRib));
            // Colour alpha is the openness the cave walls take: daylight near the mouth, none deep in.
            colours.Add(new Color(0.5f, 0.5f, 0.5f, math.saturate(1f - math.distance(p, mouth) / 18f)));
            return positions.Count - 1;
        }

        void Quad(float3 a, float3 b, float3 c, float3 d, float3 n, float tint)
        {
            var s = Vertex(a, n, tint); Vertex(b, n, tint); Vertex(c, n, tint); Vertex(d, n, tint);
            indices.Add(s); indices.Add(s + 1); indices.Add(s + 2);
            indices.Add(s); indices.Add(s + 2); indices.Add(s + 3);
        }

        void Tri(float3 a, float3 b, float3 c, float3 n, float tint)
        {
            var s = Vertex(a, n, tint); Vertex(b, n, tint); Vertex(c, n, tint);
            indices.Add(s); indices.Add(s + 1); indices.Add(s + 2);
        }

        /// <summary>Marches a ray through a cave's air until it meets rock, then bisects to the surface.</summary>
        internal static bool March(ref CaveSetBlob set, float3 from, float3 dir, float range, out float3 hit)
        {
            hit = default;
            var t = 0f;
            var d = CaveSampling.Distance(ref set, from, out _);
            if (d >= 0f)
                return false;
            var prev = 0f;
            while (t < range)
            {
                prev = t;
                t += math.clamp(-d * 0.8f, 0.04f, 1.5f);
                d = CaveSampling.Distance(ref set, from + dir * t, out _);
                if (d >= 0f)
                {
                    float lo = prev, hi = t;
                    for (int i = 0; i < 10; i++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (CaveSampling.Distance(ref set, from + dir * mid, out _) >= 0f) hi = mid; else lo = mid;
                    }
                    hit = from + dir * ((lo + hi) * 0.5f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>The wall's normal (into the air) at a surface point: the cave field's gradient.</summary>
        internal static float3 Normal(ref CaveSetBlob set, float3 p)
        {
            const float e = 0.08f;
            var g = new float3(
                CaveSampling.Distance(ref set, p + new float3(e, 0f, 0f), out _) - CaveSampling.Distance(ref set, p - new float3(e, 0f, 0f), out _),
                CaveSampling.Distance(ref set, p + new float3(0f, e, 0f), out _) - CaveSampling.Distance(ref set, p - new float3(0f, e, 0f), out _),
                CaveSampling.Distance(ref set, p + new float3(0f, 0f, e), out _) - CaveSampling.Distance(ref set, p - new float3(0f, 0f, e), out _));
            return -math.normalizesafe(g, new float3(0f, 1f, 0f));
        }

        /// <summary>
        /// A sheet laid on the wall round a place: a grid whose points are each cast onto the rock along the place's
        /// normal and lifted a little off it; a ragged outline from the hash, and cells whose points miss are left out.
        /// </summary>
        public void Sheet(in TerrainSource source, int cave, float3 centre, float3 n, float radius, float tint, uint h, float lift, bool ragged = true)
        {
            ref var set = ref source.Caves.Value;
            const int N = 6;
            var t = math.cross(new float3(0f, 1f, 0f), n);
            t = math.lengthsq(t) < 1e-4f ? new float3(1f, 0f, 0f) : math.normalize(t);
            var b = math.normalize(math.cross(n, t));
            var edge = new float[8];
            for (int k = 0; k < 8; k++)
                edge[k] = ragged ? 0.62f + 0.38f * CaveWallDetails.U(h, 100 + k) : 1.5f;
            var ids = new int[(N + 1) * (N + 1)];
            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    var uv = new float2(i, j) / N * 2f - 1f;
                    ids[j * (N + 1) + i] = -1;
                    var r = math.length(uv);
                    if (ragged)
                    {
                        var a = (math.atan2(uv.y, uv.x) / (math.PI * 2f) + 1f) * 8f;
                        var k0 = (int)math.floor(a) % 8;
                        var limit = math.lerp(edge[k0], edge[(k0 + 1) % 8], a - math.floor(a));
                        if (r > limit * 1.05f)
                            continue;
                    }
                    var start = centre + (t * uv.x + b * uv.y) * radius + n * 0.7f;
                    if (CaveSampling.Distance(ref set, start, out var c) >= 0f || c != cave)
                        continue;
                    if (!March(ref set, start, -n, 1.8f, out var hit))
                        continue;
                    var hn = Normal(ref set, hit);
                    if (math.dot(hn, n) < 0.2f)
                        continue;
                    ids[j * (N + 1) + i] = Vertex(hit + hn * lift, hn, tint * (0.92f + 0.16f * CaveWallDetails.U(h, 120 + i + j * 7)));
                }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = ids[j * (N + 1) + i], bb = ids[j * (N + 1) + i + 1], c = ids[(j + 1) * (N + 1) + i + 1], d = ids[(j + 1) * (N + 1) + i];
                    // Wound to face n (t, b, n is right-handed: counter-clockwise seen from the air side is a, d, c).
                    if (a >= 0 && bb >= 0 && c >= 0 && d >= 0)
                    {
                        indices.Add(a); indices.Add(d); indices.Add(c);
                        indices.Add(a); indices.Add(c); indices.Add(bb);
                    }
                    else if (a >= 0 && c >= 0 && d >= 0) { indices.Add(a); indices.Add(d); indices.Add(c); }
                    else if (a >= 0 && bb >= 0 && c >= 0) { indices.Add(a); indices.Add(c); indices.Add(bb); }
                    else if (a >= 0 && bb >= 0 && d >= 0) { indices.Add(a); indices.Add(d); indices.Add(bb); }
                    else if (bb >= 0 && c >= 0 && d >= 0) { indices.Add(bb); indices.Add(d); indices.Add(c); }
                }
        }

        /// <summary>A strip on a wall between two points, from `low` to `high` above each, lifted off the rock.</summary>
        public void Band(float3 a, float3 an, float3 b, float3 bn, float low, float high, float lift, float tint)
        {
            var up = new float3(0f, 1f, 0f);
            var n = math.normalizesafe(an + bn);
            var p0 = a + an * lift + up * low; var p1 = b + bn * lift + up * low;
            var p2 = b + bn * lift + up * high; var p3 = a + an * lift + up * high;
            // Facing the air: flip the winding if the strip runs the other way round the normal.
            var face = math.cross(p1 - p0, p3 - p0);
            if (math.dot(face, n) >= 0f)
                Quad(p0, p1, p2, p3, n, tint);
            else
                Quad(p0, p3, p2, p1, n, tint);
        }

        /// <summary>A box round centre, turned by rotation; darker underneath (as CaveRoomKit's).</summary>
        public void Box(float3 centre, float3 half, quaternion rotation, float tint)
        {
            for (int f = 0; f < 6; f++)
            {
                var axis = f % 3;
                var sign = f < 3 ? 1f : -1f;
                var n = float3.zero; n[axis] = sign;
                var u = float3.zero; u[(axis + 1) % 3] = 1f;
                var v = float3.zero; v[(axis + 2) % 3] = 1f;
                var wn = math.mul(rotation, n);
                var faceTint = wn.y < -0.5f ? tint * 0.6f : tint;
                float3 P(float su, float sv) => centre + math.mul(rotation, (n + u * su + v * sv) * half);
                Quad(P(-1f, -1f), P(1f, -1f), P(1f, 1f), P(-1f, 1f), wn, faceTint);
            }
        }

        /// <summary>A five-sided cone from its base along dir (icicles, flowstone drips).</summary>
        public void Spike(float3 b, float3 dir, float radius, float length, float tint)
        {
            const int sides = 5;
            var u = math.normalize(math.cross(math.abs(dir.y) > 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f), dir));
            var w = math.cross(dir, u);
            var tip = b + dir * length;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * math.PI * 2f / sides, a1 = (k + 1) * math.PI * 2f / sides, am = (k + 0.5f) * math.PI * 2f / sides;
                var p0 = b + (u * math.cos(a0) + w * math.sin(a0)) * radius;
                var p1 = b + (u * math.cos(a1) + w * math.sin(a1)) * radius;
                var out_ = u * math.cos(am) + w * math.sin(am);
                var n = math.normalize(out_ * length + dir * radius);
                // Seen from outside, (p1 - p0) x (tip - p0) points out when the ring runs u -> w round dir.
                if (math.dot(math.cross(p1 - p0, tip - p0), out_) >= 0f)
                    Tri(p0, p1, tip, n, tint * (0.85f + 0.15f * (k & 1)));
                else
                    Tri(p1, p0, tip, n, tint * (0.85f + 0.15f * (k & 1)));
            }
        }

        /// <summary>A bracket fungus: a half-disc shelf out of the wall along `out_`, with a lighter top and a rim.</summary>
        public void Shelf(float3 root, float3 out_, float radius, float thickness, float tint)
        {
            const int segments = 6;
            var up = new float3(0f, 1f, 0f);
            var side = math.normalize(math.cross(up, out_));
            float3 Rim(int k, float y) =>
                root + (side * math.cos(k * math.PI / segments) + out_ * math.sin(k * math.PI / segments) * 1.15f) * radius + up * y;
            var topC = root + up * (thickness * 0.5f + radius * 0.12f);
            for (int k = 0; k < segments; k++)
            {
                var mid = (k + 0.5f) * math.PI / segments;
                var n = side * math.cos(mid) + out_ * math.sin(mid);
                // Top (domed a little), underside, rim; each wound to face out.
                AddFacing(topC, Rim(k, thickness * 0.5f), Rim(k + 1, thickness * 0.5f), up, tint * 1.1f);
                AddFacing(root - up * thickness * 0.5f, Rim(k, -thickness * 0.5f), Rim(k + 1, -thickness * 0.5f), -up, tint * 0.75f);
                var s = Vertex(Rim(k, -thickness * 0.5f), n, tint * 0.9f);
                Vertex(Rim(k + 1, -thickness * 0.5f), n, tint * 0.9f);
                Vertex(Rim(k + 1, thickness * 0.5f), n, tint);
                Vertex(Rim(k, thickness * 0.5f), n, tint);
                if (math.dot(math.cross(positions[s + 1] - positions[s], positions[s + 3] - positions[s]), n) >= 0f)
                {
                    indices.Add(s); indices.Add(s + 1); indices.Add(s + 2);
                    indices.Add(s); indices.Add(s + 2); indices.Add(s + 3);
                }
                else
                {
                    indices.Add(s); indices.Add(s + 2); indices.Add(s + 1);
                    indices.Add(s); indices.Add(s + 3); indices.Add(s + 2);
                }
            }
        }

        void AddFacing(float3 a, float3 b, float3 c, float3 n, float tint)
        {
            if (math.dot(math.cross(b - a, c - a), n) >= 0f)
                Tri(a, b, c, n, tint);
            else
                Tri(a, c, b, n, tint);
        }

        public Mesh ToMesh(string name)
        {
            if (indices.Count == 0)
                return null;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colours);
            mesh.SetUVs(0, data);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
