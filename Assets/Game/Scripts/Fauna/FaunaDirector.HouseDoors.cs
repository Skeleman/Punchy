using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-134 row 1: the Door anchors that are a household's own front door (by anchor index), so the brain's FindDoor
    /// keeps a lodger out of other folk's houses: a household member goes in by its own door (CreatureState.OwnDoor),
    /// and a folk with no house of its own takes only doors no household owns (a hall's, a wild door), or sleeps out.
    /// Built by FaunaDirector from the households in the world the creatures live in; the set belongs to the director
    /// and the brain only reads it.
    /// </summary>
    public struct HouseDoors : IComponentData
    {
        [ReadOnly] public NativeHashSet<int> Anchors;
    }

    public partial class FaunaDirector
    {
        NativeHashSet<int> houseDoorAnchors;
        Entity houseDoorsEntity;
        World houseDoorsWorld;

        /// <summary>Marks the Door anchors that are households' doors and hands the set to the brain (a singleton in the
        /// simulating world: only the host and a solo game run the folk's brains).</summary>
        void PublishHouseDoors(in TerrainSource source)
        {
            DropHouseDoors();
            if (simWorld == null || !simWorld.IsCreated || !source.Anchors.IsCreated)
                return;
            houseDoorAnchors = new NativeHashSet<int>(Households.All.Count * 2 + 1, Allocator.Persistent);
            ref var anchors = ref source.Anchors.Value.Anchors;
            for (int a = 0; a < anchors.Length; a++)
            {
                if (anchors[a].Kind != AnchorKind.Door)
                    continue;
                if (Ownership.HouseAt(anchors[a].Position.xz) != Ownership.Nobody)
                    houseDoorAnchors.Add(a);
            }
            var em = simWorld.EntityManager;
            houseDoorsEntity = em.CreateEntity();
            em.SetName(houseDoorsEntity, "House Doors");
            em.AddComponentData(houseDoorsEntity, new HouseDoors { Anchors = houseDoorAnchors });
            houseDoorsWorld = simWorld;
            if (LogHouses)
                UnityEngine.Debug.Log($"[households] {houseDoorAnchors.Count} door anchors are households' own (lodgers keep out)");
        }

        /// <summary>Takes the set back from the brain and frees it (the households' teardown, or a rebuild).</summary>
        void DropHouseDoors()
        {
            if (houseDoorsWorld != null && houseDoorsWorld.IsCreated)
            {
                var em = houseDoorsWorld.EntityManager;
                em.CompleteAllTrackedJobs();   // (a brain job may still be reading the set)
                if (em.Exists(houseDoorsEntity))
                    em.DestroyEntity(houseDoorsEntity);
            }
            houseDoorsWorld = null;
            houseDoorsEntity = Entity.Null;
            if (houseDoorAnchors.IsCreated)
                houseDoorAnchors.Dispose();
        }
    }
}
