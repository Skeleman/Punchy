using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>In the world subscene: the prefab the server instantiates for every animal.</summary>
    public class CreatureGhostSpawnerAuthoring : MonoBehaviour
    {
        public GameObject Prefab;

        class Baker : Baker<CreatureGhostSpawnerAuthoring>
        {
            public override void Bake(CreatureGhostSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CreatureGhostSpawner { Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
