using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// W-92 (#40): rolls the SaltFlat colossus's salt crystal slowly round its circle and drags a trail behind it that
    /// fades back to salt over <see cref="TrailLength"/> metres (capped, so it never closes the ring). It is cosmetic but
    /// runs off the shared clock (W-93: _PunchyClock, the one the fauna sway on; Time.time when solo), so host and clients
    /// see the ball at the same place. W-93: the trail is a furrow, read the way the worms' craters are: a dark pressed-in
    /// floor between two raised salt lips, shaded by its own tilted normals rather than cut into the ground.
    /// </summary>
    public class SaltCrystalRoll : MonoBehaviour
    {
        /// <summary>Ground heights sampled round the circle.</summary>
        public const int Steps = 144;
        /// <summary>The trail's length behind the ball (m).</summary>
        public const float TrailLength = 90f;
        const float Speed = 1.6f, HalfWidth = 4.5f, Sink = 0.92f, Lip = 0.6f, Rebuild = 0.5f;
        const int Segs = 40, Cols = 7;
        // The furrow's cross-section, outside edge to outside edge: where across (of HalfWidth), how high (of Lip), and
        // how much dirt it shows (0 salt, 1 dirt, above 1 the shadowed inner wall).
        static readonly float[] Across = { -1f, -0.72f, -0.45f, 0f, 0.45f, 0.72f, 1f };
        static readonly float[] Rise = { 0.04f, 1f, 0.15f, 0.06f, 0.15f, 1f, 0.04f };
        static readonly float[] Shows = { 0f, 0f, 1.35f, 1f, 1.35f, 0f, 0f };
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly Color Dirt = new Color(0.33f, 0.26f, 0.20f), Salt = new Color(0.86f, 0.84f, 0.80f);

        /// <summary>Seconds per lap on a circle of this radius.</summary>
        public static float LapSeconds(float radius) => Mathf.PI * 2f * radius / Speed;

        Transform ball;
        float radius, ballRadius, start, dir, trailAt = float.NaN;
        float[] heights;
        // W-94 (#40): the trail's width along the circle, a few seeded whole-turn waves (so it closes on itself), as if
        // a body that is not round presses it in wider and narrower. By the absolute angle, so it is fixed to the ground
        // and the same for every player.
        Vector3 waveK, wavePhase;
        Mesh trail;
        Vector3[] verts;

        public void Init(Transform ball, float radius, float ballRadius, float[] heights, float start, float dir, Material material, uint seed = 1u)
        {
            this.ball = ball; this.radius = radius; this.ballRadius = ballRadius; this.heights = heights; this.start = start; this.dir = dir;
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            // Waves about 18, 9 and 5 m long on a 40 m circle (scaled with its length).
            var turn = Mathf.PI * 2f * radius;
            waveK = new Vector3(Mathf.Round(turn / rng.NextFloat(15f, 22f)), Mathf.Round(turn / rng.NextFloat(7.5f, 11f)), Mathf.Round(turn / rng.NextFloat(4.5f, 6f)));
            wavePhase = new Vector3(rng.NextFloat(0f, 6.2832f), rng.NextFloat(0f, 6.2832f), rng.NextFloat(0f, 6.2832f));
            verts = new Vector3[(Segs + 1) * Cols];
            var cols = new Color[verts.Length];
            var tris = new int[Segs * (Cols - 1) * 6];
            for (int i = 0; i <= Segs; i++)
            {
                // Dark at the ball, back to the salt's own colour at the tail; the inner walls darker still (the worms'
                // crater rim darkens the same way), the lip crests a touch brighter than the flat.
                var fade = Mathf.Pow(i / (float)Segs, 0.8f);
                for (int j = 0; j < Cols; j++)
                {
                    var near = Shows[j] > 1f ? Dirt * (2f - Shows[j]) : Color.Lerp(Salt * (Rise[j] > 0.5f ? 1.06f : 1f), Dirt, Shows[j]);
                    var c = Color.Lerp(near, Salt, fade);
                    c.a = 1f;
                    cols[i * Cols + j] = c;
                }
                if (i == Segs) break;
                for (int j = 0; j < Cols - 1; j++)
                {
                    int v = i * Cols + j, t = (i * (Cols - 1) + j) * 6;
                    tris[t] = v; tris[t + 1] = v + Cols; tris[t + 2] = v + 1;
                    tris[t + 3] = v + 1; tris[t + 4] = v + Cols; tris[t + 5] = v + Cols + 1;
                }
            }
            trail = new Mesh { name = "SaltCrystalTrail" };
            trail.MarkDynamic();
            trail.vertices = verts; trail.colors = cols;
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

        float Width(float a) => 1f + 0.15f * Mathf.Sin(waveK.x * a + wavePhase.x) + 0.09f * Mathf.Sin(waveK.y * a + wavePhase.y)
                                   + 0.05f * Mathf.Sin(waveK.z * a + wavePhase.z);

        void Update()
        {
            if (ball == null || heights == null)
                return;
            // The shared clock is the server's tick time on a client and Time.time on the host or solo; it reads 0
            // before WorldSync has run, so fall back then too.
            var clock = Shader.GetGlobalFloat(ClockId);
            var a = start + dir * Speed / radius * (clock > 0f ? clock : Time.time);
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            ball.localPosition = radial * radius + Vector3.up * (Height(a) + ballRadius * Sink);
            // Roll: face along the motion and spin about the axis across it by the distance covered over the ball's
            // radius. Absolute, from the angle alone, so every player's ball shows the same face.
            var move = new Vector3(-radial.z, 0f, radial.x) * dir;
            var spin = Mathf.Repeat(dir * (a - start) * radius / ballRadius * Mathf.Rad2Deg, 360f);
            ball.localRotation = Quaternion.LookRotation(move) * Quaternion.Euler(spin, 0f, 0f);
            // The trail, from just behind the ball's contact back along the circle; rebuilt only once the ball has
            // moved Rebuild metres (the head stays under the ball meanwhile).
            if (!float.IsNaN(trailAt) && Mathf.Abs(a - trailAt) * radius < Rebuild)
                return;
            trailAt = a;
            var span = TrailLength / radius;
            for (int i = 0; i <= Segs; i++)
            {
                var ai = a - dir * (0.02f + span * i / Segs);
                var r = new Vector3(Mathf.Cos(ai), 0f, Mathf.Sin(ai));
                var y = Height(ai) + 0.2f;
                // The lips slump back towards the tail as the salt creeps in.
                // A wider press pushes up a taller lip.
                var w = Width(ai);
                var lip = Lip * (1f - 0.7f * i / Segs) * w;
                for (int j = 0; j < Cols; j++)
                    verts[i * Cols + j] = r * (radius + Across[j] * HalfWidth * w) + Vector3.up * (y + Rise[j] * lip);
            }
            trail.vertices = verts;
            trail.RecalculateNormals();
            trail.RecalculateBounds();
        }
    }
}
