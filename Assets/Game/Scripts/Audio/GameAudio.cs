using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using Random = UnityEngine.Random;
using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>Volume groups, each with its own level (GameAudio.Volume), so settings can set them later.</summary>
    public enum SoundGroup { Creatures, World, Weather, Player }

    /// <summary>
    /// The game's sound player: one pool of 3D voices (MaxVoices) for everyone's sounds, the bank of synthesised clips
    /// (SoundSynth, made on worker threads at start), and a small static API:
    /// <list type="bullet">
    /// <item>PlayOneShot: a sound once, at a point;</item>
    /// <item>StartLoop / MoveLoop / StopLoop: a sound held on (a hive's drone, a fire's crackle), by handle.</item>
    /// </list>
    /// Voices go to what's loudest where the listener is (volume over distance): when all are busy a new sound takes the
    /// faintest one-shot if it's louder, or is dropped. Sounds beyond their reach aren't played at all. Every machine
    /// plays its own sounds from what it already knows (see CreatureAudioSystem): nothing about sound is sent.
    /// Caves: rock between the listener and a sound keeps it out. Deep in a cave the outside world goes quiet (the
    /// surface's sounds fade with CaveSystem.SurfaceHidden, so near a mouth the outside is still heard), and from above
    /// ground nothing deep in a cave is heard. Underground everything echoes, longer the bigger the cave around the
    /// listener (one reverb zone kept on the listener, sized from the cave's air around it).
    /// Created by itself on load; nothing needs to be in a scene.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public const int MaxVoices = 24;

        /// <summary>Each group's level (0..1), and the master level.</summary>
        public static readonly float[] Volume = { 1f, 1f, 1f, 1f };
        public static float Master = 1f;

        /// <summary>
        /// Whether the game goes quiet while its window isn't in front (on by default; kept on this machine). The game
        /// itself carries on (Run In Background): only the sound fades out, and back in on returning.
        /// </summary>
        public static bool MuteInBackground
        {
            get => PlayerPrefs.GetInt(MuteKey, 1) != 0;
            set { PlayerPrefs.SetInt(MuteKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        const string MuteKey = "Punchy.MuteInBackground";
        static bool focused = true;
        /// <summary>The level everything is heard at, easing to 0 out of focus (AudioListener.volume: every sound, the
        /// weather's too).</summary>
        static float focusLevel = 1f;

        static GameAudio instance;
        readonly Dictionary<SoundId, AudioClip[]> bank = new Dictionary<SoundId, AudioClip[]>();
        System.Threading.Tasks.Task<(SoundId, int, float[])[]> making;

        class Voice
        {
            public AudioSource Source;
            /// <summary>The air between: a far sound loses its top (Setup).</summary>
            public AudioLowPassFilter Air;
            public float Priority;     // volume over distance when it started
            public bool Loop;
            public int Handle;
            public float Volume;
            public SoundGroup Group;
        }

        readonly List<Voice> voices = new List<Voice>();
        int nextHandle = 1;

        /// <summary>The ground, for the caves (set each frame by CreatureAudioSystem once there is one).</summary>
        public static TerrainSource Terrain;
        public static bool HasTerrain;
        AudioReverbZone echo;
        /// <summary>How far, on average, the cave's air reaches from the listener (metres), and when it was last measured.</summary>
        float caveSize = 4f, sizedAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null)
                return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudio>();
        }

        void Awake()
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                var source = new GameObject("Voice " + i).AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1.5f;
                source.maxDistance = 40f;
                var air = source.gameObject.AddComponent<AudioLowPassFilter>();
                air.cutoffFrequency = 22000f;
                voices.Add(new Voice { Source = source, Air = air });
            }
            echo = new GameObject("Cave echo").AddComponent<AudioReverbZone>();
            echo.transform.SetParent(transform, false);
            echo.reverbPreset = AudioReverbPreset.User;
            echo.minDistance = 500f;
            echo.maxDistance = 600f;
            echo.enabled = false;
            // (The whole bank in one go off the main thread: a fraction of a second, ready by the time anything plays.)
            making = System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<(SoundId, int, float[])>();
                foreach (SoundId id in System.Enum.GetValues(typeof(SoundId)))
                    for (int v = 0; v < SoundSynth.Variants(id); v++)
                        list.Add((id, v, SoundSynth.Generate(id, v)));
                return list.ToArray();
            });
        }

        void OnApplicationFocus(bool hasFocus) => focused = hasFocus;

        void Update()
        {
            // Fading out over a quarter second when the window loses focus, back in over half a second.
            var quiet = !focused && MuteInBackground;
            focusLevel = Mathf.MoveTowards(focusLevel, quiet ? 0f : 1f, Time.unscaledDeltaTime * (quiet ? 4f : 2f));
            AudioListener.volume = focusLevel;
            if (making != null && making.IsCompleted)
            {
                foreach (var (id, v, samples) in making.Result)
                {
                    if (!bank.TryGetValue(id, out var clips))
                        bank[id] = clips = new AudioClip[SoundSynth.Variants(id)];
                    var clip = AudioClip.Create(id + " " + v, samples.Length, 1, SoundSynth.Rate, false);
                    clip.SetData(samples, 0);
                    clips[v] = clip;
                }
                making = null;
            }
            Echo();
            // Loops follow the group and master levels as they change.
            foreach (var voice in voices)
                if (voice.Loop && voice.Source.isPlaying)
                    voice.Source.volume = voice.Volume * Volume[(int)voice.Group] * Master;
        }

        void OnDestroy()
        {
            foreach (var clips in bank.Values)
                foreach (var clip in clips)
                    if (clip != null)
                        Destroy(clip);
        }

        /// <summary>
        /// The cave echo, faded in with how far underground the listener is: the decay and the delays of the first
        /// reflections and the echo grow with the cave's size (a passage rings briefly, a cavern booms on), measured twice
        /// a second while underground.
        /// </summary>
        void Echo()
        {
            var under = HasTerrain ? CaveSystem.Underground : 0f;
            echo.enabled = under > 0.01f;
            if (!echo.enabled)
                return;
            var ear = Ear;
            echo.transform.position = ear;
            if (Time.time - sizedAt > 0.5f)
            {
                sizedAt = Time.time;
                caveSize = CaveSize(ear);
            }
            var size = math.saturate((caveSize - 2f) / 18f);   // 0 a tight passage (2 m), 1 a cavern (20 m and up)
            echo.room = Mathf.RoundToInt(Mathf.Lerp(-10000f, -400f, Mathf.Sqrt(under)));
            echo.roomHF = -600;
            echo.decayTime = Mathf.Lerp(0.9f, 5f, size);
            echo.decayHFRatio = 0.6f;
            echo.reflections = Mathf.RoundToInt(Mathf.Lerp(-300f, -900f, size));
            echo.reflectionsDelay = Mathf.Lerp(0.007f, 0.06f, size);
            echo.reverb = Mathf.RoundToInt(Mathf.Lerp(-200f, 300f, size));
            echo.reverbDelay = Mathf.Lerp(0.011f, 0.09f, size);
            echo.diffusion = 100f;
            echo.density = 100f;
        }

        /// <summary>The size the echo was last sized for (metres of cave air around the listener, on average).</summary>
        public static float CaveSizeHeard => instance != null ? instance.caveSize : 0f;

        /// <summary>
        /// How far the cave's air reaches from a point, on average over 14 directions (the axes and the diagonals),
        /// marched through the cave's distance field to the rock (at most 40 m each way).
        /// </summary>
        static float CaveSize(Vector3 at)
        {
            var total = 0f;
            var n = 0;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        var axes = math.abs(x) + math.abs(y) + math.abs(z);
                        if (axes != 1 && axes != 3)
                            continue;
                        var dir = math.normalize(new float3(x, y, z));
                        var t = 0f;
                        for (int step = 0; step < 24 && t < 40f; step++)
                        {
                            var d = Terrain.CaveDistance((float3)at + dir * t);
                            if (d >= 0f)
                                break;
                            t += math.max(-d, 0.3f);
                        }
                        total += math.min(t, 40f);
                        n++;
                    }
            return total / n;
        }

        /// <summary>
        /// How much of a sound at this point gets through to the listener past the caves' rock, 0..1: a sound out on the
        /// surface fades to a murmur as the listener goes underground, and out away from every mouth (SurfaceHidden); one deep in a cave (in its air, more than
        /// a couple of metres down) is only heard from underground. (Either cave: the rock between two isn't worth the
        /// cost of finding.)
        /// </summary>
        public static float ThroughRock(Vector3 at)
        {
            if (!HasTerrain || !Terrain.Caves.IsCreated)
                return 1f;
            var inCave = Terrain.CaveDistance(at) < 0.3f;
            // (Down a cave the outside is only a murmur even by a mouth, and gone away from every mouth.)
            if (!inCave)
                return 1f - math.max(CaveSystem.SurfaceHidden, 0.8f * CaveSystem.Underground);
            var depth = Terrain.SampleHeight(new float2(at.x, at.z)) - at.y;
            return math.max(CaveSystem.Underground, 1f - math.saturate((depth - 2f) / 4f));
        }

        /// <summary>Where sounds are heard from: the listener (the camera).</summary>
        static Vector3 Ear
        {
            get
            {
                var cam = Camera.main;
                return cam != null ? cam.transform.position : Vector3.zero;
            }
        }

        /// <summary>Whether a sound at this point, this loud, reaching this far, would be heard at all.</summary>
        public static bool Audible(Vector3 at, float reach) => instance != null && (at - Ear).sqrMagnitude < reach * reach;

        Voice Take(float priority)
        {
            Voice faintest = null;
            foreach (var voice in voices)
            {
                if (!voice.Source.isPlaying)
                    return voice;
                if (!voice.Loop && (faintest == null || voice.Priority < faintest.Priority))
                    faintest = voice;
            }
            return faintest != null && faintest.Priority < priority ? faintest : null;
        }

        bool Ready(SoundId id, out AudioClip[] clips) => bank.TryGetValue(id, out clips) && clips.Length > 0;

        void Setup(Voice voice, AudioClip clip, Vector3 at, float pitch, float volume, float reach, SoundGroup group)
        {
            var s = voice.Source;
            s.Stop();
            s.clip = clip;
            s.transform.position = at;
            s.pitch = pitch;
            s.maxDistance = reach;
            // (Full volume out to a quarter of its reach, then falling away: heard where it's meant to be heard.)
            s.minDistance = Mathf.Max(1f, reach * 0.25f);
            s.volume = volume * Volume[(int)group] * Master;
            // Far off (past 40 m), muffled as the air muffles it: a wolf's howl across the valley is soft and dull, not
            // crisp, down to a 1.8 kHz top by 200 m.
            var far = Mathf.InverseLerp(40f, 200f, Vector3.Distance(at, Ear));
            voice.Air.cutoffFrequency = Mathf.Lerp(22000f, 1800f, Mathf.Sqrt(far));
            voice.Volume = volume;
            voice.Group = group;
        }

        /// <summary>
        /// Plays a sound once at a point (a random variant), at a pitch and volume, heard out to reach metres. Returns
        /// false if it wasn't played (too far, the bank not made yet, or every voice busy with something louder).
        /// </summary>
        public static bool PlayOneShot(SoundId id, Vector3 at, float pitch = 1f, float volume = 1f, float reach = 40f, SoundGroup group = SoundGroup.Creatures)
        {
            if (instance == null || !instance.Ready(id, out var clips))
                return false;
            var distance = Vector3.Distance(at, Ear);
            if (distance > reach)
                return false;
            volume *= ThroughRock(at);
            if (volume < 0.01f)
                return false;
            var priority = volume / (1f + distance / 10f);
            var voice = instance.Take(priority);
            if (voice == null)
                return false;
            instance.Setup(voice, clips[Random.Range(0, clips.Length)], at, pitch, volume, reach, group);
            voice.Loop = false;
            voice.Priority = priority;
            voice.Source.loop = false;
            voice.Source.Play();
            return true;
        }

        /// <summary>
        /// Starts a sound held on at a point; returns its handle (0 if no voice was free: try again later). Loops keep
        /// their voice until stopped, and aren't taken by one-shots.
        /// </summary>
        public static int StartLoop(SoundId id, Vector3 at, float volume = 1f, float reach = 30f, SoundGroup group = SoundGroup.World, float pitch = 1f)
        {
            if (instance == null || !instance.Ready(id, out var clips))
                return 0;
            var voice = instance.Take(float.MaxValue);
            if (voice == null)
                return 0;
            instance.Setup(voice, clips[0], at, pitch, volume * ThroughRock(at), reach, group);
            voice.Loop = true;
            voice.Priority = float.MaxValue;
            voice.Handle = instance.nextHandle++;
            voice.Source.loop = true;
            voice.Source.time = Random.Range(0f, clips[0].length);
            voice.Source.Play();
            return voice.Handle;
        }

        /// <summary>Moves a loop, and sets its volume.</summary>
        public static void MoveLoop(int handle, Vector3 at, float volume)
        {
            if (instance == null || handle == 0)
                return;
            foreach (var voice in instance.voices)
                if (voice.Loop && voice.Handle == handle)
                {
                    voice.Source.transform.position = at;
                    voice.Volume = volume * ThroughRock(at);
                    voice.Source.volume = voice.Volume * Volume[(int)voice.Group] * Master;
                }
        }

        public static void StopLoop(int handle)
        {
            if (instance == null || handle == 0)
                return;
            foreach (var voice in instance.voices)
                if (voice.Loop && voice.Handle == handle)
                {
                    voice.Source.Stop();
                    voice.Loop = false;
                    voice.Handle = 0;
                }
        }
    }
}
