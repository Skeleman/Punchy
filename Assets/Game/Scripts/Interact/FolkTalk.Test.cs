using System.Collections;
using System.Collections.Generic;
using System.IO;
using Punchy.Fauna;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Punchy.Interact
{
    /// <summary>
    /// "-talktest N [offers]" (P-26): stands the player in front of N folk in turn and presses Use, logging how long the
    /// window took and what the folk did, moving on to further doors when the folk nearby run out. With "offers": asks
    /// until a quest and a gift have each been offered, accepts both, then meets that folk again. With "pad": a virtual
    /// pad opens a talk, moves down the list, picks, tries to look and walk, and ends it two ways. With "replies" (P-42):
    /// walks a made-up reply branch (ReplyBook) through the window: a gesture, an item offer taken by a reply, the depth
    /// cap, and a goodbye. "-talkshot dir" saves the whole screen, the window included, at each step.
    /// </summary>
    public partial class FolkTalk
    {
        int testCount;
        bool testOffers, testPad, testReplies;
        int nextDoor;
        Gamepad pad;
        GamepadButton? tapNext;
        bool tapHeld, stickOn, stickRelease;
        Vector2 stickLeft, stickRight;
        string shotDir;
        int shotIndex;

        void TestArgs()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-talkshot");
            if (at >= 0 && at + 1 < args.Length)
                shotDir = args[at + 1];
            at = System.Array.IndexOf(args, "-talktest");
            if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out testCount))
                return;
            testOffers = at + 2 < args.Length && args[at + 2] == "offers";
            testPad = at + 2 < args.Length && args[at + 2] == "pad";
            testReplies = at + 2 < args.Length && args[at + 2] == "replies";
            StartCoroutine(RunTest());
        }

        IEnumerator Shot(string name)
        {
            if (shotDir == null)
                yield break;
            // (After a few frames, so the window has been drawn.)
            yield return new WaitForSecondsRealtime(0.4f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(shotDir, $"talk-{shotIndex++:00}-{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[talk] shot {path}");
            yield return null;
        }

        /// <summary>The nearest folk that can talk and hasn't been tried, or Entity.Null.</summary>
        Entity NextFolk(HashSet<uint> tried, out CreatureState state)
        {
            state = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (!TryPlayer(out var player))
                return Entity.Null;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var best = Entity.Null;
            var bestDistance = float.MaxValue;
            foreach (var e in entities)
            {
                var s = world.EntityManager.GetComponentData<CreatureState>(e);
                var body = world.EntityManager.GetComponentData<CreatureBody>(e);
                if (tried.Contains(s.Individual) || !body.Blob.IsCreated || !FolkTalkSystem.CanTalk(s, ref body.Blob.Value))
                    continue;
                var d = math.distance(s.Position, player.Position);
                if (d < bestDistance)
                    (best, bestDistance, state) = (e, d, s);
            }
            return best;
        }

        void StandBefore(in CreatureState s)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var ahead = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            var xz = s.Position.xz + ahead * 2f;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(xz.x, s.Position.y + 0.05f, xz.y);
            t.Rotation = quaternion.RotateY(math.atan2(-ahead.x, -ahead.y));
            em.SetComponentData(player, t);
            var cam = FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(math.degrees(math.atan2(-ahead.x, -ahead.y)), 10f);
        }

        /// <summary>Presses Use on this folk; false if the window (or an animal's line) didn't come. Logs the delay.</summary>
        IEnumerator Press(Entity e, System.Action<bool, float> done)
        {
            var until = Time.realtimeSinceStartup + 3f;
            while (target != e && Time.realtimeSinceStartup < until)
                yield return null;
            var aimed = target == e;
            var start = Time.realtimeSinceStartup;
            var lines = FolkTalkSystem.Shown.Count;
            var used = beastReadyAt;
            testPress = true;
            while (!talking && beastReadyAt == used && Time.realtimeSinceStartup < start + 1f)
                yield return null;
            var delay = Time.realtimeSinceStartup - start;
            done(aimed && (talking || beastReadyAt != used), delay);
        }

        IEnumerator RunTest()
        {
            yield return new WaitForSecondsRealtime(25f);
            var tried = new HashSet<uint>();
            if (testPad)
            {
                yield return PadTest(tried);
                testCount = 0;
            }
            if (testOffers)
            {
                yield return Offers(tried);
                testCount = 0;
            }
            if (testReplies)
            {
                yield return Replies(tried);
                testCount = 0;
            }
            int opened = 0, fast = 0, stood = 0, faced = 0, total = 0;
            var worst = 0f;
            for (int n = 0; n < testCount; n++)
            {
                var e = NextFolk(tried, out var s);
                for (int hop = 0; e == Entity.Null && hop < 6; hop++)
                {
                    nextDoor += 20;
                    Hop(nextDoor);
                    yield return new WaitForSecondsRealtime(6f);
                    e = NextFolk(tried, out s);
                }
                if (e == Entity.Null)
                {
                    Debug.Log("[talk] test: no more folk");
                    break;
                }
                tried.Add(s.Individual);
                StandBefore(s);
                // (Past the animal's "Use again" wait, as well as settled.)
                yield return new WaitForSecondsRealtime(BeastAgain + 0.3f);
                var ok = false;
                var delay = 0f;
                yield return Press(e, (o, d) => (ok, delay) = (o, d));
                total++;
                worst = math.max(worst, delay);
                if (ok)
                    opened++;
                if (ok && delay <= 0.2f)
                    fast++;
                Debug.Log($"[talk] test {n}: #{s.Individual:X8} {(ok ? "opened" : "FAILED")} in {delay * 1000f:F0} ms");
                if (!talking)
                {
                    if (n == 0)
                        yield return Shot("animal");
                    continue;
                }
                Choose(0);
                if (n < 3)
                {
                    // (Once it has stopped: a walking folk drops whatever it was given.)
                    yield return new WaitForSecondsRealtime(1.2f);
                    Send(Request.Gesture, who.Individual, (byte)IdleAction.Wave, default);
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (World.DefaultGameObjectInjectionWorld.EntityManager.Exists(e))
                        Debug.Log($"[talk] test {n}: gesture Wave sent, folk's action now {World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<CreatureState>(e).Action}");
                }
                yield return new WaitForSecondsRealtime(2f);
                if (n == 0)
                    yield return Shot("talk");
                var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                if (em.Exists(e) && TryPlayer(out var player))
                {
                    var now = em.GetComponentData<CreatureState>(e);
                    var to = math.normalizesafe(player.Position.xz - now.Position.xz);
                    var dot = math.dot(to, new float2(math.sin(now.Yaw), math.cos(now.Yaw)));
                    if (now.Speed < 0.2f)
                        stood++;
                    if (dot > 0.8f)
                        faced++;
                    Debug.Log($"[talk] test {n}: folk {now.Mode}/{now.Stage}, speed {now.Speed:F2}, facing the player {dot:F2}, action {now.Action}");
                }
                End("test");
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (testCount > 0)
                Debug.Log($"[talk] test summary: {total} folk, opened {opened}, within 0.2 s {fast}, worst {worst * 1000f:F0} ms, stood {stood}, faced {faced}");
        }

        void Hop(int door)
        {
            if (!Doors.TryGetDoor(door, out var p, out var f))
                return;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = p + new float3(f.x * 2f, 0.3f, f.y * 2f);
            em.SetComponentData(player, t);
        }

        /// <summary>A pad press goes in at the top of Update and is processed at once, so this frame's reads see it.</summary>
        void PumpTestPad()
        {
            if (pad == null)
                return;
            if (stickOn || stickRelease)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stickLeft, rightStick = stickRight });
                InputSystem.Update();
                stickRelease = false;
                return;
            }
            if (tapNext.HasValue)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(tapNext.Value));
                InputSystem.Update();
                tapNext = null;
                tapHeld = true;
            }
            else if (tapHeld)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.Update();
                tapHeld = false;
            }
        }

        IEnumerator Tap(GamepadButton button)
        {
            tapNext = button;
            while (tapNext.HasValue || tapHeld)
                yield return null;
            yield return null;
        }

        IEnumerator Stick(Vector2 left, Vector2 right, float seconds)
        {
            (stickLeft, stickRight, stickOn) = (left, right, true);
            yield return new WaitForSecondsRealtime(seconds);
            (stickLeft, stickRight, stickOn) = (Vector2.zero, Vector2.zero, false);
            stickRelease = true;
            yield return null;
            yield return null;
        }

        /// <summary>The whole talk on a pad; whether the camera or the player moved while the stick was pushed.</summary>
        IEnumerator PadTest(HashSet<uint> tried)
        {
            // (A test window often isn't in front.)
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            pad = InputSystem.AddDevice<Gamepad>("TalkTestPad");
            InputSystem.EnableDevice(pad);
            pad.MakeCurrent();
            var e = NextFolk(tried, out var s);
            if (e == Entity.Null)
            {
                Debug.Log("[talk] pad: no folk");
                yield break;
            }
            for (int round = 0; round < 3; round++)
            {
                StandBefore(World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<CreatureState>(e));
                yield return new WaitForSecondsRealtime(0.5f);
                var until = Time.realtimeSinceStartup + 3f;
                while (target != e && Time.realtimeSinceStartup < until)
                    yield return null;
                Debug.Log($"[talk] pad: aimed at the folk: {target == e}");
                // (A door or item nearer than a folk that has wandered takes the press: stand before it again.)
                for (int tries = 0; tries < 6 && !talking; tries++)
                {
                    yield return Tap(GamepadButton.RightShoulder);
                    Debug.Log($"[talk] pad: RB try {tries}: window {talking}, folk {TargetDistance:F2} m, door {Doors.FacingDistance:F2} m, item {Items.Items.TargetDistance:F2} m");
                    if (talking)
                        break;
                    StandBefore(World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<CreatureState>(e));
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                if (!talking)
                    yield break;
                yield return Tap(GamepadButton.DpadDown);
                var picked = selected;
                yield return Tap(GamepadButton.South);
                Debug.Log($"[talk] pad: d-pad down selected {picked}, A asked: \"{body}\"");
                if (round == 2)
                {
                    yield return Tap(GamepadButton.North);
                    Debug.Log($"[talk] pad: Y: talking {talking}");
                    yield return Tap(GamepadButton.East);
                    Debug.Log($"[talk] pad: B after Y: talking {talking}, main menu open {Punchy.UI.MainMenu.Open}");
                    yield break;
                }
                TryPlayer(out var before);
                var yaw = Camera.main.transform.eulerAngles.y;
                yield return Stick(Vector2.zero, Vector2.right, 1f);
                TryPlayer(out var after);
                Debug.Log($"[talk] pad: right stick for 1 s: camera turned {Mathf.Abs(Mathf.DeltaAngle(yaw, Camera.main.transform.eulerAngles.y)):F2} deg, player moved {math.distance(before.Position, after.Position):F3} m, still talking {talking}");
                if (round == 0)
                {
                    yield return Tap(GamepadButton.East);
                    Debug.Log($"[talk] pad: B after the right stick: talking {talking}, main menu open {Punchy.UI.MainMenu.Open}");
                }
                else
                {
                    yield return Stick(Vector2.up, Vector2.zero, 0.5f);
                    Debug.Log($"[talk] pad: left stick: talking {talking}");
                }
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        IEnumerator Offers(HashSet<uint> tried)
        {
            bool quest = false, gift = false;
            for (int n = 0; n < 60 && !(quest && gift); n++)
            {
                var e = NextFolk(tried, out var s);
                if (e == Entity.Null)
                    break;
                tried.Add(s.Individual);
                StandBefore(s);
                yield return new WaitForSecondsRealtime(0.3f);
                var ok = false;
                yield return Press(e, (o, d) => ok = o);
                if (!ok || !talking)
                    continue;
                foreach (var topic in new[] { FolkConversation.Help, FolkConversation.Favour })
                {
                    var at = choices.FindIndex(c => c.Id == topic);
                    if (at < 0)
                        continue;
                    Choose(at);
                    if (!offering)
                        continue;
                    var isQuest = shown.Offer.Kind == OfferKind.Quest;
                    if (isQuest && quest || !isQuest && gift)
                    {
                        Choose(1);
                        continue;
                    }
                    yield return Shot(isQuest ? "offer-quest" : "offer-gift");
                    Choose(0);
                    Debug.Log($"[talk] offers: {title}: {body.Replace('\n', ' ')}");
                    yield return Shot(isQuest ? "accepted-quest" : "accepted-gift");
                    quest |= isQuest;
                    gift |= !isQuest;
                }
                var first = choices.Count > 0 ? choices[0].Id : "-";
                End("test");
                yield return new WaitForSecondsRealtime(0.5f);
                if (!quest && !gift)
                    continue;
                StandBefore(World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<CreatureState>(e));
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Press(e, (o, d) => ok = o);
                if (talking)
                {
                    Debug.Log($"[talk] offers: met again: \"{body}\", first topic was {first}, now {choices[0].Id}");
                    End("test");
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }
            Debug.Log($"[talk] offers summary: quest {quest}, gift {gift}");
        }

        /// <summary>The work topic's lines while the replies test runs: three replies, a reply-only topic with a gift offer
        /// taken by a reply, and a loop back to it past the depth cap.</summary>
        const string ReplyBook = @"{
  ""Any"": {
    ""work"": [ { ""text"": ""I keep bees."", ""options"": [
      { ""say"": ""Bees?"", ""then"": ""bees"" },
      { ""say"": ""Wave."", ""do"": ""gesture:Wave"" },
      { ""say"": ""Goodbye."", ""do"": ""end"" } ] } ],
    ""bees"": [ { ""text"": ""Honey bees, mostly. Take some berries for the road."", ""offer"": ""item:Berry"", ""options"": [
      { ""say"": ""Thank you!"", ""do"": ""accept"", ""then"": ""stings"" },
      { ""say"": ""No, thanks."", ""do"": ""decline"" } ] } ],
    ""stings"": [ { ""text"": ""Mind the stings."", ""options"": [ { ""say"": ""More bees?"", ""then"": ""bees"" } ] } ]
  }
}";

        /// <summary>P-42: opens a talk and walks ReplyBook's branch through the window, as a player would (the d-pad's
        /// step, then the pick), checking what the window lists at each answer.</summary>
        IEnumerator Replies(HashSet<uint> tried)
        {
            var bad = new List<string>();
            void Check(bool ok, string what)
            {
                Debug.Log($"[talk] replies: {(ok ? "ok" : "BAD")}: {what}");
                if (!ok)
                    bad.Add(what);
            }
            string Listed() => string.Join(" | ", choices.ConvertAll(c => c.Label));
            var opened = false;
            Entity e = Entity.Null;
            for (int n = 0; n < 10 && !opened; n++)
            {
                e = NextFolk(tried, out var s);
                if (e == Entity.Null)
                    break;
                tried.Add(s.Individual);
                StandBefore(s);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Press(e, (o, d) => opened = o && talking);
            }
            Check(opened, "a talk opened");
            if (!opened)
            {
                Debug.Log("[talk] replies => FAIL (no talk)");
                yield break;
            }
            FolkConversation.TestTalk = FolkConversation.Parse(Newtonsoft.Json.Linq.JObject.Parse(ReplyBook));
            var work = choices.FindIndex(c => c.Id == "work");
            Check(work >= 0, "work is a topic");
            Choose(work);
            Check(replying && Listed() == "Bees? | Wave. | Goodbye.", $"work lists its replies: {Listed()}");
            yield return Shot("replies");
            // The d-pad's step to the second reply, then the pick: the wave, back to the topics.
            Step(1);
            Choose(selected);
            Check(!replying && choices.Exists(c => c.Id == "work") && body.Contains("You: Wave."), $"wave goes back to the topics, body \"{body.Replace('\n', ' ')}\"");
            Check(shown.Action == IdleAction.Wave, $"the folk waves ({shown.Action})");
            yield return new WaitForSecondsRealtime(0.5f);
            if (Hosting || !Joined)
            {
                var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                if (em.Exists(e))
                    Debug.Log($"[talk] replies: folk's action after the wave: {em.GetComponentData<CreatureState>(e).Action}");
            }
            Choose(choices.FindIndex(c => c.Id == "work"));
            Choose(0);
            Check(replying && offering && Listed() == "Thank you! | No, thanks." && body.Contains("Honey bees"), $"bees offers berries: {Listed()}");
            yield return Shot("reply-offer");
            Choose(0);
            Check(gifted.Contains(who.Individual) && body.Contains("(You receive") && body.Contains("Mind the stings."), $"thank you takes the gift: \"{body.Replace('\n', ' ')}\"");
            Check(replying && Listed() == "More bees?", $"stings lists its reply: {Listed()}");
            yield return Shot("reply-taken");
            Choose(0);
            Check(shown.Depth == FolkConversation.MaxDepth && !replying && !offering && choices.Exists(c => c.Id == "work"),
                  $"past the cap, the gift already given: back to the topics (depth {shown.Depth}, {Listed()})");
            Choose(choices.FindIndex(c => c.Id == "work"));
            Choose(2);
            Check(endAt > 0f && choices.Count == 0, "goodbye ends the talk");
            yield return new WaitForSecondsRealtime(EndingSeconds + 0.5f);
            Check(!talking, "the window closed after the goodbye");
            FolkConversation.TestTalk = null;
            Debug.Log($"[talk] replies => {(bad.Count == 0 ? "PASS" : "FAIL (" + string.Join("; ", bad) + ")")}");
        }
    }
}
