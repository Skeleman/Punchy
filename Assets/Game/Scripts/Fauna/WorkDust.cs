using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-106 row 1: the dust harsh-biome work kicks up. A sweeper's broom puffs grey ash at the end of each stroke; a
    /// shoveller's toss throws a spray of snow or sand onto its pile. Coloured by the job's work set (FolkWorkSets.DustOf)
    /// and drifting with the wind. Cosmetic: timed off each folk's own stroke (CreatureAnimator.SweepPhase/ShovelPhase,
    /// from its shared clock and Individual) and its job, which clients read off its Stage, so nothing new is sent. One
    /// pooled particle system, made on first use; with no such worker near it only looks every couple of seconds.
    /// </summary>
    public class WorkDust : MonoBehaviour
    {
        const float FindEvery = 2f, Range = 60f;

        World queryWorld;
        EntityQuery bodies;
        ParticleSystem dust;
        Material dustMaterial;
        Punchy.Ambience.AmbienceSystem ambience;
        readonly HashSet<Entity> workers = new HashSet<Entity>();
        readonly Dictionary<Entity, float> lastPhase = new Dictionary<Entity, float>();
        float nextFind;
        int puffs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("WorkDust");
            DontDestroyOnLoad(go);
            go.AddComponent<WorkDust>();
        }

        static bool Dusty(FolkJob job) => job == FolkJob.AshSweeper || job == FolkJob.SnowShoveller || job == FolkJob.SandClearer;

        void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            if (queryWorld != world)
            {
                queryWorld = world;
                bodies = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                workers.Clear();
                lastPhase.Clear();
            }
            var find = Time.time >= nextFind;
            if (!find && workers.Count == 0)
                return;
            var camera = Camera.main;
            if (camera == null)
                return;
            var eye = (float3)camera.transform.position;
            CreatureSnapshotSystem.Get(world, bodies, out var states, out var all, out var entities);
            if (find)
            {
                nextFind = Time.time + FindEvery;
                workers.Clear();
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Mode == CreatureMode.Work && states[i].Stage != 0 && Dusty(FolkJobs.Of(states[i])) &&
                        math.distancesq(states[i].Position, eye) < Range * Range)
                        workers.Add(entities[i]);
                if (lastPhase.Count > 64)
                    lastPhase.Clear();
            }
            if (workers.Count > 0)
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!workers.Contains(entities[i]))
                        continue;
                    var s = states[i];
                    var job = FolkJobs.Of(s);
                    if (s.Mode != CreatureMode.Work || s.Stage == 0 || !Dusty(job))
                        continue;
                    var sweep = job == FolkJob.AshSweeper;
                    var phase = sweep ? CreatureAnimator.SweepPhase(s) : CreatureAnimator.ShovelPhase(s);
                    var was = lastPhase.TryGetValue(entities[i], out var p) ? p : phase;
                    lastPhase[entities[i]] = phase;
                    // (A broom puffs at each end of its stroke; a shovel at the top of its toss.)
                    var hit = sweep ? Crossed(was, phase, 0.25f) || Crossed(was, phase, 0.75f) : Crossed(was, phase, 0.72f);
                    if (hit)
                        Emit(s, job, sweep);
                }
            CreatureSnapshotSystem.Release(world, states, all, entities);
        }

        /// <summary>Whether the phase passed the mark since last frame (wrapping at 1).</summary>
        static bool Crossed(float was, float now, float mark) =>
            now >= was ? was < mark && now >= mark : was < mark || now >= mark;

        void Emit(in CreatureState s, FolkJob job, bool sweep)
        {
            if (dust == null && !CreateDust())
                return;
            var colour = FolkWorkSets.DustOf(job);
            if (colour.a <= 0f)
                return;
            puffs++;
            var size = math.max(s.Scale, 0.6f);
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var right = new float3(forward.z, 0f, -forward.x);
            // (A broom's head is low in front; a shovel's load lands on the pile at its right.)
            var at = sweep ? s.Position + forward * 0.75f * size + new float3(0f, 0.08f, 0f)
                           : s.Position + right * 1f * size + forward * 0.2f * size + new float3(0f, 0.45f * size, 0f);
            var wind = float3.zero;
            var weather = ambience != null ? ambience.Weather : null;
            if (weather != null)
                wind = new float3(weather.WindDirection.x, 0f, weather.WindDirection.y) * weather.Local.Wind * 0.7f;
            var n = sweep ? 5 : 8;
            for (int k = 0; k < n; k++)
            {
                var spread = (float3)UnityEngine.Random.insideUnitSphere;
                dust.Emit(new ParticleSystem.EmitParams
                {
                    position = at + spread * 0.12f * size,
                    velocity = (sweep ? forward * 0.4f : right * 0.5f) + spread * 0.35f + new float3(0f, sweep ? 0.25f : 0.1f, 0f) + wind,
                    startSize = UnityEngine.Random.Range(0.15f, 0.3f) * size,
                    startLifetime = UnityEngine.Random.Range(1f, 1.8f),
                    startColor = colour,
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>The pooled system: small swelling, slowing, fading puffs on the gas puff material (Resources/SkunkGas),
        /// or the ambience's puff material if that one is missing.</summary>
        bool CreateDust()
        {
            if (ambience == null)
                ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            if (dustMaterial == null)
                dustMaterial = Resources.Load<Material>("SkunkGas");
            if (dustMaterial == null && ambience != null)
                dustMaterial = ambience.PuffMaterial;
            if (dustMaterial == null)
                return false;
            var go = new GameObject("WorkDustPuffs");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            dust = go.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startSpeed = 0f;
            main.gravityModifier = 0.02f;
            var emission = dust.emission;
            emission.enabled = false;
            var shape = dust.shape;
            shape.enabled = false;
            var grow = dust.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2.6f)));
            var drag = dust.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.8f;
            drag.multiplyDragByParticleSize = false;
            var color = dust.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            dust.Play();
            Debug.Log("WorkDust: puff system made");
            return true;
        }
    }
}
