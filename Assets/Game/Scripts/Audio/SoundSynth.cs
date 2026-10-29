using System;
using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>The game's synthesised sounds (GameAudio's bank). Append only: the world session's code names them too.</summary>
    public enum SoundId
    {
        BeeBuzz, Chirp, Song, Owl, Crickets, Howl, Step, StepWet, Clop, Cluck, Draw, Wingbeat,
        // For the world session (fires, falls, caves, towns, mines):
        FireCrackle, TorchHiss, WaterfallRush, Splash, CaveDrip, TownMurmur, MineAmbience
    }

    /// <summary>
    /// Makes every sound in the bank from scratch (the project has no recordings), as WeatherSystem's thunder is made:
    /// pure computation on float arrays, safe on a worker thread; GameAudio wraps them in clips on the main thread.
    /// Most sounds come in a few variants (a different seed each), picked at random when played. Loops are built to
    /// repeat seamlessly (whole cycles of everything in them).
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 22050;

        /// <summary>How many variants of each sound, and whether it loops.</summary>
        public static int Variants(SoundId id) => id switch
        {
            SoundId.Chirp => 6, SoundId.Song => 3, SoundId.Owl => 2, SoundId.Howl => 2, SoundId.Step => 4, SoundId.StepWet => 3,
            SoundId.Clop => 3, SoundId.Cluck => 3, SoundId.Draw => 2, SoundId.Wingbeat => 2, SoundId.Splash => 3, SoundId.CaveDrip => 4, _ => 1
        };

        public static bool Loops(SoundId id) => id == SoundId.BeeBuzz || id == SoundId.Crickets || id == SoundId.FireCrackle ||
                                                 id == SoundId.TorchHiss || id == SoundId.WaterfallRush || id == SoundId.TownMurmur ||
                                                 id == SoundId.MineAmbience;

        public static float[] Generate(SoundId id, int variant)
        {
            var rng = new System.Random(((int)id + 1) * 7919 + variant * 104729);
            return id switch
            {
                SoundId.BeeBuzz => Bees(rng),
                SoundId.Chirp => Chirp(rng),
                SoundId.Song => Song(rng),
                SoundId.Owl => Owl(rng),
                SoundId.Crickets => Crickets(rng),
                SoundId.Howl => Howl(rng),
                SoundId.Step => Step(rng, false),
                SoundId.StepWet => Step(rng, true),
                SoundId.Clop => Clop(rng),
                SoundId.Cluck => Cluck(rng),
                SoundId.Draw => Draw(rng),
                SoundId.Wingbeat => Wingbeat(rng),
                SoundId.FireCrackle => Fire(rng, false),
                SoundId.TorchHiss => Fire(rng, true),
                SoundId.WaterfallRush => Waterfall(rng),
                SoundId.Splash => Splash(rng),
                SoundId.CaveDrip => Drip(rng),
                SoundId.TownMurmur => Murmur(rng),
                SoundId.MineAmbience => Mine(rng),
                _ => new float[1]
            };
        }

        // ------------------------------------------------------------------ building blocks

        static float R(System.Random rng) => (float)rng.NextDouble();
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Noise(System.Random rng) => R(rng) * 2f - 1f;

        /// <summary>A one-pole low-pass coefficient for a cutoff in Hz.</summary>
        static float Pole(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);

        /// <summary>Attack-then-exponential-decay envelope at time t (s).</summary>
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        /// <summary>A two-pole band-pass (state variable filter), kept by the caller between samples.</summary>
        struct Band
        {
            float low, band;
            public float Run(float x, float hz, float q)
            {
                var f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(hz, Rate * 0.2f) / Rate);
                low += f * band;
                var high = x - low - band / q;
                band += f * high;
                return band;
            }
        }

        /// <summary>
        /// A seamless loop from a texture with nothing periodic in it (noise): made fade samples longer than wanted, its
        /// tail crossfaded (equal power) over its head, and the tail dropped.
        /// </summary>
        static float[] Seamless(float[] s, int fade)
        {
            var n = s.Length - fade;
            var o = new float[n];
            System.Array.Copy(s, o, n);
            for (int i = 0; i < fade; i++)
            {
                var u = i / (float)fade;
                o[i] = s[i] * Mathf.Sin(u * Mathf.PI * 0.5f) + s[n + i] * Mathf.Cos(u * Mathf.PI * 0.5f);
            }
            return o;
        }

        static float[] Normalise(float[] s, float peak)
        {
            var max = 1e-6f;
            foreach (var v in s)
                max = Mathf.Max(max, Mathf.Abs(v));
            for (int i = 0; i < s.Length; i++)
                s[i] *= peak / max;
            return s;
        }

        // ------------------------------------------------------------------ the sounds

        /// <summary>
        /// A swarm's drone, 2 s looped: a few dozen bees, each a buzzy tone (odd and even harmonics) near 230 Hz whose
        /// pitch flutters as its wings do; the flutter and every tone fit whole cycles in the loop, so it repeats seamlessly.
        /// </summary>
        static float[] Bees(System.Random rng)
        {
            const float seconds = 2f;
            var n = (int)(seconds * Rate);
            var s = new float[n];
            for (int b = 0; b < 24; b++)
            {
                var baseHz = Mathf.Round(Lerp(190f, 270f, R(rng)) * seconds) / seconds;
                var flutterHz = Mathf.Round(Lerp(3f, 9f, R(rng)) * seconds) / seconds;
                var depth = Lerp(4f, 14f, R(rng));
                var near = Lerp(0.3f, 1f, R(rng));
                var phase = 0.0;
                var off = R(rng) * 6.28f;
                for (int i = 0; i < n; i++)
                {
                    var t = i / (float)Rate;
                    var hz = baseHz + depth * Mathf.Sin(2f * Mathf.PI * flutterHz * t + off);
                    phase += hz / Rate;
                    var p = (float)(phase - Math.Floor(phase));
                    // A soft sawtooth: buzzy but not harsh.
                    s[i] += near * (p * 2f - 1f) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * flutterHz * 2f * t + off));
                }
            }
            // Darken it: two passes of a low-pass at ~1.4 kHz.
            var a = Pole(1400f);
            float y1 = 0f, y2 = 0f;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    y1 += a * (s[i] - y1);
                    y2 += a * (y1 - y2);
                    s[i] = y2;
                }
            return Normalise(s, 0.6f);
        }

        /// <summary>A short call: two to six quick whistled notes, each a fast pitch sweep up or down (FM on a sine).</summary>
        static float[] Chirp(System.Random rng)
        {
            var notes = 2 + rng.Next(5);
            var noteLength = Lerp(0.04f, 0.11f, R(rng));
            var gap = Lerp(0.02f, 0.08f, R(rng));
            var baseHz = Lerp(2600f, 4800f, R(rng));
            var sweep = Lerp(-1600f, 1800f, R(rng));
            var s = new float[(int)((noteLength + gap) * notes * Rate) + Rate / 20];
            var phase = 0.0;
            for (int k = 0; k < notes; k++)
            {
                var start = (int)(k * (noteLength + gap) * Rate);
                var hz0 = baseHz * Lerp(0.85f, 1.15f, R(rng));
                for (int i = 0; i < noteLength * Rate && start + i < s.Length; i++)
                {
                    var u = i / (noteLength * Rate);
                    var hz = hz0 + sweep * u + 180f * Mathf.Sin(u * 40f);
                    phase += hz / Rate;
                    var env = Mathf.Sin(Mathf.PI * u);
                    s[start + i] += env * (Mathf.Sin(2f * Mathf.PI * (float)phase) + 0.15f * Mathf.Sin(4f * Mathf.PI * (float)phase));
                }
            }
            return Normalise(s, 0.5f);
        }

        /// <summary>A song: a phrase of chirps, rising and falling, a second or two long.</summary>
        static float[] Song(System.Random rng)
        {
            var parts = 3 + rng.Next(4);
            var list = new System.Collections.Generic.List<float>();
            for (int p = 0; p < parts; p++)
            {
                list.AddRange(Chirp(rng));
                var rest = (int)(Lerp(0.03f, 0.15f, R(rng)) * Rate);
                for (int i = 0; i < rest; i++)
                    list.Add(0f);
            }
            return Normalise(list.ToArray(), 0.5f);
        }

        /// <summary>An owl: two or three soft, breathy hoots near 380 Hz, the last longer and falling.</summary>
        static float[] Owl(System.Random rng)
        {
            var hoots = 2 + rng.Next(2);
            var hz0 = Lerp(330f, 420f, R(rng));
            var s = new float[(int)(Rate * (hoots * 0.55f + 0.4f))];
            var phase = 0.0;
            var lp = 0f;
            for (int k = 0; k < hoots; k++)
            {
                var last = k == hoots - 1;
                var len = last ? 0.55f : 0.28f;
                var start = (int)(k * 0.5f * Rate);
                for (int i = 0; i < len * Rate && start + i < s.Length; i++)
                {
                    var u = i / (len * Rate);
                    var hz = hz0 * (last ? Lerp(1f, 0.88f, u) : 1f);
                    phase += hz / Rate;
                    lp += Pole(900f) * (Noise(rng) - lp);
                    var env = Mathf.Sin(Mathf.PI * Mathf.Min(u * 1.5f, 1f)) * (last ? 1f - u * 0.4f : 1f);
                    s[start + i] += env * (Mathf.Sin(2f * Mathf.PI * (float)phase) + 0.25f * lp);
                }
            }
            return Normalise(s, 0.45f);
        }

        /// <summary>
        /// A field of crickets, 2 s looped: several, each chirping at its own steady rate (whole chirps in the loop), each
        /// chirp a few quick pulses of a pure tone near 4.5 kHz.
        /// </summary>
        static float[] Crickets(System.Random rng)
        {
            const float seconds = 2f;
            var n = (int)(seconds * Rate);
            var s = new float[n];
            for (int c = 0; c < 6; c++)
            {
                var hz = Lerp(4000f, 5200f, R(rng));
                var chirps = 2 + rng.Next(4);   // per loop
                var pulses = 2 + rng.Next(3);
                var level = Lerp(0.3f, 1f, R(rng));
                var off = R(rng);
                for (int k = 0; k < chirps; k++)
                {
                    var at = (int)(((k + off) / chirps % 1f) * n);
                    for (int p = 0; p < pulses; p++)
                    {
                        var start = at + (int)(p * 0.03f * Rate);
                        var len = (int)(0.018f * Rate);
                        for (int i = 0; i < len; i++)
                        {
                            var u = i / (float)len;
                            s[(start + i) % n] += level * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(2f * Mathf.PI * hz * (start + i) / Rate);
                        }
                    }
                }
            }
            return Normalise(s, 0.35f);
        }

        /// <summary>A wolf's howl: a slow glide up to a long held note and down again, with vibrato and a breathy edge.</summary>
        static float[] Howl(System.Random rng)
        {
            var seconds = Lerp(2.6f, 3.6f, R(rng));
            var low = Lerp(280f, 340f, R(rng));
            var high = low * Lerp(1.7f, 2.1f, R(rng));
            var n = (int)(seconds * Rate);
            var s = new float[n];
            var phase = 0.0;
            var band = new Band();
            for (int i = 0; i < n; i++)
            {
                var u = i / (float)n;
                var shape = u < 0.25f ? Mathf.SmoothStep(0f, 1f, u / 0.25f) : u < 0.75f ? 1f : Mathf.SmoothStep(1f, 0.75f, (u - 0.75f) / 0.25f);
                var hz = Lerp(low, high, shape) * (1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 5.5f * u * seconds));
                phase += hz / Rate;
                var p = 2f * Mathf.PI * (float)phase;
                var tone = Mathf.Sin(p) + 0.35f * Mathf.Sin(2f * p) + 0.12f * Mathf.Sin(3f * p);
                var breath = band.Run(Noise(rng), hz * 2f, 3f) * 0.3f;
                var env = Mathf.SmoothStep(0f, 1f, Mathf.Min(u / 0.12f, 1f)) * Mathf.SmoothStep(0f, 1f, Mathf.Min((1f - u) / 0.2f, 1f));
                s[i] = env * (tone + breath);
            }
            return Normalise(s, 0.6f);
        }

        /// <summary>A footstep: a soft thud and a brush of grit (low-passed noise), or on wet ground a splash (brighter).</summary>
        static float[] Step(System.Random rng, bool wet)
        {
            var n = (int)((wet ? 0.18f : 0.12f) * Rate);
            var s = new float[n];
            var cut = wet ? Lerp(2200f, 3200f, R(rng)) : Lerp(500f, 900f, R(rng));
            var a = Pole(cut);
            var lp = 0f;
            var thudHz = Lerp(70f, 110f, R(rng));
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                lp += a * (Noise(rng) - lp);
                var grit = lp * Env(t, 0.004f, wet ? 0.05f : 0.025f);
                var thud = Mathf.Sin(2f * Mathf.PI * thudHz * t) * Env(t, 0.002f, 0.02f) * (wet ? 0.3f : 0.8f);
                var burble = wet ? Mathf.Sin(2f * Mathf.PI * Lerp(600f, 900f, t * 8f) * t) * Env(t, 0.03f, 0.04f) * 0.25f : 0f;
                s[i] = grit + thud + burble;
            }
            return Normalise(s, 0.5f);
        }

        /// <summary>A hoof on hard ground: a woody click with a short hollow body.</summary>
        static float[] Clop(System.Random rng)
        {
            var n = (int)(0.12f * Rate);
            var s = new float[n];
            var band = new Band();
            var body = Lerp(260f, 380f, R(rng));
            var click = Lerp(1200f, 1900f, R(rng));
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                s[i] = band.Run(Noise(rng), click, 4f) * Env(t, 0.001f, 0.012f) + Mathf.Sin(2f * Mathf.PI * body * t) * Env(t, 0.001f, 0.03f) * 0.5f;
            }
            return Normalise(s, 0.5f);
        }

        /// <summary>A hen's cluck: two to four short throaty "bok"s, each a falling pitch through a nasal formant.</summary>
        static float[] Cluck(System.Random rng)
        {
            var boks = 2 + rng.Next(3);
            var s = new float[(int)(Rate * (boks * 0.14f + 0.1f))];
            var band = new Band();
            var phase = 0.0;
            for (int k = 0; k < boks; k++)
            {
                var start = (int)(k * Lerp(0.11f, 0.16f, R(rng)) * Rate);
                var hz0 = Lerp(380f, 520f, R(rng)) * (k == boks - 1 ? 1.25f : 1f);
                var len = (int)(0.07f * Rate);
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    var t = i / (float)Rate;
                    phase += hz0 * (1f - t * 3f) / Rate;
                    var p = (float)(phase - Math.Floor(phase));
                    var pulse = p < 0.3f ? 1f : -0.4f;
                    s[start + i] += band.Run(pulse, 1100f, 2.5f) * Env(t, 0.004f, 0.025f);
                }
            }
            return Normalise(s, 0.5f);
        }

        /// <summary>A blade drawn from its scabbard: a rising metallic scrape, then a short ring.</summary>
        static float[] Draw(System.Random rng)
        {
            var n = (int)(0.6f * Rate);
            var s = new float[n];
            var band = new Band();
            var p1 = Lerp(2100f, 2500f, R(rng));
            var p2 = p1 * 1.62f;
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var scrape = t < 0.32f ? band.Run(Noise(rng), Lerp(2500f, 6000f, t / 0.32f), 6f) * Mathf.Sin(Mathf.PI * t / 0.32f) : 0f;
                var ring = t > 0.28f ? (Mathf.Sin(2f * Mathf.PI * p1 * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * p2 * t)) * Mathf.Exp(-(t - 0.28f) / 0.09f) * 0.4f : 0f;
                s[i] = scrape + ring;
            }
            return Normalise(s, 0.45f);
        }

        /// <summary>One wingbeat: a soft whump of low-passed air.</summary>
        static float[] Wingbeat(System.Random rng)
        {
            var n = (int)(0.09f * Rate);
            var s = new float[n];
            var a = Pole(Lerp(500f, 800f, R(rng)));
            var lp = 0f;
            for (int i = 0; i < n; i++)
            {
                var u = i / (float)n;
                lp += a * (Noise(rng) - lp);
                s[i] = lp * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(Mathf.PI * u);
            }
            return Normalise(s, 0.4f);
        }

        // ------------------------------------------------------------------ the world's sounds

        /// <summary>
        /// A fire, 3 s looped: a soft low roar (dark noise breathing slowly) with wood pops at random, three to six a
        /// second, and now and then a sharp snap. A torch (thin): a quieter hiss, higher up, and only the odd small pop.
        /// </summary>
        static float[] Fire(System.Random rng, bool torch)
        {
            const float seconds = 3f;
            var fade = Rate / 4;
            var n = (int)(seconds * Rate) + fade;
            var s = new float[n];
            var band = new Band();
            float lp1 = 0f, lp2 = 0f;
            var roarCut = Pole(torch ? 2200f : 380f);
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                lp1 += roarCut * (Noise(rng) - lp1);
                lp2 += roarCut * (lp1 - lp2);
                var breathe = 0.75f + 0.25f * Mathf.Sin(t * 2.1f) * Mathf.Sin(t * 0.7f + 1f);
                s[i] = torch ? band.Run(Noise(rng), 3800f, 1.2f) * 0.25f * breathe : lp2 * 8f * breathe;
            }
            var pops = (int)(seconds * (torch ? 0.7f : Lerp(3f, 6f, R(rng))));
            for (int k = 0; k < pops; k++)
            {
                var at = rng.Next(n - Rate / 10);
                var snap = !torch && R(rng) < 0.15f;
                var hz = snap ? Lerp(3000f, 5000f, R(rng)) : Lerp(900f, 2600f, R(rng));
                var decay = snap ? 0.012f : Lerp(0.004f, 0.015f, R(rng));
                var level = (snap ? 1.6f : Lerp(0.4f, 1f, R(rng))) * (torch ? 0.5f : 1f);
                var b = new Band();
                for (int i = 0; i < Rate / 10 && at + i < n; i++)
                {
                    var t = i / (float)Rate;
                    s[at + i] += b.Run(Noise(rng), hz, 3f) * Mathf.Exp(-t / decay) * level * 0.9f;
                }
            }
            return Normalise(Seamless(s, fade), torch ? 0.35f : 0.55f);
        }

        /// <summary>
        /// Falling water, 3 s looped: broad pink-ish noise (a stack of low-passes) over a low rumble, swelling slowly
        /// once a loop. Pitched up for a small cave fall, down for a big one.
        /// </summary>
        static float[] Waterfall(System.Random rng)
        {
            const float seconds = 3f;
            var fade = Rate / 2;
            var n = (int)(seconds * Rate) + fade;
            var s = new float[n];
            float a = 0f, b = 0f, c = 0f, rumble = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var x = Noise(rng);
                a += Pole(4000f) * (x - a);
                b += Pole(900f) * (x - b);
                c += Pole(250f) * (x - c);
                rumble += Pole(90f) * (x - rumble);
                var wobble = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * t / seconds);
                s[i] = (0.35f * a + 0.6f * b + 0.9f * c + 3f * rumble) * wobble;
            }
            return Normalise(Seamless(s, fade), 0.55f);
        }

        /// <summary>Stepping into water: a slap, then spray falling away, and a few droplets plinking.</summary>
        static float[] Splash(System.Random rng)
        {
            var n = (int)(0.55f * Rate);
            var s = new float[n];
            var band = new Band();
            var lp = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                lp += Pole(700f) * (Noise(rng) - lp);
                var slap = lp * Env(t, 0.002f, 0.02f) * 2f;
                var spray = band.Run(Noise(rng), Lerp(5000f, 2500f, t * 2f), 1.5f) * Env(t, 0.01f, 0.14f);
                s[i] = slap + spray;
            }
            for (int k = 0; k < 4; k++)
            {
                var at = (int)(Lerp(0.08f, 0.4f, R(rng)) * Rate);
                var hz = Lerp(1200f, 2600f, R(rng));
                for (int i = 0; at + i < n && i < Rate / 20; i++)
                {
                    var t = i / (float)Rate;
                    s[at + i] += Mathf.Sin(2f * Mathf.PI * hz * (1f + t * 6f) * t) * Mathf.Exp(-t / 0.012f) * 0.3f;
                }
            }
            return Normalise(s, 0.5f);
        }

        /// <summary>A drip in a cave: a single plink (a tone whose pitch jumps up as it rings) and its echoes off the rock.</summary>
        static float[] Drip(System.Random rng)
        {
            var n = (int)(0.9f * Rate);
            var s = new float[n];
            var hz = Lerp(1400f, 2600f, R(rng));
            var plink = new float[Rate / 10];
            var phase = 0.0;
            for (int i = 0; i < plink.Length; i++)
            {
                var t = i / (float)Rate;
                phase += hz * (1f + 0.6f * Mathf.Min(t / 0.02f, 1f)) / Rate;
                plink[i] = Mathf.Sin(2f * Mathf.PI * (float)phase) * Mathf.Exp(-t / 0.03f);
            }
            // The drip and three echoes, each later, fainter and duller.
            var delays = new[] { 0f, Lerp(0.09f, 0.14f, R(rng)), Lerp(0.22f, 0.3f, R(rng)), Lerp(0.4f, 0.5f, R(rng)) };
            for (int e = 0; e < delays.Length; e++)
            {
                var at = (int)(delays[e] * Rate);
                var level = Mathf.Pow(0.4f, e);
                var lp = 0f;
                for (int i = 0; i < plink.Length && at + i < n; i++)
                {
                    lp += (e == 0 ? 1f : Pole(2500f / e)) * (plink[i] - lp);
                    s[at + i] += lp * level;
                }
            }
            return Normalise(s, 0.45f);
        }

        /// <summary>
        /// A town square by day, heard from a little way off, 5 s looped: several voices talking (each a buzzing pitch
        /// through a vowel-ish band, in bursts like syllables, its pitch rising and falling), now and then a laugh (a quick
        /// run of high bursts) or a clatter, all muffled by distance.
        /// </summary>
        static float[] Murmur(System.Random rng)
        {
            const float seconds = 5f;
            var fade = Rate / 2;
            var n = (int)(seconds * Rate) + fade;
            var s = new float[n];
            for (int v = 0; v < 7; v++)
            {
                var pitch = Lerp(95f, 230f, R(rng));
                var vowel = Lerp(500f, 1300f, R(rng));
                var band = new Band();
                var phase = 0.0;
                var talking = 0f;
                var next = 0;
                var syllable = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i >= next)
                    {
                        // A syllable or a pause.
                        syllable = R(rng) < 0.75f ? Lerp(0.6f, 1f, R(rng)) : 0f;
                        next = i + (int)(Lerp(0.08f, 0.22f, R(rng)) * Rate);
                        vowel = Mathf.Clamp(vowel + Lerp(-250f, 250f, R(rng)), 400f, 1500f);
                    }
                    talking += 0.004f * (syllable - talking);
                    var t = i / (float)Rate;
                    phase += pitch * (1f + 0.08f * Mathf.Sin(t * 3.1f + v)) / Rate;
                    var p = (float)(phase - System.Math.Floor(phase));
                    s[i] += band.Run(p < 0.15f ? 1f : -0.1f, vowel, 4f) * talking * Lerp(0.5f, 1f, (v % 3) / 2f);
                }
            }
            // A laugh, and a clatter.
            var laugh = rng.Next(n - Rate);
            var lb = new Band();
            for (int k = 0; k < 5; k++)
                for (int i = 0; i < Rate / 12; i++)
                {
                    var at = laugh + k * Rate / 8 + i;
                    var t = i / (float)Rate;
                    s[at] += lb.Run(Mathf.Sin(2f * Mathf.PI * 320f * t) > 0.6f ? 1f : -0.2f, 1100f, 4f) * Mathf.Sin(Mathf.PI * i / (Rate / 12f)) * 0.8f;
                }
            var clatter = rng.Next(n - Rate / 5);
            var cb = new Band();
            for (int i = 0; i < Rate / 5; i++)
                s[clatter + i] += cb.Run(Noise(rng), 2800f, 6f) * Mathf.Exp(-i / (0.03f * Rate)) * 1.5f;
            // Muffled by the distance.
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                lp1 += Pole(1600f) * (s[i] - lp1);
                lp2 += Pole(1600f) * (lp1 - lp2);
                s[i] = lp2;
            }
            return Normalise(Seamless(s, fade), 0.35f);
        }

        /// <summary>
        /// Deep in a mine, 6 s looped: a low hollow wind (noise ringing in two broad resonances of the tunnel, slowly
        /// swelling) and, once a loop, a far-off pick tapping three times, echoing.
        /// </summary>
        static float[] Mine(System.Random rng)
        {
            const float seconds = 6f;
            var fade = Rate;
            var n = (int)(seconds * Rate) + fade;
            var s = new float[n];
            var low = new Band();
            var high = new Band();
            var f1 = Lerp(150f, 210f, R(rng));
            var f2 = f1 * Lerp(2.3f, 2.9f, R(rng));
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var x = Noise(rng);
                var swell = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / seconds + 0.5f);
                s[i] = (low.Run(x, f1, 9f) + 0.4f * high.Run(x, f2, 7f)) * swell;
            }
            var tapAt = rng.Next(Rate, n - 2 * Rate);
            for (int k = 0; k < 3; k++)
                for (int e = 0; e < 3; e++)
                {
                    var at = tapAt + (int)(k * 0.55f * Rate) + (int)(e * 0.16f * Rate);
                    var b = new Band();
                    var level = 0.35f * Mathf.Pow(0.45f, e);
                    for (int i = 0; i < Rate / 15 && at + i < n; i++)
                        s[at + i] += b.Run(Noise(rng), 1900f, 5f) * Mathf.Exp(-i / (0.008f * Rate)) * level * 6f;
                }
            return Normalise(Seamless(s, fade), 0.45f);
        }
    }
}
