using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Settings;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using GraphicsSettings = Punchy.Settings.GraphicsSettings;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-142 row 1 (#262): a few leaves drifting and tumbling down near the camera. They fall from the crowns of tall
    /// leafy trees (broadleaf and hive trees; conifers drop a needle now and then; palms, bushes and bare trees nothing),
    /// from giant mushroom caps (flakes of cap) and, in caves, from the hanging root curtains (dry rootlet bits). Each
    /// takes its tree's own leaf colour, some of them yellowed or browned; more fall in wind, and the wind carries them.
    /// They settle on the floor (FloorUnder, so cave floors and bridge decks too), lie a few seconds and fade.
    /// Cosmetic and local: a small pool simulated on the main thread (a few dozen at most) and drawn in one call, each
    /// leaf a quad shaped and lit in LeafFall.shader. Kept by screen size (rule 20): sources within the reach a leaf
    /// still covers a few pixels, and the pool scales with Detail; none at Low.
    /// </summary>
    public class LeafFallSystem : MonoBehaviour
    {
        /// <summary>A leaf's length (m) and the reference pixels it must keep to be worth simulating (rule 20).</summary>
        public const float LeafSize = 0.16f, LeafPixels = 2.6f;
        /// <summary>Leaves in flight or on the ground at Detail 1 (Medium); the pool scales with Detail.</summary>
        public const int BaseLeaves = 32, MostLeaves = 72;
        /// <summary>Below this Detail (the Low preset is 0.5) no leaves fall.</summary>
        public const float OffBelowDetail = 0.75f;
        /// <summary>Only trees and caps at least this tall (the species' tallest, m) drop anything.</summary>
        public const float TallTree = 5f, TallCap = 4f;

        /// <summary>Live leaves now; the sources by kind at the last gather; the reach (m); for probes and the report.</summary>
        public static int Live, Trees, Conifers, Caps, Roots;
        public static float LastReach;

        // Kinds, as the shader shapes them.
        const int Leaf = 0, Needle = 1, Flake = 2, Rootlet = 3;

        struct Source
        {
            public float3 Crown;     // the middle of where leaves come from
            public float Radius, Depth;
            public float Rate;       // leaves per second, calm air
            public Color A, B;
            public int Kind;
        }

        struct Drop
        {
            public float3 Position, Velocity;
            public quaternion Rotation;
            public float3 SpinAxis;
            public float Spin, Phase, Size, Floor, Age, Rest, Fade;
            public Color Color;
            public int Kind, State;   // 0 free, 1 falling, 2 on the ground
        }

        /// <summary>What the shader reads per leaf (64 bytes).</summary>
        struct GpuLeaf
        {
            public float3 Position;
            public float Size;
            public float4 Rotation;
            public float4 Color;     // rgb, a = how much is left of it (fade)
            public float Kind, Seed, Grounded, Pad;
        }

        readonly List<Source> sources = new List<Source>();
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> trees = new List<(float3, float3, float, float, int)>();
        readonly List<(FloraSpecies species, HashSet<FloraSpecies> set, int kind)> tall = new List<(FloraSpecies, HashSet<FloraSpecies>, int)>();
        readonly List<int> rootSpecies = new List<int>();
        List<FloraSpecies> caveSpecies = new List<FloraSpecies>();
        Drop[] drops = new Drop[0];
        GpuLeaf[] upload = new GpuLeaf[0];
        GraphicsBuffer buffer;
        MaterialPropertyBlock props;
        Material material;
        FloraSystem flora;
        BiomePalette speciesFrom;
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery;
        float nextGather;
        float3 gatheredAt = new float3(float.MaxValue);
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x51F3A2C7u);
        uint frame;
        static readonly int LeavesId = Shader.PropertyToID("_Leaves");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<LeafFallSystem>() == null)
                new GameObject("Leaf fall").AddComponent<LeafFallSystem>();
        }

        void OnDisable()
        {
            buffer?.Release();
            buffer = null;
            Live = 0;
        }

        void Update()
        {
            var cam = Camera.main;
            var count = GraphicsSettings.Detail < OffBelowDetail ? 0 : math.min(MostLeaves, (int)math.round(BaseLeaves * GraphicsSettings.Detail));
            if (count == 0 || cam == null || !TryGetWorld(out var source, out var palette))
            {
                Live = 0;
                return;
            }
            if (material == null)
            {
                material = Resources.Load<Material>("LeafFall");
                if (material == null)
                    return;
            }
            if (drops.Length != count)
            {
                System.Array.Resize(ref drops, count);
                upload = new GpuLeaf[count];
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 64);
            }

            float3 eye = cam.transform.position;
            var reach = DetailPolicy.ExtraReach(LeafSize, LeafPixels);
            LastReach = reach;
            if (Time.time >= nextGather || math.distancesq(eye, gatheredAt) > 36f)
            {
                nextGather = Time.time + 1f;
                gatheredAt = eye;
                Gather(source, palette, eye, reach);
            }

            var dt = math.min(Time.deltaTime, 0.1f);
            var windXZ = Climate.CameraWind;
            var wind01 = math.saturate(math.length(windXZ));
            Spawn(source, eye, reach, dt, wind01);
            Step(source, eye, reach, dt, windXZ, wind01);
            Draw(eye, reach);
        }

        /// <summary>The tall trees, caps and root curtains within reach, and what each drops.</summary>
        void Gather(in TerrainSource source, BiomePalette palette, float3 eye, float reach)
        {
            if (speciesFrom != palette)
            {
                speciesFrom = palette;
                tall.Clear();
                var seen = new HashSet<FloraSpecies>();
                foreach (var biome in palette.Biomes)
                    if (biome != null && biome.Flora != null)
                        foreach (var f in biome.Flora)
                        {
                            var s = f?.Species;
                            if (s == null || !seen.Add(s))
                                continue;
                            var kind = -1;
                            if ((s.Form == FloraForm.BroadleafTree || s.Form == FloraForm.HiveTree) && s.Height.y >= TallTree && s.Fullness >= 0.15f)
                                kind = Leaf;
                            else if (s.Form == FloraForm.Conifer && s.Height.y >= TallTree)
                                kind = Needle;
                            else if (s.Form == FloraForm.Mushroom && s.Height.y >= TallCap)
                                kind = Flake;
                            if (kind >= 0)
                                tall.Add((s, new HashSet<FloraSpecies> { s }, kind));
                        }
                rootSpecies.Clear();
                caveSpecies = CaveFloraTable.Collect(palette.Landmarks);
                for (int i = 0; i < caveSpecies.Count; i++)
                    if (caveSpecies[i] != null && caveSpecies[i].Form == FloraForm.Roots)
                        rootSpecies.Add(i);
            }

            sources.Clear();
            int trees0 = 0, conifers = 0, caps = 0, roots = 0;
            // Under the surface (a cave, out of sight of the mouths) the trees overhead drop nothing anyone sees.
            if (CaveSystem.SurfaceHidden < 0.99f)
            {
                if (flora == null)
                    flora = FindAnyObjectByType<FloraSystem>();
                if (flora != null)
                    foreach (var (species, set, kind) in tall)
                    {
                        trees.Clear();
                        flora.TreesNear(eye.xz, reach, set, trees);
                        foreach (var t in trees)
                        {
                            if (t.height < TallTree * 0.8f)
                                continue;
                            var r = math.max(t.crown, 1f);
                            // Calm air: a leaf every ten seconds or so from a big crown; needles far rarer.
                            var rate = 0.01f * r * r * (kind == Leaf ? math.lerp(0.5f, 1f, species.Fullness) : kind == Needle ? 0.12f : 0.4f);
                            // The budget is small, so nearer crowns get most of it: by how big a leaf looks from here
                            // (full within a seventh of the reach, a fiftieth at its edge, where leaves are a pixel or two).
                            var near = math.saturate(reach / (7f * math.max(math.distance(t.top.xz, eye.xz), 0.1f)));
                            rate *= near * near;
                            sources.Add(new Source
                            {
                                Crown = t.top - new float3(0f, t.height * (kind == Flake ? 0.08f : 0.3f), 0f),
                                Radius = r * (kind == Flake ? 0.9f : 0.75f),
                                Depth = kind == Flake ? 0.2f : t.height * 0.2f,
                                Rate = rate,
                                A = species.Foliage,
                                B = species.FoliageVariation,
                                Kind = kind
                            });
                            if (kind == Leaf) trees0++;
                            else if (kind == Needle) conifers++;
                            else caps++;
                        }
                    }
            }
            // Caves: bits of dry rootlet drift down from the root curtains in the ceiling.
            if (CaveSystem.Underground > 0.01f && rootSpecies.Count > 0 && source.Caves.IsCreated)
            {
                ref var set = ref source.Caves.Value;
                var reachSq = reach * reach;
                for (int i = 0; i < set.Plants.Length; i++)
                {
                    var plant = set.Plants[i];
                    if (!rootSpecies.Contains(plant.Species) || math.distancesq(plant.Position, eye) > reachSq)
                        continue;
                    var s = caveSpecies[plant.Species];
                    var length = math.lerp(s.Height.x, s.Height.y, 0.5f) * plant.Scale;
                    sources.Add(new Source
                    {
                        Crown = plant.Position - new float3(0f, length * 0.35f, 0f),
                        Radius = 0.45f * plant.Scale,
                        Depth = length * 0.3f,
                        Rate = 0.15f,
                        A = Color.Lerp(s.Foliage, s.Bark, 0.5f),
                        B = Color.Lerp(s.FoliageVariation, new Color(0.45f, 0.36f, 0.24f), 0.5f),
                        Kind = Rootlet
                    });
                    roots++;
                }
            }
            (Trees, Conifers, Caps, Roots) = (trees0, conifers, caps, roots);
        }

        void Spawn(in TerrainSource source, float3 eye, float reach, float dt, float wind01)
        {
            if (sources.Count == 0)
                return;
            // More come loose in wind (four times as many in a gale); root bits don't feel it.
            var windy = math.lerp(1f, 4f, wind01 * wind01);
            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var rate = src.Rate * (src.Kind == Rootlet ? 1f : windy);
                if (rng.NextFloat() >= rate * dt)
                    continue;
                var slot = -1;
                for (int i = 0; i < drops.Length; i++)
                    if (drops[i].State == 0) { slot = i; break; }
                if (slot < 0)
                    return;
                var disc = rng.NextFloat2Direction() * math.sqrt(rng.NextFloat()) * src.Radius;
                var at = src.Crown + new float3(disc.x, rng.NextFloat(-src.Depth, src.Depth), disc.y);
                if (math.distancesq(at, eye) > reach * reach)
                    continue;
                // Root curtains hang from the ceiling: a bit that would start in or just under the rock starts lower
                // down the curtain instead (FloorUnder counts the last half metre or so under a ceiling as floor, so
                // it would land at once), or not at all.
                if (src.Kind == Rootlet)
                {
                    var tries = 0;
                    while (tries++ < 12 && source.CaveDistance(at + new float3(0f, 0.5f, 0f)) > -0.8f)
                        at.y -= 0.25f;
                    if (tries > 12)
                        continue;
                }
                // The tree's own colours, with a share of them yellowed or browned (more in wind: those let go first).
                var c = Color.Lerp(src.A, src.B, rng.NextFloat());
                var dried = src.Kind == Leaf ? rng.NextFloat() < 0.55f + 0.2f * wind01 : src.Kind == Needle;
                if (dried)
                    c = Color.Lerp(c, rng.NextBool() ? new Color(0.62f, 0.48f, 0.16f) : new Color(0.42f, 0.27f, 0.13f), rng.NextFloat(0.4f, 0.8f));
                c *= rng.NextFloat(0.85f, 1.1f);
                drops[slot] = new Drop
                {
                    Position = at,
                    Velocity = float3.zero,
                    Rotation = quaternion.Euler(rng.NextFloat3(-math.PI, math.PI)),
                    SpinAxis = rng.NextFloat3Direction(),
                    Spin = rng.NextFloat(2f, 6f) * (src.Kind == Needle ? 1.6f : 1f),
                    Phase = rng.NextFloat(0f, 100f),
                    Size = LeafSize * rng.NextFloat(0.75f, 1.25f) * (src.Kind == Needle ? 0.6f : src.Kind == Flake ? 0.7f : src.Kind == Rootlet ? 0.8f : 1f),
                    Floor = source.FloorUnder(at, out _),
                    Age = 0f,
                    Rest = rng.NextFloat(4f, 9f),
                    Fade = 1f,
                    Color = c,
                    Kind = src.Kind,
                    State = 1
                };
            }
        }

        void Step(in TerrainSource source, float3 eye, float reach, float dt, float2 windXZ, float wind01)
        {
            frame++;
            var keep = reach * 1.25f;
            for (int i = 0; i < drops.Length; i++)
            {
                ref var d = ref drops[i];
                if (d.State == 0)
                    continue;
                d.Age += dt;
                if (math.distancesq(d.Position, eye) > keep * keep)
                {
                    d.State = 0;
                    continue;
                }
                if (d.State == 1)
                {
                    // Falling: a slow sink with a side-to-side flutter, carried by the wind (not underground), tumbling.
                    var flat = d.Kind == Leaf || d.Kind == Flake;
                    var sink = d.Kind == Needle ? 1.4f : d.Kind == Rootlet ? 0.9f : 0.7f;
                    var t = d.Age * (flat ? 2.1f : 1.3f) + d.Phase;
                    var sway = new float2(math.sin(t), math.cos(t * 0.73f)) * (flat ? 0.55f : 0.15f);
                    var carry = CaveSystem.Underground > 0.5f ? float2.zero : windXZ * (flat ? 0.7f : 0.4f);
                    var target = new float3(carry.x + sway.x, -sink * (flat ? 0.8f + 0.4f * math.abs(math.sin(t * 0.5f)) : 1f), carry.y + sway.y);
                    d.Velocity = math.lerp(d.Velocity, target, math.saturate(dt * 3f));
                    d.Position += d.Velocity * dt;
                    d.Rotation = math.normalize(math.mul(quaternion.AxisAngle(d.SpinAxis, d.Spin * dt), d.Rotation));
                    // The floor under it, every few frames (the leaves take turns).
                    if (((frame + (uint)i) & 3u) == 0u)
                        d.Floor = source.FloorUnder(d.Position + new float3(0f, 0.5f, 0f), out _);
                    if (d.Position.y <= d.Floor + 0.02f)
                    {
                        // Down: flat on the ground, at a yaw of its own, tipped a little.
                        d.Position.y = d.Floor + 0.02f;
                        d.Rotation = math.mul(quaternion.RotateY(d.Phase), quaternion.RotateX(math.PI * 0.5f + math.sin(d.Phase) * 0.15f));
                        d.State = 2;
                        d.Age = 0f;
                    }
                    else if (d.Age > 40f)
                        d.State = 0;   // caught on something it never reached the floor of
                }
                else
                {
                    // Lying: a few seconds, then gone (fading).
                    if (d.Age > d.Rest)
                        d.Fade -= dt * 0.5f;
                    if (d.Fade <= 0f)
                        d.State = 0;
                }
            }
        }

        void Draw(float3 eye, float reach)
        {
            var n = 0;
            for (int i = 0; i < drops.Length; i++)
            {
                var d = drops[i];
                if (d.State == 0)
                    continue;
                upload[n++] = new GpuLeaf
                {
                    Position = d.Position,
                    Size = d.Size,
                    Rotation = d.Rotation.value,
                    Color = new float4(d.Color.r, d.Color.g, d.Color.b, math.saturate(d.Fade) * math.saturate(d.Age * 4f + (d.State == 2 ? 1f : 0f))),
                    Kind = d.Kind,
                    Seed = math.frac(d.Phase * 0.618f),
                    Grounded = d.State == 2 ? 1f : 0f
                };
            }
            Live = n;
            if (n == 0)
                return;
            buffer.SetData(upload, 0, 0, n);
            props ??= new MaterialPropertyBlock();
            props.SetBuffer(LeavesId, buffer);
            var rp = new RenderParams(material)
            {
                matProps = props,
                worldBounds = new Bounds(eye, Vector3.one * (reach * 2.6f + 30f)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, n);
        }

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }
    }
}
