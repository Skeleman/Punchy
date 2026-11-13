using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// One far figure (A-109): a household member seen from afar, standing in for the folk who would be there were a
    /// player near. Where it lives (its house's door), where its day takes it (its station: a post, a worker's spot, a
    /// place to loiter) and the ground between, all worked out once from the same records the spawn reads.
    /// </summary>
    public sealed class FarFigure
    {
        /// <summary>Its house (an index into the households) and which member it is; -1 for a lodger with no house.</summary>
        public int House, Member;
        public Kin Role;
        public bool Child, Posted;
        public uint Seed;
        /// <summary>Its door and its day's place (world xz), and where it faces at work (posted folk and workers).</summary>
        public float2 Door, Station, Face;
        /// <summary>The ground along the straight way from door to station, in RouteSteps equal steps.</summary>
        public float[] Heights;
        /// <summary>The ground's slope at the station (rise per metre east and north), for its few steps about there.</summary>
        public float2 Slope;
        public float Length, Scale;
        public float4 Body, Limbs;
        /// <summary>Drawn side only: whether it's shown as a walker (not a mote), and how far into that it has eased.</summary>
        internal bool Walker;
        internal float WalkerBlend;
        internal int SeenFrame;
        /// <summary>Drawn side only: whether it looks too big to show (DetailPolicy.FarFigureMaxPixels), and how far
        /// it has faded out for that.</summary>
        internal bool Capped;
        internal float CapBlend;
        internal int CapFrame;
    }

    /// <summary>A landmark with houses and its far figures (A-109).</summary>
    public sealed class FarTown
    {
        public int Landmark;
        public string Name;
        public float3 Centre;
        public float Radius;
        public readonly List<FarFigure> Figures = new List<FarFigure>();
        /// <summary>Whether its real folk are in the world on this machine just now (the far figures then give way to
        /// them near the camera).</summary>
        public bool Real;
        internal float RealBlend;
        internal bool Drawn;
        /// <summary>The largest a far figure of this town has looked on screen while shown (reference pixels; ruling 5).</summary>
        public float MaxPixels;
        /// <summary>The same since the last log line (the -farfolk log resets it).</summary>
        public float NowPixels;
    }

    /// <summary>
    /// The far figures of every town (A-109 row 2): drawn only, so a town seen from a ridge has people in it. Nothing
    /// is simulated, sent or stored: who they are comes from the households (A-105), where their day takes them from
    /// the landmark's resident rows and village posts with every count at its smallest and no random draw, and where
    /// each is right now from the shared clock (<see cref="Place"/>), so every machine works out the same figures.
    /// FaunaDirector builds the towns; CreatureRenderSystem draws them.
    /// </summary>
    public static class FarFolk
    {
        public const int RouteSteps = 6;
        /// <summary>How tall a grown figure stands (m): what its on-screen size is measured from.</summary>
        public const float FigureHeight = 1.7f;
        public static readonly List<FarTown> Towns = new List<FarTown>();

        internal static void Clear() => Towns.Clear();

        public static float Hash01(uint seed, int k) =>
            (BiomeSampling.Scramble(seed ^ (uint)(k + 1) * 0x9E3779B9u) & 0xFFFFFFu) / 16777216f;

        /// <summary>When a figure leaves its door and when it starts back (hours), and how fast it walks (m/s).</summary>
        public static void Day(FarFigure f, out float leave, out float back, out float speed)
        {
            var u = Hash01(f.Seed, 11);
            if (f.Child)
            {
                leave = 8f + 0.5f * u;
                back = 17f + 0.1f * u;
                speed = 1.6f;
            }
            else if (f.Role == Kin.Elder && !f.Posted)
            {
                leave = 8f + u;
                back = 16.5f + 0.5f * u;
                speed = 1f;
            }
            else
            {
                leave = 6f + 0.75f * u;
                back = 17.75f + 0.35f * u;
                speed = 1.3f;
            }
        }

        /// <summary>
        /// Where a figure is at this hour of the day (seconds: the shared clock, for its small steps about its station;
        /// secondsPerHour: how long a game hour lasts). False when it is indoors. walked: metres along its way, for the
        /// stride; shown: 0 as it steps out of its door, 1 once clear of it.
        /// </summary>
        public static bool Place(FarFigure f, float hour, double seconds, float secondsPerHour,
                                 out float3 position, out float2 facing, out float walked, out float shown, out bool walking)
        {
            position = default;
            facing = new float2(0f, 1f);
            walked = 0f;
            shown = 0f;
            walking = false;
            Day(f, out var leave, out var back, out var speed);
            var perHour = math.max(secondsPerHour, 1f);
            var travel = f.Length / speed / perHour;
            if (hour < leave || hour > back + travel)
                return false;
            var arrive = leave + travel;
            // (0 at the door, 1 at the station.)
            var along = hour < arrive ? (hour - leave) / math.max(travel, 1e-5f)
                      : hour <= back ? 1f
                      : 1f - (hour - back) / math.max(travel, 1e-5f);
            along = math.saturate(along);
            var way = f.Station - f.Door;
            var dir = f.Length > 0.01f ? way / f.Length : new float2(0f, 1f);
            var at = f.Door + way * along;
            var step = along * RouteSteps;
            var k = math.min((int)step, RouteSteps - 1);
            var height = math.lerp(f.Heights[k], f.Heights[k + 1], step - k);
            walked = along * f.Length;
            walking = hour < arrive || hour > back;
            facing = hour > back ? -dir : dir;
            if (!walking)
            {
                // At its station: posted folk and workers stand facing their work, shifting a little; the rest step
                // about a few metres, a new spot every 40 s. Eased in and out over 20 s, so there's no jump at either end.
                var settle = math.saturate((hour - arrive) * perHour / 20f) * math.saturate((back - hour) * perHour / 20f);
                var reach = (f.Posted ? 0.3f : 2.5f) * settle;
                var slot = seconds / 40.0 + Hash01(f.Seed, 12);
                var n = (long)math.floor(slot);
                var t = math.smoothstep(0.55f, 1f, (float)(slot - n));
                var a = Spot(f.Seed, n);
                var b = Spot(f.Seed, n + 1);
                var offset = math.lerp(a, b, t) * reach;
                at += offset;
                height += math.dot(f.Slope, offset);
                var toward = f.Posted ? f.Face - f.Station : b - a;
                if (math.lengthsq(toward) > 1e-4f)
                    facing = math.normalize(toward);
                walking = !f.Posted && t > 0f && t < 1f && settle > 0.5f;
                walked += math.length(b - a) * reach * t + (float)(n % 16) * 0.37f;
            }
            position = new float3(at.x, height, at.y);
            var fromDoor = f.Length > 1.5f ? math.saturate(walked / 1.5f) : 1f;
            shown = (walking && hour < arrive || hour > back ? fromDoor : 1f) *
                    math.saturate((hour - leave) * perHour / 8f) * math.saturate((back + travel - hour) * perHour / 8f);
            return shown > 0f;
        }

        static float2 Spot(uint seed, long n)
        {
            var s = seed ^ (uint)(n * 0x85EBCA6BL);
            var angle = Hash01(s, 13) * math.PI * 2f;
            return new float2(math.cos(angle), math.sin(angle)) * math.sqrt(Hash01(s, 14));
        }
    }

    // A-109 row 2: builds each town's far figures and tells whether its real folk are about. On every machine (a
    // joined client too): all of it is derived, none of it touches the simulation, the spawn draws or the network.
    public partial class FaunaDirector
    {
        readonly List<Post> farPosts = new List<Post>();
        readonly Dictionary<int, uint> farTaken = new Dictionary<int, uint>();
        readonly HashSet<int> farReal = new HashSet<int>();
        EntityQuery farQuery;
        World farQueryWorld;
        float nextFarWatch;
        int farNext, farKey;

        /// <summary>
        /// A few times a second: builds the next town's far figures (one town a go, so no frame pays for them all) and
        /// marks the towns whose real folk are in the world here.
        /// </summary>
        void WatchFarFolk(in TerrainSource source)
        {
            if (Time.time < nextFarWatch || !built || !source.Landmarks.IsCreated || queryWorld == null || !queryWorld.IsCreated)
                return;
            nextFarWatch = Time.time + 0.2f;
            EnsureHouses(source);
            if (!Households.Ready)
                return;
            ref var set = ref source.Landmarks.Value;
            // (Another island, or the households rebuilt: start over.)
            var key = source.Landmarks.GetHashCode() ^ Households.All.Count * 7919;
            if (key != farKey)
            {
                farKey = key;
                farNext = 0;
                FarFolk.Clear();
            }
            for (int tries = 0; tries < 24 && farNext < set.Instances.Length; tries++)
                if (BuildFarTown(source, farNext++))
                    break;

            if (farQueryWorld != queryWorld)
            {
                farQueryWorld = queryWorld;
                farQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            }
            farReal.Clear();
            var states = farQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
                if (s.Individual != 0 && Households.Of(s.Individual, out var house, out _))
                    farReal.Add(house.Landmark);
            states.Dispose();
            foreach (var town in FarFolk.Towns)
                town.Real = farReal.Contains(town.Landmark);
        }

        /// <summary>The far figures' part of Teardown.</summary>
        void TeardownFarFolk()
        {
            FarFolk.Clear();
            farKey = 0;
            farNext = 0;
            farQueryWorld = null;
            farQuery = default;
        }

        /// <summary>
        /// One landmark's far figures, if it has houses and resident rows: the spawn's own walk through the rows
        /// (SpawnResidents), with each row at its smallest count and no random draw, so these are folk who are certain
        /// to be there. True if it made a town.
        /// </summary>
        bool BuildFarTown(in TerrainSource source, int i)
        {
            ref var set = ref source.Landmarks.Value;
            ref var inst = ref set.Instances[i];
            var type = inst.Type < landmarks.Length ? landmarks[inst.Type] : null;
            if (type == null || type.Residents == null || type.Residents.Count == 0 ||
                !Households.ByLandmark.TryGetValue(i, out var houses) || houses.Count == 0)
                return false;
            // (Igloos and domes are peopled by their own household rows, not resident rows: not far figures yet.)
            var kind = Households.All[houses[0]].Kind;
            if (kind != "house" && kind != "hut")
                return false;
            var radius = set.Types[inst.Type].Radius * inst.Scale;
            var town = new FarTown
            {
                Landmark = i,
                Name = type.name,
                Centre = new float3(inst.Position.x, inst.BaseHeight, inst.Position.y),
                Radius = radius
            };
            var freeFolk = 0;
            foreach (var r in type.Residents)
                if (FreeFolk(r))
                    freeFolk += math.max(0, r.Count.x);
            var ground = source.SampleGround(inst.Position).Biomes.Dominant();
            var workSet = ground >= 0 && ground < biomes.Length ? FolkWorkSets.For(biomes[ground]) : null;
            VillagePosts(source, inst.Position, radius, freeFolk, workSet, farPosts);
            farTaken.Clear();
            var nextPost = 0;
            var stayFree = 0;
            var row = 0;
            foreach (var r in type.Residents)
            {
                row++;
                var index = SpeciesIndex(r.Species);
                if (index < 0)
                    continue;
                var housed = HousedFolk(index, out var child);
                var worker = r.Task != CreatureTask.None;
                var stationed = r.Leash > 0f || worker;
                var home = stationed ? LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.HomeRing, r.HomeAngle), radius) : inst.Position;
                var face = worker ? LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.FaceRing, r.FaceAngle), radius) : float2.zero;
                if (worker && (r.Task == CreatureTask.Hammer || r.Task == CreatureTask.Chop) && source.Anchors.IsCreated &&
                    WorkAnchorNear(ref source.Anchors.Value, new float3(face.x, source.SampleHeight(face), face.y), 12f, out var stand, out var facing))
                {
                    home = stand;
                    face = stand + facing;
                }
                var count = math.max(0, r.Count.x);
                for (int k = 0; k < count; k++)
                {
                    var posted = false;
                    var post = default(Post);
                    if (FreeFolk(r) && nextPost < farPosts.Count && (stayFree > 0 || nextPost < freeFolk - 1))
                    {
                        post = farPosts[nextPost++];
                        posted = true;
                    }
                    else if (FreeFolk(r))
                        stayFree++;
                    if (!housed)
                        continue;
                    var slot = FarHouseSlot(houses, child);
                    var f = new FarFigure
                    {
                        House = slot >= 0 ? slot >> 4 : -1,
                        Member = slot >= 0 ? slot & 15 : -1,
                        Child = child,
                        Posted = posted || worker,
                        Scale = child ? 0.62f : 1f
                    };
                    var house = slot >= 0 ? Households.All[f.House] : null;
                    f.Role = house != null ? house.Members[f.Member].Role : child ? Kin.Child : Kin.Head;
                    f.Seed = house != null ? house.Members[f.Member].Individual
                                           : BiomeSampling.Scramble(inst.Key ^ (uint)(row * 977 + k * 31 + 1));
                    var door = house != null ? house.Door.xz : float2.zero;
                    var outward = house != null ? new float2(math.sin(house.Door.w), math.cos(house.Door.w)) : new float2(0f, 1f);
                    var side = new float2(outward.y, -outward.x);
                    float2 station;
                    if (worker)
                    {
                        station = home;
                        f.Face = face;
                    }
                    else if (posted)
                    {
                        station = post.Stand;
                        f.Face = post.Face;
                    }
                    else if (house != null && (f.Role == Kin.Elder || child && FarFolk.Hash01(f.Seed, 1) < 0.5f))
                    {
                        // An elder's short day by its own door; half the children play in front of theirs.
                        station = door + outward * (child ? 2f + 2f * FarFolk.Hash01(f.Seed, 2) : 1.5f + 2f * FarFolk.Hash01(f.Seed, 2)) +
                                  side * (FarFolk.Hash01(f.Seed, 3) * 2f - 1f) * (child ? 3f : 1.5f);
                    }
                    else
                    {
                        // Free folk about the middle of the place (the spawn's own ring; children nearer the square), on
                        // dry ground.
                        station = house != null ? door + outward * 2f : inst.Position;
                        for (int n = 0; n < 4; n++)
                        {
                            var a = FarFolk.Hash01(f.Seed, 4 + n * 2) * math.PI * 2f;
                            var d = child ? math.lerp(0.05f, 0.2f, FarFolk.Hash01(f.Seed, 5 + n * 2))
                                          : math.lerp(0.1f, 0.45f, FarFolk.Hash01(f.Seed, 5 + n * 2));
                            var p = inst.Position + new float2(math.cos(a), math.sin(a)) * d * radius;
                            var g = source.SampleGround(p);
                            if (g.HasWater && g.WaterSurface > g.Height)
                                continue;
                            station = p;
                            break;
                        }
                    }
                    f.Station = station;
                    f.Door = house != null ? door : station;
                    f.Length = math.distance(f.Door, f.Station);
                    f.Heights = new float[FarFolk.RouteSteps + 1];
                    for (int n = 0; n <= FarFolk.RouteSteps; n++)
                        f.Heights[n] = FarGround(source, math.lerp(f.Door, f.Station, n / (float)FarFolk.RouteSteps));
                    var here = f.Heights[FarFolk.RouteSteps];
                    f.Slope = new float2(FarGround(source, station + new float2(1.5f, 0f)) - here,
                                         FarGround(source, station + new float2(0f, 1.5f)) - here) / 1.5f;
                    // (Steep or broken ground by the station: stand still on it rather than float or sink.)
                    if (math.cmax(math.abs(f.Slope)) > 0.6f)
                        f.Slope = float2.zero;
                    FarColours(species[index], f.Seed, out f.Body, out f.Limbs);
                    town.Figures.Add(f);
                }
            }
            if (town.Figures.Count == 0)
                return false;
            FarFolk.Towns.Add(town);
            return true;
        }

        /// <summary>TakeHouseSlot's rule on a tally of its own (the real houses' Taken marks belong to the spawn).</summary>
        int FarHouseSlot(List<int> houses, bool child)
        {
            for (int pass = child ? 0 : 1; pass < 2; pass++)
                foreach (var h in houses)
                {
                    farTaken.TryGetValue(h, out var taken);
                    var m = FreeMember(Households.All[h], taken, child, child && pass == 0);
                    if (m < 0)
                        continue;
                    farTaken[h] = taken | 1u << m;
                    return h << 4 | m;
                }
            return -1;
        }

        /// <summary>The ground a far figure stands on: the terrain, or wading depth where that is under water.</summary>
        static float FarGround(in TerrainSource source, float2 at)
        {
            var g = source.SampleGround(at);
            return g.HasWater && g.WaterSurface > g.Height ? math.max(g.Height, g.WaterSurface - 0.4f) : g.Height;
        }

        /// <summary>A figure's body and limb colours: its species' (one of its coats, picked by its seed).</summary>
        static void FarColours(CreatureSpecies species, uint seed, out float4 body, out float4 limbs)
        {
            var b = species.Base;
            var a = species.Accent;
            if (species.Coats != null && species.Coats.Count > 0)
            {
                var coat = species.Coats[(int)(BiomeSampling.Scramble(seed ^ 0xC0A7u) % (uint)species.Coats.Count)];
                b = coat.Base;
                a = coat.Accent;
            }
            body = new float4(b.linear.r, b.linear.g, b.linear.b, 0f);
            limbs = new float4(a.linear.r, a.linear.g, a.linear.b, 0f);
        }
    }
}
