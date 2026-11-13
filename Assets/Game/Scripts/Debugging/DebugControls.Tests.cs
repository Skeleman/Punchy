using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, wall and climb tests, body swaps, the right face button checks and clear shots. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        /// <summary>The least height of a flat-topped wall for `-place wall` (m; A-14 #33: a tall garden wall, not a 1 m one).</summary>
        const float WallMin = 2f;
        /// <summary>`-place wall`'s search: the settlements already tried, the tallest flat-topped wall met in them, and when
        /// the next one's props will have loaded.</summary>
        readonly HashSet<int> wallVisited = new();
        float wallTallest, wallSearchAt;

        /// <summary>
        /// No flat-topped wall of WallMin near: logs the tallest met here and moves the player to the nearest settlement not
        /// yet tried, to look again once its props have loaded. False after eight settlements, or with none left.
        /// </summary>
        bool NextWallSettlement(in TerrainSource source, EntityManager em, Entity playerEntity, LocalTransform t)
        {
            if (!source.Settlements.IsCreated || wallVisited.Count >= 8)
                return false;
            ref var towns = ref source.Settlements.Value;
            int here = -1, next = -1;
            float hereDist = float.MaxValue, nextDist = float.MaxValue;
            for (int k = 0; k < towns.Settlements.Length; k++)
            {
                var dist = math.distance(towns.Settlements[k].Centre, t.Position.xz);
                if (dist < hereDist)
                {
                    hereDist = dist;
                    here = k;
                }
            }
            wallVisited.Add(here);
            for (int k = 0; k < towns.Settlements.Length; k++)
            {
                var dist = math.distance(towns.Settlements[k].Centre, t.Position.xz);
                if (!wallVisited.Contains(k) && dist < nextDist)
                {
                    nextDist = dist;
                    next = k;
                }
            }
            Debug.Log($"DebugControls: wall search: no flat-topped wall of {WallMin:F1} m at settlement {here} (tallest flat-topped {wallTallest:F1} m)" +
                      (next >= 0 ? $"; trying settlement {next}, {nextDist:F0} m off" : "; none left"));
            if (next < 0)
                return false;
            wallTallest = 0f;
            var c = towns.Settlements[next].Centre;
            t.Position = new float3(c.x, source.SampleHeight(c) + 0.5f, c.y);
            em.SetComponentData(playerEntity, t);
            em.SetComponentData(playerEntity, new PlayerMotion { Grounded = true });
            ledgeRequest = true;
            wallSearchAt = Time.timeSinceLevelLoad + 6f;
            return true;
        }

        /// <summary>
        /// Logs a climb test's spot, with flags for a client to watch it from the side; and with "-autowalk", starts it
        /// (ClimbTest).
        /// </summary>
        void StartClimbTest(float3 home, float3 target, float3 lookAt, bool jumps, string what)
        {
            var across = math.normalizesafe(target.xz - home.xz, new float2(0f, 1f));
            var side = new float2(across.y, -across.x);
            var mid = (home.xz + target.xz) * 0.5f;
            var eye = new float3(mid.x + side.x * 7f, lookAt.y + 1.5f, mid.y + side.y * 7f);
            // (The client's own body parked behind its camera, out of shot.)
            var park = eye.xz + side * 4f;
            Debug.Log($"DebugControls: climb test: {what}, player from ({V(home.x)}, {V(home.y)}, {V(home.z)}); " +
                      $"to watch: -goto {V(park.x)},{V(park.y)} -camlook {V(eye.x)},{V(eye.y)},{V(eye.z)},{V(lookAt.x)},{V(lookAt.y)},{V(lookAt.z)}");
            if (System.Array.IndexOf(LaunchArgs, "-autowalk") < 0)
                return;
            climbHome = home.xz;
            climbHomeY = home.y;
            climbTarget = target.xz;
            climbJumps = jumps;
            climbSince = Time.time;
        }

        /// <summary>The player's climbing skill now (A-101), for the climb tests' log lines; -1 with no body yet.</summary>
        int ClimbSkillNow()
        {
            using var bodyQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            return bodyQuery.TryGetSingleton<PlayerBody>(out var body) ? body.ClimbSkill : -1;
        }

        static string V(float v) => v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A band as "-climbband" takes it: x,y,z,ox,oz,halfwidth,height,grade.</summary>
        static string ClimbBandArg(in ClimbBand band)
        {
            static string F(float v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            return $"{F(band.Base.x)},{F(band.Base.y)},{F(band.Base.z)},{F(band.Out.x)},{F(band.Out.y)},{F(band.HalfWidth)},{F(band.Height)},{band.Grade}";
        }

        static bool ParseClimbBand(string text, out ClimbBand band)
        {
            band = default;
            var parts = text.Split(',');
            if (parts.Length != 8)
                return false;
            var v = new float[7];
            for (int i = 0; i < 7; i++)
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i]))
                    return false;
            if (!byte.TryParse(parts[7], out var grade))
                return false;
            band = new ClimbBand
            {
                Base = new float3(v[0], v[1], v[2]), Out = math.normalizesafe(new float2(v[3], v[4]), new float2(0f, 1f)),
                HalfWidth = math.clamp(v[5], 0.1f, 10f), Height = math.clamp(v[6], 0.5f, 60f),
                Grade = (byte)math.clamp((int)grade, (int)Climbing.Ladder, (int)Climbing.Most)
            };
            return true;
        }

        /// <summary>
        /// Shows a marked band on its wall (cosmetic, test only): grade 1 a ladder (two rails, a rung every 0.3 m), else
        /// vines, strands of stem hung with leaves, a little uneven, across its width. Plain boxes in the items' material.
        /// </summary>
        void MarkClimbBand(in ClimbBand band)
        {
            if (climbMarkMaterial == null)
                climbMarkMaterial = Resources.Load<Material>("ItemMaterial");
            climbMarkBlock ??= new MaterialPropertyBlock();
            var across = new float3(-band.Out.y, 0f, band.Out.x);
            var outward = new float3(band.Out.x, 0f, band.Out.y);
            var basePoint = band.Base;
            var facing = Quaternion.LookRotation(new Vector3(-band.Out.x, 0f, -band.Out.y));
            var top = band.Height - 0.5f;
            void Box(float x, float o, float y, float3 size, Color colour, float roll = 0f)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "ClimbBandMark";
                go.transform.SetPositionAndRotation(basePoint + across * x + outward * o + new float3(0f, y, 0f), facing * Quaternion.Euler(0f, 0f, roll));
                go.transform.localScale = size;
                var r = go.GetComponent<MeshRenderer>();
                if (climbMarkMaterial != null)
                    r.sharedMaterial = climbMarkMaterial;
                climbMarkBlock.SetColor(ClimbMarkColorId, colour);
                r.SetPropertyBlock(climbMarkBlock);
                climbMarks.Add(go);
            }
            if (band.Grade == Climbing.Ladder)
            {
                var wood = new Color(0.42f, 0.28f, 0.15f);
                for (int s = -1; s <= 1; s += 2)
                    Box(s * 0.25f, 0.12f, top * 0.5f + 0.25f, new float3(0.07f, top + 0.5f, 0.07f), wood);
                for (var y = 0.3f; y < top + 0.1f; y += 0.3f)
                    Box(0f, 0.12f, y, new float3(0.5f, 0.05f, 0.05f), wood * 0.9f);
                return;
            }
            var random = new Unity.Mathematics.Random((0x51ED1EAFu ^ (uint)math.abs((int)(band.Base.x * 7f + band.Base.z * 13f))) | 1u);
            var strands = math.max((int)(band.HalfWidth * 2f / 0.4f), 2);
            for (int s = 0; s < strands; s++)
            {
                var x = -band.HalfWidth + (s + 0.5f) * band.HalfWidth * 2f / strands + random.NextFloat(-0.08f, 0.08f);
                // (Hung from the top, some strands shorter.)
                var length = top * random.NextFloat(0.75f, 1f);
                Box(x, 0.04f, top - length * 0.5f, new float3(0.05f, length, 0.04f), new Color(0.2f, 0.3f, 0.12f), random.NextFloat(-2f, 2f));
                for (var y = top - 0.15f; y > top - length; y -= random.NextFloat(0.22f, 0.38f))
                {
                    var side = random.NextBool() ? 1f : -1f;
                    Box(x + side * 0.07f, 0.06f, y, new float3(0.14f, 0.09f, 0.03f),
                        Color.HSVToRGB(random.NextFloat(0.22f, 0.32f), random.NextFloat(0.5f, 0.7f), random.NextFloat(0.35f, 0.55f)), side * random.NextFloat(20f, 45f));
                }
            }
        }

        float climbNextJump;

        /// <summary>
        /// The climb test, as if the stick were pushed: 12 s at the trunk or over the rim (jumping at a rim, which is
        /// what climbs onto it), then 8 s back to where it started (down the trunk, off the top), and again.
        /// </summary>
        /// <summary>Whether the climb test's player held a trunk last frame (to log each new hold once).</summary>
        static bool climbHolding;
        /// <summary>"-place wall": a wall climber's test (up, along, down), and the last time it logged.</summary>
        static bool wallRequest, climbWall;
        /// <summary>"-place climb": the grade of the band it marks on the wall (0: a bare wall test).</summary>
        static byte climbBandGrade;
        /// <summary>The climb test's wall height (m), for where it lets go on a drop lap.</summary>
        float climbWallTop;
        /// <summary>The marked bands' markings (vines, a ladder), cleared with the shot.</summary>
        readonly List<GameObject> climbMarks = new();
        static readonly int ClimbMarkColorId = Shader.PropertyToID("_BaseColor");
        Material climbMarkMaterial;
        MaterialPropertyBlock climbMarkBlock;
        float wallLogAt, wallLastAt, climbHomeY;
        int wallStage;
        float3 wallLast;

        void ClimbTest()
        {
            if (!climbTarget.HasValue || interactRequest != null || !EnsureLocations(out _) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            // (The camera stays turned to the climb, so the stick reads forward going out and back coming home.)
            PlayerInputSystem.TestCamera = math.normalizesafe(climbTarget.Value - climbHome, new float2(0f, 1f));
            // (A marked band, "-place climb": a slower human, on a wall up to 6 m: longer up and down.)
            var banded = climbWall && climbBandGrade > Climbing.None;
            var cycle = climbWall ? (banded ? 40f : 28f) : 20f;
            var phase = math.fmod(Time.time - climbSince, cycle);
            var lap = (int)((Time.time - climbSince) / cycle);
            var toward = phase < (climbWall ? (banded ? 16f : 10f) : 12f);
            // (A wall: aimed a metre past the rim, so the body keeps pushing into it rather than stopping short.)
            var goal = toward ? climbTarget.Value + (climbWall ? math.normalizesafe(climbTarget.Value - climbHome) : float2.zero) : climbHome;
            var to = goal - player.Position.xz;
            var distance = math.length(to);
            PlayerInputSystem.TestMove = distance > 0.3f ? to / distance : float2.zero;
            // Up a trunk: straight at the trunk it holds, or straight away from it to come down, rather than at the spot
            // logged for it. (The two can be a few tenths of a metre apart; with the feet on the bark that was push enough
            // to the side to walk the player round to the trunk's far side, where heading home climbed instead.)
            if (playerQuery.TryGetSingletonEntity<PlayerTag>(out var self) && queryWorld.EntityManager.GetComponentData<PlayerMotion>(self) is var motion && motion.OnTrunk)
            {
                PlayerInputSystem.TestMove = math.normalizesafe((PlayerMovementSystem.TrunkCentre(motion, player.Position.y) - player.Position.xz) * (toward ? 1f : -1f));
                // Each time it takes hold: where the trunk it holds stands, against the spot logged for it, and which side
                // of it the player is on (to settle which side it climbs).
                if (!climbHolding)
                    Debug.Log($"DebugControls: climb test: skill {ClimbSkillNow()}, took hold of a trunk at ({V(motion.TrunkBase.x)}, {V(motion.TrunkBase.z)}), radius {V(motion.TrunkRadius)}, " +
                              $"{V(math.distance(motion.TrunkBase.xz, climbTarget.Value))} m from the spot logged; player at ({V(player.Position.x)}, {V(player.Position.z)}), " +
                              $"on its {(math.abs(math.cos(motion.TrunkAngle)) > math.abs(math.sin(motion.TrunkAngle)) ? (math.cos(motion.TrunkAngle) > 0f ? "east" : "west") : (math.sin(motion.TrunkAngle) > 0f ? "north" : "south"))} side");
                climbHolding = true;
            }
            else
                climbHolding = false;
            // A wall: early in the climb, pushing right across it (the stick's right against the camera, which looks at
            // the wall), then back down. A log line a second: where on the wall, and which way it's pushed.
            if (climbWall && playerQuery.TryGetSingletonEntity<PlayerTag>(out var wallSelf))
            {
                var wm = queryWorld.EntityManager.GetComponentData<PlayerMotion>(wallSelf);
                var look = math.normalizesafe(PlayerInputSystem.TestCamera ?? new float2(0f, 1f), new float2(0f, 1f));
                // (Early in the climb, 1.5-3.5 s after -place wall: it reaches the top in about 4 s.)
                var sideways = phase >= 1.5f && phase < 3.5f && wm.OnWall;
                if (sideways)
                    PlayerInputSystem.TestMove = new float2(look.y, -look.x);
                // Up / top-out / step-down events, each logged once per cycle.
                if (phase < 0.5f)
                    wallStage = 0;
                var rise = player.Position.y - climbHomeY;
                if (wallStage == 0 && (wm.OnWall || wm.OnTrunk) && rise > 0.5f)
                {
                    wallStage = 1;
                    Debug.Log($"DebugControls: wall test: UP, on the {(wm.OnTrunk ? "round wall" : "wall")} {V(rise)} m above the start at {V(phase)} s");
                }
                if (wallStage <= 1 && !wm.OnWall && wm.Grounded && rise > 1f && toward)
                {
                    wallStage = 2;
                    Debug.Log($"DebugControls: wall test: TOP-OUT, standing on the top {V(rise)} m above the start at {V(phase)} s");
                }
                if (wallStage == 2 && !toward && wm.OnWall)
                {
                    wallStage = 3;
                    Debug.Log($"DebugControls: wall test: STEP-DOWN, over the edge and on the wall at {V(rise)} m at {V(phase)} s");
                }
                if (wallStage == 3 && !toward && !wm.OnWall && wm.Grounded && rise < 0.5f)
                {
                    wallStage = 4;
                    Debug.Log($"DebugControls: wall test: DOWN, back on the ground at {V(phase)} s");
                }
                // (A marked band, every other lap: partway down, jump lets go and it drops to the ground.)
                if (banded && (lap & 1) == 1 && wallStage == 3 && wm.OnWall && rise > 0.8f && rise < climbWallTop * 0.6f)
                {
                    wallStage = 5;
                    PlayerInputSystem.TestJump = true;
                    Debug.Log($"DebugControls: wall test: DROP, let go of the wall {V(rise)} m up at {V(phase)} s");
                }
                if (wallStage == 5 && !wm.OnWall && wm.Grounded)
                {
                    wallStage = 6;
                    Debug.Log($"DebugControls: wall test: DROP, landed {V(rise)} m above the start at {V(phase)} s");
                }
                if (Time.time >= wallLogAt)
                {
                    wallLogAt = Time.time + 1f;
                    var stick = sideways ? "right" : toward ? "up" : "down";
                    // (Rates since the last line: up and along the wall, m/s.)
                    var step = player.Position - wallLast;
                    var dtLog = math.max(Time.time - wallLastAt, 0.01f);
                    wallLast = player.Position;
                    wallLastAt = Time.time;
                    Debug.Log($"DebugControls: wall test: skill {ClimbSkillNow()}, test move {PlayerInputSystem.TestMove}, {V(distance)} m from the rim; stick {stick}, OnWall {wm.OnWall}, grounded {wm.Grounded}, player at ({V(player.Position.x)}, {V(player.Position.y)}, {V(player.Position.z)}), vertical {V(step.y / dtLog)} m/s, along {V(math.length(step.xz) / dtLog)} m/s");
                }
            }
            if (toward && climbJumps && distance > 0.5f && distance < 2f && Time.time >= climbNextJump)
            {
                PlayerInputSystem.TestJump = true;
                climbNextJump = Time.time + 0.6f;
            }
        }


        // ---------------------------------------------------------------- right face button checks (A-04)

        /// <summary>
        /// "-actiontest roll:Human,Dog": for each body in turn, 3.5 s: switch to it, jog ahead (speed logged), one roll
        /// with the stick pushed and one standing (distance and length logged; with -shotdir a shot mid-roll).
        /// "-actiontest fly:Pigeon": takes off, climbs, glides, then holds the right face button (steady descent) and the
        /// dive button, logging height and rate each second. "-actiontest fish:Trout" (after -place clearlake): in the
        /// lake, swims, sprints and sinks 5 s, 5 s and 3 s, logging speed and depth against the surface; the camera
        /// pitch is held at the usual downward look so a sprint that follows it would show.
        /// </summary>
        static string actionTest;
        float actionStart;
        int actionSlot;
        float2 actionDir;
        float3 actionMark;
        float actionMarkAt, actionPath, actionY0;
        bool rolling;
        float3 rollFrom;
        float rollAt;
        float actionLogAt;

        void SetBody(string name)
        {
            var avatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
            if (avatars == null)
                return;
            var index = avatars.PlayableSpecies.FindIndex(s => s != null && s.name == name);
            if (index < 0)
                Debug.LogWarning($"DebugControls: no playable body '{name}'");
            else
                avatars.SetLocalSpecies(index);
        }

        /// <summary>
        /// "-climbskill N" (A-101): the player's ClimbSkill held at N (-1 lets go), from the next body on and for the body
        /// it's in now, its climb speed with it (a skill 5 body scurries, Climbing.BaseSpeed). Letting go takes effect with
        /// the next body (FaunaDirector works the species' skill out from its blob).
        /// </summary>
        void SetClimbSkill(int skill)
        {
            Climbing.SkillOverride = math.clamp(skill, -1, Climbing.Most);
            if (Climbing.SkillOverride < 0 || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var bodyQuery = em.CreateEntityQuery(typeof(PlayerBody));
            if (!bodyQuery.TryGetSingletonEntity<PlayerBody>(out var bodyEntity))
                return;
            var body = em.GetComponentData<PlayerBody>(bodyEntity);
            var director = FindAnyObjectByType<FaunaDirector>();
            var species = director != null && director.PlayerSpecies != null ? director.PlayerSpecies.Body : null;
            body.ClimbSkill = (byte)Climbing.SkillOverride;
            body.ClimbSpeed = Climbing.BaseSpeed(body.ClimbSkill, species != null ? species.Climbing : 0f);
            em.SetComponentData(bodyEntity, body);
            Debug.Log($"DebugControls: climb skill {body.ClimbSkill}, climb speed {body.ClimbSpeed:0.00}");
        }

        void ActionTest()
        {
            if (actionTest == null || !EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var self))
                return;
            var em = queryWorld.EntityManager;
            var player = em.GetComponentData<LocalTransform>(self);
            var motion = em.GetComponentData<PlayerMotion>(self);
            var colon = actionTest.IndexOf(':');
            var kind = actionTest.Substring(0, colon);
            var bodies = actionTest.Substring(colon + 1).Split(',');
            var t = Time.time - actionStart;
            var pos = player.Position;
            if (kind == "roll")
            {
                var slot = (int)(t / 3.5f);
                var lt = t - slot * 3.5f;
                if (slot >= bodies.Length)
                {
                    actionTest = null;
                    PlayerInputSystem.TestMove = null;
                    return;
                }
                if (slot != actionSlot)
                {
                    actionSlot = slot;
                    SetBody(bodies[slot]);
                    rolling = false;
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                }
                // (Jogging from 0.6 s: speed over 0.8 to 1.2 s; the roll with the stick at 1.4 s; a standing one at 2.5 s.)
                PlayerInputSystem.TestMove = lt >= 0.6f && lt < 2.0f ? actionDir : (float2?)null;
                if (lt >= 0.8f && lt < 0.8f + Time.deltaTime)
                {
                    actionMark = pos;
                    actionMarkAt = Time.time;
                }
                if (lt >= 1.2f && lt < 1.2f + Time.deltaTime)
                    Debug.Log($"DebugControls: roll test {bodies[slot]}: jog {math.distance(pos.xz, actionMark.xz) / math.max(Time.time - actionMarkAt, 0.01f):F1} m/s");
                if (lt >= 1.4f && lt < 1.4f + Time.deltaTime || lt >= 2.5f && lt < 2.5f + Time.deltaTime)
                    PlayerInputSystem.TestRoll = true;
                if (shotDir != null && slot == 0 || shotDir != null && bodies[slot] == "Dog" || shotDir != null && bodies[slot] == "Lizard")
                    if (lt >= 1.4f + 0.2f && lt < 1.4f + 0.2f + Time.deltaTime)
                        Shot($"roll-{bodies[slot]}.png");
                if (motion.Roll > 0f && !rolling)
                {
                    rolling = true;
                    rollFrom = pos;
                    rollAt = Time.time;
                }
                else if (motion.Roll <= 0f && rolling)
                {
                    rolling = false;
                    Debug.Log($"DebugControls: roll test {bodies[slot]}: roll {Time.time - rollAt:F2} s, {math.distance(pos.xz, rollFrom.xz):F1} m {(PlayerInputSystem.TestMove.HasValue ? "with the stick" : "standing")}");
                }
                return;
            }
            PlayerInputSystem.TestMove = null;
            if (kind == "fly")
            {
                if (actionSlot < 0)
                {
                    actionSlot = 0;
                    SetBody(bodies[0]);
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                }
                // (Phases: 1.5-9 climbing, 9-12 gliding, 12-16 the right face button, 16-18 the dive button.)
                if (t >= 1.5f && t < 1.5f + Time.deltaTime)
                    PlayerInputSystem.TestJump = true;
                if (t >= 1.5f && t < 19f)
                    PlayerInputSystem.TestMove = actionDir;
                PlayerInputSystem.TestClimb = t >= 1.5f && t < 9f;
                PlayerInputSystem.TestCrouch = t >= 12f && t < 16f;
                PlayerInputSystem.TestSprint = t >= 16f && t < 18f;
                if (t >= actionLogAt && t > 1f)
                {
                    actionLogAt = t + 1f;
                    var ground = source.SampleHeight(pos.xz);
                    var phase = t < 9f ? "climb" : t < 12f ? "glide" : t < 16f ? "crouch" : t < 18f ? "dive" : "end";
                    Debug.Log($"DebugControls: fly test {bodies[0]} t {t:F0} {phase}: flying {motion.Flying}, {pos.y - ground:F1} m up, rate {(pos.y - actionMark.y) / 1f:F1} m/s, airspeed {motion.Airspeed:F1}, pitch {math.degrees(motion.Pitch):F0} deg");
                    actionMark = pos;
                }
                if (t > 19f)
                {
                    actionTest = null;
                    PlayerInputSystem.TestClimb = PlayerInputSystem.TestCrouch = PlayerInputSystem.TestSprint = false;
                    PlayerInputSystem.TestMove = null;
                }
                return;
            }
            if (kind == "fish")
            {
                if (actionSlot < 0)
                {
                    actionSlot = 0;
                    SetBody(bodies[0]);
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                    fishPhase = 0;
                }
                // Into the lake, 8 m out and 0.6 m under its surface, at 1.0 s.
                if (t >= 1f && t < 1f + Time.deltaTime)
                {
                    var at = lastLake ?? pos.xz + actionDir * 8f;
                    var g = source.SampleGround(at);
                    player.Position = new float3(at.x, g.WaterSurface - 0.6f, at.y);
                    em.SetComponentData(self, player);
                    Debug.Log($"DebugControls: fish test: in the lake at ({at.x:F0}, {at.y:F0}), {g.WaterSurface - g.Height:F1} m deep");
                    actionMark = player.Position;
                    actionPath = 0f;
                    actionMarkAt = 2f;
                    actionY0 = g.WaterSurface;
                }
                var phase = t < 2f ? 0 : t < 7f ? 1 : t < 12f ? 2 : t < 15f ? 3 : 4;
                // (A gentle nose-down while swimming, so it ends mid-depth; the sprint then has the same look-down and must hold.)
                PlayerInputSystem.TestPitch = phase == 1 ? -0.08f : -0.25f;
                // (Back and forth every 1.2 s, so it stays in the water; speed is the path covered.)
                var flip = (int)(t / 1.2f) % 2 == 0 ? 1f : -1f;
                PlayerInputSystem.TestMove = phase == 1 || phase == 2 ? actionDir * flip : (float2?)null;
                PlayerInputSystem.TestSprint = phase == 2;
                PlayerInputSystem.TestCrouch = phase == 3;
                if (phase >= 1 && t > 1.1f)
                {
                    actionPath += math.distance(pos.xz, actionMark.xz);
                    actionMark = pos;
                }
                // At each phase's end: its speed, its depth under the surface, and how that moved.
                var ends = new[] { 2f, 7f, 12f, 15f };
                if (phase > fishPhase)
                {
                    var done = fishPhase;
                    fishPhase = phase;
                    if (done < 1)
                        goto next;
                    var names = new[] { "", "swim", "sprint", "crouch" };
                    var len = ends[done] - ends[done - 1];
                    Debug.Log($"DebugControls: fish test {names[done]}: path speed {actionPath / math.max(len, 0.01f):F1} m/s over {len:F0} s, depth now {actionY0 - pos.y:F2} m under the surface (was {actionY0 - actionMarkY:F2}), camera pitch held at -14 deg");
                    actionPath = 0f;
                    actionMarkY = pos.y;
                }
                next:
                if (t < 2f)
                    actionMarkY = actionY0 - 0.6f;
                if (t > 16f)
                {
                    actionTest = null;
                    PlayerInputSystem.TestSprint = PlayerInputSystem.TestCrouch = false;
                    PlayerInputSystem.TestMove = null;
                    PlayerInputSystem.TestPitch = null;
                }
            }
        }
        float actionMarkY;
        static float2? lastLake;
        int fishPhase;

        static bool IsTree(ref FloraBlob flora, in TerrainSource source, int2 cell)
        {
            var plant = FloraPlacement.Place(ref flora, source, cell, FloraPlacement.TreeLayer);
            return plant.Model >= 0 && plant.Model < flora.Models.Length && flora.Models[plant.Model].Kind != TreeKind.None;
        }

        /// <summary>Back to a clean scene for the next shot: no specimen, spawns or watched animal; the camera free.</summary>
        void ClearShot()
        {
            if (cameraPosed && Camera.main != null)
                foreach (var b in Camera.main.GetComponents<MonoBehaviour>())
                    b.enabled = true;
            cameraPosed = false;
            // (The wall shot's narrower view, put back.)
            if (wallFovWas > 0f && Camera.main != null)
                Camera.main.fieldOfView = wallFovWas;
            wallFovWas = 0f;
            foreach (var mark in climbMarks)
                if (mark != null)
                    Destroy(mark);
            climbMarks.Clear();
            ClimbBands.Clear();
            huntShotTaken = false;
            huntShotLogged = false;
            staleHunter = huntHunter;
            huntHunter = Entity.Null;
            colossusShotDistance = 0f;
            mantrapShotMode = null;
            mimicShotMode = null;
            mimicT0 = -1f;
            mimicStartT = mimicEndT = mimicShotT = -1f;
            mimicMaxSpeed = 0f;
            mimicFolded = mimicSettled = mimicRunMarked = false;
            mimicEntity = Entity.Null;
            Mantrap.HoldStrike = false;
            mantrapLungePending = false;
            Mantrap.ThinFog = false;
            if (posed != Entity.Null && queryWorld != null && queryWorld.IsCreated && queryWorld.EntityManager.Exists(posed))
                queryWorld.EntityManager.DestroyEntity(posed);
            posed = Entity.Null;
            liftHeight = 0f;
            actionTest = null;
            climbTarget = null;
            wallRequest = false;
            PlayerInputSystem.TestMove = null;
            PlayerInputSystem.TestSprint = false;
            PlayerInputSystem.TestCrouch = false;
            PlayerInputSystem.TestClimb = false;
            PlayerInputSystem.TestPitch = null;
            PlayerInputSystem.TestCamera = null;
            walkTarget = null;
            walkPath.Clear();
            untilWork = 0f;
            untilAction = 0f;
            workSince = -1f;
            swimSpot = null;
            poseRequest = null;
            watchSpecies = null;
            watchPitchOverride = null;
            watchFrom = null;
            interactRequest = null;
            interactStep = -1f;
            pendingSpawns.Clear();
            watched = Entity.Null;
            ThirdPersonCamera.WatchTarget = null;
            ThirdPersonCamera.WatchSpin = 12f;
        }

        /// <summary>
        /// A test's own screenshot into -shotdir, at least 1280 wide (a small Game view is rendered larger). It captures the
        /// screen with its overlay; a plain world shot goes through <see cref="ShotTool"/> (docs/agents/testing.md).
        /// </summary>
        static void Shot(string name) =>
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, name), Mathf.Max(1, Mathf.CeilToInt(1280f / Mathf.Max(Screen.width, 1))));
    }
}
