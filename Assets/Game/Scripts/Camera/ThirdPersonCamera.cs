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
        /// <summary>Testing: how fast the camera circles the watched point (degrees a second; 0 holds it still).</summary>
        public static float WatchSpin = 12f;
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
        EntityQuery supportQuery;
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
                yaw += Time.deltaTime * WatchSpin;
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
                // (A-30: a sphere cast through the rock's distance field from the head out, so it never steps over a thin
                // wall or a ceiling, from the cave's mouth in as well as deep inside; very close, the near plane comes in.)
                var head = new Unity.Mathematics.float3(target.x, target.y, target.z);
                var near = -1f;
                if (terrain.CaveDistance(head) < CaveNear)
                {
                    // The head under a low ceiling: the pivot drops until it's in the air.
                    for (var i = 0; i < 8 && PlayerMovementSystem.SolidDistance(terrain, ToFloat3(target)) < 0.15f && target.y > playerPosition.y + 0.4f; i++)
                        target.y -= 0.15f;
                    var back = position - target;
                    var length = back.magnitude;
                    var probeStart = ProbeStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                    var clear = CaveArm(terrain, target, back / Mathf.Max(length, 1e-4f), length);
                    if (ProbeStats)
                    {
                        ProbeCalls++;
                        ProbeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - probeStart;
                    }
                    caveArm = clear < caveArm ? clear : Mathf.MoveTowards(caveArm, clear, Time.deltaTime * 3f);
                    position = target + back * (Mathf.Min(caveArm, length) / Mathf.Max(length, 1e-4f));
                    if (caveArm < length - 0.01f)
                    {
                        rotation = Quaternion.LookRotation(target - position);
                        // Pulled in against rock: the near plane's corners stay inside the lens's sphere.
                        near = caveArm < CloseArm ? 0.05f : 0.1f;
                    }
                }
                else
                    caveArm = Distance;
                SetNearPlane(near);
                if (terrain.CaveDistance(new Unity.Mathematics.float3(position.x, position.y, position.z)) < 0f)
                {
                    transform.SetPositionAndRotation(position, rotation);
                    return;
                }
                var ground = terrain.SampleGround(new Unity.Mathematics.float2(position.x, position.z));
                var minY = Mathf.Max(ground.Height, ground.HasWater ? ground.WaterSurface : float.MinValue) + GroundClearance;
                // Roofs, rocks and rims (PropSupport): stay above one the camera has sunk into, not ones overhead.
                if (!supportQuery.IsEmpty)
                {
                    var holder = supportQuery.GetSingletonEntity();
                    var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                    var grids = em.GetComponentData<Punchy.Flora.SupportGrids>(holder);
                    if (grids.Set.IsCreated && Punchy.Flora.PropSupport.Sample(ref grids.Set.Value, em.GetBuffer<Punchy.Flora.FloraSupport>(holder, true),
                            new Unity.Mathematics.float3(position.x, position.y, position.z), 0.2f, 0.5f, out var top))
                        minY = Mathf.Max(minY, top.Height + GroundClearance);
                }
                if (position.y < minY)
                {
                    position.y = minY;
                    rotation = Quaternion.LookRotation(target - position);
                }
                // Out in the open, a hill between the player and the camera (a steep slope, A-37): the camera comes in
                // along its line to just before the ground, so the view isn't from inside the hill. Not near caves,
                // where the ground height says nothing about the tunnel the player is in.
                if (terrain.CaveDistance(head) >= CaveNear)
                {
                    var arm = position - target;
                    var armLength = arm.magnitude;
                    for (var i = 1; i <= SightSamples; i++)
                    {
                        var p = target + arm * ((float)i / SightSamples);
                        if (p.y >= terrain.SampleHeight(new Unity.Mathematics.float2(p.x, p.z)) + GroundClearance * 0.5f)
                            continue;
                        position = target + arm * Mathf.Max((i - 1f) / SightSamples, Mathf.Min(1f, MinSightArm / Mathf.Max(armLength, 1e-4f)));
                        break;
                    }
                }
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Testing (-oldcave): kept for the flag; the cave arm is now always the sphere trace (CaveArm).</summary>
        public static bool OldProbe;
        /// <summary>Testing (-perflog): time the cave arm probe.</summary>
        public static bool ProbeStats;
        public static long ProbeCalls, ProbeTicks;

        /// <summary>Within this of cave air (m) the camera casts for rock; the lens's radius and the gap it keeps from a hit.</summary>
        const float CaveNear = 6f, LensRadius = 0.3f, LensMargin = 0.05f;
        /// <summary>Closer in than this (m), the near plane comes in so the rock right at the lens isn't cut open.</summary>
        const float CloseArm = 1.2f;
        /// <summary>Out in the open: ground checks along the camera's line to the player, and the closest it comes in (m).</summary>
        const int SightSamples = 16;
        const float MinSightArm = 1.5f;

        static Unity.Mathematics.float3 ToFloat3(Vector3 v) => new Unity.Mathematics.float3(v.x, v.y, v.z);

        /// <summary>
        /// How far out from <paramref name="from"/> along <paramref name="dir"/> (unit) the lens can go, up to
        /// <paramref name="length"/>, before it touches solid (PlayerMovementSystem.SolidDistance): sphere-traced at half
        /// the distance each step (the cave field can overstate it), never less than 5 cm, so no wall is stepped over.
        /// Starting right against rock, the lens shrinks to fit, so a pivot by a wall still looks away from it.
        /// </summary>
        public static float CaveArm(in TerrainSource terrain, Vector3 from, Vector3 dir, float length)
        {
            var lens = Mathf.Clamp(PlayerMovementSystem.SolidDistance(terrain, ToFloat3(from)) * 0.9f, 0.05f, LensRadius);
            var t = 0f;
            for (var i = 0; i < 160; i++)
            {
                var d = PlayerMovementSystem.SolidDistance(terrain, ToFloat3(from + dir * t));
                if (d < lens)
                    return Mathf.Max(t - LensMargin, 0.2f);
                if (t >= length)
                    return length;
                t = Mathf.Min(t + Mathf.Max((d - lens) * 0.5f, 0.05f), length);
            }
            return t;
        }

        void SetNearPlane(float near)
        {
            if (cam == null)
                cam = GetComponent<Camera>();
            if (cam != null && defaultNear < 0f)
                defaultNear = cam.nearClipPlane;
            if (near < 0f)
                near = defaultNear;
            if (cam != null && !Mathf.Approximately(cam.nearClipPlane, near))
                cam.nearClipPlane = near;
        }

        Camera cam;
        /// <summary>The near plane the camera came with, put back once the lens is clear again.</summary>
        float defaultNear = -1f;

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
                supportQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.SupportGrids>(), ComponentType.ReadOnly<Punchy.Flora.FloraSupport>());
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
            // The main menu (Esc or Start) needs the mouse; take it back once it closes.
            if (Punchy.UI.Overlay.AnyOpen)
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
            // (Esc opens the menu, which frees the cursor above. A click takes it back after something else freed it,
            // such as switching windows.)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorCaptured(true);
        }

        static void SetCursorCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
