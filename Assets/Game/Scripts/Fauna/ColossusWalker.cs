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
    /// A-72 (#40): walks three of the seed-placed colossi (the FungalForest tortoise, the ElectricWastelands rock golem and
    /// the Sonoran walking saguaro) slowly round a loop about the spot <see cref="StoneColossi"/> placed them on. It builds
    /// nothing of its own: it picks up the roots StoneColossi built and moves them.
    /// <para>
    /// The loop is seeded (its own salt, so no landmark draw moves) and checked to stay dry, in the colossus's own biome,
    /// on gentle ground and clear of settlements; where no loop fits, the colossus stands still. Where each one is comes
    /// from the shared clock (_PunchyClock, the server tick; Time.time solo), walking a while and resting a while, so the
    /// host and every client see it at the same place without sending anything.
    /// </para>
    /// <para>
    /// Only the one nearest the camera (within <see cref="AwakeRange"/>) is awake: it moves every frame and its feet land
    /// with a thud, a dust burst and a small camera shake. The others are posed twice a second. Creatures near a walking
    /// one get out of its way (<see cref="CreatureBrainSystem.ColossusZones"/>).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(60)]   // after the camera's own Update, so the shake offset is taken back off first
    public class ColossusWalker : MonoBehaviour
    {
        /// <summary>"-walkershot Saguaro,80[,side|front|back]" (DebugControls): frame that walker from that far off.</summary>
        public static string Shot;
        /// <summary>"-walkerlog": log each walker's loop and place every 10 s, and the host/client clock check.</summary>
        public static bool Log;
        /// <summary>"-walkerphase walk|rest|seconds" (DebugControls, for shots): shift each walker's clock once so it is mid-walk, mid-rest or that far into its cycle.</summary>
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
        }

        static readonly Gait[] Gaits =
        {
            new Gait { Kind = StoneColossi.Kind.GiantTortoise, Slot = 2, Radius = 40f, Speed = 0.6f, Walk = 80f, Rest = 70f, Stride = 3.5f, Bob = 0.25f, Shake = 0.08f, Body = 30f, Height = 26f, Salt = 0xA72Du },
            new Gait { Kind = StoneColossi.Kind.RockGolem, Slot = 3, Radius = 60f, Speed = 1.5f, Walk = 120f, Rest = 45f, Stride = 4.5f, Bob = 0.4f, Shake = 0.12f, Body = 20f, Height = 56f, Salt = 0xA72Bu },
            new Gait { Kind = StoneColossi.Kind.WalkingSaguaro, Slot = 6, Radius = 70f, Speed = 1.1f, Walk = 100f, Rest = 50f, Stride = 4f, Bob = 0.3f, Shake = 0.06f, Body = 16f, Height = 50f, Salt = 0xA725u },
        };

        const float Ramp = 5f, AwakeRange = 600f, FarPeriod = 0.5f, ShakeRange = 120f, ShakeTime = 0.35f;
        const float TownClear = 150f, MaxSlope = 0.35f, Tilt = 8f, SettleTime = 6f;
        const int Checks = 48, Table = 256;
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");

        /// <summary>One walking colossus.</summary>
        class Walker
        {
            public Gait G;
            public Transform Root;
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
        }

        public static ColossusWalker Instance { get; private set; }
        readonly Walker[] walkers = new Walker[3];
        float nextTry, gaveUp;
        ParticleSystem dust;
        Vector3 shakeApplied;
        float shakeAmp, shakeLeft, nextLog;
        int awake = -1;

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
            var zones = float4x4.zero;
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w == null || w.Still || !w.Root.gameObject.activeInHierarchy)
                    continue;
                if (i == awake || Time.time >= w.NextPose)
                {
                    w.NextPose = Time.time + FarPeriod;
                    Pose(w, source, t);
                    if (i == awake)
                    {
                        Footfall(w, eye);
                        Animate(w, t);
                    }
                }
                // Creatures make room only while it is on the move (a resting giant is just a hill).
                if (w.Pace > 0.2f)
                    zones[i] = new float4(w.Position.xz, w.G.Body + 6f, w.Heading);
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

        static bool TryGetSource(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            return query.TryGetSingleton(out source) && source.Settlements.IsCreated;
        }

        /// <summary>Picks up each walking kind's root once StoneColossi has built it, and lays out its loop.</summary>
        void Capture(in TerrainSource source)
        {
            var colossi = FindAnyObjectByType<StoneColossi>();
            if (colossi == null)
                return;
            for (int i = 0; i < Gaits.Length; i++)
            {
                var g = Gaits[i];
                // ("-colossus" can swap another kind into a slot: then that slot stands still.)
                if (walkers[i] != null || StoneColossi.Placed[g.Slot] != g.Kind || colossi.Root(g.Slot) == null)
                    continue;
                var spot = StoneColossi.Spots[g.Slot];
                if (math.any(math.isnan(spot)))
                    continue;
                walkers[i] = Lay(source, g, colossi.Root(g.Slot), spot);
            }
        }

        static Walker Lay(in TerrainSource source, Gait g, Transform root, float3 spot)
        {
            var w = new Walker { G = g, Root = root, Spot = spot, Sink = root.position.y - source.SampleHeight(spot.xz) };
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
            if (g.Slot < 4)
            {
                var keep = FloraPlacement.KeepOut.Data;
                keep[g.Slot] = circle;
                FloraPlacement.KeepOut.Data = keep;
            }
            else
            {
                var keep = FloraPlacement.KeepOutMore.Data;
                keep[g.Slot - 4] = circle;
                FloraPlacement.KeepOutMore.Data = keep;
            }
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
                w.Rig = ColossusRig.For(w.G.Kind, w.Root);
            }
            w.Rig?.Animate(w.Pace, (float)(w.Distance / w.G.Stride), Settled(w.G, t, w.Offset), (float)(t % 3600.0));
        }

        /// <summary>Where a walker stands at clock time t (no ground fit), for the host/client check.</summary>
        static float2 PlaceAt(Walker w, double t) => At(w, Walked(w.G, t, w.Offset, out _));

        void Pose(Walker w, in TerrainSource source, double t)
        {
            var g = w.G;
            if (Phase != w.PhaseDone)
            {
                // -walkerphase (shots only): move this walker's clock once so it is where the shot wants it in its cycle.
                w.PhaseDone = Phase;
                double cycle = g.Walk + g.Rest, u = t + w.Offset;
                var want = Phase == "walk" ? g.Walk * 0.5f : Phase == "rest" ? g.Walk + g.Rest * 0.5f
                         : float.TryParse(Phase, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at0) ? at0 : float.NaN;
                if (!float.IsNaN(want))
                    w.Offset += want - (float)(u - math.floor(u / cycle) * cycle);
            }
            w.Distance = Walked(g, t, w.Offset, out w.Pace);
            var at = At(w, w.Distance);
            var ahead = At(w, w.Distance + 1.0) - At(w, w.Distance - 1.0);
            var fwd = math.normalizesafe(ahead, new float2(0f, 1f));
            w.Heading = math.atan2(fwd.x, fwd.y);
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
            w.Root.SetPositionAndRotation(w.Position, Quaternion.Euler(0f, math.degrees(w.Heading), 0f) * Quaternion.Euler(pitch, 0f, roll));
            StoneColossi.Positions[g.Slot] = new float3(at.x, ground, at.y);
        }

        /// <summary>On each new stride of the awake walker: a deep thud, a puff of dust at the foot and a shake nearby.</summary>
        void Footfall(Walker w, float3 eye)
        {
            var step = (long)math.floor(w.Distance / w.G.Stride);
            var was = w.Step;
            w.Step = step;
            if (was == long.MinValue || step == was || w.Pace < 0.15f)
                return;
            var fwd = new float2(math.sin(w.Heading), math.cos(w.Heading));
            var side = new float2(fwd.y, -fwd.x) * ((step & 1) == 0 ? 1f : -1f);
            var foot2 = w.Position.xz + fwd * (w.G.Body * 0.4f) + side * (w.G.Body * 0.45f);
            var foot = new Vector3(foot2.x, StoneColossi.Positions[w.G.Slot].y + 0.5f, foot2.y);
            GameAudio.PlayOneShot(SoundId.StepRock, foot, 0.3f, w.Pace, 160f, SoundGroup.World);
            Dust(foot, w.G.Body * 0.25f);
            var d = math.distance(eye, (float3)foot);
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
        /// "-walkershot Saguaro,80[,side|front|back]": a camera this far from the named walker, at its side (or ahead, or
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
                eye = new Vector3(foot.x, ground + (distance > 200f ? distance * 0.1f : 4f), foot.y);
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
                          $"{w.Distance % w.Length:F0} of {w.Length:F0} m round, pace {w.Pace:F2}, heading {math.degrees(w.Heading):F0}, clock {t:F1} s{check}");
            }
            Debug.Log($"ColossusWalker: {CreatureBrainSystem.ColossusFlees.Data} creature flees from walking colossi so far");
        }
    }
}
