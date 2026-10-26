using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.CameraRig
{
    /// <summary>
    /// Orbit camera that follows the ECS player entity. Mouse / right stick orbits, scroll zooms.
    /// Click the Game view to capture the cursor, Escape releases it.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public float Distance = 8f;
        public float MinDistance = 3f;
        public float MaxDistance = 30f;
        public float TargetHeight = 1.6f;
        public float MouseSensitivity = 0.15f;
        public float StickSensitivity = 180f;
        public float ZoomSensitivity = 0.01f;
        public float MinPitch = -20f;
        public float MaxPitch = 80f;
        [Tooltip("Keep the camera at least this far above the ground.")]
        public float GroundClearance = 0.5f;

        float yaw;
        float pitch = 20f;

        /// <summary>Points the camera: yaw is the compass direction it looks towards, in degrees.</summary>
        public void SetView(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, MinPitch, MaxPitch);
        }
        EntityManager entityManager;
        EntityQuery playerQuery;
        EntityQuery terrainQuery;
        bool queriesCreated;

        void Start()
        {
            yaw = transform.eulerAngles.y;
            SetCursorCaptured(true);
        }

        void LateUpdate()
        {
            HandleCursor();
            if (!TryGetPlayerPosition(out var playerPosition))
                return;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var look = Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseSensitivity : Vector2.zero;
                if (Gamepad.current != null)
                    look += Gamepad.current.rightStick.ReadValue() * (StickSensitivity * Time.deltaTime);
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, MinPitch, MaxPitch);

                if (Mouse.current != null)
                    Distance = Mathf.Clamp(Distance - Mouse.current.scroll.ReadValue().y * ZoomSensitivity * Distance, MinDistance, MaxDistance);
            }

            var target = playerPosition + Vector3.up * TargetHeight;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var position = target - rotation * Vector3.forward * Distance;

            // Don't let the camera dip under hills or water; if it has to rise, keep looking at the player.
            if (terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
            {
                var ground = terrain.SampleGround(new Unity.Mathematics.float2(position.x, position.z));
                var minY = Mathf.Max(ground.Height, ground.HasWater ? ground.WaterSurface : float.MinValue) + GroundClearance;
                if (position.y < minY)
                {
                    position.y = minY;
                    rotation = Quaternion.LookRotation(target - position);
                }
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        bool TryGetPlayerPosition(out Vector3 position)
        {
            position = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            if (!queriesCreated)
            {
                entityManager = world.EntityManager;
                playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                queriesCreated = true;
            }

            // The subscene streams in asynchronously, so the player may not exist for the first few frames.
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;

            position = player.Position;
            return true;
        }

        void HandleCursor()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorCaptured(false);
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorCaptured(true);
        }

        static void SetCursorCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
