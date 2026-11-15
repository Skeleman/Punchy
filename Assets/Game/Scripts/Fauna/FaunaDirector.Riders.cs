using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-117 row 2: travellers on stable horses. Some folk leaving a town with a stable take one of its horses: the rider
    /// walks from a house door through the streets to the paddock gate, in to the horse, leads it out through the gate,
    /// gets up beside it, and rides through the streets to the road nearest the gate and on along it (the brain's
    /// Travelling, the horse leading and the rider seated by RideSystem as kind Travel). Some of the travellers met on
    /// the road ride a horse lent by the nearest stable. Where the horse's road ends the rider gets down and is a visitor
    /// in that town, and the horse is led in through that stable's gate to its paddock (its home there), else tied at a
    /// hitching rail, else tethered where the road ends.
    /// Every step in a town is a waypoint path round walls (FaunaDirector.RiderPaths: the town's streets, then straight
    /// where nothing is in the way), walked by the brain's LeadOn, which keeps a horse's middle and hindquarters a body's
    /// width off every wall, stall and fence; RiderPaths' clearance check counts any overlap.
    /// A small ledger keeps each stable's count honest: a horse away is one fewer in its own paddock and one more as a
    /// guest in the paddock it reached, and guests are taken home a few hours later, so over a day every stable comes
    /// back to its own horses. A town being peopled puts out what its paddock holds now (SpawnStableHorses).
    /// All host side: the ride itself goes to clients as the rider's Holder (minus the horse's ghost id), so no new
    /// ghost field.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>A stable and its stablekeeper household (whose ledger it is).</summary>
        sealed class StableSpot
        {
            public int Landmark;
            public Household Keeper;
            public float4 Gate;
            public float2 Paddock;
            public float Room;
        }

        /// <summary>How long a horse ridden through the streets may make no headway before its way is found again (s).</summary>
        const float RideReplan = 4f;

        /// <summary>A horse under a traveller (or about to be): whose horse it is (Owner) and where it set out from.</summary>
        sealed class HorseRide
        {
            public Entity Horse, Rider;
            public StableSpot From, Owner;
            public bool Seated;
            public float3 LastAt;
            public float Began;
            /// <summary>Where its road begins, for a ride out of a stable (ridden to from the gate); NaN for none.</summary>
            public float2 RoadStart = float.NaN;
            /// <summary>What the two are doing (RidePhase), since when (Time.time), and the waypoints each walks
            /// (FaunaDirector.RiderPaths): the horse's ridden way while Riding.</summary>
            public RidePhase Phase = RidePhase.Riding;
            public float PhaseBegan;
            public List<float2> HorsePath = new List<float2>(), RiderPath = new List<float2>();
            public int HorseLeg, RiderLeg;
            /// <summary>The horse is held where it stands, letting its rider go ahead through a gate.</summary>
            public bool HorseHeld;
            /// <summary>Led home: the stable it's going into.</summary>
            public StableSpot HomeTo;
            /// <summary>For the clearance check: whether each was inside a wall last frame.</summary>
            public bool HorseInside, RiderInside;
            /// <summary>Ridden through the streets: where the horse last made headway, and when (Time.time).</summary>
            public float2 HeadwayAt;
            public float HeadwaySince;
        }

        /// <summary>A stable ride's steps: the rider walks from home to the horse in its paddock (Fetch), leads it out of
        /// the gate (LeadOut), rides it through the streets to the road and on (Riding); at a stable at the road's end
        /// it's led in through that stable's gate to the paddock (LeadHome).</summary>
        enum RidePhase : byte { Fetch, LeadOut, Riding, LeadHome }

        /// <summary>Seconds a fetch, and a lead home, may take before it's called off.</summary>
        const float FetchTime = 180f, LeadHomeTime = 240f;

        /// <summary>A horse standing away from home (a guest in another paddock, or tied up) until it's taken back.</summary>
        struct HorseReturn
        {
            public StableSpot At, Home;
            public float Due;
        }

        /// <summary>Game hours a horse stays away from home before it's taken back (off-screen).</summary>
        const float HorseAwayHours = 5f;
        /// <summary>How near a stable a horse's road must end for the horse to be handed to it (m).</summary>
        const float StableReach = 260f;

        List<StableSpot> stableSpots;
        readonly List<HorseRide> horseRides = new List<HorseRide>();
        readonly List<HorseReturn> horseReturns = new List<HorseReturn>();
        float rideHours, rideLastHour = -1f;
        int ridesBegun, ridesArrived, ridesAfoot, ridesOffscreen, ridesCancelled, horsesHomed;

        /// <summary>Every town stable, once the households are known (null until then).</summary>
        List<StableSpot> StableSpots(in TerrainSource source)
        {
            if (stableSpots != null || !Households.Ready || !source.Settlements.IsCreated)
                return stableSpots;
            stableSpots = new List<StableSpot>();
            ref var set = ref source.Settlements.Value;
            var buildings = SettlementBaking.Buildings(landmarks);
            for (int t = 0; t < set.Settlements.Length; t++)
            {
                var town = set.Settlements[t];
                var scale = set.Layouts[town.Layout].BuildingScale;
                if (!Households.ByLandmark.TryGetValue(town.Landmark, out var houses))
                    continue;
                for (int p = town.FirstPlot; p < town.FirstPlot + town.PlotCount; p++)
                {
                    if (!StableAt(buildings, set.Plots[p], scale, out var gate, out var paddock, out var room))
                        continue;
                    foreach (var h in houses)
                        if (Households.All[h].Kind == "stable" && math.all(Households.All[h].Door.xz == gate.xz))
                            stableSpots.Add(new StableSpot
                            {
                                Landmark = town.Landmark, Keeper = Households.All[h], Gate = gate, Paddock = paddock, Room = math.max(1f, room)
                            });
                }
            }
            return stableSpots;
        }

        /// <summary>Just outside a stable's gate, where a rider gets up (m out along the gate's facing).</summary>
        static float2 OutsideGate(StableSpot spot, float out_) =>
            spot.Gate.xz + new float2(math.sin(spot.Gate.w), math.cos(spot.Gate.w)) * out_;

        /// <summary>
        /// A departure, if a stable near this player has a horse to spare (two or more in its paddock) and a road starts
        /// near its gate: half the time when there is one (forced: always). True if one set out.
        /// </summary>
        bool SpawnDeparture(in TerrainSource source, float3 player, bool forced = false)
        {
            var spots = StableSpots(source);
            if (spots == null || spots.Count == 0)
                return false;
            var candidates = new List<StableSpot>();
            foreach (var spot in spots)
            {
                var d = math.distance(spot.Gate.xz, player.xz);
                if (spot.Keeper.Present >= 2 && d > (forced ? 0f : 30f) && d < 220f && !Leaving(spot))
                    candidates.Add(spot);
            }
            if (candidates.Count == 0 || !forced && rng.NextFloat() >= 0.5f)
                return false;
            var from = candidates[rng.NextInt(candidates.Count)];
            ref var set = ref source.Settlements.Value;
            // (The road whose end is nearest the gate, heading away from that end.)
            var stretch = -1;
            var onward = true;
            var best = 220f * 220f;
            for (int k = 0; k < set.Streets.Length; k++)
            {
                ref var st = ref set.Streets[k];
                if (st.Road == 0)
                    continue;
                var da = math.distancesq(st.A.xz, from.Gate.xz);
                var db = math.distancesq(st.B.xz, from.Gate.xz);
                if (da < best)
                {
                    best = da;
                    stretch = k;
                    onward = true;
                }
                if (db < best)
                {
                    best = db;
                    stretch = k;
                    onward = false;
                }
            }
            var human = TableSpecies(OverworldSlot.Traveller, 0);
            var horseSpecies = SpeciesIndex(HorseName);
            if (stretch < 0 || human < 0 || horseSpecies < 0)
                return false;
            var em = simWorld.EntityManager;
            var horse = PaddockHorse(from, horseSpecies);
            if (horse == Entity.Null)
                horse = Spawn(horseSpecies, new float3(from.Paddock.x, source.SampleHeight(from.Paddock), from.Paddock.y));
            // (The horse waits in its paddock, on the road's stretch for later.)
            var h = em.GetComponentData<CreatureState>(horse);
            h.Task = CreatureTask.Travel;
            h.Mode = CreatureMode.Work;
            h.Stage = 2;
            h.ModeTime = h.StageTime = 0f;
            h.WorkSpot = h.Position.xz;
            h.TargetHeight = stretch;
            h.AnchorSide = (byte)(onward ? 1 : 0);
            h.Home = h.Position.xz;
            h.Leash = 0f;
            h.Timer = 900f;
            em.SetComponentData(horse, h);
            // (Its rider comes out of a house in the town, else from beside the gate, and walks the streets to it.)
            var radius = LeadRadiusOf(horse) + 0.3f;
            var start = RiderHome(from, radius, out var startYaw);
            var rider = Spawn(human, new float3(start.x, source.SampleHeight(start), start.y), startYaw);
            var r = em.GetComponentData<CreatureState>(rider);
            r.Task = CreatureTask.Travel;
            r.Mode = CreatureMode.Work;
            r.Stage = 1;
            r.WorkSpot = start;
            r.TargetHeight = -1f;
            r.Home = start;
            r.Leash = 2f;
            r.Timer = 900f;
            em.SetComponentData(rider, r);
            var ride = new HorseRide
            {
                Horse = horse, Rider = rider, From = from, Began = Time.time, PhaseBegan = Time.time, LastAt = h.Position,
                RoadStart = (onward ? set.Streets[stretch].A : set.Streets[stretch].B).xz, Phase = RidePhase.Fetch
            };
            // (In through the gate, then up beside the horse on the gate's side.)
            var inside = InsideGate(from);
            ride.RiderPath = StreetRoute(start, OutsideGate(from, 1.6f), from.Gate.xz, radius);
            ride.RiderPath.Add(inside);
            var toGate = math.normalizesafe(inside - h.Position.xz, new float2(0f, 1f));
            ride.RiderPath.Add(h.Position.xz + toGate * 1.3f);
            ride.Owner = Lend(from);
            horseRides.Add(ride);
            Debug.Log($"[rider] departure from landmark {from.Landmark} gate ({from.Gate.x:F0}, {from.Gate.z:F0}) onto road {stretch} " +
                      $"(end {math.sqrt(best):F0} m from the gate); rider fetching from ({start.x:F0}, {start.y:F0}) by {ride.RiderPath.Count} waypoints; " +
                      $"stable now {from.Keeper.Present}/{from.Keeper.Horses}");
            return true;
        }

        /// <summary>Just inside a stable's paddock gate.</summary>
        static float2 InsideGate(StableSpot spot) =>
            spot.Gate.xz - new float2(math.sin(spot.Gate.w), math.cos(spot.Gate.w)) * 1.8f;

        /// <summary>Beside a stable's gate, outside, a step to the side of where the horse comes out.</summary>
        static float2 BesideGate(StableSpot spot) =>
            OutsideGate(spot, 1.6f) + new float2(math.cos(spot.Gate.w), -math.sin(spot.Gate.w)) * 1.4f;

        /// <summary>Where a rider sets out from to fetch a horse: just outside the door of a house of the town 15-80 m
        /// from the stable (not the stablekeeper's), facing out; else beside the gate.</summary>
        float2 RiderHome(StableSpot from, float radius, out float yaw)
        {
            yaw = from.Gate.w;
            var doors = new List<float4>();
            if (Households.ByLandmark.TryGetValue(from.Landmark, out var houses))
                foreach (var index in houses)
                {
                    var house = Households.All[index];
                    var d = math.distance(house.Door.xz, from.Gate.xz);
                    if (house != from.Keeper && house.Kind != "stable" && d > 15f && d < 80f)
                        doors.Add(house.Door);
                }
            while (doors.Count > 0)
            {
                var k = rng.NextInt(doors.Count);
                var door = doors[k];
                doors.RemoveAt(k);
                var facing = new float2(math.sin(door.w), math.cos(door.w));
                for (var out_ = 1f; out_ <= 2.6f; out_ += 0.8f)
                {
                    var at = door.xz + facing * out_;
                    if (!WallAt(at, 0.35f))
                    {
                        yaw = door.w;
                        return at;
                    }
                }
            }
            return BesideGate(from);
        }

        /// <summary>Whether a horse is on its way out of this stable now (one at a time).</summary>
        bool Leaving(StableSpot spot)
        {
            foreach (var ride in horseRides)
                if (ride.From == spot && !ride.Seated)
                    return true;
            return false;
        }

        /// <summary>A horse of this stable standing in its paddock now (the town is peopled), or Entity.Null.</summary>
        Entity PaddockHorse(StableSpot spot, int horseSpecies)
        {
            if (!occupied.Contains(spot.Landmark))
                return Entity.Null;
            var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = wildQuery.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = wildQuery.ToEntityArray(Allocator.Temp);
            var found = Entity.Null;
            for (int i = 0; i < entities.Length && found == Entity.Null; i++)
                if (bodies[i].Species == horseSpecies && states[i].Task == CreatureTask.None && !states[i].Gone &&
                    states[i].Mode != CreatureMode.Held && states[i].Mode != CreatureMode.Ride &&
                    math.distancesq(states[i].Home, spot.Paddock) < 0.25f)
                    found = entities[i];
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
            return found;
        }

        /// <summary>A stable lends a horse: a guest standing there first (it travels on, still its own stable's), else
        /// one of its own. Returns whose horse it is.</summary>
        StableSpot Lend(StableSpot from)
        {
            for (int i = 0; i < horseReturns.Count; i++)
                if (horseReturns[i].At == from)
                {
                    var owner = horseReturns[i].Home;
                    horseReturns.RemoveAt(i);
                    from.Keeper.Guests--;
                    return owner;
                }
            from.Keeper.Away++;
            return from;
        }

        /// <summary>
        /// A traveller just met on the road (SpawnTraveller): a third of them ride, on a horse lent by the nearest stable
        /// with one to spare. The horse leads along the same stretch, the traveller seated on it.
        /// </summary>
        void MaybeMount(in TerrainSource source, Entity traveller)
        {
            var spots = StableSpots(source);
            var horseSpecies = SpeciesIndex(HorseName);
            if (spots == null || spots.Count == 0 || horseSpecies < 0 || rng.NextFloat() >= 0.35f)
                return;
            var em = simWorld.EntityManager;
            var t = em.GetComponentData<CreatureState>(traveller);
            StableSpot lender = null;
            var best = float.MaxValue;
            foreach (var spot in spots)
            {
                var d = math.distancesq(spot.Gate.xz, t.Position.xz);
                if (spot.Keeper.Present >= 2 && d < best)
                {
                    best = d;
                    lender = spot;
                }
            }
            if (lender == null)
                return;
            var horse = Spawn(horseSpecies, t.Position, t.Yaw);
            var h = em.GetComponentData<CreatureState>(horse);
            h.Task = CreatureTask.Travel;
            h.Mode = CreatureMode.Work;
            h.Stage = 0;
            h.TargetHeight = t.TargetHeight;
            h.AnchorSide = t.AnchorSide;
            h.Home = t.Home;
            h.Leash = 0f;
            h.Timer = 900f;
            em.SetComponentData(horse, h);
            var ride = new HorseRide { Horse = horse, Rider = traveller, From = lender, Began = Time.time, LastAt = h.Position };
            if (!Seat(ride))
            {
                em.DestroyEntity(horse);
                return;
            }
            ride.Owner = Lend(lender);
            horseRides.Add(ride);
            Debug.Log($"[rider] traveller on the road at ({t.Position.x:F0}, {t.Position.z:F0}) rides a horse lent by landmark {lender.Landmark}; " +
                      $"stable now {lender.Keeper.Present}/{lender.Keeper.Horses}");
        }

        /// <summary>Gets a ride's rider up on its horse, as kind Travel, and sends the horse on along its road.</summary>
        bool Seat(HorseRide ride)
        {
            var em = simWorld.EntityManager;
            var r = em.GetComponentData<CreatureState>(ride.Rider);
            r.Task = CreatureTask.Travel;
            em.SetComponentData(ride.Rider, r);
            var refusal = Riding.Mount(em, ride.Rider, ride.Horse, populationSource);
            if (refusal != RideRefusal.None)
            {
                Debug.Log($"[rider] could not get up: {refusal}");
                return false;
            }
            var rider = em.GetComponentData<Rider>(ride.Rider);
            rider.Kind = RideKind.Travel;
            em.SetComponentData(ride.Rider, rider);
            var h = em.GetComponentData<CreatureState>(ride.Horse);
            // (Out of a stable: ridden first through the streets to where the road begins, unless that's right here.)
            var toRoad = !math.any(math.isnan(ride.RoadStart)) && math.distance(ride.RoadStart, h.Position.xz) > 3f;
            ride.HorsePath = toRoad ? StreetRoute(h.Position.xz, ride.RoadStart, ride.From.Gate.xz, LeadRadiusOf(ride.Horse) + 0.3f) : new List<float2>();
            ride.HorseLeg = 0;
            h.Stage = (byte)(toRoad ? 3 : 0);
            if (toRoad)
                h.WorkSpot = ride.HorsePath[0];
            h.StageTime = 0f;
            em.SetComponentData(ride.Horse, h);
            ride.Seated = true;
            ride.Phase = RidePhase.Riding;
            ride.PhaseBegan = Time.time;
            ridesBegun++;
            return true;
        }

        /// <summary>
        /// Each population step: riders getting up at the gate, rides that have ended (the road's end, the rider down
        /// sooner, or both gone from the world), and horses taken home once their time away is up.
        /// </summary>
        void UpdateRiders(in TerrainSource source)
        {
            var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
            if (rideLastHour >= 0f)
                rideHours += (hour - rideLastHour + 24f) % 24f;
            rideLastHour = hour;
            for (int i = horseReturns.Count - 1; i >= 0; i--)
                if (rideHours >= horseReturns[i].Due)
                {
                    var back = horseReturns[i];
                    horseReturns.RemoveAt(i);
                    if (back.At != null)
                        back.At.Keeper.Guests--;
                    back.Home.Keeper.Away--;
                    horsesHomed++;
                }
            if (horseRides.Count == 0)
                return;
            var em = simWorld.EntityManager;
            for (int i = horseRides.Count - 1; i >= 0; i--)
            {
                var ride = horseRides[i];
                var horseHere = em.Exists(ride.Horse) && em.HasComponent<CreatureState>(ride.Horse);
                var riderHere = em.Exists(ride.Rider) && em.HasComponent<CreatureState>(ride.Rider);
                if (horseHere)
                    ride.LastAt = em.GetComponentData<CreatureState>(ride.Horse).Position;
                CheckClearance(ride, horseHere, riderHere);
                if (ride.Phase == RidePhase.LeadHome)
                {
                    if (!LeadHome(ride, horseHere, riderHere))
                        horseRides.RemoveAt(i);
                    continue;
                }
                if (!ride.Seated)
                {
                    if (horseHere && riderHere && Time.time - ride.Began < FetchTime && StepOut(ride))
                        continue;
                    // (Called off: the horse goes back in its paddock, the would-be rider stays a while.)
                    if (horseHere)
                        Stable(ride.Horse, ride.From);
                    if (riderHere)
                        Visitor(ride.Rider);
                    Settle(ride, ride.From);
                    ridesCancelled++;
                    horseRides.RemoveAt(i);
                    Debug.Log($"[rider] departure from landmark {ride.From.Landmark} called off");
                    continue;
                }
                if (riderHere && em.HasComponent<Rider>(ride.Rider) && horseHere)
                {
                    StepRide(ride);
                    continue;
                }
                horseRides.RemoveAt(i);
                if (!horseHere)
                {
                    // (Both out of everyone's sight: the horse rides on to the next stable, wherever it was going.)
                    Settle(ride, Nearest(ride.LastAt.xz, float.MaxValue, ride.From) ?? ride.From);
                    ridesOffscreen++;
                    Debug.Log($"[rider] ride from landmark {ride.From.Landmark} went out of sight near ({ride.LastAt.x:F0}, {ride.LastAt.z:F0})");
                    continue;
                }
                var hs = em.GetComponentData<CreatureState>(ride.Horse);
                var arrived = hs.Task != CreatureTask.Travel;
                if (arrived)
                    ridesArrived++;
                else
                    ridesAfoot++;
                // (Led on into a stable there: still a ride until the horse is in its paddock.)
                if (Hand(ride, hs.Position, arrived, riderHere))
                    horseRides.Add(ride);
            }
        }

        /// <summary>
        /// A ride out of a stable before the rider is up: the rider walks the streets from home, in through the gate and
        /// up beside the horse (Fetch); then leads it out, going first through the gate while the horse waits a couple
        /// of metres behind, the horse coming on to just outside it (LeadOut), where the rider gets up. False once it
        /// can't go on (either turned aside), to be called off.
        /// </summary>
        bool StepOut(HorseRide ride)
        {
            var em = simWorld.EntityManager;
            var h = em.GetComponentData<CreatureState>(ride.Horse);
            var r = em.GetComponentData<CreatureState>(ride.Rider);
            if (h.Task != CreatureTask.Travel || r.Task != CreatureTask.Travel)
                return false;
            if (ride.Phase == RidePhase.Fetch)
            {
                if (!FollowPath(ride.Rider, ride.RiderPath, ref ride.RiderLeg, 0.8f))
                    return true;
                ride.Phase = RidePhase.LeadOut;
                ride.PhaseBegan = Time.time;
                var inside = InsideGate(ride.From);
                ride.RiderPath = new List<float2> { inside, BesideGate(ride.From) };
                ride.HorsePath = new List<float2> { inside, OutsideGate(ride.From, 1.6f) };
                ride.RiderLeg = ride.HorseLeg = 0;
                Debug.Log($"[rider] fetched the horse at landmark {ride.From.Landmark} after {Time.time - ride.Began:F1} s; leading it out");
            }
            var riderDone = FollowPath(ride.Rider, ride.RiderPath, ref ride.RiderLeg, 0.6f);
            LeadBehind(ride, riderDone);
            var horseDone = ride.HorseLeg >= ride.HorsePath.Count;
            h = em.GetComponentData<CreatureState>(ride.Horse);
            r = em.GetComponentData<CreatureState>(ride.Rider);
            if (horseDone && riderDone && math.distance(h.Position.xz, r.Position.xz) < 3.5f && Seat(ride))
                Debug.Log($"[rider] up at the gate of landmark {ride.From.Landmark} after {Time.time - ride.Began:F1} s");
            return true;
        }

        /// <summary>The horse comes on along its way behind the one leading it: held while it's within a couple of
        /// metres of them and they're still going (a gate is one wide), then on to the same waypoint.</summary>
        void LeadBehind(HorseRide ride, bool riderDone)
        {
            var em = simWorld.EntityManager;
            var h = em.GetComponentData<CreatureState>(ride.Horse);
            var r = em.GetComponentData<CreatureState>(ride.Rider);
            if (!riderDone && math.distance(h.Position.xz, r.Position.xz) < 2.4f && ride.HorseLeg < ride.HorsePath.Count)
            {
                HoldStill(ride.Horse);
                ride.HorseHeld = true;
                return;
            }
            FollowPath(ride.Horse, ride.HorsePath, ref ride.HorseLeg, 0.8f, ride.HorseHeld);
            ride.HorseHeld = false;
        }

        /// <summary>A ridden horse on its way through the streets to its road: the next waypoint each time it gets near
        /// one (Travelling's Stage 3), and on along the road (Stage 0) from the last.</summary>
        void StepRide(HorseRide ride)
        {
            if (ride.Phase != RidePhase.Riding)
                return;
            var em = simWorld.EntityManager;
            var h = em.GetComponentData<CreatureState>(ride.Horse);
            if (h.Task != CreatureTask.Travel || h.Stage != 3 && h.Stage != 2)
                return;
            var radius = LeadRadiusOf(ride.Horse) + 0.3f;
            // (Caught on a corner the waypoints didn't foresee, it hasn't got 0.3 m on in RideReplan s: the way again
            // from where it stands to the same end.)
            if (math.distance(h.Position.xz, ride.HeadwayAt) > 0.3f || h.Stage != 3)
            {
                ride.HeadwayAt = h.Position.xz;
                ride.HeadwaySince = Time.time;
            }
            else if (Time.time - ride.HeadwaySince > RideReplan && ride.HorsePath.Count > 0)
            {
                ride.HorsePath = StreetRoute(h.Position.xz, ride.HorsePath[ride.HorsePath.Count - 1], ride.From.Gate.xz, radius);
                ride.HorseLeg = 0;
                ride.HeadwaySince = Time.time;
                h.WorkSpot = ride.HorsePath[0];
                em.SetComponentData(ride.Horse, h);
                return;
            }
            var last = ride.HorseLeg >= ride.HorsePath.Count - 1;
            // (On to the next waypoint a little before this one, so it doesn't stop at every corner, but only when the
            // line on from where it is now is clear too; at the last it waits there and takes the road.)
            if (h.Stage == 3 && (last || math.distance(h.Position.xz, ride.HorsePath[ride.HorseLeg]) > 1.5f ||
                                 !Clear(h.Position.xz, ride.HorsePath[ride.HorseLeg + 1], radius)))
                return;
            if (last)
                h.Stage = 0;
            else
            {
                ride.HorseLeg++;
                h.Stage = 3;
                h.WorkSpot = ride.HorsePath[ride.HorseLeg];
            }
            em.SetComponentData(ride.Horse, h);
        }

        /// <summary>
        /// A horse being led home into a stable at its road's end: its rider (down now) walks the streets to the gate
        /// and stands aside, the horse coming on behind and in through the gate to the paddock, where it's stabled and
        /// the rider is a visitor. False once it's over.
        /// </summary>
        bool LeadHome(HorseRide ride, bool horseHere, bool riderHere)
        {
            if (!horseHere)
                return false;
            var spot = ride.HomeTo;
            if (!riderHere || Time.time - ride.PhaseBegan > LeadHomeTime)
            {
                Stable(ride.Horse, spot);
                if (riderHere)
                    Visitor(ride.Rider);
                Debug.Log($"[rider] led home at landmark {spot.Landmark} cut short ({(riderHere ? "took too long" : "the rider went")})");
                return false;
            }
            var riderDone = FollowPath(ride.Rider, ride.RiderPath, ref ride.RiderLeg, 0.6f);
            LeadBehind(ride, riderDone);
            if (ride.HorseLeg < ride.HorsePath.Count)
                return true;
            Stable(ride.Horse, spot);
            Visitor(ride.Rider);
            Debug.Log($"[rider] horse led into the stable of landmark {spot.Landmark} after {Time.time - ride.PhaseBegan:F0} s");
            return false;
        }

        /// <summary>A horse whose ride is over, where it stands: to the stable near there (led in through its gate by its
        /// rider, LeadHome: true, still a ride), else a hitching rail near there, else tethered on the spot.</summary>
        bool Hand(HorseRide ride, float3 at, bool arrived, bool riderHere)
        {
            var em = simWorld.EntityManager;
            var spot = Nearest(at.xz, StableReach, null);
            string where;
            var leading = false;
            if (spot != null)
            {
                Settle(ride, spot);
                where = $"stable of landmark {spot.Landmark} ({math.distance(spot.Gate.xz, at.xz):F0} m off), now {spot.Keeper.Present}/{spot.Keeper.Horses}";
                if (riderHere)
                {
                    // (Led in: the rider walks the streets to the gate and stands aside; the horse follows and goes in.)
                    var radius = LeadRadiusOf(ride.Horse) + 0.3f;
                    var r = em.GetComponentData<CreatureState>(ride.Rider);
                    ride.Phase = RidePhase.LeadHome;
                    ride.PhaseBegan = Time.time;
                    ride.HomeTo = spot;
                    ride.RiderPath = StreetRoute(r.Position.xz, BesideGate(spot), spot.Gate.xz, radius);
                    ride.HorsePath = StreetRoute(at.xz, OutsideGate(spot, 1.6f), spot.Gate.xz, radius);
                    ride.HorsePath.Add(InsideGate(spot));
                    ride.HorsePath.Add(spot.Paddock);
                    ride.RiderLeg = ride.HorseLeg = 0;
                    ride.HorseHeld = false;
                    r.Timer = math.max(r.Timer, 600f);
                    em.SetComponentData(ride.Rider, r);
                    leading = true;
                    where += $", led in by {ride.RiderPath.Count} waypoints";
                }
                else
                    Stable(ride.Horse, spot);
            }
            else
            {
                var h = em.GetComponentData<CreatureState>(ride.Horse);
                var tie = HitchRailNear(populationSource, at.xz, 60f, out var rail) ? rail : at.xz;
                h.Task = CreatureTask.None;
                h.Home = tie;
                h.Leash = 1.5f;
                em.SetComponentData(ride.Horse, h);
                // (Still away from its own stable: taken back once its time is up.)
                horseReturns.Add(new HorseReturn { At = null, Home = ride.Owner, Due = rideHours + HorseAwayHours });
                where = math.any(tie != at.xz) ? $"hitching rail at ({tie.x:F0}, {tie.y:F0})" : "tethered where it stopped";
            }
            Debug.Log($"[rider] {(arrived ? "arrived" : "got down early")} at ({at.x:F0}, {at.z:F0}) after {Time.time - ride.Began:F0} s; horse to {where}");
            return leading;
        }

        /// <summary>A horse put in a stable's paddock: leashed to its middle, nothing else to do.</summary>
        void Stable(Entity horse, StableSpot spot)
        {
            var em = simWorld.EntityManager;
            var h = em.GetComponentData<CreatureState>(horse);
            h.Task = CreatureTask.None;
            h.Home = spot.Paddock;
            h.Leash = spot.Room;
            if (h.Mode == CreatureMode.Work)
                h.Mode = CreatureMode.Idle;
            em.SetComponentData(horse, h);
        }

        /// <summary>A rider-to-be with nowhere to go now: a visitor about the spot for a while.</summary>
        void Visitor(Entity rider)
        {
            var em = simWorld.EntityManager;
            var r = em.GetComponentData<CreatureState>(rider);
            r.Task = CreatureTask.None;
            r.Home = r.Position.xz;
            r.Leash = 30f;
            em.SetComponentData(rider, r);
        }

        /// <summary>The ledger once a ride's horse stands in a paddock: home if it's its own stable, else a guest there
        /// until it's taken back.</summary>
        void Settle(HorseRide ride, StableSpot at)
        {
            if (at == ride.Owner)
            {
                at.Keeper.Away--;
                return;
            }
            at.Keeper.Guests++;
            horseReturns.Add(new HorseReturn { At = at, Home = ride.Owner, Due = rideHours + HorseAwayHours });
        }

        /// <summary>The stable nearest a spot within reach (m), not counting one; null for none.</summary>
        StableSpot Nearest(float2 at, float reach, StableSpot not)
        {
            StableSpot best = null;
            var bestD = reach * reach;
            if (stableSpots == null)
                return null;
            foreach (var spot in stableSpots)
            {
                var d = math.distancesq(spot.Gate.xz, at);
                if (spot != not && d < bestD)
                {
                    bestD = d;
                    best = spot;
                }
            }
            return best;
        }

        /// <summary>A town's hitching rail (a stable plot that grew a rail, not a stable) within reach of a spot.</summary>
        bool HitchRailNear(in TerrainSource source, float2 at, float reach, out float2 rail)
        {
            rail = default;
            if (!source.Settlements.IsCreated)
                return false;
            ref var set = ref source.Settlements.Value;
            var buildings = SettlementBaking.Buildings(landmarks);
            var bestD = reach * reach;
            var found = false;
            for (int p = 0; p < set.Plots.Length; p++)
            {
                var plot = set.Plots[p];
                if (plot.Role != PlotRole.Stable || plot.Building < 0 || plot.Building >= buildings.Count ||
                    buildings[plot.Building].Species == null || buildings[plot.Building].Species.Form != Punchy.Flora.FloraForm.HitchRail)
                    continue;
                var spot = plot.Centre + plot.Facing * 1.2f;
                var d = math.distancesq(spot, at);
                if (d < bestD)
                {
                    bestD = d;
                    rail = spot;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>For the tests (TestHooks.Riders): the rides so far and each stable's paddock count.</summary>
        public string RiderReport()
        {
            var s = $"rides begun {ridesBegun}, arrived {ridesArrived}, down early {ridesAfoot}, out of sight {ridesOffscreen}, " +
                    $"called off {ridesCancelled}, under way {horseRides.Count}; horses taken home {horsesHomed}, still away {horseReturns.Count}; " +
                    $"hours {rideHours:F1}";
            if (stableSpots != null)
                foreach (var spot in stableSpots)
                    s += $"\n  landmark {spot.Landmark}: {spot.Keeper.Present}/{spot.Keeper.Horses} (away {spot.Keeper.Away}, guests {spot.Keeper.Guests})";
            return s + "\n" + ClearanceReport();
        }

        /// <summary>For the tests: a departure from the stable nearest the first player, now (false if none could set out).</summary>
        public bool ForceDeparture() =>
            players.Count > 0 && SpawnDeparture(populationSource, players[0], true);
    }
}
