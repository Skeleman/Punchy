using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-89 row 3: the marsh folk village's wildlife (W-122's stilt village in the Foggy marsh). Frogs sit on lily pads in
    /// the open water between the plank walks (the overworld table's MarshFrog rows), a heron stalks the shallows round
    /// the pool's rim (MarshHeron) and dragonflies hawk over the water (MarshDragonfly). The pads are seed-derived
    /// scenery, built on every machine when the island is (so a joined client sees the frogs' pads too); the animals are
    /// spawned by the host when the village is peopled, from their own rng per village, so no other spawn draw moves.
    /// The village layout (huts on a 20 m ring every 60 degrees, plank walks to the middle, cross walks between three hut
    /// pairs, one landing walk out at 30 degrees) is the world lane's MarshFolkVillage asset, as REPORTS-world gave it.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>The marsh folk village's landmark type (world lane, MarshFolkBaking.TypeName).</summary>
        const string MarshVillageName = "MarshFolkVillage";
        const uint MarshSalt = 0x3A75F0u;

        static readonly Color PadGreen = new Color(0.16f, 0.30f, 0.10f);
        static readonly Color PadLight = new Color(0.24f, 0.36f, 0.12f);
        static readonly Color PadFlower = new Color(0.95f, 0.78f, 0.86f);
        static readonly Color PadHeart = new Color(0.98f, 0.86f, 0.35f);

        readonly List<GameObject> marshPads = new List<GameObject>();

        /// <summary>A frog's pad: where it sits (on the water's surface) and how big the pad is.</summary>
        struct LilyPad
        {
            public float3 At;
            public float Radius;
        }

        /// <summary>
        /// The frogs' pads of one village (seed-derived, the same on every machine): one clump per frog in open water
        /// clear of the walks and huts, in water deep enough for a frog to float. The first pad of each clump is its
        /// frog's (the others, smaller, are only scenery and come after <paramref name="frogs"/>).
        /// </summary>
        static void MarshPads(in TerrainSource source, in LandmarkInstance inst, float radius, int frogs, List<LilyPad> into, List<LilyPad> extra)
        {
            into.Clear();
            extra?.Clear();
            var r = new Unity.Mathematics.Random(math.hash(new uint2(inst.Key, MarshSalt)) | 1u);
            var walks = MarshWalks(inst, radius);
            for (int n = 0; n < frogs; n++)
                for (int t = 0; t < 40; t++)
                {
                    var a = r.NextFloat(0f, 360f);
                    var d = r.NextFloat(9f, 26f) / radius;
                    var p = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(d, a), radius);
                    var g = source.SampleGround(p);
                    if (!g.HasWater || g.WaterSurface - g.Height < 0.45f || Near(walks, p) < 2.4f || Taken(into, p, 3f))
                        continue;
                    var main = new LilyPad { At = new float3(p.x, g.WaterSurface, p.y), Radius = r.NextFloat(0.24f, 0.32f) };
                    into.Add(main);
                    // (A few smaller pads round it, and now and then a flower.)
                    for (int k = 0, m = r.NextInt(2, 5); k < m; k++)
                    {
                        var o = p + r.NextFloat2Direction() * r.NextFloat(0.45f, 1.1f);
                        var pad = new LilyPad { At = new float3(o.x, g.WaterSurface, o.y), Radius = r.NextFloat(0.12f, 0.24f) };
                        extra?.Add(pad);
                    }
                    if (r.NextFloat() < 0.3f)
                        extra?.Add(new LilyPad { At = main.At + new float3(r.NextFloat(-0.15f, 0.15f), 0f, r.NextFloat(-0.15f, 0.15f)), Radius = -1f });
                    break;
                }
        }

        /// <summary>The village's plank walks as segments (world XZ): hut to middle, the three cross walks, the landing.</summary>
        static List<float4> MarshWalks(in LandmarkInstance inst, float radius)
        {
            var walks = new List<float4>();
            var middle = inst.Position;
            var huts = new float2[6];
            for (int k = 0; k < 6; k++)
            {
                huts[k] = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(0.5f, k * 60f), radius);
                walks.Add(new float4(middle, huts[k]));
            }
            for (int j = 0; j < 3; j++)
                walks.Add(new float4(huts[2 * j], huts[2 * j + 1]));
            walks.Add(new float4(LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(0.5f, 30f), radius),
                                 LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(1.05f, 30f), radius)));
            return walks;
        }

        /// <summary>The distance from a point to the nearest walk (huts count as 4.5 m wide walks' ends).</summary>
        static float Near(List<float4> walks, float2 p)
        {
            var best = float.MaxValue;
            foreach (var w in walks)
            {
                var a = w.xy;
                var ab = w.zw - a;
                var t = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-4f));
                best = math.min(best, math.distance(p, a + ab * t));
            }
            for (int k = 0; k < 6; k++)
                best = math.min(best, math.distance(p, walks[k].zw) - 2.5f);
            return best;
        }

        static bool Taken(List<LilyPad> pads, float2 p, float gap)
        {
            foreach (var pad in pads)
                if (math.distance(pad.At.xz, p) < gap)
                    return true;
            return false;
        }

        /// <summary>The frog count the table gives a village (the most its MarshFrog rows can ask for; no rng drawn).</summary>
        int MarshFrogCount()
        {
            var frogs = 0;
            foreach (var ri in TableRows(OverworldSlot.MarshFrog))
                frogs += math.max(0, overworld.Rows[ri].Count.y);
            return frogs;
        }

        /// <summary>Builds every marsh folk village's lily pads (on every machine, once per island build).</summary>
        void BuildMarshPads(in TerrainSource source, BiomePalette palette)
        {
            ClearMarshPads();
            if (overworld == null || !source.Landmarks.IsCreated || palette.Landmarks == null)
                return;
            ref var set = ref source.Landmarks.Value;
            var frogs = MarshFrogCount();
            var pads = new List<LilyPad>();
            var extra = new List<LilyPad>();
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var inst = set.Instances[i];
                if (inst.Type < 0 || inst.Type >= palette.Landmarks.Length || palette.Landmarks[inst.Type] == null ||
                    palette.Landmarks[inst.Type].name != MarshVillageName)
                    continue;
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                MarshPads(source, inst, radius, frogs, pads, extra);
                var root = new GameObject("MarshPads").transform;
                root.position = new Vector3(inst.Position.x, 0f, inst.Position.y);
                var m = new DunesColossus.MeshBuilder();
                var rot = new Unity.Mathematics.Random(math.hash(new uint2(inst.Key, MarshSalt ^ 0x9AD5u)) | 1u);
                void Pad(LilyPad p)
                {
                    var at = (Vector3)p.At - root.position + Vector3.up * 0.012f;
                    if (p.Radius < 0f)
                    {
                        // A water lily: pale petals round a yellow heart.
                        m.Ellipsoid(at + Vector3.up * 0.05f, new Vector3(0.11f, 0.05f, 0.11f), Quaternion.identity, PadFlower, 8, 3);
                        m.Ellipsoid(at + Vector3.up * 0.09f, new Vector3(0.04f, 0.03f, 0.04f), Quaternion.identity, PadHeart, 6, 2);
                        return;
                    }
                    var turn = Quaternion.Euler(0f, rot.NextFloat(0f, 360f), 0f);
                    m.Ellipsoid(at, new Vector3(p.Radius, 0.012f, p.Radius * 0.94f), turn, rot.NextBool() ? PadGreen : PadLight, 12, 3);
                }
                foreach (var p in pads)
                    Pad(p);
                foreach (var p in extra)
                    Pad(p);
                var go = new GameObject("Pads");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m.ToMesh("MarshLilyPads");
                go.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Colossus");
                marshPads.Add(root.gameObject);
                Debug.Log($"[marsh life] village {i} at ({inst.Position.x:F0}, {inst.Position.y:F0}): {pads.Count} frog pads, {extra.Count} more pads and lilies");
            }
        }

        void ClearMarshPads()
        {
            foreach (var go in marshPads)
                if (go != null)
                    Destroy(go);
            marshPads.Clear();
        }

        /// <summary>Spawns a marsh folk village's frogs, heron and dragonflies (nothing for any other landmark; host only).</summary>
        void SpawnMarshLife(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, float radius)
        {
            if (overworld == null || type == null || type.name != MarshVillageName)
                return;
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)MarshSalt)) | 1u);
            // Frogs, each on its own pad, sitting tight (a short leash round the pad).
            var pads = new List<LilyPad>();
            MarshPads(source, inst, radius, MarshFrogCount(), pads, null);
            var next = 0;
            int frogs = 0, herons = 0, flies = 0;
            foreach (var ri in TableRows(OverworldSlot.MarshFrog).ToArray())
            {
                var row = overworld.Rows[ri];
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count && next < pads.Count; n++, next++)
                    frogs += SpawnMarshAnimal(row, pads[next].At.xz, pads[next].At.xz, math.max(0.2f, row.Range), r.NextFloat(0f, 2f * math.PI), 0f) ? 1 : 0;
            }
            // The heron: a spot in the shallows round the pool's rim (water a hand deep, walkable), its beat along it.
            foreach (var ri in TableRows(OverworldSlot.MarshHeron).ToArray())
            {
                var row = overworld.Rows[ri];
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count; n++)
                    if (Shallows(source, inst, radius, ref r, out var home))
                        herons += SpawnMarshAnimal(row, home, home, math.max(2f, row.Range), r.NextFloat(0f, 2f * math.PI), 0f) ? 1 : 0;
            }
            // Dragonflies over the open water between the walks.
            var walks = MarshWalks(inst, radius);
            foreach (var ri in TableRows(OverworldSlot.MarshDragonfly).ToArray())
            {
                var row = overworld.Rows[ri];
                var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                for (int n = 0; n < count; n++)
                    for (int t = 0; t < 20; t++)
                    {
                        var p = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.NextFloat(7f, 30f) / radius, r.NextFloat(0f, 360f)), radius);
                        var g = source.SampleGround(p);
                        if (!g.HasWater || g.WaterSurface - g.Height < 0.3f || Near(walks, p) < 1.5f)
                            continue;
                        flies += SpawnMarshAnimal(row, p, p, math.max(2f, row.Range), r.NextFloat(0f, 2f * math.PI), 1f) ? 1 : 0;
                        break;
                    }
            }
            Debug.Log($"[marsh life] village {index} peopled: {frogs} frogs on pads, {herons} heron, {flies} dragonflies");
        }

        /// <summary>A spot in the pool's shallows (water 8-15 cm deep, the deepest a heron can walk), or false.</summary>
        static bool Shallows(in TerrainSource source, in LandmarkInstance inst, float radius, ref Unity.Mathematics.Random r, out float2 at)
        {
            for (int t = 0; t < 24; t++)
            {
                var a = r.NextFloat(0f, 360f);
                for (float d = 18f; d < 50f; d += 0.5f)
                {
                    var p = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(d / radius, a), radius);
                    var g = source.SampleGround(p);
                    var depth = g.HasWater ? g.WaterSurface - g.Height : -1f;
                    if (depth > 0.08f && depth <= 0.15f)
                    {
                        at = p;
                        return true;
                    }
                }
            }
            at = default;
            return false;
        }

        /// <summary>One marsh animal at a spot, homed and leashed (counted with the settled ones), lifted for a flier.</summary>
        bool SpawnMarshAnimal(SpawnRow row, float2 home, float2 at, float leash, float yaw, float lift)
        {
            if (row.Species == null || settledCount >= MaxSettled)
                return false;
            var index = SpeciesIndex(row.Species);
            if (index < 0)
                return false;
            var g = populationSource.SampleGround(at);
            var y = (g.HasWater ? math.max(g.Height, g.WaterSurface) : g.Height) + lift;
            Enqueue(index, new float3(at.x, y, at.y), SpawnTier.Settled, e =>
            {
                var em = simWorld.EntityManager;
                var state = em.GetComponentData<CreatureState>(e);
                state.Home = home;
                state.Leash = leash;
                em.SetComponentData(e, state);
            }, yaw);
            return true;
        }
    }
}
