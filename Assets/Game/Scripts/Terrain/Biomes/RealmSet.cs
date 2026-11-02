using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// The realms the island is divided into (TerrainAuthoring.Realms), how big their regions are and which realm
    /// buffers two that mustn't touch. Biomes no realm lists can still appear through the hand-placed or map paths,
    /// but noise placement with realms on only picks listed ones.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Realm Set", fileName = "Realms")]
    public class RealmSet : ScriptableObject
    {
        public List<RealmDefinition> Realms = new List<RealmDefinition>();
        [Tooltip("Width of a realm region, in biome regions (Biome Noise Cell Size).")]
        [Range(1.5f, 5f)] public float RegionScale = 3f;
        [Tooltip("Put between two realms that mustn't border each other (e.g. Temperate between Frost and Fire).")]
        public RealmDefinition Buffer;

        /// <summary>
        /// Resolved against a biome palette (ids), for baking. Biomes not in the palette are left out; realms with no
        /// biomes left are kept (so realm ids match list order) but never picked.
        /// </summary>
        public RealmPlan Plan(IReadOnlyList<BiomeDefinition> palette)
        {
            var count = Realms.Count;
            var plan = new RealmPlan
            {
                Scale = RegionScale,
                Buffer = Buffer != null ? Realms.IndexOf(Buffer) : -1,
                Members = new int[count][],
                Weights = new float[count][],
                Frequency = new float[count],
                Avoid = new bool[count, count]
            };
            for (int r = 0; r < count; r++)
            {
                var realm = Realms[r];
                var members = new List<int>();
                var weights = new List<float>();
                if (realm != null)
                {
                    foreach (var b in realm.Biomes)
                    {
                        var id = b?.Biome != null ? IndexOf(palette, b.Biome) : -1;
                        if (id < 0 || b.Weight <= 0f)
                            continue;
                        members.Add(id);
                        weights.Add(b.Weight);
                    }
                    foreach (var other in realm.NotNextTo)
                    {
                        var o = other != null ? Realms.IndexOf(other) : -1;
                        if (o >= 0)
                            plan.Avoid[r, o] = plan.Avoid[o, r] = true;
                    }
                }
                plan.Members[r] = members.ToArray();
                plan.Weights[r] = weights.ToArray();
                plan.Frequency[r] = realm != null && members.Count > 0 ? realm.Frequency : 0f;
            }
            return plan;
        }

        static int IndexOf(IReadOnlyList<BiomeDefinition> palette, BiomeDefinition b)
        {
            for (int i = 0; i < palette.Count; i++)
                if (palette[i] == b)
                    return i;
            return -1;
        }
    }

    /// <summary>A <see cref="RealmSet"/> in biome ids, read while baking the biome cell table (managed, bake time only).</summary>
    public class RealmPlan
    {
        /// <summary>Realm region width in biome regions.</summary>
        public float Scale;
        /// <summary>The realm put between two that mustn't border, or -1.</summary>
        public int Buffer;
        /// <summary>Per realm, its biomes' palette ids and weights.</summary>
        public int[][] Members;
        public float[][] Weights;
        public float[] Frequency;
        /// <summary>Pairs of realms that mustn't share a border.</summary>
        public bool[,] Avoid;
    }
}
