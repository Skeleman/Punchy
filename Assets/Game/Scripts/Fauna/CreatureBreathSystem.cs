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
        /// <summary>How far from the camera breath is shown (m), and how long a person's exhale lasts (s).</summary>
        const float Range = 25f, Exhale = 0.45f;
        /// <summary>A-90: a person's body extent (StandHeight + BodyLength, m), the size 1 the breath shape is scaled
        /// from, and the size (against a person) below which a body's breath is too little to show.</summary>
        const float PersonExtent = 1.18f, Smallest = 0.1f;
        /// <summary>"-breathsize old": the breath before A-90 (one shape for every size), to compare.</summary>
        public static bool Legacy;
        /// <summary>"-breathlog": log the breath shape per species once, and the live puff count every 5 s.</summary>
        public static bool Log;

        /// <summary>
        /// A-90: how one body breathes in the cold, worked out from its size alone (no species values). Small bodies
        /// breathe less air, more often: a songbird's beat is about 4x a person's, its wisps a few percent of a
        /// person's, faint and brief; big bodies breathe slower and bigger. Total vapour follows body mass.
        /// </summary>
        public struct Shape
        {
            /// <summary>Linear size against a person (1); mass goes as Body cubed.</summary>
            public float Body;
            /// <summary>Seconds between exhales at a walk (half that running), and how long each exhale lasts.</summary>
            public float Every, Exhale;
            /// <summary>Wisps per exhale, their size and opacity against a person's, and their mean lifetime (s).</summary>
            public float Puffs, Size, Opacity, Lifetime;
            /// <summary>Whether it's big enough for its breath to show at all.</summary>
            public bool Shows => Body >= Smallest;
            /// <summary>Wisps a second during an exhale.</summary>
            public float Rate => Puffs / math.max(Exhale, 1e-3f);
        }

        /// <summary>The breath shape of a body standing standHeight and long bodyLength (m, already scaled).</summary>
        public static Shape ShapeFor(float standHeight, float bodyLength)
        {
            var k = math.max(standHeight + bodyLength, 0f) / PersonExtent;
            return new Shape
            {
                Body = k,
                Every = 3.2f * math.clamp(math.pow(k, 0.9f), 0.2f, 2f),
                Exhale = Exhale * math.clamp(math.sqrt(k), 0.25f, 2f),
                Puffs = 13.5f * math.clamp(k, 0.1f, 1.6f),
                Size = math.min(math.pow(k, 1.8f), 3f),
                Opacity = math.clamp(k, 0.15f, 1f),
                Lifetime = 1.5f * math.clamp(math.sqrt(k), 0.3f, 2f),
            };
        }
        /// <summary>Whether players have creature bodies, which breathe here from their own mouths (then AmbienceSystem
        /// leaves players' breath alone).</summary>
        public static bool PlayersBreathe;

        EntityQuery query;
        AmbienceSystem ambience;
        int lookedFor = -1000;
        bool logged;
        float nextLog;

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
            if (Log && !logged)
                LogShapes();
            var cam = Camera.main;
            // (Nothing to do while it isn't cold where the listener is.)
            if (ambience == null || cam == null || ambience.Cold <= ambience.BreathThreshold)
                return;
            float3 camAt = cam.transform.position;
            CreatureSnapshotSystem.Get(World, query, out var states, out var bodies, out var entities);
            var players = false;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                players |= s.Mode == CreatureMode.Puppet;
                if (math.distancesq(s.Position, camAt) > Range * Range || s.Gone)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                if (b.ColdBlooded || b.Haunts || b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats)
                    continue;
                var scale = math.max(s.Scale, 0.1f);
                // (A player's body keeps the breath it always had; A-90 shapes everyone else's to its size.)
                var legacy = Legacy || s.Mode == CreatureMode.Puppet;
                var shape = ShapeFor(b.StandHeight * scale, b.BodyLength * scale);
                if (!legacy && !shape.Shows)
                    continue;
                // An exhale every few seconds, each individual on its own beat, twice as often running.
                var every = (legacy ? 3.2f : shape.Every) * (s.Speed > b.WalkSpeed * 1.5f ? 0.5f : 1f) * (0.85f + 0.3f * ((s.Individual >> 8) % 100u) / 100f);
                var u = math.frac((s.Clock + (s.Individual % 97u) * 0.37f) / every);
                if (u * every > (legacy ? Exhale : shape.Exhale))
                    continue;
                var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
                // (From its own head: a chicken's low, a horse's high up on its neck. A body with no head, a guess.)
                var mouth = b.HasMouth
                    ? s.Position + new float3(0f, b.StandHeight * scale, 0f) + math.mul(quaternion.RotateY(s.Yaw), b.Mouth * scale)
                    : s.Position + forward * b.BodyLength * scale * 0.6f + new float3(0f, b.StandHeight * scale * (b.Upright ? 0.9f : 1.15f), 0f);
                var velocity = forward * s.Speed;
                if (legacy)
                {
                    // (A wisp to suit its size: a mouse's barely there, a horse's bigger than a person's.)
                    ambience.Breathe(mouth, forward, velocity, math.clamp(b.StandHeight * scale / 1.5f, 0.3f, 1.6f));
                    continue;
                }
                // (A flier's breath stays where it was breathed and drifts on the wind, rather than trailing after it.)
                var carry = math.lerp(0.85f, 0.08f, math.saturate(s.Fly));
                ambience.Breathe(mouth, forward, velocity, shape.Size, shape.Rate, shape.Lifetime, shape.Opacity, carry);
            }
            PlayersBreathe = players;
            CreatureSnapshotSystem.Release(World, states, bodies, entities);
            if (Log && UnityEngine.Time.unscaledTime >= nextLog)
            {
                nextLog = UnityEngine.Time.unscaledTime + 5f;
                Debug.Log($"BreathCount: {ambience.PuffCount} puffs alive, cold {ambience.Cold:F2}, {(Legacy ? "old" : "sized")} breath");
            }
        }

        /// <summary>"-breathlog": one BreathShape line per warm-blooded land species at its middle size, and one for a
        /// colossus-sized body (8 m tall, 12 m long), once the director has built its species.</summary>
        void LogShapes()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.BlobAt(0).IsCreated)
                return;
            logged = true;
            var lines = new System.Text.StringBuilder("BreathShape (species: body, beat s, puffs per exhale, puff size, lifetime s, opacity)");
            for (int i = 0; director.BlobAt(i).IsCreated; i++)
            {
                ref var b = ref director.BlobAt(i).Value;
                if (b.ColdBlooded || b.Haunts || b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats)
                    continue;
                var mid = (b.Size.x + b.Size.y) * 0.5f;
                Row(lines, director.SpeciesAt(i) != null ? director.SpeciesAt(i).name : i.ToString(), b.StandHeight * mid, b.BodyLength * mid);
            }
            Row(lines, "(colossus-sized body)", 8f, 12f);
            Debug.Log(lines.ToString());
        }

        static void Row(System.Text.StringBuilder lines, string name, float standHeight, float bodyLength)
        {
            var s = ShapeFor(standHeight, bodyLength);
            lines.Append(s.Shows
                ? $"\n  {name} (h {standHeight:F2}, l {bodyLength:F2}): body {s.Body:F2}, beat {s.Every:F2}, puffs {s.Puffs:F1}, size {s.Size:F3}, life {s.Lifetime:F2}, opacity {s.Opacity:F2}"
                : $"\n  {name} (h {standHeight:F2}, l {bodyLength:F2}): body {s.Body:F2}, no breath shown");
        }
    }
}
