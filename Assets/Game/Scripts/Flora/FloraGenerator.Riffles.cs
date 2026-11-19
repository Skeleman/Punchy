using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, rapids stones: riffle stepping-stone rows and big splash boulders (#301).
    public static partial class FloraGenerator
    {
        public const float RifflePitch = 1.6f;

        public static float RiffleRowLength(int stones) => (stones + 1) * RifflePitch;

        public static int RiffleStones(float span) =>
            span < 3.5f || span > RiffleRowLength(6) * 1.3f ? -1 : Mathf.Clamp(Mathf.RoundToInt(span / RifflePitch) - 1, 3, 6);

        static void RiffleRow(Grower g)
        {
            var b = g.B;
            var first = b.VertexCount;
            var detail = g.Sides(3, 2, 1);
            var stones = 3 + g.Variant % 4;
            var length = RiffleRowLength(stones);
            for (int k = 0; k < stones; k++)
            {
                var x = -length * 0.5f + (k + 1) * RifflePitch + g.Range(-0.15f, 0.15f);
                var radii = new Vector3(g.Range(0.5f, 0.68f), 0.32f, g.Range(0.42f, 0.58f));
                var at = new Vector3(x, g.Range(0.06f, 0.14f) - radii.y, g.Range(-0.35f, 0.35f));
                var turn = Quaternion.Euler(g.Range(-4f, 4f), g.Range(0f, 360f), g.Range(-4f, 4f));
                b.Blob(at, radii, turn, detail, 0.08f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, g.Range(0.35f, 0.6f), 0.55f);
            }
            g.Height = 0.2f;
            g.Width = length;
            g.TrunkRadius = 0f;
            b.Occlude(first, (p, n) => Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n.y + 0.6f)));
            Moss(g, first);
        }

        static void SplashRock(Grower g)
        {
            var b = g.B;
            var first = b.VertexCount;
            var detail = g.Sides(3, 2, 1);
            var r = g.Range(g.S.Height);
            var top = g.Range(0.6f, 0.9f) * r;
            var yaw = g.R() * 360f;
            // Wide rather than tall, so a boulder on a shallow bed never reads as a pillar; still reaches a 2 m deep bed.
            var radii = new Vector3(r * g.Range(1.35f, 1.6f), 0f, r * g.Range(1.1f, 1.3f));
            radii.y = Mathf.Max(radii.x, radii.z) * 0.8f;
            var turn = Quaternion.Euler(g.Range(-6f, 6f), yaw, g.Range(-6f, 6f));
            var at = new Vector3(0f, top - radii.y, 0f);
            b.Blob(at, radii, turn, detail, 0.14f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, g.Range(0.1f, 0.25f), 0.55f);
            g.AddCollider(at, radii * 0.92f, turn);
            if (g.R() < 0.6f)
            {
                var side = Quaternion.Euler(0f, yaw + g.Range(60f, 120f), 0f) * Vector3.right * r * g.Range(1.4f, 1.8f);
                var small = new Vector3(r * 0.65f, r * 0.55f, r * 0.55f);
                var chunk = new Vector3(side.x, top * g.Range(0.2f, 0.5f) - small.y, side.z);
                var tilt = Quaternion.Euler(g.Range(-12f, 12f), g.R() * 360f, g.Range(-12f, 12f));
                b.Blob(chunk, small, tilt, detail, 0.14f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, g.Range(0.1f, 0.25f), 0.55f);
                g.AddCollider(chunk, small * 0.92f, tilt);
            }
            g.Height = top;
            g.Width = radii.x * 2.4f;
            g.TrunkRadius = radii.x * 0.5f;
            b.Occlude(first, (p, n) => GroundOcclusion(p + Vector3.up * r, r) * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(n.y + 0.6f)));
            Moss(g, first);
        }
    }
}
