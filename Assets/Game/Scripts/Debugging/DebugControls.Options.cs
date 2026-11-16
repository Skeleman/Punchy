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
    // DebugControls, test flag state, launch options and pending places. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        // Once per run (statics survive the scene reloads that hosting and joining do).
        static bool launchOptionsApplied;
        /// <summary>"-watch Squirrel": the camera follows the nearest animal of that species (for testing behaviours).</summary>
        static string watchSpecies;
        /// <summary>The -watch target the "no such creature" warning was last given for, so it is said once, not every frame.</summary>
        static string watchWarned;
        static float? watchPitchOverride;
        /// <summary>
        /// "-watchfrom 12,25" (with -watch): the camera stands still instead, 25 m off and 12 m above the ground, turning
        /// to keep the animal in view (the ground over it, for one under the ground: a worm's furrow and mound). It moves
        /// to a new spot only when the animal gets more than twice that far off. (-watch alone follows a buried worm
        /// under the ground.)
        /// </summary>
        static float2? watchFrom;
        static Vector3? watchFromAt;
        /// <summary>"-walk": where the player is walking to on this shot line (A-34), and when its next probe line is due.</summary>
        static float3? walkTarget;
        static float walkLogNext;
        /// <summary>"-walk x,z;x,z;...": the legs still to walk after walkTarget, in order (W-61b; dev/test only).</summary>
        static readonly System.Collections.Generic.List<float3> walkPath = new();
        /// <summary>W-65c: how the current walk ended ("ARRIVED", "STOPPED"), null while it goes on.</summary>
        static string walkEnd;
        /// <summary>W-67: the walker's height last frame (ARRIVED waits for it to stop falling).</summary>
        static float walkLastY;
        /// <summary>
        /// W-67b: a -goto waiting for the kit boxes round its point (x, z; y the explicit height, or NaN), so it can land
        /// on a deck or floor rather than the ground under it, and the -walk holds till then; and when it gives up.
        /// </summary>
        static float3? gotoSettle;
        static float gotoSettleUntil;
        static float2 walkStillAt;
        static float walkStillSince;
        static int walkMoves;
        static int walkLeg;
        /// <summary>"-untilwork": the most seconds a shot waits past -wait for the watched folk to work, and since when it has.</summary>
        static float untilWork;
        static float workSince = -1f;
        /// <summary>"-untilaction" (A-71): the most seconds a shot waits past -wait for the watched one to be doing the
        /// action asked for in "-watch Deer:Pant" (watchAction).</summary>
        static float untilAction;
        static IdleAction watchAction;
        /// <summary>"-spawn Osprey" (or "Trout:6"): a few of a species put near the player once the world is up (testing).</summary>
        static readonly List<string> pendingSpawns = new List<string>();
        /// <summary>"-pose Bat" (or "Bat:fly", "Bat:glide", "Bat:rest", "Bat:hang", "Crow:nest", and a camera pitch: "Bat:glide:70"):
        /// one held beside the player while the camera circles it, for looking at a body (no brain: it stays put,
        /// beating its wings if flying).</summary>
        static string poseRequest;
        Entity posed;
        float posedAt;   // when the -pose specimen appeared (for "scare")
        float2? swimSpot;
        /// <summary>"-interact Cat:carry" (or ":pet"): walks up to the nearest one, pets it or picks it up, and puts it
        /// down again a while later, as if the keys were pressed (alone, hosting or joined).</summary>
        static string interactRequest;
        /// <summary>"-place ledge": waiting for the settlement's props to load round the player, then put by a rim.</summary>
        static bool ledgeRequest, ledgeRejumped;
        /// <summary>
        /// With "-autowalk" after "-place tree:X" or "-place ledge": the player heads at the trunk (climbs it) or over the
        /// rim (jumps at it: mantles) for a while, then back to where it started, over and over, so a client that joins
        /// late still sees a climb (the multiplayer check of NetPlayerLook.Climbing and Rim).
        /// </summary>
        static float2? climbTarget;
        static float2 climbHome;
        static bool climbJumps;
        static float climbSince;
        static string shotDir;
        float interactStep = -1f;
        Entity interactTarget;
        EntityQuery creatureQuery, requestQuery;
        FaunaDirector interactDirector;
        World requestWorld;
        World creatureWorld;
        FaunaDirector watchDirector;
        // Flight check on the watched animal: its steepest climb or descent while airborne (vertical over horizontal speed).
        Vector3 watchLast;
        float watchSteepest;
        int watchAirFrames;
        Entity watched;
        CreatureMode watchMode;
        byte watchStage;
        int watchShots;
        float nextWatchPick;

        /// <summary>
        /// The test flags: the command line, plus in the editor whatever is in Temp/PlayArgs.txt (so Play mode can run
        /// the same unattended tests as a build, without starting another process).
        /// </summary>
        public static string[] LaunchArgs
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
#if UNITY_EDITOR
                const string file = "Temp/PlayArgs.txt";
                if (System.IO.File.Exists(file))
                {
                    var extra = System.IO.File.ReadAllText(file).Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                    var all = new string[args.Length + extra.Length];
                    args.CopyTo(all, 0);
                    extra.CopyTo(all, args.Length);
                    args = all;
                }
#endif
                return args;
            }
        }

