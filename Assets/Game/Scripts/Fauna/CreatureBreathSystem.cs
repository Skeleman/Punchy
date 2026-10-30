using Punchy.Ambience;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Animals' breath showing in the cold, as players' does: every warm-blooded creature near the camera exhales a
    /// wisp of vapour every few seconds (quicker when it's running), through AmbienceSystem's puffs, which judge the
    /// cold. Timed from each creature's own clock and individual, so nothing is kept or sent: every machine sees
    /// its own wisps from the creatures it already has.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureBreathSystem : SystemBase
    {
        /// <summary>How far from the camera breath is shown (m), and how long each exhale lasts (s).</summary>
        const float Range = 25f, Exhale = 0.45f;

        EntityQuery query;
        AmbienceSystem ambience;
        int lookedFor = -1000;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var frame = UnityEngine.Time.frameCount;
            if (ambience == null && frame - lookedFor > 120)
            {
                lookedFor = frame;
                ambience = Object.FindAnyObjectByType<AmbienceSystem>();
            }
            var cam = Camera.main;
            // (Nothing to do while it isn't cold where the listener is.)
            if (ambience == null || cam == null || ambience.Cold <= ambience.BreathThreshold)
                return;
            float3 camAt = cam.transform.position;
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (math.distancesq(s.Position, camAt) > Range * Range || s.Gone || s.Mode == CreatureMode.Puppet)
                    continue;   // (players' own bodies breathe through AmbienceSystem already)
                ref var b = ref bodies[i].Blob.Value;
                if (b.ColdBlooded || b.Haunts || b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats)
                    continue;
                // An exhale every few seconds, each individual on its own beat, twice as often running.
                var every = (s.Speed > b.WalkSpeed * 1.5f ? 1.6f : 3.2f) * (0.85f + 0.3f * ((s.Individual >> 8) % 100u) / 100f);
                var u = math.frac((s.Clock + (s.Individual % 97u) * 0.37f) / every);
                if (u * every > Exhale)
                    continue;
                var scale = math.max(s.Scale, 0.1f);
                var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
                var mouth = s.Position + forward * b.BodyLength * scale * 0.6f + new float3(0f, b.StandHeight * scale * (b.Upright ? 0.9f : 1.15f), 0f);
                var velocity = forward * s.Speed;
                // (A wisp to suit its size: a mouse's barely there, a horse's bigger than a person's.)
                var size = math.clamp(b.StandHeight * scale / 1.5f, 0.3f, 1.6f);
                ambience.Breathe(mouth, forward, velocity, size);
            }
            states.Dispose();
            bodies.Dispose();
        }
    }
}
