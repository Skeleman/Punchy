using System.Net;
using System.Net.Sockets;
using Unity.Entities;
using Unity.NetCode;
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

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f2Key.wasPressedThisFrame)
                open = !open;
        }

        void OnGUI()
        {
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
                GUILayout.Label($"Hosting on {lanAddress}:{GameBootstrap.Port} — {players} player(s) connected");
            else
            {
                // A refused or lost connection disappears; a join in progress still has its connection entity.
                var connecting = client != null && Count<NetworkStreamConnection>(client) > 0;
                GUILayout.Label(connected ? "Connected to a host" : connecting ? "Connecting…"
                    : "Not connected: the host is unreachable, or refused us (a different world or build). Join again, or host.",
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
            }

            GUILayout.Space(6);
            GUILayout.Label("Join a host by IP:");
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(200));
            if (GUILayout.Button("Join") && NetworkEndpoint.TryParse(address.Trim(), GameBootstrap.Port, out var endpoint))
            {
                RelaySessions.Forget();
                GameBootstrap.Join(endpoint);
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
