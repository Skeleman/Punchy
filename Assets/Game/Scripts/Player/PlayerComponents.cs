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
        /// <summary>How far below the water surface the feet are while swimming.</summary>
        public float SwimDepth;
        public float SwimSpeedMultiplier;
        /// <summary>Body radius for bumping into trunks and cacti.</summary>
        public float Radius;
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
