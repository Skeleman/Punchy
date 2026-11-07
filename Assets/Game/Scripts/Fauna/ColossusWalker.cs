using Punchy.Audio;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-72 (#40): walks four of the seed-placed colossi (the FungalForest tortoise, the ElectricWastelands rock golem, the
    /// Sonoran walking saguaro and, since A-75, the MesaCanyonlands mesa tortoise) slowly round a loop about the spot
    /// <see cref="StoneColossi"/> placed them on. It builds nothing of its own: it picks up the roots StoneColossi built
    /// and moves them.
    /// <para>
    /// The loop is seeded (its own salt, so no landmark draw moves) and checked to stay dry, in the colossus's own biome,
    /// on gentle ground and clear of settlements; where no loop fits, the colossus stands still. Where each one is comes
    /// from the shared clock (_PunchyClock, the server tick; Time.time solo), walking a while and resting a while, so the
    /// host and every client see it at the same place without sending anything.
    /// </para>
    /// <para>
    /// Only the one nearest the camera (within <see cref="AwakeRange"/>) is awake: it moves every frame and its feet land
    /// with a thud, a small camera shake (within <see cref="ShakeRange"/>) and a dust burst (within <see cref="DustRange"/>).
    /// The others are posed twice a second. Creatures near a walking one get out of its way
    /// (<see cref="CreatureBrainSystem.ColossusZones"/>).
    /// </para>
    /// <para>
    /// A-74: when the local player comes within <see cref="LookRange"/> of its body, it eases to a stop and turns its body
    /// and head toward them (root and head only), then walks on. This is local and cosmetic: the stop falls behind the
    /// shared clock by Lag (at most <see cref="MaxLag"/> s), which it walks back off a little faster once the player
    /// leaves, so every peer agrees again within a minute or so. Once Lag is used up it gets bored and ignores the player
    /// until they go away.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(60)]   // after the camera's own Update, so the shake offset is taken back off first
    public class ColossusWalker : MonoBehaviour
    {
        /// <summary>"-walkershot Saguaro,80[,side|front|back]" (DebugControls): frame that walker from that far off.</summary>
        public static string Shot;
        /// <summary>"-walkerlog": log each walker's loop and place every 10 s, and the host/client clock check.</summary>
        public static bool Log;
        /// <summary>"-walkerphase walk|rest|stab|seconds" (DebugControls, for shots): shift each walker's clock once so it is mid-walk, mid-rest or that far into its cycle ("stab" is mid-rest with the heron held at the bottom of a stab).</summary>
        public static string Phase;

        /// <summary>How each kind walks.</summary>
        struct Gait
        {
            public StoneColossi.Kind Kind;
            public int Slot;
            /// <summary>Loop radius (m) before the loop check shrinks it; walking speed (m/s); seconds walking, then resting.</summary>
            public float Radius, Speed, Walk, Rest;
            /// <summary>One footfall per this (m); the footfall's bob (m) and its camera shake right underfoot (m).</summary>
            public float Stride, Bob, Shake;
            /// <summary>Half its footprint (m) and its height (m), for the ground fit, the zone creatures avoid and shots.</summary>
            public float Body, Height;
            public uint Salt;
            /// <summary>A-85: it drifts above the ground (the puffball): no footfalls, thuds, dust or shake.</summary>
            public bool Floats;
        }

        static readonly Gait[] Gaits =
        {
            new Gait { Kind = StoneColossi.Kind.GiantTortoise, Radius = 40f, Speed = 0.6f, Walk = 80f, Rest = 70f, Stride = 3.5f, Bob = 0.25f, Shake = 0.08f, Body = 30f, Height = 26f, Salt = 0xA72Du },
            new Gait { Kind = StoneColossi.Kind.RockGolem, Radius = 60f, Speed = 1.5f, Walk = 120f, Rest = 45f, Stride = 4.5f, Bob = 0.4f, Shake = 0.12f, Body = 20f, Height = 56f, Salt = 0xA72Bu },
            new Gait { Kind = StoneColossi.Kind.WalkingSaguaro, Radius = 70f, Speed = 1.1f, Walk = 100f, Rest = 50f, Stride = 4f, Bob = 0.3f, Shake = 0.06f, Body = 16f, Height = 50f, Salt = 0xA725u },
            // A-75 row 2: the mesa tortoise, the slowest of them (a walking hill), on a short loop with long rests.
            new Gait { Kind = StoneColossi.Kind.MesaTortoise, Radius = 45f, Speed = 0.45f, Walk = 90f, Rest = 80f, Stride = 3.5f, Bob = 0.25f, Shake = 0.1f, Body = 26f, Height = 30f, Salt = 0xA75Du },
            // A-76: the glass heron wades a small loop with long still rests (it stabs at the sand while resting); the lantern stag roams wider.
            new Gait { Kind = StoneColossi.Kind.GlassHeron, Radius = 45f, Speed = 0.8f, Walk = 70f, Rest = 60f, Stride = 4f, Bob = 0.2f, Shake = 0.05f, Body = 10f, Height = 32f, Salt = 0xA76Eu },
            new Gait { Kind = StoneColossi.Kind.LanternStag, Radius = 65f, Speed = 1.3f, Walk = 90f, Rest = 50f, Stride = 5f, Bob = 0.3f, Shake = 0.07f, Body = 14f, Height = 34f, Salt = 0xA757u },
            // A-78: the cinder snail glides (no legs, no rig), slowest of all with barely a bob, and rests long to graze.
            new Gait { Kind = StoneColossi.Kind.CinderSnail, Radius = 40f, Speed = 0.35f, Walk = 120f, Rest = 60f, Stride = 8f, Bob = 0.04f, Shake = 0.03f, Body = 24f, Height = 34f, Salt = 0xA7C5u },
            // A-79: the mire walker creeps on its root skirt (no rig), slow with a heavy sway, pausing long in the fog.
            new Gait { Kind = StoneColossi.Kind.MireWalker, Radius = 36f, Speed = 0.6f, Walk = 90f, Rest = 45f, Stride = 10f, Bob = 0.12f, Shake = 0.05f, Body = 20f, Height = 40f, Salt = 0xA7F1u },
            // A-84 row 3: the walking oak heaves along on its root legs, a slow rolling gait with a big bob, resting long.
            new Gait { Kind = StoneColossi.Kind.WalkingOak, Radius = 50f, Speed = 0.7f, Walk = 80f, Rest = 70f, Stride = 6f, Bob = 0.25f, Shake = 0.08f, Body = 16f, Height = 46f, Salt = 0xA70Au },
            // A-85 row 2: the puffball drifts on the air (it bobs on its own, StoneColossi.SwayPuffball), slow and wide, hanging still a while.
            new Gait { Kind = StoneColossi.Kind.Puffball, Radius = 70f, Speed = 0.5f, Walk = 110f, Rest = 50f, Stride = 10f, Bob = 0f, Shake = 0f, Body = 26f, Height = 44f, Salt = 0xA7B5u, Floats = true },
            // A-85 row 3: the charred treant plods its burnt forest, short strides, groaning, dropping embers behind it (StoneColossi.SwayTreant).
            new Gait { Kind = StoneColossi.Kind.CharredTreant, Radius = 55f, Speed = 0.55f, Walk = 90f, Rest = 60f, Stride = 6f, Bob = 0.2f, Shake = 0.07f, Body = 16f, Height = 40f, Salt = 0xA7D3u },
            new Gait { Kind = StoneColossi.Kind.IceMammoth, Radius = 60f, Speed = 0.6f, Walk = 100f, Rest = 60f, Stride = 7f, Bob = 0.3f, Shake = 0.1f, Body = 20f, Height = 34f, Salt = 0xA7E3u },
        };

        const float Ramp = 5f, AwakeRange = 600f, FarPeriod = 0.5f, ShakeRange = 120f, ShakeTime = 0.35f, DustRange = 300f;
        /// <summary>A-74 stop and look: reach past its body (m), ease time (s), most it may fall behind the clock (s), how much faster it catches up, and how far (degrees) the body and head turn.</summary>
        const float LookRange = 40f, HoldTime = 2f, MaxLag = 30f, CatchUp = 1.5f, BodyTurn = 70f, HeadTurn = 35f;
        const float TownClear = 150f, MaxSlope = 0.35f, Tilt = 8f, SettleTime = 6f;
        const int Checks = 48, Table = 256;
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");

        /// <summary>One walking colossus.</summary>
        class Walker
        {
            public Gait G;
            public Transform Root;
            public Transform Litter;   // A-77: the heron's litter, unparented so it stays put (shown, hidden and destroyed with its colossus here)
            public float3 Spot;
            /// <summary>The loop's centre: the spot itself, or nudged off it when the ground round the spot is too steep.</summary>
            public float2 Centre;
            public float Sink, Radius, P1, P2, Dir, Start, Offset, Length;
            /// <summary>Loop length walked up to each of Table + 1 even steps of angle, for an even pace round it.</summary>
            public float[] Arc;
            public bool Still;
            // This frame's pose.
            public float3 Position;
            public float Heading, Pace, NextPose;
            public double Distance;
            public long Step = long.MinValue;
            /// <summary>Its body animation while it is the awake one (ColossusRigs), made the first time it wakes.</summary>
            public ColossusRig Rig;
            public bool Rigged;
            public string PhaseDone;
            /// <summary>A-74: how stopped it is for the player (0 to 1), how far behind the shared clock (s), and whether it has had enough.</summary>
            public float Hold, Lag;
            public bool Bored;
            /// <summary>The clock at its last pose, its own clock (shared clock less Lag), and its head's turn this frame (degrees).</summary>
            public double Clock = double.NaN, Own;
            public float Look;
        }

        public static ColossusWalker Instance { get; private set; }
        readonly Walker[] walkers = new Walker[Gaits.Length];   // (one per Gait; ColossusZones has room for the four nearest)
        float nextTry, gaveUp;
        ParticleSystem dust;
        Vector3 shakeApplied;
        float shakeAmp, shakeLeft, nextLog;
        int awake = -1;
        EntityQuery playerQuery;
        World playerWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindAnyObjectByType<ColossusWalker>() != null)
                return;
            new GameObject("ColossusWalker").AddComponent<ColossusWalker>();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        void Awake()
        {
            Instance = this;
            CreatureBrainSystem.ColossusZones.Data = float4x4.zero;   // (statics outlive a play with domain reload off)
            CreatureBrainSystem.ColossusFlees.Data = 0;
            Log = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-walkerlog") >= 0;
            gaveUp = Time.realtimeSinceStartup + 120f;
        }

        void OnDestroy()
        {
            CreatureBrainSystem.ColossusZones.Data = float4x4.zero;
            foreach (var w in walkers)
            {
                w?.Rig?.Dispose();   // A-82: a rig still weighing frees its job's arrays
                if (w != null && w.Litter != null)
                    Destroy(w.Litter.gameObject);
            }
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            // Take back last frame's shake before anything reads the camera.
            var cam = Camera.main;
            if (shakeApplied != Vector3.zero && cam != null)
                cam.transform.position -= shakeApplied;
            shakeApplied = Vector3.zero;
            TendLitter();

            if (!TryGetSource(out var source))
                return;
            if (Time.realtimeSinceStartup >= nextTry && Time.realtimeSinceStartup < gaveUp)
            {
                nextTry = Time.realtimeSinceStartup + 1f;
                Capture(source);
            }

            var clock = Shader.GetGlobalFloat(ClockId);
            double t = clock > 0f ? clock : Time.time;
            var eye = cam != null ? (float3)cam.transform.position : float3.zero;
            var viewer = Viewer(eye);
            // The awake one: the nearest walker in view range, the only one moved every frame.
            var was = awake;
            awake = -1;
            var best = AwakeRange * AwakeRange;
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w == null || w.Still || !w.Root.gameObject.activeInHierarchy)
                    continue;
                var d = math.distancesq(w.Position.xz, eye.xz);
                if (d < best)
                {
                    best = d;
                    awake = i;
                }
            }
            if (was != awake && was >= 0 && walkers[was] != null)
                walkers[was].Rig?.Sleep();
            // ColossusZones holds four: the nearest four on the move get them.
            var zones = float4x4.zero;
            var zoneDist = new float4(float.MaxValue);
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w == null || w.Still || !w.Root.gameObject.activeInHierarchy)
                    continue;
                if (i == awake || Time.time >= w.NextPose)
                {
                    w.NextPose = Time.time + FarPeriod;
                    Pose(w, source, t, viewer);
                    if (i == awake)
                    {
                        Footfall(w, eye);
                        Animate(w, t);
                    }
                }
                // Creatures make room only while it is on the move (a resting giant is just a hill).
                if (w.Pace > 0.2f)
                {
                    var zone = new float4(w.Position.xz, w.G.Body + 6f, w.Heading);
                    var d = math.distancesq(w.Position.xz, eye.xz);
                    for (int z = 0; z < 4; z++)
                    {
                        if (d >= zoneDist[z])
                            continue;
                        // Push the farther ones down a place and drop the fifth.
                        (zoneDist[z], d) = (d, zoneDist[z]);
                        (zones[z], zone) = (zone, zones[z]);
                    }
                }
            }
            CreatureBrainSystem.ColossusZones.Data = zones;
            if (Log && Time.time >= nextLog)
            {
                nextLog = Time.time + 10f;
                LogWalkers(source, t);
            }
        }

        void LateUpdate()
        {
            // The footfall shake, on top of wherever the camera put itself this frame (taken back off next Update).
            var cam = Camera.main;
            if (cam == null || shakeLeft <= 0f)
                return;
            shakeLeft -= Time.deltaTime;
            var fade = math.saturate(shakeLeft / ShakeTime);
            var k = Time.time * 40f;
            shakeApplied = new Vector3(Mathf.PerlinNoise(k, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, k) - 0.5f, Mathf.PerlinNoise(k, k * 0.5f) - 0.5f) * (2f * shakeAmp * fade);
            cam.transform.position += shakeApplied;
        }

        /// <summary>Where the local player stands (the camera when there is none yet).</summary>
        float3 Viewer(float3 eye)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return eye;
            if (playerWorld != world)
            {
                playerWorld = world;
                playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerTag>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
            }
            return playerQuery.CalculateEntityCount() == 1 ? playerQuery.GetSingleton<Unity.Transforms.LocalTransform>().Position : eye;
        }

        // (A-83 #62: the terrain query, made once per world rather than made and disposed every frame.)
        static World sourceQueryWorld;
        static EntityQuery sourceQuery;

        static bool TryGetSource(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (sourceQueryWorld != world)
            {
                sourceQueryWorld = world;
                sourceQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            }
            return sourceQuery.TryGetSingleton(out source) && source.Settlements.IsCreated;
        }

        /// <summary>Picks up each walking kind's root once StoneColossi has built it, and lays out its loop.</summary>
        void Capture(in TerrainSource source)
        {
            var colossi = FindAnyObjectByType<StoneColossi>();
            if (colossi == null)
                return;
            for (int i = 0; i < Gaits.Length; i++)
            {
                // A-81: its slot is wherever StoneColossi placed its kind ("-colossus" can swap another kind into
                // that slot: then it isn't placed and nothing walks there).
                Gaits[i].Slot = System.Array.IndexOf(StoneColossi.Placed, Gaits[i].Kind);
                var g = Gaits[i];
                if (walkers[i] != null || g.Slot < 0 || colossi.Root(g.Slot) == null)
                    continue;
                var spot = StoneColossi.Spots[g.Slot];
                if (math.any(math.isnan(spot)))
                    continue;
                walkers[i] = Lay(source, g, colossi.Root(g.Slot), spot);
            }
        }

        /// <summary>
        /// A-77: unparented litter follows its colossus by hand: hidden with -nocolossus, and destroyed (the walker
        /// dropped, so it is laid again) once the colossus root is gone, as when the colossi are rebuilt.
        /// </summary>
        void TendLitter()
        {
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w == null || w.Litter == null)
                    continue;
                if (w.Root == null)
                {
                    Destroy(w.Litter.gameObject);
                    w.Rig?.Dispose();
                    walkers[i] = null;
                    continue;
                }
                if (w.Litter.gameObject.activeSelf == DunesColossus.Hidden)
                    w.Litter.gameObject.SetActive(!DunesColossus.Hidden);
            }
        }

        static Walker Lay(in TerrainSource source, Gait g, Transform root, float3 spot)
        {
            var w = new Walker { G = g, Root = root, Spot = spot, Sink = root.position.y - source.SampleHeight(spot.xz) };
            // A-77 (item 22): the glass heron's shard litter and fulgurite are where its lightning struck, not part of it:
            // left on the ground where they were laid instead of swivelling round with it.
            var litter = g.Kind == StoneColossi.Kind.GlassHeron ? root.Find("Litter") : null;
            if (litter != null)
            {
                litter.SetParent(null, true);
                w.Litter = litter;
            }
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)source.World.Seed ^ g.Salt);
            w.P1 = rng.NextFloat(0f, 2f * math.PI);
            w.P2 = rng.NextFloat(0f, 2f * math.PI);
            w.Dir = rng.NextBool() ? 1f : -1f;
            w.Start = rng.NextFloat(0f, 2f * math.PI);
            w.Offset = rng.NextFloat(0f, g.Walk + g.Rest);
            var biome = source.SampleGround(spot.xz).Biomes.Dominant();
            // Shrink the loop until every check point on it is fit to walk; at each size try it centred on the spot, then
            // nudged off it in eight seeded directions (the spot stays inside the loop). Give up after four shrinks.
            int tries = 0;
            string why = null;
            var turn = rng.NextFloat(0f, 2f * math.PI);
            for (w.Radius = g.Radius; tries < 5; tries++, w.Radius *= 0.8f)
            {
                for (int n = 0; n <= 8; n++)
                {
                    var a = turn + n * math.PI / 4f;
                    w.Centre = spot.xz + (n == 0 ? 0f : 0.6f * w.Radius) * new float2(math.cos(a), math.sin(a));
                    if ((why = Unfit(source, w, biome)) == null)
                        break;
                }
                if (why == null)
                    break;
            }
            // A-75 row 2: cut-up ground (the mesa tortoise's canyonlands) may fit no loop near the spot, so as a last try
            // it looks farther out, 1.2-2.4 radii off in sixteen directions, and walks off to that loop. Only reached when
            // every near loop failed, so the walkers that already fit keep their loops.
            var near = why;
            for (int k = 0; why != null && k < 5; k++)
            {
                w.Radius = g.Radius * math.pow(0.8f, k);
                for (int ring = 0; ring < 3 && why != null; ring++)
                    for (int n = 0; n < 16; n++)
                    {
                        var a = turn + (n + 0.5f * ring) * math.PI / 8f;
                        w.Centre = spot.xz + (1.2f + 0.6f * ring) * w.Radius * new float2(math.cos(a), math.sin(a));
                        if ((why = Unfit(source, w, biome)) == null)
                            break;
                    }
                tries = 5 + k;
            }
            if (why != null)
                why = near;
            if (why != null)
            {
                w.Still = true;
                w.Position = root.position;
                Debug.Log($"ColossusWalker: {g.Kind} stays still: no loop fits round ({spot.x:F0}, {spot.z:F0}) ({why})");
                return w;
            }
            w.Arc = new float[Table + 1];
            var last = LoopPoint(w, 0);
            for (int k = 1; k <= Table; k++)
            {
                var p = LoopPoint(w, k);
                w.Arc[k] = w.Arc[k - 1] + math.distance(last, p);
                last = p;
            }
            w.Length = w.Arc[Table];
            // Flora: widen the keep circle once to take in the whole loop, so it never walks through trees. (Moving the
            // circle with it would replant every streamed tile each time, far too heavy; this is a trampled clearing.)
            var circle = new float4(w.Centre, w.Radius * 1.15f + g.Body, 1f);
            FloraPlacement.SetKeepOut(g.Slot, circle);
            FloraSystem.Replant = true;
            Debug.Log($"ColossusWalker: {g.Kind} walks a {w.Length:F0} m loop (radius {w.Radius:F0} m, {tries} shrinks) round ({w.Centre.x:F0}, {w.Centre.y:F0}), " +
                      $"{g.Speed:F1} m/s, {g.Walk:F0} s walking then {g.Rest:F0} s resting; flora kept {circle.z:F0} m out");
            return w;
        }

        /// <summary>Why this loop can't be walked (null: it can).</summary>
        static string Unfit(in TerrainSource source, Walker w, int biome)
        {
            for (int k = 0; k < Checks; k++)
            {
                var p = LoopPoint(w, k * Table / Checks);
                var g = source.SampleGround(p);
                if (g.WaterDepth >= -1f)
                    return $"water at ({p.x:F0}, {p.y:F0})";
                if (g.Biomes.Dominant() != biome)
                    return $"another biome at ({p.x:F0}, {p.y:F0})";
                var h = g.Height;
                var slope = math.max(math.abs(source.SampleHeight(p + new float2(4f, 0f)) - h), math.abs(source.SampleHeight(p + new float2(0f, 4f)) - h)) / 4f;
                if (slope > MaxSlope)
                    return $"slope {slope:F2} at ({p.x:F0}, {p.y:F0})";
                ref var towns = ref source.Settlements.Value.Settlements;
                for (int s = 0; s < towns.Length; s++)
                    if (math.distance(towns[s].Centre, p) < towns[s].Radius + TownClear + w.G.Body)
                        return $"a settlement near ({p.x:F0}, {p.y:F0})";
            }
            return null;
        }

        /// <summary>The loop at step k of Table: a seeded wobble on a circle, so no two loops look drawn by compass.</summary>
        static float2 LoopPoint(Walker w, float k)
        {
            var a = w.Start + w.Dir * 2f * math.PI * k / Table;
            var r = w.Radius * (1f + 0.10f * math.sin(2f * a + w.P1) + 0.05f * math.sin(3f * a + w.P2));
            return w.Centre + r * new float2(math.cos(a), math.sin(a));
        }

        /// <summary>The loop point this far (m) round it.</summary>
        static float2 At(Walker w, double distance)
        {
            var d = (float)(distance - math.floor(distance / w.Length) * w.Length);
            int lo = 0, hi = Table;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (w.Arc[mid] <= d) lo = mid; else hi = mid;
            }
            var span = w.Arc[hi] - w.Arc[lo];
            return LoopPoint(w, lo + (span > 0f ? (d - w.Arc[lo]) / span : 0f));
        }

        /// <summary>How far round its loop (m) a walker is at clock time t, and how fast it is going (0 resting, 1 full).</summary>
        static double Walked(in Gait g, double t, float offset, out float pace)
        {
            double cycle = g.Walk + g.Rest, u = t + offset;
            var laps = math.floor(u / cycle);
            var c = (float)(u - laps * cycle);
            float d;
            if (c < Ramp) { pace = c / Ramp; d = c * c / (2f * Ramp); }
            else if (c < g.Walk - Ramp) { pace = 1f; d = Ramp * 0.5f + (c - Ramp); }
            else if (c < g.Walk) { var left = g.Walk - c; pace = left / Ramp; d = g.Walk - Ramp - left * left / (2f * Ramp); }
            else { pace = 0f; d = g.Walk - Ramp; }
            return (laps * (g.Walk - Ramp) + d) * g.Speed;
        }

        /// <summary>How far (0 to 1) a resting walker has settled at clock time t: it sinks in once stopped and is up again before it sets off.</summary>
        static float Settled(in Gait g, double t, float offset)
        {
            double cycle = g.Walk + g.Rest, u = t + offset;
            var c = (float)(u - math.floor(u / cycle) * cycle);
            if (c < g.Walk)
                return 0f;
            return math.smoothstep(0f, 1f, math.saturate((c - g.Walk) / SettleTime)) * math.smoothstep(0f, 1f, math.saturate(((float)cycle - c) / SettleTime));
        }

        /// <summary>The awake walker's body (ColossusRigs): limbs, sway and settling, on the pose just set.</summary>
        static void Animate(Walker w, double t)
        {
            if (!w.Rigged)
            {
                w.Rigged = true;
                w.Rig = ColossusRig.For(StoneColossi.RigOf(w.G.Slot), w.Root);
            }
            if (w.Rig == null)
                return;
            w.Rig.Look = w.Look;
            w.Rig.Animate(w.Pace, (float)(w.Distance / w.G.Stride), Settled(w.G, w.Own, w.Offset), (float)(t % 3600.0));
        }

        /// <summary>Where a walker stands at clock time t (no ground fit), for the host/client check.</summary>
        static float2 PlaceAt(Walker w, double t) => At(w, Walked(w.G, t, w.Offset, out _));

        /// <summary>
        /// A-74: eases the walker's stop for a nearby player and keeps its own clock (Own = clock less Lag). Returns the
        /// rate its own clock runs at this frame (0 stopped, 1 shared, above 1 catching up).
        /// </summary>
        static float Hold(Walker w, double t, float3 viewer)
        {
            var dt = double.IsNaN(w.Clock) ? 0f : (float)math.clamp(t - w.Clock, 0.0, 1.0);
            w.Clock = t;
            var gap = math.distance(viewer.xz, w.Position.xz);
            if (w.Lag >= MaxLag - 0.01f)
                w.Bored = true;
            if (gap > w.G.Body + LookRange + 20f)
                w.Bored = false;
            var near = gap < w.G.Body + LookRange && !w.Bored;
            w.Hold = math.saturate(w.Hold + (near ? dt : -dt) / HoldTime);
            var rate = near || w.Hold > 0f ? 1f - w.Hold : w.Lag > 0f ? CatchUp : 1f;
            w.Lag = math.clamp(w.Lag + (1f - rate) * dt, 0f, MaxLag);
            w.Own = t - w.Lag;
            return rate;
        }

        static float DeltaAngle(float a, float b) => math.atan2(math.sin(b - a), math.cos(b - a));

        void Pose(Walker w, in TerrainSource source, double t, float3 viewer)
        {
            var g = w.G;
            var rate = Hold(w, t, viewer);
            t = w.Own;
            if (Phase != w.PhaseDone)
            {
                // -walkerphase (shots only): move this walker's clock once so it is where the shot wants it in its cycle.
                w.PhaseDone = Phase;
                double cycle = g.Walk + g.Rest, u = t + w.Offset;
                var want = Phase == "walk" ? g.Walk * 0.5f : (Phase == "rest" || Phase == "stab") ? g.Walk + g.Rest * 0.5f
                         : float.TryParse(Phase, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at0) ? at0 : float.NaN;
                if (!float.IsNaN(want))
                    w.Offset += want - (float)(u - math.floor(u / cycle) * cycle);
            }
            w.Distance = Walked(g, t, w.Offset, out w.Pace);
            var at = At(w, w.Distance);
            var ahead = At(w, w.Distance + 1.0) - At(w, w.Distance - 1.0);
            var fwd = math.normalizesafe(ahead, new float2(0f, 1f));
            w.Heading = math.atan2(fwd.x, fwd.y);
            // A-74: slowed by the stop, and turned toward the player: the body takes most of it, the head the rest.
            w.Pace *= math.min(rate, 1f);
            var toward = viewer.xz - at;
            var aim = math.lengthsq(toward) > 1f ? math.degrees(DeltaAngle(w.Heading, math.atan2(toward.x, toward.y))) * w.Hold : 0f;
            var turn = math.clamp(aim * 0.6f, -BodyTurn, BodyTurn);
            w.Look = math.clamp(aim - turn, -HeadTurn, HeadTurn);
            // Fit to the ground under its whole footprint: the mean height, tilted (a little) with the slope.
            var side = new float2(fwd.y, -fwd.x);
            var reach = g.Body * 0.5f;
            float h0 = source.SampleHeight(at), hf = source.SampleHeight(at + fwd * reach), hb = source.SampleHeight(at - fwd * reach),
                  hr = source.SampleHeight(at + side * reach), hl = source.SampleHeight(at - side * reach);
            var ground = (2f * h0 + hf + hb + hl + hr) / 6f;
            var pitch = math.clamp(math.degrees(math.atan2(hb - hf, 2f * reach)), -Tilt, Tilt);
            var roll = math.clamp(math.degrees(math.atan2(hr - hl, 2f * reach)), -Tilt, Tilt);
            // A footfall's dip, only while walking.
            var stride = (float)(w.Distance / g.Stride);
            var bob = -g.Bob * w.Pace * math.abs(math.sin(math.PI * (stride - math.floor(stride))));
            w.Position = new float3(at.x, ground + w.Sink + bob, at.y);
            w.Root.SetPositionAndRotation(w.Position, Quaternion.Euler(0f, math.degrees(w.Heading) + turn, 0f) * Quaternion.Euler(pitch, 0f, roll));
            StoneColossi.Positions[g.Slot] = new float3(at.x, ground, at.y);
        }

        /// <summary>On each new stride of the awake walker: a deep thud, a puff of dust at the foot and a shake nearby.</summary>
        void Footfall(Walker w, float3 eye)
        {
            var step = (long)math.floor(w.Distance / w.G.Stride);
            var was = w.Step;
            w.Step = step;
            if (was == long.MinValue || step == was || w.Pace < 0.15f || w.G.Floats)
                return;
            var fwd = new float2(math.sin(w.Heading), math.cos(w.Heading));
            var side = new float2(fwd.y, -fwd.x) * ((step & 1) == 0 ? 1f : -1f);
            var foot2 = w.Position.xz + fwd * (w.G.Body * 0.4f) + side * (w.G.Body * 0.45f);
            var foot = new Vector3(foot2.x, StoneColossi.Positions[w.G.Slot].y + 0.5f, foot2.y);
            GameAudio.PlayOneShot(SoundId.StepRock, foot, 0.3f, w.Pace, 160f, SoundGroup.World);
            var d = math.distance(eye, (float3)foot);
            if (d < DustRange)
                Dust(foot, w.G.Body * 0.25f);
            if (d < ShakeRange)
            {
                var amp = w.G.Shake * (1f - d / ShakeRange) * (1f - d / ShakeRange) * w.Pace;
                if (amp > shakeAmp * math.saturate(shakeLeft / ShakeTime))
                {
                    shakeAmp = amp;
                    shakeLeft = ShakeTime;
                }
            }
        }

        void Dust(Vector3 at, float spread)
        {
            if (dust == null)
                dust = MakeDust();
            var e = new ParticleSystem.EmitParams();
            for (int i = 0; i < 8; i++)
            {
                var a = i * 0.785f + UnityEngine.Random.value * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                e.position = at + dir * (spread * UnityEngine.Random.value);
                e.velocity = dir * UnityEngine.Random.Range(1.5f, 3.5f) + Vector3.up * UnityEngine.Random.Range(0.4f, 1.2f);
                e.startSize = UnityEngine.Random.Range(2.5f, 5f);
                e.startLifetime = UnityEngine.Random.Range(2.5f, 4f);
                dust.Emit(e, 1);
            }
        }

        ParticleSystem MakeDust()
        {
            var go = new GameObject("FootfallDust");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.50f, 0.44f, 0.35f), new Color(0.64f, 0.59f, 0.52f, 0.45f));
            main.gravityModifier = -0.02f;
            var emission = ps.emission;
            emission.enabled = false;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            if (ambience != null && ambience.PuffMaterial != null)
                renderer.sharedMaterial = ambience.PuffMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// "-walkershot Saguaro,80[,side|front|back[,lift]]": a camera this far from the named walker, at its side (or ahead, or
        /// behind), looking at its middle, and where the player should stand for the island to stream in round it.
        /// </summary>
        public static bool ShotPose(string spec, out Vector3 eye, out Quaternion rotation, out float3 stand)
        {
            eye = default;
            rotation = Quaternion.identity;
            stand = default;
            if (Instance == null || string.IsNullOrEmpty(spec) || !TryGetSource(out var source))
                return false;
            var parts = spec.Split(',');
            var distance = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 80f;
            var view = parts.Length > 2 ? parts[2] : "side";
            foreach (var w in Instance.walkers)
            {
                if (w == null || !w.G.Kind.ToString().StartsWith(parts[0], System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var fwd = new float2(math.sin(w.Heading), math.cos(w.Heading));
                var dir = view == "front" ? fwd : view == "back" ? -fwd : new float2(fwd.y, -fwd.x);
                var foot = w.Position.xz + dir * distance;
                var ground = source.SampleHeight(foot);
                var middle = new Vector3(w.Position.x, w.Position.y - w.Sink + w.G.Height * 0.45f, w.Position.z);
                // An optional fourth part lifts the eye that far over the ground or the walker's feet, whichever is higher (A-75: the mesa tortoise's canyon walls hide it from 4 m).
                var lift = parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h
                    : distance > 200f ? distance * 0.1f : 4f;
                eye = new Vector3(foot.x, (parts.Length > 3 ? math.max(ground, w.Position.y) : ground) + lift, foot.y);
                rotation = Quaternion.LookRotation(middle - eye);
                stand = new float3(foot.x, ground + 0.1f, foot.y);
                return true;
            }
            return false;
        }

        /// <summary>-walkerlog: each walker's state, the flees it caused, and its place by the server's and the client's clock.</summary>
        void LogWalkers(in TerrainSource source, double t)
        {
            var server = double.NaN;
            var client = double.NaN;
            foreach (var world in World.All)
            {
                if (!world.IsCreated || !(world.IsServer() || world.IsClient()))
                    continue;
                using var timeQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>());
                using var rateQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ClientServerTickRate>());
                if (!timeQuery.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid)
                    continue;
                var hz = rateQuery.TryGetSingleton(out ClientServerTickRate rate) && rate.SimulationTickRate > 0 ? rate.SimulationTickRate : 60;
                var seconds = (time.ServerTick.TickIndexForValidTick + (double)time.ServerTickFraction) / hz;
                if (world.IsServer()) server = seconds; else client = seconds;
            }
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w == null)
                    continue;
                if (w.Still)
                {
                    Debug.Log($"ColossusWalker: {w.G.Kind} still");
                    continue;
                }
                var check = "";
                if (!double.IsNaN(server) && !double.IsNaN(client))
                {
                    var gap = math.distance(PlaceAt(w, server), PlaceAt(w, client));
                    check = $", host/client clocks {server:F2}/{client:F2} s, places {gap:F2} m apart";
                }
                Debug.Log($"ColossusWalker: {w.G.Kind}{(i == awake ? " (awake)" : "")} at ({w.Position.x:F1}, {w.Position.y:F1}, {w.Position.z:F1}), " +
                          $"{w.Distance % w.Length:F0} of {w.Length:F0} m round, pace {w.Pace:F2}, heading {math.degrees(w.Heading):F0}, hold {w.Hold:F2} lag {w.Lag:F1} s look {w.Look:F0}{(w.Bored ? " bored" : "")}, clock {t:F1} s{check}");
            }
            Debug.Log($"ColossusWalker: {CreatureBrainSystem.ColossusFlees.Data} creature flees from walking colossi so far");
        }
    }
}
