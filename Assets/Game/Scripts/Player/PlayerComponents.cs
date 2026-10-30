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
        /// <summary>A bird settled on a perch (a ridge, a post, a chimney): held there until it moves or takes off.</summary>
        public bool Perched;
        /// <summary>A fish's leap or hop: how fast it carries on across while in the air (m/s).</summary>
        public float HopSpeed;
        /// <summary>
        /// Up a tree trunk (a climbing body): the trunk's foot, its radius and height, and where round it the body is
        /// (radians, from the trunk out to the body).
        /// </summary>
        public bool OnTrunk;
        /// <summary>Climbing straight up a wall (a lizard or spider body on a building's side or a rock).</summary>
        public bool OnWall;
        /// <summary>Seconds after jumping off a trunk or wall before it takes hold of one again.</summary>
        public float LatchCooldown;
        public float3 TrunkBase;
        public float TrunkRadius, TrunkHeight, TrunkAngle;
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
        /// <summary>Climbs tree trunks (a monkey, a lizard, a spider): walking into one, it goes up it; and how fast (m/s).</summary>
        public bool ClimbsTrees;
        public float ClimbSpeed;
        /// <summary>Climbs any surface (a lizard, a spider): a wall it's pushed into, it goes up.</summary>
        public bool ClimbsWalls;
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
        /// <summary>How far the camera looks up (+) or down (−), radians (a bird's gentle climbs, a fish's swim).</summary>
        public float CameraPitch;
    }
}
