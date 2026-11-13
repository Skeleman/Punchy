using Punchy.Fauna;
using Punchy.Net;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-netwatch [Species]" (A-103 row 2b): the joined-client check, logged every 10 s on any machine.</summary>
        string netWatchSpecies;
        float netWatchNext;
        readonly System.Collections.Generic.Dictionary<Entity, float3> netWatchLast = new System.Collections.Generic.Dictionary<Entity, float3>();

        /// <summary>
        /// "-netwatch [Species]" (default SeaFolk): every 10 s, as this machine sees it (solo, host or joined client),
        /// how many of that species there are, how many are under water (head under the surface) and how many of those
        /// moved since the last line; and for each other player, how far its feet are under the water there. Run on a
        /// joined client it shows the folk arriving and swimming; run on the host it shows a client's dive.
        /// </summary>
        void NetWatch()
        {
            if (netWatchSpecies == null || queryWorld == null || !queryWorld.IsCreated || Time.timeSinceLevelLoad < netWatchNext ||
                !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            netWatchNext = Time.timeSinceLevelLoad + 10f;
            var director = FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex(netWatchSpecies) : -1;
            var em = queryWorld.EntityManager;
            int count = 0, under = 0, moving = 0;
            if (index >= 0)
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                var entities = q.ToEntityArray(Allocator.Temp);
                var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
                for (int i = 0; i < states.Length; i++)
                {
                    if (bodies[i].Species != index || states[i].Gone)
                        continue;
                    count++;
                    var p = states[i].Position;
                    var g = terrain.SampleGround(p.xz);
                    if (!g.HasWater || g.WaterSurface < p.y + 1.5f)
                        continue;
                    under++;
                    if (netWatchLast.TryGetValue(entities[i], out var was) && math.distance(was, p) > 0.5f)
                        moving++;
                    netWatchLast[entities[i]] = p;
                }
                entities.Dispose();
                states.Dispose();
                bodies.Dispose();
            }
            var others = "";
            // (Row 4: each other player's body too, by its Playable Species index, so a host/client run shows the folk bodies arriving.)
            var avatars = FindAnyObjectByType<NetAvatars>();
            using (var players = em.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>()))
            {
                var pe = players.ToEntityArray(Allocator.Temp);
                foreach (var e in pe)
                {
                    if (em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e))
                        continue;
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    var g = terrain.SampleGround(p.xz);
                    var bodyName = "?";
                    if (em.HasComponent<NetPlayerLook>(e) && avatars != null)
                    {
                        var s = em.GetComponentData<NetPlayerLook>(e).Species;
                        bodyName = s >= 0 && s < avatars.PlayableSpecies.Count && avatars.PlayableSpecies[s] != null ? avatars.PlayableSpecies[s].name : $"#{s}";
                    }
                    others += $" player ({bodyName}) at ({p.x:F0}, {p.y:F1}, {p.z:F0})" + (g.HasWater ? $" feet {g.WaterSurface - p.y:F2} m under water;" : " on land;");
                }
                pe.Dispose();
            }
            var role = ServerExists() ? "host" : queryWorld.IsClient() ? "joined" : "solo";
            Debug.Log($"NetWatch ({role}) {netWatchSpecies}: {count} seen, {under} under water, {moving} of them moving;{(others.Length > 0 ? others : " no other players")}");
        }

        static bool ServerExists()
        {
            foreach (var w in Unity.Entities.World.All)
                if (w.IsCreated && w.IsServer())
                    return true;
            return false;
        }
    }
}
