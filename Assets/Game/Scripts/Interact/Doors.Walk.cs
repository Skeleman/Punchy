using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// Which door the player means (P-25 row 1). Door anchors stand in front of their doors, where folk wait, so the old
    /// test aimed at a point the player had already walked past when standing at the door. This one measures to the
    /// door's opening, and it counts the way the camera looks as well as the way the body points.
    /// </summary>
    public static class DoorPick
    {
        /// <summary>Metres from a door's opening to the player.</summary>
        public const float Reach = 3f;
        /// <summary>How far a door anchor stands out from its door (AnchorBaking: 0.6 to 0.8 m).</summary>
        public const float OpeningBack = 0.6f;
        /// <summary>Seconds a prompt stays after the player stops qualifying, so it doesn't flicker.</summary>
        public const float Hold = 0.35f;
        /// <summary>A held door is let go beyond its reach and this.</summary>
        public const float HoldMargin = 0.5f;

        const float MaxHeight = 2f;
        // (Cosine of the half cone: about 75 degrees either side.)
        const float Cone = 0.25f;
        // Nearer than this the door needs no facing: the player is at it.
        const float Close = 1.2f;
        // How far behind the door's plane still counts: standing in the doorway does, the far side of the wall doesn't.
        const float Behind = -0.5f;

        public static float2 Opening(float3 anchor, float2 facing) => anchor.xz - math.normalizesafe(facing) * OpeningBack;

        /// <summary>
        /// Whether this door can be used from here, how far its opening is, and its score (lower wins: near and in front).
        /// <paramref name="forward"/> and <paramref name="look"/> are the body's and the camera's flat directions.
        /// </summary>
        public static bool Test(float3 player, float2 forward, float2 look, float3 anchor, float2 facing, out float distance, out float score)
        {
            distance = score = float.MaxValue;
            // (Almost every door on the island ends here.)
            var far = Reach + OpeningBack + 0.2f;
            if (math.distancesq(player.xz, anchor.xz) > far * far || math.abs(anchor.y - player.y) > MaxHeight)
                return false;
            var opening = Opening(anchor, facing);
            var to = opening - player.xz;
            distance = math.length(to);
            if (distance >= Reach || math.dot(-to, math.normalizesafe(facing)) < Behind)
                return false;
            var direction = math.normalizesafe(to);
            var ahead = math.max(math.dot(direction, forward), math.dot(direction, look));
            if (distance > Close && ahead < Cone)
                return false;
            score = distance * (2f - ahead);
            return true;
        }
    }

    /// <summary>
    /// "-doorwalk N" (P-25 row 1): walks the player up to ten doors from N on, from five angles each (running to the
    /// even-numbered tries' doors, strolling to the odd ones), and logs when the prompt shows against arriving 1.4 m from
    /// the opening. The rule the game used before P-25 is scored on the same frames, with why it refused. Add "-body" for
    /// a small animal. Pads and keyboards share this path: the rule doesn't read the device. A second number picks how
    /// each approach ends: 0 stops 1.4 m out; 1 walks on until the door stops the player; 2 walks into the wall 0.8 m to
    /// the side of the door; 3 stops 1.4 m out, then turns the body away with the camera still on the door.
    /// </summary>
    public partial class Doors
    {
        const float WalkFrom = 5f, ArriveAt = 1.4f, WatchFor = 0.6f, PassWithin = 0.2f;
        static readonly float[] WalkAngles = { -60f, -30f, 0f, 30f, 60f };

        int walkFirst = -1, walkDoor, walkAngle, walkPhase, walkPicked = -1, walkMode;
        float walkUntil, walkArrived, walkNewAt, walkOldAt, walkBest, walkProgressAt;
        int walkFlickers, walkTotal, walkReached, walkBlocked, walkNewPass, walkOldPass, walkItems;
        bool walkNewNow, walkOldNow;
        string walkOldWhy;
        float3 walkSpot;
        float2 walkOpening, walkTarget;
        float walkStallBest, walkStallAt;

        /// <summary>The rule before P-25, for comparison only.</summary>
        static bool OldRule(float3 player, float2 forward, float3 anchor, out string why)
        {
            var to = anchor - player;
            var d = math.length(to.xz);
            why = d >= 2.5f ? "reach" : math.abs(to.y) > 2f ? "height"
                : d > 0.3f && math.dot(math.normalizesafe(to.xz), forward) < 0.4f ? "cone" : null;
            return why == null;
        }

        void DoorWalk(ref TerrainSource terrain)
        {
            var now = Time.realtimeSinceStartup;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            var door = walkFirst + walkDoor;
            if (walkDoor >= 10 || !TryGetDoor(door, out var anchor, out var outward))
            {
                PlayerInputSystem.TestMove = null;
                PlayerInputSystem.TestWalk = false;
                Debug.Log($"[doorwalk] summary: {walkTotal} approaches, {walkReached} arrived, {walkBlocked} blocked; shown within {PassWithin} s of arriving and still up: new {walkNewPass}/{walkReached}, old {walkOldPass}/{walkReached}; {walkItems} ended with an item taking the key");
                walkFirst = -1;
                return;
            }
            if (walkPhase == 0)
            {
                var f = math.normalizesafe(outward);
                var a = math.radians(WalkAngles[walkAngle]);
                var (c, s) = (math.cos(a), math.sin(a));
                walkOpening = DoorPick.Opening(anchor, f);
                walkTarget = walkMode == 2 ? walkOpening + new float2(f.y, -f.x) * 0.8f : walkOpening;
                var xz = walkTarget + new float2(f.x * c - f.y * s, f.x * s + f.y * c) * WalkFrom;
                walkSpot = new float3(xz.x, terrain.SampleHeight(xz) + 0.05f, xz.y);
                var to = walkTarget - xz;
                t.Rotation = quaternion.RotateY(math.atan2(to.x, to.y));
                PlayerInputSystem.TestMove = float2.zero;
                PlayerInputSystem.TestWalk = walkDoor % 2 == 1;
                // (The houses' collision boxes load round the player after a teleport: longer for a new door's first try.)
                walkUntil = now + (walkAngle == 0 ? 6f : 2.5f);
                (walkArrived, walkNewAt, walkOldAt, walkFlickers, walkNewNow, walkOldNow) = (-1f, -1f, -1f, 0, false, false);
                walkPhase = 1;
            }
            if (walkPhase == 1)
            {
                t.Position = walkSpot;
                em.SetComponentData(player, t);
                if (now < walkUntil)
                    return;
                (walkBest, walkProgressAt, walkStallBest, walkStallAt) = (float.MaxValue, now, float.MaxValue, now);
                walkPhase = 2;
            }

            // Both rules, on this frame.
            var forward = math.normalizesafe(math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz);
            var newNow = walkPicked == door;
            var oldNow = OldRule(t.Position, forward, anchor, out walkOldWhy);
            if (newNow && walkNewAt < 0f)
                walkNewAt = now;
            if (oldNow && walkOldAt < 0f)
                walkOldAt = now;
            if (walkNewNow && !newNow)
                walkFlickers++;
            (walkNewNow, walkOldNow) = (newNow, oldNow);

            var off = walkTarget - t.Position.xz;
            var distance = math.length(off);
            if (walkPhase == 2)
            {
                if (distance < walkBest - 0.3f)
                    (walkBest, walkProgressAt) = (distance, now);
                if (distance < walkStallBest - 0.05f)
                    (walkStallBest, walkStallAt) = (distance, now);
                // (Modes 1 and 2 go on until something stops the player, or the player reaches the point itself.)
                var stopped = walkMode == 1 || walkMode == 2 ? distance < 0.1f || distance < 2.5f && now - walkStallAt > 0.4f : distance <= ArriveAt;
                if (stopped)
                {
                    PlayerInputSystem.TestMove = float2.zero;
                    walkArrived = now;
                    if (walkMode == 3)
                    {
                        // (120 degrees off the door, the camera left looking at it, as after a step aside.)
                        var toDoor = math.degrees(math.atan2(off.x, off.y));
                        t.Rotation = quaternion.RotateY(math.radians(toDoor + 120f));
                        em.SetComponentData(player, t);
                        var cam = Object.FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
                        if (cam != null)
                            cam.SetView(toDoor, 12f);
                    }
                    walkUntil = now + WatchFor;
                    walkPhase = 3;
                }
                else if (now - walkProgressAt > 2f)
                {
                    walkBlocked++;
                    walkTotal++;
                    Debug.Log($"[doorwalk] door {door} angle {WalkAngles[walkAngle]:F0}: BLOCKED {distance:F1} m from the opening");
                    NextWalk();
                }
                else
                    PlayerInputSystem.TestMove = off / distance;
                return;
            }
            if (now < walkUntil)
                return;
            var newPass = walkNewAt >= 0f && walkNewAt <= walkArrived + PassWithin && walkNewNow;
            var oldPass = walkOldAt >= 0f && walkOldAt <= walkArrived + PassWithin && walkOldNow;
            walkTotal++;
            walkReached++;
            walkNewPass += newPass ? 1 : 0;
            walkOldPass += oldPass ? 1 : 0;
            walkItems += facing < 0 && walkNewNow ? 1 : 0;
            var turned = math.degrees(math.acos(math.clamp(math.dot(forward, math.normalizesafe(off)), -1f, 1f)));
            Debug.Log($"[doorwalk] door {door} angle {WalkAngles[walkAngle]:F0} {(PlayerInputSystem.TestWalk ? "stroll" : "run")}: new {(newPass ? "ok" : "FAIL")} at {At(walkNewAt)}, flickers {walkFlickers}; old {(oldPass ? "ok" : "FAIL (" + walkOldWhy + ")")} at {At(walkOldAt)}; ended {math.distance(t.Position.xz, walkOpening):F2} m from the opening, body {turned:F0} deg off its target");
            NextWalk();
        }

        /// <summary>A first-shown time against arriving (negative: before it).</summary>
        string At(float when) => when < 0f ? "never" : $"{when - walkArrived:+0.00;-0.00} s";

        void NextWalk()
        {
            PlayerInputSystem.TestMove = float2.zero;
            walkPhase = 0;
            if (++walkAngle < WalkAngles.Length)
                return;
            walkAngle = 0;
            walkDoor++;
        }
    }
}
