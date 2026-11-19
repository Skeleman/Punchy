using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>W-173 (#399): town houses that used to be one-off meshes, rebuilt from the kit's house parts.</summary>
    public static partial class StructureKit
    {
        /// <summary>A one-storey stone cottage, <paramref name="half"/> to the wall's outer face; the caller hangs a <paramref name="door"/> (width, height) in the gap.</summary>
        public static List<KitPart> StoneHouse(uint seed, CultureTrim t, float half, float wall, bool ladder, out Vector2 door)
        {
            var width = half * 2f - Thick;
            t.Storey = wall;
            t.Bay = width / 3f;
            t.Quoins = true;
            t.TowerChance = t.BalconyChance = t.StairChance = 0f;
            var parts = House(seed, t, width, width, 1, 0f);
            var out_ = width * 0.5f;
            parts.RemoveAll(p => p.Centre.z > out_ - 0.2f && Mathf.Abs(p.Centre.x) < 0.3f && p.Centre.y < wall * 0.9f &&
                                 (p.Piece == KitPiece.Door || p.Piece == KitPiece.Trim && p.Size.x < 2.5f));
            if (ladder)
                RoofLadder(parts, seed, t, width, width, 1, 1);
            door = new Vector2(Mathf.Min(1.3f, t.Bay * 0.55f), Mathf.Min(wall * 0.78f, 2.3f));
            return parts;
        }

        public static List<KitPart> Hall(CultureTrim t, float half, float front, float plinth, float wall, float ridge)
        {
            var parts = new List<KitPart>();
            const float eave = 0.9f, slab = 0.22f, steps = 5f;
            Add(parts, KitPiece.Platform, KitMaterial.Stone, new Vector3(0f, plinth * 0.5f - 0.15f, 0f), new Vector3(half * 2f + 0.4f, plinth + 0.3f, front * 2f + 0.4f), 0f, true);
            Add(parts, KitPiece.Platform, KitMaterial.Stone, new Vector3(0f, plinth * 0.29f, front + 0.75f), new Vector3(3.2f, plinth * 0.75f, 0.8f), 0f, true);
            Add(parts, KitPiece.Wall, KitMaterial.Wood, new Vector3(0f, (plinth + wall) * 0.5f, 0f), new Vector3(half * 2f, wall - plinth, front * 2f), 0f, true);
            var posts = Mathf.Max(2, Mathf.RoundToInt(half / 1.6f));
            for (int k = 0; k <= posts; k++)
                foreach (var z in new[] { -1f, 1f })
                    Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(Mathf.Lerp(-half, half, k / (float)posts), (plinth + wall) * 0.5f, z * (front + 0.06f)), new Vector3(0.28f, wall - plinth, 0.2f), 0f, true);
            Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(0f, wall - 0.12f, 0f), new Vector3(half * 2f + 0.12f, 0.25f, front * 2f + 0.12f), 0f, true);

            var rise = ridge - wall;
            var tan = rise / front;
            var pitch = Mathf.Atan(tan) * Mathf.Rad2Deg;
            var run = front + eave;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, wall + rise * 0.5f - eave * tan * 0.5f + slab, side * run * 0.5f),
                    new Vector3(half * 2f + 1.2f, slab, run / Mathf.Cos(pitch * Mathf.Deg2Rad)), 0f, true, side * pitch);
            Add(parts, KitPiece.Trim, KitMaterial.Roof, new Vector3(0f, ridge + slab * 1.2f, 0f), new Vector3(half * 2f + 1.3f, 0.22f, 0.3f), 0f, false);
            foreach (var x in new[] { -1f, 1f })
            {
                for (int k = 0; k < steps; k++)
                    Add(parts, KitPiece.Wall, KitMaterial.Wood, new Vector3(x * (half - Thick * 0.5f), wall + (k + 0.5f) * rise / steps, 0f),
                        new Vector3(Thick, rise / steps + 0.01f, front * 2f * (1f - (k + 0.5f) / steps)), 0f, true);
                foreach (var z in new[] { -1f, 1f })
                {
                    var a = new Vector3(x * (half + 0.6f), wall + rise * 0.55f, z * front * 0.55f);
                    var d = new Vector3(x * (half + 0.6f), ridge + 1.1f, -z * 0.5f) - a;
                    Add(parts, KitPiece.Beam, KitMaterial.Trim, a + d * 0.5f, new Vector3(0.18f, d.magnitude, 0.22f), 0f, true, Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg);
                }
            }
            return parts;
        }

        /// <summary>A round thatched hut, <paramref name="radius"/> to the wall face, its porch door (width, height) facing +Z at radius * 1.05.</summary>
        public static List<KitPart> RoundHut(uint seed, CultureTrim t, float radius, float wall, float height, float ladderPick, bool windows, out Vector2 door)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x4077u) & 0x7FFFFFFF));
            const float ring = 0.3f, post = 0.15f, cheek = 0.16f, facets = 14;
            door = new Vector2(Mathf.Min(radius * 0.5f, 1.25f), Mathf.Min(wall * 0.8f, 2.4f));
            var overhang = radius * 1.3f;
            var eave = wall - 0.3f;
            var midR = overhang * 0.55f;
            var midY = wall + (height - wall) * 0.45f;
            var pitch = Mathf.Atan2(midY - eave, overhang - midR);
            var top = eave + (overhang - radius + ring) * Mathf.Tan(pitch);
            var porchTop = door.y + post + 0.08f;
            var span = door.x * 0.5f + post + cheek;
            RingWall(parts, Vector3.zero, radius, ring, top, (int)facets, span - cheek * 0.5f, porchTop, KitMaterial.Wall, KitMaterial.Trim);

            var front = radius * 1.05f + 0.4f;
            var back = Mathf.Sqrt(Mathf.Max(radius * radius - span * span, 0f)) - ring;
            var depth = (front + back) * 0.5f;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(side * (span - cheek * 0.5f), porchTop * 0.5f, depth), new Vector3(cheek, porchTop, front - back), 0f, true);
            Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(0f, porchTop + 0.1f, depth), new Vector3(span * 2f, 0.2f, front - back), 0f, false);
            Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, porchTop + 0.3f, depth + 0.12f), new Vector3(span * 2f + 0.3f, 0.1f, front - back + 0.35f), 0f, false, 14f);

            ConeRoof(parts, Vector3.zero, overhang, eave, midR, midY, height, (int)facets);
            if (rng.NextDouble() < 0.5)
                Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, height + 0.35f, 0f), new Vector3(0.08f, 0.8f, 0.08f), 0f, false);

            if (windows)
            {
                var stepA = 360f / facets;
                var face = radius * Mathf.Cos(Mathf.PI / facets);
                var y = wall * 0.58f;
                for (int k = 0; k < 4; k++)
                {
                    // On the facet nearest each diagonal, so the pane lies flat on the wall.
                    var a = Mathf.Round((45f + k * 90f) / stepA - 0.5f) * stepA + stepA * 0.5f;
                    Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(a, new Vector3(0f, y, face + 0.02f)), new Vector3(0.55f, 0.69f, 0.08f), a, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(a, new Vector3(0f, y - 0.4f, face + 0.06f)), new Vector3(0.75f, 0.08f, 0.16f), a, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(a, new Vector3(0f, y + 0.39f, face + 0.04f)), new Vector3(0.7f, 0.09f, 0.1f), a, false);
                }
            }
            if (ladderPick >= 0f)
            {
                var dir = Turn(ladderPick < 0.34f ? 90f : ladderPick < 0.67f ? 270f : 180f, Vector3.forward);
                Ladder(parts, dir * (overhang + 0.4f), dir * (overhang + 0.05f) + Vector3.up * (wall - 0.2f), dir, false);
            }
            return parts;
        }

        /// <summary>A round wall of flat facets with banding, and a door gap <paramref name="doorHalf"/> each side of +Z up to <paramref name="doorTop"/>.</summary>
        public static void RingWall(List<KitPart> parts, Vector3 at, float radius, float thick, float height, int segments,
                                    float doorHalf, float doorTop, KitMaterial m, KitMaterial band)
        {
            var gap = doorHalf > 0f ? 2f * Mathf.Asin(Mathf.Min(doorHalf / radius, 0.9f)) * Mathf.Rad2Deg : 0f;
            var n = doorHalf > 0f ? Mathf.Max(3, Mathf.RoundToInt((360f - gap) / (360f / segments))) : segments;
            var step = (360f - gap) / n;
            var len = 2f * radius * Mathf.Tan(step * 0.5f * Mathf.Deg2Rad) + 0.04f;
            var face = radius * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad);
            for (int k = 0; k < n; k++)
            {
                var a = gap * 0.5f + (k + 0.5f) * step;
                Add(parts, KitPiece.Wall, m, at + Turn(a, new Vector3(0f, height * 0.5f - 0.05f, face - thick * 0.5f)), new Vector3(len, height + 0.1f, thick), a, true);
                foreach (var y in new[] { 0.15f, height * 0.9f })
                    Add(parts, KitPiece.Trim, band, at + Turn(a, new Vector3(0f, y, face + 0.03f)), new Vector3(len + 0.02f, 0.12f, 0.06f), a, false);
            }
            if (doorHalf <= 0f)
                return;
            var chord = Mathf.Sqrt(Mathf.Max(radius * radius - doorHalf * doorHalf, 0f));
            var width = 2f * doorHalf + 0.3f;
            Add(parts, KitPiece.Wall, m, at + new Vector3(0f, (doorTop + height) * 0.5f, chord - thick * 0.5f), new Vector3(width, height - doorTop, thick), 0f, true);
            Add(parts, KitPiece.Trim, band, at + new Vector3(0f, height * 0.9f, chord + 0.03f), new Vector3(width, 0.12f, 0.06f), 0f, false);
        }

        /// <summary>A cone roof of three rings of lapped slabs, broken at (<paramref name="midR"/>, <paramref name="midY"/>), with a thatch knot at the peak.</summary>
        public static void ConeRoof(List<KitPart> parts, Vector3 at, float eaveR, float eaveY, float midR, float midY, float apexY, int segments)
        {
            var knot = Mathf.Max(0.35f, eaveR * 0.12f);
            var topY = apexY - knot * 0.6f;
            var tierN = new[] { segments, Mathf.Max(6, segments * 5 / 7), Mathf.Max(5, segments * 3 / 7) };
            var tierR = new[] { eaveR, midR, midR * 0.45f, knot * 0.6f };
            var tierY = new[] { eaveY, midY, Mathf.Lerp(midY, topY, 0.55f), topY };
            for (int r = 0; r < 3; r++)
            {
                var dr = tierR[r] - tierR[r + 1];
                var dy = tierY[r + 1] - tierY[r];
                var slope = Mathf.Atan2(dy, dr) * Mathf.Rad2Deg;
                var slant = Mathf.Sqrt(dr * dr + dy * dy) + 0.2f;
                var width = 2f * tierR[r] * Mathf.Tan(Mathf.PI / tierN[r]) + 0.05f;
                var mid = new Vector3(0f, (tierY[r] + tierY[r + 1]) * 0.5f, (tierR[r] + tierR[r + 1]) * 0.5f);
                for (int k = 0; k < tierN[r]; k++)
                {
                    var a = (k + (r % 2) * 0.5f) * 360f / tierN[r];
                    var rot = Quaternion.Euler(slope, a, 0f);
                    parts.Add(new KitPart { Piece = KitPiece.Roof, Material = KitMaterial.Roof, Centre = at + Turn(a, mid) + rot * new Vector3(0f, -0.1f, -0.1f),
                                            Size = new Vector3(width, 0.2f, slant), Rotation = rot });
                }
            }
            Add(parts, KitPiece.Roof, KitMaterial.Roof, at + new Vector3(0f, apexY - knot * 0.5f, 0f), new Vector3(knot * 1.6f, knot, knot * 1.6f), 45f, false);
        }
    }
}
