using Punchy.Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-101 row 1b (#267): a ledge, a graded lip or narrow shelf along a wall that a climber hangs from by its hands and
    /// shimmies along: a straight run of lip at lip height (From to To), which side of it the hanger hangs on, how deep
    /// the shelf is (room to pull up onto it), and its grade by its grip (a cut stone course or a root 2, a rock lip 3, a
    /// thin crack 4).
    /// </summary>
    public struct ClimbLedge
    {
        /// <summary>The lip's two ends, on the lip (world).</summary>
        public float3 From, To;
        /// <summary>The side the hanger hangs on: out from the wall under the lip (xz, unit).</summary>
        public float2 Out;
        /// <summary>How deep the top behind the lip is (m): room enough, and it pulls up onto it.</summary>
        public float Depth;
        public byte Grade;

        /// <summary>Along the lip from From to To (xz, unit).</summary>
        public float2 Dir => math.normalizesafe(To.xz - From.xz, new float2(1f, 0f));
        public float Length => math.distance(From.xz, To.xz);
        /// <summary>The lip's height s metres along it from From.</summary>
        public float HeightAt(float s) => math.lerp(From.y, To.y, math.saturate(s / math.max(Length, 1e-3f)));
    }

    /// <summary>
    /// A-101 row 1b: the ledges a climber can hang from. Until the world lane's are on main (W-144 cave ledges, W-147
    /// structure ledges) they are the ones "-place ledge:lip" builds (DebugControls), and a joined client the same from
    /// "-ledgepiece". Lips whose ends meet (within Join) are one ledge round a corner. A shared list, so Burst movement
    /// reads it. TODO(A-101 row 2): the world's ledges join them.
    /// </summary>
    public struct ClimbLedges
    {
        public static readonly SharedStatic<FixedList512Bytes<ClimbLedge>> List = SharedStatic<FixedList512Bytes<ClimbLedge>>.GetOrCreate<ClimbLedges, ListKey>();
        class ListKey { }

        /// <summary>How close two lips' ends are to be one ledge round a corner (m).</summary>
        public const float Join = 0.15f;

        /// <summary>Adds a ledge (main thread); a full list drops the oldest.</summary>
        public static void Add(ClimbLedge ledge)
        {
            ref var list = ref List.Data;
            if (list.Length >= list.Capacity)
                list.RemoveAt(0);
            list.Add(ledge);
        }

        public static void Clear() => List.Data.Clear();

        /// <summary>The lip whose end meets lip i's end (its To end, or its From end), and which of its ends; -1 if none.</summary>
        public static int Joined(int i, bool atTo, out bool otherAtTo)
        {
            ref var list = ref List.Data;
            var end = atTo ? list[i].To : list[i].From;
            otherAtTo = false;
            for (int j = 0; j < list.Length; j++)
            {
                if (j == i)
                    continue;
                if (math.distance(list[j].From.xz, end.xz) <= Join && math.abs(list[j].From.y - end.y) < 0.3f)
                    return j;
                if (math.distance(list[j].To.xz, end.xz) <= Join && math.abs(list[j].To.y - end.y) < 0.3f)
                {
                    otherAtTo = true;
                    return j;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// A-101 row 1b (Zach 11-12: "ledges that can be shimmied across... factor in the old system of climbing onto corner
    /// surfaces"): hanging from a ledge's lip, folded into the #33 wall climb rather than a second system. It uses the
    /// same latch (PlayerMotion.OnWall, which is also the climb pose the others see), with PlayerMotion.Hang saying which
    /// lip. Sideways shimmies along the lip, round an outer corner (swinging round it) and into an inner one (turning
    /// there); up pulls onto the top if there's room; down lets go; a jump leaps back off. A climber reaching a lip from a
    /// fall or a jump grabs it if its skill is at least the lip's grade; at a lip's open end it steps onto a climbable
    /// face beside, and from such a face back onto the lip. Bodies that climb the wall itself (skill at least the wall's
    /// grade) keep the wall climb and never hang. Burst-safe; called by PlayerMovementSystem right after the ladders.
    /// </summary>
    public static class LedgeHang
    {
        /// <summary>How far above the feet the hands hold the lip while hanging (m, of the body's size).</summary>
        public const float HandReach = 2.1f;
        /// <summary>The hold's distance off the lip past the body's radius (m).</summary>
        const float Gap = 0.05f;
        /// <summary>A lip is grabbed this far under the hands (m) to this far over them, rising no faster than GrabRise
        /// (m/s): near the top of a jump, or falling past it.</summary>
        const float GrabUnder = 0.3f, GrabOver = 0.6f, GrabRise = 3f;
        /// <summary>After catching a lip from the air it hangs this long (s) before up pulls onto the top.</summary>
        const float GrabHold = 0.4f;
        /// <summary>A shimmy's speed next to the climb's own (Climbing.Speed).</summary>
        const float Shimmy = 0.8f;
        /// <summary>The least shelf depth, of the body's size, it pulls up onto.</summary>
        const float PullDepth = 0.5f;

        /// <summary>
        /// One frame of the ledge hang for the local player: hanging (shimmy, pull up, let go, leap off), or taking hold of
        /// a lip (from the air, or off a climbable face). True when it moved the body this frame; false leaves the walk and
        /// the wall climb to go on as before (always, with no ledges near).
        /// </summary>
        public static bool Step(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in PlayerMotor settings,
                                float3 from, float2 facing, bool climbsWall, bool swimming, float dt)
        {
            ref var list = ref ClimbLedges.List.Data;
            if (list.Length == 0 || !m.OnWall)
                m.Hang = 0;
            if (list.Length == 0)
                return false;
            var size = body.Size > 0f ? body.Size : 1f;
            var hands = from.y + HandReach * size;
            if (m.Hang == 0)
            {
                if (m.OnWall)
                {
                    if (!FromFace(ref m, body, input, settings, from, facing, hands))
                        return false;
                }
                else
                {
                    // Taking hold from the air: a jump's top or a fall past the lip, pushing (or facing) into it. (Not a
                    // body that climbs the wall itself: it latches onto the wall as before.)
                    var push = math.normalizesafe(input.MoveDirection, facing);
                    if (m.Grounded || m.Mantle > 0f || m.LatchCooldown > 0f || swimming || climbsWall || m.VerticalVelocity > GrabRise ||
                        !Nearest(from.xz, hands, 0f, settings.Radius + 0.35f, body.ClimbSkill, out var grabbed, out _) ||
                        math.dot(push, -list[grabbed].Out) < 0.3f)
                        return false;
                    m.Hang = (byte)(grabbed + 1);
                    m.LatchCooldown = GrabHold;
                }
            }
            return Hang(ref t, ref m, body, input, settings, from, climbsWall, size, dt);
        }

        /// <summary>Hanging from lip Hang-1: as Step.</summary>
        static bool Hang(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in PlayerMotor settings,
                         float3 from, bool climbsWall, float size, float dt)
        {
            ref var list = ref ClimbLedges.List.Data;
            var i = m.Hang - 1;
            if (i >= list.Length || !Climbing.CanClimb(body.ClimbSkill, list[i].Grade))
            {
                // (The ledge is gone, or too hard for this body now: let go.)
                m.OnWall = false;
                m.Hang = 0;
                return false;
            }
            var l = list[i];
            var rad = settings.Radius + Gap;
            if (input.JumpPressed)
            {
                // Leaping off: up and back from the wall.
                m.OnWall = false;
                m.Hang = 0;
                m.VerticalVelocity = settings.JumpSpeed * 0.8f;
                m.LatchCooldown = 0.5f;
                t.Position = new float3(from.x + l.Out.x * 0.4f, from.y, from.z + l.Out.y * 0.4f);
                return true;
            }
            var into = -l.Out;
            StickOnSurface(input, out var up, out var side);
            // (The camera looking away from the wall: pushing on, away from it, is down, and the stick's right is still
            // the screen's right.)
            if (math.dot(math.normalizesafe(input.CameraForward, into), into) < 0f)
            {
                up = -up;
                side = -side;
            }
            Bounds(i, rad, out var lo, out var hi);
            var s = math.clamp(Param(i, from.xz, rad), lo, hi);
            if (up > 0.5f && up > math.abs(side))
            {
                if (m.LatchCooldown > 0f)
                {
                    // (Just caught hold: the stick still held into the wall from the jump waits a moment before pulling up.)
                }
                else if (l.Depth >= PullDepth * size)
                {
                    // Pulling up onto the top: the mantle, hands on the lip, up to it and a leg over, onto the shelf.
                    var lip = l.From.xz + l.Dir * math.clamp(s, 0f, l.Length);
                    var onto = lip - l.Out * math.min(settings.Radius + 0.15f, l.Depth * 0.5f);
                    m.OnWall = false;
                    m.Hang = 0;
                    m.Mantle = 1e-4f;
                    m.MantleFrom = from;
                    m.MantleTo = new float3(onto.x, l.HeightAt(math.clamp(s, 0f, l.Length)), onto.y);
                    m.VerticalVelocity = 0f;
                    t.Position = from;
                    return true;
                }
                else if (climbsWall)
                {
                    // (No room on top, but a face it can climb: on up the wall.)
                    m.Hang = 0;
                    return false;
                }
            }
            else if (up < -0.5f && -up > math.abs(side))
            {
                // Letting go: dropped straight down (to the ground, or a section below), not caught again at once.
                m.OnWall = false;
                m.Hang = 0;
                m.LatchCooldown = 0.25f;
                m.VerticalVelocity = 0f;
                return true;
            }
            else if (math.abs(side) > 0.2f)
            {
                // Shimmying: the stick's right (a hanger facing the wall) along the lip.
                var across = new float2(into.y, -into.x);
                var speed = Shimmy * Climbing.Speed(body.ClimbSpeed, body.ClimbSkill, l.Grade);
                s += side * math.dot(across, l.Dir) * speed * dt;
                if (s > hi || s < lo)
                {
                    var atTo = s > hi;
                    var over = atTo ? s - hi : lo - s;
                    var j = ClimbLedges.Joined(i, atTo, out var jAtTo);
                    if (j >= 0 && Climbing.CanClimb(body.ClimbSkill, list[j].Grade))
                    {
                        // Round the corner onto the next lip: from its end that meets this one, as far on as went past.
                        Bounds(j, rad, out var jLo, out var jHi);
                        i = j;
                        l = list[j];
                        s = jAtTo ? math.max(jHi - over, jLo) : math.min(jLo + over, jHi);
                        m.Hang = (byte)(j + 1);
                    }
                    else if (OntoFace(ref t, ref m, l, atTo, body, settings, from, rad))
                        return true;
                    else
                        s = math.clamp(s, lo, hi);
                }
            }
            Place(ref t, l, s, rad, size, dt);
            m.OnWall = true;
            m.Grounded = false;
            m.VerticalVelocity = 0f;
            return true;
        }

        /// <summary>
        /// The hold s metres along lip l: on the lip's line, out from it by rad, facing it; past an end that wraps round an
        /// outer corner, swung round that end (s past the end is the distance round the swing).
        /// </summary>
        static void Place(ref LocalTransform t, in ClimbLedge l, float s, float rad, float size, float dt)
        {
            var d = l.Dir;
            var length = l.Length;
            float2 xz, look;
            float y;
            if (s > length || s < 0f)
            {
                var past = s > length;
                var turn = (past ? s - length : -s) / rad;
                var round = l.Out * math.cos(turn) + (past ? d : -d) * math.sin(turn);
                xz = (past ? l.To.xz : l.From.xz) + round * rad;
                y = past ? l.To.y : l.From.y;
                look = -round;
            }
            else
            {
                xz = l.From.xz + d * s + l.Out * rad;
                y = l.HeightAt(s);
                look = -l.Out;
            }
            t.Position = new float3(xz.x, y - HandReach * size, xz.y);
            // (Turned smoothly: into an inner corner, the next lip faces a quarter turn round.)
            t.Rotation = math.slerp(t.Rotation, quaternion.LookRotationSafe(new float3(look.x, 0f, look.y), math.up()), math.min(1f, dt * 10f));
        }

        /// <summary>
        /// How far along lip i a hold at xz is (m from From), with a hold swung round an outer corner past an end counted on
        /// round the swing.
        /// </summary>
        static float Param(int i, float2 xz, float rad)
        {
            ref var list = ref ClimbLedges.List.Data;
            var l = list[i];
            var d = l.Dir;
            var length = l.Length;
            var u = math.dot(xz - l.From.xz, d);
            if (u > length && Outer(i, true))
            {
                var r = xz - l.To.xz;
                return length + math.max(math.atan2(math.dot(r, d), math.dot(r, l.Out)), 0f) * rad;
            }
            if (u < 0f && Outer(i, false))
            {
                var r = xz - l.From.xz;
                return -math.max(math.atan2(math.dot(r, -d), math.dot(r, l.Out)), 0f) * rad;
            }
            return u;
        }

        /// <summary>How far along lip i a hold may go: short of an end turning into an inner corner by rad (where the
        /// next lip's hold meets it), half way round the swing at an end wrapping an outer corner, to an open end.</summary>
        static void Bounds(int i, float rad, out float lo, out float hi)
        {
            lo = -EndReach(i, false, rad);
            hi = ClimbLedges.List.Data[i].Length + EndReach(i, true, rad);
        }

        /// <summary>How far past lip i's end a hold goes: -rad into an inner corner, half the swing round an outer one,
        /// 0 at an open end.</summary>
        static float EndReach(int i, bool atTo, float rad)
        {
            ref var list = ref ClimbLedges.List.Data;
            var j = ClimbLedges.Joined(i, atTo, out var jAtTo);
            if (j < 0)
                return 0f;
            var l = list[i];
            var away = jAtTo ? -list[j].Dir : list[j].Dir;
            if (math.dot(l.Out, away) > 0f)
                return -rad;
            return 0.5f * math.acos(math.clamp(math.dot(l.Out, list[j].Out), -1f, 1f)) * rad;
        }

        /// <summary>Whether lip i's end wraps round an outer corner onto the next lip.</summary>
        static bool Outer(int i, bool atTo)
        {
            ref var list = ref ClimbLedges.List.Data;
            var j = ClimbLedges.Joined(i, atTo, out var jAtTo);
            return j >= 0 && math.dot(list[i].Out, jAtTo ? -list[j].Dir : list[j].Dir) <= 0f;
        }

        /// <summary>
        /// The lip nearest xz that a body with this skill holds with its hands at handsY: along it within its ends, out from
        /// it between nearOut and farOut on its hanging side, its height near the hands. False if none.
        /// </summary>
        static bool Nearest(float2 xz, float handsY, float nearOut, float farOut, int skill, out int index, out float along)
        {
            ref var list = ref ClimbLedges.List.Data;
            index = -1;
            along = 0f;
            var best = float.MaxValue;
            for (int i = 0; i < list.Length; i++)
            {
                var l = list[i];
                var rel = xz - l.From.xz;
                var s = math.dot(rel, l.Dir);
                var o = math.dot(rel, l.Out);
                if (s < 0f || s > l.Length || o < nearOut || o > farOut || !Climbing.CanClimb(skill, l.Grade))
                    continue;
                var dy = handsY - l.HeightAt(s);
                if (dy < -GrabUnder || dy > GrabOver || math.abs(o) >= best)
                    continue;
                best = math.abs(o);
                index = i;
                along = s;
            }
            return index >= 0;
        }

        /// <summary>
        /// At lip l's open end (atTo: its To end), onto a face beside it this body climbs (a marked section, W-144's routes
        /// later), holding the wall there as the wall climb does. False if the face beside isn't climbable for it.
        /// </summary>
        static bool OntoFace(ref LocalTransform t, ref PlayerMotion m, in ClimbLedge l, bool atTo, in PlayerBody body, in PlayerMotor settings,
                             float3 from, float rad)
        {
            var end = atTo ? l.To.xz : l.From.xz;
            var beside = end + l.Out * rad + (atTo ? l.Dir : -l.Dir) * 0.3f;
            var into = -l.Out;
            var at = new float3(beside.x, from.y, beside.y);
            if (!Climbing.CanClimb(body.ClimbSkill, Climbing.WallGrade(new WorldClimbs(), at, into, settings.Radius + 0.5f)))
                return false;
            t.Position = at;
            t.Rotation = quaternion.LookRotationSafe(new float3(into.x, 0f, into.y), math.up());
            m.OnWall = true;
            m.Hang = 0;
            m.Grounded = false;
            m.VerticalVelocity = 0f;
            return true;
        }

        /// <summary>
        /// On a climbable face (the wall climb), pushed sideways to where the face beside isn't climbable for it, with a lip
        /// there at its hands: it takes hold of the lip (Hang set). False otherwise, and the wall climb goes on.
        /// </summary>
        static bool FromFace(ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in PlayerMotor settings, float3 from, float2 facing, float hands)
        {
            StickOnSurface(input, out var up, out var side);
            if (math.dot(math.normalizesafe(input.CameraForward, facing), facing) < 0f)
                side = -side;
            if (math.abs(side) <= 0.3f || math.abs(side) < math.abs(up))
                return false;
            var across = new float2(facing.y, -facing.x);
            var beside = from.xz + across * (math.sign(side) * settings.Radius);
            if (Climbing.CanClimb(body.ClimbSkill, Climbing.WallGrade(new WorldClimbs(), new float3(beside.x, from.y, beside.y), facing, settings.Radius + 0.5f)) ||
                !Nearest(beside, hands, -0.2f, settings.Radius + 0.5f, body.ClimbSkill, out var index, out _) ||
                math.dot(ClimbLedges.List.Data[index].Out, -facing) < 0.7f)
                return false;
            m.Hang = (byte)(index + 1);
            return true;
        }

        /// <summary>The stick against the camera: forward and right (as PlayerMovementSystem's own, for the wall climb).</summary>
        static void StickOnSurface(in PlayerInput input, out float forward, out float right)
        {
            var look = math.normalizesafe(input.CameraForward, new float2(0f, 1f));
            forward = math.dot(input.MoveDirection, look);
            right = math.dot(input.MoveDirection, new float2(look.y, -look.x));
        }
    }
}
