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
        string lanAddress;
        GUIStyle box;

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

            GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 250), box);
            GUILayout.Label("<b>Multiplayer</b>  (F2)", new GUIStyle(GUI.skin.label) { richText = true });
            if (server != null)
                GUILayout.Label($"Hosting on {lanAddress}:{GameBootstrap.Port} — {players} player(s) connected");
            else
                GUILayout.Label(connected ? "Connected to a host" : "Connecting…");

            GUILayout.Space(6);
            GUILayout.Label("Join a host by IP:");
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(200));
            if (GUILayout.Button("Join") && NetworkEndpoint.TryParse(address.Trim(), GameBootstrap.Port, out var endpoint))
                GameBootstrap.Join(endpoint);
            GUILayout.EndHorizontal();
            if (server == null && GUILayout.Button("Host my own game instead"))
                GameBootstrap.Host();

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
