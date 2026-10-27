using Unity.Entities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>On the WorldState ghost prefab: the host's time of day and weather.</summary>
    public class WorldStateAuthoring : MonoBehaviour
    {
        class Baker : Baker<WorldStateAuthoring>
        {
            public override void Bake(WorldStateAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new WorldState { TimeOfDay = 12f });
                AddBuffer<WeatherCellState>(entity);
            }
        }
    }
}
