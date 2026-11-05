using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// Quests, P-17 (#26, #27). (1) The host checks hand-ins: it keeps its own tally of what each player carries (it
    /// already decides every pickup, container take and drop, and pays every reward) and refuses a hand-in that claims
    /// more than the player carries, with a log line. (2) A third objective, "clear": make <c>creature</c> leave
    /// <c>radius</c> metres round the board; the host counts them for each player who took it, and it's done once
    /// none have been inside for <see cref="ClearHold"/> s, however they were driven out. Test flags: "-spawnstones"
    /// (host: 4 stones by board 0, 20 s in), "-fakehandin" (a client hands in without the items), "-clearquest" (take
    /// the nearest board's clear quest, standing at it), "-chase" (every 2 s, step up to the nearest counted creature to
    /// send it off).
    /// </summary>
    public partial class Items
    {
        const float ClearHold = 10f;
        /// <summary>The host's tally of what each player carries: NetworkId (0: the host's own player) → item → count.</summary>
        readonly Dictionary<int, Dictionary<byte, int>> carried = new Dictionary<int, Dictionary<byte, int>>();
        /// <summary>Clear quests in progress on the host: (player, board, slot) → seconds the area has been empty, and the last count logged.</summary>
        readonly Dictionary<(int, int, int), (float empty, int last, bool seen)> clearing = new Dictionary<(int, int, int), (float, int, bool)>();
        bool spawnStonesArg, fakeHandInArg, clearQuestArg, chaseArg;
        float chaseAt;
        FaunaDirector director;

        void CheckArgs(string[] args)
        {
            spawnStonesArg = System.Array.IndexOf(args, "-spawnstones") >= 0;
            fakeHandInArg = System.Array.IndexOf(args, "-fakehandin") >= 0;
            clearQuestArg = System.Array.IndexOf(args, "-clearquest") >= 0;
            chaseArg = System.Array.IndexOf(args, "-chase") >= 0;
        }

        /// <summary>The host's tally: <paramref name="count"/> more (or fewer) of an item on a player.</summary>
        void Carry(int player, byte item, int count)
        {
            if (!carried.TryGetValue(player, out var bag))
                carried[player] = bag = new Dictionary<byte, int>();
            bag.TryGetValue(item, out var have);
            bag[item] = math.max(have + count, 0);
        }

        int Carries(int player, byte item) => carried.TryGetValue(player, out var bag) && bag.TryGetValue(item, out var n) ? n : 0;

        /// <summary>Whether the host lets a client's hand-in through: it carries what the quest asks for. Takes it off the tally if so.</summary>
        bool CheckHandIn(int player, int board, int slot)
        {
            var q = Def(board, slot);
            var id = ItemId(q.item);
            var need = math.max(q.count, 1);
            var have = id >= 0 ? Carries(player, (byte)id) : 0;
            if (id < 0 || have < need)
            {
                Debug.LogWarning($"Quests: refused player {player}'s hand-in of {q.id} at board {board}: it claims {need} {q.item}, the host saw it carry {have}");
                return false;
            }
            Carry(player, (byte)id, -need);
            Debug.Log($"Quests: player {player}'s hand-in of {q.id} checked: carried {have} {q.item}, {need} handed in");
            return true;
        }

        static int PlayerId(EntityManager em, Entity connection) =>
            em.Exists(connection) && em.HasComponent<NetworkId>(connection) ? em.GetComponentData<NetworkId>(connection).Value : -1;

        // ------------------------------------------------------------------ clear an area

        int SpeciesIndex(string name)
        {
            director ??= Object.FindAnyObjectByType<FaunaDirector>();
            return director != null ? director.SpeciesIndex(name) : -1;
        }

        /// <summary>How many of a species are within the radius of a point, in the world that simulates them.</summary>
        int CountInside(int species, float3 centre, float radius, out float3 nearest)
        {
            nearest = default;
            var world = Hosting ? server : World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || species < 0)
                return -1;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureBody>(), ComponentType.ReadOnly<CreatureState>());
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var n = 0;
            var best = float.MaxValue;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Species != species)
                    continue;
                var d = math.distance(states[i].Position.xz, centre.xz);
                if (d > radius)
                    continue;
                n++;
                if (d < best)
                    (best, nearest) = (d, states[i].Position);
            }
            return n;
        }

        /// <summary>The host (or solo): every taken clear quest's count, each frame; done after ClearHold s at none.</summary>
        void UpdateClearing()
        {
            if (Joined || quests.Length == 0)
                return;
            var done = new List<(int, int, int)>();
            foreach (var kv in questRecord)
            {
                if (kv.Value != 1)
                    continue;
                var (player, board, slot) = kv.Key;
                var q = Def(board, slot);
                if (q.type != "clear")
                    continue;
                // P-19: creatures only live round players, so the area counts only while the player who took it is
                // within radius + 30 m; and it must have held at least one before it can be cleared (an area far from
                // everyone reads 0, and a despawn with nobody near isn't a clearing).
                if (!PlayerPosition(player, out var who) || math.distance(who.xz, boards[board].Pos.xz) > q.radius + 30f)
                    continue;
                var count = CountInside(SpeciesIndex(q.creature), boards[board].Pos, q.radius, out _);
                if (count < 0)
                    continue;
                if (!clearing.TryGetValue(kv.Key, out var c))
                    c = (0f, -1, false);   // (so the first count is logged too)
                if (count != c.last)
                    Debug.Log($"Quests: {q.id} for player {player} at board {board}: {count} {q.creature} within {q.radius:F0} m{(c.seen || count > 0 ? "" : " (none seen yet: can't be cleared)")}");
                var seen = c.seen || count > 0;
                c = (count == 0 && seen ? c.empty + Time.deltaTime : 0f, count, seen);
                clearing[kv.Key] = c;
                if (c.empty >= ClearHold)
                    done.Add(kv.Key);
            }
            foreach (var key in done)
            {
                var (player, board, slot) = key;
                clearing.Remove(key);
                Debug.Log($"Quests: {Def(board, slot).id} cleared: no {Def(board, slot).creature} inside for {ClearHold:F0} s");
                HostRecord(player, board, slot, 2, Hosting ? "host" : "solo");
                if (player == 0)
                    myQuests[(board, slot)] = 2;
                else if (Hosting)
                    SendToPlayer(player, new QuestStateRpc { Board = board, Slot = (byte)slot, State = 2 });
            }
        }

        /// <summary>A player's position on the host: 0 is the host's own (or solo's), others by their connection's id.</summary>
        bool PlayerPosition(int player, out float3 pos)
        {
            pos = default;
            if (player == 0 || !Hosting)
            {
                if (!TryPlayer(out var me))
                    return false;
                pos = me.Position;
                return true;
            }
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerTag>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<LocalTransform>());
            using var owners = q.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var places = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < owners.Length; i++)
                if (owners[i].NetworkId == player)
                {
                    pos = places[i].Position;
                    return true;
                }
            // (A remote player's body may not carry PlayerTag on the server: any ghost it owns with a position.)
            using var q2 = em.CreateEntityQuery(ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<LocalTransform>());
            using var owners2 = q2.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var places2 = q2.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < owners2.Length; i++)
                if (owners2[i].NetworkId == player)
                {
                    pos = places2[i].Position;
                    return true;
                }
            return false;
        }

        void SendToPlayer<T>(int player, T rpc) where T : unmanaged, IRpcCommand
        {
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            using var cs = q.ToEntityArray(Allocator.Temp);
            foreach (var c in cs)
                if (em.GetComponentData<NetworkId>(c).Value == player)
                    Send(em, c, rpc);
        }

        // ------------------------------------------------------------------ tests

        void CheckTests(LocalTransform player)
        {
            if (Joined)
            {
                using var iq = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamInGame));
                if (iq.IsEmpty)
                    return;
            }
            if (questTestAt == 0f)
                questTestAt = Time.realtimeSinceStartup;
            var age = Time.realtimeSinceStartup - questTestAt;
            if (spawnStonesArg && !Joined && boards.Count > 0 && Time.realtimeSinceStartup > 20f)
            {
                spawnStonesArg = false;
                // (What board 0's first quest asks for, if it's a fetch; else 4 stones.)
                var q0 = Def(0, 0);
                var name = q0.type == "fetch" ? q0.item : "Stone";
                var n = q0.type == "fetch" ? math.max(q0.count, 1) : 4;
                Spawn((byte)ItemId(name), n, boards[0].Pos + new float3(0.8f, 0f, -1.6f));
                Debug.Log($"Quests: -spawnstones: {n} {name} by board 0 (for {q0.id})");
            }
            if (clearQuestArg && age > 20f && boards.Count > 0)
            {
                clearQuestArg = false;
                int bestBoard = -1, bestSlot = -1;
                var bestD = float.MaxValue;
                // (The clear quest with the most of its creature inside right now, so there's something to drive off.)
                var bestCount = -1;
                for (int b = 0; b < boards.Count; b++)
                    for (int s = 0; s < boards[b].Offers.Length; s++)
                    {
                        var q = Def(b, s);
                        if (q.type != "clear")
                            continue;
                        var n = CountInside(SpeciesIndex(q.creature), boards[b].Pos, q.radius, out _);
                        var d = math.distance(boards[b].Pos, player.Position);
                        if (n > bestCount || n == bestCount && d < bestD)
                            (bestCount, bestD, bestBoard, bestSlot) = (n, d, b, s);
                    }
                Debug.Log($"Quests: -clearquest: board {bestBoard} slot {bestSlot} ({bestCount} inside as this machine sees it)");
                if (bestBoard >= 0)
                {
                    StandAt(boards[bestBoard].Pos + new float3(0f, 0.05f, -1.6f));
                    TakeQuest(bestBoard, bestSlot);
                }
            }
            if (chaseArg)
                WalkChase(player);
        }

        UnityEngine.InputSystem.Gamepad chasePad;

        /// <summary>
        /// "-chase" (P-19): walks the player at the nearest counted creature of a taken clear quest with a virtual
        /// gamepad's left stick, through the game's own input (no teleports), so whatever reacts to a player walking up
        /// reacts to this. Camera-relative, as a stick is.
        /// </summary>
        void WalkChase(LocalTransform player)
        {
            var stick = Vector2.zero;
            foreach (var kv in myQuests)
            {
                if (kv.Value != 1 || Def(kv.Key.Item1, kv.Key.Item2).type != "clear")
                    continue;
                var q = Def(kv.Key.Item1, kv.Key.Item2);
                if (CountInside(SpeciesIndex(q.creature), boards[kv.Key.Item1].Pos, q.radius + 5f, out var at) <= 0)
                    continue;
                var cam = Camera.main;
                if (cam == null)
                    break;
                var to = math.normalizesafe(at.xz - player.Position.xz);
                var f = math.normalizesafe(new float2(cam.transform.forward.x, cam.transform.forward.z));
                var r = new float2(f.y, -f.x);
                stick = new Vector2(math.dot(to, r), math.dot(to, f));
                if (Time.realtimeSinceStartup >= chaseAt)
                {
                    chaseAt = Time.realtimeSinceStartup + 3f;
                    Debug.Log($"Quests: -chase: walking at a {q.creature} {math.distance(at.xz, player.Position.xz):F1} m away");
                }
                break;
            }
            if (chasePad == null)
            {
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                chasePad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("ChasePad");
                UnityEngine.InputSystem.InputSystem.EnableDevice(chasePad);
            }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(chasePad, new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = stick });
        }

        void StandAt(float3 pos)
        {
            var e = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var pq = e.CreateEntityQuery(ComponentType.ReadWrite<Punchy.Player.PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!pq.TryGetSingletonEntity<LocalTransform>(out var pe) || !TryTerrain(out var terrain))
                return;
            var t = e.GetComponentData<LocalTransform>(pe);
            t.Position = new float3(pos.x, terrain.SampleHeight(pos.xz) + 0.05f, pos.z);
            e.SetComponentData(pe, t);
        }
    }
}
