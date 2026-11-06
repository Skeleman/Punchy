using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A skunk's gas (any species with FleeGas): as it switches into Flee it puffs a short burst of soft green gas from
    /// its rear, then leaves a thinning trail for a couple of seconds that drifts and spreads with the wind. Once per
    /// flee start, then not again for 10 s. Cosmetic: each client sees its own copy of the creature change mode (the
    /// mode is already shared), so nothing new goes over the network. One pooled particle system, made on first use;
    /// with no FleeGas species nearby it only looks for one every couple of seconds.
    /// </summary>
    public class SkunkGas : MonoBehaviour
    {
        const float Cooldown = 10f, TrailTime = 2.5f, CheckEvery = 0.1f, FindEvery = 2f, Range = 90f;
        const int BurstCount = 60;
        // (A yellow-green, nearly opaque at the core; the gas material draws it unlit, so not too bright.)
        static readonly Color Gas = new Color(0.62f, 0.78f, 0.18f, 0.9f);

        struct Trail { public Entity Creature; public float Start, Owed; }

        World queryWorld;
        EntityQuery bodies;
        ParticleSystem gas;
        Punchy.Ambience.AmbienceSystem ambience;
        Material gasMaterial;
        readonly HashSet<Entity> gassers = new HashSet<Entity>();
        readonly Dictionary<Entity, int> found = new Dictionary<Entity, int>();
        readonly Dictionary<Entity, CreatureMode> lastMode = new Dictionary<Entity, CreatureMode>();
        readonly Dictionary<Entity, float> lastPuff = new Dictionary<Entity, float>();
        readonly List<Trail> trails = new List<Trail>();
        float nextCheck, nextFind;
        // For the log: puffs so far, the most particles alive at once, and the cost of the frames with gas about.
        int puffs, peak, costFrames;
        double costTotal, costMax;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("SkunkGas");
            DontDestroyOnLoad(go);
            go.AddComponent<SkunkGas>();
        }

        void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            if (queryWorld != world)
            {
                queryWorld = world;
                bodies = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                gassers.Clear();
                lastMode.Clear();
                trails.Clear();
            }
            var busy = gassers.Count > 0 || trails.Count > 0;
            var started = busy ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            var camera = Camera.main;
            if (camera == null)
                return;
            var eye = (float3)camera.transform.position;
            var find = Time.time >= nextFind;
            var check = gassers.Count > 0 && Time.time >= nextCheck;
            if (find || check || trails.Count > 0)
                Look(world, eye, find, check);

            if (gas != null && gas.particleCount > 0)
                peak = math.max(peak, gas.particleCount);
            if (busy)
            {
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                costTotal += ms;
                costMax = math.max(costMax, ms);
                costFrames++;
                if (trails.Count == 0 && puffs > 0 && costFrames > 0 && Time.frameCount % 600 == 0)
                    Log();
            }
        }

        void OnDestroy()
        {
            if (puffs > 0)
                Log();
        }

        void Log() => Debug.Log($"SkunkGas: {puffs} puffs, peak {peak} particles, cost {costTotal / math.max(costFrames, 1):0.000} ms/frame avg, {costMax:0.000} ms max over {costFrames} frames");

        /// <summary>Finds the gassers, puffs for the ones that just turned to run, and feeds the trails.</summary>
        void Look(World world, float3 eye, bool find, bool check)
        {
            // A-83 (#62): the creatures as the shared snapshot last had them (a frame behind at most), not read from
            // the entities, which waited on the brain whenever it was still running.
            CreatureSnapshotSystem.Get(world, bodies, out var states, out var all, out var entities);

            // Every couple of seconds: which FleeGas creatures are near enough to see.
            if (find)
            {
                nextFind = Time.time + FindEvery;
                gassers.Clear();
                for (int i = 0; i < all.Length; i++)
                    if (all[i].Blob.IsCreated && all[i].Blob.Value.FleeGas && math.distancesq(states[i].Position, eye) < Range * Range)
                    {
                        gassers.Add(entities[i]);
                        if (!lastMode.ContainsKey(entities[i]))
                            lastMode[entities[i]] = states[i].Mode;
                    }
            }

            // Where in the snapshot the gassers and the trailing ones are (gone since, if missing).
            found.Clear();
            if (check || trails.Count > 0)
                for (int i = 0; i < entities.Length; i++)
                    if (gassers.Contains(entities[i]) || Trailing(entities[i]))
                        found[entities[i]] = i;

            // A few times a second: has one just turned to run?
            if (check)
            {
                nextCheck = Time.time + CheckEvery;
                foreach (var e in gassers)
                {
                    if (!found.TryGetValue(e, out var i))
                        continue;
                    var s = states[i];
                    var was = lastMode.TryGetValue(e, out var m) ? m : s.Mode;
                    lastMode[e] = s.Mode;
                    if (s.Mode != CreatureMode.Flee || was == CreatureMode.Flee)
                        continue;
                    if (lastPuff.TryGetValue(e, out var when) && Time.time - when < Cooldown)
                        continue;
                    lastPuff[e] = Time.time;
                    puffs++;
                    Emit(s, all[i], BurstCount, 1f);
                    trails.Add(new Trail { Creature = e, Start = Time.time });
                    Debug.Log($"SkunkGas: puff {puffs} (entity {e.Index}) at {s.Position.x:0.0},{s.Position.y:0.0},{s.Position.z:0.0}");
                }
                if (lastPuff.Count > 64)
                    lastPuff.Clear();
            }

            // The trail: thinning over TrailTime, following the creature as it runs.
            for (int k = trails.Count - 1; k >= 0; k--)
            {
                var t = trails[k];
                var age = Time.time - t.Start;
                if (age > TrailTime || !found.TryGetValue(t.Creature, out var i))
                {
                    trails.RemoveAt(k);
                    continue;
                }
                var thin = 1f - age / TrailTime;
                t.Owed += Time.deltaTime * 50f * thin * thin;
                var n = (int)t.Owed;
                t.Owed -= n;
                trails[k] = t;
                if (n > 0)
                    Emit(states[i], all[i], n, 0.55f + 0.45f * thin);
            }
            CreatureSnapshotSystem.Release(world, states, all, entities);
        }

        bool Trailing(Entity e)
        {
            foreach (var t in trails)
                if (t.Creature == e)
                    return true;
            return false;
        }

        /// <summary>Puffs n particles from the creature's rear, blown back and drifting with the wind.</summary>
        void Emit(in CreatureState s, in CreatureBody body, int n, float strength)
        {
            if ((gas == null && !CreateGas()) || !body.Blob.IsCreated)
                return;
            ref var b = ref body.Blob.Value;
            var size = math.max(s.Scale, 0.6f);
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var rear = s.Position + new float3(0f, (b.StandHeight + b.RearRadius * b.BodyFlatten) * size, 0f) - forward * b.BodyLength * 0.55f * size;
            var wind = float3.zero;
            var weather = ambience != null ? ambience.Weather : null;
            if (weather != null)
                wind = new float3(weather.WindDirection.x, 0f, weather.WindDirection.y) * weather.Local.Wind * 0.9f;
            for (int k = 0; k < n; k++)
            {
                var spread = (float3)UnityEngine.Random.insideUnitSphere;
                gas.Emit(new ParticleSystem.EmitParams
                {
                    // (A little above the rump, so the soft edge where a puff meets the ground doesn't eat the cloud.)
                    position = rear + spread * 0.06f * size + new float3(0f, 0.12f * size, 0f),
                    velocity = -forward * UnityEngine.Random.Range(0.3f, 0.9f) * strength + spread * 0.3f + new float3(0f, 0.15f, 0f) + wind,
                    startSize = UnityEngine.Random.Range(0.35f, 0.6f) * size * strength,
                    startLifetime = UnityEngine.Random.Range(2.2f, 3.4f),
                    startColor = new Color(Gas.r, Gas.g, Gas.b, Gas.a * math.sqrt(strength)),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>The pooled system: swelling, slowing, fading puffs. On its own unlit gas material (Resources/SkunkGas,
        /// Punchy/Gas Puff, A-43): the ambience's soft puff material dims puffs and fades them near the ground, and the
        /// cloud didn't show in builds. The ambience's puff material only if that one is missing.</summary>
        bool CreateGas()
        {
            if (gasMaterial == null)
                gasMaterial = Resources.Load<Material>("SkunkGas");
            if (gasMaterial == null)
            {
                if (ambience == null)
                    ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
                if (ambience != null)
                    gasMaterial = ambience.PuffMaterial;
            }
            if (gasMaterial == null)
                return false;
            var go = new GameObject("SkunkGasPuffs");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            gas = go.AddComponent<ParticleSystem>();
            var main = gas.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            main.startSpeed = 0f;
            main.gravityModifier = -0.01f;
            var emission = gas.emission;
            emission.enabled = false;
            var shape = gas.shape;
            shape.enabled = false;
            var grow = gas.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 3.5f)));
            var drag = gas.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.4f;
            drag.multiplyDragByParticleSize = false;
            var color = gas.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.85f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var spin = gas.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = gasMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            gas.Play();
            return true;
        }
    }
}
