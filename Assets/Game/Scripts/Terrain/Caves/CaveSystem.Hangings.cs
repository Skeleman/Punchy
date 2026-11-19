using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Punchy.Settings;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-178: things hung in caves that players and creatures push through (CaveWallBuild.Hangings): glowing tendrils
    /// over GlowFungi patches, root curtains, vine drapes and ropes strung across mine passages. Each one in reach
    /// (DetailPolicy.ExtraReach by its kind's on-screen size) is a chain on the shared Punchy.Ambience.ChainSim: it sways
    /// and parts around the player, other players and creatures brushing it. Rebuilt as tube meshes each frame in the
    /// cave wall vertex format, one mesh per look (kind, and for tendrils the cave style's glow), drawn with copies of
    /// the cave material. Cosmetic and local: nothing collides, nothing is synced, and placement takes its own hash
    /// streams so the world's layout is unchanged.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>How a kind of hanging looks and swings.</summary>
        struct HangingLook
        {
            /// <summary>Size on screen for its reach (thin, but metres long).</summary>
            public float Size;
            /// <summary>Radius at the root, how much it tapers by the tip, and a bulb just above the tip (glow bead, leaf clump).</summary>
            public float Radius, Taper, Bulb;
            public int Points;
            public Color Colour;
            /// <summary>Vertex grey at root and tip (the cave shader shades the material colour by it).</summary>
            public float Root, Tip;
        }

        static readonly HangingLook[] HangingLooks =
        {
            // Tendril
            new HangingLook { Size = 0.3f, Radius = 0.026f, Taper = 0.65f, Bulb = 0.01f, Points = 8, Root = 0.5f, Tip = 0.9f },
            // Roots: thick, dark, knobbly with soil.
            new HangingLook { Size = 0.3f, Radius = 0.03f, Taper = 0.8f, Bulb = 0f, Points = 8, Colour = new Color(0.36f, 0.26f, 0.17f), Root = 0.55f, Tip = 0.85f },
            // Vines: thin and green with a leaf clump at the end.
            new HangingLook { Size = 0.25f, Radius = 0.016f, Taper = 0.35f, Bulb = 0.022f, Points = 9, Colour = new Color(0.28f, 0.42f, 0.17f), Root = 0.6f, Tip = 0.9f },
            // Rope: even hemp.
            new HangingLook { Size = 0.4f, Radius = 0.02f, Taper = 0f, Bulb = 0f, Points = 10, Colour = new Color(0.56f, 0.46f, 0.3f), Root = 0.75f, Tip = 0.75f },
        };

        const int HangingMaxPoints = 10;

        /// <summary>One look's mesh, rebuilt each frame.</summary>
        sealed class HangingBatch
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color> Colours = new List<Color>();
            public readonly List<Vector4> Data = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            public Material Material;
            public Mesh Mesh;
        }

        readonly Dictionary<long, int> hangingChains = new Dictionary<long, int>();
        readonly HashSet<long> hangingFine = new HashSet<long>();
        readonly List<long> hangingDrop = new List<long>();
        readonly List<float4> hangingPushers = new List<float4>();
        readonly Dictionary<int, HangingBatch> hangingBatches = new Dictionary<int, HangingBatch>();
        Punchy.Ambience.ChainSim hangingSim;
        World hangingWorld;
        EntityQuery creatureQuery;

        static bool Pinned(in CaveHanging t) => t.Kind == CaveHangingKind.Rope || math.any(t.End != 0f);

        static float3 HangingCentre(in CaveHanging t) =>
            Pinned(t) ? (t.Anchor + t.End) * 0.5f : t.Anchor - new float3(0f, t.Length * 0.5f, 0f);

        void DrawHangings(float3 camPos)
        {
            if (wallBuilds.Count == 0 && hangingChains.Count == 0)
                return;
            hangingSim ??= new Punchy.Ambience.ChainSim(HangingMaxPoints);
            hangingSim.Complete();

            // Which hangings swing: those big enough on screen; a chain is made when one comes in and dropped when it leaves.
            var seen = 0;
            var farthest = 0f;
            foreach (var pair in wallBuilds)
            {
                var list = pair.Value.Hangings;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    var look = HangingLooks[(int)t.Kind];
                    var reach = DetailPolicy.ExtraReach(look.Size, DetailPolicy.CavePlantPixels);
                    farthest = math.max(farthest, reach);
                    var id = ((long)pair.Key << 20) | (long)i;
                    var d = math.distance(HangingCentre(t), camPos);
                    var has = hangingChains.TryGetValue(id, out var chain);
                    if (!DetailPolicy.Within(d, reach, has))
                    {
                        if (has)
                        {
                            hangingSim.Remove(chain);
                            hangingChains.Remove(id);
                            hangingFine.Remove(id);
                        }
                        continue;
                    }
                    if (!has)
                        hangingChains[id] = hangingSim.Add(t.Anchor, t.Length, look.Points, SwingOf(t), t.Random * 6.283f, t.End);
                    if (DetailPolicy.Within(d, DetailPolicy.ViewReach(look.Size, DetailPolicy.CavePlantFinestPixels), hangingFine.Contains(id)))
                        hangingFine.Add(id);
                    else
                        hangingFine.Remove(id);
                    seen++;
                }
            }
            // Chains whose cave was dropped.
            if (hangingChains.Count > seen)
            {
                hangingDrop.Clear();
                foreach (var pair in hangingChains)
                    if (!wallBuilds.TryGetValue((int)(pair.Key >> 20), out var build) || (int)(pair.Key & 0xFFFFF) >= build.Hangings.Count)
                        hangingDrop.Add(pair.Key);
                    else
                    {
                        var t = build.Hangings[(int)(pair.Key & 0xFFFFF)];
                        var reach = DetailPolicy.ExtraReach(HangingLooks[(int)t.Kind].Size, DetailPolicy.CavePlantPixels);
                        if (!DetailPolicy.Within(math.distance(HangingCentre(t), camPos), reach, true))
                            hangingDrop.Add(pair.Key);
                    }
                foreach (var id in hangingDrop)
                {
                    hangingSim.Remove(hangingChains[id]);
                    hangingChains.Remove(id);
                    hangingFine.Remove(id);
                }
            }
            if (hangingChains.Count == 0)
                return;

            BuildHangingMeshes();
            foreach (var batch in hangingBatches.Values)
                if (batch.Mesh != null && batch.Mesh.vertexCount > 0)
                {
                    var rp = new RenderParams(batch.Material)
                    {
                        worldBounds = batch.Mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true
                    };
                    Graphics.RenderMesh(rp, batch.Mesh, 0, Matrix4x4.identity);
                }

            GatherHangingPushers(camPos, farthest);
            hangingSim.SetPushers(hangingPushers);
            hangingSim.Schedule(Time.deltaTime);
        }

        static Punchy.Ambience.ChainSim.Settings SwingOf(in CaveHanging t)
        {
            var s = Punchy.Ambience.ChainSim.Settings.Hanging;
            switch (t.Kind)
            {
                case CaveHangingKind.Tendril:
                    s.Sway = 0.15f + t.Random * 0.2f;
                    break;
                case CaveHangingKind.Roots:
                    s.Sway = 0.05f + t.Random * 0.08f;
                    s.Damping = 0.94f;
                    break;
                case CaveHangingKind.Vines:
                    s.Sway = 0.12f + t.Random * 0.15f;
                    break;
                case CaveHangingKind.Rope:
                    s.Sway = 0.04f;
                    s.SwayRate = 0.5f;
                    s.Damping = 0.95f;
                    break;
            }
            s.PinEnd = Pinned(t);
            return s;
        }

        /// <summary>The batch for a hanging's look: its kind, and for tendrils the style slot (their glow takes its hue).</summary>
        HangingBatch BatchOf(CaveHangingKind kind, int style)
        {
            var key = kind == CaveHangingKind.Tendril ? style * 8 : (int)kind + 1;
            if (hangingBatches.TryGetValue(key, out var batch))
                return batch;
            batch = new HangingBatch { Material = new Material(Material) { name = "Cave " + kind } };
            var m = batch.Material;
            m.SetFloat(DressedId, -1f);
            m.SetFloat(StyleAirId, 1f); // W-179: in the cave's haze like the walls and fungi
            if (kind == CaveHangingKind.Tendril)
            {
                CaveWallDetails.Colours(CaveWallKind.GlowFungi, out var rock, out _);
                var own = CaveWallDetails.OwnLight(CaveWallKind.GlowFungi) * 1.6f;
                // A style with its own glow (crystal, ...) tints the tendrils toward it at the same brightness, so they
                // don't read off-hue against its walls.
                var cs = style > 0 && styles != null && style - 1 < styles.Count ? styles[style - 1] : null;
                if (cs != null && cs.Glow > 0f && cs.GlowColor.grayscale > 0.01f)
                {
                    var hue = cs.GlowColor * (own.grayscale / cs.GlowColor.grayscale);
                    own = Color.Lerp(own, hue, 0.75f);
                    rock = Color.Lerp(rock, cs.GlowColor * (rock.grayscale / cs.GlowColor.grayscale), 0.6f);
                }
                m.SetColor(RockColorId, Color.Lerp(rock, Color.white, 0.3f));
                m.SetColor(DarkColorId, rock);
                m.SetColor(OwnLightId, new Color(own.r, own.g, own.b, 0f));
            }
            else
            {
                var c = HangingLooks[(int)kind].Colour;
                m.SetColor(RockColorId, c);
                m.SetColor(DarkColorId, c * 0.7f);
                m.SetColor(OwnLightId, Color.clear);
            }
            batch.Mesh = new Mesh { name = "Cave " + kind, indexFormat = IndexFormat.UInt32 };
            batch.Mesh.MarkDynamic();
            hangingBatches[key] = batch;
            return batch;
        }

        /// <summary>Tubes along every swinging chain, tapering to the tip; more sides when close.</summary>
        void BuildHangingMeshes()
        {
            foreach (var batch in hangingBatches.Values)
            {
                batch.Positions.Clear();
                batch.Normals.Clear();
                batch.Colours.Clear();
                batch.Data.Clear();
                batch.Indices.Clear();
            }
            foreach (var pair in hangingChains)
            {
                if (!wallBuilds.TryGetValue((int)(pair.Key >> 20), out var build))
                    continue;
                var t = build.Hangings[(int)(pair.Key & 0xFFFFF)];
                var look = HangingLooks[(int)t.Kind];
                var b = BatchOf(t.Kind, build.Style);
                // The cave's style slot rides in z, so Cave.shader's style-air path gives the hanging its cave's haze.
                var noRib = new Vector4(0f, 1f, build.Style, SeamRib(0f, 1f));
                var sides = hangingFine.Contains(pair.Key) ? 6 : 3;
                var points = hangingSim.Points(pair.Value);
                var n = points.Length;
                var side = new float3(1f, 0f, 0f);
                var tapered = look.Taper > 0f;
                for (int i = 0; i < n; i++)
                {
                    var along = math.normalizesafe(points[math.min(i + 1, n - 1)] - points[math.max(i - 1, 0)], new float3(0f, -1f, 0f));
                    // Carry the ring's frame down the chain so the tube doesn't twist.
                    side = math.normalizesafe(side - along * math.dot(side, along), math.abs(along.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f));
                    var up = math.cross(along, side);
                    var f = i / (n - 1f);
                    var radius = tapered && i == n - 1 ? look.Radius * 0.15f : look.Radius * (1f - f * look.Taper) + (i == n - 2 ? look.Bulb : 0f);
                    var grey = math.lerp(look.Root, look.Tip, f);
                    var start = b.Positions.Count;
                    for (int s = 0; s < sides; s++)
                    {
                        math.sincos(s * 6.283185f / sides, out var sn, out var cs);
                        var normal = side * cs + up * sn;
                        b.Positions.Add(points[i] + normal * radius);
                        b.Normals.Add(normal);
                        b.Colours.Add(new Color(grey, grey, grey, 0f));
                        b.Data.Add(noRib);
                    }
                    if (i == 0)
                        continue;
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start - sides + s, c = start - sides + (s + 1) % sides, d = start + s, e = start + (s + 1) % sides;
                        b.Indices.Add(a); b.Indices.Add(d); b.Indices.Add(c);
                        b.Indices.Add(c); b.Indices.Add(d); b.Indices.Add(e);
                    }
                }
            }
            foreach (var batch in hangingBatches.Values)
            {
                var mesh = batch.Mesh;
                mesh.Clear();
                if (batch.Positions.Count == 0)
                    continue;
                mesh.SetVertices(batch.Positions);
                mesh.SetNormals(batch.Normals);
                mesh.SetColors(batch.Colours);
                mesh.SetUVs(0, batch.Data);
                mesh.SetIndices(batch.Indices, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();
            }
        }

        /// <summary>The player, other players and creatures near the camera, as spheres the hangings part around.</summary>
        void GatherHangingPushers(float3 camPos, float reach)
        {
            hangingPushers.Clear();
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || queryWorld != world)
                return;
            if (hangingWorld != world)
            {
                creatureQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Fauna.CreatureBody>(),
                    ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                hangingWorld = world;
            }
            AddBodies(playerQuery, camPos, reach);
            AddBodies(othersQuery, camPos, reach);
            if (creatureQuery.IsEmpty)
                return;
            using var bodies = creatureQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            foreach (var body in bodies)
                if (math.distancesq(body.Position, camPos) < reach * reach && hangingPushers.Count < 24)
                    hangingPushers.Add(new float4(body.Position + new float3(0f, 0.5f * body.Scale, 0f), 0.5f * body.Scale));
        }

        /// <summary>A walker as two spheres, chest and head.</summary>
        void AddBodies(EntityQuery query, float3 camPos, float reach)
        {
            if (query.IsEmpty)
                return;
            using var bodies = query.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            foreach (var body in bodies)
                if (math.distancesq(body.Position, camPos) < reach * reach)
                {
                    hangingPushers.Add(new float4(body.Position + new float3(0f, 0.95f, 0f), 0.45f));
                    hangingPushers.Add(new float4(body.Position + new float3(0f, 1.55f, 0f), 0.4f));
                }
        }

        void ReleaseHangings()
        {
            hangingSim?.Dispose();
            hangingSim = null;
            hangingChains.Clear();
            hangingFine.Clear();
            foreach (var batch in hangingBatches.Values)
            {
                if (batch.Material != null)
                    Destroy(batch.Material);
                if (batch.Mesh != null)
                    Destroy(batch.Mesh);
            }
            hangingBatches.Clear();
        }
    }
}
