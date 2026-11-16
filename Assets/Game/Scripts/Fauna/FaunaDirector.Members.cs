using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-110 row 3: the same people up close. A town's housed folk are its household members: near a player, the host
    // spawns each member who is outdoors by Whereabouts as itself (its own Individual, house, species, coat and
    // size, all from its member seed) at its Whereabouts spot, so the far figure and the creature are one person at
    // one place. The town's resident rows no longer draw housed folk. Nothing new is sent or stored: a joined client
    // knows a member's creature by the Individual its ghost already carries.
    public partial class FaunaDirector
    {
        /// <summary>The most household members out in the world at once, a count of their own beside MaxSettled
        /// (which stays for domestics and visitors). Over it, the nearest to any player are the ones made.</summary>
        public const int MaxHoused = 80;

        /// <summary>How long a member asked for is left alone before it may be asked for again (s): its creature is
        /// made within a few frames and is in Despawn's tally by the next look.</summary>
        const float MemberAskAgain = 5f;

        int housedCount;
        /// <summary>The members whose creatures are in the world (host): Despawn's tally, by Individual.</summary>
        readonly HashSet<uint> memberLive = new HashSet<uint>();

        struct MemberAsk
        {
            public float Distance;
            public TownPlan Plan;
            public int Index;
            public Whereabout At;
        }

        readonly List<MemberAsk> memberAsks = new List<MemberAsk>();
        readonly System.Text.StringBuilder memberLine = new System.Text.StringBuilder();
        float nextMemberLog;
        int memberCapLogged = -1;

        /// <summary>How many members' creatures are in the world or waiting to be made (host).</summary>
        public int HousedCount => housedCount;

        /// <summary>Whether a creature is a household member of a town whose folk are its members (its plan is built).</summary>
        static bool TownMember(uint individual) =>
            individual != 0 && Households.Of(individual, out var house, out _) && Whereabouts.Towns.ContainsKey(house.Landmark);

        /// <summary>
        /// Whether a landmark is a town whose housed folk are its household members (it gets a town plan): houses of
        /// the house kind with members, and a grown housed species among its resident rows. True before its plan is
        /// built too, so its rows never draw housed folk.
        /// </summary>
        bool TownOfMembers(in TerrainSource source, int i)
        {
            ref var set = ref source.Landmarks.Value;
            ref var inst = ref set.Instances[i];
            var type = inst.Type < landmarks.Length ? landmarks[inst.Type] : null;
            if (type == null || type.Residents == null || type.Residents.Count == 0 || !Households.Ready ||
                !Households.ByLandmark.TryGetValue(i, out var houses) || houses.Count == 0)
                return false;
            var kind = Households.All[houses[0]].Kind;
            if (kind != "house" && kind != "hut")
                return false;
            var members = false;
            foreach (var h in houses)
                members |= Households.All[h].Members != null && Households.All[h].Members.Length > 0;
            if (!members)
                return false;
            foreach (var r in type.Residents)
                if (HousedFolk(SpeciesIndex(r.Species), out var child) && !child)
                    return true;
            return false;
        }

        /// <summary>
        /// The moment members are placed by: the one the far figures were last drawn at when that is fresh (so a
        /// creature starts exactly where its far figure stands), else the cycle's own.
        /// </summary>
        void MemberClock(out int day, out float hour, out double seconds)
        {
            if (Time.frameCount - Whereabouts.ClockFrame <= 2)
            {
                day = Whereabouts.ClockDay;
                hour = Whereabouts.ClockHour;
                seconds = Whereabouts.ClockSeconds;
                return;
            }
            day = Cycle != null ? Cycle.Day : 0;
            hour = Cycle != null ? Cycle.TimeOfDay : 12f;
            if (Cycle != null)
                Whereabouts.SecondsPerHour = Cycle.DayLengthMinutes * 2.5f;
            seconds = Cycle == null || Cycle.Paused ? Time.timeAsDouble : Whereabouts.Seconds(day, hour);
        }

        /// <summary>
        /// Host: asks for the creature of every member who is outdoors within Radius of a player and has none, the
        /// nearest first, up to MaxHoused. Draws nothing from the spawn stream.
        /// </summary>
        void SpawnMembers(in TerrainSource source)
        {
            if (Whereabouts.Towns.Count == 0 || !Households.Ready || players.Count == 0)
                return;
            MemberClock(out var day, out var hour, out var seconds);
            memberAsks.Clear();
            memberLine.Clear();
            var now = Time.time;
            var log = FarFolk.Log && now >= nextMemberLog;
            // (A member's day is built the first time it is asked for: a few a look, the rest wait their turn.)
            var budget = 24;
            foreach (var plan in Whereabouts.Towns.Values)
            {
                if (NearestPlayer(plan.Centre) > Radius + plan.Radius + 20f)
                    continue;
                LogFolkJobs(plan);
                // (Logged: outdoors is every member Whereabouts has out within Radius, with or without its creature;
                // liveIn counts creatures whose member is not, which row 4 sends in at a door.)
                int inRange = 0, live = 0, waiting = 0, atDoor = 0, outdoors = 0, liveIn = 0, liveLate = 0, goneIn = 0;
                for (int i = 0; i < plan.Residents.Count; i++)
                {
                    var r = plan.Residents[i];
                    if (r.House >= Households.All.Count)
                        continue;
                    var house = Households.All[r.House];
                    // (Dead this session, or a hermit holds the place.)
                    if (house.Gone(r.Member) || (house.Pinned >> r.Member & 1u) != 0)
                        continue;
                    if (memberLive.Contains(r.Seed))
                    {
                        live++;
                        if (log && Whereabouts.Built(plan, i, day, hour))
                        {
                            var lw = Whereabouts.Of(plan, i, day, hour, seconds);
                            if (lw.Outdoors && Whereabouts.IndoorEnd(plan, lw) < 0 && NearestPlayer(lw.Position.xz) <= Radius)
                                outdoors++;
                            else if (!lw.Outdoors)
                                liveLate++;
                            else if (Whereabouts.IndoorEnd(plan, lw) >= 0)
                                liveIn++;
                        }
                        continue;
                    }
                    if (!Whereabouts.Built(plan, i, day, hour))
                    {
                        if (budget <= 0)
                            continue;
                        budget--;
                    }
                    var w = Whereabouts.Of(plan, i, day, hour, seconds);
                    // (Row 4: its creature went in at a door ahead of its day; it comes out when its day does.)
                    if (Whereabouts.StillIn(r, w))
                    {
                        if (w.Outdoors)
                            goneIn++;
                        continue;
                    }
                    if (!w.Outdoors)
                        continue;
                    var d = NearestPlayer(w.Position.xz);
                    if (d > Radius)
                        continue;
                    inRange++;
                    if (Whereabouts.IndoorEnd(plan, w) < 0)
                        outdoors++;
                    // (Stepping in at a door: its far figure is part shown and goes in; no creature for that. Stepping
                    // out of one, row 4: its creature is made in the doorway and steps out as the far figure would.)
                    if (w.Shown < 1f && !SteppingOut(plan, w))
                    {
                        atDoor++;
                        continue;
                    }
                    if (now - r.Asked < MemberAskAgain)
                    {
                        waiting++;
                        continue;
                    }
                    memberAsks.Add(new MemberAsk { Distance = d, Plan = plan, Index = i, At = w });
                }
                if (log)
                    memberLine.Append($" [{plan.Name} #{plan.Landmark}: outdoors {outdoors}, creatures {live} ({liveIn} walking in, {liveLate} late in), no creature yet {inRange}, " +
                                      $"waiting {waiting}, at a door {atDoor}, in early {goneIn}{MemberTiers(plan.Landmark)}]");
            }
            memberAsks.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                                    : a.Plan.Landmark != b.Plan.Landmark ? a.Plan.Landmark.CompareTo(b.Plan.Landmark) : a.Index.CompareTo(b.Index));
            int asked = 0, capped = 0;
            foreach (var ask in memberAsks)
            {
                if (housedCount >= MaxHoused)
                {
                    capped++;
                    continue;
                }
                var plan = ask.Plan;
                var index = ask.Index;
                var r = plan.Residents[index];
                r.Asked = now;
                asked++;
                Enqueue(r.Species, ask.At.Position + r.LeftNow(now), SpawnTier.Housed, e => SetUpMember(e, plan, index),
                        math.atan2(ask.At.Facing.x, ask.At.Facing.y));
            }
            // (The cap biting is to be reported, not tuned round: said once each time it starts to bite.)
            if (capped > 0 && memberCapLogged != 1)
                Debug.Log($"[members] cap: {capped} outdoor members in range left as far figures, {housedCount} of {MaxHoused} housed creatures");
            memberCapLogged = capped > 0 ? 1 : 0;
            if (log)
            {
                nextMemberLog = now + 5f;
                Debug.Log($"[members] day {day} hour {hour:F2}: housed {housedCount} of {MaxHoused}, settled {settledCount}, asked {asked}, capped {capped}, " +
                          $"went in {membersWentIn} (waited inside at most {inEarlyMost:F1} min, late by at most {inLateMost:F1} min), stuck {membersStuck};{memberLine}");
                membersWentIn = membersStuck = 0;
                inEarlyMost = inLateMost = 0f;
            }
        }

        /// <summary>Row 4: whether a member is just stepping out of an indoor place's door (within the 1.5 m its far
        /// figure fades in over), not stepping in at one.</summary>
        static bool SteppingOut(TownPlan plan, in Whereabout w) =>
            w.Outdoors && w.From >= 0 && w.From < plan.Places.Count && w.From != w.To && plan.Places[w.From].Indoors &&
            math.distance(w.Position.xz, plan.Places[w.From].At) < 1.6f;

        /// <summary>
        /// A member's creature, just made: itself (its Individual, its house's door, its coat and size from its member
        /// seed, not the spawn stream), standing where Whereabouts has it at this moment, doing its job if it is on
        /// its working leg.
        /// </summary>
        void SetUpMember(Entity e, TownPlan plan, int index)
        {
            var em = simWorld.EntityManager;
            var r = plan.Residents[index];
            if (r.House >= Households.All.Count)
                return;
            var house = Households.All[r.House];
            var st = em.GetComponentData<CreatureState>(e);
            var body = em.GetComponentData<CreatureBody>(e);
            ref var b = ref blobs[r.Species].Value;
            st.Individual = r.Seed;
            st.OwnDoor = house.Door;
            house.Taken |= 1u << r.Member;
            st.Seed = BiomeSampling.Scramble(r.Seed ^ 0x5EEDu) | 1u;
            st.Phase = FarFolk.Hash01(r.Seed, 42);
            st.Scale = math.lerp(b.Size.x, math.max(b.Size.x, b.Size.y), FarFolk.Hash01(r.Seed, 40));
            // (A child with no child species of its own in this town is a small grown one.)
            if (r.Child && !b.Child)
                st.Scale *= 0.62f;
            body.Coat = math.clamp(r.Coat, 0, math.max(b.Coats.Length - 1, 0));

            // Where it is now (a few frames after it was asked for), so it starts on its far figure.
            MemberClock(out var day, out var hour, out var seconds);
            var w = Whereabouts.Of(plan, index, day, hour, seconds);
            if (w.Outdoors)
            {
                // (Plus what is left of its leaving offset, row 4 (b), when it is made again before its far figure
                // has eased back onto its day: it starts where that figure is drawn.)
                st.Position = w.Position + r.LeftNow(Time.time);
                st.Yaw = math.atan2(w.Facing.x, w.Facing.y);
            }
            st.Home = plan.Centre;
            st.Leash = plan.Radius * 0.8f;
            var walking = w.Outdoors && (w.Doing == Doing.Walk || w.Doing == Doing.Carry) && w.To >= 0 && w.To < plan.Places.Count;
            var job = r.Job >= 0 && r.Job < plan.Places.Count ? plan.Places[r.Job] : default;
            // (A post with a title but no task yet, a shop or a garden: the creature still holds its job, row 4 (d).)
            if (job.Job != FolkJob.None && w.To == r.Job)
                st.Job = (byte)job.Job;
            if (SteppingOut(plan, w))
            {
                // Coming out at a door (row 4): made in the doorway, it steps out as the far figure would have, the
                // brain's own step-out (GoIndoors' last stage). Its far figure stays hidden while it is in the doorway
                // (LiveIndoors), so there is never a double.
                StepOutOf(ref st, plan.Places[w.From]);
            }
            else if (job.Task != CreatureTask.None && w.To == r.Job)
            {
                // On its working leg: at its post, as a landmark's own worker is.
                st.Task = job.Task;
                st.Job = (byte)job.Job;
                // (Row 5: on the spot its day has it at there, its far figure's, so going on legs leaves it standing.)
                st.WorkSpot = w.Outdoors && w.Doing == Doing.Work ? w.Position.xz : job.At;
                st.WorkFace = job.WorkFace;
                st.Home = job.At;
                st.Leash = job.Task == CreatureTask.Patrol ? plan.Radius * 0.8f : 7f;
                st.Mode = CreatureMode.Work;
                st.Timer = 40f + 80f * FarFolk.Hash01(r.Seed, 43);
            }
            else if (walking && w.From != w.To)
            {
                // On its way somewhere: it keeps walking there.
                st.Mode = CreatureMode.Wander;
                st.Target = plan.Places[w.To].At;
                st.Timer = 6f;
            }
            // (Row 7b: a far figure walks its day's straight lines, through a house's corner now and then; one made
            // there stood in the walls, unable to step any way, till its brain gave its door up an hour and a half
            // later. It starts on the nearest clear ground instead.)
            var moved = 0f;
            if (w.Outdoors && st.Mode != CreatureMode.Home && OutOfWalls(ref st.Position, 0.35f, out moved))
            {
                if (st.Mode == CreatureMode.Work)
                    st.WorkSpot = st.Position.xz;
            }
            em.SetComponentData(e, st);
            em.SetComponentData(e, body);
            if (FarFolk.Log && moved > 0f)
                Debug.Log($"[members] spawn {FolkNames.Of(r.Seed)} id {r.Seed:X8}: far figure's spot was inside walls, made {moved:F1} m off it");
            if (em.HasComponent<LocalTransform>(e))
                em.SetComponentData(e, LocalTransform.FromPosition(st.Position));
            if (FarFolk.Log)
                Debug.Log($"[members] spawn {FolkNames.Of(r.Seed)} id {r.Seed:X8} {house.Members[r.Member].Role} {species[r.Species].name} of {plan.Name} #{plan.Landmark} " +
                          $"at ({st.Position.x:F1}, {st.Position.y:F1}, {st.Position.z:F1}), far figure at ({w.Position.x:F1}, {w.Position.y:F1}, {w.Position.z:F1}), " +
                          $"apart {math.distance(st.Position, w.Position):F2} m, {w.Doing}{(st.Task != CreatureTask.None ? " task " + st.Task : "")}, hour {hour:F2}");
        }

        /// <summary>A creature just made in an indoor place's doorway, stepping out of it (the brain's own step-out,
        /// GoIndoors' last stage).</summary>
        static void StepOutOf(ref CreatureState st, in TownPlace from)
        {
            var outward = math.normalizesafe(from.Face - from.At, new float2(0f, 1f));
            st.Mode = CreatureMode.Home;
            st.ModeTime = 0f;
            st.Stage = 3;
            st.StageTime = 0f;
            st.ActionTime = 1f;
            st.Anchor = new float3(from.At.x, from.Height, from.At.y);
            st.AnchorYaw = math.atan2(outward.x, outward.y);
            st.Position = new float3(from.At.x - outward.x * 0.7f, from.Height, from.At.y - outward.y * 0.7f);
            st.Yaw = st.AnchorYaw;
            st.Timer = 0f;
        }

        /// <summary>Row 7b: if a body of this radius at p is inside a building's or prop's walls, moves p to the nearest
        /// clear spot (rings half a metre apart, out to 8 m) and says how far. False if p was clear or nothing near is. // sim:</summary>
        bool OutOfWalls(ref float3 p, float radius, out float moved)
        {
            moved = 0f;
            if (!floraBlob.IsCreated || !Punchy.Flora.FloraPlacement.Blocked(ref floraBlob.Value, lastSource, p.xz, radius))
                return false;
            for (var ring = 1; ring <= 16; ring++)
            {
                var d = ring * 0.5f;
                for (var k = 0; k < 12; k++)
                {
                    var a = (k + 0.5f * (ring & 1)) * (math.PI * 2f / 12f);
                    var at = p.xz + new float2(math.sin(a), math.cos(a)) * d;
                    if (Punchy.Flora.FloraPlacement.Blocked(ref floraBlob.Value, lastSource, at, radius))
                        continue;
                    moved = d;
                    p = new float3(at.x, lastSource.SampleHeight(at), at.y);
                    return true;
                }
            }
            return false;
        }

        /// <summary>-farfolk: a member's creature first seen on this machine (the host's own, a joined client's ghost),
        /// and how far it is from where Whereabouts has the member.</summary>
        void LogMemberSeen(TownPlan plan, Resident r, in CreatureState s)
        {
            MemberClock(out var day, out var hour, out var seconds);
            if (!Whereabouts.Built(plan, r.Index, day, hour))
            {
                Debug.Log($"[members] seen {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark} at ({s.Position.x:F1}, {s.Position.y:F1}, {s.Position.z:F1}), its day not built yet");
                return;
            }
            var w = Whereabouts.Of(plan, r.Index, day, hour, seconds);
            Debug.Log($"[members] seen {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark} at ({s.Position.x:F1}, {s.Position.y:F1}, {s.Position.z:F1}), " +
                      $"far figure {(w.Outdoors ? $"at ({w.Position.x:F1}, {w.Position.y:F1}, {w.Position.z:F1}), apart {math.distance(s.Position, w.Position):F2} m" : "indoors")}, " +
                      $"clock {s.Clock:F1} s, hour {hour:F2}");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Tests (host): lets go every member creature of the town nearest a spot and forgets its row 4 notes (gone in
        /// at a door, asked for), so SpawnMembers makes them again from where their day has them now. A creature made
        /// before a clock jump keeps the leg it was made on (a member near a player is never put back on its legs),
        /// so a test that sets the hour calls this to see the town as that hour has it. Returns how many it let go.
        /// </summary>
        public int RemakeMembersNear(float3 at)
        {
            if (simWorld == null || !simWorld.IsCreated || Whereabouts.Towns.Count == 0)
                return 0;
            TownPlan plan = null;
            foreach (var p in Whereabouts.Towns.Values)
                if (plan == null || math.distancesq(p.Centre.xz, at.xz) < math.distancesq(plan.Centre.xz, at.xz))
                    plan = p;
            var seeds = new HashSet<uint>();
            foreach (var r in plan.Residents)
            {
                seeds.Add(r.Seed);
                r.GoneInTo = -1;
                r.Asked = float.MinValue;
                r.InSince = r.WaitSince = -1f;
            }
            var em = simWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            var gone = 0;
            foreach (var e in entities)
            {
                var s = em.GetComponentData<CreatureState>(e);
                if (s.Mode == CreatureMode.Held || !seeds.Contains(s.Individual) || !Households.Of(s.Individual, out var house, out var m))
                    continue;
                house.Taken &= ~(1u << m) | house.Pinned | house.Lost;
                memberLive.Remove(s.Individual);
                memberSentIn.Remove(s.Individual);
                housedCount = math.max(0, housedCount - 1);
                em.DestroyEntity(e);
                gone++;
            }
            return gone;
        }
#endif
    }
}
