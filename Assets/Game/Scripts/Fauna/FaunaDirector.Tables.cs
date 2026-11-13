using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// The overworld spawn table's socket kinds (Resources/SpawnTables/Overworld, A-80): which spawner asks. The first
    /// seven are the critter spots, in AmbientLife.CritterKind's order. New kinds go at the end (rows store the number).
    /// </summary>
    public enum OverworldSlot
    {
        Songbird, Woodpecker, Owl, Frog, Mouse, Gull, WharfFish,
        Hermit, Camper, Traveller, Messenger, HoneyThief, Domestic, CaveFolk, CrossingFisher,
        /// <summary>A-88 row 2 (#37): the sea folk lake's dome schools and its big fish near the bed.</summary>
        LakeSchool, LakeBed,
        /// <summary>A-89 row 3: the marsh folk village's frogs on lily pads, its heron in the shallows, its dragonflies.</summary>
        MarshFrog, MarshHeron, MarshDragonfly,
        /// <summary>A-103 row 1: the turtle folk households, one per igloo of a turtle village (Count: folk per igloo).</summary>
        TurtleHousehold,
        /// <summary>A-103 row 2: the sea folk households, one per shell dome of the lake village (Count: folk per dome).</summary>
        SeaHousehold
    }

    /// <summary>
    /// The director's reads of the overworld spawn table (A-80): it replaced the critter table, the domestic table and
    /// the role look-ups, so every overworld spawner picks its species from rows. The spawners keep their own places
    /// and timing; only the "which species" question goes to the table. A pick draws nothing from the director's rng.
    /// </summary>
    public partial class FaunaDirector
    {
        SpawnTable overworld;
        readonly List<int> fitting = new List<int>();

        /// <summary>The overworld table (loaded once).</summary>
        public static SpawnTable OverworldTable => SpawnTable.Load("Overworld");

        /// <summary>Every species the overworld table can name, for the species list.</summary>
        static IEnumerable<CreatureSpecies> OverworldSpecies(SpawnTable table)
        {
            if (table == null)
                yield break;
            foreach (var r in table.Rows)
                if (r.Species != null)
                    yield return r.Species;
        }

        /// <summary>The species index the table draws for one socket of a kind, or -1 (no row fits, or its species isn't known).</summary>
        int TableSpecies(OverworldSlot slot, uint seed, DayPhase time = DayPhase.All, LandmarkType landmark = null)
        {
            if (overworld == null)
                return -1;
            var s = SpawnSocket.Of((int)slot, seed);
            s.Time = time;
            s.Landmark = landmark;
            var row = overworld.PickRow(s);
            return row >= 0 ? SpeciesIndex(overworld.Rows[row].Species) : -1;
        }

        /// <summary>Every row of a kind that fits (in table order), for spawners that take all of them.</summary>
        List<int> TableRows(OverworldSlot slot, LandmarkType landmark = null)
        {
            fitting.Clear();
            if (overworld == null)
                return fitting;
            var s = SpawnSocket.Of((int)slot, 0);
            s.Landmark = landmark;
            overworld.AllFitting(s, fitting);
            return fitting;
        }

        /// <summary>A critter spot's kind as a table kind.</summary>
        static OverworldSlot CritterSlot(AmbientLife.CritterKind kind) => (OverworldSlot)(int)kind;

        // ---- The cave table (A-80 row 3) ----

        SpawnTable caveTable;

        /// <summary>The cave table (loaded once): rows per chamber type, the den visitors and the moths at glows.</summary>
        public static SpawnTable CaveTable => SpawnTable.Load("Cave");

        /// <summary>The part of the day now, for rows with Times.</summary>
        DayPhase PhaseNow => Cycle != null ? SpawnTable.PhaseOf(Cycle.TimeOfDay) : DayPhase.Day;

        /// <summary>
        /// A cave node's spot kind for the cave table: a chamber by a pool, the deepest chamber (farthest from the mouth),
        /// or any other chamber; -1 for passage and shaft nodes. (W-115 will give cave sockets their own chamber tags;
        /// until then the kind is read from the cave's nodes and pools.)
        /// </summary>
        static int ChamberSpot(ref CaveSetBlob set, ref CaveData cave, int node, int deepest)
        {
            if (node >= set.Kinds.Length || set.Kinds[node] != CaveNodeKind.Chamber)
                return -1;
            var n = set.Nodes[node];
            for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount && p < set.Pools.Length; p++)
                if (set.Pools[p].Lava <= 0f && math.distance(set.Pools[p].Centre.xz, n.Position.xz) < n.Radius + set.Pools[p].Radius)
                    return (int)CaveSpot.PoolChamber;
            return (int)(node == deepest ? CaveSpot.DeepChamber : CaveSpot.Chamber);
        }

        /// <summary>The cave's chamber node farthest from its mouth, or -1.</summary>
        static int DeepestChamber(ref CaveSetBlob set, ref CaveData cave)
        {
            int best = -1;
            float far = -1f;
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount && n < set.Kinds.Length; n++)
                if (set.Kinds[n] == CaveNodeKind.Chamber && math.distancesq(set.Nodes[n].Position, cave.Mouth) > far)
                {
                    far = math.distancesq(set.Nodes[n].Position, cave.Mouth);
                    best = n;
                }
            return best;
        }

        /// <summary>The socket a cave spot asks the cave table with: seeded by the cave's key and the node.</summary>
        static SpawnSocket CaveSocket(int spot, ref CaveData cave, int node, int biome, DayPhase time)
        {
            var s = SpawnSocket.Of(spot, SpawnTable.Scramble(cave.Key ^ SpawnTable.Scramble((uint)node + 0x4A3Bu)), biome: biome);
            s.Time = time;
            return s;
        }

        // (The island the cave table counts were last logged for: cave count and first mouth.)
        float4 caveTableLogged = new float4(-1f);

        /// <summary>
        /// Once per island: the rows per table, and what the cave table would put in every chamber by day and by night
        /// (no spawning): chambers per type, picks per species, and a few filled chambers' centres to frame shots by.
        /// </summary>
        void LogSpawnTables(in TerrainSource source)
        {
            if (caveTable == null || !source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;
            var key = new float4(set.Caves.Length, set.Caves.Length > 0 ? set.Caves[0].Mouth : float3.zero);
            if (math.all(key == caveTableLogged))
                return;
            caveTableLogged = key;
            var dungeon = SpawnTable.Load("Dungeon");
            UnityEngine.Debug.Log($"[spawn tables] rows: Overworld {(overworld != null ? overworld.Rows.Count : 0)}, " +
                                  $"Dungeon {(dungeon != null ? dungeon.Rows.Count : 0)}, Cave {caveTable.Rows.Count}");
            // (The biomes the caves open in, which the cave rows' Biomes key on.)
            var mouths = new SortedDictionary<string, int>();
            for (int c = 0; c < set.Caves.Length; c++)
            {
                if (set.Caves[c].NodeCount < 2)
                    continue;
                var id = source.SampleGround(set.Caves[c].Mouth.xz).Biomes.Dominant();
                var biomeName = id >= 0 && id < biomes.Length && biomes[id] != null ? biomes[id].name : "none";
                mouths[biomeName] = (mouths.TryGetValue(biomeName, out var count) ? count : 0) + 1;
            }
            var mouthList = new System.Text.StringBuilder();
            foreach (var kv in mouths)
                mouthList.Append($" {kv.Key} {kv.Value}");
            UnityEngine.Debug.Log($"[spawn tables] cave mouths by biome:{mouthList}");
            foreach (var time in new[] { DayPhase.Day, DayPhase.Night })
            {
                var spots = new int[5];
                var picks = new SortedDictionary<string, int>();
                var text = new System.Text.StringBuilder();
                int shown = 0;
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    ref var cave = ref set.Caves[c];
                    if (cave.NodeCount < 2)
                        continue;
                    var biome = source.SampleGround(cave.Mouth.xz).Biomes.Dominant();
                    var deepest = DeepestChamber(ref set, ref cave);
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                    {
                        var spot = ChamberSpot(ref set, ref cave, n, deepest);
                        if (spot < 0)
                            continue;
                        spots[spot]++;
                        var socket = CaveSocket(spot, ref cave, n, biome, time);
                        var pick = caveTable.PickRow(socket);
                        var name = pick >= 0 && caveTable.Rows[pick].Species != null ? caveTable.Rows[pick].Species.name : "empty";
                        picks[$"{(CaveSpot)spot}/{name}"] = (picks.TryGetValue($"{(CaveSpot)spot}/{name}", out var had) ? had : 0) + 1;
                        if (name != "empty" && shown < 4 && time == DayPhase.Day)
                        {
                            shown++;
                            text.Append($" | cave {c} node {n} {(CaveSpot)spot} {name} x{caveTable.CountOf(pick, socket)} at {set.Nodes[n].Position} r {set.Nodes[n].Radius:0.0} mouth {cave.Mouth}");
                        }
                    }
                }
                var list = new System.Text.StringBuilder();
                foreach (var kv in picks)
                    list.Append($" {kv.Key} {kv.Value}");
                UnityEngine.Debug.Log($"[spawn tables] cave {time}: chambers {spots[1]}, pool {spots[2]}, deep {spots[3]}; picks{list}{text}");
            }
        }
    }

    /// <summary>The cave table's spot kinds (Resources/SpawnTables/Cave): rows store the number, new kinds go at the end.</summary>
    public enum CaveSpot { Passage, Chamber, PoolChamber, DeepChamber, Glow }
}
