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
    /// - <c>all</c>: every spell the bridge casts, in turn, 1.5 s apart, round the player (ChemistryBridge.Spells.cs);
    /// - <c>big</c>: the big scene: grease and water over a meadow 20 m north, a fog, then a Meteor into it;
    /// - <c>measure s</c>: logs the frame times over the next s seconds;
    /// - <c>castat spell dx,dz[,charge]</c>: a spell from the player at a point that far away;
    /// - <c>face dx,dz</c>: turns the camera that way;
    /// - <c>gowater</c>: onto the shore of the nearest water, facing it; <c>castwater spell [charge]</c>: a spell on it;
    /// - <c>rec s</c>: s seconds of frames at 30 a second of game time, as JPEGs (for a demo clip);
    /// - <c>shot name</c>: a screenshot (into -chem-shots' folder);
    /// - <c>quit</c>: ends the process.
    /// Only the local player moves (Punchy's movement is client-authoritative), so on a client it's the client's own.
    /// </summary>
    public partial class ChemistryBridge
    {
        readonly List<(float at, string what)> steps = new List<(float, string)>();
        int nextStep;
        float scriptStart = -1f;
        // The script's clock: real time, but game time while recording (Time.captureFramerate), so a clip's steps keep their spacing.
        float scriptClock;
        // A walk in progress: from, by, how long each way, since when.
        float3 walkFrom;
        float2 walkBy;
        float walkFor, walkSince = -1f;
        float3? leftFrom;
        Vector3? waterAim;

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
            else
                scriptClock += Time.captureFramerate > 0 ? Time.deltaTime : Time.unscaledDeltaTime;
            Walk(terrain);
            if (nextStep >= steps.Count || scriptClock < steps[nextStep].at)
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
                        Teleporting(new float3(xz[0], terrain.SampleHeight(new float2(xz[0], xz[1])), xz[1]));
                        Put(terrain, new float3(xz[0], 0f, xz[1]));
                    }
                    break;
                case "hop":
                    var by = Floats(words.Length > 1 ? words[1] : "");
                    if (by.Length >= 2)
                    {
                        leftFrom = player;
                        var to = player.xz + new float2(by[0], by[1]);
                        Teleporting(new float3(to.x, terrain.SampleHeight(to), to.y));
                        Put(terrain, player + new float3(by[0], 0f, by[1]));
                    }
                    break;
                case "back":
                    if (leftFrom is float3 was)
                    {
                        Teleporting(was);
                        Put(terrain, was, false);
                    }
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
                case "all":
                    done = QueueAll(player);
                    break;
                case "big":
                    done = QueueBig(player);
                    break;
                case "measure":
                    var secs = Floats(words.Length > 1 ? words[1] : "");
                    StartMeasure(secs.Length > 0 ? secs[0] : 10f);
                    break;
                case "castat":
                    // castat spell dx,dz[,charge]: a spell from the player at a point that far away.
                    var off = Floats(words.Length > 2 ? words[2] : "");
                    done = Ready && words.Length > 2 && off.Length >= 2
                        && CastFrom(words[1], player, player + new float3(off[0], 0f, off[1]), off.Length > 2 ? (int)off[2] : 1);
                    break;
                case "face":
                    // face dx,dz: turns the camera that way (for the clip's shots).
                    var f = Floats(words.Length > 1 ? words[1] : "");
                    if (f.Length >= 2)
                        Face(new float2(f[0], f[1]));
                    break;
                case "gowater":
                    // gowater: onto the shore 10 m south of the nearest water, facing it.
                    var wi = Ready ? Nearest(player, KindWater) : -1;
                    done = wi >= 0;
                    if (done)
                    {
                        var wc = CellCentre(wi % Size, wi / Size);
                        Put(terrain, new float3(wc.x, 0f, wc.z - 10f));
                        Face(new float2(0f, 1f));
                        waterAim = wc;
                    }
                    break;
                case "castwater":
                    // castwater spell [charge]: that spell on the water gowater found, from where the player stands.
                    done = Ready && waterAim.HasValue && words.Length > 1;
                    if (done)
                        CastFrom(words[1], player, waterAim.Value, words.Length > 2 && int.TryParse(words[2], out var wch) ? wch : 1);
                    break;
                case "rec":
                    // rec s: s seconds of frames, 30 a second of game time (Time.captureFramerate), as JPEGs into -chem-shots'
                    // folder: the game's own image, whatever windows are over it (for a demo clip).
                    var recFor = Floats(words.Length > 1 ? words[1] : "");
                    StartCoroutine(Record(recFor.Length > 0 ? recFor[0] : 10f));
                    break;
                case "shot":
                    // shot name: a screenshot into -chem-shots' folder (or the player's data folder).
                    var dir = ShotDir();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, (words.Length > 1 ? words[1] : "shot") + ".png"));
                    break;
                case "quit":
                    Debug.Log("[Chemistry] script: quit");
                    Application.Quit();
                    break;
            }
            if (!done)
                return;
            Debug.Log($"[Chemistry] script: {what} (at {scriptClock:F1} s, from ({player.x:F0}, {player.y:F0}, {player.z:F0}) in layer {viewLayer}; {FilledChunks} chunks filled)");
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

        System.Collections.IEnumerator Record(float seconds)
        {
            const int Fps = 30;
            var dir = ShotDir();
            Time.captureFramerate = Fps;
            var frames = (int)(seconds * Fps);
            for (int k = 0; k < frames; k++)
            {
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"rec{k:0000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
            }
            Time.captureFramerate = 0;
            Debug.Log($"[Chemistry] script: recorded {frames} frames");
        }

        static string ShotDir()
        {
            var args = System.Environment.GetCommandLineArgs();
            int k = System.Array.IndexOf(args, "-chem-shots");
            return k >= 0 && k + 1 < args.Length ? args[k + 1] : Application.persistentDataPath;
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
