using System.Collections.Generic;
using System.Globalization;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Punchy.Chemistry
{
    /// <summary>
    /// A test script for runs without keys (the open world's checks: far apart, a teleport, a walk away and back):
    /// <c>-chem-script "8:demo; 40:goto 1500,-300; 50:demo; 90:walk 300,0,30; 160:quit"</c>. Each step is a time in seconds
    /// from when this peer's session started, then what to do; a step that needs the view waits for it (and the steps
    /// after it wait too):
    /// - <c>demo</c>: a Fireball on the nearest grass 6 m or more away (as -chem-demo);
    /// - <c>lake</c>: a Sleet Storm on the nearest water 6 m or more away;
    /// - <c>cast spell</c>: that spell 5 m north of the player, in its layer;
    /// - <c>goto x,z</c>: a teleport there, onto the ground; <c>hop dx,dz</c>: a teleport that far from here; <c>back</c>:
    ///   to where the last goto or hop left from, exactly (into a cave again);
    /// - <c>walk dx,dz,s</c>: walks that far in s seconds (on the ground, the player carried), then back the same way;
    /// - <c>quit</c>: ends the process.
    /// Only the local player moves (Punchy's movement is client-authoritative), so on a client it's the client's own.
    /// </summary>
    public partial class ChemistryBridge
    {
        readonly List<(float at, string what)> steps = new List<(float, string)>();
        int nextStep;
        float scriptStart = -1f;
        // A walk in progress: from, by, how long each way, since when.
        float3 walkFrom;
        float2 walkBy;
        float walkFor, walkSince = -1f;
        float3? leftFrom;

        void StartScript()
        {
            var args = System.Environment.GetCommandLineArgs();
            int k = System.Array.IndexOf(args, "-chem-script");
            if (k < 0 || k + 1 >= args.Length)
                return;
            foreach (var part in args[k + 1].Split(';'))
            {
                var p = part.Trim();
                int colon = p.IndexOf(':');
                if (colon > 0 && float.TryParse(p.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                    steps.Add((t, p.Substring(colon + 1).Trim()));
            }
            Debug.Log($"[Chemistry] script: {steps.Count} steps");
        }

        void Script(TerrainSource terrain, float3 player)
        {
            if (steps.Count == 0)
                return;
            if (scriptStart < 0f)
                scriptStart = Time.unscaledTime;
            Walk(terrain);
            if (nextStep >= steps.Count || Time.unscaledTime - scriptStart < steps[nextStep].at)
                return;
            var what = steps[nextStep].what;
            var words = what.Split(' ');
            bool done = true;
            switch (words[0])
            {
                case "demo":
                    done = Ready && CastOnNearest("fireball", KindGrass, player);
                    break;
                case "lake":
                    done = Ready && CastOnNearest("sleet-storm", KindWater, player);
                    break;
                case "cast":
                    done = Ready && words.Length > 1 && world.At((int)math.floor(player.x), Chem.ChunkKey.Row(viewLayer, (int)math.floor(player.z) + 5)) != null;
                    if (done)
                        Cast(words[1], player + new float3(0f, 0f, 5f), Vector3.forward);
                    break;
                case "goto":
                    var xz = Floats(words.Length > 1 ? words[1] : "");
                    if (xz.Length >= 2)
                    {
                        leftFrom = player;
                        Put(terrain, new float3(xz[0], 0f, xz[1]));
                    }
                    break;
                case "hop":
                    var by = Floats(words.Length > 1 ? words[1] : "");
                    if (by.Length >= 2)
                    {
                        leftFrom = player;
                        Put(terrain, player + new float3(by[0], 0f, by[1]));
                    }
                    break;
                case "back":
                    if (leftFrom is float3 was)
                        Put(terrain, was, false);
                    break;
                case "walk":
                    var w = Floats(words.Length > 1 ? words[1] : "");
                    if (w.Length >= 3)
                    {
                        walkFrom = player;
                        walkBy = new float2(w[0], w[1]);
                        walkFor = math.max(1f, w[2]);
                        walkSince = Time.unscaledTime;
                    }
                    break;
                case "quit":
                    Debug.Log("[Chemistry] script: quit");
                    Application.Quit();
                    break;
            }
            if (!done)
                return;
            Debug.Log($"[Chemistry] script: {what} (at {Time.unscaledTime - scriptStart:F1} s, from ({player.x:F0}, {player.y:F0}, {player.z:F0}) in layer {viewLayer}; {FilledChunks} chunks filled)");
            nextStep++;
        }

        void Walk(TerrainSource terrain)
        {
            if (walkSince < 0f)
                return;
            var t = (Time.unscaledTime - walkSince) / walkFor;
            if (t >= 2f)
            {
                walkSince = -1f;
                t = 2f;
                Debug.Log($"[Chemistry] script: back from the walk; {FilledChunks} chunks filled");
            }
            var f = t <= 1f ? t : 2f - t;
            Put(terrain, walkFrom + new float3(walkBy.x * f, 0f, walkBy.y * f));
        }

        // The local player onto the ground at a point (or at the point itself).
        void Put(TerrainSource terrain, float3 at, bool onGround = true)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var tr = em.GetComponentData<LocalTransform>(player);
            tr.Position = onGround ? new float3(at.x, terrain.SampleHeight(at.xz) + 0.05f, at.z) : at;
            em.SetComponentData(player, tr);
            if (em.HasComponent<PlayerMotion>(player))
            {
                var m = em.GetComponentData<PlayerMotion>(player);
                m.VerticalVelocity = 0f;
                em.SetComponentData(player, m);
            }
        }

        static float[] Floats(string text)
        {
            var parts = text.Split(',');
            var list = new List<float>();
            foreach (var p in parts)
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            return list.ToArray();
        }
    }
}
