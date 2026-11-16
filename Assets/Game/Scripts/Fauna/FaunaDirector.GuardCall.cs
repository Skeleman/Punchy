using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-134 row 5: a guard for a thief inside a room. Rooms are off the island, so with every player inside one the
    // town's creatures are gone and no guard is there to send when a theft is seen from inside the house. The send is
    // held instead, the same way row 4 holds the owner's call: once a player is within Radius of that door again (the
    // thief out of the room, or anyone in the street) and the town's guards are back, the nearest one on its rounds
    // within TheftGuardFetch (the owner has called for one, so it comes from across town) walks to the door. Past
    // TheftOwnerWait the call lapses. Host or solo only; the guard's line reaches every machine on the same TheftRpc as
    // before (Seen = TheftGuardLate: the guard's line only).
    public partial class FaunaDirector
    {
        /// <summary>TheftRpc.Seen for a guard sent late to an earlier theft: only its line is queued (the theft itself,
        /// with the owner's line and the record, came when it was judged).</summary>
        const byte TheftGuardLate = 2;
        /// <summary>How often (s) held guard calls look for a player and a guard near their door.</summary>
        const float GuardCallEvery = 0.5f;
        /// <summary>How often (s) a held call with a player near but no guard says so in the log.</summary>
        const float GuardCallTell = 10f;
        /// <summary>How far (m) a held call fetches its guard from (the owner has called for one: further than
        /// TheftGuardReach, the guard who happened to be near), and how long (s) that guard's line waits for it to walk
        /// there (TheftGuardWait scaled to the longer walk).</summary>
        const float TheftGuardFetch = 120f, TheftGuardLateWait = TheftGuardWait * TheftGuardFetch / TheftGuardReach;

        /// <summary>Host or solo: seen thefts whose guard is still to be sent, and when they were last looked at.</summary>
        readonly List<GuardCall> guardCalls = new List<GuardCall>();
        float guardCallsAt;

        struct GuardCall
        {
            public TheftRpc Theft;
            public float Until;
            public float ToldAt;   // (when the 'no guard yet' line may be logged again)
        }

        /// <summary>From JudgeTakes: a seen theft with no guard to send and no player near the door holds its guard
        /// until someone is.</summary>
        void HoldGuardCall(TheftRpc theft)
        {
            guardCalls.Add(new GuardCall { Theft = theft, Until = Time.time + TheftOwnerWait });
            Debug.Log($"[thefts] the guard for {Ownership.Whose(theft.House, "house")} waits: no player within {Radius:F0} m of its door");
        }

        /// <summary>Whether a creature is a guard on its rounds (one JudgeTakes may send).</summary>
        static bool OnRounds(in CreatureState s) =>
            s.Task == CreatureTask.Patrol && (s.Mode == CreatureMode.Patrol || s.Mode == CreatureMode.Wander);

        /// <summary>
        /// Host or solo, each frame after JudgeTakes: twice a second, each held call whose door has a player within
        /// Radius sends the nearest guard on its rounds within TheftGuardFetch (still none: it keeps waiting, the town's
        /// guards may still be coming in), and tells every machine which guard went.
        /// </summary>
        void SendHeldGuards(bool host)
        {
            if (guardCalls.Count == 0 || Time.time < guardCallsAt)
                return;
            guardCallsAt = Time.time + GuardCallEvery;
            var em = simWorld.EntityManager;
            if (theftsServer != simWorld)
                return;   // (JudgeTakes makes the queries; a call exists only after it ran)
            var now = Time.time;
            NativeArray<Entity> entities = default;
            NativeArray<CreatureState> states = default;
            for (int k = guardCalls.Count - 1; k >= 0; k--)
            {
                var call = guardCalls[k];
                var whose = Ownership.Whose(call.Theft.House, "house");
                if (now >= call.Until)
                {
                    Debug.Log($"[thefts] no guard went to {whose}: no player came near its door in time");
                    guardCalls.RemoveAt(k);
                    continue;
                }
                var spot = call.Theft.Chase;
                if (NearestPlayer(spot) > Radius)
                    continue;   // (the thief still in the room)
                if (!entities.IsCreated)
                {
                    entities = theftsGuards.ToEntityArray(Allocator.Temp);
                    states = theftsGuards.ToComponentDataArray<CreatureState>(Allocator.Temp);
                }
                var guard = -1;
                var guardD = TheftGuardFetch;
                var rounds = 0;
                var nearest = float.MaxValue;
                for (int i = 0; i < states.Length; i++)
                {
                    if (!OnRounds(states[i]))
                        continue;
                    var d = math.distance(states[i].Position.xz, spot);
                    rounds++;
                    nearest = math.min(nearest, d);
                    if (d < guardD)
                    {
                        guard = i;
                        guardD = d;
                    }
                }
                if (guard < 0)
                {
                    if (now >= call.ToldAt)
                    {
                        Debug.Log($"[thefts] the guard for {whose} still waits: a player is near but no guard on its rounds " +
                                  $"within {TheftGuardFetch:F0} m ({rounds} on rounds of {states.Length}, nearest {(rounds > 0 ? nearest.ToString("F0") : "-")} m)");
                        call.ToldAt = now + GuardCallTell;
                        guardCalls[k] = call;
                    }
                    continue;
                }
                guardCalls.RemoveAt(k);
                var s = states[guard];
                SetMode(ref s, CreatureMode.Wander);
                s.Target = spot;
                s.Timer = TheftGuardWalk;
                em.SetComponentData(entities[guard], s);
                states[guard] = s;
                var rpc = call.Theft;
                rpc.Seen = TheftGuardLate;
                rpc.Guard = s.Individual;
                Debug.Log($"[thefts] guard {FolkNames.Of(rpc.Guard) ?? rpc.Guard.ToString()} sent to {whose} from {guardD:F0} m, " +
                          $"nearest player {NearestPlayer(spot):F0} m (held {TheftOwnerWait - (call.Until - now):F0} s)");
                if (host)
                {
                    using var connections = theftsConnections.ToEntityArray(Allocator.Temp);
                    foreach (var c in connections)
                        SendTheft(em, c, rpc);
                }
                else
                    TakeTheft(rpc, true);
            }
            if (entities.IsCreated)
            {
                entities.Dispose();
                states.Dispose();
            }
        }
    }
}
