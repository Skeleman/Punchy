using Punchy.Terrain;
using Unity.Entities;
using UnityEngine;

namespace Punchy.Player
{
    /// <summary>
    /// Put on the player's root GameObject in the world subscene. The root's position is the player's feet;
    /// child GameObjects with MeshRenderers become the visible body.
    /// </summary>
    public class PlayerAuthoring : MonoBehaviour
    {
        public float WalkSpeed = 6f;
        public float SprintMultiplier = 2f;
        public float JumpSpeed = 8f;
        public float Gravity = 25f;
        [Tooltip("How quickly the character turns to face its movement direction.")]
        public float TurnSharpness = 12f;
        public float GroundSnapDistance = 0.6f;
        [Tooltip("In water deeper than this the player swims, feet this far below the surface.")]
        public float SwimDepth = 1.3f;
        public float SwimSpeedMultiplier = 0.45f;

        class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlayerTag>(entity);
                AddComponent<TerrainFocus>(entity);
                AddComponent(entity, new PlayerMotor
                {
                    WalkSpeed = authoring.WalkSpeed,
                    SprintMultiplier = authoring.SprintMultiplier,
                    JumpSpeed = authoring.JumpSpeed,
                    Gravity = authoring.Gravity,
                    TurnSharpness = authoring.TurnSharpness,
                    GroundSnapDistance = authoring.GroundSnapDistance,
                    SwimDepth = authoring.SwimDepth,
                    SwimSpeedMultiplier = authoring.SwimSpeedMultiplier
                });
                AddComponent(entity, new PlayerMotion { Grounded = false });
            }
        }
    }
}
