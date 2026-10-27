using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>On the creature ghost prefab (alongside its GhostAuthoringComponent): every animal is one of these.</summary>
    public class CreatureGhostAuthoring : MonoBehaviour
    {
        class Baker : Baker<CreatureGhostAuthoring>
        {
            public override void Bake(CreatureGhostAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CreatureNet { Species = -1, Scale = 1f });
            }
        }
    }
}