#if UNITY_EDITOR
        // Play mode may start without a domain reload: start each run afresh.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            launchOptionsApplied = false;
            watchSpecies = null;
            watchPitchOverride = null;
            watchFrom = null;
            watchFromAt = null;
            walkTarget = null;
            walkPath.Clear();
            flyOn = false;
            flyRoute.Clear();
            flyPending = null;
            propPending = null;
            ladderPending = null;
            climbOnLadder = false;
            rockWatchFor = 0f;
            rockWatchSince = -1f;
            untilWork = 0f;
            untilAction = 0f;
            pendingSpawns.Clear();
            poseRequest = null;
            interactRequest = null;
            ledgeRequest = false;
            ledgeRejumped = false;
            climbTarget = null;
            liftHeight = 0f;
            actionTest = null;
            wallRequest = false;
            climbBandGrade = Climbing.None;
            ClimbBands.Clear();
            ClimbLedges.Clear();
            Punchy.Player.PlayerInputSystem.TestCamera = null;
            shotDir = null;
            shotLines.Clear();
            shotIndex = -1;
            Time.timeScale = 1f;
            laterOptions = null;
        }
#endif

        /// <summary>
        /// Play-mode tests (W-97): test flags applied mid-session, as a shot line applies them ("-weather Rain -time 22"),
        /// on the next frame once the launch options are in.
        /// </summary>
        public static void RunOptions(string line) => laterOptions = line;

        static string laterOptions;

        /// <summary>
        /// For unattended tests: "-time 22" sets the clock, "-place summit" starts at the first preset location whose
        /// name contains the word (the list is logged).
        /// </summary>
        void ApplyLaunchOptions()
        {
            if (launchOptionsApplied || !EnsureLocations(out _))
                return;
            launchOptionsApplied = true;
            Debug.Log("DebugControls: places: " + string.Join(", ", locations.ConvertAll(l => l.Name)));
            ApplyOptions(LaunchArgs);
            // "-shotlist shots.txt": a list of shots to take one after another in this one session (see ShotList).
            var list = System.Array.IndexOf(LaunchArgs, "-shotlist");
            if (list >= 0 && list + 1 < LaunchArgs.Length)
                LoadShotList(LaunchArgs[list + 1]);
        }

        /// <summary>When the latest options were applied (spawns and poses wait for the world round them to settle).</summary>
        float optionsAt;
        /// <summary>How long that settling takes: the world's first build, or (a later shot) the nearby chunks after a move.</summary>
        float settle = 5f;

        // W-30: render counters, logged with each shot of a shot list.
        static Unity.Profiling.ProfilerRecorder drawCalls, batches, tris;
        static BlobAssetReference<ArchipelagoBlob> archipelagoKept;

        /// <summary>
        /// "-place HumanVillage" (by a landmark), "-place HumanVillage:centre" (in its middle) or "-place summit" (the first
        /// place whose name contains the word). False if no such place is on the island yet.
        /// </summary>
        bool PlaceAtLandmark(string name)
        {
            // An exact name first (the landmark itself), else the first that contains it.
            var index = locations.FindIndex(l => string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = locations.FindIndex(l => l.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (index < 0)
            {
                var middle = centres.FindIndex(l => string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase));
                if (middle < 0)
                    return false;
                index = locations.FindIndex(l => l.Name == centres[middle].Name);
                if (index < 0)
                {
                    locations.Add(centres[middle]);
                    index = locations.Count - 1;
                }
            }
            current = index - 1;
            Jump(1);
            Debug.Log($"DebugControls: started at {locations[index].Name}");
            return true;
        }

        /// <summary>
        /// A-74: "-place Landmark[:centre]" again whenever the island's landmarks change, until it times out: one not
        /// found yet (a v2/v3-only type), and one found on the baked first island, whose landmarks the v2/v3 pass can
        /// move (HumanVillage:centre was a bare hillside 130 m from any village). A -goto or -at on the same line holds.
        /// </summary>
        void PlaceAtLandmarkPending()
        {
            if (landmarkPlacePending == null)
                return;
            if (Time.unscaledTime > landmarkPlaceUntil)
            {
                if (!landmarkPlaced)
                    Debug.LogWarning($"DebugControls: -place {landmarkPlacePending} gave up; no such place on the island");
                landmarkPlacePending = null;
                return;
            }
            if (!EnsureLocations(out _) || math.all(locationsKey == landmarkPlaceKey))
                return;
            landmarkPlaceKey = locationsKey;
            landmarkPlaced = PlaceAtLandmark(landmarkPlacePending) || landmarkPlaced;
        }

        /// <summary>
        /// The player entity exists and has left the origin. Player builds can't use TestHooks (editor only), so this
        /// does the same lookup itself.
        /// </summary>
        static bool PlayerPlaced()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return false;
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            return q.TryGetSingleton<LocalTransform>(out var t) && math.lengthsq(t.Position) >= 1e-6f;
        }

        /// <summary>A-74: "-pickspecies Name" once NetAvatars and the player exist, held for 3 s past that.</summary>
        void PickSpeciesPending()
        {
            if (speciesPickPending == null)
                return;
            if (FindAnyObjectByType<Punchy.Net.NetAvatars>() is not Punchy.Net.NetAvatars avatars || !PlayerPlaced())
                return;
            var index = avatars.PlayableSpecies.FindIndex(sp => sp != null && string.Equals(sp.name, speciesPickPending, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: -pickspecies {speciesPickPending}: no such playable body");
                speciesPickPending = null;
                return;
            }
            if (speciesPickUntil < 0f)
                speciesPickUntil = Time.unscaledTime + 3f;
            avatars.SetLocalSpecies(index);
            if (Time.unscaledTime > speciesPickUntil)
                speciesPickPending = null;
        }
    }
}
