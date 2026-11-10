using System.Collections.Generic;
using System.IO;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Interact
{
    /// <summary>Client → host: what a talk asks of a folk (only the host runs its brain): FolkTalk.Request.</summary>
    public struct FolkTalkRpc : IRpcCommand
    {
        public uint Individual;
        public byte Request, Value;
        public float3 At;
    }

    /// <summary>
    /// Talking to folk (P-26). The Use key on a folk in front of the player (DoorPick's rules) opens a small window of
    /// FolkConversation.Topics; a topic picked (mouse, keys 1-9, arrows or d-pad with Enter or A) shows its answer with
    /// the speaker's name and job. An answer offering a quest or a gift can be accepted or declined. Esc, B or walking away
    /// ends it. Played as an animal there's no window: the folk's reaction goes up as a line over its head, and it does
    /// what the reaction says. The words are each machine's own and nothing about them is sent; what the folk does (stop
    /// and face the player, gesture, respond to an animal) goes to the host, which runs its brain (FolkTalkRpc). Names
    /// come from StreamingAssets/folk-names.json, picked by Individual, so every machine calls a folk the same.
    /// </summary>
    public partial class FolkTalk : MonoBehaviour
    {
        enum Request : byte { Hold = 1, Release, Gesture, Respond }

        // (The folk waits this long at most, in case the player who stopped it drops out of the game.)
        const float HoldSeconds = 180f, LeaveAt = 5f, MaxHeight = 2f, GestureSeconds = 2f, EndingSeconds = 2.5f, BeastAgain = 1f;
        const float WalkAwayPush = 0.5f, CameraPull = 3f;
        const float PromptDrop = 0.7f;
        const int GiftCount = 3;
        const string NamesFile = "folk-names.json";

        /// <summary>How far the folk the player could talk to is (float.MaxValue: none): the Use key goes to the nearest of it, a door and an item.</summary>
        public static float TargetDistance = float.MaxValue;
        public static bool Open => instance != null && instance.talking;
        /// <summary>How far the camera comes in for a talk (m; 0: no talk).</summary>
        public static float CameraArm => Open ? CameraPull : 0f;

        static FolkTalk instance;
        World server, client, snapshotWorld;
        EntityQuery creatures;
        bool hasQuery;

        Entity target;
        float heldUntil;
        Vector3 promptAt;
        string prompt;
        float beastReadyAt;

        bool talking, armed, offering, metBefore, wasQuiet;
        Entity folk;
        FolkTalker who;
        string title, body;
        readonly List<(string Id, string Label)> choices = new List<(string, string)>();
        int selected;
        float endAt;
        Answer shown;
        // (This session's memory: who the player has met, and whose gift they've had.)
        readonly HashSet<uint> met = new HashSet<uint>();
        readonly HashSet<uint> gifted = new HashSet<uint>();
        readonly HashSet<uint> holds = new HashSet<uint>();
        string[] names = System.Array.Empty<string>();
        GUIStyle boxStyle, textStyle, promptStyle;
        bool testPress;

        [System.Serializable]
        class NameFile { public string[] names; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null)
                return;
            var go = new GameObject("FolkTalk");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<FolkTalk>();
            instance.LoadNames();
            instance.TestArgs();
        }

        void Start() => GuiHook.Add(gameObject, DrawGui, () => talking || prompt != null && !Overlay.AnyOpen, false);

        void LoadNames()
        {
            try
            {
                var read = JsonUtility.FromJson<NameFile>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, NamesFile)));
                if (read?.names != null)
                    names = read.names;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"FolkTalk: couldn't read {NamesFile}: {e.Message}");
            }
        }

        bool Hosting => server != null && server.IsCreated && server.IsServer();
        bool Joined => !Hosting && client != null && client.IsCreated && client.IsClient();

        void Update()
        {
            PumpTestPad();
            (server, client) = (NetWorlds.Server, NetWorlds.Client);
            if (Hosting)
                ServeRequests();
            if (talking)
            {
                UpdateTalk();
                return;
            }
            FindTarget();
            var used = UseKey.WasPressed() || testPress;
            testPress = false;
            if (used && target != Entity.Null && !Overlay.AnyOpen)
                Begin();
        }

        // ------------------------------------------------------------------ who the player means

        bool TryPlayer(out LocalTransform player)
        {
            player = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            return q.TryGetSingleton(out player);
        }

        /// <summary>The folk the player means, by the door rules' reach and cone, and the prompt for it.</summary>
        void FindTarget()
        {
            prompt = null;
            TargetDistance = float.MaxValue;
            var was = target;
            target = Entity.Null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || !TryPlayer(out var player))
                return;
            if (!hasQuery || snapshotWorld != world)
            {
                creatures = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                (hasQuery, snapshotWorld) = (true, world);
            }
            var forward = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz);
            var camera = Camera.main;
            var look = camera != null ? math.normalizesafe(((float3)camera.transform.forward).xz) : forward;
            CreatureSnapshotSystem.Get(world, creatures, out var states, out var bodies, out var entities);
            var bestScore = float.MaxValue;
            var distance = float.MaxValue;
            var far = DoorPick.Reach * DoorPick.Reach;
            var at = float3.zero;
            for (int i = 0; i < states.Length; i++)
            {
                if (math.distancesq(states[i].Position.xz, player.Position.xz) >= far || math.abs(states[i].Position.y - player.Position.y) > MaxHeight)
                    continue;
                if (!bodies[i].Blob.IsCreated)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                if (!FolkTalkSystem.CanTalk(states[i], ref b))
                    continue;
                var d = math.distance(states[i].Position.xz, player.Position.xz);
                if (DoorPick.Aim(player.Position.xz, forward, look, states[i].Position.xz, d, out var score) && score < bestScore && !IsWalled(world, states[i], ref b, player.Position))
                    (bestScore, target, distance, at) = (score, entities[i], d, FolkTalkSystem.Head(states[i], ref b));
                // (Held for a moment, like a door's, so the prompt doesn't flicker as it or the player moves.)
                else if (entities[i] == was && target == Entity.Null && Time.realtimeSinceStartup < heldUntil && d < DoorPick.Reach)
                    (target, distance, at) = (was, d, FolkTalkSystem.Head(states[i], ref b));
            }
            CreatureSnapshotSystem.Release(world, states, bodies, entities);
            if (target == Entity.Null)
                return;
            if (bestScore < float.MaxValue)
                heldUntil = Time.realtimeSinceStartup + DoorPick.Hold;
            TargetDistance = distance;
            // (The nearest of this, a door and an item takes the Use key.)
            if (distance >= Doors.FacingDistance || distance >= Items.Items.TargetDistance)
            {
                target = Entity.Null;
                return;
            }
            // (Below the head: an animal's reaction goes up over it, on the same spot.)
            promptAt = at - new float3(0f, PromptDrop, 0f);
            prompt = $"{UseKey.Name}  talk";
        }

        static bool IsWalled(World world, in CreatureState s, ref CreatureBlob b, float3 player)
        {
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            if (q.IsEmpty)
                return false;
            var kit = q.GetSingletonBuffer<KitBox>(true);
            return kit.Length > 0 && FolkTalkSystem.Walled(kit, s, ref b, player);
        }

        // ------------------------------------------------------------------ the talk

        void Begin()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (!FolkConversation.TryTalker(world, target, null, out var talker))
                return;
            if (talker.Beast)
            {
                MeetAsBeast(world, target, talker);
                return;
            }
            (folk, who) = (target, talker);
            (talking, offering, armed, selected, endAt) = (true, false, false, 0, 0f);
            prompt = null;
            title = NameOf(who);
            metBefore = !met.Add(who.Individual);
            body = metBefore ? "Oh, it's you again." : "They turn to you.";
            FillTopics(metBefore);
            // (No walk-by lines over the window's folk while it's talking to the player.)
            wasQuiet = FolkTalkSystem.Quiet;
            FolkTalkSystem.Quiet = true;
            FolkTalkSystem.Shown.RemoveAll(l => l.Who == folk);
            Send(Request.Hold, who.Individual, 0, default);
            Overlay.HoldForTalk();
            MainMenu.SetPlayerActions(false);
            Debug.Log($"[talk] opened with {title} (#{who.Individual:X8}), {choices.Count} topics, met before: {metBefore}");
        }

        /// <summary>The topics in the window; someone met before gets "how are you" first, and "who are you" further down.</summary>
        void FillTopics(bool again)
        {
            choices.Clear();
            foreach (var t in FolkConversation.Topics(who))
                choices.Add((t.Id, t.Label));
            if (!again)
                return;
            var i = choices.FindIndex(c => c.Id == FolkConversation.Who);
            if (i < 0)
                return;
            var whoTopic = choices[i];
            choices.RemoveAt(i);
            choices.Insert(math.max(choices.Count - 1, 0), whoTopic);
        }

        string NameOf(in FolkTalker talker)
        {
            var job = talker.Job ?? talker.Trade ?? DialogueArchive.Spoken(talker.Species);
            var name = names.Length > 0 ? names[BiomeSampling.Scramble(talker.Individual ^ 0x4E414D45u) % (uint)names.Length] : null;
            return name != null ? $"{name}, {job?.ToLowerInvariant()}" : job;
        }

        void MeetAsBeast(World world, Entity who2, in FolkTalker talker)
        {
            if (Time.realtimeSinceStartup < beastReadyAt)
                return;
            beastReadyAt = Time.realtimeSinceStartup + BeastAgain;
            var answer = FolkConversation.Ask(talker, FolkConversation.Meet);
            var em = world.EntityManager;
            var s = em.GetComponentData<CreatureState>(who2);
            ref var b = ref em.GetComponentData<CreatureBody>(who2).Blob.Value;
            var now = Time.time;
            FolkTalkSystem.Shown.RemoveAll(l => l.Who == who2);
            FolkTalkSystem.Shown.Add(new FolkTalkSystem.Said { Who = who2, Text = answer.Text, Head = FolkTalkSystem.Head(s, ref b), Start = now, Until = now + 3.5f + answer.Text.Length * 0.05f });
            FolkTalkLabels.Ensure();
            FolkTalkLabels.Show(true);
            if (answer.Action != IdleAction.None)
                Send(Request.Gesture, talker.Individual, (byte)answer.Action, default);
            if (answer.Response != BeastResponse.None && TryPlayer(out var player))
                Send(Request.Respond, talker.Individual, (byte)answer.Response, player.Position);
            Debug.Log($"[talk] met as an animal: {answer}");
        }

        void Choose(int index)
        {
            if (index < 0 || index >= choices.Count || endAt > 0f)
                return;
            var id = choices[index].Id;
            selected = 0;
            if (offering)
            {
                Decide(id == "accept");
                return;
            }
            shown = FolkConversation.Ask(folk, id);
            if (shown.Text == null)
            {
                End("gone");
                return;
            }
            body = shown.Text;
            Debug.Log($"[talk] {title}: {shown}");
            if (shown.Action != IdleAction.None)
                Send(Request.Gesture, who.Individual, (byte)shown.Action, default);
            if (shown.Ends)
            {
                endAt = Time.realtimeSinceStartup + EndingSeconds;
                choices.Clear();
                return;
            }
            var offers = shown.Offer.Kind == OfferKind.Quest && !Items.Items.HasFolkQuest(shown.Offer.Id)
                         || shown.Offer.Kind == OfferKind.Item && !gifted.Contains(who.Individual);
            if (!offers)
                return;
            offering = true;
            choices.Clear();
            choices.Add(("accept", "Accept"));
            choices.Add(("decline", "Decline"));
        }

        void Decide(bool accept)
        {
            offering = false;
            if (accept && shown.Offer.Kind == OfferKind.Quest && Items.Items.TakeFolkQuest(shown.Offer.Id, out var town))
                body += $"\n\n(Added to your quests. Hand it in at the notice board in {town}.)";
            else if (accept && shown.Offer.Kind == OfferKind.Item && Items.Items.GiveFromFolk(shown.Offer.Id, GiftCount, out var item))
            {
                gifted.Add(who.Individual);
                body += $"\n\n(You receive {GiftCount} x {item}.)";
            }
            Debug.Log($"[talk] {(accept ? "accepted" : "declined")} {shown.Offer}");
            FillTopics(metBefore);
        }

        void UpdateTalk()
        {
            Overlay.HoldForTalk();
            MainMenu.SetPlayerActions(false);
            var keys = Keyboard.current;
            var pad = Gamepad.current;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(folk) || !TryPlayer(out var player))
            {
                End("gone");
                return;
            }
            var state = world.EntityManager.GetComponentData<CreatureState>(folk);
            if (math.distance(state.Position.xz, player.Position.xz) > LeaveAt)
            {
                End("too far");
                return;
            }
            if (endAt > 0f && Time.realtimeSinceStartup >= endAt)
            {
                End("said goodbye");
                return;
            }
            if (keys != null && keys.escapeKey.wasPressedThisFrame || pad != null && pad.buttonEast.wasPressedThisFrame)
            {
                End("cancelled");
                return;
            }
            // Walking away: a fresh push on the keys or stick (the one that brought the player here doesn't count).
            var move = pad != null ? pad.leftStick.ReadValue().magnitude > WalkAwayPush : false;
            if (keys != null)
                move |= keys.wKey.isPressed || keys.aKey.isPressed || keys.sKey.isPressed || keys.dKey.isPressed;
            if (!move)
                armed = true;
            else if (armed)
            {
                End("walked away");
                return;
            }
            if (keys != null)
            {
                for (int i = 0; i < 9; i++)
                    if (keys[Key.Digit1 + i].wasPressedThisFrame)
                    {
                        Choose(i);
                        return;
                    }
                if (keys.upArrowKey.wasPressedThisFrame)
                    Step(-1);
                if (keys.downArrowKey.wasPressedThisFrame)
                    Step(1);
                if (keys.enterKey.wasPressedThisFrame)
                    Choose(selected);
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame)
                    Step(-1);
                if (pad.dpad.down.wasPressedThisFrame)
                    Step(1);
                if (pad.buttonSouth.wasPressedThisFrame)
                    Choose(selected);
            }
        }

        void Step(int by)
        {
            if (choices.Count > 0)
                selected = (selected + by + choices.Count) % choices.Count;
        }

        void End(string why)
        {
            if (!talking)
                return;
            talking = false;
            FolkTalkSystem.Quiet = wasQuiet;
            Send(Request.Release, who.Individual, 0, default);
            MainMenu.SetPlayerActions(true);
            // (Overlay stays held for one more frame, so the Esc that ended this doesn't open the main menu.)
            Overlay.HoldForTalk();
            Debug.Log($"[talk] ended: {why}");
        }

        // ------------------------------------------------------------------ what the folk does (the host's)

        void Send(Request request, uint individual, byte value, float3 at)
        {
            if (Joined)
            {
                var em = client.EntityManager;
                using var q = em.CreateEntityQuery(typeof(NetworkId));
                if (q.IsEmpty)
                    return;
                var rpc = em.CreateEntity(typeof(FolkTalkRpc), typeof(SendRpcCommandRequest));
                em.SetComponentData(rpc, new FolkTalkRpc { Individual = individual, Request = (byte)request, Value = value, At = at });
                em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = q.GetSingletonEntity() });
                return;
            }
            Apply(request, individual, value, at, LocalPlayerId());
        }

        /// <summary>The local player's network id: their connection's when hosting, 0 solo.</summary>
        int LocalPlayerId()
        {
            if (!Hosting || client == null || !client.IsCreated)
                return 0;
            using var q = client.EntityManager.CreateEntityQuery(typeof(NetworkId));
            return q.IsEmpty ? 0 : q.GetSingleton<NetworkId>().Value;
        }

        void ServeRequests()
        {
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(typeof(FolkTalkRpc), typeof(ReceiveRpcCommandRequest));
            using var requests = q.ToEntityArray(Allocator.Temp);
            foreach (var r in requests)
            {
                var m = em.GetComponentData<FolkTalkRpc>(r);
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                var id = em.Exists(from) && em.HasComponent<NetworkId>(from) ? em.GetComponentData<NetworkId>(from).Value : 0;
                Apply((Request)m.Request, m.Individual, m.Value, m.At, id);
                em.DestroyEntity(r);
            }
        }

        void Apply(Request request, uint individual, byte value, float3 at, int playerId)
        {
            if (request == Request.Respond)
            {
                FolkConversation.Respond(individual, new Answer { Response = (BeastResponse)value }, at);
                return;
            }
            var sim = NetWorlds.Server;
            if (sim == null || !sim.IsCreated)
                return;
            var em = sim.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var s = em.GetComponentData<CreatureState>(e);
                if (s.Individual != individual)
                    continue;
                switch (request)
                {
                    case Request.Hold:
                        // (Follow's "wait facing them while they stand" is the stop; one already following on a quest carries on.)
                        if (!Follow.IsFollowing(e) && Follow.Start(e, playerId, HoldSeconds))
                            holds.Add(individual);
                        break;
                    case Request.Release:
                        if (holds.Remove(individual))
                            Follow.Stop(e);
                        break;
                    case Request.Gesture:
                        s.Action = (IdleAction)value;
                        s.ActionTime = 0f;
                        s.ActionLength = GestureSeconds;
                        em.SetComponentData(e, s);
                        break;
                }
                return;
            }
        }

        // ------------------------------------------------------------------ drawing

        void DrawGui()
        {
            if (talking)
                DrawWindow();
            else if (prompt != null && !Overlay.AnyOpen)
                DrawPrompt();
        }

        void DrawPrompt()
        {
            promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, richText = true };
            var size = promptStyle.CalcSize(new GUIContent(prompt)) + new Vector2(24f, 10f);
            var x = (Screen.width - size.x) * 0.5f;
            var y = Screen.height * 0.72f;
            var camera = Camera.main;
            if (camera != null)
            {
                var at = camera.WorldToScreenPoint(promptAt);
                if (at.z > 0.1f)
                {
                    x = Mathf.Clamp(at.x - size.x * 0.5f, 8f, Mathf.Max(8f, Screen.width - size.x - 8f));
                    y = Mathf.Clamp(Screen.height - at.y - size.y * 0.5f, 8f, Mathf.Max(8f, Screen.height - size.y - 8f));
                }
            }
            GUI.Box(new Rect(x, y, size.x, size.y), prompt, promptStyle);
        }

        /// <summary>The window, low on the screen: who's speaking, what they said, then the numbered choices.</summary>
        void DrawWindow()
        {
            boxStyle ??= new GUIStyle(GUI.skin.box);
            textStyle ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            // (Sized by the screen, as the walk-by lines are: the game has no UI scale option yet.)
            var size = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 42f));
            textStyle.fontSize = size;
            var width = Mathf.Min(Screen.width - 32f, size * 34f);
            var pad = size * 0.7f;
            var inner = width - pad * 2f;
            var titleText = $"<b>{title}</b>";
            var titleHeight = textStyle.CalcHeight(new GUIContent(titleText), inner);
            var bodyHeight = textStyle.CalcHeight(new GUIContent(body), inner);
            var rowHeight = size * 1.6f;
            var height = pad * 2f + titleHeight + bodyHeight + size * 0.5f + choices.Count * rowHeight;
            var x = (Screen.width - width) * 0.5f;
            var y = Mathf.Max(8f, Screen.height - height - size * 1.5f);
            GUI.Box(new Rect(x, y, width, height), GUIContent.none, boxStyle);
            var line = y + pad;
            GUI.Label(new Rect(x + pad, line, inner, titleHeight), titleText, textStyle);
            line += titleHeight;
            GUI.Label(new Rect(x + pad, line, inner, bodyHeight), body, textStyle);
            line += bodyHeight + size * 0.5f;
            for (int i = 0; i < choices.Count; i++)
            {
                var label = $"{i + 1}.  {choices[i].Label}";
                var rect = new Rect(x + pad, line, inner, rowHeight);
                if (i == selected)
                    label = $"<b>{label}</b>";
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    Choose(i);
                    return;
                }
                GUI.Label(rect, label, textStyle);
                line += rowHeight;
            }
        }
    }
}
