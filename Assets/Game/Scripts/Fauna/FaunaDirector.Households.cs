using System.Collections.Generic;
using System.IO;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>A household member's place in it (A-105): the head, the head's partner, an elder, or a child.</summary>
    public enum Kin : byte { Head, Partner, Elder, Child }

    /// <summary>What one household member is to another (A-105 row 3), for family talk: dialogue.json's "kin" key names
    /// these in lower case ("partner", "child", "inlaw"...). None: not kin (itself).</summary>
    public enum KinRelation : byte { None, Partner, Child, Parent, Grandparent, Grandchild, Sibling, InLaw }

    /// <summary>One member of a household: who it is (the Individual it's given when it spawns), its role, and whether
    /// its family words are feminine (daughter, wife, grandmother) or not (son, husband, grandfather).</summary>
    public struct Kinsfolk
    {
        public uint Individual;
        public Kin Role;
        public bool Feminine;
    }

    /// <summary>
    /// A house and the folk who live in it (A-105 row 1): its door (xz, its outward yaw in w), what kind of home it is
    /// ("house" in a town or fishing village, "hut" for a hermit, "igloo", "dome") and its members. All of it comes from
    /// the door's spot, so it is the same on every machine with nothing sent or stored. Taken marks the members out in
    /// the world just now (host only: the rest are away, or not yet spawned). Lost marks the members who have died (A-105
    /// row 2, host only, for this session): their beds stay empty, so nobody new moves in yet.
    /// </summary>
    public sealed class Household
    {
        public int Landmark;
        public float4 Door;
        public uint Key;
        public string Kind;
        public Kinsfolk[] Members;
        internal uint Taken, Pinned, Lost;

        public string NameOf(int member) => FolkNames.Of(Members[member].Individual) ?? "?";

        /// <summary>The family word for a member ("daughter", "husband", "grandmother"...), as the head would say it.</summary>
        public static string Word(in Kinsfolk k) => k.Role switch
        {
            Kin.Partner => k.Feminine ? "wife" : "husband",
            Kin.Elder => k.Feminine ? "mother" : "father",
            Kin.Child => k.Feminine ? "daughter" : "son",
            _ => "head"
        };

        /// <summary>Whether a member has died this session (host only: a client never marks one).</summary>
        public bool Gone(int member) => (Lost >> member & 1u) != 0;

        /// <summary>What member other is to member speaker. Elders are the head's parents, children the head's and the
        /// partner's, so an elder's partner is the other elder.</summary>
        public KinRelation Relation(int speaker, int other)
        {
            if (speaker == other || speaker < 0 || other < 0 || speaker >= Members.Length || other >= Members.Length)
                return KinRelation.None;
            var me = Members[speaker].Role;
            var them = Members[other].Role;
            switch (me)
            {
                case Kin.Head:
                    return them == Kin.Partner ? KinRelation.Partner : them == Kin.Elder ? KinRelation.Parent : them == Kin.Child ? KinRelation.Child : KinRelation.None;
                case Kin.Partner:
                    return them == Kin.Head ? KinRelation.Partner : them == Kin.Elder ? KinRelation.InLaw : them == Kin.Child ? KinRelation.Child : KinRelation.None;
                case Kin.Elder:
                    return them == Kin.Head ? KinRelation.Child : them == Kin.Partner ? KinRelation.InLaw : them == Kin.Child ? KinRelation.Grandchild : KinRelation.Partner;
                default:
                    return them == Kin.Elder ? KinRelation.Grandparent : them == Kin.Child ? KinRelation.Sibling : KinRelation.Parent;
            }
        }

        /// <summary>The family word member speaker uses for member other ("my daughter", "my mother-in-law"), sex-matched by
        /// other's Feminine; null when other is no kin of speaker.</summary>
        public string WordFor(int speaker, int other)
        {
            var f = Members[other].Feminine;
            switch (Relation(speaker, other))
            {
                case KinRelation.Partner: return f ? "wife" : "husband";
                case KinRelation.Child: return f ? "daughter" : "son";
                case KinRelation.Parent: return f ? "mother" : "father";
                case KinRelation.Grandparent: return f ? "grandmother" : "grandfather";
                case KinRelation.Grandchild: return f ? "granddaughter" : "grandson";
                case KinRelation.Sibling: return f ? "sister" : "brother";
                case KinRelation.InLaw:
                    return Members[speaker].Role == Kin.Elder ? (f ? "daughter-in-law" : "son-in-law") : (f ? "mother-in-law" : "father-in-law");
                default: return null;
            }
        }
    }

    /// <summary>
    /// The island's households (A-105), built by the fauna director once the world is built and kept until it's torn
    /// down: every house's members, and which house each derived Individual belongs to, for talk (row 3) and the
    /// -households log. Seed-derived, so every machine builds the same table.
    /// </summary>
    public static class Households
    {
        internal static readonly List<Household> All = new List<Household>();
        internal static readonly Dictionary<int, List<int>> ByLandmark = new Dictionary<int, List<int>>();
        internal static readonly Dictionary<int2, int> ByDoor = new Dictionary<int2, int>();
        static readonly Dictionary<uint, int> byIndividual = new Dictionary<uint, int>();
        public static bool Ready { get; private set; }

        /// <summary>The house a folk lives in and its place in it, by its Individual (false for a lodger or any other).</summary>
        public static bool Of(uint individual, out Household house, out int member)
        {
            house = null;
            member = -1;
            if (!byIndividual.TryGetValue(individual, out var slot))
                return false;
            house = All[slot >> 4];
            member = slot & 15;
            return true;
        }

        internal static int2 DoorKey(float4 door) => (int2)math.round(door.xz * 2f);

        internal static void Set(List<Household> houses)
        {
            Clear();
            All.AddRange(houses);
            for (int h = 0; h < All.Count; h++)
            {
                var house = All[h];
                if (!ByLandmark.TryGetValue(house.Landmark, out var list))
                    ByLandmark[house.Landmark] = list = new List<int>();
                list.Add(h);
                ByDoor[DoorKey(house.Door)] = h;
                for (int m = 0; m < house.Members.Length; m++)
                    byIndividual[house.Members[m].Individual] = h << 4 | m;
            }
            Ready = true;
        }

        internal static void Clear()
        {
            All.Clear();
            ByLandmark.Clear();
            ByDoor.Clear();
            byIndividual.Clear();
            Ready = false;
        }
    }

    /// <summary>
    /// Folk names (P-26): a folk's sex and name, both picked from its Individual, so every machine calls it the same
    /// with nothing sent. FolkTalk's titles and the households (A-105) use this one pick: a household draws each
    /// member's sex and nudges its Individual until the Individual gives that sex, so its name and its family words
    /// (daughter, husband) agree. Names come from StreamingAssets/folk-names.json, read once: a feminine folk's from
    /// the feminine and either lists, a masculine one's from the masculine and either lists.
    /// </summary>
    public static class FolkNames
    {
        [System.Serializable]
        class NameFile { public string[] feminine, masculine, either; }

        static string[] feminine, masculine;

        /// <summary>Whether a folk is feminine (she) or masculine (he), from its Individual.</summary>
        public static bool Feminine(uint individual) => (BiomeSampling.Scramble(individual ^ 0x53455821u) & 1u) != 0;

        public static string Of(uint individual)
        {
            if (feminine == null)
                Load();
            var list = Feminine(individual) ? feminine : masculine;
            return list.Length > 0 ? list[BiomeSampling.Scramble(individual ^ 0x4E414D45u) % (uint)list.Length] : null;
        }

        static void Load()
        {
            feminine = masculine = System.Array.Empty<string>();
            try
            {
                var read = JsonUtility.FromJson<NameFile>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "folk-names.json")));
                var either = read?.either ?? System.Array.Empty<string>();
                feminine = Concat(read?.feminine, either);
                masculine = Concat(read?.masculine, either);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"FolkNames: couldn't read folk-names.json: {e.Message}");
            }
        }

        static string[] Concat(string[] a, string[] b)
        {
            var list = new List<string>(a ?? System.Array.Empty<string>());
            list.AddRange(b);
            return list.ToArray();
        }
    }

    /// <summary>
    /// A-105 row 1: households. Every house on the island (a town's House plots, a fishing village's houses, a hermit's
    /// hut, a turtle village's igloos, a sea folk village's domes) gets one person or a family from its door's spot:
    /// a head living alone, a lone elder, a couple, a couple with children, or a couple with an elder and children, held
    /// to what the home can hold. Nothing is sent or stored and the spawn draws don't move: as the host peoples a
    /// landmark, each folk it spawns there takes the next free place in a house (adults filling houses from the middle
    /// out, children joining a house with a grown-up in it) and is given that member's Individual and the house's door
    /// (CreatureState.OwnDoor); a folk with no place left keeps its own draw, a lodger, as before.
    /// </summary>
    public partial class FaunaDirector
    {
        const uint HouseSalt = 0x484F4D45u;
        readonly List<IglooSeat> houseSeats = new List<IglooSeat>();
        bool logHouses, logHousesRead;
        EntityQuery houseQuery;
        World houseQueryWorld;
        float nextHouseWatch;
        readonly HashSet<uint> housesSeen = new HashSet<uint>();
        readonly Dictionary<uint, bool> housesInside = new Dictionary<uint, bool>();
        int houseArrivals, houseWrongDoor;

        bool LogHouses
        {
            get
            {
                if (!logHousesRead)
                {
                    logHousesRead = true;
                    logHouses = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-households") >= 0;
                }
                return logHouses;
            }
        }

        /// <summary>Builds the island's households, once per build (on every machine that needs them).</summary>
        void EnsureHouses(in TerrainSource source)
        {
            if (Households.Ready || !built || !source.Landmarks.IsCreated)
                return;
            var list = new List<Household>();
            ref var lm = ref source.Landmarks.Value;
            // Towns: a house on each House plot with a building, its door where AnchorBaking stands the plot's Door.
            if (source.Settlements.IsCreated)
            {
                ref var set = ref source.Settlements.Value;
                var buildings = SettlementBaking.Buildings(landmarks);
                for (int t = 0; t < set.Settlements.Length; t++)
                {
                    var town = set.Settlements[t];
                    var scale = set.Layouts[town.Layout].BuildingScale;
                    for (int p = town.FirstPlot; p < town.FirstPlot + town.PlotCount; p++)
                    {
                        var plot = set.Plots[p];
                        if (plot.Role != PlotRole.House || plot.Building < 0)
                            continue;
                        var species = plot.Building < buildings.Count ? buildings[plot.Building].Species : null;
                        var front = species != null ? Punchy.Flora.FloraGenerator.DoorFront(species) : -1f;
                        var at = front > 0f ? plot.Centre + plot.Facing * (front * scale + 0.6f) : plot.Centre + plot.Facing * (plot.Half.y * 0.75f + 0.6f);
                        AddHouse(list, town.Landmark, new float4(at.x, 0f, at.y, math.atan2(plot.Facing.x, plot.Facing.y)), "house", 1, 3, 3);
                    }
                }
            }
            // Wild houses: each Door a landmark owns (a fishing village's houses, a hermit's hut).
            if (source.Anchors.IsCreated)
            {
                ref var anchors = ref source.Anchors.Value.Anchors;
                for (int a = 0; a < anchors.Length; a++)
                {
                    var anchor = anchors[a];
                    if (anchor.Kind != AnchorKind.Door || !anchor.InLandmark || anchor.OwnerIndex >= lm.Instances.Length)
                        continue;
                    var li = anchor.OwnerIndex;
                    var type = lm.Instances[li].Type < landmarks.Length ? landmarks[lm.Instances[li].Type] : null;
                    var hut = type != null && type.Anchors == WildAnchors.Hermit;
                    AddHouse(list, li, new float4(anchor.Position.x, 0f, anchor.Position.z, math.atan2(anchor.Facing.x, anchor.Facing.y)),
                             hut ? "hut" : "house", 1, hut ? 1 : 3, hut ? 0 : 3);
                }
            }
            // Igloos and domes, sized by their household rows.
            if (overworld != null && floraBlob.IsCreated)
            {
                for (int i = 0; i < lm.Instances.Length; i++)
                {
                    var inst = lm.Instances[i];
                    var type = inst.Type < landmarks.Length ? landmarks[inst.Type] : null;
                    if (type == null)
                        continue;
                    var radius = lm.Types[inst.Type].Radius * inst.Scale;
                    if (type.name == TurtleVillageBaking.TypeName)
                    {
                        RowSize(OverworldSlot.TurtleHousehold, type, out var lo, out var hi);
                        IglooSeats(source, inst, radius, houseSeats);
                        foreach (var seat in houseSeats)
                            if (seat.Placed)
                                AddHouse(list, i, seat.Door, "igloo", lo, hi, 2);
                    }
                    else if (type.name == SeaFolkBaking.VillageName)
                    {
                        RowSize(OverworldSlot.SeaHousehold, type, out var lo, out var hi);
                        if (SeaVillageSpots(inst, radius))
                            foreach (var door in seaDoors)
                                AddHouse(list, i, door, "dome", lo, hi, 2);
                    }
                }
            }
            // Each landmark's houses from its middle out, so the first folk fill the middle ones.
            var landmarkCount = lm.Instances.Length;
            var centres = new float2[landmarkCount];
            for (int i = 0; i < landmarkCount; i++)
                centres[i] = lm.Instances[i].Position;
            list.Sort((x, y) =>
            {
                if (x.Landmark != y.Landmark)
                    return x.Landmark.CompareTo(y.Landmark);
                var c = x.Landmark < landmarkCount ? centres[x.Landmark] : float2.zero;
                var d = math.distancesq(x.Door.xz, c).CompareTo(math.distancesq(y.Door.xz, c));
                return d != 0 ? d : x.Key.CompareTo(y.Key);
            });
            Households.Set(list);
            var members = 0;
            foreach (var h in list)
                members += h.Members.Length;
            Debug.Log($"[households] {list.Count} houses, {members} folk, {Households.ByLandmark.Count} landmarks");
        }

        /// <summary>The summed Count range of a landmark's household rows (at least one grown-up).</summary>
        void RowSize(OverworldSlot slot, LandmarkType type, out int lo, out int hi)
        {
            lo = hi = 0;
            foreach (var ri in TableRows(slot, type))
            {
                var c = overworld.Rows[ri].Count;
                lo += math.max(0, c.x);
                hi += math.max(c.x, c.y);
            }
            lo = math.max(1, lo);
            hi = math.max(lo, hi);
        }

        /// <summary>
        /// One house's household, from its door's spot: the mix rolled from its key, then held to the grown-ups and
        /// children its kind of home can hold. Each member's sex is drawn from the key too (a couple is a man and a
        /// woman), and its Individual is hashed from the key, nudged until it gives that sex and no two in the house share
        /// a name.
        /// </summary>
        static void AddHouse(List<Household> into, int landmark, float4 door, string kind, int minAdults, int maxAdults, int maxChildren)
        {
            var q = Households.DoorKey(door);
            // (The kind keyed by its own letters: string.GetHashCode differs between runtimes.)
            var key = BiomeSampling.Scramble(math.hash(q) ^ KindSalt(kind) ^ HouseSalt);
            var roll = BiomeSampling.Scramble(key ^ 0x524F4C4Cu) % 100u;
            var bits = BiomeSampling.Scramble(key ^ 0x4B494E44u);
            var adults = new List<Kin>(3);
            var children = 0;
            if (roll < 16) adults.Add(Kin.Head);
            else if (roll < 26) adults.Add(Kin.Elder);
            else if (roll < 42) { adults.Add(Kin.Head); adults.Add(Kin.Partner); }
            else if (roll < 82) { adults.Add(Kin.Head); adults.Add(Kin.Partner); children = 1 + (int)(bits % 3u); }
            else { adults.Add(Kin.Head); adults.Add(Kin.Partner); adults.Add(Kin.Elder); children = 1 + (int)(bits % 2u); }
            while (adults.Count < minAdults)
            {
                if (!adults.Contains(Kin.Head)) adults.Insert(0, Kin.Head);
                else if (!adults.Contains(Kin.Partner)) adults.Insert(1, Kin.Partner);
                else adults.Add(Kin.Elder);
            }
            while (adults.Count > math.max(1, maxAdults))
                adults.RemoveAt(adults.Count - 1);
            // (Children only with a parent in the house.)
            if (!adults.Contains(Kin.Head))
                children = 0;
            children = math.min(children, maxChildren);
            var members = new Kinsfolk[adults.Count + children];
            var headFeminine = (bits >> 8 & 1u) != 0;
            for (int k = 0; k < members.Length; k++)
            {
                var role = k < adults.Count ? adults[k] : Kin.Child;
                var feminine = role == Kin.Head ? headFeminine : role == Kin.Partner ? !headFeminine : (bits >> (9 + k) & 1u) != 0;
                var individual = BiomeSampling.Scramble(key ^ (uint)(k + 1) * 0x9E3779B9u) | 1u;
                // (Nudged until it gives the drawn sex, so the name fits the family word, and no name repeats in the house.)
                for (int tries = 0; tries < 64; tries++)
                {
                    var clash = FolkNames.Feminine(individual) != feminine;
                    for (int j = 0; j < k; j++)
                        clash |= members[j].Individual == individual || FolkNames.Of(members[j].Individual) == FolkNames.Of(individual);
                    if (!clash)
                        break;
                    individual = BiomeSampling.Scramble(individual + 0x6D2B79F5u) | 1u;
                }
                members[k] = new Kinsfolk { Individual = individual, Role = role, Feminine = feminine };
            }
            into.Add(new Household { Landmark = landmark, Door = door, Key = key, Kind = kind, Members = members });
        }

        static uint KindSalt(string kind)
        {
            var h = 0x811C9DC5u;
            foreach (var c in kind)
                h = (h ^ c) * 0x01000193u;
            return h;
        }

        /// <summary>
        /// A landmark's houses all empty again (when it's peopled anew); hermits keep their huts, and the dead's beds stay
        /// empty (A-105 row 2).
        /// </summary>
        void ResetHouses(int landmark)
        {
            if (!Households.ByLandmark.TryGetValue(landmark, out var list))
                return;
            foreach (var h in list)
                Households.All[h].Taken = Households.All[h].Pinned | Households.All[h].Lost;
        }

        /// <summary>
        /// A housed folk has died (Despawn, host): its place in its house is kept empty, so its household has an empty
        /// bed and the folk peopling the landmark next go to other houses or lodge, as before.
        /// </summary>
        void HouseMemberDied(in CreatureState s)
        {
            if (!Households.Of(s.Individual, out var house, out var m))
                return;
            house.Lost |= 1u << m;
            house.Taken |= 1u << m;
            if (LogHouses)
                Debug.Log($"[households] died {FolkNames.Of(s.Individual)} {house.Members[m].Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) landmark {house.Landmark}: bed left empty");
        }

        /// <summary>The next free place in one of a landmark's houses for a folk spawning there, or -1 (a lodger).</summary>
        int TakeHouseSlot(int landmark, bool child)
        {
            if (!Households.ByLandmark.TryGetValue(landmark, out var list))
                return -1;
            if (child)
            {
                // First a house with a grown-up in it, then any.
                for (int pass = 0; pass < 2; pass++)
                    foreach (var h in list)
                    {
                        var m = FreeMember(Households.All[h], true, pass == 0);
                        if (m >= 0)
                            return Take(h, m);
                    }
                return -1;
            }
            foreach (var h in list)
            {
                var m = FreeMember(Households.All[h], false, false);
                if (m >= 0)
                    return Take(h, m);
            }
            return -1;
        }

        /// <summary>
        /// A place in the house with this door (an igloo's, a dome's) for a folk homed by it; a child that finds it full
        /// goes to another house of the same landmark with a grown-up in. -1: none (a lodger, homed by that door still).
        /// </summary>
        int TakeDoorSlot(float4 door, bool child)
        {
            if (!Households.ByDoor.TryGetValue(Households.DoorKey(door), out var h))
                return -1;
            var m = FreeMember(Households.All[h], child, false);
            if (m >= 0)
                return Take(h, m);
            return child ? TakeHouseSlot(Households.All[h].Landmark, true) : -1;
        }

        /// <summary>
        /// A free grown-up's place in the house nearest a hermit's shelter (within 6 m, in the same landmark), kept for
        /// it while its landmark is peopled anew; or -1.
        /// </summary>
        int PinHut(int landmark, float2 shelter)
        {
            if (!Households.ByLandmark.TryGetValue(landmark, out var list))
                return -1;
            var best = -1;
            var bestD = 36f;
            foreach (var h in list)
            {
                var d = math.distancesq(Households.All[h].Door.xz, shelter);
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            var m = best < 0 ? -1 : FreeMember(Households.All[best], false, false);
            if (m < 0)
                return -1;
            Households.All[best].Pinned |= 1u << m;
            return Take(best, m);
        }

        static int FreeMember(Household house, bool child, bool needAdult)
        {
            if (needAdult)
            {
                var adultIn = false;
                for (int m = 0; m < house.Members.Length; m++)
                    adultIn |= house.Members[m].Role != Kin.Child && (house.Taken >> m & 1u) != 0;
                if (!adultIn)
                    return -1;
            }
            for (int m = 0; m < house.Members.Length; m++)
                if ((house.Members[m].Role == Kin.Child) == child && (house.Taken >> m & 1u) == 0)
                    return m;
            return -1;
        }

        static int Take(int house, int member)
        {
            Households.All[house].Taken |= 1u << member;
            return house << 4 | member;
        }

        /// <summary>Makes a newly spawned folk the household member its place is for: its Individual and its house's door.</summary>
        static void Settle(ref CreatureState state, int slot)
        {
            if (slot < 0 || (slot >> 4) >= Households.All.Count)
                return;
            var house = Households.All[slot >> 4];
            state.Individual = house.Members[slot & 15].Individual;
            state.OwnDoor = house.Door;
        }

        /// <summary>Whether a species is grown or child folk (the ones who live in houses).</summary>
        bool HousedFolk(int speciesIndex, out bool child)
        {
            child = false;
            if (speciesIndex < 0 || speciesIndex >= blobs.Count)
                return false;
            ref var b = ref blobs[speciesIndex].Value;
            child = b.Child;
            return b.Upright && FolkRaces.Has(b.Comfort.Race, RaceTrait.Folk);
        }

        /// <summary>-households: a landmark's houses as the host has just peopled them, one line per house.</summary>
        void LogLandmarkHouses(int landmark, string typeName, float2 centre)
        {
            if (!LogHouses || !Households.ByLandmark.TryGetValue(landmark, out var list))
                return;
            var sb = new System.Text.StringBuilder();
            var home = 0;
            var folk = 0;
            foreach (var h in list)
            {
                var house = Households.All[h];
                sb.Clear();
                for (int m = 0; m < house.Members.Length; m++)
                {
                    var k = house.Members[m];
                    var lost = (house.Lost >> m & 1u) != 0;
                    var taken = !lost && (house.Taken >> m & 1u) != 0;
                    home += taken ? 1 : 0;
                    sb.Append(m > 0 ? ", " : "").Append(k.Role).Append(' ').Append(FolkNames.Of(k.Individual))
                      .Append(k.Feminine ? " (f, " : " (m, ").Append(lost ? "died, bed empty)" : taken ? "in)" : "away)");
                }
                folk += house.Members.Length;
                Debug.Log($"[households] {typeName} {landmark} house {h} {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) size {house.Members.Length}: {sb}");
            }
            Debug.Log($"[households] {typeName} {landmark} at ({centre.x:F0}, {centre.y:F0}): {list.Count} houses, {folk} members, {home} out in the world");
        }

        /// <summary>
        /// -households, on any machine: names each housed folk this machine sees once (by its synced Individual), with its
        /// house, so a host's and a client's logs can be compared house by house.
        /// </summary>
        void WatchHouses(in TerrainSource source)
        {
            if (!LogHouses || Time.time < nextHouseWatch || queryWorld == null || !queryWorld.IsCreated)
                return;
            nextHouseWatch = Time.time + 1f;
            EnsureHouses(source);
            if (houseQueryWorld != queryWorld)
            {
                houseQueryWorld = queryWorld;
                houseQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            }
            var states = houseQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
            {
                if (!Households.Of(s.Individual, out var house, out var m))
                    continue;
                // (A-105 row 2: each going in at a door, from the synced mode and stage, and whether that door is its
                // own house's: the inside spot is 0.7 m in from the door it used.)
                var inside = s.Mode == CreatureMode.Home && s.Stage >= 1;
                housesInside.TryGetValue(s.Individual, out var was);
                housesInside[s.Individual] = inside;
                if (inside && !was)
                {
                    var own = math.distance(s.Position.xz, house.Door.xz) < 1.5f;
                    houseArrivals++;
                    houseWrongDoor += own ? 0 : 1;
                    Debug.Log($"[households] home {FolkNames.Of(s.Individual)} {house.Members[m].Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) " +
                              $"landmark {house.Landmark}: {(own ? "own door" : $"WRONG door ({s.Position.x:F1}, {s.Position.z:F1})")} " +
                              $"at {FishCatch.Hour():F2}h; {houseArrivals} home, {houseWrongDoor} at a wrong door");
                }
                if (!housesSeen.Add(s.Individual))
                    continue;
                var k = house.Members[m];
                Debug.Log($"[households] seen {FolkNames.Of(k.Individual)} {k.Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) " +
                          $"landmark {house.Landmark}, near ({s.Position.x:F0}, {s.Position.z:F0}){(math.any(s.OwnDoor.xz != house.Door.xz) ? " (door differs)" : "")}");
            }
            states.Dispose();
        }

        /// <summary>The households' part of Teardown: rebuilt with the next build.</summary>
        void TeardownHouses()
        {
            Households.Clear();
            housesSeen.Clear();
            housesInside.Clear();
            houseArrivals = houseWrongDoor = 0;
            houseQueryWorld = null;
            houseQuery = default;
        }
    }
}
