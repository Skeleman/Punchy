using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Player
{
    public struct PlayerTag : IComponentData { }

    /// <summary>Tunable movement settings, baked from PlayerAuthoring.</summary>
    public struct PlayerMotor : IComponentData
    {
        public float WalkSpeed;
        public float SprintMultiplier;
        public float JumpSpeed;
        public float Gravity;
        public float TurnSharpness;
        /// <summary>Max drop per frame that still counts as walking downhill instead of falling.</summary>
        public float GroundSnapDistance;
    }

    /// <summary>Per-frame movement state.</summary>
    public struct PlayerMotion : IComponentData
    {
        public float VerticalVelocity;
        public bool Grounded;
    }

    /// <summary>Singleton written by PlayerInputSystem each frame. MoveDirection is world-space XZ, length 0..1.</summary>
    public struct PlayerInput : IComponentData
    {
        public float2 MoveDirection;
        public bool Sprint;
        public bool JumpPressed;
    }
}
