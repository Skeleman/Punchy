using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// W-92 (#40): rolls the SaltFlat colossus's salt crystal slowly round its circle and drags a dirt trail behind it, a
    /// strip that fades back to salt over <see cref="TrailLength"/> metres (capped, so it never closes the ring). It is
    /// cosmetic and runs off the clock, so each player rolls their own copy from the same seeded circle.
    /// </summary>
    public class SaltCrystalRoll : MonoBehaviour
    {
        /// <summary>Ground heights sampled round the circle.</summary>
        public const int Steps = 144;
        /// <summary>The trail's length behind the ball (m).</summary>
        public const float TrailLength = 90f;
        const float Speed = 1.6f, HalfWidth = 3.5f, Sink = 0.92f;
        const int Segs = 40;
        static readonly Color Dirt = new Color(0.33f, 0.26f, 0.20f), Salt = new Color(0.86f, 0.84f, 0.80f);

        /// <summary>Seconds per lap on a circle of this radius.</summary>
        public static float LapSeconds(float radius) => Mathf.PI * 2f * radius / Speed;

        Transform ball;
        float radius, ballRadius, start, dir, last = float.NaN;
        float[] heights;
        Mesh trail;
        Vector3[] verts;

        public void Init(Transform ball, float radius, float ballRadius, float[] heights, float start, float dir, Material material)
        {
            this.ball = ball; this.radius = radius; this.ballRadius = ballRadius; this.heights = heights; this.start = start; this.dir = dir;
            verts = new Vector3[(Segs + 1) * 2];
            var cols = new Color[verts.Length];
            var normals = new Vector3[verts.Length];
            var tris = new int[Segs * 6];
            for (int i = 0; i <= Segs; i++)
            {
                // Dark at the ball, back to the salt's own colour at the tail.
                var c = Color.Lerp(Dirt, Salt, Mathf.Pow(i / (float)Segs, 0.8f));
                cols[i * 2] = cols[i * 2 + 1] = c;
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                if (i == Segs) break;
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            trail = new Mesh { name = "SaltCrystalTrail" };
            trail.MarkDynamic();
            trail.vertices = verts; trail.colors = cols; trail.normals = normals;
            trail.triangles = tris;
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = trail;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Update();
        }

        float Height(float a)
        {
            var f = Mathf.Repeat(a / (Mathf.PI * 2f), 1f) * Steps;
            int i = (int)f;
            return Mathf.Lerp(heights[i % Steps], heights[(i + 1) % Steps], f - i);
        }

        void Update()
        {
            if (ball == null || heights == null)
                return;
            var a = start + dir * Speed / radius * Time.time;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            ball.localPosition = radial * radius + Vector3.up * (Height(a) + ballRadius * Sink);
            // Roll: turn about the axis across the motion by the distance covered over the ball's radius.
            if (!float.IsNaN(last))
            {
                var move = new Vector3(-radial.z, 0f, radial.x) * dir;
                var dist = Mathf.Abs(a - last) * radius;
                ball.localRotation = Quaternion.AngleAxis(dist / ballRadius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, move)) * ball.localRotation;
            }
            last = a;
            // The trail, from just behind the ball's contact back along the circle.
            var span = TrailLength / radius;
            for (int i = 0; i <= Segs; i++)
            {
                var ai = a - dir * (0.02f + span * i / Segs);
                var r = new Vector3(Mathf.Cos(ai), 0f, Mathf.Sin(ai));
                var y = Height(ai) + 0.25f;
                verts[i * 2] = r * (radius - HalfWidth) + Vector3.up * y;
                verts[i * 2 + 1] = r * (radius + HalfWidth) + Vector3.up * y;
            }
            trail.vertices = verts;
            trail.RecalculateBounds();
        }
    }
}
