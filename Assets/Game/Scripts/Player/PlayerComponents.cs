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
        /// <summary>How fast a river carries a swimmer downstream (m/s), doubled in rapids.</summary>
        public float RiverCurrent;
        /// <summary>Body radius for bumping into trunks and cacti.</summary>
        public float Radius;
    }

    /// <summary>Per-frame movement state.</summary>
    public struct PlayerMotion : IComponentData
    {
        public float VerticalVelocity;
        public bool Grounded;
        /// <summary>On the wing (a flying body): airspeed (m/s), and the climb (+) or dive (−) angle (radians).</summary>
        public bool Flying;
        public float Airspeed, Pitch;
        /// <summary>Beating its wings this frame (climbing, or pressing on), rather than gliding.</summary>
        public bool Flapping;
        /// <summary>
        /// Climbing up onto a ledge (0 when not): how far through (0..1); from where, the rim's height, and where it
        /// ends up on top.
        /// </summary>
        public float Mantle;
        public float3 MantleFrom, MantleTo;
        /// <summary>Standing on a prop's top (a roof, a rock, a rim), rather than the ground.</summary>
        public bool OnProp;
    }

    /// <summary>
    /// What the player's body can do, from its species (set by FaunaDirector): how fast it goes on the ground next to
    /// a human (SpeedFactor), and whether and how fast it flies.
    /// </summary>
    public struct PlayerBody : IComponentData
    {
        public float SpeedFactor;
        public bool Flies;
        public float FlySpeed;
        /// <summary>Can climb up onto ledges (a body with hands, or a climber); others only step up and drop down.</summary>
        public bool Climbs;
        /// <summary>Its height next to a human's (steps, reach and ledges scale with it); 0 reads as 1.</summary>
        public float Size;
        /// <summary>A fish: swims anywhere under the water; out of it, flops along in little hops.</summary>
        public bool Swims;
        /// <summary>A ghost: drifts above the ground or water, no gravity, rising with jump and sinking with sprint.</summary>
        public bool Hovers;
    }

    /// <summary>Singleton written by PlayerInputSystem each frame. MoveDirection is world-space XZ, length 0..1.</summary>
    public struct PlayerInput : IComponentData
    {
        public float2 MoveDirection;
        /// <summary>Sprinting or walking (toggles, one or the other; neither is the everyday jog).</summary>
        public bool Sprint, Walk;
        public bool JumpPressed;
        /// <summary>Jump and the sprint button held down (in flight: climb and dive).</summary>
        public bool JumpHeld, DiveHeld;
        /// <summary>The crouch button held down (a hovering body: sinking).</summary>
        public bool CrouchHeld;
        /// <summary>Where the camera looks, world XZ (in flight: the way to steer).</summary>
        public float2 CameraForward;
    }
}
