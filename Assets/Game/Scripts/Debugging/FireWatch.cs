#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Punchy.Fauna;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
        // Row 2: creature-frames per fire band (lover, basks, wary, shy, folk, other) and ring past the flame's edge
        // (0-2, 2-5, 5-10 m), and the distinct ones seen within 10 m.
        static readonly string[] BandNames = { "lover", "basks", "wary", "shy", "folk", "other" };
        readonly long[,] rings = new long[6, 3];
        readonly HashSet<Entity>[] seen = { new(), new(), new(), new(), new(), new() };

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
            System.Array.Clear(active.rings, 0, active.rings.Length);
            foreach (var h in active.seen)
                h.Clear();
        }

        /// <summary>
        /// { frames watched, fires watched, creature-frames within 3 m of a flame, creature-frames with their position
        /// in a flame, of those folk, distinct creatures ever in one, creature-frames with the body overlapping a flame,
        /// the deepest any stood in (m) }.
        /// </summary>
        public static float[] Read() => active == null ? new float[8] :
            new float[] { active.frames, active.fires.Count, active.near, active.inside, active.folkInside, active.everInside.Count, active.touching, active.worst };

        /// <summary>
        /// Row 2: per band, "name distinct r0-2/r2-5/r5-10" (creature-frames past the flame's edge, divided by frames
        /// so it reads as the average count standing in that ring).
        /// </summary>
        public static string Bands()
        {
            if (active == null || active.frames == 0)
                return "not watching";
            var text = new StringBuilder();
            for (int k = 0; k < BandNames.Length; k++)
                text.Append($"{BandNames[k]} {active.seen[k].Count} {active.rings[k, 0] / (float)active.frames:F2}/{active.rings[k, 1] / (float)active.frames:F2}/{active.rings[k, 2] / (float)active.frames:F2}; ");
            return text.ToString();
        }

        static int BandOf(ref CreatureBlob b)
        {
            if (b.Upright && b.Hands)
                return 4;
            if (b.Comfort.LovesFire)
                return 0;
            if ((b.Comfort.Seeks & ShelterNeed.Fire) != 0 && b.Comfort.FireShy > 0.3f && b.Comfort.FireShy < 0.5f)
                return 1;
            if (b.Comfort.FireShy > 0.5f)
                return 3;
            if (b.Comfort.FireShy > 0.1f)
                return 2;
            return 5;
        }

        /// <summary>Row 2 probe set-up: puts the player at (x, z) on the ground and holds the clock at this hour.</summary>
        public static string Goto(float x, float z, float hour)
        {
            using var tq = EM.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var pq = EM.CreateEntityQuery(ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadOnly<PlayerTag>());
            if (!tq.TryGetSingleton<TerrainSource>(out var source) || pq.CalculateEntityCount() < 1)
                return "no world";
            using var players = pq.ToEntityArray(Allocator.Temp);
            var player = players[0];
            var t = EM.GetComponentData<LocalTransform>(player);
            t.Position = new float3(x, source.SampleHeight(new float2(x, z)) + 0.1f, z);
            EM.SetComponentData(player, t);
            if (EM.HasComponent<PlayerMotion>(player))
                EM.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cycle = UnityEngine.Object.FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            if (cycle != null)
            {
                cycle.TimeOfDay = hour;
                cycle.Paused = true;
            }
            return $"at {t.Position} hour {hour}";
        }

        /// <summary>
        /// Row 2 probe set-up: moves up to <paramref name="count"/> walking animals and folk from within
        /// <paramref name="range"/> of the fire at (x, z) to a ring 1-3 m past its flames, standing idle, so where each
        /// band drifts to over the next minute or two can be counted. Returns how many by band.
        /// </summary>
        public static string Gather(float x, float z, float range, int count)
        {
            using var fq = EM.CreateEntityQuery(ComponentType.ReadOnly<FireSpots>());
            if (!fq.TryGetSingleton<FireSpots>(out var list) || !list.Spots.IsCreated)
                return "no fires";
            var fire = default(FireSpot);
            var best = float.MaxValue;
            foreach (var f in list.Spots)
            {
                var d = math.distance(f.Position.xz, new float2(x, z));
                if ((f.Flags & FireSpots.Ground) != 0 && d < best)
                {
                    best = d;
                    fire = f;
                }
            }
            using var q = EM.CreateEntityQuery(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(),
                                               ComponentType.Exclude<PlayerAvatar>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var moved = new int[6];
            var n = 0;
            var rng = new Unity.Mathematics.Random(0xF1AEu);
            foreach (var e in entities)
            {
                var s = EM.GetComponentData<CreatureState>(e);
                var body = EM.GetComponentData<CreatureBody>(e);
                ref var b = ref body.Blob.Value;
                if (n >= count || !b.Comfort.Minds || b.Habitat != Habitat.Land || b.Haunts || s.Fly >= 0.5f ||
                    s.Mode is CreatureMode.Puppet or CreatureMode.Held || s.Holder != 0 ||
                    math.distance(s.Position.xz, fire.Position.xz) > range)
                    continue;
                var a = rng.NextFloat(math.PI * 2f);
                var at = fire.Position.xz + new float2(math.sin(a), math.cos(a)) * (fire.Radius + rng.NextFloat(1f, 3f));
                s.Position = new float3(at.x, fire.Position.y, at.y);
                s.Target = at;
                s.Mode = CreatureMode.Idle;
                s.Timer = 2f;
                s.Speed = 0f;
                EM.SetComponentData(e, s);
                if (EM.HasComponent<LocalTransform>(e))
                {
                    var t = EM.GetComponentData<LocalTransform>(e);
                    t.Position = s.Position;
                    EM.SetComponentData(e, t);
                }
                moved[BandOf(ref b)]++;
                n++;
            }
            var text = new StringBuilder($"fire {fire.Position} heat {fire.Heat:F2} moved {n}: ");
            for (int k = 0; k < BandNames.Length; k++)
                text.Append($"{BandNames[k]} {moved[k]} ");
            return text.ToString();
        }

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
                    var past = d - fire.Radius;
                    if (past < 10f && b.Comfort.Minds)
                    {
                        var band = BandOf(ref b);
                        rings[band, past < 2f ? 0 : past < 5f ? 1 : 2]++;
                        seen[band].Add(entities[i]);
                    }
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
