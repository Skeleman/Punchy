using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Player
{
    /// <summary>
    /// Reads the project-wide Input System actions (Player/Move, Jump, Sprint, Walk) and turns them into a
    /// camera-relative world direction in the PlayerInput singleton. Sprint and walk are toggles, one press on and one
    /// off, each clearing the other; a sprint ends by itself when the player stops. In flight the sprint button dives
    /// while held instead (no sprint carried into the air or back). Managed because it touches
    /// InputSystem and Camera.main; everything downstream is Burst.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class PlayerInputSystem : SystemBase
    {
        InputAction move, jump, sprint, walk;
        /// <summary>The toggles' state, and how long the player has been standing still (a sprint ends after a moment).</summary>
        bool sprinting, walking;
        float stillFor;
        // "-autowalk" on the command line: walk in a slow circle with nobody at the keys (for multiplayer tests).
        bool autowalk;
        /// <summary>Tests: walk this way (world XZ) as if the stick were pushed, and sprint, while set.</summary>
        public static float2? TestMove;
        public static bool TestSprint, TestWalk;
        /// <summary>The toggles as they stand (for the readout).</summary>
        public static bool Sprinting, Walking;
        /// <summary>Tests: press jump once (cleared after the frame), and hold it (in flight: climb).</summary>
        public static bool TestJump, TestClimb;

        protected override void OnCreate()
        {
            EntityManager.CreateSingleton<PlayerInput>("PlayerInput");
        }

        protected override void OnStartRunning()
        {
            var actions = InputSystem.actions;
            if (actions == null)
            {
                Debug.LogError("No project-wide Input Actions asset is assigned (Project Settings > Input System Package).");
                return;
            }

            autowalk = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-autowalk") >= 0;
            move = actions.FindAction("Player/Move");
            jump = actions.FindAction("Player/Jump");
            sprint = actions.FindAction("Player/Sprint");
            walk = actions.FindAction("Player/Walk");
            actions.FindActionMap("Player")?.Enable();
        }

        protected override void OnUpdate()
        {
            InputGlyphs.Update();
            var raw = move != null ? (float2)move.ReadValue<Vector2>() : float2.zero;

            // Rotate stick/WASD input so "forward" means "away from the camera".
            var direction = raw;
            var cam = Camera.main;
            var cameraForward = new float2(0f, 1f);
            if (cam != null)
            {
                var forward = math.normalizesafe(((float3)cam.transform.forward).xz, new float2(0f, 1f));
                cameraForward = forward;
                var right = new float2(forward.y, -forward.x);
                direction = forward * raw.y + right * raw.x;
            }

            if (autowalk)
            {
                var a = (float)SystemAPI.Time.ElapsedTime * 0.25f;
                direction = new float2(math.cos(a), math.sin(a));
            }
            if (TestMove.HasValue)
                direction = TestMove.Value;
            if (math.lengthsq(direction) > 1f)
                direction = math.normalize(direction);
            // Typing a join code or address in the multiplayer panel shouldn't walk the player around.
            var typing = Punchy.Net.NetMenu.IsOpen;
            if (typing && !autowalk)
                direction = float2.zero;

            // The toggles (on the ground; in flight the sprint button dives instead, and nothing carries over).
            var flying = false;
            foreach (var motion in SystemAPI.Query<RefRO<PlayerMotion>>().WithAll<PlayerTag>())
                flying = motion.ValueRO.Flying;
            if (flying)
                sprinting = false;
            else if (!typing)
            {
                if (sprint != null && sprint.WasPressedThisFrame())
                {
                    sprinting = !sprinting;
                    walking = false;
                }
                if (walk != null && walk.WasPressedThisFrame())
                {
                    walking = !walking;
                    sprinting = false;
                }
            }
            stillFor = math.lengthsq(direction) < 0.01f ? stillFor + SystemAPI.Time.DeltaTime : 0f;
            if (stillFor > 0.4f)
                sprinting = false;
            Sprinting = sprinting;
            Walking = walking;

            SystemAPI.SetSingleton(new PlayerInput
            {
                MoveDirection = direction,
                Sprint = TestSprint || sprinting && !flying,
                Walk = TestWalk || walking && !sprinting && !TestSprint,
                DiveHeld = TestSprint || !typing && sprint != null && sprint.IsPressed(),
                JumpPressed = TestJump || !typing && jump != null && jump.WasPressedThisFrame(),
                JumpHeld = TestClimb || !typing && jump != null && jump.IsPressed(),
                CameraForward = TestMove.HasValue && math.lengthsq(TestMove.Value) > 0f ? math.normalize(TestMove.Value) : cameraForward
            });
            TestJump = false;
        }
    }
}
