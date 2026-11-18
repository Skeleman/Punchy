using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// W-89 (#40): keeps the ElectricWastelands rock golem (StoneColossi.BuildGolem) floating apart and wired together.
    /// Each rock segment bobs and turns slowly on its own phase, and the floating rocks' arcs (ElectricitySystem, the same
    /// electricity the arcing spires fire) are drawn across every linked gap each frame, from rock surface to rock surface, so
    /// the arcs follow the rocks as they drift. Cosmetic: the bob runs on local time and the arcs on the shared clock; nothing here is sent.
    /// </summary>
    public class GolemArcs : MonoBehaviour
    {
        /// <summary>Arc width: a colossus's arcs are this many times a spire's.</summary>
        const float Thickness = 7f;

        readonly List<Transform> segments = new List<Transform>();
        readonly List<Vector3> rest = new List<Vector3>();
        readonly List<float> radius = new List<float>(), bob = new List<float>(), phase = new List<float>();
        readonly List<(int a, int b, Vector3 offset, float scale)> links = new List<(int, int, Vector3, float)>();
        int seed;

        /// <summary>Adds a rock that bobs <paramref name="amplitude"/> m (0 holds it still); returns its index for <see cref="Link"/>.</summary>
        public int AddSegment(Transform t, float surface, float amplitude, float offset)
        {
            segments.Add(t);
            rest.Add(t.localPosition);
            radius.Add(surface);
            bob.Add(amplitude);
            phase.Add(offset);
            return segments.Count - 1;
        }

        public Transform Segment(int i) => segments[i];

        /// <summary>Wires two rocks with <paramref name="count"/> arcs, spread a little apart so they don't overlap.</summary>
        public void Link(int a, int b, int count, float scale = 1f)
        {
            for (int k = 0; k < count; k++)
            {
                var h = math.hash(new int3(a, b, k));
                var offset = count == 1 ? Vector3.zero : new Vector3((h & 0xFF) / 255f - 0.5f, ((h >> 8) & 0xFF) / 255f - 0.5f, ((h >> 16) & 0xFF) / 255f - 0.5f) * 3f;
                links.Add((a, b, offset, scale));
            }
        }

        void Update()
        {
            var t = Time.time;
            for (int i = 0; i < segments.Count; i++)
            {
                if (bob[i] <= 0f)
                    continue;
                var p = phase[i];
                segments[i].localPosition = rest[i] + new Vector3(math.sin(t * 0.23f + p * 1.7f) * 0.3f, math.sin(t * 0.5f + p), math.cos(t * 0.31f + p * 2.3f) * 0.3f) * bob[i];
                segments[i].localRotation = Quaternion.Euler(math.sin(t * 0.37f + p) * 3f * bob[i], math.sin(t * 0.11f + p * 0.7f) * 8f, math.cos(t * 0.29f + p) * 3f * bob[i]);
            }
        }

        void LateUpdate()
        {
            // R-29: the arcs are the shared electricity (ElectricitySystem), which re-jags, forks, lights and cracks
            // them. The seed is the golem's place when it formed plus the link, never where the rocks drift to.
            if (seed == 0)
                seed = (int)math.hash((int3)math.round((float3)transform.position)) | 1;
            var scale = transform.lossyScale.x;
            for (int i = 0; i < links.Count; i++)
            {
                var (a, b, offset, s) = links[i];
                Vector3 pa = segments[a].position, pb = segments[b].position;
                var dir = (pb - pa).normalized;
                var from = pa + (dir * radius[a] + offset) * scale;
                var to = pb - (dir * radius[b] - offset) * scale;
                if ((to - from).sqrMagnitude < 0.01f)
                    continue;
                // One link cracks for the whole golem, as one random crack did before.
                Punchy.Rendering.Effects.ElectricitySystem.AddArc(from, to, (int)math.hash(new int2(seed, i)),
                    Punchy.Rendering.Effects.ElectricitySystem.Blue, Thickness * s, quiet: i != 0);
            }
        }
    }
}
