using System.Net;
using System.Net.Sockets;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Networking.Transport;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// F2: a small multiplayer panel. Shows whether this machine is hosting (and the LAN address friends join), who
    /// is connected, and lets you join another host by IP or go back to hosting.
    /// </summary>
    public class NetMenu : MonoBehaviour
    {
        public KeyCode Toggle = KeyCode.F2;
        public NetAvatars Avatars;

        bool open;

        /// <summary>The panel is showing: the camera leaves the mouse free so its buttons can be clicked.</summary>
        public static bool IsOpen { get; private set; }
        string address = "192.168.1.";
        string code = "";
        string lanAddress;
        GUIStyle box;

        void Start()
        {
            // "-joincode ABC123" on the command line joins that online game straight away; "-hostonline" hosts one.
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-joincode" && i + 1 < args.Length && RelaySessions.JoinCode == null && !joinedFromCommandLine)
                {
                    joinedFromCommandLine = true;
                    RelaySessions.Join(args[i + 1]);
                }
                if (args[i] == "-hostonline" && !joinedFromCommandLine)
                {
                    joinedFromCommandLine = true;
                    RelaySessions.Host();
                }
            }
        }

        // Survives the scene reload a join does, so it only happens once.
        static bool joinedFromCommandLine;

        /// <summary>A note shown on screen for a few seconds (the host left, and so on).</summary>
        static string notice;
        static float noticeUntil;
        float placementDeadline = -1f;
        bool wasInGame;
        float lostAt = -1f;

        // A status line in the player log every so often, so a test can be read from the log alone.
        float nextStatus;
        Punchy.Weather.DayNightCycle cycle;

        void LogStatus()
        {
            if (Time.unscaledTime < nextStatus)
                return;
            nextStatus = Time.unscaledTime + 10f;
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated || !client.IsClient())
                return;
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var time = cycle != null ? $"{(int)cycle.TimeOfDay:00}:{(int)((cycle.TimeOfDay % 1f) * 60f):00}" : "?";
            var role = NetWorlds.Server != null ? "host" : "joined";
            Debug.Log($"Net status ({role}): in game {Count<NetworkStreamInGame>(client) > 0}, players {Count<NetPlayer>(client)}, " +
                      $"animals {Count<Punchy.Net.CreatureReplica>(client)} ({AnimalsWithBodies(client, out var nearest)} with bodies, nearest {nearest:F0} m), " +
                      $"time {time}, {1f / Time.smoothDeltaTime:F0} fps, {Whereabouts(client)}");
        }

        /// <summary>Where we are, and how far the nearest other player is (for reading tests from the log).</summary>
        static string Whereabouts(World world)
        {
            var em = world.EntityManager;
            using var players = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (!players.TryGetSingleton<LocalTransform>(out var me))
                return "no player";
            using var ghosts = em.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
            var entities = ghosts.ToEntityArray(Unity.Collections.Allocator.Temp);
            var nearest = float.PositiveInfinity;
            foreach (var e in entities)
            {
                if (em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e))
                    continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                if (math.lengthsq(p) > 1e-6f)
                    nearest = math.min(nearest, math.distance(p, me.Position));
            }
            entities.Dispose();
            var ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            return $"at ({me.Position.x:F0}, {me.Position.y:F0}, {me.Position.z:F0})" +
                   (float.IsPositiveInfinity(nearest) ? "" : $", nearest player {nearest:F0} m") +
                   (ambience != null ? $", cold {ambience.Cold:F2}, others nearby {ambience.OthersNearby} ({ambience.OthersBreathing} breathing)" : "");
        }

        /// <summary>Replicated animals that have been given a body to draw, and how far the nearest one is from us.</summary>
        static int AnimalsWithBodies(World world, out float nearest)
        {
            nearest = float.PositiveInfinity;
            var em = world.EntityManager;
            using var players = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var bodies = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureReplica>(), ComponentType.ReadOnly<Punchy.Fauna.CreatureBody>(),
                ComponentType.ReadOnly<Punchy.Fauna.CreatureState>());
            if (players.TryGetSingleton<LocalTransform>(out var player))
            {
                var states = bodies.ToComponentDataArray<Punchy.Fauna.CreatureState>(Unity.Collections.Allocator.Temp);
                foreach (var c in states)
                    nearest = math.min(nearest, math.distance(c.Position, player.Position));
                states.Dispose();
            }
            return bodies.CalculateEntityCount();
        }

        void Update()
        {
            LogStatus();
            PlaceLocalPlayer();
            WatchForHostLeaving();
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f2Key.wasPressedThisFrame)
                open = !open;
            IsOpen = open;
        }

        void OnGUI()
        {
            if (notice != null && Time.unscaledTime < noticeUntil)
            {
                box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) };
                GUI.Box(new Rect(Screen.width * 0.5f - 200, 60, 400, 30), notice, box);
            }
            if (!open)
                return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) };
            lanAddress ??= LanAddress();

            var server = NetWorlds.Server;
            var client = NetWorlds.Client;
            var players = server != null ? Count<NetworkId>(server) : -1;
            var connected = client != null && Count<NetworkId>(client) > 0;

            GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 360), box);
            GUILayout.Label("<b>Multiplayer</b>  (F2)", new GUIStyle(GUI.skin.label) { richText = true });
            if (server != null && RelaySessions.JoinCode != null)
            {
                GUILayout.Label($"Hosting online — {players} player(s) connected");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>Code: {RelaySessions.JoinCode}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                    GUIUtility.systemCopyBuffer = RelaySessions.JoinCode;
                GUILayout.EndHorizontal();
            }
            else if (server != null)
                GUILayout.Label($"Hosting — {players} player(s) connected. Others on your network join this PC at <b>{lanAddress}</b>",
                    new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true });
            else
            {
                // A refused or lost connection disappears; a join in progress still has its connection entity.
                var connecting = client != null && Count<NetworkStreamConnection>(client) > 0;
                GUILayout.Label(connected ? "Connected to a host" : connecting ? $"Connecting to {GameBootstrap.JoiningTo}…"
                    : "Not connected: the host is unreachable, or refused us (a different world or build). Join again, or host.",
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
            }

            GUILayout.Space(6);
            GUILayout.Label("Join a host by IP (the address its F2 panel shows):");
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(200));
            if (GUILayout.Button("Join"))
            {
                if (address.Trim() == lanAddress)
                    Notify("That's this PC's own address. Type the address shown on the host's F2 panel.", 6f);
                else if (!NetworkEndpoint.TryParse(address.Trim(), GameBootstrap.Port, out var endpoint))
                    Notify($"\"{address.Trim()}\" isn't an IP address (it looks like 192.168.1.20).", 6f);
                else
                {
                    RelaySessions.Forget();
                    GameBootstrap.Join(endpoint);
                }
            }
            GUILayout.EndHorizontal();
            if (server == null && GUILayout.Button("Host my own game instead"))
            {
                RelaySessions.Forget();
                GameBootstrap.Host();
            }

            // Online, through Unity Relay: friends anywhere join with a code.
            GUILayout.Space(6);
            GUI.enabled = !RelaySessions.Busy;
            if (RelaySessions.JoinCode == null && GUILayout.Button("Host online (get a join code)"))
                RelaySessions.Host();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Code:", GUILayout.Width(40));
            code = GUILayout.TextField(code, 8, GUILayout.Width(100));
            if (GUILayout.Button("Join online"))
                RelaySessions.Join(code);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (RelaySessions.Status != null)
                GUILayout.Label(RelaySessions.Status, new GUIStyle(GUI.skin.label) { wordWrap = true });

            // Which body we have (everyone sees it).
            if (Avatars != null && Avatars.PlayableSpecies.Count > 0)
            {
                GUILayout.Space(6);
                var index = Avatars.LocalIndex;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Body:", GUILayout.Width(40));
                if (GUILayout.Button("<", GUILayout.Width(24)))
                    Avatars.SetLocalSpecies((index - 1 + Avatars.PlayableSpecies.Count) % Avatars.PlayableSpecies.Count);
                GUILayout.Label(index >= 0 ? Avatars.PlayableSpecies[index].name : "(custom)", GUILayout.Width(150));
                if (GUILayout.Button(">", GUILayout.Width(24)))
                    Avatars.SetLocalSpecies((index + 1) % Avatars.PlayableSpecies.Count);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// After a world rebuild: put the player back where they were (hosting again), or next to the host (having
        /// joined), once the scene and the host's player have arrived.
        /// </summary>
        void PlaceLocalPlayer()
        {
            var placement = GameBootstrap.PendingPlacement;
            if (placement == GameBootstrap.Placement.None)
                return;
            if (placementDeadline < 0f)
                placementDeadline = Time.unscaledTime + 15f;
            if (Time.unscaledTime > placementDeadline)
            {
                Debug.Log($"NetMenu: gave up placing the player ({placement}): no host position arrived");
                GameBootstrap.PendingPlacement = GameBootstrap.Placement.None;
                placementDeadline = -1f;
                return;
            }
            var world = NetWorlds.Client;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            using var players = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            using var terrain = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!players.TryGetSingletonEntity<PlayerTag>(out var player) || !terrain.TryGetSingleton<TerrainSource>(out var source))
                return;
            var target = GameBootstrap.CarriedPosition.xz;
            if (placement == GameBootstrap.Placement.NearHost)
            {
                // The host's NetPlayer is the first one that isn't ours and has a real position.
                using var ghosts = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                });
                var found = false;
                var list = ghosts.ToEntityArray(Unity.Collections.Allocator.Temp);
                foreach (var e in list)
                {
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    var mine = em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e);
                    if (!mine && math.lengthsq(p) > 1e-6f)
                    {
                        target = p.xz + new float2(2.5f, 1.5f);
                        found = true;
                        break;
                    }
                }
                list.Dispose();
                if (!found)
                    return;
            }
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(target.x, source.SampleHeight(target) + 0.2f, target.y);
            em.SetComponentData(player, t);
            Debug.Log($"NetMenu: placed the player ({placement}) at ({t.Position.x:F0}, {t.Position.y:F0}, {t.Position.z:F0})");
            GameBootstrap.PendingPlacement = GameBootstrap.Placement.None;
            placementDeadline = -1f;
        }

        /// <summary>A joined player whose host goes away carries on alone, hosting their own game where they stand.</summary>
        void WatchForHostLeaving()
        {
            var client = NetWorlds.Client;
            if (NetWorlds.Server != null || client == null || !client.IsCreated || !client.IsClient())
            {
                wasInGame = false;
                lostAt = -1f;
                return;
            }
            if (Count<NetworkStreamInGame>(client) > 0)
            {
                wasInGame = true;
                GameBootstrap.JoiningSince = -1f;
                lostAt = -1f;
                return;
            }
            // A join that never gets through (wrong address, host not running, blocked) gives up rather than
            // leaving us in an empty world that looks like it worked.
            // (Only until the host answers: after that, loading the world can take a while on a slow PC.)
            if (GameBootstrap.JoiningSince >= 0f && Count<NetworkId>(client) > 0)
                GameBootstrap.JoiningSince = -1f;
            if (!wasInGame && GameBootstrap.JoiningSince >= 0f && Time.unscaledTime - GameBootstrap.JoiningSince > JoinTimeout)
            {
                Notify($"Couldn't reach {GameBootstrap.JoiningTo}. Check the address: the host's F2 panel shows it.", 8f);
                GameBootstrap.JoiningSince = -1f;
                RelaySessions.Forget();
                GameBootstrap.Host();
                return;
            }
            if (!wasInGame)
                return;
            if (lostAt < 0f)
            {
                lostAt = Time.unscaledTime;
                Notify("Lost the connection to the host. Carrying on alone...");
            }
            else if (Time.unscaledTime - lostAt > 2f)
            {
                wasInGame = false;
                lostAt = -1f;
                RelaySessions.Forget();
                GameBootstrap.Host();
            }
        }

        const float JoinTimeout = 15f;

        public static void Notify(string text, float seconds = 5f)
        {
            notice = text;
            Debug.Log($"NetMenu: {text}");
            noticeUntil = Time.unscaledTime + seconds;
        }

        static int Count<T>(World world) where T : unmanaged, IComponentData
        {
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.CalculateEntityCount();
        }

        static string LanAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);   // picks the outgoing interface; nothing is sent
                return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
            }
            catch
            {
                return "?";
            }
        }
    }
}
