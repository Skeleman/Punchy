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

        /// <summary>Testing: when set, the camera circles this point (an animal being watched) instead of the player.</summary>
        public static Vector3? WatchTarget;
        /// <summary>Testing: the pitch to watch from (low, looking up, for an animal up a tree).</summary>
        public static float WatchPitch = 15f;
        /// <summary>Testing: how far from the watched animal (close for small ones).</summary>
        public static float WatchDistance = 3.5f;
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
        /// <summary>In a cave: how far out the camera is now (it comes in at once for rock, out again slowly).</summary>
        float caveArm = 8f;

        /// <summary>Points the camera: yaw is the compass direction it looks towards, in degrees.</summary>
        public void SetView(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, MinPitch, MaxPitch);
        }
        EntityManager entityManager;
        EntityQuery playerQuery;
        EntityQuery terrainQuery;
        // The world the queries belong to; joining or hosting replaces the worlds, so they are rebuilt then.
        World queryWorld;
        bool menuWasOpen;

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
            if (WatchTarget.HasValue)
            {
                playerPosition = WatchTarget.Value - Vector3.up * TargetHeight;
                yaw += Time.deltaTime * 12f;
                pitch = Mathf.MoveTowards(pitch, WatchPitch, Time.deltaTime * 30f);
                Distance = Mathf.MoveTowards(Distance, WatchDistance, Time.deltaTime * 4f);
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var look = Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseSensitivity : Vector2.zero;
                if (Gamepad.current != null)
                    look += Gamepad.current.rightStick.ReadValue() * (StickSensitivity * Time.deltaTime);
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, MinPitch, MaxPitch);

                if (Mouse.current != null)
                    Distance = Mathf.Clamp(Distance - Mouse.current.scroll.ReadValue().y * ZoomSensitivity * Distance,
                                           WatchTarget.HasValue ? Mathf.Min(MinDistance, WatchDistance) : MinDistance, MaxDistance);
            }

            var target = playerPosition + Vector3.up * TargetHeight;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var position = target - rotation * Vector3.forward * Distance;

            // Don't let the camera dip under hills or water; if it has to rise, keep looking at the player.
            if (terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
            {
                // In a cave, the camera looks wherever the player turns it (up at the ceiling, down a shaft), on a
                // spring arm: it comes in along its line to just short of any rock in the way, at once, and eases
                // back out when the way clears (so bumpy walls don't pump it in and out).
                var head = new Unity.Mathematics.float3(target.x, target.y, target.z);
                if (terrain.CaveDistance(head) < 0f)
                {
                    var back = position - target;
                    var length = back.magnitude;
                    var clear = length;
                    for (var t = 0.25f; t <= length; t += 0.25f)
                    {
                        var p = target + back * (t / length);
                        if (terrain.IsRock(new Unity.Mathematics.float3(p.x, p.y, p.z), 0.35f))
                        {
                            clear = Mathf.Max(t - 0.45f, 0.3f);
                            break;
                        }
                    }
                    caveArm = clear < caveArm ? clear : Mathf.MoveTowards(caveArm, clear, Time.deltaTime * 3f);
                    position = target + back * (Mathf.Min(caveArm, length) / Mathf.Max(length, 1e-4f));
                }
                else
                    caveArm = Distance;
                if (terrain.CaveDistance(new Unity.Mathematics.float3(position.x, position.y, position.z)) < 0f)
                {
                    transform.SetPositionAndRotation(position, rotation);
                    return;
                }
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

            if (queryWorld != world)
            {
                entityManager = world.EntityManager;
                playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                queryWorld = world;
            }

            // The subscene streams in asynchronously, so the player may not exist for the first few frames.
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;

            position = player.Position;
            return true;
        }

        void HandleCursor()
        {
            // The multiplayer panel (F2) needs the mouse; take it back once the panel closes.
            if (Punchy.Net.NetMenu.IsOpen)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    SetCursorCaptured(false);
                menuWasOpen = true;
                return;
            }
            if (menuWasOpen)
            {
                menuWasOpen = false;
                SetCursorCaptured(true);
            }
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
