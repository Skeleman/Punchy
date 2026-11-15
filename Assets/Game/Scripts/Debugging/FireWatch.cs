#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Punchy.Fauna;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// A-121 probe: counts, every frame over a long run, the creatures and folk standing in a fire's flames (FireSpots)
    /// round a spot, so "nobody walks through a campfire" is a number. Started and read by eval; lives on a hidden
    /// GameObject while it watches.
    /// </summary>
    public class FireWatch : MonoBehaviour
    {
        static FireWatch active;
        float2 centre;
        float range;
        long frames, near, inside, folkInside, touching;
        float worst;
        readonly HashSet<Entity> everInside = new HashSet<Entity>();
        readonly HashSet<int> fires = new HashSet<int>();

        static EntityManager EM => World.DefaultGameObjectInjectionWorld.EntityManager;

        /// <summary>Starts (or restarts) watching every fire within range of (x, z).</summary>
        public static void Begin(float x, float z, float range)
        {
            if (active == null)
            {
                var go = new GameObject("FireWatch") { hideFlags = HideFlags.HideAndDontSave };
                active = go.AddComponent<FireWatch>();
            }
            active.centre = new float2(x, z);
            active.range = range;
            active.frames = active.near = active.inside = active.folkInside = active.touching = 0;
            active.worst = 0f;
            active.everInside.Clear();
            active.fires.Clear();
        }

        /// <summary>
        /// { frames watched, fires watched, creature-frames within 3 m of a flame, creature-frames with their position
        /// in a flame, of those folk, distinct creatures ever in one, creature-frames with the body overlapping a flame,
        /// the deepest any stood in (m) }.
        /// </summary>
        public static float[] Read() => active == null ? new float[8] :
            new float[] { active.frames, active.fires.Count, active.near, active.inside, active.folkInside, active.everInside.Count, active.touching, active.worst };

        public static void Stop()
        {
            if (active != null)
                Destroy(active.gameObject);
            active = null;
        }

        /// <summary>The landmark fires within range of (x, z): "type x z radius" per line, nearest first.</summary>
        public static string Fires(float x, float z, float range)
        {
            using var tq = EM.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var pq = EM.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!tq.TryGetSingleton<TerrainSource>(out var source) || !source.Landmarks.IsCreated || pq.CalculateEntityCount() != 1)
                return "no world";
            var palette = EM.GetComponentObject<BiomePalette>(pq.GetSingletonEntity());
            ref var set = ref source.Landmarks.Value;
            var from = new float2(x, z);
            var found = new List<(float d, string line)>();
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var type = palette.Landmarks[inst.Type];
                if (type == null)
                    continue;
                foreach (var effect in type.Effects)
                {
                    if (effect.Kind != LandmarkEffectKind.Flame)
                        continue;
                    var at = LandmarkSampling.ToWorld(inst, effect.Offset, set.Types[inst.Type].Radius * inst.Scale);
                    var d = math.distance(at, from);
                    if (d <= range)
                        found.Add((d, $"{type.name} {at.x:F1} {at.y:F1} r{math.max(effect.Width * inst.Scale * 0.5f + effect.Size * inst.Scale * 0.4f, 0.6f):F2}"));
                }
            }
            found.Sort((a, b) => a.d.CompareTo(b.d));
            var text = new StringBuilder();
            foreach (var f in found)
                text.AppendLine(f.line);
            text.Append($"spots listed {FireSpotSystem.LastCounts}");
            return text.ToString();
        }

        void Update()
        {
            using var fq = EM.CreateEntityQuery(ComponentType.ReadOnly<FireSpots>());
            if (!fq.TryGetSingleton<FireSpots>(out var list) || !list.Spots.IsCreated)
                return;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(),
                                               ComponentType.Exclude<PlayerAvatar>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            using var entities = q.ToEntityArray(Allocator.Temp);
            frames++;
            for (int f = 0; f < list.Spots.Length; f++)
            {
                var fire = list.Spots[f];
                if ((fire.Flags & FireSpots.Ground) == 0 || math.distance(fire.Position.xz, centre) > range)
                    continue;
                fires.Add(f);
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    ref var b = ref bodies[i].Blob.Value;
                    // (Fliers in the air, anything carried and ghosts are not walking on the ground.)
                    if (s.Mode == CreatureMode.Puppet || s.Mode == CreatureMode.Held || s.Fly >= 0.5f || b.Haunts ||
                        math.abs(s.Position.y - fire.Position.y) > 3f)
                        continue;
                    var d = math.distance(s.Position.xz, fire.Position.xz);
                    var body = math.max(math.max(b.FrontRadius, b.RearRadius) * s.Scale, 0.1f);
                    if (d < fire.Radius + 3f)
                        near++;
                    if (d < fire.Radius + body)
                        touching++;
                    if (d < fire.Radius)
                    {
                        inside++;
                        if (b.Upright && b.Hands)
                            folkInside++;
                        everInside.Add(entities[i]);
                        worst = math.max(worst, fire.Radius - d);
                    }
                }
            }
        }
    }
}
#endif
