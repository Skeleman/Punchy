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
        /// <summary>
        /// #85 (P-7): the point the camera looks at, above the player's feet: TargetHeight for a human, scaled by the
        /// body's size (PlayerBody.Size) so a small body (a rabbit) stays in frame looking up underground; never under 0.3 m.
        /// </summary>
        public float PivotHeight => Mathf.Max(TargetHeight * bodySize, 0.3f);
        float bodySize = 1f;
        public float MouseSensitivity = 0.15f;
        /// <summary>P-8: moving the mouse or stick up looks down (Look tab, PlayerOptions).</summary>
        public static bool InvertY;
        public float StickSensitivity = 180f;
        public float ZoomSensitivity = 0.01f;
        public float MinPitch = -20f;
        public float MaxPitch = 80f;
        [Tooltip("Underground (caves, temple rooms): how far the camera can look up and down (#85).")]
        public float UndergroundMinPitch = -60f;
        public float UndergroundMaxPitch = 85f;
        [Tooltip("Keep the camera at least this far above the ground.")]
        public float GroundClearance = 0.5f;
        /// <summary>
        /// P-20 (#85, item 26): above ground, how far the view can look up: the camera slides along the ground in towards
        /// the character instead of stopping (MinPitch, -20 in the scene, was the old limit).
        /// </summary>
        const float LookUpMinPitch = -65f;
        /// <summary>Sliding in along the ground, the arm gets no shorter than this; past it the camera rises instead (m).</summary>
        const float MinSlideArm = 0.9f;
        float OpenMinPitch => Mathf.Min(MinPitch, LookUpMinPitch);

        float yaw;
        float pitch = 20f;
        /// <summary>In a cave: how far out the camera is now (it comes in at once for rock, out again slowly).</summary>
        float caveArm = 8f;
        /// <summary>Last frame's view was underground (cave or temple room): the wider pitch range applies.</summary>
        bool underground;

        /// <summary>Points the camera: yaw is the compass direction it looks towards, in degrees.</summary>
        public void SetView(float newYaw, float newPitch)
        {
            yaw = newYaw;
            // (#85: the underground range; above ground LateUpdate eases it back into MinPitch..MaxPitch.)
            pitch = Mathf.Clamp(newPitch, Mathf.Min(MinPitch, UndergroundMinPitch), Mathf.Max(MaxPitch, UndergroundMaxPitch));
        }
        EntityManager entityManager;
        EntityQuery playerQuery;
        EntityQuery terrainQuery;
        EntityQuery supportQuery;
        EntityQuery bodyQuery;
        // The world the queries belong to; joining or hosting replaces the worlds, so they are rebuilt then.
        World queryWorld;
        bool menuWasOpen;

        void Start()
        {
            yaw = transform.eulerAngles.y;
            camLog = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-camlog") >= 0;
            var spinAt = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-spin");
            if (spinAt >= 0 && spinAt + 1 < Punchy.Debugging.DebugControls.LaunchArgs.Length)
                float.TryParse(Punchy.Debugging.DebugControls.LaunchArgs[spinAt + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out spin);
            SetCursorCaptured(true);
        }

        void LateUpdate()
        {
            HandleCursor();
            if (!TryGetPlayerPosition(out var playerPosition))
                return;
            if (WatchTarget.HasValue)
            {
                playerPosition = WatchTarget.Value - Vector3.up * PivotHeight;
                yaw += Time.deltaTime * WatchSpin;
                pitch = Mathf.MoveTowards(pitch, WatchPitch, Time.deltaTime * 30f);
                Distance = Mathf.MoveTowards(Distance, WatchDistance, Time.deltaTime * 4f);
            }

            yaw += spin * Time.deltaTime;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var look = Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseSensitivity : Vector2.zero;
                if (Gamepad.current != null)
                    look += Gamepad.current.rightStick.ReadValue() * (StickSensitivity * Time.deltaTime);
                yaw += look.x;
                pitch = Mathf.Clamp(pitch + (InvertY ? look.y : -look.y), underground ? UndergroundMinPitch : OpenMinPitch, underground ? UndergroundMaxPitch : MaxPitch);

                if (Mouse.current != null)
                    Distance = Mathf.Clamp(Distance - Mouse.current.scroll.ReadValue().y * ZoomSensitivity * Distance,
                                           WatchTarget.HasValue ? Mathf.Min(MinDistance, WatchDistance) : MinDistance, MaxDistance);
            }

            // #85: back above ground, a look from the wider underground range eases into the normal one.
            if (!underground)
                pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(pitch, OpenMinPitch, MaxPitch), Time.deltaTime * 90f);
            var target = playerPosition + Vector3.up * PivotHeight;
            // #85 (P-7): a small body looking up underground. Circling the camera under a low pivot puts it on the floor
            // beside the body, which then drops out of the bottom of the frame; instead the camera circles no lower
            // than SmallLookOrbit and tilts its view up for the rest, at most until the body reaches the frame's edge.
            var orbitPitch = pitch;
            var lookUp = 0f;
            if (underground && bodySize < 0.75f && pitch < SmallLookOrbit)
            {
                orbitPitch = SmallLookOrbit;
                lookUp = SmallLookOrbit - pitch;   // (capped in Place, once the arm's length is known)
            }
            var rotation = Quaternion.Euler(orbitPitch, yaw, 0f);
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
                // #85: underground is a cave near the head, or a sunken temple room (its walls aren't cave field, but
                // SolidDistance knows them), or the head under the ground surface. There the rock probe decides, and
                // the open-air checks below (ground clamp, hill pull-in) are skipped: they pulled the camera in to
                // 1.5 m in temple rooms, into a small body (a rabbit), and up through the stone.
                var ceilingUp = head.y < terrain.SampleHeight(head.xz) - 0.3f;
                // (P-13: a house's room, off the island: InteriorCells boxes are solid to SolidDistance too.)
                underground = terrain.CaveDistance(head) < CaveNear || terrain.TempleAir(head) || ceilingUp ||
                              Punchy.Terrain.InteriorCells.Distance(head) < CaveNear;
                if (underground)
                {
                    var probeStart = ProbeStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                    float clear;
                    if (OldProbe)
                    {
                        // The head under a low ceiling: the pivot drops until it's in the air.
                        for (var i = 0; i < 8 && PlayerMovementSystem.SolidDistance(terrain, ToFloat3(target)) < 0.15f && target.y > playerPosition.y + 0.4f; i++)
                            target.y -= 0.15f;
                        var oldBack = position - target;
                        var oldLength = oldBack.magnitude;
                        clear = CaveArm(terrain, target, oldBack / Mathf.Max(oldLength, 1e-4f), oldLength);
                    }
                    else
                    {
                        // The same pivot drop and sphere trace in Burst (#54, the ArmProbe pattern from W-48): managed
                        // TerrainSource calls cost ~16 us each, and a tight chamber takes up to 160 of them a frame.
                        var result = new Unity.Collections.NativeArray<float>(2, Unity.Collections.Allocator.TempJob);
                        Unity.Jobs.IJobExtensions.Run(new ArmProbe
                        {
                            Terrain = terrain, Target = ToFloat3(target), Position = ToFloat3(position),
                            PlayerY = playerPosition.y, Result = result
                        });
                        target.y = result[0];
                        clear = result[1];
                        result.Dispose();
                    }
                    var back = position - target;
                    var length = back.magnitude;
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
                var lens = new Unity.Mathematics.float3(position.x, position.y, position.z);
                // (P-19: the lens may go under cave water now: Underwater draws it. P-18 kept it above.)
                if (terrain.CaveDistance(lens) < 0f || underground && (terrain.TempleAir(lens) || position.y < terrain.SampleHeight(lens.xz)))
                {
                    Place(position, rotation, lookUp, target, playerPosition);
                    return;
                }
                var ground = terrain.SampleGround(new Unity.Mathematics.float2(position.x, position.z));
                // (P-19: the ground only, not water: the lens may go under a lake, a river or the sea now, and Underwater
                // draws it. It used to stay above every water surface.)
                var minY = ground.Height + GroundClearance;
                var groundMinY = minY;
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
                    // P-20 (#85, item 26): looking up, the camera slides along the ground in towards the character with its
                    // view still pitched up, so the player can look further up (the sky, a tree, a cliff). The point on
                    // the arm where it meets the ground (+ clearance) is found by halving; only when that leaves less than
                    // MinSlideArm (a small body, a steep bank) does the camera rise and look down at the head as before.
                    var slid = false;
                    // (Only against bare ground: over a roof, rock or rim the ground height says nothing about the prop.)
                    if (orbitPitch < 0f && target.y > minY && minY <= groundMinY + 0.01f)
                    {
                        var arm = position - target;
                        float lo = 0f, hi = 1f;
                        for (var i = 0; i < 10; i++)
                        {
                            var mid = (lo + hi) * 0.5f;
                            var p = target + arm * mid;
                            if (p.y >= terrain.SampleGround(new Unity.Mathematics.float2(p.x, p.z)).Height + GroundClearance)
                                lo = mid;
                            else
                                hi = mid;
                        }
                        var slide = target + arm * lo;
                        var under = terrain.SampleGround(new Unity.Mathematics.float2(slide.x, slide.z)).Height + GroundClearance;
                        if (lo * arm.magnitude >= MinSlideArm && slide.y >= under - 0.01f)
                        {
                            position = slide;
                            slid = true;
                        }
                    }
                    if (!slid)
                    {
                        position.y = minY;
                        rotation = Quaternion.LookRotation(target - position);
                    }
                }
                // Out in the open, a hill between the player and the camera (a steep slope, A-37): the camera comes in
                // along its line to just before the ground, so the view isn't from inside the hill. Not near caves,
                // where the ground height says nothing about the tunnel the player is in.
                if (!underground)
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

            Place(position, rotation, lookUp, target, playerPosition);
        }

        /// <summary>
        /// Puts the camera there. A small body looking up (<paramref name="lookUp"/> degrees past the orbit) tilts the view
        /// up only as far as keeps its feet inside the frame's foot (P-9: the angle the body spans from the lens, plus a
        /// margin), so the whole body stays in view, not just its head.
        /// </summary>
        void Place(Vector3 position, Quaternion rotation, float lookUp, Vector3 target, Vector3 feet)
        {
            if (lookUp > 0f)
            {
                var half = (cam != null ? cam.fieldOfView : 60f) * 0.5f;
                var toTarget = target - position;
                var toFeet = feet - position;
                var span = Vector3.Angle(toTarget, toFeet);
                lookUp = Mathf.Clamp(lookUp, 0f, Mathf.Max(half - span - 4f, 0f));
            }
            transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(-lookUp, 0f, 0f));
            if (camLog && Time.unscaledTime >= nextCamLog && terrainQuery.TryGetSingleton<TerrainSource>(out var t))
            {
                nextCamLog = Time.unscaledTime + 2f;
                var p = ToFloat3(position);
                Debug.Log($"Camera: at ({p.x:F1}, {p.y:F1}, {p.z:F1}), pivot {PivotHeight:F2} m, arm {(position - target).magnitude:F2} m, yaw {yaw % 360f:F0} (view {transform.eulerAngles.y:F0}), pitch {pitch:F0} tilt {lookUp:F0}, " +
                          $"underground {underground}, in temple air {t.TempleAir(p)}, ground {t.SampleHeight(p.xz) - p.y:+0.0;-0.0} m vs lens, cave {t.CaveDistance(p):F1}");
            }
        }

        /// <summary>How far above a cave stream's or pool's surface the lens is kept (m).</summary>
        const float WaterClearance = 0.15f;
        float nextWaterLog;

        /// <summary>
        /// The surface of the cave stream or pool a point is under (its water, not just over its channel), or
        /// float.MinValue: the streams' segments (each point's surface height and width) and the pools' discs.
        /// </summary>
        public static float CaveWaterAbove(ref CaveSetBlob set, Unity.Mathematics.float3 p)
        {
            var best = float.MinValue;
            for (int s = 0; s < set.Streams.Length; s++)
            {
                ref var st = ref set.Streams[s];
                if (p.x < st.Min.x - 2f || p.z < st.Min.y - 2f || p.x > st.Max.x + 2f || p.z > st.Max.y + 2f)
                    continue;
                for (int k = st.FirstPoint; k < st.FirstPoint + st.PointCount - 1; k++)
                {
                    var a = set.StreamPoints[k];
                    var b = set.StreamPoints[k + 1];
                    var ab = b.Position.xz - a.Position.xz;
                    var t = Unity.Mathematics.math.saturate(Unity.Mathematics.math.dot(p.xz - a.Position.xz, ab) / Unity.Mathematics.math.max(Unity.Mathematics.math.lengthsq(ab), 1e-4f));
                    var half = Unity.Mathematics.math.lerp(a.Width, b.Width, t) * 0.5f;
                    if (Unity.Mathematics.math.distance(p.xz, a.Position.xz + ab * t) > half)
                        continue;
                    var level = Unity.Mathematics.math.lerp(a.Position.y, b.Position.y, t);
                    var depth = Unity.Mathematics.math.lerp(a.Depth, b.Depth, t);
                    if (p.y < level && p.y > level - depth - 1.5f)
                        best = Unity.Mathematics.math.max(best, level);
                }
            }
            for (int i = 0; i < set.Pools.Length; i++)
            {
                var pool = set.Pools[i];
                if (pool.Lava > 0.5f || Unity.Mathematics.math.distance(p.xz, pool.Centre.xz) > pool.Radius)
                    continue;
                if (p.y < pool.Centre.y && p.y > pool.Centre.y - pool.Depth - 1.5f)
                    best = Unity.Mathematics.math.max(best, pool.Centre.y);
            }
            return best;
        }

        /// <summary>Testing: "-spin N" turns the view N degrees a second, as a mouse would (P-18: is the yaw free?).</summary>
        float spin;

        /// <summary>Testing: "-camlog" logs where the camera is every 2 s (P-9: is it inside the room it should be?).</summary>
        bool camLog;
        float nextCamLog;
        /// <summary>#85: the lowest a small body's camera circles underground (deg); looking further up tilts the view instead.</summary>
        const float SmallLookOrbit = -10f;

        /// <summary>Testing (-oldcave): the cave arm's pivot drop and sphere trace in managed code (CaveArm), as before the Burst ArmProbe (#54).</summary>
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
                    // (P-9: centre already in the solid, as in a temple room, where SolidDistance is a flat 0.5 m and says nothing
                    // of the ceiling coming: back off by the whole lens, or the near plane looks through the roof at the sky.)
                    return Mathf.Max(t - (d < 0f ? lens + LensMargin : LensMargin), 0.2f);
                if (t >= length)
                    return length;
                t = Mathf.Min(t + Mathf.Max((d - lens) * 0.5f, 0.05f), length);
            }
            return t;
        }

        /// <summary>
        /// The cave arm in Burst (#54): the pivot drop under a low ceiling, then CaveArm's sphere trace, step for step.
        /// Result[0] is the pivot's height after the drop, Result[1] how far out the lens can go.
        /// </summary>
        [Unity.Burst.BurstCompile]
        public struct ArmProbe : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public Unity.Mathematics.float3 Target, Position;
            public float PlayerY;
            public Unity.Collections.NativeArray<float> Result;

            public void Execute()
            {
                var target = Target;
                for (var i = 0; i < 8 && PlayerMovementSystem.SolidDistance(Terrain, target) < 0.15f && target.y > PlayerY + 0.4f; i++)
                    target.y -= 0.15f;
                var back = Position - target;
                var length = Unity.Mathematics.math.length(back);
                var dir = back / Unity.Mathematics.math.max(length, 1e-4f);
                Result[0] = target.y;
                Result[1] = Trace(Terrain, target, dir, length);
            }

            static float Trace(in TerrainSource terrain, Unity.Mathematics.float3 from, Unity.Mathematics.float3 dir, float length)
            {
                var lens = Unity.Mathematics.math.clamp(PlayerMovementSystem.SolidDistance(terrain, from) * 0.9f, 0.05f, LensRadius);
                var t = 0f;
                for (var i = 0; i < 160; i++)
                {
                    var d = PlayerMovementSystem.SolidDistance(terrain, from + dir * t);
                    if (d < lens)
                        return Unity.Mathematics.math.max(t - (d < 0f ? lens + LensMargin : LensMargin), 0.2f);
                    if (t >= length)
                        return length;
                    t = Unity.Mathematics.math.min(t + Unity.Mathematics.math.max((d - lens) * 0.5f, 0.05f), length);
                }
                return t;
            }
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
                bodyQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
                queryWorld = world;
            }

            // The subscene streams in asynchronously, so the player may not exist for the first few frames.
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;

            position = player.Position;
            // (The body's size: PlayerBody is a singleton set by FaunaDirector; 0 reads as a human.)
            bodySize = bodyQuery.TryGetSingleton<PlayerBody>(out var body) && body.Size > 0f ? body.Size : 1f;
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
