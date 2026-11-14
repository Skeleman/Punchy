using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>The kinds of named place a town's people go to (A-110): every walk ends at one.</summary>
    public enum PlaceKind : byte { Home, Step, Post, Shop, Well, Square, Garden, Bench, Green, Gate }

    /// <summary>What a town's person is doing where it is (A-110).</summary>
    public enum Doing : byte { Indoors, Walk, Carry, Work, Sit, Play, Idle }

    /// <summary>
    /// A named place in a town (A-110): a house's door (Home, indoors) and the step in front of it, a job post, a shop
    /// or hall worked inside, the well, the square, a garden, a bench, a green, the gate.
    /// </summary>
    public struct TownPlace
    {
        public PlaceKind Kind;
        /// <summary>Where it is (world xz), what someone at it faces, and the ground's slope there (rise per metre).</summary>
        public float2 At, Face, Slope;
        /// <summary>Its ground height, and how far out those at it stand from its middle (0: right on it).</summary>
        public float Height, Spread;
        /// <summary>Whether someone who has reached it is inside a building, out of sight.</summary>
        public bool Indoors;
        /// <summary>The street node its paths start from (-1: none near), and for a Home or Step its household.</summary>
        public int Node, House;
    }

    /// <summary>One household member of a town (A-110): who it is, where it lives and works, and its cached day.</summary>
    public sealed class Resident
    {
        public int Index;
        /// <summary>Its household (an index into Households.All), that house's place in the town's list, and which
        /// member of the house it is.</summary>
        public int House, Local, Member;
        public Kin Role;
        public bool Child;
        public uint Seed;
        public float Scale, Speed;
        public float4 Body, Limbs;
        /// <summary>Its own door, the step in front of it, and its job's place (-1: none), as town places.</summary>
        public int Home, Step, Job;
        /// <summary>Its day, and one more kept beside it (the day before or after, or one a check asked for).</summary>
        internal DayPlan Plan, Spare;
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

    /// <summary>One stop of a day: a place, when the member gets there and when it leaves, and the way to it.</summary>
    struct Stop
    {
        public int Place;
        public float3 At;
        public float Arrive, Leave;
        public Doing Doing;
        /// <summary>Whether it carries something on the way here (water from the well).</summary>
        public bool Carry;
        /// <summary>The way here from the stop before: its points, and the metres walked at each.</summary>
        public float3[] Way;
        public float[] Along;
        public float Length;
    }

    /// <summary>A member's day (at most Whereabouts.MaxStops stops), built once per folk day.</summary>
    sealed class DayPlan
    {
        public int Day;
        public float PerHour;
        public readonly List<Stop> Stops = new List<Stop>(Whereabouts.MaxStops);
    }

    /// <summary>Where a household member is at a moment (A-110).</summary>
    public struct Whereabout
    {
        public bool Outdoors;
        public float3 Position;
        public float2 Facing;
        public Doing Doing;
        /// <summary>Metres along its way (for the stride), and 0 as it steps out of a door to 1 once clear of it.</summary>
        public float Walked, Shown;
        /// <summary>The household it is inside (an index into Households.All), or -1 when outdoors or in a shop.</summary>
        public int Inside;
        /// <summary>The ends of its leg (town places): where it is or was last, and where it is going (the same when it
        /// is stopped).</summary>
        public int From, To;
    }

    /// <summary>
    /// A town's records as its people use them (A-110): its houses, named places, the street graph between them, a
    /// coarse ground grid for the ways no street serves, and every household member. Built by FaunaDirector from the
    /// baked records alone (households, settlement, anchors, the landmark's resident rows), with no random draw, so
    /// every machine builds the same plan.
    /// </summary>
    public sealed class TownPlan
    {
        public int Landmark;
        public string Name;
        public float2 Centre;
        public float Radius;
        /// <summary>Its households (indices into Households.All), from the middle out, and each one's members here.</summary>
        public readonly List<int> Houses = new List<int>();
        public readonly List<List<int>> HouseResidents = new List<List<int>>();
        public readonly Dictionary<Household, int> LocalOf = new Dictionary<Household, int>();
        public readonly List<TownPlace> Places = new List<TownPlace>();
        public readonly List<Resident> Residents = new List<Resident>();
        /// <summary>Places by use: where folk meet (squares, benches, greens), where children play, where water is
        /// fetched, where the unposted work or shop, and the gate the watch keeps (-1: none).</summary>
        public readonly List<int> Social = new List<int>(), Play = new List<int>(), Wells = new List<int>(), Errands = new List<int>();
        /// <summary>Every house's doorstep (for calling on a neighbour), in the order of Houses.</summary>
        public readonly List<int> Steps = new List<int>();
        public int Gate = -1;
        /// <summary>The members who take turns at the watch (those living nearest the gate).</summary>
        public readonly List<int> Watch = new List<int>();
        /// <summary>The street graph: nodes (world, with height) and each node's (neighbour, metres) links.</summary>
        public readonly List<float3> Nodes = new List<float3>();
        public readonly List<List<(int to, float length)>> Links = new List<List<(int, float)>>();
        readonly Dictionary<int, int[]> paths = new Dictionary<int, int[]>();
        readonly Dictionary<int, float[]> lengths = new Dictionary<int, float[]>();
        /// <summary>The ground on a square grid over the town, for the ways no street serves.</summary>
        public float[] Grid;
        public int GridSize;
        public float2 GridMin;
        public float GridCell = 1f;
        /// <summary>How many days built so far had a member not home by the end of the folk day (should stay 0).</summary>
        public int Overruns;
        /// <summary>A line about how it was put together, for the logs.</summary>
        public string Made;
        /// <summary>Its harsh biome's work set by name (null: a gentle biome), and how many of its posts are that set's.</summary>
        public string WorkSet;
        public int WorkSetPosts;

        /// <summary>The ground at a point, from the grid.</summary>
        public float Ground(float2 at)
        {
            if (Grid == null || GridSize < 2)
                return 0f;
            var p = math.clamp((at - GridMin) / GridCell, 0f, GridSize - 1.001f);
            var c = (int2)math.floor(p);
            var f = p - c;
            var i = c.y * GridSize + c.x;
            return math.lerp(math.lerp(Grid[i], Grid[i + 1], f.x), math.lerp(Grid[i + GridSize], Grid[i + GridSize + 1], f.x), f.y);
        }

        /// <summary>The street node nearest a point within reach, or -1.</summary>
        public int NodeNear(float2 at, float reach)
        {
            var best = -1;
            var bestD = reach * reach;
            for (int n = 0; n < Nodes.Count; n++)
            {
                var d = math.distancesq(Nodes[n].xz, at);
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>Shortest ways from one node to all (the node before each on its way; -1: not reached).</summary>
        int[] PathsFrom(int source)
        {
            if (paths.TryGetValue(source, out var before))
                return before;
            var n = Nodes.Count;
            before = new int[n];
            var far = new float[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                before[i] = -1;
                far[i] = float.MaxValue;
            }
            far[source] = 0f;
            before[source] = source;
            for (int pass = 0; pass < n; pass++)
            {
                var at = -1;
                var best = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && far[i] < best)
                    {
                        best = far[i];
                        at = i;
                    }
                if (at < 0)
                    break;
                done[at] = true;
                foreach (var (to, length) in Links[at])
                    if (best + length < far[to])
                    {
                        far[to] = best + length;
                        before[to] = at;
                    }
            }
            paths[source] = before;
            lengths[source] = far;
            return before;
        }

        /// <summary>About how far the walk between two places is (m): by the streets where Route would take them,
        /// straight otherwise. For choosing where to go, so nobody is sent on a walk that takes half its day.</summary>
        internal float Walk(int fromPlace, int toPlace)
        {
            var from = Places[fromPlace];
            var to = Places[toPlace];
            var straight = math.distance(from.At, to.At);
            if (from.Node < 0 || to.Node < 0 || straight <= 12f || PathsFrom(to.Node)[from.Node] < 0)
                return straight;
            return lengths[to.Node][from.Node] + math.distance(from.At, Nodes[from.Node].xz) + math.distance(to.At, Nodes[to.Node].xz);
        }

        readonly List<float3> way = new List<float3>();

        /// <summary>
        /// The way from one point to another, each by a place: along the streets where both have a street node and a
        /// path joins them, straight over the ground otherwise (a camp with no streets, or a place off them).
        /// </summary>
        internal void Route(float3 from, int fromPlace, float3 to, int toPlace, out float3[] points, out float[] along)
        {
            way.Clear();
            way.Add(from);
            var a = Places[fromPlace].Node;
            var b = Places[toPlace].Node;
            var straight = math.distance(from.xz, to.xz);
            var byStreet = a >= 0 && b >= 0 && straight > 12f;
            if (byStreet)
            {
                var before = PathsFrom(b);   // (from b, so walking the chain from a runs forwards)
                if (before[a] < 0)
                    byStreet = false;
                else
                {
                    var first = way.Count;
                    for (int n = a, guard = 0; guard <= Nodes.Count; guard++)
                    {
                        way.Add(Nodes[n]);
                        if (n == b)
                            break;
                        n = before[n];
                    }
                    // (Not back past its own door: skip an end node the next one is nearer to than it is.)
                    if (way.Count - first >= 2 && math.distancesq(from.xz, way[first + 1].xz) <= math.distancesq(way[first].xz, way[first + 1].xz))
                        way.RemoveAt(first);
                    var last = way.Count - 1;
                    if (last - first >= 1 && math.distancesq(to.xz, way[last - 1].xz) <= math.distancesq(way[last].xz, way[last - 1].xz))
                        way.RemoveAt(last);
                }
            }
            if (!byStreet)
            {
                var steps = math.max(1, (int)math.ceil(straight / 5f));
                for (int k = 1; k < steps; k++)
                {
                    var p = math.lerp(from.xz, to.xz, k / (float)steps);
                    way.Add(new float3(p.x, Ground(p), p.y));
                }
            }
            way.Add(to);
            points = way.ToArray();
            along = new float[points.Length];
            for (int k = 1; k < points.Length; k++)
                along[k] = along[k - 1] + math.distance(points[k - 1].xz, points[k].xz);
        }
    }

    /// <summary>
    /// Where every household member of a town is at any moment (A-110 row 2): indoors or out, where, facing which way
    /// and doing what. A pure function of the town's plan (built from the baked records), the member, the day and the
    /// hour: nothing is simulated, sent or stored, so every machine gets the same answer. Each member's day is a short
    /// list of stops at named places (its own door, its job, the well, the square, a garden, a neighbour's door, a
    /// bench, the gate), built from its Individual and the day and cached; between stops it walks the streets at a
    /// fixed speed, so its position never jumps. The folk day turns over at 4 h, when all but the dawn watch are home.
    /// Jobs are handed out in a fixed order by FaunaDirector (FinishTownPlan); row 6's exceptions (the dead, the moved)
    /// will override single members and are not read here yet.
    /// </summary>
    public static class Whereabouts
    {
        public const int MaxStops = 8;
        /// <summary>The hour the folk day turns over.</summary>
        public const float Turn = 4f;
        /// <summary>The shortest stay at a stop (hours).</summary>
        const float MinStay = 0.3f;
        /// <summary>How long a game hour lasts (s): the shared clock's rate, set by whoever reads the clock.</summary>
        public static float SecondsPerHour = 60f;

        /// <summary>Every town's plan, by landmark.</summary>
        public static readonly Dictionary<int, TownPlan> Towns = new Dictionary<int, TownPlan>();

        internal static void Clear() => Towns.Clear();

        static int Mod(int a, int n) => (a % n + n) % n;

        /// <summary>A calendar day and hour as a folk day and its hour (4 to 28).</summary>
        public static void FolkDay(ref int day, ref float hour)
        {
            if (hour < Turn)
            {
                day--;
                hour += 24f;
            }
        }

        /// <summary>Whether a member's day is built for this moment (building one costs a few path lookups).</summary>
        public static bool Built(TownPlan town, int index, int day, float hour)
        {
            FolkDay(ref day, ref hour);
            var r = town.Residents[index];
            return Is(r.Plan, day) || Is(r.Spare, day);
        }

        /// <summary>Builds a member's day for this moment ahead of its being asked for (the day about to turn),
        /// beside the one it is living now.</summary>
        public static void Prepare(TownPlan town, int index, int day, float hour)
        {
            FolkDay(ref day, ref hour);
            var r = town.Residents[index];
            if (!Is(r.Plan, day) && !Is(r.Spare, day))
                r.Spare = Build(town, r, day);
        }

        static bool Is(DayPlan plan, int folkDay) => plan != null && plan.Day == folkDay && plan.PerHour == SecondsPerHour;

        static DayPlan DayOf(TownPlan town, Resident r, int folkDay)
        {
            if (Is(r.Plan, folkDay))
                return r.Plan;
            if (Is(r.Spare, folkDay))
                (r.Plan, r.Spare) = (r.Spare, r.Plan);
            else
            {
                r.Spare = r.Plan;
                r.Plan = Build(town, r, folkDay);
            }
            return r.Plan;
        }

        /// <summary>Where a member stands at a place: on it, or out from its middle by the place's spread.</summary>
        static float3 Stand(TownPlan town, Resident r, int place)
        {
            var p = town.Places[place];
            var at = p.At;
            var height = p.Height;
            if (p.Spread > 0f)
            {
                var angle = FarFolk.Hash01(r.Seed, 30 + place) * math.PI * 2f;
                var offset = new float2(math.cos(angle), math.sin(angle)) * p.Spread * (0.5f + 0.5f * FarFolk.Hash01(r.Seed, 31 + place));
                at += offset;
                height += math.dot(p.Slope, offset);
            }
            return new float3(at.x, height, at.y);
        }

        static Doing DoingAt(TownPlan town, Resident r, int place)
        {
            var p = town.Places[place];
            if (p.Indoors)
                return Doing.Indoors;
            switch (p.Kind)
            {
                case PlaceKind.Post:
                case PlaceKind.Garden:
                    return r.Child ? Doing.Play : Doing.Work;
                case PlaceKind.Bench:
                    return Doing.Sit;
                case PlaceKind.Step:
                    return r.Child ? Doing.Play : r.Role == Kin.Elder ? Doing.Sit : place == r.Step ? Doing.Work : Doing.Idle;
                case PlaceKind.Square:
                case PlaceKind.Green:
                    return r.Child ? Doing.Play : Doing.Idle;
                default:
                    return Doing.Idle;
            }
        }

        /// <summary>Leaves the last stop at an hour (never before a short stay there) and walks to a place.</summary>
        static void Go(TownPlan town, Resident r, DayPlan plan, int place, float leave)
        {
            var stops = plan.Stops;
            if (place < 0)
                return;
            var last = stops[stops.Count - 1];
            if (last.Place == place)
                return;
            if (stops.Count >= MaxStops)
            {
                // (A day planned with more stops than it can hold: counted, so the checks catch it.)
                town.Overruns++;
                return;
            }
            last.Leave = stops.Count == 1 ? math.max(leave, Turn + 0.02f) : math.max(leave, last.Arrive + MinStay);
            stops[stops.Count - 1] = last;
            var stop = new Stop
            {
                Place = place,
                At = Stand(town, r, place),
                Leave = float.MaxValue,
                Doing = DoingAt(town, r, place),
                Carry = town.Places[last.Place].Kind == PlaceKind.Well
            };
            town.Route(last.At, last.Place, stop.At, place, out stop.Way, out stop.Along);
            stop.Length = stop.Along[stop.Along.Length - 1];
            stop.Arrive = last.Leave + stop.Length / r.Speed / plan.PerHour;
            stops.Add(stop);
        }

        static float Arrived(DayPlan plan) => plan.Stops[plan.Stops.Count - 1].Arrive;

        /// <summary>The longest walk anyone sets out on for company, an errand or play (m): past it, its own step does.</summary>
        const float FarthestWalk = 90f;

        /// <summary>One of the three places of a list that are the shortest walk from a member's door (so nobody
        /// crosses a whole town for a bench), or otherwise when the list is empty or all of it is too far. Ties go to
        /// the earlier place, so every machine picks the same.</summary>
        static int Near(TownPlan town, Resident r, List<int> from, float u, int otherwise)
        {
            int a = -1, b = -1, c = -1, found = 0;
            float da = float.MaxValue, db = float.MaxValue, dc = float.MaxValue;
            foreach (var place in from)
            {
                var d = town.Walk(r.Step, place);
                if (place == r.Step || d > FarthestWalk)
                    continue;
                found++;
                if (d < da)
                    (a, da, b, db, c, dc) = (place, d, a, da, b, db);
                else if (d < db)
                    (b, db, c, dc) = (place, d, b, db);
                else if (d < dc)
                    (c, dc) = (place, d);
            }
            if (found == 0)
                return otherwise;
            var k = math.min((int)(u * math.min(found, 3)), 2);
            return k == 0 ? a : k == 1 ? b : c;
        }

        static DayPlan Build(TownPlan town, Resident r, int day)
        {
            var plan = new DayPlan { Day = day, PerHour = SecondsPerHour };
            var seed = BiomeSampling.Scramble(r.Seed + (uint)day * 0x9E3779B9u);
            float U(int k) => FarFolk.Hash01(seed, k);

            // The watch: three of the rota a night (evening, late, dawn), the dawn one still at the gate when the day
            // turns, so it starts its next day there.
            var turn = town.Gate >= 0 && town.Watch.Count >= 4 ? town.Watch.IndexOf(r.Index) : -1;
            var n = town.Watch.Count;
            var evening = turn >= 0 && turn == Mod(3 * day, n);
            var late = turn >= 0 && turn == Mod(3 * day + 1, n);
            var dawn = turn >= 0 && turn == Mod(3 * day + 2, n);
            var wasDawn = turn >= 0 && turn == Mod(3 * day - 1, n);

            var start = wasDawn ? town.Gate : r.Home;
            plan.Stops.Add(new Stop
            {
                Place = start,
                At = Stand(town, r, start),
                Arrive = float.MinValue,
                Leave = float.MaxValue,
                Doing = DoingAt(town, r, start)
            });
            var social = Near(town, r, town.Social, U(20), r.Step);
            if (U(21) < 0.35f)
                social = Near(town, r, town.Steps, U(22), r.Step);

            if (wasDawn)
            {
                // Off the watch at first light, home to sleep; an afternoon at its work if it has any.
                Go(town, r, plan, r.Home, 6.6f + 0.3f * U(1));
                if (r.Job >= 0)
                {
                    Go(town, r, plan, r.Job, 12.9f + 0.4f * U(2));
                    Go(town, r, plan, r.Home, 17.5f + 0.5f * U(3));
                }
            }
            else if (r.Child)
            {
                // Lessons and chores indoors for half the day; out to play for the other half, and for some a while
                // on their own step as well.
                var morning = U(1) < 0.5f;
                if (!morning && U(2) < 0.35f)
                {
                    Go(town, r, plan, r.Step, 8.2f + 2f * U(3));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.5f + 0.5f * U(4));
                }
                Go(town, r, plan, Near(town, r, town.Play, U(5), r.Step), morning ? 7.5f + 3.5f * U(6) : 13f + 3.5f * U(6));
                Go(town, r, plan, r.Home, Arrived(plan) + 1.2f + 1.2f * U(7));
                if (morning && U(2) < 0.35f)
                {
                    Go(town, r, plan, r.Step, 14f + 2f * U(3));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.5f + 0.5f * U(4));
                }
            }
            else if (r.Role == Kin.Elder)
            {
                Go(town, r, plan, r.Step, 8f + 3f * U(1));
                Go(town, r, plan, r.Home, Arrived(plan) + 1f + U(2));
                Go(town, r, plan, social, 14f + 2.5f * U(3));
                Go(town, r, plan, r.Home, Arrived(plan) + 1.2f + 0.8f * U(4));
            }
            else if (r.Job >= 0)
            {
                Go(town, r, plan, r.Job, 6f + 0.75f * U(1));
                var lunch = U(2);
                if (lunch < 0.85f)
                {
                    Go(town, r, plan, lunch < 0.5f ? r.Home : social, 11.75f + 0.5f * U(3));
                    Go(town, r, plan, r.Job, 12.75f + 0.5f * U(4));
                }
                Go(town, r, plan, r.Home, 17.5f + 0.5f * U(5));
                if (!evening && !late && !dawn && U(6) < 0.3f)
                {
                    Go(town, r, plan, social, 18.5f + 0.5f * U(7));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.4f + 0.5f * U(8));
                }
            }
            else
            {
                // No post: the house keeps it in most of the day. Water first thing (where there is a well, half of
                // them), an errand or a neighbour some time in the morning, for half a chore on its own step or a
                // while on the green in the afternoon (not before a night on the watch), and now and then an evening out.
                if (town.Wells.Count > 0 && U(15) < 0.5f)
                {
                    Go(town, r, plan, Near(town, r, town.Wells, U(1), r.Step), 6.6f + 1.2f * U(2));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.3f + 0.3f * U(3));
                }
                if (U(16) < 0.75f)
                {
                    Go(town, r, plan, U(4) < 0.7f ? Near(town, r, town.Errands, U(5), social) : social, 8f + 5f * U(6));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.4f + 0.6f * U(7));
                }
                var onWatch = evening || late || dawn;
                if (!onWatch && U(8) < 0.5f)
                {
                    Go(town, r, plan, U(9) < 0.5f ? r.Step : social, 14f + 3.5f * U(10));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.6f + 0.7f * U(11));
                }
                else if (!onWatch && U(12) < 0.4f)
                {
                    Go(town, r, plan, social, 18.5f + 0.5f * U(13));
                    Go(town, r, plan, r.Home, Arrived(plan) + 0.4f + 0.5f * U(14));
                }
            }
            if (evening)
            {
                Go(town, r, plan, town.Gate, 20f);
                Go(town, r, plan, r.Home, 24f);
            }
            else if (late)
            {
                Go(town, r, plan, town.Gate, 23.7f);
                Go(town, r, plan, r.Home, 26.4f);
            }
            else if (dawn)
                Go(town, r, plan, town.Gate, 26.1f);
            if (Arrived(plan) > 24f + Turn - 0.05f)
                town.Overruns++;
            return plan;
        }

        /// <summary>How far a member at a stop moves about it (m).</summary>
        static float Reach(Doing doing) =>
            doing == Doing.Play ? 4f : doing == Doing.Idle ? 1.5f : doing == Doing.Work ? 0.3f : 0f;

        static float2 Spot(uint seed, long n)
        {
            var s = seed ^ (uint)(n * 0x85EBCA6BL);
            var angle = FarFolk.Hash01(s, 13) * math.PI * 2f;
            return new float2(math.cos(angle), math.sin(angle)) * math.sqrt(FarFolk.Hash01(s, 14));
        }

        /// <summary>
        /// Where a member is at this hour (0 to 24) of this day. seconds: the shared clock, for its small steps about
        /// a place it has stopped at.
        /// </summary>
        public static Whereabout Of(TownPlan town, int index, int day, float hour, double seconds)
        {
            FolkDay(ref day, ref hour);
            var r = town.Residents[index];
            var plan = DayOf(town, r, day);
            var stops = plan.Stops;
            var k = stops.Count - 1;
            while (k > 0 && hour < stops[k].Arrive)
                k--;
            var stop = stops[k];
            var w = new Whereabout { Inside = -1, From = stop.Place, To = stop.Place, Facing = new float2(0f, 1f) };
            var place = town.Places[stop.Place];
            if (k < stops.Count - 1 && hour >= stop.Leave)
            {
                // On its way to the next stop.
                var next = stops[k + 1];
                var gone = math.clamp((hour - stop.Leave) * plan.PerHour * r.Speed, 0f, next.Length);
                var seg = 1;
                while (seg < next.Along.Length - 1 && next.Along[seg] < gone)
                    seg++;
                var span = next.Along[seg] - next.Along[seg - 1];
                var t = span > 1e-4f ? (gone - next.Along[seg - 1]) / span : 1f;
                w.Position = math.lerp(next.Way[seg - 1], next.Way[seg], t);
                var dir = next.Way[seg].xz - next.Way[seg - 1].xz;
                if (math.lengthsq(dir) > 1e-6f)
                    w.Facing = math.normalize(dir);
                w.Outdoors = true;
                w.Doing = next.Carry ? Doing.Carry : Doing.Walk;
                w.Walked = gone;
                w.To = next.Place;
                // (Out of a door, or in at one: shown over the first and last metre and a half.)
                w.Shown = (place.Indoors ? math.saturate(gone / 1.5f) : 1f) *
                          (town.Places[next.Place].Indoors ? math.saturate((next.Length - gone) / 1.5f) : 1f);
                return w;
            }
            w.Doing = stop.Doing;
            w.Position = stop.At;
            if (place.Indoors)
            {
                w.Inside = place.Kind == PlaceKind.Home ? place.House : -1;
                return w;
            }
            w.Outdoors = true;
            w.Shown = 1f;
            w.Walked = stop.Length;
            var toward = place.Face - stop.At.xz;
            var reach = Reach(stop.Doing);
            if (reach > 0f)
            {
                // About the place: a new spot every 40 s, eased in and out over 20 s so there's no jump at either end.
                var settle = math.saturate((hour - stop.Arrive) * plan.PerHour / 20f) * math.saturate((stop.Leave - hour) * plan.PerHour / 20f);
                reach *= settle;
                var slot = seconds / 40.0 + FarFolk.Hash01(r.Seed, 12);
                var n = (long)math.floor(slot);
                var t = math.smoothstep(0.55f, 1f, (float)(slot - n));
                var a = Spot(r.Seed, n);
                var b = Spot(r.Seed, n + 1);
                var offset = math.lerp(a, b, t) * reach;
                w.Position += new float3(offset.x, math.dot(place.Slope, offset), offset.y);
                if (stop.Doing != Doing.Work)
                {
                    toward = b - a;
                    if (t > 0f && t < 1f && settle > 0.5f)
                        w.Doing = stop.Doing == Doing.Play ? Doing.Play : Doing.Walk;
                    w.Walked += math.length(b - a) * reach * t + (n % 16) * 0.37f;
                }
            }
            if (math.lengthsq(toward) > 1e-4f)
                w.Facing = math.normalize(toward);
            return w;
        }

        /// <summary>When the first grown member of a house (not an elder) leaves it in the morning, and when the first
        /// is back in after the working day (hours; 24 for none).</summary>
        public static void WorkHours(TownPlan town, int local, int day, out float firstOut, out float firstBack)
        {
            firstOut = firstBack = 24f;
            foreach (var index in town.HouseResidents[local])
            {
                var r = town.Residents[index];
                if (r.Child || r.Role == Kin.Elder)
                    continue;
                var stops = DayOf(town, r, day).Stops;
                if (stops.Count > 1 && stops[0].Place == r.Home)
                    firstOut = math.min(firstOut, stops[0].Leave);
                for (int k = 1; k < stops.Count; k++)
                    if (stops[k].Place == r.Home && stops[k].Arrive >= 15f)
                    {
                        firstBack = math.min(firstBack, stops[k].Arrive);
                        break;
                    }
            }
        }

        /// <summary>Who is inside a house at this hour: how many of its members, and how many of them grown.
        /// into, if given, gets their indices among the town's residents (for the player lane's interiors).</summary>
        public static int InHouse(TownPlan town, int local, int day, float hour, out int grown, List<int> into = null)
        {
            var home = 0;
            grown = 0;
            var house = town.Houses[local];
            foreach (var index in town.HouseResidents[local])
            {
                if (Of(town, index, day, hour, 0.0).Inside != house)
                    continue;
                home++;
                if (!town.Residents[index].Child)
                    grown++;
                into?.Add(index);
            }
            return home;
        }

        /// <summary>How many of a town's members are outdoors at this hour.</summary>
        public static int Outdoors(TownPlan town, int day, float hour)
        {
            var count = 0;
            for (int i = 0; i < town.Residents.Count; i++)
                if (Of(town, i, day, hour, 0.0).Outdoors)
                    count++;
            return count;
        }

        /// <summary>The shared clock's seconds at an hour of a day.</summary>
        public static double Seconds(int day, float hour) => (day * 24.0 + hour) * SecondsPerHour;

        /// <summary>
        /// A digest of a town's day, a quarter hour at a time: each member's indoors or out, the place it is at or
        /// bound for, and its position to 10 cm. For checking that two machines agree. peak: the most outdoors at once,
        /// and peakHour when.
        /// </summary>
        public static uint Digest(TownPlan town, int day, out int peak, out float peakHour)
        {
            var digest = 2166136261u;
            peak = 0;
            peakHour = 0f;
            for (int q = 0; q < 96; q++)
            {
                var hour = q * 0.25f;
                var seconds = Seconds(day, hour);
                var outdoors = 0;
                for (int i = 0; i < town.Residents.Count; i++)
                {
                    var w = Of(town, i, day, hour, seconds);
                    digest = (digest ^ (uint)(w.Outdoors ? 1 : 0)) * 16777619u;
                    digest = (digest ^ (uint)(w.To + 1)) * 16777619u;
                    if (!w.Outdoors)
                        continue;
                    outdoors++;
                    var p = (int3)math.round(w.Position * 10f);
                    digest = (digest ^ (uint)p.x) * 16777619u;
                    digest = (digest ^ (uint)p.y) * 16777619u;
                    digest = (digest ^ (uint)p.z) * 16777619u;
                }
                if (outdoors > peak)
                {
                    peak = outdoors;
                    peakHour = hour;
                }
            }
            return digest;
        }

        /// <summary>
        /// Walks a town through a folk day and into the next, two seconds at a time, and counts the breaks: a member
        /// outdoors who moved further than its walking speed allows, or one who went in or came out anywhere but at a
        /// door. worst: the largest step over the two seconds, as a multiple of walking speed.
        /// </summary>
        public static int Breaks(TownPlan town, int day, out float worst)
        {
            const float step = 2f;
            var breaks = 0;
            worst = 0f;
            var perHour = SecondsPerHour;
            var steps = (int)(24.5f * perHour / step);
            var before = new Whereabout[town.Residents.Count];
            for (int s = 0; s <= steps; s++)
            {
                var hours = Turn + s * step / perHour;
                var d = day + (int)math.floor(hours / 24f);
                var hour = hours - 24f * math.floor(hours / 24f);
                var seconds = (day * 24.0 + Turn) * perHour + s * (double)step;
                for (int i = 0; i < town.Residents.Count; i++)
                {
                    var r = town.Residents[i];
                    var w = Of(town, i, d, hour, seconds);
                    var was = before[i];
                    before[i] = w;
                    if (s == 0)
                        continue;
                    if (w.Outdoors && was.Outdoors)
                    {
                        var ratio = math.distance(w.Position.xz, was.Position.xz) / (r.Speed * step);
                        worst = math.max(worst, ratio);
                        if (ratio > 1.25f)
                            breaks++;
                    }
                    else if (w.Outdoors != was.Outdoors)
                    {
                        // (The door it used: where it was inside, or where it now is.)
                        var door = town.Places[w.Outdoors ? was.To : w.To];
                        var at = w.Outdoors ? w.Position : was.Position;
                        if (!door.Indoors || math.distance(at.xz, door.At) > 1.5f + r.Speed * step * 1.25f)
                            breaks++;
                    }
                }
            }
            return breaks;
        }
    }
}
