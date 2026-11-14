using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Ambience;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Punchy.Chemistry
{
    /// <summary>
    /// What the cells-only spells look like besides the shaders' maps (ChemistryBridge.Spells.cs, README "Spells"), drawn
    /// with Punchy's own effects and nothing new but their settings:
    /// - **Landmark plumes** (LandmarkEffectSystem.PlumeMaterial, the LandmarkPlume shader, fed our own emitter buffer):
    ///   the fumarole's glowing embers and the hot springs' bubbles over lava, the geyser's spout for Geyser, the hot
    ///   springs' steam where lava meets water, the waterfall's landing spray for Flood, the fairy ring's sparkles for
    ///   Plant Growth, acid-green bubbles for Acid Splash and a glow over a falling star's mark. A one-shot is a plume
    ///   with a long period whose phase starts now (the shader's Eruption), so it spouts once and falls quiet.
    /// - **Arc bolts** (Resources/ArcBolt, the arcing spires' bolts, FloraSystem.BoltMesh): Lightning Bolt, Shocking
    ///   Grasp, Thunder Strike's column, a storm cloud's strikes and the arcs through every charged cell (lightning running
    ///   through water); a pale, straight copy of the material is Ray of Frost's beam.
    /// - **The weather's particles** (WeatherSystem.PrecipitationShader): rain streaks under a rain cloud, dust veils in a
    ///   dust storm and Sand Blast, white veils in fog.
    /// - **Puffs** (ChemistryEffects' steam and smoke systems, and one more on the breath-puff material): fog, dust, the
    ///   rain cloud's body, splashes of water, mud, grease and sand.
    /// - **Flames** (FireSystem, the standard fire): Burning Hands' and Flame Wave's cones of flame, a falling star.
    /// Where each shows comes from the cells (and a cast's cells, which every peer runs on the same tick), placed by a hash
    /// of the cell, so two screens agree on what matters; only the drift of a puff is left to chance. Cosmetic and local.
    /// </summary>
    public sealed partial class ChemistryEffects
    {
        // State lists the bridge fills at each map (window indices).
        public readonly List<Spot> Lava = new List<Spot>(), Fog = new List<Spot>(), Dust = new List<Spot>(), Cloud = new List<Spot>(),
                                   Charged = new List<Spot>(), Stars = new List<Spot>(), Quenched = new List<Spot>(), Grown = new List<Spot>();

        // At most this many plumes, arcs, puffs a frame for the new effects.
        const int MaxPlumes = 160, MaxArcs = 140, SpellPuffsPerFrame = 40, CloudPuffsPerFrame = 24, RainPerFrame = 300, VeilsPerFrame = 6;
        const int LavaLightId = 0x4C4156, FlashLightId = 0x464C53;

        Material plumeMaterial, boltMaterial, frostMaterial, rainMaterial, veilMaterial;
        Mesh boltMesh;
        ParticleSystem softPuffs, rain, veils;
        bool madeSpellFx;
        GraphicsBuffer plumeBuffer;
        MaterialPropertyBlock plumeProps;
        static readonly int EmittersId = Shader.PropertyToID("_LandmarkEmitters");
        static readonly int MaxParticlesId = Shader.PropertyToID("_MaxParticles");
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");

        // The plumes the cells make (rebuilt at each map) and the one-shots of casts (until they end).
        readonly List<LandmarkEffectSystem.Emitter> statePlumes = new List<LandmarkEffectSystem.Emitter>(), drawn = new List<LandmarkEffectSystem.Emitter>();
        readonly List<(LandmarkEffectSystem.Emitter e, float until)> shots = new List<(LandmarkEffectSystem.Emitter, float)>();
        // Arcs between charged cells (rebuilt at each map), and timed bolts (from, to, until, frost).
        readonly List<Matrix4x4> arcs = new List<Matrix4x4>(), firing = new List<Matrix4x4>(), frosting = new List<Matrix4x4>();
        readonly List<(Vector3 a, Vector3 b, float from, float until, bool frost, float thick)> bolts = new List<(Vector3, Vector3, float, float, bool, float)>();
        // Timed flames (a cone of flame, a falling star): where, how high, from, until, and for a falling one, where it starts.
        readonly List<(Vector3 at, float size, float from, float until, Vector3 fall)> flames = new List<(Vector3, float, float, float, Vector3)>();
        float flashUntil, flashBright;
        Vector3 flashAt;
        Vector3 lavaCentre;
        int lavaNear;

        /// <summary>The new effects alive now: plumes drawn, bolts and arcs, timed flames.</summary>
        public int SpellFxCount => drawn.Count + bolts.Count + arcs.Count + flames.Count;

        void DisposeSpells()
        {
            foreach (var ps in new[] { softPuffs, rain, veils })
                if (ps != null)
                    Object.Destroy(ps.gameObject);
            softPuffs = rain = veils = null;
            plumeBuffer?.Release();
            plumeBuffer = null;
            if (frostMaterial != null)
                Object.Destroy(frostMaterial);
            if (rainMaterial != null)
                Object.Destroy(rainMaterial);
            if (veilMaterial != null)
                Object.Destroy(veilMaterial);
            frostMaterial = rainMaterial = veilMaterial = null;
            madeSpellFx = false;
            shots.Clear();
            bolts.Clear();
            flames.Clear();
        }

        /// <summary>The view started again: drop what the last one was showing.</summary>
        public void ClearSpells()
        {
            Lava.Clear(); Fog.Clear(); Dust.Clear(); Cloud.Clear(); Charged.Clear(); Stars.Clear(); Quenched.Clear(); Grown.Clear();
            statePlumes.Clear();
            arcs.Clear();
            shots.Clear();
            bolts.Clear();
            flames.Clear();
        }

        void EnsureSpellFx()
        {
            if (madeSpellFx)
                return;
            madeSpellFx = true;
            var landmarks = Object.FindAnyObjectByType<LandmarkEffectSystem>();
            plumeMaterial = landmarks != null ? landmarks.PlumeMaterial : null;
            boltMaterial = Resources.Load<Material>("ArcBolt");
            boltMesh = Punchy.Flora.FloraSystem.BoltMesh();
            if (boltMaterial != null)
            {
                // Ray of Frost: the same bolt, pale cyan, wider and nearly straight.
                frostMaterial = new Material(boltMaterial) { name = "ChemistryFrostBeam" };
                frostMaterial.SetColor("_Color", new Color(1.6f, 3.2f, 4.2f, 1f));
                frostMaterial.SetFloat("_Width", 0.12f);
                frostMaterial.SetFloat("_Jag", 0.08f);
            }
            var weather = Object.FindAnyObjectByType<Punchy.Weather.WeatherSystem>();
            var precipitation = weather != null ? weather.PrecipitationShader : null;
            if (precipitation != null)
            {
                rainMaterial = new Material(precipitation) { name = "ChemistryRain" };
                rainMaterial.SetFloat("_Shape", 1f);
                veilMaterial = new Material(precipitation) { name = "ChemistryVeils" };
                veilMaterial.SetFloat("_Shape", 2f);
                rain = CreateWeatherParticles("ChemistryRain", rainMaterial, ParticleSystemRenderMode.Stretch, 6000, false);
                veils = CreateWeatherParticles("ChemistryVeils", veilMaterial, ParticleSystemRenderMode.Billboard, 400, true);
            }
            var ambience = Object.FindAnyObjectByType<AmbienceSystem>();
            if (ambience != null && ambience.PuffMaterial != null)
                softPuffs = CreatePuffs("ChemistryPuffs", ambience.PuffMaterial, MaxPuffs);
            if (plumeMaterial == null || boltMaterial == null || precipitation == null || softPuffs == null)
                Debug.LogWarning($"[Chemistry] spell effects missing (plumes {plumeMaterial != null}, bolts {boltMaterial != null}, " +
                                 $"weather particles {precipitation != null}, puffs {softPuffs != null}): those are off");
        }

        /// <summary>A particle system like WeatherSystem's rain and dust veils (its shader), emitted by hand.</summary>
        static ParticleSystem CreateWeatherParticles(string name, Material material, ParticleSystemRenderMode mode, int max, bool veil)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (veil)
            {
                var fade = ps.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                 new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                var grow = ps.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.velocityScale = mode == ParticleSystemRenderMode.Stretch ? 0.05f : 0f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------------------------------------ from the cells

        /// <summary>After each map: the plumes and arcs the cells make (lava, a star's mark, charged cells), and bursts for what just changed.</summary>
        public void SpellsMapped(int2 corner, int size, float[] heights)
        {
            if (heights == null)
                return;
            statePlumes.Clear();
            arcs.Clear();
            if (Lava.Count == 0 && Stars.Count == 0 && Charged.Count == 0 && Quenched.Count == 0 && Grown.Count == 0)
                return;
            EnsureSpellFx();
            // Lava: one plume pair per 3 x 3 block of cells with lava in it (the first lava cell in the block): the hot
            // springs' bubbles, glowing, and the fumarole field's embers on every other block.
            var blocks = new HashSet<int2>();
            float3 sum = 0f;
            foreach (var l in Lava)
            {
                int x = corner.x + l.Index % size, y = corner.y + l.Index / size;
                sum += (float3)At(corner, size, heights, l.Index, 0.05f);
                var block = new int2(FloorDiv(x, 3), FloorDiv(y, 3));
                if (statePlumes.Count >= MaxPlumes - 8 || !blocks.Add(block))
                    continue;
                var at = At(corner, size, heights, l.Index, 0.05f);
                var seed = Frac(Hash(block.x, block.y), 7);
                statePlumes.Add(Bubbles(at, 1.3f, 0.28f, 6, new float4(1f, 0.42f, 0.08f, 2.5f), 0.85f, 1.6f, seed));
                if (((block.x + block.y) & 1) == 0)
                    statePlumes.Add(Plume(at, 2.6f, 1.2f, 0.11f, 12, 2.2f, new float4(1f, 0.45f, 0.12f, 4f), 0.9f, seed));
            }
            lavaNear = Lava.Count;
            lavaCentre = Lava.Count > 0 ? (Vector3)(sum / Lava.Count) : Vector3.zero;
            // A falling star's mark: a low glow of sparks where it will land.
            foreach (var s in Stars)
                if (statePlumes.Count < MaxPlumes)
                {
                    int x = corner.x + s.Index % size, y = corner.y + s.Index / size;
                    statePlumes.Add(Plume(At(corner, size, heights, s.Index, 0.05f), 1.2f, 0.6f, 0.07f, 8, 1.2f, new float4(1f, 0.7f, 0.3f, 3f), 0.8f, Frac(Hash(x, y), 3)));
                }
            // Lava that set into rock in water: the hot springs' steam, once, and a hiss.
            foreach (var q in Quenched)
                if (shots.Count < MaxPlumes)
                {
                    int x = corner.x + q.Index % size, y = corner.y + q.Index / size;
                    if ((Hash(x, y) & 3) != 0)
                        continue;
                    OneShot(Plume(At(corner, size, heights, q.Index, 0.1f), 5f, 1.5f, 1.6f, 18, 4f, new float4(0.92f, 0.93f, 0.95f, 0f), 0.35f, 0f), 2.5f);
                }
            if (Quenched.Count > 0)
                Sound(Punchy.Audio.SoundId.Hiss, At(corner, size, heights, Quenched[0].Index, 0.3f), 0.7f, 0.8f);
            Quenched.Clear();
            // New growth coming up: the fairy ring's green sparkles, a cell in four.
            foreach (var g in Grown)
            {
                int x = corner.x + g.Index % size, y = corner.y + g.Index / size;
                if ((Hash(x, y) & 3) == 0 && shots.Count < MaxPlumes)
                    OneShot(Plume(At(corner, size, heights, g.Index, 0.05f), 1.8f, 1.2f, 0.06f, 10, 2.5f, new float4(0.6f, 1f, 0.45f, 3f), 1f, 0f), 2.5f);
            }
            Grown.Clear();
            // The arcs through charged cells: from each to a charged neighbour east or north (chosen by a hash of the
            // cell, so not every pair), a little above the ground or water, jumping as the bolt shader jumps.
            var charged = new HashSet<int>();
            foreach (var c in Charged)
                charged.Add(c.Index);
            foreach (var c in Charged)
            {
                if (arcs.Count >= MaxArcs)
                    break;
                int x = c.Index % size, y = c.Index / size;
                var h = Hash(corner.x + x, corner.y + y);
                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 + (int)(h >> 7 & 1) : 0), ny = y + (k == 1 ? 1 + (int)(h >> 9 & 1) : 0);
                    if (nx >= size || ny >= size || !charged.Contains(ny * size + nx) || ((h >> (k + 3)) & 1) == 0)
                        continue;
                    var a = At(corner, size, heights, c.Index, 0.15f + 0.3f * Frac(h, 11));
                    var b = At(corner, size, heights, ny * size + nx, 0.15f + 0.3f * Frac(h, 12 + (uint)k));
                    arcs.Add(Matrix4x4.TRS(a, Quaternion.LookRotation(b - a), new Vector3(0.9f, 0.9f, Vector3.Distance(a, b))));
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ casts

        /// <summary>
        /// A spell has landed (every peer, the same tick): its burst, from its cells (window indices, in the order it
        /// touches them; the first is beside the caster for a spell cast from the caster) and its first cell's point.
        /// </summary>
        public void SpellCast(string spell, List<int> cells, int2 corner, int size, float[] heights, int charge, Vector3 hand, Vector3? strikeTo)
        {
            if (cells.Count == 0 || heights == null)
                return;
            EnsureSpellFx();
            var now = Time.time;
            var first = At(corner, size, heights, cells[0], 0f);
            var last = At(corner, size, heights, cells[cells.Count - 1], 0f);
            var centre = Centre(corner, size, heights, cells);
            var seed = Frac(Hash(corner.x + cells[0] % size, corner.y + cells[0] / size), 5);
            switch (spell)
            {
                case "burning-hands":
                case "flame-wave":
                    // A cone of flame rolling out from the hands: each cell flames for a moment, later the further it is.
                    for (int k = 0; k < cells.Count; k++)
                    {
                        var at = At(corner, size, heights, cells[k], 0f);
                        var delay = Vector3.Distance(at, first) * 0.05f;
                        flames.Add((at, 1.1f + 0.4f * charge, now + delay, now + delay + 0.55f, Vector3.zero));
                    }
                    if (spell == "flame-wave")
                        Splashes(cells, corner, size, heights, new Color(0.55f, 0.52f, 0.5f, 0.35f), 1.6f, 1.2f, 2.2f, 0.4f, (last - first).normalized * 4f);
                    Sound(Punchy.Audio.SoundId.TorchHiss, first, 0.8f, 1f);
                    break;
                case "fireball":
                    OneShot(Plume(centre, 4f, 2f, 0.12f, 30, 2f, new float4(1f, 0.45f, 0.12f, 4f), 0.9f, 0f), 1.5f);
                    Flash(centre + Vector3.up * 2f, 1.2f, 0.25f);
                    break;
                case "meteor":
                    // The fumarole's embers thrown high, a ring of dust and a flash.
                    OneShot(Plume(centre, 10f, 3.5f, 0.16f, 90, 2.6f, new float4(1f, 0.45f, 0.12f, 4.5f), 0.95f, 0f), 2f);
                    OneShot(Plume(centre, 12f, 4f, 3.2f, 40, 6f, new float4(0.3f, 0.29f, 0.28f, 0f), 0.5f, 0f), 3f);
                    Splashes(cells, corner, size, heights, new Color(0.55f, 0.47f, 0.36f, 0.45f), 2.2f, 0.8f, 3f, 0.3f, Vector3.zero, outward: centre);
                    Flash(centre + Vector3.up * 3f, 2.5f, 0.4f);
                    Sound(Punchy.Audio.SoundId.Drum, centre, 0.5f, 1f);
                    break;
                case "falling-star":
                    // A star falls from high over the mark and lands on the tick the core lands it (its delay).
                    flames.Add((centre, 2.2f, now, now + strikeDelay, centre + new Vector3(8f, 45f, 5f)));
                    break;
                case "lava-pool":
                    OneShot(Plume(centre, 4f, 2f, 0.13f, 40, 2.4f, new float4(1f, 0.45f, 0.12f, 4f), 0.9f, 0f), 2f);
                    Sound(Punchy.Audio.SoundId.Bubbles, centre, 0.6f, 1f);
                    break;
                case "scalding-steam":
                    OneShot(Plume(centre, 6f, 2.5f + charge, 2.5f, 35, 5f, new float4(0.92f, 0.93f, 0.95f, 0f), 0.45f, 0f), 3f);
                    Sound(Punchy.Audio.SoundId.Hiss, centre, 0.9f, 1f);
                    break;
                case "geyser":
                    // The geyser landmark's spout, erupting once, with its base steam and boiling bubbles.
                    OneShot(Plume(centre, 14f + 4f * charge, 1.6f, 2.2f, 140, 2.8f, new float4(0.92f, 0.93f, 0.95f, 0f), 0.6f, 0f), 3.5f);
                    OneShot(Plume(centre, 6f, 1.5f, 2f, 30, 5f, new float4(0.92f, 0.93f, 0.95f, 0f), 0.2f, 0f), 5f);
                    OneShot(Bubbles(centre, 2.8f, 0.08f, 40, new float4(0.95f, 0.97f, 1f, 0f), 0.6f, 1.2f, seed), 4f);
                    Sound(Punchy.Audio.SoundId.WaterfallRush, centre, 1f, 1f);
                    break;
                case "flood":
                case "wave":
                case "create-water":
                    // The waterfall's landing spray along where the water lands.
                    for (int k = 0; k < cells.Count; k += spell == "create-water" ? 4 : 2)
                        if (shots.Count < MaxPlumes)
                            OneShot(Plume(At(corner, size, heights, cells[k], 0f), 1.8f, 1.4f, 0.9f, 20, 2.5f, new float4(0.92f, 0.93f, 0.95f, 0f), 0.3f, 0f), 1.4f);
                    Sound(Punchy.Audio.SoundId.Splash, centre, 0.8f, 1f);
                    break;
                case "iceberg":
                case "ray-of-frost":
                case "frost":
                case "sleet-storm":
                    if (spell == "ray-of-frost" || spell == "frost")
                        bolts.Add((hand, last + Vector3.up * 0.4f, now, now + 0.35f, true, 1f));
                    Splashes(cells, corner, size, heights, new Color(0.88f, 0.93f, 0.97f, 0.5f), 1.1f, 0.4f, 1.8f, spell == "sleet-storm" ? 0.3f : 0.6f, Vector3.zero);
                    break;
                case "lightning-bolt":
                    // The stroke from the hands to whatever it jumped to (the nearest charged water or metal), else its end.
                    var to = strikeTo ?? last + Vector3.up * 0.3f;
                    bolts.Add((hand, to, now, now + 0.4f, false, 3f));
                    bolts.Add((hand, to, now + 0.12f, now + 0.3f, false, 2f));
                    Flash(to + Vector3.up, 1.5f, 0.25f);
                    Sound(Punchy.Audio.SoundId.Zip, to, 0.6f, 1f);
                    break;
                case "shocking-grasp":
                    bolts.Add((hand, first + Vector3.up * 0.2f, now, now + 0.3f, false, 1.5f));
                    OneShot(Plume(first, 1f, 0.6f, 0.05f, 16, 0.8f, new float4(0.8f, 0.9f, 1f, 4f), 1f, 0f), 0.6f);
                    Sound(Punchy.Audio.SoundId.Zip, first, 1.3f, 1f);
                    break;
                case "thunder-strike":
                    // The sky's mark: sparks over where it will strike, until the column comes down (StrikeColumn).
                    OneShot(Plume(centre, 3f, 2.5f + charge, 0.08f, 30, 1.2f, new float4(0.75f, 0.85f, 1f, 3f), 0.9f, 0f), strikeDelay);
                    break;
                case "rain-cloud":
                case "fog-cloud":
                case "dust-storm":
                    Veils(cells, corner, size, heights, spell == "dust-storm" ? new Color(0.8f, 0.65f, 0.45f, 0.18f) : new Color(0.92f, 0.94f, 0.96f, 0.16f), spell == "rain-cloud" ? 3.5f : 1.2f, 4);
                    break;
                case "sand-blast":
                    // Sand thrown along the line: puffs flying out from the hands, and a dust veil.
                    Splashes(cells, corner, size, heights, new Color(0.76f, 0.62f, 0.4f, 0.55f), 0.8f, 0.3f, 1.2f, 0.8f, (last - first).normalized * 9f);
                    Veils(cells, corner, size, heights, new Color(0.8f, 0.65f, 0.45f, 0.15f), 1f, 2);
                    Sound(Punchy.Audio.SoundId.Hiss, first, 0.6f, 0.7f);
                    break;
                case "mud":
                    Splashes(cells, corner, size, heights, new Color(0.2f, 0.14f, 0.08f, 0.7f), 0.6f, 0.9f, 1f, 0.7f, Vector3.zero);
                    Sound(Punchy.Audio.SoundId.StepWet, centre, 0.7f, 1f);
                    break;
                case "grease":
                    Splashes(cells, corner, size, heights, new Color(0.62f, 0.52f, 0.2f, 0.7f), 0.5f, 0.8f, 0.9f, 0.7f, Vector3.zero);
                    Sound(Punchy.Audio.SoundId.StepWet, centre, 1.2f, 1f);
                    break;
                case "acid-splash":
                    // Acid-green bubbles fizzing (the hot springs' bubbles, glowing green) and green sparks.
                    OneShot(Bubbles(centre, 1.6f + charge, 0.12f, 40, new float4(0.55f, 1f, 0.2f, 1.5f), 0.9f, 0.9f, seed), 2.5f);
                    OneShot(Plume(centre, 1.5f, 1.5f + charge, 0.06f, 24, 1.4f, new float4(0.55f, 1f, 0.2f, 3f), 1f, 0f), 2f);
                    Sound(Punchy.Audio.SoundId.Hiss, centre, 1.4f, 1f);
                    break;
                case "plant-growth":
                    OneShot(Plume(centre, 2.5f, 3f + charge, 0.06f, 60, 4f, new float4(0.7f, 1f, 0.6f, 3f), 1f, 0f), 3f);
                    break;
                case "spike-growth":
                    Splashes(cells, corner, size, heights, new Color(0.35f, 0.3f, 0.2f, 0.5f), 0.8f, 0.6f, 1.4f, 0.35f, Vector3.zero);
                    break;
            }
        }

        // The ticks before a delayed spell lands, in seconds (set by the bridge from the spell's delay).
        float strikeDelay = 1f;
        public void SetDelay(int ticks, int ticksPerSecond) => strikeDelay = math.max(0.1f, ticks / (float)math.max(1, ticksPerSecond));

        /// <summary>Thunder Strike's column: bolts from the sky to the cells it struck (charged now), at most 12, and its flash and thunder.</summary>
        public void StrikeColumn(Vector3 centre, List<Vector3> struck)
        {
            EnsureSpellFx();
            var now = Time.time;
            bolts.Add((centre + new Vector3(2f, 60f, 1f), centre, now, now + 0.5f, false, 6f));
            for (int k = 0; k < struck.Count && k < 12; k++)
                bolts.Add((centre + new Vector3(0f, 40f, 0f) + (struck[k] - centre) * 0.3f, struck[k], now + 0.03f * k, now + 0.35f + 0.03f * k, false, 3f));
            Flash(centre + Vector3.up * 4f, 4f, 0.5f);
            Sound(Punchy.Audio.SoundId.Zip, centre, 0.35f, 1f);
            Sound(Punchy.Audio.SoundId.Drum, centre, 0.4f, 1f);
        }

        /// <summary>A storm cloud's strike (World.ShedStrikesSince): a bolt from the cloud to the cell.</summary>
        public void CloudStrike(Vector3 at)
        {
            EnsureSpellFx();
            var now = Time.time;
            bolts.Add((at + new Vector3(0.5f, 5f, -0.3f), at, now, now + 0.3f, false, 2.5f));
            Flash(at + Vector3.up * 3f, 1.5f, 0.25f);
            Sound(Punchy.Audio.SoundId.Zip, at, 0.5f, 1f);
        }

        // ------------------------------------------------------------------------------------------------ each frame

        /// <summary>Draws this frame's plumes, bolts, timed flames and lights, and emits the gas cells' particles.</summary>
        public void SpellsFrame(int2 corner, int size, float[] heights, int maxGas, Camera cam)
        {
            if (cam == null || heights == null)
                return;
            if (!madeSpellFx)
            {
                if (Fog.Count == 0 && Dust.Count == 0 && Cloud.Count == 0)
                    return;
                EnsureSpellFx();
            }
            var now = Time.time;
            DrawPlumes(corner, size, now);
            DrawBolts(corner, size, now);
            DrawFlames(cam.transform.position, now);
            Lights(now);
            Gases(corner, size, heights, maxGas);
        }

        void DrawPlumes(int2 corner, int size, float now)
        {
            drawn.Clear();
            drawn.AddRange(statePlumes);
            for (int k = shots.Count - 1; k >= 0; k--)
                if (now >= shots[k].until)
                    shots.RemoveAt(k);
                else if (drawn.Count < MaxPlumes)
                    drawn.Add(shots[k].e);
            if (drawn.Count == 0 || plumeMaterial == null)
                return;
            if (plumeBuffer == null || plumeBuffer.count < drawn.Count)
            {
                plumeBuffer?.Release();
                plumeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxPlumes, Marshal.SizeOf<LandmarkEffectSystem.Emitter>());
            }
            plumeBuffer.SetData(drawn);
            plumeProps ??= new MaterialPropertyBlock();
            plumeProps.SetBuffer(EmittersId, plumeBuffer);
            int most = 1;
            foreach (var e in drawn)
                most = math.max(most, (int)e.Timing.w);
            plumeProps.SetInteger(MaxParticlesId, most);
            var bounds = new Bounds(new Vector3(corner.x + size * 0.5f, 0f, corner.y + size * 0.5f), new Vector3(size, 2000f, size));
            var rp = new RenderParams(plumeMaterial) { worldBounds = bounds, matProps = plumeProps };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, drawn.Count * most);
        }

        void DrawBolts(int2 corner, int size, float now)
        {
            if (boltMaterial == null)
                return;
            firing.Clear();
            frosting.Clear();
            firing.AddRange(arcs);
            for (int k = bolts.Count - 1; k >= 0; k--)
            {
                var b = bolts[k];
                if (now >= b.until)
                {
                    bolts.RemoveAt(k);
                    continue;
                }
                if (now < b.from || (b.b - b.a).sqrMagnitude < 0.01f)
                    continue;
                var m = Matrix4x4.TRS(b.a, Quaternion.LookRotation(b.b - b.a), new Vector3(b.thick, b.thick, Vector3.Distance(b.a, b.b)));
                (b.frost ? frosting : firing).Add(m);
            }
            var bounds = new Bounds(new Vector3(corner.x + size * 0.5f, 0f, corner.y + size * 0.5f), new Vector3(size, 2000f, size));
            if (firing.Count > 0)
                Graphics.RenderMeshInstanced(new RenderParams(boltMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds }, boltMesh, 0, firing);
            if (frosting.Count > 0 && frostMaterial != null)
                Graphics.RenderMeshInstanced(new RenderParams(frostMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds }, boltMesh, 0, frosting);
        }

        void DrawFlames(Vector3 eye, float now)
        {
            for (int k = flames.Count - 1; k >= 0; k--)
            {
                var f = flames[k];
                if (now >= f.until)
                {
                    flames.RemoveAt(k);
                    continue;
                }
                if (now < f.from)
                    continue;
                var t = (now - f.from) / math.max(0.01f, f.until - f.from);
                var seed = (int)Hash((int)math.floor(f.at.x), (int)math.floor(f.at.z));
                if (f.fall != Vector3.zero)
                {
                    // A falling star: a bright flame streaking down, leaning back the way it came.
                    var at = Vector3.Lerp(f.fall, f.at, t * t);
                    FireSystem.AddFlame(at, f.size, seed, FireSystem.Wood, (f.fall - f.at).normalized * 2f);
                    continue;
                }
                // A cone's flame flares up, then sinks.
                var height = f.size * math.sin(math.PI * math.min(1f, t * 1.2f));
                if ((f.at - eye).sqrMagnitude < NearFlames * NearFlames)
                    FireSystem.AddFlame(f.at, height, seed, FireSystem.Wood);
                else
                    FireSystem.AddFarFlame(f.at, height, seed, FireSystem.Wood);
            }
        }

        void Lights(float now)
        {
            // The lava's glow (a fire light at its middle), and a moment's flash for a blast or a bolt.
            if (lavaNear > 0)
            {
                var flicker = 0.9f + 0.1f * Mathf.Sin(now * 3.1f);
                var light = FireSystem.Light(FireSystem.Wood, flicker * math.clamp(0.7f + lavaNear * 0.02f, 0.7f, 2.2f));
                light.w *= math.clamp(1f + lavaNear * 0.015f, 1f, 2.5f);
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fire, LavaLightId, lavaCentre + Vector3.up * 1.5f, light);
            }
            if (now < flashUntil)
            {
                var k = (flashUntil - now) / 0.4f;
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fire, FlashLightId, flashAt,
                                                new Vector4(1.6f, 1.7f, 2f, 0f) * flashBright * math.saturate(k) + new Vector4(0f, 0f, 0f, 18f + 6f * flashBright));
            }
        }

        void Gases(int2 corner, int size, float[] heights, int maxGas)
        {
            var wind = (Vector3)new float3(Punchy.Weather.Climate.CameraWind.x, 0f, Punchy.Weather.Climate.CameraWind.y) * 0.6f;
            var dt = Time.deltaTime;
            int budget = SpellPuffsPerFrame;
            // Fog: big, slow, low puffs of the gas-cloud material that thin out with the fog, and white veils.
            budget -= EmitGas(Fog, steamPuffs, 1.2f, maxGas, budget, corner, size, heights, dt, 0.3f, 3.2f, 0.12f, 6f, new Color(0.9f, 0.92f, 0.94f, 0.35f), wind * 0.5f);
            EmitVeils(Fog, 0.08f, corner, size, heights, dt, 1.5f, new Color(0.92f, 0.94f, 0.96f, 0.14f));
            // Dust: the breath-puff material in dust's colour, swirling, and dust veils as the weather's dust storms have.
            budget -= EmitGas(Dust, softPuffs, 1.5f, maxGas, budget, corner, size, heights, dt, 0.8f, 1.8f, 0.6f, 3f, new Color(0.72f, 0.58f, 0.38f, 0.45f), wind * 1.5f);
            EmitVeils(Dust, 0.06f, corner, size, heights, dt, 1.5f, new Color(0.8f, 0.65f, 0.45f, 0.16f));
            // A rain cloud: dark puffs a few metres up, and the weather's rain streaks falling from it.
            EmitGas(Cloud, softPuffs, 1.2f, maxGas, CloudPuffsPerFrame, corner, size, heights, dt, 4.6f, 3.6f, 0.1f, 5f, new Color(0.34f, 0.36f, 0.42f, 0.75f), wind * 0.3f);
            EmitVeils(Cloud, 0.05f, corner, size, heights, dt, 4.2f, new Color(0.45f, 0.47f, 0.53f, 0.2f));
            if (rain != null && Cloud.Count > 0)
            {
                var due = Cloud.Count * 25f * dt;
                var count = math.min(RainPerFrame, (int)due + (Random.value < due - (int)due ? 1 : 0));
                for (int i = 0; i < count; i++)
                {
                    var s = Cloud[Random.Range(0, Cloud.Count)];
                    var at = CellPoint(corner, size, heights, s.Index, 4.2f);
                    rain.Emit(new ParticleSystem.EmitParams
                    {
                        position = at, velocity = new Vector3(wind.x, -13f, wind.z), startSize = 0.04f, startLifetime = 0.32f,
                        startColor = new Color(0.85f, 0.9f, 1f, 0.6f), applyShapeToPosition = false
                    }, 1);
                }
            }
        }

        int EmitGas(List<Spot> spots, ParticleSystem ps, float rate, int maxGas, int budget, int2 corner, int size, float[] heights, float dt,
                    float lift, float puffSize, float rise, float life, Color color, Vector3 wind)
        {
            if (spots.Count == 0 || budget <= 0 || ps == null)
                return 0;
            var due = spots.Count * rate * dt;
            var count = math.min(budget, (int)due + (Random.value < due - (int)due ? 1 : 0));
            for (int i = 0; i < count; i++)
            {
                var s = spots[Random.Range(0, spots.Count)];
                var amount = math.saturate(s.Amount / (float)math.max(1, maxGas));
                if (Random.value > 0.35f + 0.65f * amount)
                    continue;
                var c = color;
                c.a *= 0.5f + 0.5f * amount;
                Emit(ps, CellPoint(corner, size, heights, s.Index, lift), puffSize * (0.8f + 0.4f * amount), rise, life, c, wind);
            }
            return count;
        }

        void EmitVeils(List<Spot> spots, float rate, int2 corner, int size, float[] heights, float dt, float lift, Color color)
        {
            if (veils == null || spots.Count == 0)
                return;
            var due = spots.Count * rate * dt;
            var count = math.min(VeilsPerFrame, (int)due + (Random.value < due - (int)due ? 1 : 0));
            for (int i = 0; i < count; i++)
            {
                var s = spots[Random.Range(0, spots.Count)];
                veils.Emit(new ParticleSystem.EmitParams
                {
                    position = CellPoint(corner, size, heights, s.Index, lift), velocity = Random.insideUnitSphere * 0.3f,
                    startSize = Random.Range(5f, 9f), startLifetime = Random.Range(4f, 6f), startColor = color,
                    rotation = Random.Range(0f, 360f), applyShapeToPosition = false
                }, 1);
            }
        }

        // A cast's puffs over its cells (every few cells), flying along `along` or out from `outward`.
        void Splashes(List<int> cells, int2 corner, int size, float[] heights, Color color, float puffSize, float rise, float life, float share,
                      Vector3 along, Vector3? outward = null)
        {
            var ps = softPuffs;
            if (ps == null)
                return;
            int n = 0;
            foreach (var i in cells)
            {
                int x = corner.x + i % size, y = corner.y + i / size;
                if (Frac(Hash(x, y), 21) > share || n++ > 120)
                    continue;
                var at = At(corner, size, heights, i, 0.3f) + new Vector3(Frac(Hash(x, y), 22) - 0.5f, 0f, Frac(Hash(x, y), 23) - 0.5f);
                var v = along;
                if (outward is Vector3 o)
                    v = (at - o).normalized * 5f;
                Emit(ps, at, puffSize, rise, life, color, v);
            }
        }

        void Veils(List<int> cells, int2 corner, int size, float[] heights, Color color, float lift, int every)
        {
            if (veils == null)
                return;
            for (int k = 0; k < cells.Count; k += every)
                veils.Emit(new ParticleSystem.EmitParams
                {
                    position = At(corner, size, heights, cells[k], lift), velocity = Vector3.zero, startSize = 7f, startLifetime = 5f,
                    startColor = color, rotation = Frac(Hash(cells[k], k), 1) * 360f, applyShapeToPosition = false
                }, 1);
        }

        void OneShot(LandmarkEffectSystem.Emitter e, float seconds)
        {
            // An eruption that starts now and lasts `seconds`: a long period whose phase is 0 at this moment.
            const float period = 600f;
            var clock = Shader.GetGlobalFloat(ClockId);
            e.Timing.x = period;
            e.Timing.y = math.saturate(seconds / period);
            e.Seed = 1f - math.frac(clock / period);
            shots.Add((e, Time.time + seconds + e.Shape.w));
        }

        void Flash(Vector3 at, float bright, float seconds)
        {
            flashAt = at;
            flashBright = math.max(bright, Time.time < flashUntil ? flashBright : 0f);
            flashUntil = Time.time + seconds;
        }

        static void Sound(Punchy.Audio.SoundId id, Vector3 at, float pitch, float volume)
        {
            if (Punchy.Audio.GameAudio.Audible(at, 80f))
                Punchy.Audio.GameAudio.PlayOneShot(id, at, pitch, volume, 80f, Punchy.Audio.SoundGroup.World);
        }

        // A landmark plume: rises `height` m over `width`, puffs `puffSize` across living `life` s, rgb + glow, opacity.
        static LandmarkEffectSystem.Emitter Plume(Vector3 at, float height, float width, float puffSize, int particles, float life, float4 color, float opacity, float seed) =>
            new LandmarkEffectSystem.Emitter
            {
                Position = at, Seed = seed, Color = color,
                Shape = new float4(height, width, puffSize, life),
                Timing = new float4(0f, 0.25f, opacity, particles)
            };

        // Bubbles on a pool's surface (the plume shader's bubbles: a negative lifetime).
        static LandmarkEffectSystem.Emitter Bubbles(Vector3 at, float width, float bubbleSize, int particles, float4 color, float opacity, float life, float seed) =>
            new LandmarkEffectSystem.Emitter
            {
                Position = at, Seed = seed, Color = color,
                Shape = new float4(0f, width, bubbleSize, -life),
                Timing = new float4(0f, 0.25f, opacity, particles)
            };

        static Vector3 At(int2 corner, int size, float[] heights, int index, float lift) =>
            new Vector3(corner.x + index % size + 0.5f, heights[index] + lift, corner.y + index / size + 0.5f);

        static Vector3 Centre(int2 corner, int size, float[] heights, List<int> cells)
        {
            var sum = Vector3.zero;
            foreach (var i in cells)
                sum += At(corner, size, heights, i, 0f);
            return sum / cells.Count;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
