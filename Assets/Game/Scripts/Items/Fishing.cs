using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Items
{
    /// <summary>Client → host → everyone (P-33): a player's fishing line. Kind 1: cast to Target (Water 1: onto water, its
    /// surface at Target.y; 0: onto the ground, short of where it was aimed); Kind 2: reeled in. From a client, Player is
    /// ignored (the host knows who sent it).</summary>
    public struct FishRpc : IRpcCommand
    {
        public int Player;
        public byte Kind, Water;
        public float3 Target;
    }

    /// <summary>
    /// Fishing, the player's side (P-33 row 1, #266): a rod (ItemDef with CreatureWeapon.Rod) in the hand is cast, not
    /// swung. Hold the attack button (left mouse while it steers the camera, X on a pad) to aim: the longer, the further,
    /// up to the rod's Reach after ChargeTime; release to cast. The bobber flies in an arc the way the body faces and lands
    /// only on water the ground query knows (sea, lake, river, pool, cave pool: TerrainSource.SampleGround(float3)), within
    /// a few metres of the player's height; aimed at ground, it drops short and the line reels itself in. On water it
    /// floats with the swell; press again to reel in. The line is drawn slack from the rod's tip (CreatureRenderSystem.
    /// RodTip, where the rod is drawn) to the bobber. The cast is the rod's flick (the weapon swing). A client tells the
    /// host and the host tells everyone (FishRpc), so others see the cast and the bobber; a player who joins later doesn't
    /// see a line already out until it is cast again.
    /// Test flags: "-cast 0.8;0.4" casts with those charges (0-1), the first 25 s after the body stands and then every
    /// 12 s, each reeled in 8 s after it was cast; "-castwater" turns the body first toward the nearest water in reach.
    /// </summary>
    public partial class Items
    {
        /// <summary>A line out: where its bobber is headed and the bed under it, whether onto water, and when it was cast
        /// and reeled in (Time.time; Reeled below 0 while it's still out).</summary>
        struct FishLine
        {
            public float3 Target;
            public float Bed, Cast, Reeled;
            public bool Water;
        }

        /// <summary>The shortest cast (m), how long the button is held for the longest (s), and the reel's length (s).</summary>
        const float MinCast = 4f, ChargeTime = 1.2f, ReelTime = 0.8f;
        /// <summary>How far below (and above) the player's feet water still counts for a cast (m): a pond down a bank, not a
        /// lake far below a cliff or one over a cave's roof.</summary>
        const float CastDrop = 12f, CastRise = 3f;
        /// <summary>A short cast onto ground lies there this long before reeling itself in (s).</summary>
        const float GroundLie = 0.6f;
        /// <summary>Under this many pixels across (the body), no line or bobber is drawn: the rod's own cut (rule 20).</summary>
        const float LinePixels = 6f;
        const int LineSegments = 12, LineSides = 4;

        FishLine ownLine;
        bool ownOut, ownLanded;
        float aimFrom = -1f;
        /// <summary>Other players' lines, by network id (the host's word).</summary>
        readonly Dictionary<int, FishLine> linesOf = new Dictionary<int, FishLine>();
        readonly Dictionary<int, Mesh> lineMeshes = new Dictionary<int, Mesh>();
        readonly Vector3[] lineVerts = new Vector3[(LineSegments + 1) * LineSides];
        readonly Vector3[] lineNormals = new Vector3[(LineSegments + 1) * LineSides];
        int[] lineTris;
        MaterialPropertyBlock lineBlock, bobberRed, bobberWhite;
        readonly List<int> lineDone = new List<int>();
        float[] castArg;
        int castDone;
        float castNext = -1f;
        bool castWaterArg;

        void FishArgs(string[] args)
        {
            var at = System.Array.IndexOf(args, "-cast");
            if (at >= 0 && at + 1 < args.Length)
            {
                var parts = args[at + 1].Split(';');
                castArg = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out castArg[i]);
            }
            castWaterArg = System.Array.IndexOf(args, "-castwater") >= 0;
        }

        bool HoldingRod()
        {
            var hand = worn[(int)GearSlot.Hand];
            return hand != Nothing && ItemTable.Items[hand].Weapon == CreatureWeapon.Rod && CanWearGear();
        }

        /// <summary>Outside the inventory and menus, with a rod in hand: hold to aim, release to cast, press to reel in.</summary>
        void FishKeys(in TerrainSource terrain)
        {
            if (!HoldingRod())
            {
                // (The rod put away: its line goes with it.)
                if (ownOut)
                    Reel(true);
                aimFrom = -1f;
                return;
            }
            FishTest(terrain);
            var flight = TryPlayer(out var me) ? FlightTime(math.distance(me.Position.xz, ownLine.Target.xz)) : 1f;
            // (A short cast onto ground reels itself in once it has dropped.)
            if (ownOut && !ownLine.Water && ownLine.Reeled < 0f && Time.time > ownLine.Cast + flight + GroundLie)
                Reel(false);
            if (ownOut && ownLine.Reeled >= 0f && Time.time > ownLine.Reeled + ReelTime)
                ownOut = false;
            if (ownOut && !ownLanded && ownLine.Reeled < 0f && Time.time > ownLine.Cast + flight)
            {
                ownLanded = true;
                Debug.Log($"[fish] landed {(ownLine.Water ? "on water" : "on the ground")} at ({ownLine.Target.x:F1}, {ownLine.Target.y:F2}, {ownLine.Target.z:F1}), " +
                          $"{math.distance(me.Position.xz, ownLine.Target.xz):0.0} m out");
            }
            if (panel || Punchy.UI.Overlay.AnyOpen)
            {
                aimFrom = -1f;
                return;
            }
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            var mouseOn = mouse != null && Cursor.lockState == CursorLockMode.Locked;
            var pressed = mouseOn && mouse.leftButton.wasPressedThisFrame || pad != null && pad.buttonWest.wasPressedThisFrame;
            var released = mouseOn && mouse.leftButton.wasReleasedThisFrame || pad != null && pad.buttonWest.wasReleasedThisFrame;
            if (pressed)
            {
                if (ownOut && ownLine.Reeled < 0f)
                    Reel(false);
                else if (!ownOut)
                    aimFrom = Time.time;
            }
            if (aimFrom >= 0f)
            {
                var charge = math.saturate((Time.time - aimFrom) / ChargeTime);
                prompt = "Cast " + new string('|', 1 + (int)(charge * 9f)) + new string('.', 9 - (int)(charge * 9f));
                if (released)
                {
                    aimFrom = -1f;
                    Cast(terrain, charge);
                }
            }
        }

        /// <summary>"-cast": the test casts, each turned toward water first with "-castwater", and reeled in 8 s on.</summary>
        void FishTest(in TerrainSource terrain)
        {
            if (castArg == null || castDone >= castArg.Length || !TryPlayer(out _))
                return;
            if (castNext < 0f)
                castNext = Time.time + 25f;
            if (ownOut && ownLine.Reeled < 0f && Time.time > ownLine.Cast + 8f)
                Reel(false);
            if (Time.time < castNext || ownOut)
                return;
            var charge = math.saturate(castArg[castDone++]);
            if (castWaterArg)
                FaceWater(terrain, math.lerp(MinCast, ItemTable.Items[worn[(int)GearSlot.Hand]].Reach, charge));
            Cast(terrain, charge);
            castNext = Time.time + 12f;
        }

        /// <summary>Testing: turns the player toward the nearest water this far off (every 10 degrees round), if any.</summary>
        void FaceWater(in TerrainSource terrain, float distance)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var q = Q(em, ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<PlayerTag>(out var player))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            var start = math.atan2(math.mul(t.Rotation, new float3(0f, 0f, 1f)).x, math.mul(t.Rotation, new float3(0f, 0f, 1f)).z);
            for (int k = 0; k < 36; k++)
            {
                // (Nearest to the way it faces first: 0, +10, -10, +20 ...)
                var yaw = start + math.radians(10f * ((k + 1) / 2) * (k % 2 == 0 ? 1f : -1f));
                if (!WaterAt(terrain, t.Position, new float2(math.sin(yaw), math.cos(yaw)), distance, out _))
                    continue;
                t.Rotation = quaternion.RotateY(yaw);
                em.SetComponentData(player, t);
                Debug.Log($"[fish] test: turned {math.degrees(yaw - start):0} deg (to yaw {math.degrees(yaw):0}) toward water {distance:0.0} m off");
                return;
            }
            Debug.Log($"[fish] test: no water {distance:0.0} m off in any direction");
        }

        /// <summary>The water a cast from here this far this way lands on, if any (the ground query's, 3D: cave water too).</summary>
        static bool WaterAt(in TerrainSource terrain, float3 from, float2 dir, float distance, out GroundSample g)
        {
            var xz = from.xz + dir * distance;
            g = terrain.SampleGround(new float3(xz.x, from.y + 0.5f, xz.y));
            return g.HasWater && g.WaterSurface > g.Height + 0.05f && g.WaterSurface > from.y - CastDrop && g.WaterSurface < from.y + CastRise;
        }

        /// <summary>How long a cast this far out (m) is in the air (s).</summary>
        static float FlightTime(float distance) => 0.5f + 0.03f * distance;

        void Cast(in TerrainSource terrain, float charge)
        {
            if (!TryPlayer(out var me))
                return;
            var forward = math.mul(me.Rotation, new float3(0f, 0f, 1f));
            var dir = math.normalizesafe(forward.xz, new float2(0f, 1f));
            var distance = math.lerp(MinCast, ItemTable.Items[worn[(int)GearSlot.Hand]].Reach, charge);
            var line = new FishLine { Cast = Time.time, Reeled = -1f };
            string where;
            if (WaterAt(terrain, me.Position, dir, distance, out var g))
            {
                var xz = me.Position.xz + dir * distance;
                line.Target = new float3(xz.x, g.WaterSurface, xz.y);
                line.Bed = g.Height;
                line.Water = true;
                where = $"onto {WaterKind(terrain, g)} (surface {g.WaterSurface:0.00}, {g.WaterSurface - g.Height:0.0} m deep)";
            }
            else
            {
                // (Onto ground: it drops short, on the floor under where it falls, a cave's too.)
                var xz = me.Position.xz + dir * distance * 0.35f;
                line.Target = new float3(xz.x, terrain.FloorUnder(new float3(xz.x, me.Position.y + 1f, xz.y), out _), xz.y);
                where = $"onto the ground: dropped short at {distance * 0.35f:0.0} m";
            }
            ownLine = line;
            ownOut = true;
            ownLanded = false;
            // (The rod's flick is the weapon swing.)
            ownSwing = Time.time;
            Debug.Log($"[fish] cast: charge {charge:0.00}, aimed {distance:0.0} m, {where}");
            var rpc = new FishRpc { Kind = 1, Water = (byte)(line.Water ? 1 : 0), Target = line.Target };
            if (Joined)
                SendToHost(rpc);
            else if (Hosting)
            {
                rpc.Player = OwnNetworkId();
                Broadcast(rpc);
            }
        }

        static string WaterKind(in TerrainSource terrain, in GroundSample g) =>
            g.CaveWater ? "cave water" : math.abs(g.WaterSurface - terrain.World.SeaLevel) < 0.05f ? "the sea" : g.River > 0.5f ? "a river" : "a lake";

        /// <summary>Reels our line in (at once, with no reel to watch, if the rod was put away).</summary>
        void Reel(bool now)
        {
            if (!ownOut)
                return;
            if (now)
                ownOut = false;
            else
                ownLine.Reeled = Time.time;
            Debug.Log($"[fish] reeled in{(now ? " (rod put away)" : "")}");
            var rpc = new FishRpc { Kind = 2 };
            if (Joined)
                SendToHost(rpc);
            else if (Hosting)
            {
                rpc.Player = OwnNetworkId();
                Broadcast(rpc);
            }
        }

        /// <summary>The host: a client's cast or reel, kept and passed on to everyone.</summary>
        void ServeFish(EntityManager em)
        {
            using var rs = Q(em, ComponentType.ReadWrite<FishRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                var m = em.GetComponentData<FishRpc>(r);
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                {
                    m.Player = em.GetComponentData<NetworkId>(from).Value;
                    TakeFish(m);
                    Broadcast(m);
                }
                em.DestroyEntity(r);
            }
        }

        /// <summary>On a joined client: another player's cast or reel (our own comes back too: we have it already).</summary>
        void OnFish(FishRpc m)
        {
            if (m.Player != OwnNetworkId())
                TakeFish(m);
        }

        void TakeFish(in FishRpc m)
        {
            if (m.Kind == 1)
            {
                linesOf[m.Player] = new FishLine { Target = m.Target, Water = m.Water != 0, Cast = Time.time, Reeled = -1f, Bed = m.Target.y - 2f };
                swungAt[m.Player] = Time.time;
                Debug.Log($"[fish] player {m.Player} cast {(m.Water != 0 ? "onto water" : "onto the ground")} at ({m.Target.x:F1}, {m.Target.y:F2}, {m.Target.z:F1})");
            }
            else if (linesOf.TryGetValue(m.Player, out var line))
            {
                line.Reeled = Time.time;
                linesOf[m.Player] = line;
                Debug.Log($"[fish] player {m.Player} reeled in");
            }
        }

        /// <summary>Each frame: every line out, ours and other players', from its rod's tip to its bobber.</summary>
        void DrawFishing(in TerrainSource terrain)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || Camera.main == null)
                return;
            var em = world.EntityManager;
            if (ownOut)
            {
                var own = Q(em, ComponentType.ReadWrite<PlayerAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                if (own.TryGetSingletonEntity<PlayerAvatar>(out var avatar))
                    DrawLine(em, terrain, avatar, -1, ownLine);
            }
            if (linesOf.Count == 0)
                return;
            // (Lines reeled all the way in are dropped.)
            lineDone.Clear();
            foreach (var kv in linesOf)
                if (kv.Value.Reeled >= 0f && Time.time > kv.Value.Reeled + ReelTime)
                    lineDone.Add(kv.Key);
            foreach (var id in lineDone)
                linesOf.Remove(id);
            var remote = Q(em, ComponentType.ReadWrite<RemoteAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var bodies = remote.ToEntityArray(Allocator.Temp);
            foreach (var e in bodies)
            {
                var ghost = em.GetComponentData<RemoteAvatar>(e).Ghost;
                if (em.Exists(ghost) && em.HasComponent<GhostOwner>(ghost) && linesOf.TryGetValue(em.GetComponentData<GhostOwner>(ghost).NetworkId, out var line))
                    DrawLine(em, terrain, e, em.GetComponentData<GhostOwner>(ghost).NetworkId, line);
            }
        }

        void DrawLine(EntityManager em, in TerrainSource terrain, Entity body, int key, in FishLine line)
        {
            var s = em.GetComponentData<CreatureState>(body);
            if (s.Outfit.Weapon != CreatureWeapon.Rod)
                return;
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var cam = (float3)Camera.main.transform.position;
            var scale = math.max(s.Scale, 0.3f);
            if (view.PixelsPerMetre > 0f && Punchy.Settings.DetailPolicy.ScreenSize(scale, math.distance(s.Position, cam), view.PixelsPerMetre) < LinePixels)
                return;
            // The rod's tip, where the rod is drawn: the body posed as the renderer poses it, its right hand's end.
            var cb = em.GetComponentData<CreatureBody>(body);
            ref var b = ref cb.Blob.Value;
            int hand = -1;
            for (int i = 0; i < b.Parts.Length && hand < 0; i++)
                if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f && b.Parts[i].SegmentCount > 0)
                    hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
            if (hand < 0)
                return;
            using var output = new NativeArray<SegmentInstance>(b.Segments.Length + 64, Allocator.Temp);
            CreatureAnimator.Evaluate(ref b, s, cb.Coat, terrain, output, 0);
            var tip = CreatureRenderSystem.RodTip(output[hand], s.Yaw, s.Outfit.Swing, s.Scale);

            // The bobber: in flight an arc out to where it lands; then on the water, riding the swell and bobbing (or lying
            // on the ground); reeled, back to the tip.
            var t = Time.time;
            var flight = FlightTime(math.distance(tip.xz, line.Target.xz));
            var landed = line.Target;
            if (line.Water)
                landed.y += WaterSwells.Height(line.Target.xz, line.Target.y, line.Bed) + math.sin(t * 2.3f + key) * 0.02f;
            float3 bob;
            float slack;
            if (line.Reeled >= 0f)
            {
                var u = math.smoothstep(0f, 1f, math.saturate((t - line.Reeled) / ReelTime));
                bob = math.lerp(landed, tip, u);
                slack = 1f - u;
            }
            else if (t < line.Cast + flight)
            {
                var u = math.saturate((t - line.Cast) / flight);
                bob = math.lerp(tip, landed, u) + new float3(0f, 1f, 0f) * math.distance(tip.xz, landed.xz) * 0.3f * 4f * u * (1f - u);
                slack = 0f;
            }
            else
            {
                bob = landed;
                slack = 1f;
            }
            if (itemMaterial == null) itemMaterial = Resources.Load<Material>("ItemMaterial");   // (not ??=: it misses a destroyed Unity object)
            if (itemMaterial == null)
                return;
            // The line: a thin tube, slack between its ends (a sag of up to 1.2 m), never thinner than about a pixel.
            var length = math.distance(tip, bob);
            var sag = slack * math.min(0.15f * length, 1.2f);
            var radius = math.max(0.003f, view.PixelsPerMetre > 0f ? 0.5f * math.distance(cam, (tip + bob) * 0.5f) / view.PixelsPerMetre : 0f);
            BuildLine(key, tip, bob, sag, radius);
            lineBlock ??= Block(new Color(0.86f, 0.86f, 0.8f));
            var rp = new RenderParams(itemMaterial) { matProps = lineBlock };
            Graphics.RenderMesh(rp, lineMeshes[key], 0, Matrix4x4.identity);
            if (view.PixelsPerMetre > 0f && Punchy.Settings.DetailPolicy.ScreenSize(0.1f, math.distance(bob, cam), view.PixelsPerMetre) < 1f)
                return;
            var bobber = ItemMeshes.Bobber();
            var at = Matrix4x4.Translate(bob);
            bobberRed ??= Block(new Color(0.8f, 0.12f, 0.1f));
            bobberWhite ??= Block(new Color(0.92f, 0.92f, 0.88f));
            Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = bobberRed }, bobber, 0, at);
            if (bobber.subMeshCount > 1)
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = bobberWhite }, bobber, 1, at);
        }

        static MaterialPropertyBlock Block(Color c)
        {
            var b = new MaterialPropertyBlock();
            b.SetColor(BaseColorId, c);
            return b;
        }

        /// <summary>This line's tube mesh (one per player, rebuilt in place each frame): LineSegments pieces from a to b,
        /// hanging this far at its middle.</summary>
        void BuildLine(int key, float3 a, float3 b, float sag, float radius)
        {
            if (!lineMeshes.TryGetValue(key, out var mesh))
            {
                mesh = new Mesh { name = "Fishing line", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                lineMeshes[key] = mesh;
                mesh.vertices = lineVerts;
                mesh.triangles = LineTriangles();
            }
            var along = math.normalizesafe(b - a, new float3(0f, -1f, 0f));
            var side = math.normalizesafe(math.cross(along, new float3(0f, 1f, 0f)), new float3(1f, 0f, 0f));
            var up = math.cross(side, along);
            for (int i = 0; i <= LineSegments; i++)
            {
                var t = i / (float)LineSegments;
                var p = math.lerp(a, b, t) - new float3(0f, 1f, 0f) * sag * 4f * t * (1f - t);
                for (int k = 0; k < LineSides; k++)
                {
                    var angle = k * math.PI * 2f / LineSides;
                    var n = side * math.cos(angle) + up * math.sin(angle);
                    lineVerts[i * LineSides + k] = p + n * radius;
                    lineNormals[i * LineSides + k] = n;
                }
            }
            mesh.vertices = lineVerts;
            mesh.normals = lineNormals;
            mesh.RecalculateBounds();
        }

        /// <summary>The line tube's triangles (the same for every line; made once).</summary>
        int[] LineTriangles()
        {
            if (lineTris != null)
                return lineTris;
            lineTris = new int[LineSegments * LineSides * 6];
            var n = 0;
            for (int i = 0; i < LineSegments; i++)
                for (int k = 0; k < LineSides; k++)
                {
                    int a0 = i * LineSides + k, a1 = i * LineSides + (k + 1) % LineSides, b0 = a0 + LineSides, b1 = a1 + LineSides;
                    lineTris[n++] = a0; lineTris[n++] = b0; lineTris[n++] = b1;
                    lineTris[n++] = a0; lineTris[n++] = b1; lineTris[n++] = a1;
                }
            return lineTris;
        }
    }
}
