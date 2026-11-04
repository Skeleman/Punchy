using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    /// <summary>
    /// W-89 (#40): keeps the ElectricWastelands rock golem (StoneColossi.BuildGolem) floating apart and wired together.
    /// Each rock segment bobs and turns slowly on its own phase, and the floating rocks' arcs (ArcBolt.shader, the same
    /// bolts the arcing spires fire) are drawn across every linked gap each frame, from rock surface to rock surface, so
    /// the arcs follow the rocks as they drift. Cosmetic and driven by local time only: nothing here is shared.
    /// </summary>
    public class GolemArcs : MonoBehaviour
    {
        /// <summary>Bolt matrix x/y scale: ArcBolt is sized for spires, a colossus's arcs are this many times thicker.</summary>
        const float Thickness = 7f;

        readonly List<Transform> segments = new List<Transform>();
        readonly List<Vector3> rest = new List<Vector3>();
        readonly List<float> radius = new List<float>(), bob = new List<float>(), phase = new List<float>();
        readonly List<(int a, int b, Vector3 offset, float scale)> links = new List<(int, int, Vector3, float)>();
        readonly List<Matrix4x4> firing = new List<Matrix4x4>();
        Material material;
        Mesh mesh;
        float nextCrack;

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
            if (material == null)
            {
                material = Resources.Load<Material>("ArcBolt");
                mesh = Punchy.Flora.FloraSystem.BoltMesh();
                if (material == null)
                    return;
            }
            var scale = transform.lossyScale.x;
            firing.Clear();
            foreach (var (a, b, offset, s) in links)
            {
                Vector3 pa = segments[a].position, pb = segments[b].position;
                var dir = (pb - pa).normalized;
                var from = pa + (dir * radius[a] + offset) * scale;
                var to = pb - (dir * radius[b] - offset) * scale;
                var gap = to - from;
                if (gap.sqrMagnitude < 0.01f)
                    continue;
                firing.Add(Matrix4x4.TRS(from, Quaternion.LookRotation(gap), new Vector3(Thickness * s, Thickness * s, gap.magnitude)));
            }
            if (firing.Count == 0)
                return;
            var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                                                  worldBounds = new Bounds(transform.position + Vector3.up * 30f * scale, Vector3.one * 90f * scale) };
            Graphics.RenderMeshInstanced(rp, mesh, 0, firing);
            // Now and then a crack from one of the arcs (the spires' zap).
            if (Time.time >= nextCrack)
            {
                nextCrack = Time.time + UnityEngine.Random.Range(0.6f, 2.2f);
                var at = firing[UnityEngine.Random.Range(0, firing.Count)].GetPosition();
                if (Punchy.Audio.GameAudio.Audible(at, 90f))
                    Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Zip, at, 2.4f, 0.8f, 90f, Punchy.Audio.SoundGroup.World);
            }
        }
    }
}
