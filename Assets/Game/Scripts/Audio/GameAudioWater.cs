using System.Collections.Generic;
using Punchy.CameraRig;
using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>
    /// GameAudio under water (#22, A-98): while the camera's lens is in the water (Underwater.LensUnder: the camera, not
    /// the player, so the sound matches the picture) every voice's low-pass drops to a dull 800 Hz at the surface and
    /// 500 Hz ten metres down, its pitch to 0.9, and the world above grows a little quieter the deeper the lens goes.
    /// Eased in and out over WetIn / WetOut seconds; a lens bobbing at the waterline has to stay in (or out) for WetHold
    /// seconds before the sound follows it, so it never flutters. The weather's own sources (thunder, which isn't one of
    /// the pool's voices) are adopted (Adopt) and muffled the same way. Lava needs nothing: nobody swims in it.
    /// Everything here is local to this machine (the camera's own water): nothing is sent.
    /// </summary>
    public partial class GameAudio
    {
        const float WetIn = 0.25f, WetOut = 0.25f, WetHold = 0.12f;
        /// <summary>The water's cutoff at the surface and DeepAt metres down (Hz), and the pitch under it.</summary>
        const float CutShallow = 800f, CutDeep = 500f, DeepAt = 10f, PitchUnder = 0.9f;
        /// <summary>How much quieter the world above is, just under the surface and DeepAt metres down.</summary>
        const float QuietShallow = 0.1f, QuietDeep = 0.4f;

        /// <summary>How far under water the sound is (0 dry .. 1 fully under), eased.</summary>
        static float wet;
        /// <summary>This frame's water cutoff, pitch and gain (22000 / 1 / 1 when dry).</summary>
        static float wetCut = 22000f, wetPitch = 1f, wetGain = 1f;
        /// <summary>Whether the sound has gone under (the lens held under for WetHold), and how long the lens has disagreed.</summary>
        bool wetTarget;
        float disagreeFor;
        /// <summary>The lens's depth under the surface (m), eased so a swell over the lens doesn't jump the cutoff.</summary>
        float wetDepth;
        /// <summary>The depth last logged (m), -1 for out of the water.</summary>
        float loggedDepth = -1f;

        /// <summary>A source outside the pool (the weather's thunder), muffled with the voices.</summary>
        struct Adopted
        {
            public AudioSource Source;
            public AudioLowPassFilter Air;
            public float Volume, Pitch;
            public SoundGroup Group;
        }
        readonly List<Adopted> adopted = new List<Adopted>();

        /// <summary>
        /// Takes a source made elsewhere (WeatherSystem's thunder) under GameAudio's wing: its group's level and the
        /// water's muffle apply to it from now until it's destroyed. Its volume and pitch as set now are its own.
        /// </summary>
        public static void Adopt(AudioSource source, SoundGroup group)
        {
            if (instance == null || source == null)
                return;
            var air = source.GetComponent<AudioLowPassFilter>();
            if (air == null)
                air = source.gameObject.AddComponent<AudioLowPassFilter>();
            var a = new Adopted { Source = source, Air = air, Volume = source.volume, Pitch = source.pitch, Group = group };
            instance.adopted.Add(a);
            instance.ShapeAdopted(a);
        }

        /// <summary>The water's cutoff (Hz), pitch and gain on every voice now (22000 / 1 / 1 when dry).</summary>
        public static (float cutoff, float pitch, float gain, float wet) WaterMuffle => (wet > 0f ? wetCut : 22000f, wetPitch, wetGain, wet);

        /// <summary>Follows the lens in and out of the water, and sets this frame's water cutoff, pitch and gain.</summary>
        void Water()
        {
            var lens = Underwater.LensUnder;
            var dt = Time.unscaledDeltaTime;
            // (Hysteresis in time on top of Underwater's few centimetres: a swell washing over the lens again and again
            // doesn't flutter the sound.)
            disagreeFor = lens != wetTarget ? disagreeFor + dt : 0f;
            if (disagreeFor >= WetHold)
            {
                wetTarget = lens;
                disagreeFor = 0f;
            }
            wet = Mathf.MoveTowards(wet, wetTarget ? 1f : 0f, dt / (wetTarget ? WetIn : WetOut));
            if (lens)
                wetDepth = Mathf.Lerp(wetDepth, Underwater.LensDepth, 1f - Mathf.Exp(-dt / 0.3f));
            var deep = Mathf.Clamp01(wetDepth / DeepAt);
            // (Frequencies blend on a log scale, so the top falls away evenly as the lens goes in.)
            wetCut = 22000f * Mathf.Pow(Mathf.Lerp(CutShallow, CutDeep, deep) / 22000f, wet);
            wetPitch = Mathf.Lerp(1f, PitchUnder, wet);
            wetGain = 1f - wet * Mathf.Lerp(QuietShallow, QuietDeep, deep);
            // (A line each metre the lens moves while fully under, and one on coming out: the swell rocking the lens
            // across a metre mark doesn't repeat it.)
            var logDry = wet <= 0f && loggedDepth >= 0f;
            var logWet = wet >= 1f && (loggedDepth < 0f || Mathf.Abs(wetDepth - loggedDepth) >= 1f);
            if (logDry || logWet)
            {
                loggedDepth = logDry ? -1f : wetDepth;
                Debug.Log(logDry
                    ? "GameAudio: out of the water: cutoff 22000 Hz, pitch 1.00, gain 1.00"
                    : $"GameAudio: under water ({Underwater.LensWater}, {wetDepth:F1} m down): cutoff {wetCut:F0} Hz, pitch {wetPitch:F2}, gain {wetGain:F2}");
            }
            for (int i = adopted.Count - 1; i >= 0; i--)
                if (adopted[i].Source == null)
                    adopted.RemoveAt(i);
                else
                    ShapeAdopted(adopted[i]);
        }

        void ShapeAdopted(in Adopted a)
        {
            a.Air.enabled = wet > 0f;
            a.Air.cutoffFrequency = wetCut;
            a.Source.pitch = a.Pitch * wetPitch;
            a.Source.volume = a.Volume * Volume[(int)a.Group] * wetGain;
        }
    }
}
