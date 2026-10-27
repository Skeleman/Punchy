using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>In the world subscene: which prefab the server spawns for each player.</summary>
    public class NetPlayerSpawnerAuthoring : MonoBehaviour
    {
        public GameObject Prefab;

        class Baker : Baker<NetPlayerSpawnerAuthoring>
        {
            public override void Bake(NetPlayerSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NetPlayerSpawner { Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
