using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>On the NetPlayer ghost prefab (alongside its GhostAuthoringComponent).</summary>
    public class NetPlayerAuthoring : MonoBehaviour
    {
        class Baker : Baker<NetPlayerAuthoring>
        {
            public override void Bake(NetPlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<NetPlayer>(entity);
                AddComponent<NetPlayerCommand>(entity);
                AddComponent(entity, new NetPlayerLook { Species = -1 });
            }
        }
    }
}
