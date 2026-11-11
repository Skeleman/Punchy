using System.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// P-31 test hook: "-rejoin &lt;seconds&gt;[,&lt;times&gt;]" on a joiner started with "-join &lt;ip&gt;": once in the game,
    /// waits that long, leaves (as the menu's Play alone does), waits a few seconds and joins the same host again (as
    /// the menu's Join does), the given number of times (1 by default). Logs each step and how long each rejoin took
    /// to get back into the game. Does nothing without the flag.
    /// </summary>
    public class NetRejoinTest : MonoBehaviour
    {
        const float Apart = 5f, GiveUp = 60f;
        float wait;
        int times;
        NetworkEndpoint host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            int at = System.Array.IndexOf(args, "-rejoin"), join = System.Array.IndexOf(args, "-join");
            if (at < 0 || at + 1 >= args.Length || join < 0 || join + 1 >= args.Length)
                return;
            var parts = args[at + 1].Split(',');
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wait)
                || !NetworkEndpoint.TryParse(args[join + 1], GameBootstrap.Port, out var host))
            {
                Debug.LogWarning("rejoin: needs -rejoin <seconds>[,<times>] and -join <ip>");
                return;
            }
            var go = new GameObject("NetRejoinTest");
            DontDestroyOnLoad(go);
            var test = go.AddComponent<NetRejoinTest>();
            test.wait = wait;
            test.times = parts.Length > 1 && int.TryParse(parts[1], out var n) ? Mathf.Max(1, n) : 1;
            test.host = host;
        }

        static bool InGame()
        {
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated || !client.IsClient())
                return false;
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamInGame>());
            return q.CalculateEntityCount() > 0;
        }

        IEnumerator Start()
        {
            for (int i = 1; i <= times; i++)
            {
                while (!InGame())
                    yield return null;
                yield return new WaitForSecondsRealtime(wait);
                Debug.Log($"rejoin: {i} of {times}: leaving");
                GameBootstrap.PlaySolo();
                yield return new WaitForSecondsRealtime(Apart);
                Debug.Log($"rejoin: {i} of {times}: joining {host}");
                var start = Time.realtimeSinceStartup;
                GameBootstrap.Join(host);
                while (!InGame() && Time.realtimeSinceStartup < start + GiveUp)
                    yield return null;
                var took = Time.realtimeSinceStartup - start;
                Debug.Log(InGame() ? $"rejoin: {i} of {times}: back in game after {took:0.0} s" : $"rejoin: {i} of {times}: not back after {GiveUp:0} s");
            }
        }
    }
}
