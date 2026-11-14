using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>A town of houses seen from afar (A-109, A-110): its plan, and how its far figures are being drawn.</summary>
    public sealed class FarTown
    {
        public int Landmark;
        public string Name;
        public float3 Centre;
        public float Radius;
        /// <summary>Its people and their places: what Whereabouts reads.</summary>
        public TownPlan Plan;
        /// <summary>Whether its real folk are in the world on this machine just now (the far figures then give way to
        /// them near the camera).</summary>
        public bool Real;
        internal bool Drawn;
        /// <summary>The largest a far figure of this town has looked on screen while shown (reference pixels; ruling 5).</summary>
        public float MaxPixels;
        /// <summary>The same since the last log line (the -farfolk log resets it).</summary>
        public float NowPixels;
        /// <summary>The last frame it was drawn: the day and hour, how many members Whereabouts put outdoors (Placed),
        /// how many of those were drawn after the fades (Shown), how many were left out as dead (Gone), and how many
        /// were waiting for their day to be built (Unbuilt). StatFrame is that frame's number (-1: never drawn).</summary>
        public int StatDay, Placed, Shown, Gone, Unbuilt, StatFrame = -1;
        /// <summary>How many members had their own creature in the world on this machine that frame (row 3).</summary>
        public int Live;
        public float StatHour;
        /// <summary>The -farfolk log's figures for a day: the most outdoors at once, when, and the day's digest.</summary>
        internal int PeakDay = int.MinValue, Peak;
        internal float PeakHour;
        internal uint Digest;
        internal bool MadeLogged;
    }

    /// <summary>
    /// The far figures of every town (A-109 row 2, A-110 row 2): drawn only, so a town seen from a ridge has people
    /// in it. Nothing is simulated, sent or stored: every household member who is outdoors by Whereabouts is drawn
    /// where Whereabouts puts it, so every machine draws the same figures. FaunaDirector builds the towns' plans;
    /// CreatureRenderSystem draws them.
    /// </summary>
    public static class FarFolk
    {
        /// <summary>How tall a grown figure stands (m): what its on-screen size is measured from.</summary>
        public const float FigureHeight = 1.7f;
        public static readonly List<FarTown> Towns = new List<FarTown>();
        /// <summary>Whether every town's plan has been built (they are built one at a time after the world loads).</summary>
        public static bool Complete;
        /// <summary>The number of the director's latest look at the creatures in the world (row 3): a member whose
        /// LiveScan is this has its creature here now.</summary>
        public static int Scan;

        static bool logRead, log;
        /// <summary>The -farfolk launch flag: the far figures' and the members' log lines.</summary>
        public static bool Log
        {
            get
            {
                if (!logRead)
                {
                    logRead = true;
                    log = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-farfolk") >= 0;
                }
                return log;
            }
        }

        internal static void Clear()
        {
            Towns.Clear();
            Complete = false;
        }

        public static float Hash01(uint seed, int k) =>
            (BiomeSampling.Scramble(seed ^ (uint)(k + 1) * 0x9E3779B9u) & 0xFFFFFFu) / 16777216f;
    }

    // A-109 row 2, A-110 rows 2 and 3: has each town's plan built (FaunaDirector.TownPlan) and notes, member by
    // member, whose creature is in the world here (the far figure gives way to it). On every machine (a joined client
    // too): all of it is derived or read from creature state the machine already has; none of it touches the
    // simulation or the network.
    public partial class FaunaDirector
    {
        readonly HashSet<int> farReal = new HashSet<int>();
        /// <summary>The members whose creatures were here at the last look and at this one (row 4: one gone from here
        /// while going indoors went in at its door).</summary>
        List<(TownPlan Plan, Resident Member)> farSeen = new List<(TownPlan, Resident)>(), farSeenNow = new List<(TownPlan, Resident)>();
        /// <summary>Landmarks whose plan was started ahead of their turn (a town near the camera goes first).</summary>
        readonly HashSet<int> farTried = new HashSet<int>();
        EntityQuery farQuery;
        World farQueryWorld;
        float nextFarWatch;
        int farNext, farKey;

        /// <summary>
        /// A few times a second: builds a little more of the next town's plan (so no frame pays for a whole town) and
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
                farSeen.Clear();
                farNext = 0;
                farTried.Clear();
                planPending = null;
                planNear = false;
                FarFolk.Clear();
                Whereabouts.Clear();
            }
            // A town near the camera first (row 3: its members can't spawn as themselves till its plan is built); a
            // plan holds the same whatever order the plans are built in.
            if (planPending == null && !FarFolk.Complete && Camera.main != null)
            {
                var eye = ((float3)Camera.main.transform.position).xz;
                for (int i = 0; i < set.Instances.Length && planPending == null; i++)
                {
                    if (i < farNext || farTried.Contains(i) || math.distance(set.Instances[i].Position, eye) > 500f)
                        continue;
                    farTried.Add(i);
                    planPending = StartTownPlan(source, i);
                    planCursor = 0;
                    planNear = planPending != null;
                }
            }
            for (int tries = 0; planPending == null && tries < 24 && farNext < set.Instances.Length; tries++)
            {
                if (farTried.Contains(farNext))
                {
                    farNext++;
                    continue;
                }
                planPending = StartTownPlan(source, farNext++);
                planCursor = 0;
                planNear = false;
            }
            if (planPending != null && FillTownGrid(source, planPending, ref planCursor, planNear ? 2400 : 600))
            {
                var plan = planPending;
                planPending = null;
                if (FinishTownPlan(source, plan))
                {
                    ref var inst = ref set.Instances[plan.Landmark];
                    Whereabouts.Towns[plan.Landmark] = plan;
                    FarFolk.Towns.Add(new FarTown
                    {
                        Landmark = plan.Landmark,
                        Name = plan.Name,
                        Centre = new float3(inst.Position.x, inst.BaseHeight, inst.Position.y),
                        Radius = plan.Radius,
                        Plan = plan
                    });
                }
            }
            FarFolk.Complete = planPending == null && farNext >= set.Instances.Length;

            if (farQueryWorld != queryWorld)
            {
                farQueryWorld = queryWorld;
                farQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            }
            // Whose creature is here (row 3): each member's own, where it is and how it moves, so its far figure
            // gives way to it and stands where it stands while it fades.
            farReal.Clear();
            FarFolk.Scan++;
            var now = Time.time;
            var states = farQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
            {
                if (s.Individual == 0 || !Households.Of(s.Individual, out var house, out var m))
                    continue;
                farReal.Add(house.Landmark);
                if (!Whereabouts.Towns.TryGetValue(house.Landmark, out var plan) || !plan.LocalOf.TryGetValue(house, out var local) ||
                    m >= plan.HouseResidents[local].Count)
                    continue;
                var r = plan.Residents[plan.HouseResidents[local][m]];
                var known = r.LiveScan == FarFolk.Scan - 1 && now > r.LiveTime;
                r.LiveVelocity = known ? (s.Position - r.LivePosition) / (now - r.LiveTime) : float3.zero;
                // (A jump, not a walk: a rider set down, a test moving it.)
                if (math.lengthsq(r.LiveVelocity) > 100f)
                    r.LiveVelocity = float3.zero;
                r.LivePosition = s.Position;
                r.LiveFacing = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                r.LiveClock = s.Clock;
                r.LiveTime = now;
                r.LiveIndoors = s.Mode == CreatureMode.Home && s.Stage >= 1;
                r.LiveScan = FarFolk.Scan;
                farSeenNow.Add((plan, r));
                if (!known && FarFolk.Log)
                    LogMemberSeen(plan, r, s);
            }
            states.Dispose();
            // Row 4: a member whose creature went from here while going in at a door is in there (the host lets it go
            // once inside, as a joined client sees by its ghost going): its far figure stays hidden till Whereabouts has
            // it come out. (The host notes it as it lets it go; this is the same note on every machine.)
            if (farSeen.Count > 0)
            {
                var noted = false;
                int day = 0;
                float hour = 0f;
                double seconds = 0;
                foreach (var (plan, r) in farSeen)
                {
                    if (r.LiveScan == FarFolk.Scan)
                        continue;
                    if (!noted)
                    {
                        MemberClock(out day, out hour, out seconds);
                        noted = true;
                    }
                    if (!Whereabouts.Built(plan, r.Index, day, hour))
                        continue;
                    var w = Whereabouts.Of(plan, r.Index, day, hour, seconds);
                    if (r.LiveIndoors)
                    {
                        // (The host noted it already as it let it go; noting it again here gives the same place and leg.)
                        r.GoneInTo = Whereabouts.IndoorEnd(plan, w);
                        r.GoneInLeg = w.Leg;
                        continue;
                    }
                    // Row 4, leaving range: gone from here out of doors. Its far figure takes over where the creature
                    // last stood (and where it was heading, for the moments since) and walks back onto its day at its
                    // own pace, so there is no jump.
                    if (!w.Outdoors)
                        continue;
                    var last = r.LivePosition + r.LiveVelocity * math.clamp(now - r.LiveTime, 0f, 0.4f);
                    r.LeftOffset = last - w.Position;
                    r.LeftTime = now;
                    r.LeftEase = math.clamp(math.length(r.LeftOffset.xz) / math.max(r.Speed, 0.5f), 1f, 30f);
                    if (FarFolk.Log)
                        Debug.Log($"[members] left {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark} at ({last.x:F1}, {last.y:F1}, {last.z:F1}), " +
                                  $"{math.length(r.LeftOffset.xz):F2} m off its day, back on it in {r.LeftEase:F1} s, hour {hour:F2}");
                }
            }
            (farSeen, farSeenNow) = (farSeenNow, farSeen);
            farSeenNow.Clear();
            foreach (var town in FarFolk.Towns)
                town.Real = farReal.Contains(town.Landmark);
        }

        /// <summary>The far figures' part of Teardown.</summary>
        void TeardownFarFolk()
        {
            FarFolk.Clear();
            Whereabouts.Clear();
            planPending = null;
            planNear = false;
            farTried.Clear();
            farKey = 0;
            farNext = 0;
            farQueryWorld = null;
            farQuery = default;
            farSeen.Clear();
            farSeenNow.Clear();
            folkJobsLogged.Clear();
        }

        /// <summary>The ground a far figure stands on: the terrain, or wading depth where that is under water.</summary>
        static float FarGround(in TerrainSource source, float2 at)
        {
            var g = source.SampleGround(at);
            return g.HasWater && g.WaterSurface > g.Height ? math.max(g.Height, g.WaterSurface - 0.4f) : g.Height;
        }

        /// <summary>A figure's body and limb colours: its species' (one of its coats, picked by its seed with the
        /// coats' weights, as CreatureBlob.PickCoat does). Returns the coat, which its creature wears too (row 3).</summary>
        static int FarColours(CreatureSpecies species, uint seed, out float4 body, out float4 limbs)
        {
            var b = species.Base;
            var a = species.Accent;
            var picked = 0;
            if (species.Coats != null && species.Coats.Count > 0)
            {
                var total = 0f;
                foreach (var c in species.Coats)
                    total += math.max(c.Weight, 0f);
                var pick = FarFolk.Hash01(seed, 41) * total;
                picked = species.Coats.Count - 1;
                for (int i = 0; i < species.Coats.Count; i++)
                {
                    pick -= math.max(species.Coats[i].Weight, 0f);
                    if (pick <= 0f)
                    {
                        picked = i;
                        break;
                    }
                }
                b = species.Coats[picked].Base;
                a = species.Coats[picked].Accent;
            }
            body = new float4(b.linear.r, b.linear.g, b.linear.b, 0f);
            limbs = new float4(a.linear.r, a.linear.g, a.linear.b, 0f);
            return picked;
        }
    }
}
