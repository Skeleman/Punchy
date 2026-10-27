using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>In the world subscene: the WorldState ghost prefab the host spawns once.</summary>
    public class WorldStateSpawnerAuthoring : MonoBehaviour
    {
        public GameObject Prefab;

        class Baker : Baker<WorldStateSpawnerAuthoring>
        {
            public override void Bake(WorldStateSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new WorldStateSpawner { Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.None) });
            }
        }
    }
}
