using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// W-92 (#40): rolls the SaltFlat colossus's salt crystal slowly round its circle and drags a trail behind it that
    /// fades back to salt over <see cref="TrailLength"/> metres (capped, so it never closes the ring). It is cosmetic but
    /// runs off the shared clock (W-93: _PunchyClock, the one the fauna sway on; Time.time when solo), so host and clients
    /// see the ball at the same place. W-113 (item 16): the trail is no longer a mesh (it lay on the ground, moved with the
    /// ball and, sampled once into the colossus's collision, blocked the player where it had been). It is a furrow drawn
    /// into the ground by TerrainSurface.shader, the way the worms' marks are: a dark pressed-in floor between two raised
    /// salt lips, shading only, so the player walks straight across it.
    /// </summary>
    public class SaltCrystalRoll : MonoBehaviour
    {
        /// <summary>Ground heights sampled round the circle.</summary>
        public const int Steps = 144;
        /// <summary>The trail's length behind the ball (m).</summary>
        public const float TrailLength = 90f;
        const float Speed = 1.6f, HalfWidth = 4.5f, Sink = 0.92f, Depth = 0.6f;
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly int TrailId = Shader.PropertyToID("_PunchySaltTrail");
        static readonly int RunId = Shader.PropertyToID("_PunchySaltTrailRun");
        static readonly int WaveId = Shader.PropertyToID("_PunchySaltTrailWave");

        /// <summary>Seconds per lap on a circle of this radius.</summary>
        public static float LapSeconds(float radius) => Mathf.PI * 2f * radius / Speed;

        /// <summary>The ground under the ball, where its far shadow starts (StoneColossi.DrawFarShadows; the root sits at
        /// the circle's centre, so a shadow laid from the root stood still while the ball rolled, playtest 11-07).</summary>
        public Vector3 BallFoot => ball == null ? transform.position : ball.position - Vector3.up * (ballRadius * Sink);

        Transform ball;
        float radius, ballRadius, start, dir;
        float[] heights;
        // W-94 (#40): the trail's width along the circle, two seeded whole-turn waves (so it closes on itself), as if a
        // body that is not round presses it in wider and narrower. By the absolute angle, so it is fixed to the ground and
        // the same for every player.
        Vector4 wave;

        /// <param name="material">Unused since W-113 (the trail mesh it drew is gone); kept so the colossus builder's call stands.</param>
        public void Init(Transform ball, float radius, float ballRadius, float[] heights, float start, float dir, Material material, uint seed = 1u)
        {
            this.ball = ball; this.radius = radius; this.ballRadius = ballRadius; this.heights = heights; this.start = start; this.dir = dir;
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            // Waves about 18 and 9 m long (the third, 5 m, wave of the old mesh is drawn but no longer used, so the
            // first two keep their draws).
            var turn = Mathf.PI * 2f * radius;
            var k1 = Mathf.Round(turn / rng.NextFloat(15f, 22f));
            var k2 = Mathf.Round(turn / rng.NextFloat(7.5f, 11f));
            rng.NextFloat(4.5f, 6f);
            wave = new Vector4(k1, k2, rng.NextFloat(0f, 6.2832f), rng.NextFloat(0f, 6.2832f));
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
            // The furrow, from the ball's contact back along the circle (world x, z: the root sits on the circle's centre).
            var at = transform.position;
            Shader.SetGlobalVector(TrailId, new Vector4(at.x, at.z, radius, HalfWidth));
            Shader.SetGlobalVector(RunId, new Vector4(Mathf.Repeat(a, Mathf.PI * 2f), dir, TrailLength / radius, Depth));
            Shader.SetGlobalVector(WaveId, wave);
        }

        void OnDisable() => Shader.SetGlobalVector(TrailId, Vector4.zero);
    }
}
