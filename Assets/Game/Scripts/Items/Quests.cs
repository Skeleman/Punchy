using System.Collections.Generic;
using Punchy.Interact;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Items
{
    /// <summary>Client → host: take quest slot Slot from board Board.</summary>
    public struct TakeQuestRpc : IRpcCommand { public int Board; public byte Slot; }
    /// <summary>Client → host: hand quest Board/Slot in (the client has given up the items).</summary>
    public struct TurnInRpc : IRpcCommand { public int Board; public byte Slot; }
    /// <summary>Host → the player: quest Board/Slot is now State (1 taken, 2 done).</summary>
    public struct QuestStateRpc : IRpcCommand { public int Board; public byte Slot, State; }

    /// <summary>One quest in StreamingAssets/quests.json (see its _readme).</summary>
    [System.Serializable]
    public class QuestDef
    {
        public string id;
        /// <summary>"fetch": bring count of item to the board; "find": a rare item is hidden near the board: bring it back.</summary>
        public string type;
        public string item;
        public int count = 1;
        public string text;
        /// <summary>"clear" (P-17): the species to drive off, and how far round the board (m).</summary>
        public string creature;
        public float radius = 25f;
        public string reward = "Coin";
        public int rewardCount = 5;
    }

    [System.Serializable]
    class QuestFile { public QuestDef[] quests; }

    /// <summary>
    /// Quests, first slice (#27, P-15). The quests are read from StreamingAssets/quests.json, outside the build and
    /// edited by hand: it's read again a second after it changes (or on F10). Each is an objective type and its
    /// params, and text with {item}, {count}, {place} and {town} slots. Two types for now: fetch (bring count of an item
    /// to the board) and find (the host hides a rare item within 40 m of the board; bring it back). Until the animal
    /// lane's folk give quests, a notice board stands near every settlement's centre with 1-2 quests picked by the
    /// settlement's index. Use at the board takes the next one you haven't; Use again with what it asks for hands it
    /// in. Progress is the host's: a client asks (TakeQuestRpc, TurnInRpc), the host records it per player and answers
    /// (QuestStateRpc) and pays the reward on the ground by the board (Items.Spawn). The quest log is a pane of the
    /// inventory screen. Test flags: "-atboard" (stand at the nearest board, 12 s in), "-takequest" (30 s),
    /// "-turnin" (45 s), "-giveitem Stick:3" (test only: into the inventory at start).
    /// </summary>
    public partial class Items
    {
        const string QuestFileName = "quests.json";
        QuestDef[] quests = System.Array.Empty<QuestDef>();
        System.DateTime questStamp;
        float questCheckAt;

        struct Board { public float3 Pos; public string Town; public int[] Offers; }
        readonly List<Board> boards = new List<Board>();
        bool boardsBuilt;
        readonly List<GameObject> boardModels = new List<GameObject>();

        /// <summary>This player's quests: (board, slot) → 1 taken, 2 done.</summary>
        readonly Dictionary<(int, int), int> myQuests = new Dictionary<(int, int), int>();
        /// <summary>The host's record: (connection's NetworkId, board, slot) → state (the host's own player is id 0).</summary>
        readonly Dictionary<(int, int, int), int> questRecord = new Dictionary<(int, int, int), int>();
        int targetBoard = -1;
        bool atBoardArg, takeQuestArg, turnInArg;
        int takeQuestsArg;
        bool handingIn;
        float questTestAt;
        string giveArg;

        static string QuestPath => System.IO.Path.Combine(Application.streamingAssetsPath, QuestFileName);

        void QuestArgs(string[] args)
        {
            atBoardArg = System.Array.IndexOf(args, "-atboard") >= 0;
            takeQuestArg = System.Array.IndexOf(args, "-takequest") >= 0;
            turnInArg = System.Array.IndexOf(args, "-turnin") >= 0;
            var tq = System.Array.IndexOf(args, "-takequests");
            if (tq >= 0 && tq + 1 < args.Length)
                int.TryParse(args[tq + 1], out takeQuestsArg);
            var g = System.Array.IndexOf(args, "-giveitem");
            if (g >= 0 && g + 1 < args.Length)
                giveArg = args[g + 1];
            LoadQuests("start");
        }

        void LoadQuests(string why)
        {
            try
            {
                questStamp = System.IO.File.GetLastWriteTimeUtc(QuestPath);
                var file = JsonUtility.FromJson<QuestFile>(System.IO.File.ReadAllText(QuestPath));
                quests = file?.quests ?? System.Array.Empty<QuestDef>();
                Debug.Log($"Quests: {quests.Length} quests loaded ({why}): " + string.Join(" | ", System.Array.ConvertAll(quests, q => $"{q.id} [{q.type}] \"{q.text}\"")));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Quests: couldn't read {QuestFileName}: {e.Message}");
            }
        }

        static int ItemId(string name) => System.Array.FindIndex(ItemTable.Items, d => string.Equals(d.Name, name, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>A quest's text with its slots filled.</summary>
        string QuestText(int board, int slot)
        {
            var q = quests[boards[board].Offers[slot] % quests.Length];
            var place = $"the notice board in {boards[board].Town}";
            return (q.text ?? "").Replace("{item}", q.item).Replace("{count}", q.count.ToString()).Replace("{town}", boards[board].Town).Replace("{place}", place).Replace("{creature}", q.creature ?? "");
        }

        /// <summary>Called from Update: reloading, the boards, the Use key at a board, the tests.</summary>
        void UpdateQuests(in TerrainSource terrain)
        {
            if (Time.realtimeSinceStartup >= questCheckAt)
            {
                questCheckAt = Time.realtimeSinceStartup + 1f;
                if (System.IO.File.Exists(QuestPath) && System.IO.File.GetLastWriteTimeUtc(QuestPath) != questStamp)
                    LoadQuests("file changed");
            }
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                LoadQuests("F10");
            if (!boardsBuilt)
                BuildBoards(terrain);
            if (quests.Length == 0 || boards.Count == 0 || !TryPlayer(out var player))
                return;
            targetBoard = -1;
            for (int b = 0; b < boards.Count; b++)
                if (math.distance(boards[b].Pos.xz, player.Position.xz) < 2.5f)
                    targetBoard = b;
            if (targetBoard >= 0 && prompt == null)
            {
                var slot = NextSlot(targetBoard, out var turnIn);
                prompt = slot < 0 ? $"Notice board ({boards[targetBoard].Town}): nothing more for you"
                       : turnIn ? $"{UseKey.Name}  hand in: {QuestText(targetBoard, slot)}" : $"{UseKey.Name}  take: {QuestText(targetBoard, slot)}";
                if (slot >= 0 && UseKey.WasPressed() && target < 0 && targetChest < 0 && targetContainer < 0)
                {
                    if (turnIn) TurnIn(targetBoard, slot);
                    else TakeQuest(targetBoard, slot);
                }
            }
            QuestTests(player);
            CheckTests(player);
            UpdateClearing();
        }

        /// <summary>The slot to act on at a board: one taken and ready to hand in, else the first not yet taken (-1: none).</summary>
        int NextSlot(int board, out bool turnIn)
        {
            turnIn = false;
            for (int s = 0; s < boards[board].Offers.Length; s++)
                if (myQuests.TryGetValue((board, s), out var st) && st == 1 && HasFor(board, s))
                {
                    turnIn = true;
                    return s;
                }
            for (int s = 0; s < boards[board].Offers.Length; s++)
                if (!myQuests.ContainsKey((board, s)))
                    return s;
            return -1;
        }

        QuestDef Def(int board, int slot) => quests[boards[board].Offers[slot] % quests.Length];

        bool HasFor(int board, int slot)
        {
            var q = Def(board, slot);
            var id = ItemId(q.item);
            if (id < 0)
                return false;
            var have = 0;
            foreach (var s in slots)
                if (s.item == id) have += s.count;
            return have >= math.max(q.count, 1);
        }

        void BuildBoards(in TerrainSource terrain)
        {
            if (!terrain.Settlements.IsCreated || !terrain.Landmarks.IsCreated)
                return;
            var w = World.DefaultGameObjectInjectionWorld;
            var pq = Q(w.EntityManager, ComponentType.ReadOnly<BiomePalette>());
            if (pq.CalculateEntityCount() != 1)
                return;
            var palette = w.EntityManager.GetComponentObject<BiomePalette>(pq.GetSingletonEntity());
            boardsBuilt = true;
            ref var set = ref terrain.Settlements.Value;
            ref var marks = ref terrain.Landmarks.Value;
            for (int t = 0; t < set.Settlements.Length; t++)
            {
                var town = set.Settlements[t];
                var inst = marks.Instances[town.Landmark];
                var name = palette.Landmarks != null && inst.Type < palette.Landmarks.Length && palette.Landmarks[inst.Type] != null ? palette.Landmarks[inst.Type].name : $"town {t}";
                // (Beside the centre, where the well or fire usually stands; one or two quests, picked by the town's index.)
                var xz = town.Centre + new float2(4f, 1.5f);
                var h = math.hash(new uint2((uint)t, 0x0B0Au));
                var offers = (h & 1) == 0 ? new[] { (int)(h >> 8 & 0xFF) } : new[] { (int)(h >> 8 & 0xFF), (int)(h >> 16 & 0xFF) };
                boards.Add(new Board { Pos = new float3(xz.x, terrain.SampleHeight(xz), xz.y), Town = name, Offers = offers });
                boardModels.Add(MakeBoard(boards[t].Pos));
            }
            Debug.Log($"Quests: {boards.Count} notice boards: " + string.Join(", ", boards.ConvertAll(b => $"{b.Town} ({b.Offers.Length})")));
        }

        static GameObject MakeBoard(float3 pos)
        {
            var root = new GameObject("Notice board") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(root);
            root.transform.position = pos;
            void Part(Vector3 at, Vector3 size, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.hideFlags = HideFlags.HideInHierarchy;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = at;
                go.transform.localScale = size;
                var r = go.GetComponent<Renderer>();
                itemMaterial ??= Resources.Load<Material>("ItemMaterial");
                if (itemMaterial != null) r.sharedMaterial = itemMaterial;
                var b = new MaterialPropertyBlock();
                b.SetColor(BaseColorId, c);
                r.SetPropertyBlock(b);
            }
            Part(new Vector3(-0.6f, 0.9f, 0f), new Vector3(0.1f, 1.8f, 0.1f), new Color(0.4f, 0.28f, 0.16f));
            Part(new Vector3(0.6f, 0.9f, 0f), new Vector3(0.1f, 1.8f, 0.1f), new Color(0.4f, 0.28f, 0.16f));
            Part(new Vector3(0f, 1.35f, 0f), new Vector3(1.4f, 0.8f, 0.06f), new Color(0.55f, 0.4f, 0.24f));
            Part(new Vector3(-0.25f, 1.4f, -0.04f), new Vector3(0.35f, 0.45f, 0.01f), new Color(0.92f, 0.88f, 0.75f));
            Part(new Vector3(0.25f, 1.35f, -0.04f), new Vector3(0.35f, 0.4f, 0.01f), new Color(0.9f, 0.85f, 0.7f));
            return root;
        }

        void TakeQuest(int board, int slot)
        {
            myQuests[(board, slot)] = 1;
            Debug.Log($"Quests: took \"{QuestText(board, slot)}\" (board {board} slot {slot})");
            if (Joined)
                SendToHost(new TakeQuestRpc { Board = board, Slot = (byte)slot });
            else
                HostRecord(0, board, slot, 1, Hosting ? "host" : "solo");
        }

        void TurnIn(int board, int slot)
        {
            var q = Def(board, slot);
            var id = ItemId(q.item);
            var need = math.max(q.count, 1);
            for (int i = 0; i < slots.Length && need > 0; i++)
                if (slots[i].item == id)
                {
                    var n = math.min(need, slots[i].count);
                    slots[i].count -= n;
                    need -= n;
                    if (slots[i].count <= 0) slots[i] = (255, 0);
                }
            Debug.Log($"Quests: handed in {q.count} {q.item} at board {board} ({boards[board].Town}); inventory: {DescribeInventory()}");
            if (Joined)
            {
                handingIn = true;
                SendToHost(new TurnInRpc { Board = board, Slot = (byte)slot });
            }
            else
            {
                myQuests[(board, slot)] = 2;
                HostRecord(0, board, slot, 2, Hosting ? "host" : "solo");
            }
        }

        /// <summary>The host's (or solo's) record of a player's quest; a find quest's item is hidden when it's taken, the reward paid when it's done.</summary>
        void HostRecord(int player, int board, int slot, int state, string who)
        {
            if (board < 0 || board >= boards.Count || slot < 0 || slot >= boards[board].Offers.Length)
                return;
            questRecord.TryGetValue((player, board, slot), out var was);
            if (state <= was)
                return;
            questRecord[(player, board, slot)] = state;
            var q = Def(board, slot);
            Debug.Log($"Quests: player {player} {(state == 1 ? "took" : "finished")} {q.id} at board {board} ({who})");
            if (state == 1 && q.type == "find" && ItemId(q.item) >= 0)
            {
                // (The rare item, somewhere within 40 m of the board: seeded by board, slot and player.)
                var h = math.hash(new uint3((uint)board, (uint)slot, (uint)player + 77u));
                var angle = (h & 0xFFFF) / 65535f * math.PI * 2f;
                var xz = boards[board].Pos.xz + new float2(math.cos(angle), math.sin(angle)) * (15f + (h >> 16 & 0xFF) / 255f * 25f);
                var y = TryTerrain(out var t) ? t.SampleHeight(xz) : boards[board].Pos.y;
                Spawn((byte)ItemId(q.item), 1, new float3(xz.x, y, xz.y));
                Debug.Log($"Quests: hid the {q.item} for {q.id} at ({xz.x:F0}, {xz.y:F0})");
            }
            if (state == 2)
            {
                var reward = ItemId(q.reward);
                if (reward >= 0)
                {
                    Spawn((byte)reward, math.max(q.rewardCount, 1), boards[board].Pos + new float3(0f, 0f, -1.2f));
                    Debug.Log($"Quests: reward for {q.id}: {q.reward} x{q.rewardCount} by the board");
                }
            }
        }

        /// <summary>The host's side (from Serve): quests taken and handed in by clients.</summary>
        void ServeQuests(EntityManager em)
        {
            using (var rs = Q(em, ComponentType.ReadWrite<TakeQuestRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<TakeQuestRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    var id = em.Exists(from) ? em.GetComponentData<NetworkId>(from).Value : -1;
                    HostRecord(id, m.Board, m.Slot, 1, "host, for a client");
                    if (em.Exists(from)) Send(em, from, new QuestStateRpc { Board = m.Board, Slot = m.Slot, State = 1 });
                    em.DestroyEntity(r);
                }
            using (var rs = Q(em, ComponentType.ReadWrite<TurnInRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<TurnInRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    var id = em.Exists(from) ? em.GetComponentData<NetworkId>(from).Value : -1;
                    // (P-17: only if the host saw this player carry what the quest asks for.)
                    var ok = m.Board >= 0 && m.Board < boards.Count && m.Slot < boards[m.Board].Offers.Length && CheckHandIn(id, m.Board, m.Slot);
                    if (ok)
                        HostRecord(id, m.Board, m.Slot, 2, "host, for a client");
                    if (em.Exists(from)) Send(em, from, new QuestStateRpc { Board = m.Board, Slot = m.Slot, State = (byte)(ok ? 2 : 1) });
                    em.DestroyEntity(r);
                }
        }

        void ReceiveQuestState(QuestStateRpc m)
        {
            var was = myQuests.TryGetValue((m.Board, m.Slot), out var w) ? w : 0;
            myQuests[(m.Board, m.Slot)] = m.State;
            Debug.Log($"Quests: board {m.Board} slot {m.Slot} {(m.State == 2 ? "done" : was == 1 && handingIn ? "hand-in refused by the host" : "taken")} (from the host)");
            handingIn = false;
        }

        void QuestTests(LocalTransform player)
        {
            if (takeQuestsArg > 0 && Time.realtimeSinceStartup > 25f)
                TakeQuestsTest(player);
            if (!atBoardArg && !takeQuestArg && !turnInArg && giveArg == null)
                return;
            if (giveArg != null)
            {
                var parts = giveArg.Split(':');
                var id = ItemId(parts[0]);
                if (id >= 0)
                    Grant((byte)id, parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 1);
                giveArg = null;
            }
            if (Joined)
            {
                var q = Q(client.EntityManager, ComponentType.ReadWrite<NetworkStreamInGame>());
                if (q.IsEmpty)
                    return;
            }
            questTestAt = questTestAt == 0f ? Time.realtimeSinceStartup : questTestAt;
            var age = Time.realtimeSinceStartup - questTestAt;
            var nearest = 0;
            for (int b = 1; b < boards.Count; b++)
                if (math.distance(boards[b].Pos, player.Position) < math.distance(boards[nearest].Pos, player.Position)) nearest = b;
            if (atBoardArg && age > 12f)
            {
                atBoardArg = false;
                var e = World.DefaultGameObjectInjectionWorld.EntityManager;
                var pq = Q(e, ComponentType.ReadWrite<Punchy.Player.PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
                if (pq.TryGetSingletonEntity<LocalTransform>(out var pe))
                {
                    var t = e.GetComponentData<LocalTransform>(pe);
                    t.Position = boards[nearest].Pos + new float3(0f, 0.05f, -1.6f);
                    t.Rotation = quaternion.identity;
                    e.SetComponentData(pe, t);
                    Debug.Log($"Quests: -atboard: board {nearest} ({boards[nearest].Town})");
                }
            }
            if (takeQuestArg && age > 30f)
            {
                takeQuestArg = false;
                var slot = NextSlot(nearest, out _);
                Debug.Log($"Quests: -takequest: board {nearest} slot {slot}");
                if (slot >= 0) TakeQuest(nearest, slot);
            }
            if (turnInArg && age > 45f)
            {
                turnInArg = false;
                var slot = NextSlot(nearest, out var ready);
                if (fakeHandInArg && !ready)
                {
                    // (Test: hand in a taken quest without the items, to see the host refuse it.)
                    for (int s = 0; s < boards[nearest].Offers.Length; s++)
                        if (myQuests.TryGetValue((nearest, s), out var st) && st == 1)
                            (slot, ready) = (s, true);
                    Debug.Log($"Quests: -fakehandin: board {nearest} slot {slot}, without the items");
                }
                Debug.Log($"Quests: -turnin: board {nearest} slot {slot} ({(ready ? "has the items" : "doesn't have the items")})");
                if (slot >= 0 && ready) TurnIn(nearest, slot);
            }
        }

        Vector2 questScroll;
        GUIStyle questLineStyle;

        /// <summary>
        /// The quest log, under the inventory pane (P-16): each quest's row as tall as its wrapped text, and the list
        /// scrolls once it would run past the bottom of the screen.
        /// </summary>
        void DrawQuestLog(Rect inventory)
        {
            questLineStyle ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13 };
            var lines = new List<string>();
            foreach (var kv in myQuests)
                if (kv.Key.Item1 < boards.Count && quests.Length > 0)
                    lines.Add($"{(kv.Value == 2 ? "<b>[done]</b> " : "\u2022 ")}{QuestText(kv.Key.Item1, kv.Key.Item2)}");
            if (lines.Count == 0)
                lines.Add("No quests yet: notice boards stand in every town.");
            var inner = inventory.width - 16f;
            var heights = lines.ConvertAll(l => questLineStyle.CalcHeight(new GUIContent(l), inner - 16f) + 4f);
            var total = 0f;
            foreach (var h in heights) total += h;
            // (On the left of the screen, up to three quarters of its height: under the inventory there was no room.)
            var top = Screen.height * 0.12f;
            var room = Screen.height * 0.75f;
            var boxHeight = Mathf.Min(total + 30f, room);
            var area = new Rect(20f, top, inventory.width, boxHeight);
            GUI.Box(area, "");
            GUI.Label(new Rect(area.x + 8f, area.y + 4f, inner, 22f), "<b>Quests</b>", questLineStyle);
            var view = new Rect(area.x + 8f, area.y + 26f, inner, boxHeight - 30f);
            questScroll = GUI.BeginScrollView(view, questScroll, new Rect(0f, 0f, inner - 16f, total));
            var y = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(0f, y, inner - 16f, heights[i]), lines[i], questLineStyle);
                y += heights[i];
            }
            GUI.EndScrollView();
        }

        /// <summary>"-takequests N" (tests, P-16): takes the next quests from the nearest boards until N are taken, any distance.</summary>
        void TakeQuestsTest(LocalTransform player)
        {
            if (takeQuestsArg <= 0 || boards.Count == 0 || quests.Length == 0)
                return;
            var order = new List<int>();
            for (int b = 0; b < boards.Count; b++) order.Add(b);
            order.Sort((x, y) => math.distance(boards[x].Pos, player.Position).CompareTo(math.distance(boards[y].Pos, player.Position)));
            foreach (var b in order)
                for (int slot = 0; slot < boards[b].Offers.Length && takeQuestsArg > 0; slot++)
                    if (!myQuests.ContainsKey((b, slot)))
                    {
                        TakeQuest(b, slot);
                        takeQuestsArg--;
                    }
            takeQuestsArg = 0;
        }
    }
}
