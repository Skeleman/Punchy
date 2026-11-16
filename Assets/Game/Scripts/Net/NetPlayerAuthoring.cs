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
                AddComponent<NetPlayerKit>(entity);   // (P-35 r3 row 4: what it has on, the host's word; Items/Kit.cs)
            }
        }
    }
}
