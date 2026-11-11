using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>
    /// The hum under water (A-98): while the sound is under (GameAudioWater's wet), a looped bed of the sea's low rumble
    /// with the odd far bubble and creak (SoundId.UnderwaterHum) fades in with the muffle and grows a little with depth,
    /// and a burst of bubbles plays when the camera goes in, comes up, or moves fast under water. Both are flat (2D)
    /// sources of their own, outside the pool of MaxVoices, so they never take a voice from the world's sounds. Their
    /// level is HumLevel / BubbleLevel within the World group. Local only, like the rest of GameAudio.
    /// </summary>
    public partial class GameAudio
    {
        /// <summary>The hum's level just under the surface and DeepAt metres down (within the World group).</summary>
        const float HumLevel = 0.45f, HumDeep = 0.6f;
        /// <summary>Bubbles' level, the camera speed under water that stirs them (m/s), and the least time between bursts.</summary>
        const float BubbleLevel = 0.6f, BubbleSpeed = 3f, BubbleGap = 0.6f;

        AudioSource hum, bubbles;
        Vector3 lastEar;
        bool wasUnder, hadEar;
        float bubbleWait;

        /// <summary>The hum's volume now (0 when dry), for the checks.</summary>
        public static float HumVolume => instance != null && instance.hum != null && instance.hum.isPlaying ? instance.hum.volume : 0f;

        AudioSource Flat(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }

        /// <summary>Runs the hum and the bubbles for this frame (after Water has set wet).</summary>
        void Hum()
        {
            var dt = Time.unscaledDeltaTime;
            if (wet > 0f && Ready(SoundId.UnderwaterHum, out var clips))
            {
                if (hum == null)
                {
                    hum = Flat("Underwater hum");
                    hum.loop = true;
                    hum.clip = clips[0];
                }
                var deep = Mathf.Clamp01(wetDepth / DeepAt);
                hum.volume = wet * Mathf.Lerp(HumLevel, HumDeep, deep) * Volume[(int)SoundGroup.World];
                if (!hum.isPlaying)
                {
                    // (A random start each time in, so the same creak doesn't greet every dive.)
                    hum.timeSamples = Random.Range(0, hum.clip.samples);
                    hum.Play();
                }
            }
            else if (hum != null && hum.isPlaying)
                hum.Stop();

            // Bubbles: going in, coming up, and swimming (or the camera swinging) fast under water.
            var ear = Ear;
            var speed = hadEar && dt > 0f ? Vector3.Distance(ear, lastEar) / dt : 0f;
            lastEar = ear;
            hadEar = true;
            bubbleWait -= dt;
            var burst = 0f;
            if (wetTarget != wasUnder)
                burst = 1f;
            else if (wetTarget && speed > BubbleSpeed && speed < 30f && bubbleWait <= 0f)
                burst = Mathf.Lerp(0.35f, 0.8f, Mathf.InverseLerp(BubbleSpeed, BubbleSpeed + 6f, speed));
            wasUnder = wetTarget;
            // (Over 30 m/s it's a jump of the camera, not a swim.)
            if (burst > 0f && Ready(SoundId.Bubbles, out var pops))
            {
                if (bubbles == null)
                    bubbles = Flat("Bubbles");
                bubbles.pitch = Random.Range(0.9f, 1.15f);
                bubbles.PlayOneShot(pops[Random.Range(0, pops.Length)], burst * BubbleLevel * Volume[(int)SoundGroup.World]);
                bubbleWait = BubbleGap * Random.Range(1f, 1.6f);
            }
        }
    }
}
