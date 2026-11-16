using System.Collections.Generic;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    public partial class FaunaDirector
    {
        /// <summary>How near (m) a guard on its rounds sees a house's door, how near the guard sent there must be
        /// (m), and how close (m) it gets before it speaks.</summary>
        const float TheftSeeReach = 12f, TheftGuardReach = 40f, TheftGuardSpeaks = 3f;
        /// <summary>How long (s) the guard walks to the door, how long (s) a guard's line waits for it to get there, and
        /// how long (s) an owner's line waits for the thief to meet one of the household.</summary>
        const float TheftGuardWalk = 25f, TheftGuardWait = 60f, TheftOwnerWait = 600f;
        /// <summary>How near (m) the thief must be to one of the household for its line.</summary>
        const float TheftOwnerReach = 25f;

        /// <summary>A line still to be said about a theft: by one of a household to the thief (Guard 0), or by the
        /// guard sent to a door once it's there.</summary>
        struct TheftLine
        {
            public int House;
            public uint Guard;
            public float2 At;
            public string Item;
            public float Until;
        }

        /// <summary>Host: each connection's thefts (NetworkId → records, its own save's and this session's), for a
        /// joining client's catch-up; and the connections that have had theirs.</summary>
        readonly Dictionary<int, List<TheftRpc>> theftsByPlayer = new Dictionary<int, List<TheftRpc>>();
        readonly HashSet<Entity> theftsSentTo = new HashSet<Entity>();
        readonly List<Entity> theftsGone = new List<Entity>();
        /// <summary>Client: thefts heard and waiting for the households, and lines waiting for their speaker.</summary>
        readonly List<TheftRpc> theftsHeard = new List<TheftRpc>();
        readonly List<TheftLine> theftLines = new List<TheftLine>();
        World theftsSession, theftsServer, theftsClient;
        EntityQuery theftsConnections, theftsReceived, theftRecordsReceived, theftsMyId, theftsPlayer, theftsTalkers, theftsGuards;
        Unity.Mathematics.Random theftRng = new Unity.Mathematics.Random(0x7EF7u);
        /// <summary>Test flag "-theftguard" (host or solo): before a take is judged, the nearest guard on its rounds
        /// (200 m) is put 6 m out from that door, facing it, so a guard's sighting can be checked on a known setup.</summary>
        static readonly bool theftGuardArg = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-theftguard") >= 0;

        /// <summary>
        /// Each frame (A-134 row 3): the host or a solo game judges the takes Ownership.Took handed over (who saw, which
        /// guard goes) and the host tells every client; a client takes thefts in (its own player's into the record, a seen
        /// one's lines queued) and, once joined, tells the host its saved record. Lines are said when their speaker is near.
        /// </summary>
        void ServeThefts()
        {
            if (simWorld != theftsSession)
            {
                theftsSession = simWorld;
                theftsByPlayer.Clear();
                theftsSentTo.Clear();
                theftsServer = null;
                ownerCalls.Clear();
                ownersOut.Clear();
                guardCalls.Clear();
            }
            Thefts.Today = Cycle != null ? Cycle.Day : 0;
            if (Households.Ready)
                Thefts.DateSaved();   // (the save's records, once the households are up; a joined client reads its save late)
            var host = simWorld != null && simWorld.IsCreated && simWorld.IsServer();
            var solo = simWorld != null && simWorld.IsCreated && !host;
            if (Thefts.Pending.Count > 0)
            {
                if ((host || solo) && Households.Ready)
                    JudgeTakes(host);
                else if (!host && !solo)
                    Thefts.Pending.Clear();   // (a joined client never decides one)
            }
            if ((host || solo) && Households.Ready)
                SendHeldGuards(host);
            if (host)
                CatchUpThefts(simWorld);
            if (queryWorld != null && queryWorld.IsCreated && queryWorld.IsClient())
                HearThefts(queryWorld, !host && !solo);
            if (theftLines.Count > 0 && queryWorld != null && queryWorld.IsCreated)
                SayTheftLines(queryWorld);
        }

        /// <summary>Host or solo: each take still waiting is judged. Seen if one of the household is at home (HomeSign) or
        /// a guard on its rounds stands within TheftSeeReach of the door, facing it, with no wall between; then the
        /// nearest guard on its rounds within TheftGuardReach walks to the door. The host sends it to every client.</summary>
        void JudgeTakes(bool host)
        {
            var em = simWorld.EntityManager;
            if (theftsServer != simWorld)
            {
                theftsServer = simWorld;
                theftsGuards = em.CreateEntityQuery(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                theftsConnections = host ? em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>()) : default;
                theftRecordsReceived = host ? em.CreateEntityQuery(ComponentType.ReadOnly<TheftRecordRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>()) : default;
                theftsSentTo.Clear();
            }
            var hasKit = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.KitBox>()).TryGetSingletonBuffer<Punchy.Flora.KitBox>(out var kit, true) && kit.Length > 0;
            using var entities = theftsGuards.ToEntityArray(Allocator.Temp);
            using var states = theftsGuards.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = theftsGuards.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var day = Cycle != null ? Cycle.Day : 0;
            var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
            foreach (var take in Thefts.Pending)
            {
                if (take.Owner < 0 || take.Owner >= Households.All.Count)
                    continue;
                var house = Households.All[take.Owner];
                var outward = new float2(math.sin(house.Door.w), math.cos(house.Door.w));
                var spot = house.Door.xz + outward * 1.5f;
                if (theftGuardArg)
                    PlaceTheftGuard(em, entities, states, house.Door, spot);
                var home = HomeSign.Of(house, day, hour).Home;
                var seenBy = home > 0 ? "one of the household at home" : null;
                var guard = -1;
                var guardD = float.MaxValue;
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    if (!OnRounds(s))
                        continue;
                    var d = math.distance(s.Position.xz, spot);
                    if (d < guardD && d < TheftGuardReach)
                    {
                        guard = i;
                        guardD = d;
                    }
                    if (seenBy != null || d > TheftSeeReach)
                        continue;
                    var to = d > 0.01f ? (spot - s.Position.xz) / d : float2.zero;
                    var blob = bodies[i].Blob;
                    ref var b = ref blob.Value;
                    if (math.dot(new float2(math.sin(s.Yaw), math.cos(s.Yaw)), to) > 0.5f &&
                        !(hasKit && FolkTalkSystem.Walled(kit, s, ref b, new float3(spot.x, house.Door.y, spot.y))))
                        seenBy = $"the guard {FolkNames.Of(s.Individual) ?? s.Individual.ToString()} {d:F0} m from the door";
                }
                var rpc = new TheftRpc
                {
                    Thief = take.Player >= 0 ? take.Player : MyNetworkId(queryWorld),
                    House = take.Owner,
                    Key = house.Key,
                    Item = take.Item,
                    Seen = (byte)(seenBy != null ? 1 : 0),
                    Chase = spot
                };
                if (seenBy != null && guard >= 0)
                {
                    var s = states[guard];
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = spot;
                    s.Timer = TheftGuardWalk;
                    em.SetComponentData(entities[guard], s);
                    rpc.Guard = s.Individual;
                }
                Debug.Log($"[thefts] {(take.Player < 0 ? "the local player" : $"player {take.Player}")} took {take.Item} from {Ownership.Whose(take.Owner, "house")} (household {take.Owner}): " +
                          (seenBy != null ? $"seen by {seenBy}; {(guard >= 0 ? $"guard {FolkNames.Of(rpc.Guard) ?? rpc.Guard.ToString()} sent from {guardD:F0} m" : "no guard near")}" :
                              $"unseen ({home} at home, no guard on its rounds facing the door within {TheftSeeReach:F0} m)"));
                if (seenBy == null)
                    continue; // nobody saw it: nobody holds it against the thief, so nothing is kept or sent
                // (Row 4: seen from inside, one of them steps out at the door to face the thief.)
                if (home > 0)
                    CallOwnerOut(take.Owner, day, hour);
                // (Row 5: no guard here because every player is away, inside a room say: it goes once someone is near.)
                if (guard < 0 && NearestPlayer(spot) > Radius)
                    HoldGuardCall(rpc);
                if (host)
                {
                    if (!theftsByPlayer.TryGetValue(rpc.Thief, out var list))
                        theftsByPlayer[rpc.Thief] = list = new List<TheftRpc>();
                    var old = rpc;
                    old.Old = 1;
                    old.Ago = 0;
                    list.Add(old);
                    using var connections = theftsConnections.ToEntityArray(Allocator.Temp);
                    foreach (var c in connections)
                        SendTheft(em, c, rpc);
                }
                else
                    TakeTheft(rpc, true);
            }
            Thefts.Pending.Clear();
        }

        /// <summary>"-theftguard": the nearest guard on its rounds within 200 m of the spot stands 6 m out from the door,
        /// facing it (in the arrays too, so this judging sees it).</summary>
        static void PlaceTheftGuard(EntityManager em, NativeArray<Entity> entities, NativeArray<CreatureState> states, float4 door, float2 spot)
        {
            var best = -1;
            var bestD = 200f;
            for (int i = 0; i < states.Length; i++)
                if (states[i].Task == CreatureTask.Patrol && states[i].Mode == CreatureMode.Patrol && math.distance(states[i].Position.xz, spot) < bestD)
                {
                    best = i;
                    bestD = math.distance(states[i].Position.xz, spot);
                }
            if (best < 0)
            {
                var guards = 0;
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Task == CreatureTask.Patrol)
                        guards++;
                Debug.Log($"[thefts] -theftguard: no guard on its rounds within 200 m ({guards} guards of {states.Length} creatures made)");
                return;
            }
            var s = states[best];
            var outward = new float2(math.sin(door.w), math.cos(door.w));
            var at = door.xz + outward * 6f;
            s.Position = new float3(at.x, door.y, at.y);
            s.Yaw = math.atan2(-outward.x, -outward.y);
            states[best] = s;
            em.SetComponentData(entities[best], s);
            if (em.HasComponent<LocalTransform>(entities[best]))
                em.SetComponentData(entities[best], LocalTransform.FromPositionRotation(s.Position, quaternion.RotateY(s.Yaw)));
            Debug.Log($"[thefts] -theftguard: put guard {FolkNames.Of(s.Individual) ?? s.Individual.ToString()} 6 m out from the door (was {bestD:F0} m away)");
        }

        static void SendTheft(EntityManager em, Entity connection, TheftRpc theft)
        {
            var e = em.CreateEntity(typeof(TheftRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(e, theft);
            em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }

        /// <summary>Host: a joining client is sent its own thefts so far (as old: into its record, no reactions); and a
        /// client's saved record, as it tells it, is kept (MaxHeld at most, houses this world lacks dropped).</summary>
        void CatchUpThefts(World server)
        {
            var em = server.EntityManager;
            if (theftsServer != server)
            {
                theftsServer = server;
                theftsGuards = em.CreateEntityQuery(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                theftsConnections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                theftRecordsReceived = em.CreateEntityQuery(ComponentType.ReadOnly<TheftRecordRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
                theftsSentTo.Clear();
            }
            using var connections = theftsConnections.ToEntityArray(Allocator.Temp);
            theftsGone.Clear();
            foreach (var c in theftsSentTo)
                if (connections.IndexOf(c) < 0)
                    theftsGone.Add(c);
            foreach (var c in theftsGone)
                theftsSentTo.Remove(c);
            foreach (var c in connections)
            {
                if (theftsSentTo.Contains(c))
                    continue;
                theftsSentTo.Add(c);
                var id = em.GetComponentData<NetworkId>(c).Value;
                if (theftsByPlayer.TryGetValue(id, out var list) && list.Count > 0)
                {
                    foreach (var t in list)
                        SendTheft(em, c, t);
                    Debug.Log($"[thefts] host: sent player {id} its {list.Count} thefts as it joined");
                }
            }
            if (theftRecordsReceived.IsEmpty)
                return;
            using var rpcs = theftRecordsReceived.ToEntityArray(Allocator.Temp);
            var kept = new Dictionary<int, (int kept, int dropped)>();
            foreach (var e in rpcs)
            {
                var m = em.GetComponentData<TheftRecordRpc>(e);
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(e).SourceConnection;
                em.DestroyEntity(e);
                var id = em.Exists(from) && em.HasComponent<NetworkId>(from) ? em.GetComponentData<NetworkId>(from).Value : -1;
                if (id < 0)
                    continue;
                if (!theftsByPlayer.TryGetValue(id, out var list))
                    theftsByPlayer[id] = list = new List<TheftRpc>();
                kept.TryGetValue(id, out var n);
                var house = Thefts.Resolve(m.House.ToString());
                if (house < 0 || list.Count >= Thefts.MaxHeld || !Thefts.KnownItem(m.Item.ToString()) || m.Ago < 0 || m.Ago >= Thefts.FadeDays)
                {
                    kept[id] = (n.kept, n.dropped + 1);
                    continue;
                }
                list.Add(new TheftRpc { Thief = id, House = house, Key = Households.All[house].Key, Item = m.Item, Old = 1, Ago = m.Ago });
                kept[id] = (n.kept + 1, n.dropped);
            }
            foreach (var k in kept)
                Debug.Log($"[thefts] host: player {k.Key} told its saved record: kept {k.Value.kept}, dropped {k.Value.dropped} (most {Thefts.MaxHeld}, unknown houses or items, or faded)");
        }

        /// <summary>This machine's NetworkId in a client world (the host's own client's too), or -1 (solo).</summary>
        int MyNetworkId(World client)
        {
            if (client == null || !client.IsCreated || !client.IsClient())
                return -1;
            if (theftsClient != client)
                BindTheftClient(client);
            return theftsMyId.TryGetSingleton<NetworkId>(out var id) ? id.Value : -1;
        }

        void BindTheftClient(World client)
        {
            var em = client.EntityManager;
            theftsClient = client;
            theftsReceived = em.CreateEntityQuery(ComponentType.ReadOnly<TheftRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            theftsMyId = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
            theftsPlayer = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            theftsTalkers = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            theftsHeard.Clear();
        }

        /// <summary>A client world: takes the host's thefts in once the households are up (a joined client then tells the
        /// host its saved record, once). The host's own client world gets them too, and takes them like any other.</summary>
        void HearThefts(World client, bool joined)
        {
            var em = client.EntityManager;
            if (theftsClient != client)
                BindTheftClient(client);
            if (!theftsReceived.IsEmpty)
            {
                using var rpcs = theftsReceived.ToEntityArray(Allocator.Temp);
                foreach (var e in rpcs)
                {
                    theftsHeard.Add(em.GetComponentData<TheftRpc>(e));
                    em.DestroyEntity(e);
                }
            }
            if (!Households.Ready)
                return;
            foreach (var t in theftsHeard)
                TakeTheft(t, t.Thief == MyNetworkId(client));
            theftsHeard.Clear();
            if (joined && theftsMyId.TryGetSingleton<NetworkId>(out _))
            {
                var told = 0;
                foreach (var r in Thefts.Mine)
                {
                    if (!r.ToHost || !r.Dated)
                        continue;
                    r.ToHost = false;
                    var e = em.CreateEntity(typeof(TheftRecordRpc), typeof(SendRpcCommandRequest));
                    em.SetComponentData(e, new TheftRecordRpc { House = r.House, Item = r.Item, Ago = math.max(0, Thefts.Today - r.Day) });
                    told++;
                }
                if (told > 0)
                    Debug.Log($"[thefts] client: told the host {told} thefts from this player's save");
            }
        }

        /// <summary>One theft on this machine: checked against its households and items (logged and dropped when it
        /// names one this world lacks), into the record if this machine's player took it, and a seen new one's lines
        /// queued.</summary>
        void TakeTheft(TheftRpc t, bool mine)
        {
            var item = t.Item.ToString();
            if (t.House < 0 || t.House >= Households.All.Count || Households.All[t.House].Key != t.Key || !Thefts.KnownItem(item))
            {
                Debug.Log($"[thefts] dropped a theft of {item} from household {t.House}: no such household or item here");
                return;
            }
            if (t.Seen == TheftGuardLate)
            {
                // (Row 5: a guard sent late to an earlier theft, which was taken in when it was judged: its line only.)
                if (t.Guard != 0)
                {
                    theftLines.Add(new TheftLine { House = t.House, Guard = t.Guard, At = t.Chase, Item = item, Until = Time.time + TheftGuardLateWait });
                    Debug.Log($"[thefts] guard {FolkNames.Of(t.Guard) ?? t.Guard.ToString()} is on its way to {Ownership.Whose(t.House, "house")}");
                }
                return;
            }
            if (t.Seen == 0)
                return; // only a seen theft counts (the host sends no other, this guards an old sender)
            var house = Households.All[t.House];
            if (mine && Thefts.AddMine(Thefts.KeyOf(house), item, Thefts.Today - t.Ago) && t.Old == 0)
                Debug.Log($"[thefts] this player's record: took {item} from {Ownership.Whose(t.House, "house")}, seen; {Thefts.Mine.Count} thefts held");
            if (t.Old != 0)
                return;
            var now = Time.time;
            if (mine)
                theftLines.Add(new TheftLine { House = t.House, At = house.Door.xz, Item = item, Until = now + TheftOwnerWait });
            if (t.Guard != 0)
                theftLines.Add(new TheftLine { House = t.House, Guard = t.Guard, At = t.Chase, Item = item, Until = now + TheftGuardWait });
        }

        /// <summary>Lines waiting for their speaker (on this machine's client world): one of the household near the thief
        /// (TheftOwnerReach), or the sent guard within TheftGuardSpeaks of the door. Through FolkTalkSystem.Shown.</summary>
        void SayTheftLines(World client)
        {
            var now = Time.time;
            for (int k = theftLines.Count - 1; k >= 0; k--)
                if (now >= theftLines[k].Until)
                {
                    Debug.Log($"[thefts] a {(theftLines[k].Guard != 0 ? "guard's" : "household's")} line about {theftLines[k].Item} went unsaid (no speaker near in time)");
                    theftLines.RemoveAt(k);
                }
            if (theftLines.Count == 0 || FolkTalkSystem.Quiet)
                return;
            var em = client.EntityManager;
            if (theftsClient != client)
                BindTheftClient(client);
            var hasPlayer = theftsPlayer.CalculateEntityCount() == 1;
            var player = hasPlayer ? theftsPlayer.GetSingleton<LocalTransform>().Position : float3.zero;
            using var entities = theftsTalkers.ToEntityArray(Allocator.Temp);
            using var states = theftsTalkers.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = theftsTalkers.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            for (int k = theftLines.Count - 1; k >= 0; k--)
            {
                var line = theftLines[k];
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    if (line.Guard != 0)
                    {
                        if (s.Individual != line.Guard || math.distance(s.Position.xz, line.At) > TheftGuardSpeaks)
                            continue;
                    }
                    else if (!hasPlayer || math.distance(s.Position, player) > TheftOwnerReach ||
                             !Households.Of(s.Individual, out var of, out _) || Households.All.IndexOf(of) != line.House)
                        continue;
                    var blob = bodies[i].Blob;
                    ref var b = ref blob.Value;
                    var text = TheftText(line.Guard != 0 ? "theftGuard" : "theftOwner", line.Item);
                    FolkTalkSystem.Shown.Add(new FolkTalkSystem.Said
                    {
                        Who = entities[i], Text = text, Head = FolkTalkSystem.Head(s, ref b), Start = now, Until = now + 4f + text.Length * 0.05f
                    });
                    FolkTalkLabels.Ensure();
                    FolkTalkLabels.Show(true);
                    Debug.Log($"[thefts] {FolkNames.Of(s.Individual) ?? "a folk"} ({(line.Guard != 0 ? "the guard" : "of the household")}) at ({s.Position.x:F0}, {s.Position.z:F0}): {text}");
                    theftLines.RemoveAt(k);
                    break;
                }
            }
        }

        /// <summary>A line from dialogue.json's conversation.Any topic (theftOwner, theftGuard), by weight, {item} filled;
        /// a plain one if the file has none.</summary>
        string TheftText(string topic, string item)
        {
            var lines = DialogueArchive.Current?.Talk != null && DialogueArchive.Current.Talk.Cultures.TryGetValue("Any", out var any) &&
                        any.TryGetValue(topic, out var list) ? list : null;
            string text = null;
            if (lines != null && lines.Count > 0)
            {
                var total = 0f;
                foreach (var l in lines)
                    total += math.max(0f, l.Weight);
                var roll = theftRng.NextFloat(total);
                foreach (var l in lines)
                {
                    text = l.Text;
                    roll -= math.max(0f, l.Weight);
                    if (roll < 0f)
                        break;
                }
            }
            text ??= topic == "theftGuard" ? "Halt! A theft was reported here." : "Hey! That {item} was ours!";
            return text.Replace("{item}", item.ToLowerInvariant());
        }
    }
}
