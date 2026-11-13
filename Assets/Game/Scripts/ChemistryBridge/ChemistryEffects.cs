using System.Collections.Generic;
using Punchy.Ambience;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Punchy.Chemistry
{
    /// <summary>
    /// What the chemistry window shows besides the shaders' map (ChemistryBridge, see README.md): a flame on every
    /// burning cell through Punchy's standard fire (FireSystem: flame, sparks, glow and a little smoke, so a grass fire
    /// looks like the island's other fires) plus one fire light, and soft billboard puffs from the gas cells in two
    /// pooled ParticleSystems (no Shader.Find): white steam that rises and fades with the gas on the skunk's gas-cloud
    /// material (Resources/SkunkGas, Punchy/Gas Puff: unlit and not faded at the ground, so it reads plainly), and grey
    /// smoke on the breath-puff material (AmbienceSystem.PuffMaterial, Punchy/Soft Particle, lit, so it darkens at
    /// night). The bridge hands it the cell lists after
    /// each tick; it adds the flames every frame and emits a capped number of puffs a frame. Cosmetic and local only.
    /// </summary>
    public sealed class ChemistryEffects
    {
        /// <summary>A cell that shows something: its window index and how strong (fire level, gas amount).</summary>
        public struct Spot
        {
            public int Index;
            public int Amount;
        }

        // Flames past this distance from the camera (m) are far flames (no sparks or smoke, FireSystem.AddFarFlame).
        const float NearFlames = 70f, CloseFlames = 35f;
        // At most this many puffs are emitted a frame, and alive at once.
        const int PuffsPerFrame = 48, SmokePerFrame = 16, MaxPuffs = 2500;
        // Puffs a second per gas cell at full amount.
        const float SteamRate = 4f, SmokeRate = 0.8f;
        const int LightId = 0x43484D;   // the fire light's LightBudget id (any stable number)

        public readonly List<Spot> Burning = new List<Spot>(), Steam = new List<Spot>(), Smoke = new List<Spot>(), Melted = new List<Spot>(), Doused = new List<Spot>();

        ParticleSystem steamPuffs, smokePuffs;
        bool madePuffs;

        /// <summary>Puffs alive now (steam and smoke).</summary>
        public int PuffCount => (steamPuffs != null ? steamPuffs.particleCount : 0) + (smokePuffs != null ? smokePuffs.particleCount : 0);

        public void Dispose()
        {
            if (steamPuffs != null)
                Object.Destroy(steamPuffs.gameObject);
            if (smokePuffs != null)
                Object.Destroy(smokePuffs.gameObject);
            steamPuffs = smokePuffs = null;
            madePuffs = false;
        }

        /// <summary>Adds this frame's flames and emits this frame's puffs. heights: the ground (or water) at each cell.</summary>
        public void Frame(int2 corner, int size, float[] heights, int maxGas, Camera cam)
        {
            if (cam == null || heights == null)
                return;
            var eye = cam.transform.position;
            Flames(corner, size, heights, eye);
            if (Steam.Count == 0 && Smoke.Count == 0 && Melted.Count == 0 && Doused.Count == 0)
                return;
            EnsurePuffs();
            var wind = (Vector3)new float3(Punchy.Weather.Climate.CameraWind.x, 0f, Punchy.Weather.Climate.CameraWind.y) * 0.6f;
            var emitted = 0;
            // A wisp of vapour off each cell of ice as it melts (cosmetic: the melt itself makes no steam).
            foreach (var m in Melted)
                if (emitted++ < PuffsPerFrame)
                    Emit(steamPuffs, CellPoint(corner, size, heights, m.Index, 0.3f), 0.8f, 0.9f, 1.6f, new Color(0.9f, 0.93f, 0.95f, 0.5f), wind);
            Melted.Clear();
            // A burst of steam off each cell the water puts out (the hiss; the steam gas it leaves keeps puffing after).
            foreach (var d in Doused)
                for (int k = 0; k < 3; k++)
                    if (emitted++ < PuffsPerFrame * 2)
                        Emit(steamPuffs, CellPoint(corner, size, heights, d.Index, 0.3f), 1.4f, 1.6f, 2.4f, new Color(0.92f, 0.94f, 0.96f, 0.7f), wind);
            Doused.Clear();
            emitted += EmitFrom(Steam, SteamRate, maxGas, PuffsPerFrame - emitted, corner, size, heights, wind, true);
            // (Smoke gets a smaller share, so a big grass fire's smoke never crowds out the steam.)
            EmitFrom(Smoke, SmokeRate, maxGas, math.min(SmokePerFrame, PuffsPerFrame - emitted), corner, size, heights, wind, false);
        }

        void Flames(int2 corner, int size, float[] heights, Vector3 eye)
        {
            if (Burning.Count == 0)
                return;
            float3 sum = 0f;
            var near = 0;
            foreach (var b in Burning)
            {
                int x = b.Index % size, y = b.Index / size;
                var seed = Hash(corner.x + x, corner.y + y);
                // Jittered inside the cell so the front doesn't read as a grid of candles.
                var at = new Vector3(corner.x + x + 0.2f + 0.6f * Frac(seed, 1), heights[b.Index], corner.y + y + 0.2f + 0.6f * Frac(seed, 2));
                // A grass fire's flames: knee to head high by how fierce the cell burns.
                var height = (0.7f + 0.3f * math.min(b.Amount, 3)) * (0.75f + 0.5f * Frac(seed, 3));
                var distance2 = (at - eye).sqrMagnitude;
                if (distance2 < NearFlames * NearFlames)
                {
                    FireSystem.AddFlame(at, height, (int)seed, FireSystem.Wood);
                    // Close up, a second, lower tongue elsewhere in the cell so the front reads as a sheet of flame.
                    if (distance2 < CloseFlames * CloseFlames)
                    {
                        var second = new Vector3(corner.x + x + 0.15f + 0.7f * Frac(seed, 4), at.y, corner.y + y + 0.15f + 0.7f * Frac(seed, 5));
                        FireSystem.AddFlame(second, height * 0.65f, (int)(seed ^ 0x5bd1e995u), FireSystem.Wood);
                    }
                    sum += (float3)at;
                    near++;
                }
                else
                {
                    FireSystem.AddFarFlame(at, height, (int)seed, FireSystem.Wood);
                }
            }
            // One light for the near fire, at its middle, brighter the more there is (the LightBudget picks the slot).
            if (near > 0)
            {
                var t = Time.time;
                var flicker = 0.85f + 0.1f * Mathf.Sin(t * 8.3f) + 0.05f * Mathf.Sin(t * 21.1f);
                var light = FireSystem.Light(FireSystem.Wood, flicker * math.clamp(0.6f + near * 0.02f, 0.6f, 2f));
                light.w *= math.clamp(1f + near * 0.01f, 1f, 2.5f);
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fire, LightId, (Vector3)(sum / near) + Vector3.up * 1.2f, light);
            }
        }

        int EmitFrom(List<Spot> spots, float rate, int maxGas, int budget, int2 corner, int size, float[] heights, Vector3 wind, bool steam)
        {
            var ps = steam ? steamPuffs : smokePuffs;
            if (spots.Count == 0 || budget <= 0 || ps == null)
                return 0;
            // (Whole puffs due this frame, plus one more by chance for the fraction left over.)
            var due = spots.Count * rate * Time.deltaTime;
            var count = math.min(budget, (int)due + (Random.value < due - (int)due ? 1 : 0));
            for (int i = 0; i < count; i++)
            {
                var s = spots[Random.Range(0, spots.Count)];
                var amount = math.saturate(s.Amount / (float)math.max(1, maxGas));
                // A thin gas puffs less often and fainter, so steam fades away as the gas does.
                if (Random.value > 0.35f + 0.65f * amount)
                    continue;
                var at = CellPoint(corner, size, heights, s.Index, 0.4f);
                if (steam)
                    Emit(ps, at, 1.2f + 0.8f * amount, 1.1f, 3f, new Color(0.9f, 0.92f, 0.95f, 0.3f + 0.45f * amount), wind);
                else
                    Emit(ps, at + Vector3.up * 0.8f, 0.9f + 0.6f * amount, 0.9f, 3.5f, new Color(0.2f, 0.19f, 0.18f, 0.35f + 0.35f * amount), wind);
            }
            return count;
        }

        static void Emit(ParticleSystem ps, Vector3 at, float size, float rise, float life, Color color, Vector3 wind)
        {
            if (ps == null)
                return;
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = new Vector3(0f, rise * Random.Range(0.75f, 1.25f), 0f) + wind + Random.insideUnitSphere * 0.15f,
                startSize = size * Random.Range(0.8f, 1.2f),
                startLifetime = life * Random.Range(0.8f, 1.2f),
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false
            }, 1);
        }

        static Vector3 CellPoint(int2 corner, int size, float[] heights, int index, float lift)
        {
            int x = index % size, y = index / size;
            return new Vector3(corner.x + x + Random.value, heights[index] + lift, corner.y + y + Random.value);
        }

        void EnsurePuffs()
        {
            if (madePuffs)
                return;
            madePuffs = true;
            var ambience = Object.FindAnyObjectByType<AmbienceSystem>();
            var smoke = ambience != null ? ambience.PuffMaterial : null;
            var steam = Resources.Load<Material>("SkunkGas");
            if (steam == null || smoke == null)
                Debug.LogWarning($"[Chemistry] puff materials missing (steam {steam != null}, smoke {smoke != null}): those puffs are off");
            if (steam != null)
                steamPuffs = CreatePuffs("ChemistrySteam", steam, MaxPuffs);
            if (smoke != null)
                smokePuffs = CreatePuffs("ChemistrySmoke", smoke, MaxPuffs / 2);
        }

        static ParticleSystem CreatePuffs(string name, Material material, int max)
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
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            // Puffs swell and slow as they mix with the air, and fade in quickly and out slowly.
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3f)));
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.6f;
            drag.multiplyDragByParticleSize = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        static uint Hash(int x, int y)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u;
            h ^= h >> 13;
            h *= 0x85ebca6bu;
            h ^= h >> 16;
            return h;
        }

        static float Frac(uint seed, uint salt)
        {
            uint h = seed ^ (salt * 0x9e3779b9u);
            h ^= h >> 15;
            h *= 0x2c1b3c6du;
            h ^= h >> 12;
            return (h & 0xffffff) / 16777216f;
        }
    }
}
