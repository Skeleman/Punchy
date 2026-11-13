using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, ApplyOptions: reading the test flags. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        void ApplyOptions(string[] args)
        {
            optionsAt = Time.timeSinceLevelLoad;
            // "-place ledge:lip[:Body]" and "-ledgepiece" (A-101 row 1b): the ledge lip test (LedgeLipTest).
            LedgeLipOptions(args);
            // "-colossusclimb [Kind[,Kind...]|all]" (A-107 row 1): the colossus climb probe (ColossusClimbTest).
            // "-colossusride [Kind[,Kind...]|all] [margin]" (A-107 row 2): the same climbing walking colossi, then riding.
            ColossusClimbOptions(args);
            // "-breathtest" (A-102 row 1): air and water breathers held on a lake bed, their breath logged (BreathTest).
            breathTest = System.Array.IndexOf(args, "-breathtest") >= 0;
            // "-ridetest" (A-104 row 1): three rider/mount pairs, one refused, walked for a minute (RideTest).
            rideTest = System.Array.IndexOf(args, "-ridetest") >= 0;
            // "-ridelife" (A-104 row 2): the rides a town makes by itself, then each way of getting down (RideLifeTest).
            rideLife |= System.Array.IndexOf(args, "-ridelife") >= 0;   // (Kept on through -camlook lines for its shots.)
            // "-rideplayer [rider|mount]" (A-104 row 3): the player rides a dog, or another player (RidePlayerTest).
            var rp = System.Array.IndexOf(args, "-rideplayer");
            if (rp >= 0)
                ridePlayer = rp + 1 < args.Length && (args[rp + 1] == "rider" || args[rp + 1] == "mount") ? args[rp + 1] : "";
            // "-divetest [Human|FishFolk]" (A-102 row 2): a person and a fish folk out swimming and diving (DiveTest).
            var diveArg = System.Array.IndexOf(args, "-divetest");
            diveTest = diveArg >= 0 ? (diveArg + 1 < args.Length && !args[diveArg + 1].StartsWith("-") ? args[diveArg + 1] : "") : null;
            // "-playerdive [water]" (A-102 row 3): the player dives, rises and runs out of breath (PlayerDiveTest).
            var playerDiveArg = System.Array.IndexOf(args, "-playerdive");
            playerDive = playerDiveArg >= 0;
            pdWater = playerDive && playerDiveArg + 1 < args.Length && args[playerDiveArg + 1] == "water";
            // ("-playerdive [water] follow": the player's own camera follows the dive rather than the side shot camera.)
            pdFollow = playerDive && System.Array.IndexOf(args, "follow", playerDiveArg) > playerDiveArg;
            // "-netwatch [Species]" (A-103 row 2b): that species (default SeaFolk) and other players' depth, every 10 s (NetWatch).
            var netWatchArg = System.Array.IndexOf(args, "-netwatch");
            netWatchSpecies = netWatchArg < 0 ? null
                : netWatchArg + 1 < args.Length && !args[netWatchArg + 1].StartsWith("-") ? args[netWatchArg + 1] : "SeaFolk";
            landmarkPlacePending = null; // (a later shot's options replace an earlier -place still being retried)
            if (!drawCalls.Valid)
            {
                drawCalls = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
                batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
                tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            }
            // #25 (W-83): "-interiortest" builds one test room off the island (InteriorCells.TestRoom) and logs where.
            if (System.Array.IndexOf(args, "-interiortest") >= 0 && queryWorld != null && queryWorld.IsCreated &&
                terrainQuery.TryGetSingleton<TerrainSource>(out var roomSource))
                Debug.Log(InteriorCells.TestRoom(roomSource));
            // W-30: "-archipelago off|on" takes the archipelago out of the terrain or puts it back (before/after numbers from one build).
            var archArg = System.Array.IndexOf(args, "-archipelago");
            if (archArg >= 0 && archArg + 1 < args.Length)
            {
                EnsureLocations(out _);
                if (queryWorld != null && queryWorld.IsCreated && terrainQuery.TryGetSingleton<TerrainSource>(out var archSource))
                {
                    if (archSource.Archipelago.IsCreated)
                        archipelagoKept = archSource.Archipelago;
                    archSource.Archipelago = args[archArg + 1] == "off" ? default : archipelagoKept;
                    queryWorld.EntityManager.SetComponentData(terrainQuery.GetSingletonEntity(), archSource);
                }
            }
            // "-incave": on the floor of the nearest cave's deepest chamber (after any -place).
            var inCave = System.Array.IndexOf(args, "-incave") >= 0;
            footDrift = System.Array.IndexOf(args, "-footdrift") >= 0;
            var spinAt = System.Array.IndexOf(args, "-spincheck");
            spinCheck = spinAt >= 0;
            // ("-noflaresettle": fliers steer their flare the old way, for #12's before numbers from the same build.)
            Punchy.Fauna.CreatureBrainSystem.FlareSettleOff.Data = System.Array.IndexOf(args, "-noflaresettle") >= 0 ? 1 : 0;
            // A-22 (#23): "-renderpasslog" logs CreatureRenderSystem's time per pass; "-oldcull" culls the old (managed) way.
            Punchy.Fauna.CreatureRenderSystem.PassLog = System.Array.IndexOf(args, "-renderpasslog") >= 0;
            Punchy.Fauna.CreatureRenderSystem.OldCull = System.Array.IndexOf(args, "-oldcull") >= 0;
            if (spinCheck && spinAt + 1 < args.Length && ParseFloats(args[spinAt + 1]) is float[] spin && spin.Length >= 2)
            {
                spinDegrees = spin[0];
                spinSeconds = spin[1];
            }
            // "-actiontest roll:Human,Dog" (or fly:Pigeon, fish:Trout): the right face button's checks (A-04), logged, each
            // switching to the bodies named in turn (see ActionTest); "-setbody Lizard": switch to that body now.
            var testArg = System.Array.IndexOf(args, "-actiontest");
            if (testArg >= 0 && testArg + 1 < args.Length)
            {
                actionTest = args[testArg + 1];
                actionStart = Time.time;
                actionSlot = -1;
            }
            var bodyArg = System.Array.IndexOf(args, "-setbody");
            if (bodyArg >= 0 && bodyArg + 1 < args.Length)
                SetBody(args[bodyArg + 1]);
            // "-climbskill N" (A-101): the player climbs with skill N (0-5) whatever its body, until "-climbskill -1".
            var climbSkillArg = System.Array.IndexOf(args, "-climbskill");
            if (climbSkillArg >= 0 && climbSkillArg + 1 < args.Length && int.TryParse(args[climbSkillArg + 1], out var climbSkill))
                SetClimbSkill(climbSkill);
            // "-climbband x,y,z,ox,oz,halfwidth,height,grade" (A-101 row 1): marks a climbing band, as "-place climb" logs
            // it, so a joined client's player climbs the same band and sees the same marking.
            var climbBandArg = System.Array.IndexOf(args, "-climbband");
            if (climbBandArg >= 0 && climbBandArg + 1 < args.Length && ParseClimbBand(args[climbBandArg + 1], out var markedBand))
            {
                ClimbBands.Add(markedBand);
                MarkClimbBand(markedBand);
            }
            // "-shotonhunt": one shot the moment a land hunter enters its sprint, camera framing hunter and prey (#9).
            shotOnHunt = System.Array.IndexOf(args, "-shotonhunt") >= 0;
            var huntSideArg = System.Array.IndexOf(args, "-huntside");
            huntSide = huntSideArg >= 0 && huntSideArg + 1 < args.Length && float.TryParse(args[huntSideArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hs) ? hs : 0f;
            // A-23 (#7): "-huntpair" with -huntside aims at the middle of hunter and prey instead of the hunter, so a close
            // side-on view keeps both in frame.
            huntPair = System.Array.IndexOf(args, "-huntpair") >= 0;
            // A-21 (#6): "-leapphase 0.15" holds every leaping run (dogs, cats, wolves) at that point of its stride, for a
            // shot of each phase; "-hunter Dog" makes -shotonhunt wait for that species; "-gaitlog" logs each loaded
            // four-legged species' gallop (Leap, Bound or Rotary).
            var leapArg = System.Array.IndexOf(args, "-leapphase");
            CreatureAnimator.LeapFreeze.Data = leapArg >= 0 && leapArg + 1 < args.Length && float.TryParse(args[leapArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lp) ? math.frac(lp) + 1f : 0f;
            var hunterArg = System.Array.IndexOf(args, "-hunter");
            huntSpecies = hunterArg >= 0 && hunterArg + 1 < args.Length ? args[hunterArg + 1] : null;
            if (System.Array.IndexOf(args, "-gaitlog") >= 0)
                GaitLog();
            // "-remoteroll": a client frames, logs and shoots another player's somersault (#48).
            remoteRoll = System.Array.IndexOf(args, "-remoteroll") >= 0;
            // A-26: "-chattest" (launch line) makes folk stop to talk far more often (Punchy.Fauna.CreatureBrainSystem.ForceChat);
            // "-chatshot" frames the first pair talking side-on (ChatShot).
            if (System.Array.IndexOf(args, "-chattest") >= 0)
                Punchy.Fauna.CreatureBrainSystem.ForceChat.Data = 1;
            chatShot = System.Array.IndexOf(args, "-chatshot") >= 0;
            FolkTalkLabels.BubblesWithoutHud = chatShot;
            // A-27: "-chatmix" (launch line) logs what the townsfolk round the camera are doing, and the talks, every 15 s
            // (FolkTalkSystem.LogMix).
            Punchy.Fauna.FolkTalkSystem.ChatMix = System.Array.IndexOf(LaunchArgs, "-chatmix") >= 0;
            remoteRollStep = 0;
            // A-09 (#40): "-nocolossus" hides the Dunes colossus, "-framelog" logs frame times, "-colossusshot 300" puts the
            // player and camera that far from it.
            DunesColossus.Hidden = System.Array.IndexOf(args, "-nocolossus") >= 0;
            // A-10 (#41): "-nomantrap" hides the carnivorous plants, "-mantraptest" runs the 60 s scripted test on the first one,
            // "-mantrapshot side|lunge" frames it.
            Mantrap.Hidden = System.Array.IndexOf(args, "-nomantrap") >= 0;
            if (System.Array.IndexOf(args, "-mantraptest") >= 0)
                Mantrap.TestRequested = true;
            if (System.Array.IndexOf(args, "-mantrapstill") >= 0)
                Mantrap.StillRequested = true;
            var mmArg = System.Array.IndexOf(args, "-mimicshot");
            mimicShotMode = mmArg >= 0 && mmArg + 1 < args.Length ? args[mmArg + 1] : null;
            var mtArg = System.Array.IndexOf(args, "-mantrapshot");
            mantrapShotMode = mtArg >= 0 && mtArg + 1 < args.Length ? args[mtArg + 1] : null;
            DunesColossus.FrameLog = System.Array.IndexOf(args, "-framelog") >= 0;
            // A-72: "-walkershot Saguaro,80,side" frames a walking colossus (see WalkerShot).
            var wsArg = System.Array.IndexOf(args, "-walkershot");
            ColossusWalker.Shot = wsArg >= 0 && wsArg + 1 < args.Length ? args[wsArg + 1] : null;
            var wpArg = System.Array.IndexOf(args, "-walkerphase");
            ColossusWalker.Phase = wpArg >= 0 && wpArg + 1 < args.Length ? args[wpArg + 1] : null;
            var colArg = System.Array.IndexOf(args, "-colossusshot");
            colossusShotDistance = colArg >= 0 && colArg + 1 < args.Length && float.TryParse(args[colArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cd) ? cd : 0f;
            // "-huntlog": log land hunters' chases (Z-53).
            huntLog = System.Array.IndexOf(args, "-huntlog") >= 0;
            // A-90: "-breathlog" logs the breath shape per species and the live puff count; "-breathsize old" brings back
            // the one-size breath from before, to compare.
            Punchy.Fauna.CreatureBreathSystem.Log = System.Array.IndexOf(args, "-breathlog") >= 0;
            var bsArg = System.Array.IndexOf(args, "-breathsize");
            Punchy.Fauna.CreatureBreathSystem.Legacy = bsArg >= 0 && bsArg + 1 < args.Length && args[bsArg + 1] == "old";
            // "-personalitylog Dog": list each of that species' personality a few seconds in (#50).
            var plArg = System.Array.IndexOf(args, "-personalitylog");
            personalitySpecies = plArg >= 0 && plArg + 1 < args.Length ? args[plArg + 1] : null;
            // "-comfort": log weather comfort meters every 5 s; "-comfort off" disables the brain's comfort check (A-71).
            var cfArg = System.Array.IndexOf(args, "-comfort");
            comfortLog = cfArg >= 0;
            comfortNext = 6f; // (statics outlive a play with domain reload off)
            Comfort.Off.Data = cfArg >= 0 && cfArg + 1 < args.Length && args[cfArg + 1] == "off" ? 1 : 0;
            // "-weathertalk": log each weather line folk say, with its cell and how many are up there (A-71 row 4).
            WeatherTalkSystem.LogLines = System.Array.IndexOf(args, "-weathertalk") >= 0;
            // "-comfortheat -0.3": shifts the comfort grid's heat (a forced cold snap or heat wave for comfort shots).
            var chArg = System.Array.IndexOf(args, "-comfortheat");
            Comfort.HeatShift.Data = chArg >= 0 && chArg + 1 < args.Length && float.TryParse(args[chArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var chv) ? chv : 0f;
            // "-thirsty" (A-49 #64): every creature's thirst is topped up for this line, so drinking shows up soon.
            thirsty = System.Array.IndexOf(args, "-thirsty") >= 0;
            personalityAt = -1f;
            // "-lift 15": the player held that high over the ground (see Lift), per shot line.
            var liftArg = System.Array.IndexOf(args, "-lift");
            liftHeight = liftArg >= 0 && liftArg + 1 < args.Length && float.TryParse(args[liftArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lh) ? lh : 0f;
            liftNext = Time.time + 4f; // first sample 4 s in, once the spawned herd has settled
            // "-spawnradius 12": this line's -spawn puts land animals at most that far out (default 45 m; #46 needs deer in flee range).
            var srArg = System.Array.IndexOf(args, "-spawnradius");
            spawnRadius = srArg >= 0 && srArg + 1 < args.Length && float.TryParse(args[srArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sr) ? sr : 45f;
            liftSample = 0;
            // "-liftpass 8" (A-47 #46): with -lift, the player starts 40 m west and 10 m north of where it stood, and 5 s in
            // runs due east at that speed (m/s) for 80 m: a moving threat passing the herd 10 m off, on the ground or high up.
            var lpArg = System.Array.IndexOf(args, "-liftpass");
            liftPass = lpArg >= 0 && lpArg + 1 < args.Length && float.TryParse(args[lpArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lpv) ? lpv : 0f;
            liftPassStart = null;
            // "-nobrainlod" (A-47 #59): every creature thinks every frame. "-fixeddt" (#61): Play mode steps a fixed 1/30 s,
            // so two runs of the same line can be compared number for number.
            Punchy.Fauna.CreatureBrainSystem.BrainLodOff.Data = System.Array.IndexOf(args, "-nobrainlod") >= 0 ? 1 : 0;
            // "-brainlod 60,3" (A-54 #59): the LOD's range (m) and frames per think for this line (default 80 m, every 4th).
            var lodArg = System.Array.IndexOf(args, "-brainlod");
            Punchy.Fauna.CreatureBrainSystem.BrainLod.Data = lodArg >= 0 && lodArg + 1 < args.Length && ParseFloats(args[lodArg + 1]) is float[] lod && lod.Length >= 2
                ? new float2(lod[0], lod[1]) : Punchy.Fauna.CreatureBrainSystem.BrainLodDefault;
            Time.captureFramerate = System.Array.IndexOf(args, "-fixeddt") >= 0 ? 30 : 0;
            fixedFrames = Time.captureFramerate > 0 ? 0 : -1;
            fixedSearch0 = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            // #46/#43 tests: "-flatfear" (plain 3D fear distance), "-nobiometable" (every biome has every sound),
            // "-callrate 1.5" (the call budget a second), "-calllog" (calls played a 10 s window, with the biome).
            Punchy.Fauna.CreatureBrainSystem.HeightFearOff.Data = System.Array.IndexOf(args, "-flatfear") >= 0 ? 1 : 0;
            // A-46: "-oldwander" (#12, walkers ease down while turning, as before); flees are counted from each line's start
            // (logged with the shot, #57's fair check).
            Punchy.Fauna.CreatureBrainSystem.OldWander.Data = System.Array.IndexOf(args, "-oldwander") >= 0 ? 1 : 0;
            // A-46b: "-oldplay" (#12, a playing runner re-aims freely each frame, as before).
            Punchy.Fauna.CreatureBrainSystem.OldPlay.Data = System.Array.IndexOf(args, "-oldplay") >= 0 ? 1 : 0;
            lineSearch0 = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            // "-pickclass Fish" (#68): what one click on the Character tab's class does; "-menu Character": the menu open
            // on that tab for this line's shot (closed again on lines without it).
            var pickAt = System.Array.IndexOf(args, "-pickclass");
            if (pickAt >= 0 && pickAt + 1 < args.Length && System.Enum.TryParse<Punchy.Net.CharacterTab.BodyClass>(args[pickAt + 1], out var pickClass))
            {
                var pickAvatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
                if (pickAvatars != null)
                    Punchy.Net.CharacterTab.PickClass(pickAvatars, pickClass);
            }
            // A-74 (#8): "-pickspecies Elf": the player in that playable body, for race reaction shots. Held for a few
            // seconds once the player exists, since the body saved last session is restored on the first avatar frame.
            var speciesAt = System.Array.IndexOf(args, "-pickspecies");
            if (speciesAt >= 0 && speciesAt + 1 < args.Length)
            {
                speciesPickPending = args[speciesAt + 1];
                speciesPickUntil = -1f;
            }
            var menuAt = System.Array.IndexOf(args, "-menu");
            Punchy.UI.MainMenu.Show(menuAt >= 0 && menuAt + 1 < args.Length && !args[menuAt + 1].StartsWith("-") ? args[menuAt + 1] : null, menuAt >= 0);
            Punchy.Fauna.AmbientSounds.TableOff.Data = System.Array.IndexOf(args, "-nobiometable") >= 0 ? 1 : 0;
            var rateArg = System.Array.IndexOf(args, "-callrate");
            if (rateArg >= 0 && rateArg + 1 < args.Length && float.TryParse(args[rateArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cr))
                Punchy.Audio.CreatureAudioSystem.CallRate = cr;
            callLog = System.Array.IndexOf(args, "-calllog") >= 0;
            callLogNext = Time.time + 10f;
            callLogFrom = Punchy.Audio.CreatureAudioSystem.CallsPlayed;
            // "-voicelog": log cats' and dogs' voices and howls as they play (Z-51); "-stancelog": folk lines with the
            // speaker's stance, and the stance odds per player body (Z-52).
            if (System.Array.IndexOf(args, "-voicelog") >= 0)
                Punchy.Audio.CreatureAudioSystem.LogVoices = true;
            if (System.Array.IndexOf(args, "-stancelog") >= 0)
                FolkTalkSystem.LogStances = true;
            // "-nohud": no readout and no interaction prompt (for screenshots). "-hud": start with the readout on.
            ShowReadout = System.Array.IndexOf(args, "-hud") >= 0;
            // "-meteors": every night a meteor shower (Sky.hlsl's test switch), for checking shooting stars against clouds (Z-09).
            if (System.Array.IndexOf(args, "-meteors") >= 0)
                Shader.SetGlobalFloat("_PunchySkyTest", 1f);
            Punchy.Player.PlayerMovementSystem.NoPushOut.Data = System.Array.IndexOf(args, "-nopushout") >= 0;
            // "-oldlie" (A-42): lying four-legged bodies' legs left where they fall, for before shots from the same build.
            CreatureAnimator.OldLie.Data = System.Array.IndexOf(args, "-oldlie") >= 0 ? 1 : 0;
            // Creature surface detail (A-92, #230), on by default; each costs well under 0.3 ms on a 30-horse herd.
            // "-noseams": limb segments not blended into the one they hang from at the joint.
            CreatureAnimator.NoSeams.Data = System.Array.IndexOf(args, "-noseams") >= 0 ? 1 : 0;
            // "-nofur": no fur strands or coat patches in the creature shader.
            Shader.SetGlobalFloat("_PunchyNoFur", System.Array.IndexOf(args, "-nofur") >= 0 ? 1f : 0f);
            // "-noshells": near bodies without their two fur shells.
            CreatureRenderSystem.Shells = System.Array.IndexOf(args, "-noshells") < 0;
            // "-nosurfaces": creatures without their A-97 surfaces and fur lengths (all medium fur, as before).
            CreatureRenderSystem.Surfaces = System.Array.IndexOf(args, "-nosurfaces") < 0;
            // "-nobloom" (A-96): bloom off in the volumes' own copies of their profiles (the asset is untouched).
            Punchy.Debugging.SparkleProbe.NoBloom(System.Array.IndexOf(args, "-nobloom") >= 0);
            // "-stopnan 0" (R-18): the main camera without URP's Stop NaN guard (a bench arm; hunting NaN by eye).
            var stopNan = System.Array.IndexOf(args, "-stopnan");
            Punchy.CameraRig.NanGuard.Set(!(stopNan >= 0 && stopNan + 1 < args.Length && args[stopNan + 1] == "0"));
            // "-solidsoff" (A-44): wild animals ignore cave walls, towers and carved ground as before, for before numbers
            // from the same build. "-solidstats": log their contacts, sink and floating every 60 s (SolidStatsLog).
            CreatureMotion.SolidsOff.Data = System.Array.IndexOf(args, "-solidsoff") >= 0 ? 1 : 0;
            CreatureMotion.StatsOn.Data = System.Array.IndexOf(args, "-solidstats") >= 0 ? 1 : 0;
            // "-redstar": one red-orange shooting star held mid-flight high in the east, for shots (W-42).
            if (System.Array.IndexOf(args, "-redstar") >= 0)
                Shader.SetGlobalFloat("_PunchySkyTest", 2f);
            if (System.Array.IndexOf(args, "-nohud") >= 0)
            {
                ShowReadout = false;
                noHud = true;
                InteractionPrompt.Hidden = true;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                FrameFlag(args, i);
                if (args[i] == "-time" && Cycle != null && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours))
                    Cycle.TimeOfDay = Mathf.Repeat(hours, 24f);
                if (args[i] == "-watch")
                    watchSpecies = args[i + 1];
                // "-viewpitch 60": the player's own camera looking down from that angle (-20: up), as if the mouse moved.
                if (args[i] == "-viewpitch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewPitch))
                {
                    var rig = FindAnyObjectByType<ThirdPersonCamera>();
                    if (rig != null)
                        rig.SetView(rig.transform.eulerAngles.y, viewPitch);
                }
                // "-view yaw,pitch": the player's own camera turned that way (A-30: aimed at a cave wall).
                if (args[i] == "-view" && ParseFloats(args[i + 1]) is float[] view && view.Length >= 2 && FindAnyObjectByType<ThirdPersonCamera>() is ThirdPersonCamera viewRig)
                    viewRig.SetView(view[0], view[1]);
                // "-watchpitch 45": watch from above (to see over leaves), rather than low or level.
                if (args[i] == "-watchpitch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var watchPitch))
                    watchPitchOverride = watchPitch;
                if (args[i] == "-watchfrom" && ParseFloats(args[i + 1]) is float[] from && from.Length >= 2)
                    watchFrom = new float2(from[0], from[1]);
                // "-walk x,y,z" (A-34): the player walks toward that point from where -goto put it (as if the stick were
                // pushed), so the cave push-out acts on its steps; the shot lands mid-walk at -wait.
                // W-61b: "-walk x,z;x,z;x,z" (or x,y,z points) walks the waypoints in order, one leg at a time.
                if (args[i] == "-walk" && ParseWalkPath(args[i + 1]) is float3 firstLeg)
                {
                    walkTarget = firstLeg;
                    walkEnd = null;
                    walkStillAt = float.MaxValue;
                    walkStillSince = Time.unscaledTime;
                    walkMoves = 0;
                    walkLogNext = 0f;
                    Punchy.Player.PlayerMovementSystem.PushedTotal.Data = 0f;
                }
                // "-untilwork 60" (A-34): after -wait, hold the shot up to that many seconds more until the watched folk
                // has been at its work (Mode Work) for a second, so a smith on a break is caught hammering.
                if (args[i] == "-untilwork" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var untilWorkFor))
                    untilWork = untilWorkFor;
                if (args[i] == "-untilaction" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var untilActionFor))
                    untilAction = untilActionFor;
                // "-timescale 4": everything runs four times as fast (a dusk, a burrow cycle, a roost in seconds).
                if (args[i] == "-timescale" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                    Time.timeScale = Mathf.Clamp(scale, 0.1f, 20f);
                if (args[i] == "-spawn")
                    pendingSpawns.Add(args[i + 1]);
                if (args[i] == "-pose")
                    poseRequest = args[i + 1];
                if (args[i] == "-interact")
                    interactRequest = args[i + 1];
                // "-emote Wave" (Wave, Bow, Cheer, Dance): as if its key were pressed.
                if (args[i] == "-emote" && System.Enum.TryParse<IdleAction>(args[i + 1], true, out var emote) && emote >= IdleAction.Wave)
                    InteractionInputSystem.Requested = emote;
                // "-shotdir C:\shots": tests that take their own screenshots (-interact) save them there.
                if (args[i] == "-shotdir")
                    shotDir = args[i + 1];
                // "-sparkleprobe label" (A-96): count creature pixels that are not a number or over 16 for 360 frames
                // from 3 s on, logged per frame as "[sparkle]" lines, with crops of the first into -shotdir. "-sparklecap 0.5"
                // before it: a lower cap (a check that the probe sees the creatures at all).
                if (args[i] == "-sparkleprobe")
                {
                    var capAt = System.Array.IndexOf(args, "-sparklecap");
                    var sparkleCap = capAt >= 0 && capAt + 1 < args.Length && float.TryParse(args[capAt + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 16f;
                    Punchy.Debugging.SparkleProbe.Begin(args[i + 1], cap: sparkleCap, cropDir: shotDir);
                }
                // "-cam x,y,z,yaw,pitch" or "-camlook x,y,z,tx,ty,tz": the camera put there directly (its follow off till
                // the next shot), for framing a shot exactly.
                if ((args[i] == "-cam" || args[i] == "-camlook") && ParseFloats(args[i + 1]) is float[] c && c.Length >= 5)
                    PoseCamera(new Vector3(c[0], c[1], c[2]), args[i] == "-cam"
                        ? Quaternion.Euler(c[4], c[3], 0f)
                        : Quaternion.LookRotation(new Vector3(c[3], c[4], c.Length > 5 ? c[5] : 0f) - new Vector3(c[0], c[1], c[2])));
                if (args[i] == "-place" && args[i + 1].StartsWith("cave:", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var caveSource))
                    PlaceInCave(caveSource, args[i + 1].Substring(5));
                if (args[i] == "-rockwatch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rockSeconds))
                {
                    rockWatchFor = rockSeconds;
                    rockWatchSince = -1f;
                }
                if (args[i] == "-flyhitch" && int.TryParse(args[i + 1], out var hitchMs))
                    flyHitchMs = hitchMs;
                if (args[i] == "-flycave" && EnsureLocations(out var flySource))
                    StartFlyCave(flySource, args[i + 1]);
                if (args[i] == "-dungeonwalk")
                    StartDungeonWalk(args[i + 1]);
                if (args[i] == "-place" && args[i + 1].StartsWith("batfolk", System.StringComparison.OrdinalIgnoreCase))
                {
                    // (Kept trying until v3 is up, ~90 s in: see PlaceAtBatFolkPending.)
                    batFolkPlaceUntil = Time.unscaledTime + 180f;
                    batFolkPlaceKey = new float3(-1f);
                    batFolkPlaced = false;
                    PlaceAtBatFolkPending();
                }
                else if (args[i] == "-place" && args[i + 1].StartsWith("tree:", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var treeSource))
                    PlaceByTree(treeSource, args[i + 1].Substring(5));
                else if (args[i] == "-place" && args[i + 1].StartsWith("prop:", System.StringComparison.OrdinalIgnoreCase))
                {
                    // (#137: kept until the flora lookup is up, which is built on workers since #102; Update retries it.)
                    propPending = args[i + 1].Substring(5);
                    propPendingUntil = Time.unscaledTime + PropWait;
                    if (EnsureLocations(out var propSource) && PlaceByProp(propSource, propPending))
                        propPending = null;
                }
                else if (args[i] == "-place" && args[i + 1].StartsWith("ladder:", System.StringComparison.OrdinalIgnoreCase))
                {
                    // (A-101 + W-147: a real ladder; waits for the flora lookup, as props do.)
                    ladderPending = args[i + 1].Substring(7);
                    ladderPendingUntil = Time.unscaledTime + PropWait;
                    if (EnsureLocations(out var ladderSource) && PlaceByLadder(ladderSource, ladderPending))
                        ladderPending = null;
                }
                else if (args[i] == "-place" && args[i + 1].StartsWith("cavelava", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var lavaSource))
                    PlaceInCavePool(lavaSource, args[i + 1].Substring(8), lava: true);
                else if (args[i] == "-place" && args[i + 1].StartsWith("cavepool", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var poolSource))
                    PlaceInCavePool(poolSource, args[i + 1].Substring(8));
                else if (args[i] == "-place" && args[i + 1].StartsWith("cavestream", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var streamSource))
                    PlaceByCaveStream(streamSource, args[i + 1].Substring(10));
                else if (args[i] == "-place" && args[i + 1].StartsWith("riverfall", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var fallSource))
                    PlaceByRiverFall(fallSource, args[i + 1].Substring(9));
                else if (args[i] == "-place" && string.Equals(args[i + 1], "meadow", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var meadowSource))
                    PlaceInMeadow(meadowSource);
                else if (args[i] == "-place" && string.Equals(args[i + 1], "crossing", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var crossingSource))
                    PlaceByCrossing(crossingSource);
                else if (args[i] == "-place" && string.Equals(args[i + 1], "clearlake", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var lakeSource))
                    PlaceByClearLake(lakeSource);
                else if (args[i] == "-place" && (string.Equals(args[i + 1], "ledge", System.StringComparison.OrdinalIgnoreCase) || args[i + 1].StartsWith("wall", System.StringComparison.OrdinalIgnoreCase) ||
                                                 args[i + 1].StartsWith("climb", System.StringComparison.OrdinalIgnoreCase)))
                {
                    // ("climb[:grade][:Body]", A-101 row 1: the wall test with a marked band on the wall, grade 2 vines
                    // unless given, 1 a ladder; for the human's climb, whose skill climbs no unmarked wall.)
                    var climbBand = args[i + 1].StartsWith("climb", System.StringComparison.OrdinalIgnoreCase);
                    // ("wall": the same, for a wall-climbing body: the nearest wall of a building or rock, not a rim to mantle.)
                    wallRequest = climbBand || args[i + 1].StartsWith("wall", System.StringComparison.OrdinalIgnoreCase);
                    climbBandGrade = climbBand ? Climbing.Assisted : Climbing.None;
                    if (climbBand)
                    {
                        var parts = args[i + 1].Split(':');
                        for (int k = 1; k < parts.Length; k++)
                            if (byte.TryParse(parts[k], out var bandGrade))
                                climbBandGrade = (byte)math.clamp((int)bandGrade, (int)Climbing.Ladder, (int)Climbing.Most);
                            else
                                SetBody(parts[k]);
                    }
                    // ("wall:Lizard": as that body; A-13 #33.)
                    else if (wallRequest && args[i + 1].IndexOf(':') > 0)
                        SetBody(args[i + 1].Substring(args[i + 1].IndexOf(':') + 1));
                    // To the nearest settlement first: its buildings, wells and stalls have the ledges (PlaceByLedge,
                    // once their tops have loaded round the player).
                    var town = locations.FindIndex(l => l.Name.StartsWith("Nearest settlement"));
                    if (town >= 0)
                    {
                        current = town - 1;
                        Jump(1);
                    }
                    ledgeRequest = true;
                    ledgeRejumped = false;
                    wallVisited.Clear();
                    wallTallest = 0f;
                    wallSearchAt = 0f;
                }
                // P-5/P-7: "-place temple" / "temple:N": on a sunken temple room's floor (for camera shots underground, #85).
                else if (args[i] == "-place" && args[i + 1].StartsWith("temple", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var templeSource))
                    PlaceInTemple(templeSource, args[i + 1]);
                else if (args[i] == "-place")
                {
                    landmarkPlacePending = args[i + 1];
                    landmarkPlaceUntil = Time.unscaledTime + 120f;
                    landmarkPlaceKey = locationsKey;
                    landmarkPlaced = PlaceAtLandmark(args[i + 1]);
                }
            }
            if (inCave && EnsureLocations(out var source))
                GoIntoCave(source);
            // "-dungeonspots" (A-91): logs an entrance and a deepest-room camera spot per dungeon type.
            if (System.Array.IndexOf(args, "-dungeonspots") >= 0)
            {
                dsPending = true;
                dsPendingSince = Time.unscaledTime;
            }
            if (System.Array.IndexOf(args, "-goto") >= 0 || System.Array.IndexOf(args, "-at") >= 0
                || System.Array.IndexOf(args, "-walkershot") >= 0)
                landmarkPlacePending = null;
            // "-goto 120,-340" (x,z, as F3 copies it): there, on the ground (after any -place).
            var goTo = System.Array.IndexOf(args, "-goto");
            if (goTo >= 0 && goTo + 1 < args.Length && EnsureLocations(out var ground) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var goer))
            {
                var parts = args[goTo + 1].Split(',');
                if (parts.Length >= 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gx) &&
                    float.TryParse(parts[^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gz))
                {
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(goer);
                    t.Position = new float3(gx, ground.SampleHeight(new float2(gx, gz)) + 0.1f, gz);
                    // "-goto x,y,z": onto the floor under that point (in a cave), not the ground surface.
                    if (parts.Length >= 3 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gy))
                        t.Position.y = ground.FloorUnder(new float3(gx, gy + 1f, gz), out _) + 0.05f;
                    em.SetComponentData(goer, t);
                    Debug.Log($"DebugControls: to {gx:F0},{t.Position.y:F1},{gz:F0}");
                    // W-67b: the kit boxes there may not be placed yet (tiles still loading after the jump): wait for them.
                    gotoSettle = new float3(gx, parts.Length >= 3 ? t.Position.y - 0.05f : float.NaN, gz);
                    gotoSettleUntil = Time.unscaledTime + 5f;
                }
            }
            // #72 (W-87): "-campsitetest swamp": 9 m west of the test campsite the bake added (CampsiteTest), facing it.
            if (goTo < 0 && System.Array.IndexOf(args, "-campsitetest") >= 0 && !math.any(math.isnan(Punchy.Terrain.CampsiteTest.At)) &&
                EnsureLocations(out var campGround) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var camper))
            {
                var c = Punchy.Terrain.CampsiteTest.At - new float2(9f, 0f);
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(camper);
                t.Position = new float3(c.x, campGround.SampleHeight(c) + 0.1f, c.y);
                t.Rotation = quaternion.LookRotationSafe(new float3(1f, 0f, 0f), math.up());
                em.SetComponentData(camper, t);
                Debug.Log($"DebugControls: beside the test campsite, {Punchy.Terrain.CampsiteTest.LastLog}");
                gotoSettle = new float3(c.x, float.NaN, c.y);
                gotoSettleUntil = Time.unscaledTime + 5f;
            }
            // "-gocave -1669,2494" (x,z): onto the floor at the cave node nearest that spot in plan, at any depth.
            // "-gocave #2": the second-largest cave (by summed chamber volume), at its biggest node.
            var goCave = System.Array.IndexOf(args, "-gocave");
            if (goCave >= 0 && goCave + 1 < args.Length && EnsureLocations(out var goCaveSource) && goCaveSource.Caves.IsCreated &&
                playerQuery.TryGetSingletonEntity<LocalTransform>(out var caver))
                GoToCave(goCaveSource, caver, args[goCave + 1]);
            // Testing toggles, per shot line: "-oldcave" (every cave chunk drawn, managed camera probe, as before W-48),
            // "-nocavewalls" (no cave walls drawn, to price them), "-perflog" (frame costs over the last 3 s of the wait).
            CaveSystem.NoRoomCull = ThirdPersonCamera.OldProbe = System.Array.IndexOf(args, "-oldcave") >= 0;
            CaveSystem.NoWalls = System.Array.IndexOf(args, "-nocavewalls") >= 0;
            // "-nocaveskip" (W-135): from the surface, every unburied cave chunk drawn, as before the opening test.
            CaveSystem.NoSurfaceSkip = System.Array.IndexOf(args, "-nocaveskip") >= 0;
            // "-cavelevel n" (W-135 row 2): every cave chunk built at wall level n (0: 0.5 m, 1: 1 m, 2: 2 m).
            int caveLevelAt = System.Array.IndexOf(args, "-cavelevel");
            if (caveLevelAt >= 0 && caveLevelAt + 1 < args.Length && int.TryParse(args[caveLevelAt + 1], out var caveLevel))
                CaveSystem.ForceLevel = caveLevel;
            // "-oldtrace" (#81): TraceSystem's managed per-creature ground lookups, as before W-66.
            Punchy.Fauna.TraceSystem.OldTrace = System.Array.IndexOf(args, "-oldtrace") >= 0;
            perfLog = System.Array.IndexOf(args, "-perflog") >= 0;
            // "-fishprobe" / "-fishlineup" (A-100): the catch-table probe and the catch line-up (DebugControls.Fish.cs).
            fishProbe = System.Array.IndexOf(args, "-fishprobe") >= 0;
            var lineupArg = System.Array.IndexOf(args, "-fishlineup");
            fishLineup = lineupArg >= 0;
            fishLineupGroup = lineupArg >= 0 && lineupArg + 1 < args.Length && (args[lineupArg + 1] == "small" || args[lineupArg + 1] == "big")
                ? args[lineupArg + 1] : null;
            // "-fishlure approach|nibble|bite|scatter" (A-100 row 2): a bobber in front of the nearest fish, at that moment.
            var lureArg = System.Array.IndexOf(args, "-fishlure");
            fishLure = lureArg >= 0 ? (lureArg + 1 < args.Length && !args[lureArg + 1].StartsWith("-") ? args[lureArg + 1] : "approach") : null;
            // "-skyoff n" (a bit mask, to price the sky's parts): see _PunchySkyOff in Sky.hlsl. Per shot line.
            var skyOffArg = System.Array.IndexOf(args, "-skyoff");
            if (skyOffArg >= 0 && skyOffArg + 1 < args.Length && float.TryParse(args[skyOffArg + 1], out var skyOff))
                Shader.SetGlobalFloat("_PunchySkyOff", skyOff);
            // "-skyfull" / "-lowres half|quarter": the cloud buffer's size from this shot line on (see GraphicsSettings).
            if (System.Array.IndexOf(args, "-skyfull") >= 0)
                Punchy.Rendering.LowResEffectFeature.SetMode("Clouds", Punchy.Rendering.LowResMode.Off);
            var lowResArg = System.Array.IndexOf(args, "-lowres");
            if (lowResArg >= 0 && lowResArg + 1 < args.Length && System.Enum.TryParse<Punchy.Rendering.LowResMode>(args[lowResArg + 1], true, out var lowResMode))
                Punchy.Rendering.LowResEffectFeature.SetMode("Clouds", lowResMode);
            // R-3 A/B: "-depthpriming off|auto|forced", the PC renderer's depth priming from this shot line on (the asset's
            // is Forced; on desktop Auto behaves the same). Set on the live renderer, so the asset never changes.
            var primingArg = System.Array.IndexOf(args, "-depthpriming");
            if (primingArg >= 0 && primingArg + 1 < args.Length &&
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp &&
                urp.GetRenderer(0) is UnityEngine.Rendering.Universal.UniversalRenderer renderer)
            {
                var priming = args[primingArg + 1].ToLowerInvariant();
                renderer.depthPrimingMode = priming == "off" ? UnityEngine.Rendering.Universal.DepthPrimingMode.Disabled
                    : priming == "auto" ? UnityEngine.Rendering.Universal.DepthPrimingMode.Auto : UnityEngine.Rendering.Universal.DepthPrimingMode.Forced;
                Debug.Log($"DebugControls: -depthpriming {renderer.depthPrimingMode}");
            }
            // A-45 A/B toggles: "-oldsearch" (#57, every brain search every frame), "-oldsnapshot" (#56, each reader copies
            // the creatures itself), "-oldtrace" (#55, the ground sampled for every creature in trace range).
            // (-oldsearch also holds from the command line or PlayArgs through every shot-list line: A-55's A/B lost it
            // when the first shot line, without the flag, switched it back off.)
            var oldSearch = System.Array.IndexOf(args, "-oldsearch") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldsearch") >= 0 ? 1 : 0;
            Debug.Log($"DebugControls: -oldsearch {(oldSearch != 0 ? "ON (every brain search every frame)" : "off (staggered searches)")}");
            Punchy.Fauna.CreatureBrainSystem.SearchEveryFrame.Data = oldSearch;
            // #132 A/B: "-oldwalls" places every landmark prop within 15 m for each wall check, as before.
            FloraPlacement.OldWalls.Data = System.Array.IndexOf(args, "-oldwalls") >= 0 ? 1 : 0;
            // A-70 row 1 A/B: "-oldfootprints" places buildings again for every wall check, as before; "-footprintcheck"
            // asks FloraPlacement.Blocked too and counts any disagreement with the baked walls (logged with perf2).
            // (Both also hold from PlayArgs through every shot-list line, as -oldsearch does.)
            Punchy.Fauna.BuildingFootprints.Off.Data = System.Array.IndexOf(args, "-oldfootprints") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldfootprints") >= 0 ? 1 : 0;
            Punchy.Fauna.BuildingFootprints.Check.Data = System.Array.IndexOf(args, "-footprintcheck") >= 0 || System.Array.IndexOf(LaunchArgs, "-footprintcheck") >= 0 ? 1 : 0;
            Punchy.Fauna.CreatureSnapshotSystem.Off = System.Array.IndexOf(args, "-oldsnapshot") >= 0;
            // A-70 row 3 A/B: "-syncsnapshot", the snapshot copied on the main thread (waiting for the brain), as before.
            Punchy.Fauna.CreatureSnapshotSystem.Sync = System.Array.IndexOf(args, "-syncsnapshot") >= 0 || System.Array.IndexOf(LaunchArgs, "-syncsnapshot") >= 0;
            // A-70 row 4 A/B: "-oldposerate", every creature posed every frame with the full capsule and no frustum cull.
            Punchy.Fauna.CreatureRenderSystem.OldPoseRate = System.Array.IndexOf(args, "-oldposerate") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldposerate") >= 0;
            Punchy.Fauna.TraceSystem.OldOrder = System.Array.IndexOf(args, "-oldtrace") >= 0;
            Punchy.Fauna.TraceSystem.Stats = perfLog;
            Punchy.Fauna.ReaderTimes.On = perfLog;
            Punchy.Fauna.CreatureBrainSystem.SpreadOn.Data = perfLog ? 1 : 0;
            CaveSystem.CountStats = ThirdPersonCamera.ProbeStats = perfLog;
            // "-goflat" (A-13 #46): to the nearest dry spot whose ground height spread over a 30 m circle is under 0.5 m
            // (found once, by rings round the start; the spot and its measured spread are logged).
            if (System.Array.IndexOf(args, "-goflat") >= 0 && EnsureLocations(out var flatSource) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var flatGoer))
            {
                // ("-goflat 2": the third such spot, each at least 150 m from the ones before, so a test line gets a
                // fresh herd away from the last line's, A-49 #46.)
                var flatAt = System.Array.IndexOf(args, "-goflat");
                var flatIndex = flatAt + 1 < args.Length && int.TryParse(args[flatAt + 1], out var fi) ? math.max(fi, 0) : 0;
                while (flatSpots.Count <= flatIndex && FindFlatSpot(flatSource, out var foundSpread, flatSpots) is float2 foundFlat)
                {
                    flatSpots.Add(foundFlat);
                    flatSpreads.Add(foundSpread);
                }
                flatSpot = flatSpots.Count > flatIndex ? flatSpots[flatIndex] : (float2?)null;
                flatSpread = flatSpot.HasValue ? flatSpreads[flatIndex] : 0f;
                if (flatSpot.HasValue)
                {
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(flatGoer);
                    var f = flatSpot.Value;
                    t.Position = new float3(f.x, flatSource.SampleHeight(f) + 0.1f, f.y);
                    em.SetComponentData(flatGoer, t);
                    Debug.Log($"DebugControls: flat spot at ({f.x:F0}, {f.y:F0}), ground {flatSource.SampleHeight(f):F1} m, height spread {flatSpread:F2} m over 30 m");
                }
                else
                    Debug.LogWarning("DebugControls: -goflat: no flat spot found");
            }
            // "-at Pool" (any AnchorKind): on the floor by the nearest anchor of that kind (after any -place).
            var at = System.Array.IndexOf(args, "-at");
            if (at >= 0 && at + 1 < args.Length && System.Enum.TryParse<AnchorKind>(args[at + 1], true, out var kind) &&
                EnsureLocations(out var terrain))
                GoToAnchor(terrain, kind);
            // "-warp town 2" (biome, town, cave or landmark; the number defaults to 1): the warp key's Nth target counted
            // from the nearest, as if the key were pressed that many times from here (A-36).
            var warpArg = System.Array.IndexOf(args, "-warp");
            if (warpArg >= 0 && warpArg + 1 < args.Length && WarpCategory(args[warpArg + 1]) is int warpCat && warpCat >= 0)
            {
                var nth = warpArg + 2 < args.Length && int.TryParse(args[warpArg + 2], out var wn) ? math.max(1, wn) : 1;
                warpIndex[warpCat] = -1;
                Warp(warpCat, 1);
                for (int k = 1; k < nth; k++)
                    Warp(warpCat, 1);
            }
            // "-weather Storm" (any WeatherPreset; Clear clears): that weather forced over the player, where it ends up.
            var weatherArg = System.Array.IndexOf(args, "-weather");
            if (weatherArg >= 0 && weatherArg + 1 < args.Length && QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var under) &&
                FindAnyObjectByType<WeatherSystem>() is WeatherSystem weather)
                weather.ForceAt(args[weatherArg + 1], new Vector2(under.Position.x, under.Position.z));
            // "-waterprobe 1" (2, 3; 0 off): the water drawn as its foam, its thickness over the bed, or its alpha.
            var probeArg = System.Array.IndexOf(args, "-waterprobe");
            if (probeArg >= 0 && probeArg + 1 < args.Length && float.TryParse(args[probeArg + 1], out var probe))
                Shader.SetGlobalFloat("_PunchyWaterProbe", probe);
            // "-camrel dx,dy,dz,tx,ty,tz": like -camlook, but both points relative to where the player ends up (after any
            // -place), for framing a place whose coordinates aren't known.
            // A-79: "-shotcam x,y,z,tx,ty,tz" pins the camera from this line on (held by ShotCam); "-shotcam off" frees it.
            var shotCamArg = System.Array.IndexOf(args, "-shotcam");
            if (shotCamArg >= 0 && shotCamArg + 1 < args.Length)
            {
                if (ParseFloats(args[shotCamArg + 1]) is float[] sc && sc.Length >= 6)
                {
                    shotCamFrom = new Vector3(sc[0], sc[1], sc[2]);
                    shotCamTo = new Vector3(sc[3], sc[4], sc[5]);
                    shotCamOn = (shotCamTo - shotCamFrom).sqrMagnitude > 0.01f;
                    Debug.Log($"DebugControls: shotcam {shotCamFrom} looking at {shotCamTo}");
                }
                else
                    shotCamOn = false;
            }
            var camRel = System.Array.IndexOf(args, "-camrel");
            if (camRel >= 0 && camRel + 1 < args.Length && ParseFloats(args[camRel + 1]) is float[] rel && rel.Length >= 6 &&
                QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var origin))
            {
                Vector3 camFrom = (Vector3)origin.Position + new Vector3(rel[0], rel[1], rel[2]);
                Vector3 camTo = (Vector3)origin.Position + new Vector3(rel[3], rel[4], rel[5]);
                PoseCamera(camFrom, Quaternion.LookRotation(camTo - camFrom));
                Debug.Log($"DebugControls: camera {camFrom.x:F1},{camFrom.y:F1},{camFrom.z:F1} looking at {camTo.x:F1},{camTo.y:F1},{camTo.z:F1}");
            }
        }
    }
}
