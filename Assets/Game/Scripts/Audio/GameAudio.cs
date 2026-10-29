using System.Collections.Generic;
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
    /// Created by itself on load; nothing needs to be in a scene.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public const int MaxVoices = 24;

        /// <summary>Each group's level (0..1), and the master level.</summary>
        public static readonly float[] Volume = { 1f, 1f, 1f, 1f };
        public static float Master = 1f;

        static GameAudio instance;
        readonly Dictionary<SoundId, AudioClip[]> bank = new Dictionary<SoundId, AudioClip[]>();
        System.Threading.Tasks.Task<(SoundId, int, float[])[]> making;

        class Voice
        {
            public AudioSource Source;
            public float Priority;     // volume over distance when it started
            public bool Loop;
            public int Handle;
            public float Volume;
            public SoundGroup Group;
        }

        readonly List<Voice> voices = new List<Voice>();
        int nextHandle = 1;

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
                voices.Add(new Voice { Source = source });
            }
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

        void Update()
        {
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
            instance.Setup(voice, clips[0], at, pitch, volume, reach, group);
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
                    voice.Volume = volume;
                    voice.Source.volume = volume * Volume[(int)voice.Group] * Master;
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
