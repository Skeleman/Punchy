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
    /// ignored (the host knows who sent it). Kind 3 (row 2): its stage now (FishStage), with the fish's item and size
    /// once caught.</summary>
    public struct FishRpc : IRpcCommand
    {
        public int Player;
        public byte Kind, Water, Stage, Item, Size;
        public float3 Target;
    }

    /// <summary>Where a line on the water is (P-33 row 2): waiting, a bite (the bobber dips), hooked (reeling, the line
    /// taut), caught (reeled in with the fish on it), or lost (missed or snapped: the bait gone, reeled in empty).</summary>
    public static class FishStage
    {
        public const byte Waiting = 0, Bite = 1, Hooked = 2, Caught = 3, Lost = 4;
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
    /// Row 2: what bites comes from FishSource (IFishSource; a stub table by kind of water until A-100). After its wait the
    /// bobber dips with a splash and its sound; press within BiteWindow to hook it, then hold to reel (the line tightens,
    /// more while the fish runs; let go to slacken it): wound in, the fish (a Fish item; its kind and size in the log and
    /// a note) goes to the inventory and hangs from the rod tip a moment; a missed bite or a snapped line loses the bait.
    /// Each stage goes to everyone (FishRpc Kind 3), so others see the bite, the fight and the catch.
    /// Test flags: "-cast 0.8;0.4" casts with those charges (0-1), the first 5 s after a rod is in hand, each next one 3 s
    /// after the line before is in (one with nothing on it is reeled in after 30 s); "-fishplay hook|miss|snap" plays the
    /// bite and the reel; "-castwater" turns the body first toward the nearest water in reach,
    /// and the camera to look out along the cast from a little to one side.
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
            /// <summary>Row 2: its FishStage and since when (Time.time), and the fish caught (item id and cm).</summary>
            public byte Stage, Fish, Size;
            public float StageAt;
        }

        /// <summary>Row 2: what bites (IFishSource; A-100's tables replace the stub in row 3).</summary>
        public static IFishSource FishSource = new StubFishSource();
        /// <summary>How long a bite lasts before the fish lets go (s), and how long a catch hangs from the rod tip (s).</summary>
        const float BiteWindow = 0.9f, CatchShow = 1.5f;
        /// <summary>The reel (row 2): holding the button winds in (Wind of the way a second, slower while the fish runs) and
        /// tightens the line (Tighten a second, more while it runs); letting go slackens it (Slacken a second). At 1 it snaps.</summary>
        const float Wind = 0.32f, Tighten = 0.4f, RunTighten = 0.55f, Slacken = 0.75f;

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
        FishBite ownBite;
        bool ownBiteSet;
        float ownTension, ownProgress;
        uint castCount;
        /// <summary>Testing: "-fishplay hook" (strike at the bite, reel keeping the line under 0.7), "miss" (never
        /// strike), "snap" (strike, then hold until it snaps).</summary>
        string fishPlayArg;
        string catchNote;
        float catchNoteUntil;

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
            var play = System.Array.IndexOf(args, "-fishplay");
            if (play >= 0 && play + 1 < args.Length)
                fishPlayArg = args[play + 1].ToLowerInvariant();
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
            if (ownOut && ownLine.Reeled >= 0f && Time.time > ownLine.Reeled + ReelTime + (ownLine.Stage == FishStage.Caught ? CatchShow : 0f))
                ownOut = false;
            if (ownOut && !ownLanded && ownLine.Reeled < 0f && Time.time > ownLine.Cast + flight)
            {
                ownLanded = true;
                ownLine.StageAt = Time.time;
                Debug.Log($"[fish] landed {(ownLine.Water ? "on water" : "on the ground")} at ({ownLine.Target.x:F1}, {ownLine.Target.y:F2}, {ownLine.Target.z:F1}), " +
                          $"{math.distance(me.Position.xz, ownLine.Target.xz):0.0} m out");
            }
            // (In the inventory or a menu the button does nothing, but a bite still comes and goes, and a hooked fish
            // still pulls on a line that isn't being wound.)
            var open = panel || Punchy.UI.Overlay.AnyOpen;
            bool pressed = false, released = false, held = false;
            if (!open)
            {
                var mouse = Mouse.current;
                var pad = Gamepad.current;
                var mouseOn = mouse != null && Cursor.lockState == CursorLockMode.Locked;
                pressed = mouseOn && mouse.leftButton.wasPressedThisFrame || pad != null && pad.buttonWest.wasPressedThisFrame;
                released = mouseOn && mouse.leftButton.wasReleasedThisFrame || pad != null && pad.buttonWest.wasReleasedThisFrame;
                held = mouseOn && mouse.leftButton.isPressed || pad != null && pad.buttonWest.isPressed;
            }
            var onWater = ownOut && ownLanded && ownLine.Water && ownLine.Reeled < 0f;
            if (fishPlayArg != null && onWater)
                (pressed, held) = TestPlay();
            if (pressed)
            {
                // (A press strikes at a bite; while waiting it reels in; with no line out it starts the aim.)
                if (onWater && ownLine.Stage == FishStage.Bite)
                    Hook();
                else if (ownOut && ownLine.Reeled < 0f && ownLine.Stage == FishStage.Waiting)
                    Reel(false);
                else if (!ownOut)
                    aimFrom = Time.time;
            }
            if (onWater)
                FishStep(terrain, held);
            if (catchNote != null && Time.time < catchNoteUntil)
                prompt = catchNote;
            if (open)
            {
                aimFrom = -1f;
                return;
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

        /// <summary>"-cast": the test casts, each turned toward water first with "-castwater", the next 3 s after a line is in.</summary>
        void FishTest(in TerrainSource terrain)
        {
            if (castArg == null || castDone >= castArg.Length || !TryPlayer(out _))
                return;
            if (castNext < 0f)
                castNext = Time.time + 5f;
            // (Nothing bit in 30 s: reel in. The next cast 3 s after a line is in.)
            if (ownOut && ownLine.Reeled < 0f && ownLine.Stage == FishStage.Waiting && Time.time > ownLine.Cast + 30f)
                Reel(false);
            if (ownOut)
            {
                castNext = math.max(castNext, Time.time + 3f);
                return;
            }
            if (Time.time < castNext)
                return;
            var charge = math.saturate(castArg[castDone++]);
            if (castWaterArg)
                FaceWater(terrain, math.lerp(MinCast, ItemTable.Items[worn[(int)GearSlot.Hand]].Reach, charge));
            Cast(terrain, charge);
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
                if (FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>() is Punchy.CameraRig.ThirdPersonCamera rig)
                    rig.SetView(math.degrees(yaw) + 35f, 18f);
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
            ownBiteSet = false;
            castCount++;
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

        /// <summary>For the log: which water (at sea level it is the sea, as WaterSwells counts it: inlets and lagoons too).</summary>
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

        /// <summary>
        /// Row 2, each frame a line is on the water: the bite comes after the source's wait (the bobber dips, a splash and
        /// its sound) and lasts BiteWindow; struck in time it is hooked. Hooked, holding the button winds in and tightens
        /// the line, more while the fish runs (it runs in bursts), letting go slackens it: wound all the way in, it is
        /// caught; at full tension the line snaps. A missed bite or a snapped line loses the bait: the line comes in empty.
        /// </summary>
        void FishStep(in TerrainSource terrain, bool held)
        {
            var now = Time.time;
            switch (ownLine.Stage)
            {
                case FishStage.Waiting:
                    if (!ownBiteSet)
                    {
                        ownBiteSet = true;
                        var water = terrain.SampleGround(ownLine.Target - new float3(0f, 0.1f, 0f));
                        var seed = math.hash(new uint3(castCount, (uint)(int)(ownLine.Target.x * 10f), (uint)(int)(ownLine.Target.z * 10f)));
                        if (!FishSource.Next(terrain, water, ownLine.Target, seed, out ownBite))
                            ownBite.Wait = -1f;
                        else
                            Debug.Log($"[fish] waiting: a {ownBite.Name} ({ownBite.Size} cm) will bite in {ownBite.Wait:0.0} s");
                    }
                    if (ownBite.Wait >= 0f && now > ownLine.StageAt + ownBite.Wait)
                    {
                        SetStage(FishStage.Bite);
                        Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Splash, ownLine.Target, 1.4f, 0.5f, 25f, Punchy.Audio.SoundGroup.World);
                        Debug.Log($"[fish] bite! ({BiteWindow:0.0} s to strike)");
                    }
                    break;
                case FishStage.Bite:
                    if (now > ownLine.StageAt + BiteWindow)
                        Lose($"missed: the {ownBite.Name} let go, the bait is gone");
                    break;
                case FishStage.Hooked:
                {
                    var dt = Time.deltaTime;
                    var runs = math.sin(now * 2.3f + ownBite.Size) > 0.3f;
                    ownTension = math.max(0f, ownTension + dt * (held ? Tighten + (runs ? RunTighten * ownBite.Strength : 0f) : -Slacken));
                    if (held)
                        ownProgress += dt * Wind * (runs ? 0.4f : 1f);
                    var bars = (int)(math.saturate(ownTension) * 10f);
                    prompt = $"Reel (hold)  line [{new string('#', bars)}{new string('.', 10 - bars)}]  {math.saturate(ownProgress):P0}";
                    if (ownTension >= 1f)
                        Lose($"the line snapped: the {ownBite.Name} got away");
                    else if (ownProgress >= 1f)
                        Catch();
                    break;
                }
            }
        }

        /// <summary>Testing (-fishplay): this frame's press and hold, as a player would.</summary>
        (bool pressed, bool held) TestPlay()
        {
            var t = Time.time - ownLine.StageAt;
            switch (fishPlayArg)
            {
                case "hook":
                    // (Strike a quarter second into the bite; reel, letting go above 0.7 and taking up again under 0.35.)
                    if (ownLine.Stage == FishStage.Bite)
                        return (t > 0.25f && t < 0.25f + Time.deltaTime * 1.5f, false);
                    if (ownLine.Stage == FishStage.Hooked)
                        testWinding = testWinding ? ownTension < 0.7f : ownTension < 0.35f;
                    return (false, ownLine.Stage == FishStage.Hooked && testWinding);
                case "snap":
                    if (ownLine.Stage == FishStage.Bite)
                        return (t > 0.25f && t < 0.25f + Time.deltaTime * 1.5f, false);
                    return (false, ownLine.Stage == FishStage.Hooked);
                default:
                    return (false, false);
            }
        }

        bool testWinding = true;

        void Hook()
        {
            ownTension = 0.3f;
            ownProgress = 0f;
            testWinding = true;
            SetStage(FishStage.Hooked);
            Debug.Log($"[fish] hooked a {ownBite.Name}: reel it in");
        }

        void Catch()
        {
            Grant(ItemTable.Fish, 1);
            ownLine.Fish = ItemTable.Fish;
            ownLine.Size = ownBite.Size;
            SetStage(FishStage.Caught);
            ownLine.Reeled = Time.time;
            catchNote = $"Caught a {ownBite.Name} ({ownBite.Size} cm)";
            catchNoteUntil = Time.time + 3f;
            Debug.Log($"[fish] caught a {ownBite.Name}, {ownBite.Size} cm; inventory: {DescribeInventory()}");
        }

        void Lose(string why)
        {
            SetStage(FishStage.Lost);
            ownLine.Reeled = Time.time;
            Debug.Log($"[fish] {why}");
        }

        /// <summary>Our line's stage, here and for everyone (FishRpc Kind 3).</summary>
        void SetStage(byte stage)
        {
            ownLine.Stage = stage;
            ownLine.StageAt = Time.time;
            var rpc = new FishRpc { Kind = 3, Stage = stage, Item = ownLine.Fish, Size = ownLine.Size };
            if (Joined)
                SendToHost(rpc);
            else if (Hosting)
            {
                rpc.Player = OwnNetworkId();
                Broadcast(rpc);
            }
        }

        static string StageName(byte stage) => stage switch
        {
            FishStage.Bite => "a bite",
            FishStage.Hooked => "hooked",
            FishStage.Caught => "caught",
            FishStage.Lost => "lost it",
            _ => "waiting",
        };

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
            else if (m.Kind == 3 && linesOf.TryGetValue(m.Player, out var staged))
            {
                (staged.Stage, staged.StageAt, staged.Fish, staged.Size) = (m.Stage, Time.time, m.Item, m.Size);
                // (Caught or lost: it reels in, with the fish or empty.)
                if (m.Stage == FishStage.Caught || m.Stage == FishStage.Lost)
                    staged.Reeled = Time.time;
                if (m.Stage == FishStage.Bite)
                    Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Splash, staged.Target, 1.4f, 0.5f, 25f, Punchy.Audio.SoundGroup.World);
                linesOf[m.Player] = staged;
                // (The host's tally of what each player carries, P-17: a client's catch.)
                if (m.Stage == FishStage.Caught && Hosting && m.Item < ItemTable.Items.Length)
                    Carry(m.Player, m.Item, 1);
                var what = m.Stage == FishStage.Caught && m.Item < ItemTable.Items.Length ? ", " + ItemTable.Items[m.Item].Name + " " + m.Size + " cm" +
                    (Hosting ? $" (host tally: {Carries(m.Player, m.Item)})" : "") : "";
                Debug.Log($"[fish] player {m.Player}: {StageName(m.Stage)}{what}");
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
                if (kv.Value.Reeled >= 0f && Time.time > kv.Value.Reeled + ReelTime + (kv.Value.Stage == FishStage.Caught ? CatchShow : 0f))
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
            // (A bite: the bobber dips and jerks. Hooked: pulled under and darting about, the line taut.)
            if (line.Stage == FishStage.Bite && line.Reeled < 0f)
                landed.y -= 0.07f * (0.5f + 0.5f * math.sin((t - line.StageAt) * 20f));
            else if (line.Stage == FishStage.Hooked && line.Reeled < 0f)
                landed += new float3(0.18f * math.sin(t * 7f), -0.06f, 0.18f * math.cos(t * 5.3f));
            float3 bob;
            float slack;
            var caught = line.Stage == FishStage.Caught;
            if (line.Reeled >= 0f)
            {
                var u = math.smoothstep(0f, 1f, math.saturate((t - line.Reeled) / ReelTime));
                // (A catch comes in on the line and hangs from the rod's tip a moment.)
                bob = math.lerp(landed, caught ? tip - new float3(0f, 0.35f, 0f) : tip, u);
                slack = caught ? 0f : 1f - u;
            }
            else if (line.Stage == FishStage.Hooked)
            {
                bob = landed;
                slack = 0f;
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
            if (caught && line.Fish < ItemTable.Items.Length)
            {
                // The fish, nose down from the line (its mesh lies on its side, nose along +z), sized by its length.
                var fishMesh = ItemMeshes.Of(line.Fish);
                var hang = Matrix4x4.TRS(bob, Quaternion.LookRotation(Vector3.down, Vector3.forward), Vector3.one * math.clamp(line.Size / 35f, 0.5f, 2f));
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = FishBlock(line.Fish, 0) }, fishMesh, 0, hang);
                if (fishMesh.subMeshCount > 1)
                    Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = FishBlock(line.Fish, 1) }, fishMesh, 1, hang);
                return;
            }
            var bobber = ItemMeshes.Bobber();
            var at = Matrix4x4.Translate(bob);
            if (line.Stage == FishStage.Bite && line.Reeled < 0f)
            {
                // The splash: a ring of droplets, growing over the bite.
                var grow = 0.6f + 2f * math.saturate((t - line.StageAt) / BiteWindow);
                bobberWhite ??= Block(new Color(0.92f, 0.92f, 0.88f));
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = bobberWhite }, ItemMeshes.Splash(), 0,
                                    Matrix4x4.TRS(new Vector3(bob.x, line.Target.y, bob.z), Quaternion.identity, new Vector3(grow, 1f, grow)));
            }
            bobberRed ??= Block(new Color(0.8f, 0.12f, 0.1f));
            bobberWhite ??= Block(new Color(0.92f, 0.92f, 0.88f));
            Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = bobberRed }, bobber, 0, at);
            if (bobber.subMeshCount > 1)
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = bobberWhite }, bobber, 1, at);
        }

        MaterialPropertyBlock fishMain, fishAccent;

        /// <summary>A caught fish's colours, its item's.</summary>
        MaterialPropertyBlock FishBlock(byte item, int part)
        {
            if (part == 0)
                return fishMain ??= Block(ItemTable.Items[item].Colour);
            return fishAccent ??= Block(ItemTable.Items[item].Accent);
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
