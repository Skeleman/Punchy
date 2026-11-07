using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-106 (#131, second part): the Ember Forest burns at night. Its trees near the camera carry standard fire flames
    /// in their crowns, with a few smaller ones at their feet and on the ground between them (each flame's glow lights
    /// the ground), all in one colour of the standard fire picked from the world seed and the night, so each night's
    /// fire is its own. Cosmetic only: it doesn't spread, burn or hurt anything. It catches as the light goes and dies
    /// down at dawn with the landmarks' night fires. Only trees within ForestFireRange are gathered (twice a second, or
    /// as the camera moves), and only the nearest few throw light.
    /// W-112: past that, out to ForestFarRange, each burning tree keeps one far flame in its crown (FireSystem's far
    /// level of detail: the flame and a broad glow card standing in for its light, no real light), so the forest
    /// glows across the island instead of lighting up only round the player. The trees' ember seams and leaf slivers
    /// glow in the night's fire colour too (FireSystem.EmberTint).
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        /// <summary>Trees within this distance of the camera burn (at full detail; GraphicsSettings.DetailRange scales it).</summary>
        const float ForestFireRange = 70f;
        /// <summary>At most this many flames a frame from the forest, and this many of the nearest throw light.</summary>
        const int MaxForestFlames = 320, ForestLights = 6;
        const int ForestSeedBase = 0x6F000000;
        /// <summary>W-112: burning trees keep a far flame out to this distance (GraphicsSettings.ViewRange scales it), at most MaxFarFlames.</summary>
        const float ForestFarRange = 600f;
        const int MaxFarFlames = 3000;

        Punchy.Flora.FloraSystem flora;
        readonly HashSet<Punchy.Flora.FloraSpecies> burningSpecies = new HashSet<Punchy.Flora.FloraSpecies>();
        BiomePalette burningFrom;
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> burningTrees =
            new List<(float3, float3, float, float, int)>();
        /// <summary>This night's forest flames: position, size, seed (sorted nearest first when gathered).</summary>
        readonly List<(Vector3 at, float size, int seed)> forestFlames = new List<(Vector3, float, int)>();
        float3 forestGatheredAt = new float3(float.MaxValue);
        float nextForestGather;
        /// <summary>
        /// Refactor 3.2 (playtest 11-07: flames vanished in the middle distance and came back up close): how far the near
        /// flames actually reach. In a dense forest the MaxForestFlames cap ends the near list well inside the range, and
        /// the far flames only started at 0.75 x the range, so the trees in between burned with nothing. Near and far now
        /// hand over at this reach: the near flames shrink away over its last quarter and the far ones grow in over the
        /// same band.
        /// </summary>
        float nearReach = float.MaxValue;
        bool lastCut;
        float lastShownReach = -100f;
        /// <summary>W-112: the far flames (crown position, size, seed), gathered less often than the near ones.</summary>
        readonly List<(Vector3 at, float size, int seed)> farFlames = new List<(Vector3, float, int)>();
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> farTrees =
            new List<(float3, float3, float, float, int)>();
        float3 farGatheredAt = new float3(float.MaxValue);
        float nextFarGather;
        /// <summary>The near flames' reach the far flames were gathered against; a big change re-gathers them.</summary>
        float farGatheredReach;
        static readonly int EmberTintId = Shader.PropertyToID("_PunchyEmberTint");
        Punchy.Weather.DayNightCycle cycle;

        /// <summary>Last forest gather, for the world probe: "trees, flames, colour".</summary>
        public static string LastForestLog = "";

        /// <summary>Debug: false hides the forest fires (render A/B).</summary>
        public static bool ForestFiresOn = true;

        /// <summary>Debug (W-112 A/B): false drops the far flames and keeps the embers wood-coloured, as before W-112.</summary>
        public static bool FarForestFiresOn = true;

        /// <summary>The colour the forest burns in tonight: from the world seed and the night (evening and the morning after count as one).</summary>
        public static int ForestColour(uint worldSeed, int day, float timeOfDay)
        {
            var night = timeOfDay >= 12f ? day : day - 1;
            var colours = FireSystem.Effect != null ? math.min(FireSystem.Effect.Colours.Length, 4) : 1;
            return (int)(Scramble(math.hash(new uint2(worldSeed, (uint)night + 0xF12Eu))) % (uint)math.max(colours, 1));
        }

        static uint Scramble(uint h)
        {
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return h;
        }

        void ForestFires(in TerrainSource source, BiomePalette palette, Vector3 eye, float lit)
        {
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var colour = cycle != null ? ForestColour((uint)source.World.Seed, cycle.Day, cycle.TimeOfDay) : FireSystem.Wood;
            // Off (the A/B switch) is the old look: wood-coloured embers whatever the fire burns, even under the override.
            Shader.SetGlobalVector(EmberTintId, FarForestFiresOn ? FireSystem.EmberTint(colour) : new Vector4(1f, 0.36f, 0.08f, 1f));
            if (!ForestFiresOn || lit <= 0.01f)
            {
                forestGatheredAt = farGatheredAt = new float3(float.MaxValue);
                return;
            }
            if (burningFrom != palette)
            {
                burningFrom = palette;
                burningSpecies.Clear();
                foreach (var biome in palette.Biomes)
                    if (biome != null && biome.HasTag(BiomeTags.Burning))   // W-116: by tag, was by name
                        foreach (var f in biome.Flora)
                            if (f.Species != null)
                                burningSpecies.Add(f.Species);
            }
            if (burningSpecies.Count == 0)
                return;
            if (flora == null)
                flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
            if (flora == null)
                return;

            var range = Punchy.Settings.GraphicsSettings.DetailRange(ForestFireRange);
            if (Time.unscaledTime >= nextForestGather || math.distancesq(forestGatheredAt, (float3)eye) > 36f)
            {
                nextForestGather = Time.unscaledTime + 0.5f;
                forestGatheredAt = eye;
                GatherForest(source, eye, range);
            }
            if (FarForestFiresOn)
                FarForestFires(eye, math.min(range, nearReach), colour, lit);
            if (forestFlames.Count == 0)
                return;

            var reach = math.min(range, nearReach);
            for (int i = 0; i < forestFlames.Count; i++)
            {
                var f = forestFlames[i];
                // Flames far out shrink away rather than pop, over the last quarter of what the near flames reach.
                var d = Vector3.Distance(f.at, eye);
                var size = f.size * lit * math.saturate((reach - d) / (reach * 0.25f));
                if (size > 0.02f)
                    FireSystem.AddFlame(f.at, size, f.seed, colour);
            }
            // The nearest few crowns throw light round them (CaveSystem.MovingLights lights the surface too).
            var lights = 0;
            for (int i = 0; i < forestFlames.Count && lights < ForestLights; i++)
            {
                var f = forestFlames[i];
                if (f.size < 1.6f)
                    continue;
                var flicker = 0.8f + 0.2f * Mathf.Sin(Time.time * 9f + (f.seed & 255));
                var light = FireSystem.Light(colour, flicker * lit * 2.5f);
                light.w *= 2.4f;
                CaveSystem.MovingLights.Add((f.seed, f.at, light));
                lights++;
            }
        }

        /// <summary>
        /// W-112: one far flame in each burning tree's crown from where the near flames thin out to ForestFarRange,
        /// fading in over the band where the near ones shrink away, so nothing pops. <paramref name="range"/> is where
        /// the near flames really end (refactor 3.2: nearReach, not the nominal range). Gathered every two seconds or
        /// every 25 m the camera moves (a main-thread walk over the tree tiles in range).
        /// </summary>
        /// <summary>How many far flames the last gather placed (for probes and the profiler).</summary>
        public static int LastFarFlames;

        void FarForestFires(Vector3 eye, float range, int colour, float lit)
        {
            var far = Punchy.Settings.GraphicsSettings.ViewRange(ForestFarRange);
            if (far <= range)
                return;
            if (Time.unscaledTime >= nextFarGather || math.distancesq(farGatheredAt, (float3)eye) > 625f ||
                math.abs(range - farGatheredReach) > 5f)
            {
                nextFarGather = Time.unscaledTime + 2f;
                farGatheredAt = eye;
                farTrees.Clear();
                farFlames.Clear();
                flora.TreesNear(((float3)eye).xz, far, burningSpecies, farTrees);
                foreach (var t in farTrees)
                {
                    var d = math.distance(t.foot.xz, ((float3)eye).xz);
                    if (d < range * 0.7f)
                        continue;
                    var at = t.top - new float3(0f, t.height * 0.12f, 0f);
                    farFlames.Add((at, math.clamp(t.height * 0.3f, 2f, 5f), ForestSeedBase + 0x800000 + (int)((uint)t.seed & 0x7FFFFF)));
                }
                if (farFlames.Count > MaxFarFlames)
                {
                    var e = eye;
                    farFlames.Sort((a, b) => (a.at - e).sqrMagnitude.CompareTo((b.at - e).sqrMagnitude));
                    farFlames.RemoveRange(MaxFarFlames, farFlames.Count - MaxFarFlames);
                }
                LastFarFlames = farFlames.Count;
                // (Gathered with the hand-off as it was then; the gathers come every couple of seconds.)
                farGatheredReach = range;
            }
            for (int i = 0; i < farFlames.Count; i++)
            {
                var f = farFlames[i];
                var d = Vector3.Distance(f.at, eye);
                // In as the near flames shrink away (the last quarter of their range), out at the far edge.
                var size = f.size * lit * math.saturate((d - range * 0.75f) / (range * 0.25f)) * math.saturate((far - d) / (far * 0.15f));
                if (size > 0.05f)
                    FireSystem.AddFarFlame(f.at, size, f.seed, colour);
            }
        }

        /// <summary>The trees in range and their flames: two to four in each crown, sometimes one at the foot and one on the ground nearby.</summary>
        void GatherForest(in TerrainSource source, Vector3 eye, float range)
        {
            burningTrees.Clear();
            forestFlames.Clear();
            flora.TreesNear(((float3)eye).xz, range, burningSpecies, burningTrees);
            foreach (var t in burningTrees)
            {
                var h = (uint)t.seed;
                var crownFlames = 3 + (int)(Scramble(h) % 3u);
                var crown = math.max(t.crown * 0.45f, 0.4f);
                for (int k = 0; k < crownFlames; k++)
                {
                    var r = Scramble(h + (uint)k * 0x9E3779B9u);
                    var angle = (r & 0xFFFF) / 65535f * math.PI * 2f;
                    var out01 = ((r >> 16) & 0xFF) / 255f;
                    var down = ((r >> 24) & 0xFF) / 255f;
                    // In the crown: round the trunk's top, down into the branches, wider lower down.
                    var at = t.top + new float3(math.cos(angle), 0f, math.sin(angle)) * crown * out01 * (0.5f + down)
                             - new float3(0f, t.height * 0.35f * down, 0f);
                    var size = math.clamp(t.height * (0.22f + 0.14f * out01), 1.6f, 4.5f) * (k == 0 ? 1.25f : 1f);
                    forestFlames.Add((at, size, ForestSeedBase + (int)((h + (uint)k) & 0xFFFFFF)));
                }
                var extra = Scramble(h ^ 0xA5A5A5A5u);
                if ((extra & 3u) == 0u)
                {
                    // Burning roots at the foot.
                    forestFlames.Add(((Vector3)t.foot + Vector3.up * 0.05f, 0.9f + (extra >> 8 & 0xFF) / 255f * 0.7f,
                                      ForestSeedBase + (int)((h + 7u) & 0xFFFFFF)));
                }
                if ((extra & 12u) == 0u)
                {
                    // A patch of burning litter a few metres off, on the ground there.
                    var a = (extra >> 16 & 0xFF) / 255f * math.PI * 2f;
                    var at = t.foot + new float3(math.cos(a), 0f, math.sin(a)) * (1.5f + (extra >> 24 & 0xF) * 0.2f);
                    at.y = source.FloorUnder(at + new float3(0f, 1f, 0f), out _) + 0.03f;
                    forestFlames.Add((at, 0.6f + (extra >> 28) * 0.04f, ForestSeedBase + (int)((h + 11u) & 0xFFFFFF)));
                }
            }
            var e = eye;
            forestFlames.Sort((a, b) => (a.at - e).sqrMagnitude.CompareTo((b.at - e).sqrMagnitude));
            var cut = forestFlames.Count > MaxForestFlames;
            if (cut)
                forestFlames.RemoveRange(MaxForestFlames, forestFlames.Count - MaxForestFlames);
            // Where the near flames really end: the last one kept when the cap cut the list, else the whole range.
            nearReach = cut ? math.max(Vector3.Distance(forestFlames[forestFlames.Count - 1].at, eye), 10f) : float.MaxValue;
            LastForestLog = $"{burningTrees.Count} trees, {forestFlames.Count} flames, near reach {(cut ? nearReach : range):F0} of {range:F0} m{(cut ? " (capped)" : "")}";
            // (Logged when the cap starts or stops cutting, or the hand-off moves by 10 m: where near meets far.)
            var shown = cut ? nearReach : range;
            if (cut != lastCut || math.abs(shown - lastShownReach) > 10f)
            {
                (lastCut, lastShownReach) = (cut, shown);
                Debug.Log($"LandmarkEffectSystem: forest fire {LastForestLog}");
            }
        }
    }
}
