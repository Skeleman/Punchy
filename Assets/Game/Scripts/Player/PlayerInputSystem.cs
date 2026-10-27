using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Player
{
    /// <summary>
    /// Reads the project-wide Input System actions (Player/Move, Jump, Sprint) and turns them into a
    /// camera-relative world direction in the PlayerInput singleton. Managed because it touches
    /// InputSystem and Camera.main; everything downstream is Burst.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class PlayerInputSystem : SystemBase
    {
        InputAction move, jump, sprint;

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

            move = actions.FindAction("Player/Move");
            jump = actions.FindAction("Player/Jump");
            sprint = actions.FindAction("Player/Sprint");
            actions.FindActionMap("Player")?.Enable();
        }

        protected override void OnUpdate()
        {
            var raw = move != null ? (float2)move.ReadValue<Vector2>() : float2.zero;

            // Rotate stick/WASD input so "forward" means "away from the camera".
            var direction = raw;
            var cam = Camera.main;
            if (cam != null)
            {
                var forward = math.normalizesafe(((float3)cam.transform.forward).xz, new float2(0f, 1f));
                var right = new float2(forward.y, -forward.x);
                direction = forward * raw.y + right * raw.x;
            }

            if (math.lengthsq(direction) > 1f)
                direction = math.normalize(direction);

            SystemAPI.SetSingleton(new PlayerInput
            {
                MoveDirection = direction,
                Sprint = sprint != null && sprint.IsPressed(),
                JumpPressed = jump != null && jump.WasPressedThisFrame()
            });
        }
    }
}
