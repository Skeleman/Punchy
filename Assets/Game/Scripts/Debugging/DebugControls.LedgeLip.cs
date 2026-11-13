using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, the ledge lip test (A-101 row 1b, #267): "-place ledge:lip[:Body]" builds a stone L on flat ground
    // near the player with a lip round its outer and inner corners, and drives the player to hang from it, shimmy both
    // ways round both corners, step onto a marked face at the lip's open end and back, drop, and pull up onto the top.
    // A joined client builds the same piece from "-ledgepiece" (logged by the host) and watches.
    public partial class DebugControls
    {
        /// <summary>"-place ledge:lip": build the piece (once the ground has settled round the player) and run the test.</summary>
        bool lipRequest;
        /// <summary>"-ledgepiece": the piece to build as the host logged it (a joined client), and its built frame.</summary>
        float4? lipPiecePending;
        float lipPieceHeight;
        byte lipPieceGrade;
        /// <summary>The piece's frame: its corner, its local x and z (xz), the ground there, the lip height over it.</summary>
        float3 lipOrigin;
        float2 lipAx, lipAz;
        float lipTop;
        /// <summary>The test's stage, when it began, its tries, and the last hold seen (to log each change once).</summary>
        int lipStage = -1;
        float lipStageAt, lipLogAt;
        int lipTries, lipLastHang, lipCorners;
        float3 lipLastPos;
        float lipStillFor;
        bool lipClimber;

        /// <summary>The lip test's flags (ApplyOptions): "-place ledge:lip[:Body]" and "-ledgepiece x,y,z,yaw,height,grade"
        /// ("-lipwait" holds the test for a joined player).</summary>
        void LedgeLipOptions(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-place" && args[i + 1].StartsWith("ledge:lip", System.StringComparison.OrdinalIgnoreCase))
                {
                    var parts = args[i + 1].Split(':');
                    if (parts.Length > 2)
                        SetBody(parts[2]);
                    lipRequest = true;
                    lipStage = -1;
                }
                if (args[i] == "-ledgepiece" && ParseFloats(args[i + 1]) is float[] p && p.Length >= 6)
                {
                    lipPiecePending = new float4(p[0], p[1], p[2], p[3]);
                    lipPieceHeight = p[4];
                    lipPieceGrade = (byte)math.clamp((int)p[5], (int)Climbing.Ladder, (int)Climbing.Most);
                }
            }
        }

        /// <summary>A point of the piece, local x and z (m) from its corner, at height y over the ground there.</summary>
        float3 LipAt(float x, float z, float y) => new(lipOrigin.x + lipAx.x * x + lipAz.x * z, lipOrigin.y + y, lipOrigin.z + lipAx.y * x + lipAz.y * z);
        float2 LipDir(float x, float z) => lipAx * x + lipAz * z;

        /// <summary>
        /// Builds the piece at origin (its corner, on the ground), turned yaw (radians), the lip height over the ground and
        /// its grade: two solid boxes in an L (a 6x2 m arm and a 2x6 m arm, local x 0..6 z 0..2 and x 0..2 z 0..6), a lip
        /// along the L's inside and round its outer corner (three lips: along z=2 from x=5 to the inner corner at (2,2),
        /// along x=2 to the outer corner at (2,6), on along z=6 to x=0.6), and a marked band on the z=2 face past the
        /// lip's open end (x 5..6) to step onto and back. Boxes in the client world (as the dungeons' props), shown with
        /// plain cubes.
        /// </summary>
        void BuildLipPiece(float3 origin, float yaw, float height, byte grade)
        {
            lipOrigin = origin;
            lipAx = new float2(math.cos(yaw), math.sin(yaw));
            lipAz = new float2(-math.sin(yaw), math.cos(yaw));
            lipTop = height;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<DungeonPropBox>());
            var boxEntity = q.CalculateEntityCount() > 0 ? q.GetSingletonEntity() : Entity.Null;
            if (boxEntity == Entity.Null)
            {
                // (TODO-free test only: a dungeon's own boxes made later would be a second list.)
                boxEntity = em.CreateEntity(typeof(DungeonPropBox));
                em.SetName(boxEntity, "Dungeon prop boxes");
            }
            var rot = quaternion.LookRotation(new float3(lipAz.x, 0f, lipAz.y), math.up());
            if (climbMarkMaterial == null)
                climbMarkMaterial = Resources.Load<Material>("ItemMaterial");
            climbMarkBlock ??= new MaterialPropertyBlock();
            void Solid(float x0, float x1, float z0, float z1, float y0, float y1, Color colour, bool solid)
            {
                var centre = LipAt((x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (y0 + y1) * 0.5f);
                var half = new float3((x1 - x0) * 0.5f, (y1 - y0) * 0.5f, (z1 - z0) * 0.5f);
                if (solid)
                    em.GetBuffer<DungeonPropBox>(boxEntity).Add(new DungeonPropBox { Box = new KitBox { Centre = centre, Half = half, Rotation = rot, Reach = math.length(half) } });
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "LedgeLipMark";
                go.transform.SetPositionAndRotation(centre, rot);
                go.transform.localScale = half * 2f;
                var r = go.GetComponent<MeshRenderer>();
                if (climbMarkMaterial != null)
                    r.sharedMaterial = climbMarkMaterial;
                climbMarkBlock.SetColor(ClimbMarkColorId, colour);
                r.SetPropertyBlock(climbMarkBlock);
                climbMarks.Add(go);
            }
            var stone = new Color(0.55f, 0.53f, 0.5f);
            Solid(0f, 6f, 0f, 2f, -1f, height, stone, true);
            Solid(0f, 2f, 2f, 6f, -1f, height, stone * 0.95f, true);
            // The lip: a cut stone course standing a little proud of the faces, darker.
            var course = new Color(0.36f, 0.33f, 0.3f);
            Solid(2f, 5f, 2f, 2.08f, height - 0.2f, height, course, false);
            Solid(2f, 2.08f, 2f, 6.08f, height - 0.2f, height, course, false);
            Solid(0.6f, 2.08f, 6f, 6.08f, height - 0.2f, height, course, false);
            var z = LipDir(0f, 1f);
            var x = LipDir(1f, 0f);
            ClimbLedges.Add(new ClimbLedge { From = LipAt(5f, 2f, height), To = LipAt(2f, 2f, height), Out = z, Depth = 2f, Grade = grade });
            ClimbLedges.Add(new ClimbLedge { From = LipAt(2f, 2f, height), To = LipAt(2f, 6f, height), Out = x, Depth = 2f, Grade = grade });
            ClimbLedges.Add(new ClimbLedge { From = LipAt(2f, 6f, height), To = LipAt(0.6f, 6f, height), Out = z, Depth = 2f, Grade = grade });
            var band = new ClimbBand { Base = LipAt(5.5f, 2f, 0f), Out = z, HalfWidth = 0.5f, Height = height, Grade = grade };
            ClimbBands.Add(band);
            MarkClimbBand(band);
        }

        /// <summary>
        /// The lip test, a frame: builds the piece (after the settle, on the flattest dry 10 m spot near the player; or the
        /// logged one on a client), then drives the stick through its stages, logging each ("lip test: ...").
        /// </summary>
        void LedgeLipTest()
        {
            if (queryWorld == null || !queryWorld.IsCreated)
                return;
            if (lipPiecePending.HasValue && Time.timeSinceLevelLoad - optionsAt > settle)
            {
                var p = lipPiecePending.Value;
                lipPiecePending = null;
                BuildLipPiece(p.xyz, p.w, lipPieceHeight, lipPieceGrade);
                Debug.Log($"DebugControls: lip piece built for watching at ({V(p.x)}, {V(p.y)}, {V(p.z)})");
            }
            if (!lipRequest || Time.timeSinceLevelLoad - optionsAt < settle + 3f || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingletonEntity<PlayerTag>(out var self))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(self);
            var m = em.GetComponentData<PlayerMotion>(self);
            using var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            if (!bodyQuery.TryGetSingleton<PlayerBody>(out var body))
                return;   // (the body is set a moment after the player)
            var size = body.Size > 0f ? body.Size : 1f;
            if (lipStage < 0 && System.Array.IndexOf(LaunchArgs, "-lipwait") >= 0)
            {
                // ("-lipwait", the host + client check: the test holds until a joined player is in, up to a minute and a half.)
                using var others = em.CreateEntityQuery(ComponentType.ReadOnly<RemoteAvatar>());
                if (others.IsEmpty && Time.timeSinceLevelLoad - optionsAt < settle + 90f)
                    return;
            }
            if (lipStage < 0)
            {
                if (!FindLipSpot(source, t.Position.xz, out var origin))
                {
                    Debug.LogWarning("DebugControls: lip test: FAIL, no flat dry spot near the player");
                    lipRequest = false;
                    return;
                }
                var yaw = 0f;
                // (A person-high lip for its size; a full 3.3 m wall for a wall climber, whatever its size.)
                var height = Climbing.CanClimb(body.ClimbSkill, Climbing.Smooth) ? 3.3f : 3.3f * size;
                BuildLipPiece(origin, yaw, height, Climbing.Assisted);
                static string F(float v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                var eye = LipAt(9f, 9.5f, 3.5f);
                var look = LipAt(2.5f, 2.5f, 2f);
                Debug.Log($"DebugControls: lip test: piece at ({V(origin.x)}, {V(origin.y)}, {V(origin.z)}), lip {V(height)} m up, skill {body.ClimbSkill}; " +
                          $"for a client: -ledgepiece {F(origin.x)},{F(origin.y)},{F(origin.z)},{F(yaw)},{F(height)},{Climbing.Assisted}; " +
                          $"to watch: -goto {V(eye.x)},{V(eye.z)} -camlook {V(eye.x)},{V(eye.y)},{V(eye.z)},{V(look.x)},{V(look.y)},{V(look.z)}");
                lipClimber = Climbing.CanClimb(body.ClimbSkill, Climbing.Smooth);
                lipTries = 0;
                LipStage(0);
                LipStart(self, em);
                return;
            }
            var stageTime = Time.time - lipStageAt;
            var ledges = ClimbLedges.List.Data;
            // (The camera faces the lip held, or the first lip's wall: the stick's right is along the lip.)
            var holding = m.Hang > 0 && m.Hang <= ledges.Length ? ledges[m.Hang - 1] : ledges.Length > 0 ? ledges[0] : default;
            var into = -holding.Out;
            PlayerInputSystem.TestCamera = into;
            var right = new float2(into.y, -into.x);
            var moved = math.distance(t.Position, lipLastPos);
            lipStillFor = moved < 0.002f ? lipStillFor + Time.deltaTime : 0f;
            lipLastPos = t.Position;
            if (m.Hang != lipLastHang)
            {
                if (lipLastHang > 0 && m.Hang > 0)
                {
                    var a = ledges[lipLastHang - 1];
                    var b = ledges[m.Hang - 1];
                    var shared = math.distance(a.To.xz, b.From.xz) < ClimbLedges.Join || math.distance(a.To.xz, b.To.xz) < ClimbLedges.Join ? a.To.xz : a.From.xz;
                    var far = math.distance(b.From.xz, shared) < ClimbLedges.Join ? b.To.xz : b.From.xz;
                    var inner = math.dot(a.Out, math.normalizesafe(far - shared)) > 0f;
                    lipCorners++;
                    Debug.Log($"DebugControls: lip test: SHIMMY onto lip {m.Hang - 1} round the {(inner ? "inner" : "outer")} corner at {V(stageTime)} s");
                }
                lipLastHang = m.Hang;
            }
            if (Time.time > lipLogAt)
            {
                lipLogAt = Time.time + 1f;
                Debug.Log($"DebugControls: lip test: stage {lipStage}, at ({V(t.Position.x)}, {V(t.Position.y - lipOrigin.y)} up, {V(t.Position.z)}), hang {m.Hang}, " +
                          $"wall {m.OnWall}, grounded {m.Grounded}, mantle {V(m.Mantle)}");
            }
            if (lipClimber)
            {
                ClimberStage(t, m, stageTime, right);
                return;
            }
            switch (lipStage)
            {
                case 0:   // Pushing in at the first lip's wall and jumping, till it hangs.
                    PlayerInputSystem.TestMove = into;
                    if (stageTime > 0.5f && stageTime < 0.6f && m.Grounded)
                        PlayerInputSystem.TestJump = true;
                    if (m.Hang > 0)
                    {
                        Debug.Log($"DebugControls: lip test: HANG from lip {m.Hang - 1}, hands {V(t.Position.y + HandsUp(size) - lipOrigin.y)} m up, try {lipTries + 1}");
                        LipStage(1);
                    }
                    else if (stageTime > 2.5f)
                    {
                        if (++lipTries >= 3)
                            LipFail("no hold after 3 jumps");
                        else
                        {
                            LipStart(self, em);
                            LipStage(0);
                        }
                    }
                    break;
                case 1:   // Shimmying right: in round the inner corner, out round the outer one, to the open end.
                    PlayerInputSystem.TestMove = right;
                    if (m.Hang == 0)
                        LipFail("let go shimmying right");
                    else if (stageTime > 1f && lipStillFor > 0.5f)
                    {
                        Debug.Log($"DebugControls: lip test: END of lip {m.Hang - 1}, {lipCorners} corners, at {V(stageTime)} s");
                        LipStage(2);
                    }
                    else if (stageTime > 30f)
                        LipFail("never reached the end shimmying right");
                    break;
                case 2:   // Back left, round both corners, off the first lip's open end onto the marked face.
                    PlayerInputSystem.TestMove = -right;
                    if (m.Hang == 0 && m.OnWall)
                    {
                        Debug.Log($"DebugControls: lip test: ONTO FACE (the marked band) at {V(stageTime)} s, {lipCorners} corners in all");
                        LipStage(3);
                    }
                    else if (m.Hang == 0)
                        LipFail("let go shimmying left");
                    else if (stageTime > 30f)
                        LipFail("never reached the face shimmying left");
                    break;
                case 3:   // A moment on the face, then right again back onto the lip.
                    PlayerInputSystem.TestMove = stageTime < 1f ? float2.zero : right;
                    if (m.Hang > 0)
                    {
                        Debug.Log($"DebugControls: lip test: ONTO LEDGE {m.Hang - 1} off the face at {V(stageTime)} s");
                        LipStage(4);
                    }
                    else if (!m.OnWall)
                        LipFail("came off the face");
                    else if (stageTime > 8f)
                        LipFail("never took the lip back off the face");
                    break;
                case 4:   // Along a little, then down: letting go.
                    PlayerInputSystem.TestMove = stageTime < 0.6f ? right : -into;
                    if (stageTime > 0.6f && m.Hang == 0 && !m.OnWall)
                    {
                        Debug.Log($"DebugControls: lip test: DROP at {V(t.Position.y - lipOrigin.y)} m up");
                        LipStage(5);
                    }
                    else if (stageTime > 6f)
                        LipFail("never let go");
                    break;
                case 5:   // Falling to the ground, then jumping back up to the lip.
                    PlayerInputSystem.TestMove = float2.zero;
                    if (m.Grounded && stageTime < 5f && lipLastHang == 0 && stageTime > 0.1f && lipTries >= 0)
                    {
                        Debug.Log($"DebugControls: lip test: landed {V(t.Position.y - lipOrigin.y)} m up at {V(stageTime)} s");
                        lipTries = -1;
                    }
                    if (lipTries < 0 && stageTime > 1.2f)
                    {
                        PlayerInputSystem.TestMove = into;
                        if (m.Grounded && stageTime < 1.4f)
                            PlayerInputSystem.TestJump = true;
                        if (m.Hang > 0)
                        {
                            Debug.Log($"DebugControls: lip test: HANG again from lip {m.Hang - 1}");
                            LipStage(6);
                        }
                    }
                    if (stageTime > 6f)
                        LipFail("didn't land and hang again");
                    break;
                case 6:   // Up: pulling onto the top.
                    PlayerInputSystem.TestMove = stageTime < 0.5f ? float2.zero : into;
                    if (m.Mantle > 0f && lipTries < 0)
                    {
                        Debug.Log("DebugControls: lip test: PULL-UP");
                        lipTries = 0;
                    }
                    if (lipTries == 0 && m.Mantle <= 0f && m.Grounded && t.Position.y - lipOrigin.y > lipTop - 0.3f)
                    {
                        Debug.Log($"DebugControls: lip test: ON TOP, standing {V(t.Position.y - lipOrigin.y)} m up");
                        Debug.Log("DebugControls: lip test: PASS");
                        LipStage(7);
                    }
                    else if (stageTime > 6f)
                        LipFail("never stood on the top");
                    break;
                case 7:   // Done: standing still on the top.
                    PlayerInputSystem.TestMove = float2.zero;
                    if (stageTime > 2f)
                        LipEnd();
                    break;
            }
        }

        /// <summary>A wall climber at the piece: the wall climb as it was, never a ledge hold. It latches onto the wall, climbs
        /// it and tops out over the rim, walks back off the edge onto the wall (the step-down), and climbs down to the ground.
        /// (The wall climb doesn't follow a face round a side corner: pushed along, it stops in the L's inner one.)</summary>
        void ClimberStage(LocalTransform t, PlayerMotion m, float stageTime, float2 right)
        {
            var into = -ClimbLedges.List.Data[0].Out;
            PlayerInputSystem.TestCamera = into;
            var up = t.Position.y - lipOrigin.y;
            if (m.Hang > 0)
            {
                LipFail("a wall climber took a ledge hold");
                return;
            }
            switch (lipStage)
            {
                case 0:   // Into the wall: latching on.
                    PlayerInputSystem.TestMove = into;
                    if (m.OnWall)
                    {
                        Debug.Log($"DebugControls: lip test: WALL CLIMBER latched onto the wall (no hang) at {V(stageTime)} s");
                        LipStage(1);
                    }
                    else if (stageTime > 6f)
                        LipFail("a wall climber never latched");
                    break;
                case 1:   // Up (still pushing in): over the rim onto the top.
                    PlayerInputSystem.TestMove = into;
                    if (m.Grounded && up > lipTop - 0.3f)
                    {
                        Debug.Log($"DebugControls: lip test: WALL CLIMBER TOP-OUT, standing {V(up)} m up at {V(stageTime)} s");
                        LipStage(2);
                    }
                    else if (stageTime > 12f)
                        LipFail("a wall climber never topped out");
                    break;
                case 2:   // Back off the edge it came up: over onto the wall again.
                    PlayerInputSystem.TestMove = -into;
                    if (m.OnWall)
                    {
                        Debug.Log($"DebugControls: lip test: WALL CLIMBER STEP-DOWN onto the wall {V(up)} m up at {V(stageTime)} s");
                        LipStage(3);
                    }
                    else if (stageTime > 6f)
                        LipFail("a wall climber never stepped down");
                    break;
                case 3:   // Down the wall (the camera still facing it: back is down) to the ground.
                    PlayerInputSystem.TestMove = -into;
                    if (m.Grounded && up < 0.5f)
                    {
                        Debug.Log($"DebugControls: lip test: WALL CLIMBER DOWN at {V(stageTime)} s, never hanging; PASS");
                        LipEnd();
                    }
                    else if (stageTime > 15f)
                        LipFail("a wall climber never climbed down");
                    break;
            }
        }

        static float HandsUp(float size) => LedgeHang.HandReach * size;

        void LipStage(int stage)
        {
            lipStage = stage;
            lipStageAt = Time.time;
        }

        void LipFail(string why)
        {
            Debug.LogWarning($"DebugControls: lip test: FAIL at stage {lipStage}: {why}");
            LipEnd();
        }

        void LipEnd()
        {
            lipRequest = false;
            PlayerInputSystem.TestMove = null;
            PlayerInputSystem.TestCamera = null;
        }

        /// <summary>The player put down 1.2 m out from the first lip's wall, a metre along from the face band, facing it.</summary>
        void LipStart(Entity self, EntityManager em)
        {
            var at = LipAt(3.5f, 3.2f, 0f);
            var t = em.GetComponentData<LocalTransform>(self);
            t.Position = at;
            t.Rotation = quaternion.LookRotationSafe(new float3(-lipAz.x, 0f, -lipAz.y), math.up());
            em.SetComponentData(self, t);
            em.SetComponentData(self, new PlayerMotion { Grounded = true });
            lipLastHang = 0;
            lipCorners = 0;
        }

        /// <summary>The corner of a flat dry 10 m square near xz (within 60 m), on the ground: heights within 0.35 m, no water,
        /// no cave mouth.</summary>
        static bool FindLipSpot(in TerrainSource source, float2 xz, out float3 origin)
        {
            origin = default;
            for (int ring = 0; ring <= 6; ring++)
            {
                var steps = math.max(1, ring * 8);
                for (int k = 0; k < steps; k++)
                {
                    var angle = k * 2f * math.PI / steps;
                    var corner = xz + new float2(math.cos(angle), math.sin(angle)) * ring * 10f - 5f;
                    float lo = float.MaxValue, hi = float.MinValue;
                    var ok = true;
                    for (int i = 0; i < 5 && ok; i++)
                        for (int j = 0; j < 5 && ok; j++)
                        {
                            var p = corner + new float2(i, j) * 2.5f - 0.5f;
                            var g = source.SampleGround(p);
                            ok = g.WaterDepth <= 0f && !source.IsCaveHole(p);
                            lo = math.min(lo, g.Height);
                            hi = math.max(hi, g.Height);
                        }
                    if (ok && hi - lo < 0.35f)
                    {
                        origin = new float3(corner.x, source.SampleHeight(corner + 3f), corner.y);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
