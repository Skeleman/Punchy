using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Punchy.Settings;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-178: glowing tendrils hanging from cave ceilings over GlowFungi patches (CaveWallBuild.Tendrils). Each one in
    /// reach (DetailPolicy.ExtraReach by its on-screen size) is a chain on the shared Punchy.Ambience.ChainSim: it sways
    /// and parts around the player, other players and creatures brushing it. Rebuilt as one tube mesh a frame in the
    /// cave wall vertex format and drawn with its own glowing copy of the cave material. Cosmetic and local: nothing
    /// collides, nothing is synced, and placement takes its own hash stream so the world's layout is unchanged.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>A tendril's size on screen for its reach (thin, but a couple of metres long).</summary>
        const float TendrilSize = 0.3f;
        const int TendrilPoints = 8;

        readonly Dictionary<long, int> tendrilChains = new Dictionary<long, int>();
        readonly HashSet<long> tendrilFine = new HashSet<long>();
        readonly List<long> tendrilDrop = new List<long>();
        readonly List<float4> tendrilPushers = new List<float4>();
        readonly List<Vector3> tendrilPositions = new List<Vector3>();
        readonly List<Vector3> tendrilNormals = new List<Vector3>();
        readonly List<Color> tendrilColours = new List<Color>();
        readonly List<Vector4> tendrilData = new List<Vector4>();
        readonly List<int> tendrilIndices = new List<int>();
        Punchy.Ambience.ChainSim tendrilSim;
        Material tendrilMaterial;
        Mesh tendrilMesh;
        World tendrilWorld;
        EntityQuery creatureQuery;

        void DrawTendrils(float3 camPos)
        {
            if (wallBuilds.Count == 0 && tendrilChains.Count == 0)
                return;
            tendrilSim ??= new Punchy.Ambience.ChainSim(TendrilPoints);
            tendrilSim.Complete();
            if (tendrilMaterial == null)
            {
                CaveWallDetails.Colours(CaveWallKind.GlowFungi, out var rock, out var dark);
                tendrilMaterial = new Material(Material) { name = "Cave tendrils" };
                tendrilMaterial.SetColor(RockColorId, Color.Lerp(rock, Color.white, 0.3f));
                tendrilMaterial.SetColor(DarkColorId, rock);
                tendrilMaterial.SetFloat(DressedId, -1f);
                tendrilMaterial.SetColor(OwnLightId, CaveWallDetails.OwnLight(CaveWallKind.GlowFungi) * 1.6f);
            }

            // Which tendrils swing: those big enough on screen; a chain is made when one comes in and dropped when it leaves.
            var reach = DetailPolicy.ExtraReach(TendrilSize, DetailPolicy.CavePlantPixels);
            var fine = DetailPolicy.ViewReach(TendrilSize, DetailPolicy.CavePlantFinestPixels);
            var seen = 0;
            foreach (var pair in wallBuilds)
            {
                var list = pair.Value.Tendrils;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    var id = ((long)pair.Key << 20) | (long)i;
                    var d = math.distance(t.anchor - new float3(0f, t.length * 0.5f, 0f), camPos);
                    var has = tendrilChains.TryGetValue(id, out var chain);
                    if (!DetailPolicy.Within(d, reach, has))
                    {
                        if (has)
                        {
                            tendrilSim.Remove(chain);
                            tendrilChains.Remove(id);
                            tendrilFine.Remove(id);
                        }
                        continue;
                    }
                    if (!has)
                    {
                        var settings = Punchy.Ambience.ChainSim.Settings.Hanging;
                        settings.Sway = 0.15f + t.random * 0.2f;
                        tendrilChains[id] = tendrilSim.Add(t.anchor, t.length, TendrilPoints, settings, t.random * 6.283f);
                    }
                    if (DetailPolicy.Within(d, fine, tendrilFine.Contains(id)))
                        tendrilFine.Add(id);
                    else
                        tendrilFine.Remove(id);
                    seen++;
                }
            }
            // Chains whose cave was dropped.
            if (tendrilChains.Count > seen)
            {
                tendrilDrop.Clear();
                foreach (var pair in tendrilChains)
                    if (!wallBuilds.TryGetValue((int)(pair.Key >> 20), out var build) || (int)(pair.Key & 0xFFFFF) >= build.Tendrils.Count)
                        tendrilDrop.Add(pair.Key);
                    else
                    {
                        var t = build.Tendrils[(int)(pair.Key & 0xFFFFF)];
                        var d = math.distance(t.anchor - new float3(0f, t.length * 0.5f, 0f), camPos);
                        if (!DetailPolicy.Within(d, reach, true))
                            tendrilDrop.Add(pair.Key);
                    }
                foreach (var id in tendrilDrop)
                {
                    tendrilSim.Remove(tendrilChains[id]);
                    tendrilChains.Remove(id);
                    tendrilFine.Remove(id);
                }
            }
            if (tendrilChains.Count == 0)
                return;

            BuildTendrilMesh(camPos);
            if (tendrilMesh.vertexCount > 0)
            {
                var rp = new RenderParams(tendrilMaterial)
                {
                    worldBounds = tendrilMesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true
                };
                Graphics.RenderMesh(rp, tendrilMesh, 0, Matrix4x4.identity);
            }

            GatherTendrilPushers(camPos, reach);
            tendrilSim.SetPushers(tendrilPushers);
            tendrilSim.Schedule(Time.deltaTime);
        }

        /// <summary>Tubes along every swinging chain, tapering to the tip; more sides when close.</summary>
        void BuildTendrilMesh(float3 camPos)
        {
            tendrilPositions.Clear();
            tendrilNormals.Clear();
            tendrilColours.Clear();
            tendrilData.Clear();
            tendrilIndices.Clear();
            var noRib = new Vector4(0f, 1f, 0f, SeamRib(0f, 1f));
            foreach (var pair in tendrilChains)
            {
                var sides = tendrilFine.Contains(pair.Key) ? 6 : 3;
                var points = tendrilSim.Points(pair.Value);
                var n = points.Length;
                var side = new float3(1f, 0f, 0f);
                for (int i = 0; i < n; i++)
                {
                    var along = math.normalizesafe(points[math.min(i + 1, n - 1)] - points[math.max(i - 1, 0)], new float3(0f, -1f, 0f));
                    // Carry the ring's frame down the chain so the tube doesn't twist.
                    side = math.normalizesafe(side - along * math.dot(side, along), math.abs(along.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f));
                    var up = math.cross(along, side);
                    var f = i / (n - 1f);
                    var radius = i == n - 1 ? 0.004f : 0.026f * (1f - f * 0.65f) + (i == n - 2 ? 0.01f : 0f);
                    var start = tendrilPositions.Count;
                    for (int s = 0; s < sides; s++)
                    {
                        math.sincos(s * 6.283185f / sides, out var sn, out var cs);
                        var normal = side * cs + up * sn;
                        tendrilPositions.Add(points[i] + normal * radius);
                        tendrilNormals.Add(normal);
                        // Brighter towards the tip, where the glow gathers.
                        tendrilColours.Add(new Color(0.5f + f * 0.4f, 0.5f + f * 0.4f, 0.5f + f * 0.4f, 0f));
                        tendrilData.Add(noRib);
                    }
                    if (i == 0)
                        continue;
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start - sides + s, b = start - sides + (s + 1) % sides, c = start + s, e = start + (s + 1) % sides;
                        tendrilIndices.Add(a); tendrilIndices.Add(c); tendrilIndices.Add(b);
                        tendrilIndices.Add(b); tendrilIndices.Add(c); tendrilIndices.Add(e);
                    }
                }
            }
            if (tendrilMesh == null)
            {
                tendrilMesh = new Mesh { name = "Cave tendrils", indexFormat = IndexFormat.UInt32 };
                tendrilMesh.MarkDynamic();
            }
            tendrilMesh.Clear();
            tendrilMesh.SetVertices(tendrilPositions);
            tendrilMesh.SetNormals(tendrilNormals);
            tendrilMesh.SetColors(tendrilColours);
            tendrilMesh.SetUVs(0, tendrilData);
            tendrilMesh.SetIndices(tendrilIndices, MeshTopology.Triangles, 0);
            tendrilMesh.RecalculateBounds();
        }

        /// <summary>The player, other players and creatures near the camera, as spheres the tendrils part around.</summary>
        void GatherTendrilPushers(float3 camPos, float reach)
        {
            tendrilPushers.Clear();
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || queryWorld != world)
                return;
            if (tendrilWorld != world)
            {
                creatureQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Fauna.CreatureBody>(),
                    ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                tendrilWorld = world;
            }
            AddBodies(playerQuery, camPos, reach);
            AddBodies(othersQuery, camPos, reach);
            if (creatureQuery.IsEmpty)
                return;
            using var bodies = creatureQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            foreach (var body in bodies)
                if (math.distancesq(body.Position, camPos) < reach * reach && tendrilPushers.Count < 24)
                    tendrilPushers.Add(new float4(body.Position + new float3(0f, 0.5f * body.Scale, 0f), 0.5f * body.Scale));
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
                    tendrilPushers.Add(new float4(body.Position + new float3(0f, 0.95f, 0f), 0.45f));
                    tendrilPushers.Add(new float4(body.Position + new float3(0f, 1.55f, 0f), 0.4f));
                }
        }

        void ReleaseTendrils()
        {
            tendrilSim?.Dispose();
            tendrilSim = null;
            tendrilChains.Clear();
            tendrilFine.Clear();
            if (tendrilMaterial != null)
                Destroy(tendrilMaterial);
            tendrilMaterial = null;
            if (tendrilMesh != null)
                Destroy(tendrilMesh);
            tendrilMesh = null;
        }
    }
}
