using Punchy.Fauna;
using Punchy.Player;
using Punchy.Weather;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, the colossus climb probe (A-107 row 1, #276): "-colossusclimb [Kind|all]" walks the player to each
    // placed colossus in turn and, for each of its climb sections, stands it a step out from the section's outermost
    // column with the section's grade as its climb skill, pushes it straight in, and logs whether it climbs and tops out
    // onto the colossus (PASS), hangs on the flank going nowhere (FAIL stuck), or never takes hold (FAIL no hold).
    // Row 2: "-colossusride [Kind[,Kind...]|all] [margin]" does the same with the walkers walking (climb while it walks,
    // pushing toward the body's middle as it goes), with margin grades of climb skill to spare (default 1; 0 is a climber
    // at their limit, whom a big lurch shakes off), then stands still on the back for RideTime s while it walks and turns,
    // and logs a RIDE line: the share of frames still on the body, NaN frames, how far it carried them and how far it turned.
    public partial class DebugControls
    {
        /// <summary>"-colossusclimb": the kind to probe (null for all placed), and whether the probe is on.</summary>
        string ccKind;
        bool ccRequest;
        /// <summary>The probe's place: the slot and section, its stage, when the stage began, and the start of the climb.</summary>
        int ccSlot = -1, ccSection, ccStage;
        float ccStageAt, ccLogAt;
        float3 ccStart;
        float2 ccInward;
        float ccSummit;
        /// <summary>The climb so far: highest feet, highest column top stood on, when it last rose, whether it held on.</summary>
        float ccHighest, ccStood, ccRoseAt, ccToppedAt, ccHighAt;
        bool ccFell, ccFarShot;
        bool ccHeld, ccEntered;
        float ccJumpAt;
        PlayerMotion ccMotion;
        int ccPassed, ccFailed, ccSkillWas = -1;
        /// <summary>Row 2: sections a lurch shook the climber off at their limit (expected, not a failure), and the
        /// shake-off count when the section began.</summary>
        int ccShaken, ccShakenBefore, ccSpare, ccTries;
        /// <summary>Row 2's ride: on, the skill to spare, and the ride so far (frames on the body and in all, NaN frames,
        /// where it began, the heading it began at and the turn since).</summary>
        bool ccRide;
        int ccMargin = 1, ccOnFrames, ccFrames, ccNaN;
        float3 ccRideFrom;
        float ccYaw, ccTurned;
        const float RideTime = 40f;
        /// <summary>"-ccshots" (with -shotdir): the probe's own shots, each kind from afar, each section before the climb,
        /// and the climber high on the body; the shot under way, if any.</summary>
        bool ccShots;
        string ccShotTaking;
        /// <summary>"-ccclip" (with -shotdir, riding): frames from the game camera into a folder per climb and per ride
        /// (clip-Kind-sN-climb, -ride), at 5 a second climbing and 15 riding, to be joined into clips; when the next is due.</summary>
        bool ccClip;
        float ccFrameAt;
        int ccFrame;
        Texture2D ccFrameTex;

        /// <summary>The probe's flag (ApplyOptions): "-colossusclimb [Kind|all]".</summary>
        void ColossusClimbOptions(string[] args)
        {
            ColossusClimbSystem.LogPeers = System.Array.IndexOf(args, "-ccpeers") >= 0;
            var i = System.Array.IndexOf(args, "-colossusclimb");
            var ride = System.Array.IndexOf(args, "-colossusride");
            ccRide = ride >= 0;
            if (ccRide)
            {
                i = ride;
                ccMargin = i + 2 < args.Length && int.TryParse(args[i + 2], out var margin) ? margin : 1;
            }
            if (i < 0)
                return;
            ccRequest = true;
            ccShots = System.Array.IndexOf(args, "-ccshots") >= 0;
            ccClip = System.Array.IndexOf(args, "-ccclip") >= 0;
            ccKind = i + 1 < args.Length && !args[i + 1].StartsWith("-") && args[i + 1] != "all" ? args[i + 1] : null;
            ccSlot = -1;
            ccStage = 0;
        }

        /// <summary>"-ccshots": a shot named name from eye looking at target (the camera pinned till it is taken), if none
        /// is under way.</summary>
        void CcShot(string name, float3 eye, float3 target)
        {
            if (!ccShots || string.IsNullOrEmpty(shotDir) || ShotTool.Busy)
                return;
            shotCamFrom = eye;
            shotCamTo = target;
            shotCamOn = true;
            ccShotTaking = $"{StoneColossi.Placed[ccSlot]}-{name}{(ccRide ? "-ride" : "")}";
            ShotTool.Take(System.IO.Path.Combine(shotDir, ccShotTaking + ".png"), 1920, 1080, still: false);
        }

        /// <summary>"-ccclip": the game camera's frame into shotDir/clip-Kind-sN-part/fNNNN.jpg, rate a second.</summary>
        void CcFrame(string part, float rate)
        {
            if (!ccClip || !ccRide || string.IsNullOrEmpty(shotDir) || Time.time < ccFrameAt || ShotTool.Busy || Camera.main == null)
                return;
            ccFrameAt = Time.time + 1f / rate;
            var dir = System.IO.Path.Combine(shotDir, $"clip-{StoneColossi.Placed[ccSlot]}-s{ccSection}-{part}");
            System.IO.Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = was;
            if (ccFrameTex == null)
                ccFrameTex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            ccFrameTex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            ccFrameTex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"f{ccFrame++:D4}.jpg"), ccFrameTex.EncodeToJPG(85));
        }

        /// <summary>Whether slot is one the probe climbs (placed, of the asked kind, with climb sections once solid).</summary>
        bool CcWanted(int slot) => slot < StoneColossi.Placed.Length && slot < StoneColossi.Positions.Length &&
                                   !math.any(math.isnan(StoneColossi.Positions[slot])) &&
                                   (ccKind == null || System.Array.Exists(ccKind.Split(','), k => string.Equals(StoneColossi.Placed[slot].ToString(), k, System.StringComparison.OrdinalIgnoreCase)));

        void CcStage(int stage)
        {
            ccStage = stage;
            ccStageAt = Time.time;
            ccEntered = false;
        }

        /// <summary>Moves on to the next section, or the next wanted slot's first, or ends the probe.</summary>
        void CcNext()
        {
            ccTries = 0;
            if (ccSlot >= 0 && ccSection + 1 < ColossusCollision.SectionsOf(ccSlot).Length)
            {
                ccSection++;
                CcStage(1);
                return;
            }
            do ccSlot++;
            while (ccSlot < StoneColossi.Placed.Length && !CcWanted(ccSlot));
            ccSection = 0;
            if (ccSlot >= StoneColossi.Placed.Length)
            {
                Debug.Log($"DebugControls: colossus climb: DONE, {ccPassed} passed, {ccFailed} failed" +
                          (ccRide ? $", {ccShaken} shaken off at the limit, {ccNaN} NaN frames, shaken off {ColossusClimbSystem.ShakenOff} times, biggest kick ridden {V(ColossusClimbSystem.BiggestKick)} m/s" : ""));
                ColossusClimbSystem.LogRide = false;
                ccRequest = false;
                PlayerInputSystem.TestMove = null;
                PlayerInputSystem.TestCamera = null;
                SetClimbSkill(ccSkillWas);
                Climbing.SkillOverride = -1;
                ColossusWalker.Freeze = false;
                return;
            }
            CcStage(0);
        }

        void CcResult(bool pass, string why, float3 at)
        {
            var sec = ColossusCollision.SectionsOf(ccSlot)[ccSection];
            // (Row 2: a climber with no skill to spare whom a lurch shook off did what the rule says.)
            if (!pass && ccRide && ccSpare < 1 && ColossusClimbSystem.ShakenOff > ccShakenBefore)
            {
                ccShaken++;
                Debug.Log($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} section {ccSection} {sec.Look} grade {sec.Grade}: SHAKEN OFF at the limit " +
                          $"(skill to spare {ccSpare}, {ColossusClimbSystem.ShakenOff - ccShakenBefore} lurches), {why}; highest feet {V(ccHighest - ccStart.y)} m up, {V(Time.time - ccStageAt)} s");
                PlayerInputSystem.TestMove = float2.zero;
                CcNext();
                return;
            }
            // (Row 2: a walking body's leg can stride away before the climber reaches it: try again from where it is now.)
            if (!pass && ccRide && why == "no hold" && ccTries < 2)
            {
                ccTries++;
                Debug.Log($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} section {ccSection}: no hold (it walked on), try {ccTries + 1}");
                PlayerInputSystem.TestMove = float2.zero;
                CcStage(1);
                return;
            }
            if (pass) ccPassed++;
            else ccFailed++;
            Debug.Log($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} section {ccSection} {sec.Look} grade {sec.Grade}: " +
                      $"{(pass ? "PASS" : "FAIL")} {why}; highest feet {V(ccHighest - ccStart.y)} m up, stood on a top {V(ccStood - ccStart.y)} m up " +
                      $"(section summit {V(ccSummit - ccStart.y)} m), at ({V(at.x)}, {V(at.y)}, {V(at.z)}), {V(Time.time - ccStageAt)} s");
            if (!pass)
            {
                if (ccMotion.OnTrunk)
                {
                    // (And the gathered column nearest its foot: a trunk that is no column is a rock or a tree in the way.)
                    ref var all = ref ColossusClimbs.List.Data;
                    var nearest = float.MaxValue;
                    for (int i = 0; i < all.Length; i++)
                        nearest = math.min(nearest, math.distance(all[i].Base, ccMotion.TrunkBase));
                    Debug.Log($"DebugControls: colossus climb: holding a {(ColossusClimbs.IsColumn(ccMotion.TrunkBase, ccMotion.TrunkRadius) ? "column" : "trunk (no column)")}, " +
                              $"foot {V(ccMotion.TrunkBase.y - at.y)}, top {V(ccMotion.TrunkBase.y + ccMotion.TrunkHeight - at.y)}, radius {V(ccMotion.TrunkRadius)}, " +
                              $"at ({V(ccMotion.TrunkBase.x)}, {V(ccMotion.TrunkBase.z)}); {all.Length} columns gathered, nearest {V(nearest)} m");
                }
                // (What it was up against: the columns within reach, their feet and tops over the body's, grade, gap, and how
                // squarely ahead they are.)
                var near = new System.Text.StringBuilder();
                ref var list = ref ColossusClimbs.List.Data;
                for (int i = 0; i < list.Length; i++)
                {
                    var c = list[i];
                    var to = c.Base.xz - at.xz;
                    var gap = math.length(to) - c.Radius;
                    if (gap < 2.5f)
                        near.Append($" [foot {V(c.Base.y - at.y)}, top {V(c.Base.y + c.Height - at.y)}, grade {c.Grade}, gap {V(gap)}, ahead {math.dot(math.normalizesafe(to), ccInward):F2}]");
                }
                Debug.Log($"DebugControls: colossus climb: up against{(near.Length == 0 ? " nothing" : near.ToString())}");
            }
            PlayerInputSystem.TestMove = float2.zero;
            if (pass && ccRide)
            {
                // Row 2: now stand on the back while it walks and turns.
                ccOnFrames = ccFrames = 0;
                ccRideFrom = at;
                ccTurned = 0f;
                ccYaw = float.NaN;
                CcStage(3);
                return;
            }
            CcNext();
        }

        /// <summary>
        /// The probe, a frame: once the world has settled and the colossi are solid, goes to each wanted slot (so it shows),
        /// then climbs each of its sections in turn, logging each result ("colossus climb: ...").
        /// </summary>
        void ColossusClimbTest()
        {
            if (!ccRequest || queryWorld == null || !queryWorld.IsCreated || Time.timeSinceLevelLoad - optionsAt < settle + 3f ||
                !EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<PlayerTag>(out var self))
                return;
            var em = queryWorld.EntityManager;
            using var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            if (!bodyQuery.TryGetSingleton<PlayerBody>(out var body))
                return;
            if (ccSlot < 0)
            {
                if (ccSkillWas < 0)
                    ccSkillWas = Climbing.SkillOverride;
                // Row 1 climbs still bodies: the walkers are held where they are, unturned, for the whole run. Row 2's
                // ride climbs them walking.
                ColossusWalker.Freeze = !ccRide;
                ColossusClimbSystem.LogRide = ccRide;
                ColossusClimbSystem.ShakenOff = 0;
                ColossusClimbSystem.BiggestKick = 0f;
                ccNaN = 0;
                Debug.Log($"DebugControls: colossus climb: start, {ColossusCollision.Summary()}");
                CcNext();
                return;
            }
            var t = em.GetComponentData<LocalTransform>(self);
            var m = em.GetComponentData<PlayerMotion>(self);
            var stageTime = Time.time - ccStageAt;
            ccMotion = m;
            if (ccShotTaking != null && !ShotTool.Busy)
            {
                Debug.Log($"DebugControls: colossus climb: shot {ccShotTaking} {ShotTool.Status} {ShotTool.LastCheck}");
                ccShotTaking = null;
                shotCamOn = false;
            }
            if (!math.all(math.isfinite(t.Position)) || !math.all(math.isfinite(t.Rotation.value)))
                ccNaN++;
            switch (ccStage)
            {
                case 0:   // Off to the slot (30 m off its middle), so its colossus is shown and solid.
                    if (!ccEntered)
                    {
                        var p = StoneColossi.Positions[ccSlot];
                        var xz = p.xz + new float2(30f, 0f);
                        t.Position = new float3(xz.x, source.SampleHeight(xz) + 0.5f, xz.y);
                        em.SetComponentData(self, t);
                        em.SetComponentData(self, new PlayerMotion { Grounded = true });
                        PlayerInputSystem.TestMove = float2.zero;
                        ccEntered = true;
                        ccFarShot = false;
                        // (Shots: the hour held where "-time" put it and clear weather at each colossus, so a later kind
                        // is not shot at night or in a storm.)
                        if (ccShots)
                        {
                            if (Cycle != null)
                                Cycle.Paused = true;
                            if (FindAnyObjectByType<WeatherSystem>() is WeatherSystem weather)
                                weather.ForceAt("Clear", new Vector2(xz.x, xz.y));
                        }
                    }
                    else if (ccShots && stageTime > 4f && !ccFarShot && ccShotTaking == null)
                    {
                        ccFarShot = true;
                        // (From afar, rule 25: across the land at it, a little above, with the sun behind.)
                        var p = StoneColossi.Positions[ccSlot];
                        var farXZ = p.xz + new float2(-260f, -260f);
                        CcShot("far", new float3(farXZ.x, math.max(source.SampleHeight(farXZ), p.y) + 45f, farXZ.y), p + new float3(0f, 20f, 0f));
                        ccStageAt -= 1f;
                    }
                    else if (stageTime > 4f && ccShotTaking == null)
                    {
                        if (ColossusCollision.SectionsOf(ccSlot).Length == 0)
                        {
                            Debug.Log($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} has no climb sections");
                            ccSection = int.MaxValue - 1;
                            CcNext();
                        }
                        else
                            CcStage(1);
                    }
                    break;
                case 1:   // A step out from the section, still, with the section's grade as the climb skill.
                    if (!ccEntered)
                    {
                        if (!ColossusCollision.SectionRoute(ccSlot, ccSection, out ccStart, out ccInward, out ccSummit))
                        {
                            ccFailed++;
                            Debug.LogWarning($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} section {ccSection}: FAIL no columns");
                            CcNext();
                            return;
                        }
                        ccStart.y = source.SampleHeight(ccStart.xz);
                        t.Position = ccStart + new float3(0f, 0.3f, 0f);
                        t.Rotation = quaternion.LookRotationSafe(new float3(ccInward.x, 0f, ccInward.y), math.up());
                        em.SetComponentData(self, t);
                        em.SetComponentData(self, new PlayerMotion { Grounded = true });
                        var grade = ColossusCollision.SectionsOf(ccSlot)[ccSection].Grade;
                        // (Riding: never the top skill, which takes hold of the smooth columns between the sections too.)
                        var skill = ccRide ? math.clamp(grade + ccMargin, 0, Climbing.Craggy) : grade;
                        ccSpare = skill - grade;
                        ccShakenBefore = ColossusClimbSystem.ShakenOff;
                        SetClimbSkill(skill);
                        PlayerInputSystem.TestMove = float2.zero;
                        PlayerInputSystem.TestCamera = ccInward;
                        var eye = ccStart + new float3(-ccInward.x * 12f + ccInward.y * 6f, 5f, -ccInward.y * 12f - ccInward.x * 6f);
                        Debug.Log($"DebugControls: colossus climb: {StoneColossi.Placed[ccSlot]} section {ccSection} from ({V(ccStart.x)}, {V(ccStart.y)}, {V(ccStart.z)}), " +
                                  $"summit {V(ccSummit - ccStart.y)} m up; to watch: -goto {V(ccStart.x)},{V(ccStart.z)} -camlook {V(eye.x)},{V(eye.y)},{V(eye.z)},{V(ccStart.x + ccInward.x * 4f)},{V(ccStart.y + 3f)},{V(ccStart.z + ccInward.y * 4f)}");
                        ccEntered = true;
                    }
                    else if (ccShots && stageTime > 1.5f && stageTime < 2f && ccShotTaking == null)
                    {
                        // (The section from out on the ground, its whole height, the climber small at its foot.)
                        var height = math.max(ccSummit - ccStart.y, 8f);
                        var side = new float2(ccInward.y, -ccInward.x);
                        var eyeXZ = ccStart.xz - ccInward * height * 1.1f + side * height * 0.4f;
                        CcShot($"s{ccSection}-section", new float3(eyeXZ.x, math.max(source.SampleHeight(eyeXZ), ccStart.y) + height * 0.45f, eyeXZ.y),
                               ccStart + new float3(ccInward.x * 4f, height * 0.45f, ccInward.y * 4f));
                        ccStageAt -= 0.5f;
                    }
                    else if (stageTime > 2f && ccShotTaking == null)
                    {
                        ccHighest = ccStood = t.Position.y;
                        ccRoseAt = Time.time;
                        ccToppedAt = ccHighAt = -1f;
                        ccHeld = ccFell = false;
                        CcStage(2);
                    }
                    break;
                case 2:   // Pushing straight in: climb the flank, top out, walk on over the back until it stops rising.
                    if (!ccEntered)
                    {
                        ccFrame = 0;
                        ccEntered = true;
                    }
                    CcFrame("climb", 5f);
                    // (Riding: in toward the body's middle as it is now, as it walks and turns, not the way it first faced; and
                    // once on a low top but no higher for 2 s, toward the section's peak, as a player would turn to the higher part
                    // rather than push on into a slanted wall or off the column's far side.)
                    if (ColossusCollision.SectionPeak(ccSlot, ccSection, out var peak, out var middle))
                    {
                        if (ccHighAt < 0f && (ccFell || ccToppedAt >= 0f && Time.time - ccRoseAt > 2f))
                            ccInward = math.normalizesafe(peak.xz - t.Position.xz, ccInward);
                        else if (ccRide)
                            ccInward = math.normalizesafe(middle - t.Position.xz, ccInward);
                    }
                    PlayerInputSystem.TestMove = ccInward;
                    PlayerInputSystem.TestCamera = ccInward;
                    if (math.any(math.isnan(t.Position)))
                    {
                        CcResult(false, "NaN position", t.Position);
                        return;
                    }
                    var holding = m.OnWall || m.OnTrunk;
                    ccHeld |= holding;
                    // (Under an overhang, a shell's rim or a belly, it jumps for it, as a player would.)
                    if (m.Grounded && !holding && Time.time - ccRoseAt > 3f && Time.time > ccJumpAt)
                    {
                        PlayerInputSystem.TestJump = true;
                        ccJumpAt = Time.time + 2f;
                    }
                    if (t.Position.y > ccHighest + 0.05f)
                    {
                        ccHighest = t.Position.y;
                        ccRoseAt = Time.time;
                    }
                    if (m.Grounded && !holding && t.Position.y > ccStart.y + 1.5f &&
                        ColossusClimbs.FloorUnder(t.Position, 0.3f, 0.2f, out var top) && top > ccStood)
                    {
                        ccStood = top;
                        if (ccHighAt < 0f && top - ccStart.y >= 0.6f * (ccSummit - ccStart.y))
                        {
                            ccHighAt = Time.time;
                            // (Topped out high on the body: from behind and above the climber, the back ahead.)
                            CcShot($"s{ccSection}-top", t.Position + new float3(-ccInward.x * 9f, 5f, -ccInward.y * 9f), t.Position + new float3(ccInward.x * 6f, 0f, ccInward.y * 6f));
                        }
                        if (ccToppedAt < 0f)
                        {
                            ccToppedAt = Time.time;
                            Debug.Log($"DebugControls: colossus climb: TOP-OUT onto a column {V(top - ccStart.y)} m up at {V(stageTime)} s");
                        }
                    }
                    if (Time.time > ccLogAt)
                    {
                        ccLogAt = Time.time + 1f;
                        Debug.Log($"DebugControls: colossus climb: ({V(t.Position.x)}, {V(t.Position.y - ccStart.y)} up, {V(t.Position.z)}), wall {m.OnWall}, trunk {m.OnTrunk}{(m.OnTrunk ? $" (top {V(m.TrunkBase.y + m.TrunkHeight - t.Position.y)})" : "")}, grounded {m.Grounded}");
                    }
                    var walkedOff = ccToppedAt >= 0f && t.Position.y < ccStood - 2.5f;
                    // (Off a low top onto a lower column or back to the ground: climb on from there, as a player would.)
                    if (walkedOff && ccHighAt < 0f)
                    {
                        walkedOff = false;
                        // (And from here toward the section's peak, not on past the body.)
                        ccFell = true;
                        ccToppedAt = -1f;
                        ccHighest = t.Position.y;
                        ccRoseAt = Time.time;
                    }
                    // (A pass stands at least 60% of the way up the section: a low rim column stood on is not the back.)
                    var high = ccStood - ccStart.y >= 0.6f * (ccSummit - ccStart.y);
                    // (Done once high for a few seconds, walked off, or no higher for a while; a climb still going up the
                    // next column goes on, to the time limit.)
                    if (ccToppedAt >= 0f && (walkedOff || Time.time - ccRoseAt > 7f || ccHighAt >= 0f && Time.time - ccHighAt > 4f))
                        CcResult(high, !high ? "stalled on a low top" : walkedOff ? "topped out, then walked off the far side" : "topped out", t.Position);
                    else if (ccToppedAt < 0f && Time.time - ccRoseAt > 8f)
                        CcResult(false, ccHeld ? "stuck on the flank" : "no hold", t.Position);
                    // (Long enough for the tallest: a 50 m leg is a single column, climbed at well under a metre a second.)
                    else if (stageTime > 40f + 2f * (ccSummit - ccStart.y))
                        CcResult(false, "out of time", t.Position);
                    break;
                case 3:   // Row 2: standing still on the back while it walks and turns.
                {
                    PlayerInputSystem.TestMove = float2.zero;
                    if (!ccEntered)
                    {
                        ccFrame = 0;
                        ccEntered = true;
                    }
                    CcFrame("ride", 15f);
                    var on = m.OnTrunk || m.Grounded && ColossusClimbs.FloorUnder(t.Position, 0.3f, 0.3f, out var under) && t.Position.y - under < 0.3f;
                    // (The watch starts once it stands: a climb that ended a step off a narrow top's far side is still
                    // dropping to the next top as the ride begins, up to 3 s, and that is the probe's steering, not the carry.)
                    if (ccFrames > 0 || on || stageTime > 3f)
                        ccFrames++;
                    if (on)
                        ccOnFrames++;
                    var fwd = math.mul(t.Rotation, new float3(0f, 0f, 1f));
                    var yaw = math.degrees(math.atan2(fwd.x, fwd.z));
                    if (!float.IsNaN(ccYaw))
                        ccTurned += math.degrees(math.atan2(math.sin(math.radians(yaw - ccYaw)), math.cos(math.radians(yaw - ccYaw))));
                    ccYaw = yaw;
                    if (stageTime > RideTime * 0.5f && stageTime < RideTime * 0.5f + 1f && ccShotTaking == null)
                        CcShot($"s{ccSection}-riding", t.Position + new float3(-14f, 8f, -14f), t.Position);
                    var carried = math.distance(t.Position.xz, ccRideFrom.xz);
                    // (A walker rests between walks: wait for it to set off, up to three ride times.)
                    if (stageTime > RideTime && (carried > 1f || stageTime > RideTime * 3f))
                    {
                        var share = ccOnFrames / (float)math.max(ccFrames, 1);
                        var ok = share > 0.98f && ccNaN == 0 && carried > 1f;
                        Debug.Log($"DebugControls: colossus climb: RIDE {(ok ? "PASS" : "FAIL")} {StoneColossi.Placed[ccSlot]} section {ccSection}: on the body {share * 100f:F1}% of {ccFrames} frames, " +
                                  $"{ccNaN} NaN frames, carried {V(carried)} m, turned {V(ccTurned)} deg, now ({V(t.Position.x)}, {V(t.Position.y)}, {V(t.Position.z)})");
                        if (ok) ccPassed++;
                        else ccFailed++;
                        CcNext();
                    }
                    break;
                }
            }
        }
    }
}
