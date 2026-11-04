using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: climbing, tunnels, webs, nests, burrows, lounging, hearths, play rings. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            // ------------------------------------------------------------------------------- trees

            /// <summary>Picks a tree nearby and sets off to climb it (false if there's none).</summary>
            bool StartClimb(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                // (A burrower's Anchor is its burrow: it doesn't climb.)
                if (b.Burrows || !HasFlora || !FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 30f, 3.5f, ref rng, out var tree))
                    return false;
                SetMode(ref s, CreatureMode.Climb);
                s.Anchor = tree.Base;
                s.AnchorSize = math.max(tree.TrunkRadius, 0.05f);
                // A branch a little way up into the crown, and a spot out along it.
                var up = math.clamp(tree.CrownBase + (tree.Height - tree.CrownBase) * rng.NextFloat(0.15f, 0.4f), 1.2f, tree.Height * 0.85f);
                s.TargetHeight = tree.Base.y + up;
                s.Focus = tree.Base.xz + rng.NextFloat2Direction() * (s.AnchorSize + math.clamp(tree.CrownRadius * 0.4f, 0.35f, 1.6f));
                s.Timer = toSleep ? rng.NextFloat(120f, 300f) : rng.NextFloat(15f, 50f);
                return true;
            }

            /// <summary>
            /// Climbing a tree: to the foot of the trunk, up it (facing it), out along a branch, sitting there (idle
            /// actions, or asleep at night), back to the trunk and down again. Running from danger it ignores while
            /// up there.
            /// </summary>
            void Climb(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float idleLook,
                       ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical, ref float sit,
                       ref float desiredLook, ref float headDown, ref float curl)
            {
                var trunk = s.Anchor.xz;
                var climbSpeed = walk * 0.75f;
                // Standing at the foot (and out on a branch) the body keeps its legs' length off the trunk; up it, its feet
                // are on the bark and the body hangs a leg's length out from them (CreatureAnimator). It goes from one to
                // the other over its first stand height up the trunk, and back over its last coming down, so its chest
                // never ends up in the trunk.
                var standOff = s.AnchorSize + b.StandHeight * s.Scale * 0.8f + 0.02f;
                var low = math.saturate((s.Position.y - Terrain.SampleHeight(s.Position.xz)) / math.max(b.StandHeight * s.Scale, 0.05f));
                var reach = math.lerp(standOff, s.AnchorSize + 0.02f, math.min(math.saturate(s.Vertical), low));
                var outward = math.normalizesafe(s.Position.xz - trunk, new float2(0f, 1f));
                switch (s.Stage)
                {
                    case 0:
                        s.Target = trunk + outward * (standOff + 0.15f);
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 0.3f)
                            NextStage(ref s);
                        else if (s.ModeTime > 40f)
                        {
                            Pause(ref s, ref b, ref rng);
                            s.AnchorSize = 0f;
                        }
                        break;
                    case 1:
                        custom = true;
                        vertical = 1f;
                        TurnTowards(ref s, ref b, trunk, dt);
                        s.Position.xz = trunk + outward * reach;
                        // Gets onto the trunk before going up.
                        s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                        s.Position.y = math.min(s.Position.y + s.Speed * dt, s.TargetHeight);
                        if (s.Position.y >= s.TargetHeight - 0.01f)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Position.y = s.TargetHeight;
                        // Level out, then walk out along the branch.
                        if (s.Vertical > 0.25f)
                            s.Speed = 0f;
                        else if (GlideTo(ref s, ref b, s.Focus, walk * 0.6f, dt) < 0.15f)
                        {
                            NextStage(ref s);
                            s.Yaw = math.atan2(s.Position.x - trunk.x, s.Position.z - trunk.y);   // faces out
                        }
                        break;
                    case 3:
                        custom = true;
                        s.Speed = 0f;
                        s.YawRate = 0f;
                        sit = 1f;
                        desiredLook = idleLook;
                        if (SleepTime(ref b) > 0.5f)
                        {
                            headDown = 0.6f;
                            curl = 0.7f;
                        }
                        if (s.Action == IdleAction.None)
                            s.Timer -= dt;
                        if (s.Timer <= 0f)
                            NextStage(ref s);
                        break;
                    case 4:
                        custom = true;
                        s.Position.y = s.TargetHeight;
                        if (GlideTo(ref s, ref b, trunk + math.normalizesafe(s.Focus - trunk, outward) * standOff, walk * 0.6f, dt) < 0.1f)
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        vertical = 1f;
                        TurnTowards(ref s, ref b, trunk, dt);
                        s.Position.xz = trunk + outward * reach;
                        s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                        var floor = Terrain.SampleHeight(s.Position.xz);
                        s.Position.y = math.max(s.Position.y - s.Speed * dt, floor);
                        if (s.Position.y <= floor + 0.01f)
                        {
                            Pause(ref s, ref b, ref rng);
                            s.AnchorSize = 0f;   // let go of the tree (AnchorSize > 0 means a lair to burrowers)
                        }
                        break;
                }
            }

            /// <summary>
            /// A sand or snow worm: deep under the ground, gliding from one flat spot to another round where it lives (the
            /// world draws its ripple and furrow on the surface from CreatureBurrowSystem); every half minute to minute and
            /// a half, out on flat ground, it bursts up out of it in a great arc, its body rippling up and down, and dives
            /// back in further on. (Air runs 0..1 through the arc; Fly holds its body's wave upright meanwhile.) The arc
            /// starts and ends deep enough that all of the worm is under the ground there, tilted up out of it or down
            /// into it as the animator draws it, so it comes up out of its hole and goes all the way back in before it
            /// levels out.
            /// </summary>
            void Tunnel(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var radius = math.max(b.FrontRadius * s.Scale, 0.3f);
                var depth = radius * 3f + 1f;
                // (Tilted by up to about 0.9 rad at the ends of its arc, the whole length of it under: body, tail and head.)
                var length = b.BodyLength;
                for (int k = 0; k < b.Segments.Length; k++)
                    length += b.Segments[k].Length;
                var deep = math.max(depth, length * s.Scale * 0.8f + radius);
                if (s.Mode != CreatureMode.Wander)
                    SetMode(ref s, CreatureMode.Wander);
                if (math.all(s.Home == float2.zero))
                {
                    s.Home = s.Position.xz;
                    s.Timer = rng.NextFloat(10f, 40f);
                }
                var ground = Terrain.SampleHeight(s.Position.xz);
                if (s.Air > 0f)
                {
                    // Out of the ground and back at speed, about three seconds of it above the ground.
                    var above = 1f - 2f * math.asin(deep / (deep + s.AirHeight)) / math.PI;
                    s.Air += dt * math.max(above, 0.05f) / 3f;
                    s.Speed = b.RunSpeed;
                    s.Fly = 1f;
                    if (s.Air >= 1f)
                    {
                        s.Air = 0f;
                        s.Fly = 0f;
                        s.Timer = rng.NextFloat(30f, 80f);
                    }
                }
                else
                {
                    s.Speed = b.WalkSpeed;
                    s.Fly = 0f;
                    s.Timer -= dt;
                    // Somewhere flat to make for: the flattest of a few spots round its home.
                    if (math.distance(s.Position.xz, s.Target) < 4f || math.all(s.Target == float2.zero))
                    {
                        var best = -1f;
                        for (int k = 0; k < 6; k++)
                        {
                            var p = s.Home + rng.NextFloat2Direction() * rng.NextFloat(0.2f, 1f) * b.WanderRadius;
                            var g = Terrain.SampleGround(p);
                            if (g.HasWater && g.WaterSurface > g.Height || Obstructed(p, radius, out _))
                                continue;
                            var flat = Terrain.SampleNormal(p, 3f).y;
                            if (flat > best)
                            {
                                best = flat;
                                s.Target = p;
                            }
                        }
                    }
                    // Rock or a cliff ahead: it doesn't tunnel through. Close ahead of its head, it turns off to whichever
                    // side is clear and makes for somewhere that way. Further on, where its next arc would be up in the
                    // air, and due to breach soon anyway with the ground flat here: if the arc clears it there (the arc's
                    // height at that point over the ground's rise there and whatever rock or building stands on it, with a
                    // body's radius to spare) and comes down somewhere clear, it breaches now and leaps it.
                    // (Looked at four times a second, not every step: a rock lookup places every plant in a few cells.)
                    var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    var reach = math.max(radius * 6f, 6f);
                    var look = (int)(s.Clock * 4f) != (int)((s.Clock - dt) * 4f);
                    if (look && (Obstructed(s.Position.xz + forward * reach, radius, out _) || Obstructed(s.Position.xz + forward * reach * 0.5f, radius, out _)))
                    {
                        var side = rng.NextBool() ? 1f : -1f;
                        for (int k = 0; k < 4; k++)
                        {
                            var turn = side * (k < 2 ? 0.8f : 1.6f) * (k % 2 == 0 ? 1f : -1f);
                            var way = new float2(math.sin(s.Yaw + turn), math.cos(s.Yaw + turn));
                            if (!Obstructed(s.Position.xz + way * reach, radius, out _) && !Obstructed(s.Position.xz + way * reach * 2f, radius, out _))
                            {
                                s.Target = s.Position.xz + way * math.max(reach * 3f, 20f);
                                break;
                            }
                        }
                    }
                    else if (look && s.Timer > 0f && s.Timer < 15f && Terrain.SampleNormal(s.Position.xz, 3f).y > 0.97f)
                    {
                        // (The arc as Air will run it: its height above where it starts, and how far on each part of it
                        // comes; above the ground from out to in.)
                        var high = radius * 6f;
                        var above = 1f - 2f * math.asin(deep / (deep + high)) / math.PI;
                        var span = b.RunSpeed * 3f / math.max(above, 0.05f);
                        var obstacle = false;
                        var clears = true;
                        for (int k = 1; k < 8 && clears; k++)
                        {
                            var u = (1f - above) * 0.5f + above * k / 8f;
                            var p = s.Position.xz + forward * span * u;
                            if (!Obstructed(p, radius, out var standing))
                                continue;
                            obstacle = true;
                            var arc = (deep + high) * math.sin(math.PI * u) - deep;
                            var rise = Terrain.SampleHeight(p) - ground;
                            clears = arc > rise + standing + radius;
                        }
                        var lands = s.Position.xz + forward * span * (1f + above) * 0.5f;
                        if (obstacle && clears && !Obstructed(lands, radius, out _))
                            s.Timer = 0f;
                    }
                    // Its time come and the ground flat here (dunes, a snowfield): up it bursts. (Not yet: soon again.)
                    if (s.Timer <= 0f)
                    {
                        var here = Terrain.SampleGround(s.Position.xz);
                        if (!(here.HasWater && here.WaterSurface > here.Height) && Terrain.SampleNormal(s.Position.xz, 3f).y > 0.97f)
                        {
                            s.Air = 0.001f;
                            s.AirHeight = radius * 6f;
                        }
                        else
                            s.Timer = 3f;
                    }
                }
                // (Out of the ground it flies straight on: turning in the air, at a run, round a target close by
                // coiled its body flat in the air, and an arc that bends misses the obstacle it was meant to clear.)
                if (s.Air > 0f)
                    s.YawRate = 0f;
                else
                    TurnTowards(ref s, ref b, s.Target, dt);
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                ground = Terrain.SampleHeight(s.Position.xz);
                var under = s.Air > 0f ? deep : depth;
                s.Position.y = ground - under + (under + s.AirHeight) * (s.Air > 0f ? math.sin(math.PI * s.Air) : 0f);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>
            /// Something a worm under the ground won't go through at this point, within its own radius and a half: a cliff
            /// or steep bank (the ground's slope), a building or a landmark's rock (FloraPlacement.Blocked), or a boulder,
            /// tor or standing stone out in the wild (FloraPlacement.RockAt); and how tall what stands there is (m above
            /// its foot; 0 for a slope, whose rise the caller samples).
            /// </summary>
            bool Obstructed(float2 at, float radius, out float top)
            {
                top = 0f;
                var steep = Terrain.SampleNormal(at, 3f).y < 0.8f;
                if (!HasFlora)
                    return steep;
                var built = FloraPlacement.Blocked(ref Flora.Value, Terrain, at, radius * 1.5f, out var builtTop);
                var rock = FloraPlacement.RockAt(ref Flora.Value, Terrain, at, radius * 1.5f, out var rockTop);
                top = math.max(built ? builtTop : 0f, rock ? rockTop : 0f);
                return steep || built || rock;
            }

            /// <summary>Turns on the spot towards a point.</summary>
            static void TurnTowards(ref CreatureState s, ref CreatureBlob b, float2 at, float dt)
            {
                var to = at - s.Position.xz;
                var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                var turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = dt > 0f ? turn / dt : 0f;
            }

            /// <summary>Walks straight to a point at a steady pace, keeping its height (along a branch). Returns the distance left.</summary>
            static float GlideTo(ref CreatureState s, ref CreatureBlob b, float2 at, float speed, float dt)
            {
                var to = at - s.Position.xz;
                var distance = math.length(to);
                TurnTowards(ref s, ref b, at, dt);
                var facing = math.saturate(math.cos(WrapAngle(math.atan2(to.x, to.y) - s.Yaw)) * 1.5f);
                s.Speed = math.min(speed * facing, distance / math.max(dt, 1e-4f));
                CreatureMotion.Accelerate(ref s, ref b, dt);
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                return math.length(at - s.Position.xz);
            }

            // ------------------------------------------------------------------------------- webs

            /// <summary>Goes to its web, picking a spot for one first if it has none (false if there's nowhere).</summary>
            bool GoToWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (s.AnchorSize <= 0f && !PlaceWeb(ref s, ref b, ref rng))
                    return false;
                SetMode(ref s, CreatureMode.Web);
                s.Timer = rng.NextFloat(60f, 200f);
                return true;
            }

            /// <summary>
            /// A spot for a web, only ever on something big and solid (never a mushroom or a flower): best strung across
            /// the gap between two things 1.5–4 m apart (two trunks, a trunk and a boulder, two posts or a palisade's
            /// stakes, a cave passage's two walls), hung no higher than the lower of them; failing that, beside one tall
            /// trunk; failing that, none. From the place, like burrows: the same web site whenever a spider lives here.
            /// </summary>
            bool PlaceWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                var radius = b.WebSize * s.Scale;
                // (Each spider of a place its own: two don't share a web.)
                var place = LairRandom(s.Position.xz, 0xA11Eu ^ s.Individual);
                var cellCentre = (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                if (CaveWalker(s, ref b))
                    return CaveWeb(ref s, radius, cellCentre, ref place);
                if (!HasFlora)
                    return false;
                var anchors = new FixedList512Bytes<FloraPlacement.WebAnchor>();
                FloraPlacement.NearAnchors(ref Flora.Value, Terrain, cellCentre, 12f, 1.2f, ref anchors);

                // Every pair the right distance apart, one of them at random (the same one each time: place's draw).
                var found = 0;
                float3 bestCentre = default;
                float2 bestDir = default;
                var bestSpan = 0f;
                for (int i = 0; i < anchors.Length; i++)
                for (int j = i + 1; j < anchors.Length; j++)
                {
                    var a = anchors[i];
                    var c = anchors[j];
                    var dir = c.Base.xz - a.Base.xz;
                    var apart = math.length(dir);
                    dir /= math.max(apart, 1e-4f);
                    var gap = apart - a.Radius - c.Radius;
                    if (gap < math.max(1.5f, radius * 2.2f) || gap > 4f)
                        continue;
                    var mid = a.Base.xz + dir * (a.Radius + gap * 0.5f);
                    if (!Walkable(mid, Habitat.Land))
                        continue;
                    var ground = Terrain.SampleHeight(mid);
                    // Its top within both: hung from the lower one's top down, its foot clear of the ground.
                    var y = math.min(ground + radius + 0.4f, math.min(a.Top, c.Top) - 0.1f - radius);
                    if (y - radius < ground + 0.1f)
                        continue;
                    found++;
                    if (place.NextFloat() * found >= 1f)
                        continue;
                    bestCentre = new float3(mid.x, y, mid.y);
                    bestDir = dir;
                    bestSpan = gap * 0.5f;
                }
                if (found > 0)
                {
                    SetWeb(ref s, bestCentre, bestDir, bestSpan, place.NextBool());
                    return true;
                }

                // One tall trunk: the web beside it, its edge just off the bark.
                found = 0;
                var trunk = -1;
                for (int i = 0; i < anchors.Length; i++)
                    if (anchors[i].Trunk && anchors[i].Top - anchors[i].Base.y >= 2f && place.NextFloat() * ++found < 1f)
                        trunk = i;
                if (trunk < 0)
                    return false;
                var tree = anchors[trunk];
                var yaw = place.NextFloat(-math.PI, math.PI);
                var right = new float2(math.cos(yaw), -math.sin(yaw));
                // Trunk to the spider's left (side 1: centre to the trunk's right) or right (side 0).
                var toTheRight = place.NextBool();
                var centre = tree.Base.xz + right * (toTheRight ? 1f : -1f) * (tree.Radius + radius * 1.02f);
                if (!Walkable(centre, Habitat.Land))
                    return false;
                s.Anchor = new float3(centre.x, Terrain.SampleHeight(centre) + radius + 0.25f, centre.y);
                s.AnchorSize = radius;
                s.AnchorYaw = yaw;
                s.AnchorSide = (byte)(toTheRight ? 1 : 0);
                return true;
            }

            /// <summary>
            /// A web strung between two anchors (side 2): its centre, the way across (from one to the other), and how
            /// far each anchor's surface is from the centre (kept in AnchorSize, so every machine draws the threads
            /// out to them). The spider faces one way out of it or the other.
            /// </summary>
            static void SetWeb(ref CreatureState s, float3 centre, float2 across, float span, bool flip)
            {
                s.Anchor = centre;
                s.AnchorSize = span;
                // (The web's plane faces the spider's heading, its right along the gap.)
                s.AnchorYaw = WrapAngle(math.atan2(-across.y, across.x) + (flip ? math.PI : 0f));
                s.AnchorSide = 2;
            }

            /// <summary>
            /// A cave web: across a passage where its two walls are 1.5–5 m apart, a little above the floor and clear of
            /// the roof; failing that, beside the nearest wall (as beside a trunk). Spots tried round where the spider
            /// is (the same for the same few metres of cave, each time), on its level of the cave.
            /// </summary>
            bool CaveWeb(ref CreatureState s, float radius, float2 cellCentre, ref Random place)
            {
                if (!Terrain.Caves.IsCreated)
                    return false;
                var near = (math.floor(s.Position.xz / 4f) + 0.5f) * 4f;
                var wallCentre = float3.zero;
                var wallAcross = float3.zero;
                var byWall = false;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    var xz = near + place.NextFloat2Direction() * place.NextFloat(0f, 4f);
                    var floor = Terrain.FloorUnder(new float3(xz.x, s.Position.y + 0.5f, xz.y), out var inCave);
                    if (!inCave || math.abs(floor - s.Position.y) > 3f)
                        continue;
                    var centre = new float3(xz.x, floor + radius + 0.3f, xz.y);
                    if (Terrain.CaveDistance(centre) > -0.2f || Terrain.CaveDistance(centre + new float3(0f, radius * 0.9f, 0f)) > 0f)
                        continue;
                    // Across the passage: towards the nearest wall (the way the distance field rises fastest).
                    const float e = 0.2f;
                    var across = math.normalizesafe(new float3(
                        Terrain.CaveDistance(centre + new float3(e, 0f, 0f)) - Terrain.CaveDistance(centre - new float3(e, 0f, 0f)), 0f,
                        Terrain.CaveDistance(centre + new float3(0f, 0f, e)) - Terrain.CaveDistance(centre - new float3(0f, 0f, e))),
                        new float3(1f, 0f, 0f));
                    // Out to each wall.
                    float left = 0f, right = 0f;
                    for (; left < 4f && Terrain.CaveDistance(centre - across * left) < 0f; left += 0.1f) { }
                    for (; right < 4f && Terrain.CaveDistance(centre + across * right) < 0f; right += 0.1f) { }
                    var gap = left + right;
                    if (left >= 4f || right >= 4f || gap < math.max(1.5f, radius * 2.2f) || gap > 5f)
                    {
                        // (The first spot near a wall with room for the web beside it, in case there's no crossing.)
                        var beside = centre + across * (right - radius * 1.02f);
                        if (!byWall && right < 4f && right > 0.3f && Level(ref beside, radius))
                        {
                            byWall = true;
                            wallCentre = beside;
                            wallAcross = across;
                        }
                        continue;
                    }
                    var mid = centre + across * ((right - left) * 0.5f);
                    if (!Level(ref mid, radius))
                        continue;
                    SetWeb(ref s, mid, across.xz, gap * 0.5f, place.NextBool());
                    return true;
                }
                if (!byWall)
                    return false;
                // Beside the wall, the wall to the spider's right (side 0), as a web beside a trunk is drawn.
                s.Anchor = wallCentre;
                s.AnchorSize = radius;
                s.AnchorYaw = math.atan2(-wallAcross.z, wallAcross.x);
                s.AnchorSide = 0;
                return true;
            }

            /// <summary>
            /// A cave web's centre put at its height over the floor right under it (the floor rises towards the walls),
            /// if there's room there for the web: clear of the rock round it and of the roof.
            /// </summary>
            bool Level(ref float3 centre, float radius)
            {
                var floor = Terrain.FloorUnder(centre + new float3(0f, 0.5f, 0f), out var inCave);
                centre.y = floor + radius + 0.3f;
                return inCave && Terrain.CaveDistance(centre) < -0.2f && Terrain.CaveDistance(centre + new float3(0f, radius * 0.9f, 0f)) < 0f;
            }

            /// <summary>
            /// Under its web (0), up a thread (1), then waiting in it head down, facing out of the web (Stage 2–4 says
            /// where the tree is; see CreatureMode.Web); down again when it's time to move on (5).
            /// </summary>
            void InWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk,
                       ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                var climbSpeed = walk * 0.6f;
                if (s.Stage == 0)
                {
                    s.Target = s.Anchor.xz;
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Anchor.xz) < 0.2f)
                        NextStage(ref s);
                    else if (s.ModeTime > 40f)
                    {
                        // Can't get there: forget it and find another spot next time.
                        s.AnchorSize = 0f;
                        Pause(ref s, ref b, ref rng);
                    }
                    return;
                }
                custom = true;
                if (s.Stage == 1)
                {
                    vertical = 1f;
                    s.Position.xz = s.Anchor.xz;
                    s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                    s.Position.y = math.min(s.Position.y + s.Speed * dt, s.Anchor.y);
                    if (s.Position.y >= s.Anchor.y - 0.01f)
                    {
                        s.Stage = (byte)(2 + s.AnchorSide);
                        s.StageTime = 0f;
                    }
                }
                else if (s.Stage <= 4)
                {
                    vertical = -1f;
                    s.Speed = 0f;
                    s.Position = s.Anchor;
                    var wanted = WrapAngle(s.AnchorYaw - s.Yaw);
                    var turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                    s.Yaw = WrapAngle(s.Yaw + turn);
                    s.YawRate = turn / math.max(dt, 1e-4f);
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Stage = 5;
                        s.StageTime = 0f;
                    }
                }
                else
                {
                    vertical = 1f;
                    s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                    // (Down to whatever is under the web: the cave floor in a cave.)
                    var floor = Terrain.FloorUnder(s.Position + new float3(0f, 0.1f, 0f), out _);
                    s.Position.y = math.max(s.Position.y - s.Speed * dt, floor);
                    if (s.Position.y <= floor + 0.01f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- burrows and nests

            static Random LairRandom(float2 near, uint kind) => new Random(math.hash(new int3((int2)math.floor(near / 16f), (int)kind)) | 1u);

            /// <summary>
            /// Picks its nest, from the place like a burrow (the same nest whenever a bird lives here): a cup against a
            /// trunk low in a tree's crown, a platform of sticks on a treetop, or a scrape on flat ground (near home, for
            /// residents).
            /// </summary>
            bool FindNest(ref CreatureState s, ref CreatureBlob b)
            {
                var near = s.Leash > 0f ? s.Home : (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                var place = LairRandom(near, 0x4E57u ^ (s.Leash > 0f ? s.Individual : 0u));
                var size = math.max(b.BodyLength * s.Scale * 0.7f, 0.08f);
                if (b.Nests == NestSite.Ledge)
                {
                    // The Nest anchor nearest home (a cave mouth's wall): the colony's cups spread along the wall either
                    // side of it and a little up and down, each standing out from the rock.
                    if (!Terrain.Anchors.IsCreated)
                        return false;
                    var ledges = new NativeList<int>(8, Allocator.Temp);
                    Anchors.Near(ref Terrain.Anchors.Value, new float3(near.x, s.Position.y, near.y), 60f, AnchorKind.Nest, ref ledges);
                    var found = -1;
                    // (Not an egg clutch on a cave floor, which the world marks as a Nest too: those are Size 0.6, a
                    // ledge 0.4.)
                    for (int k = 0; k < ledges.Length; k++)
                        if (Terrain.Anchors.Value.Anchors[ledges[k]].Size > 0.5f)
                            continue;
                        else if (found < 0 || math.distancesq(Terrain.Anchors.Value.Anchors[ledges[k]].Position.xz, near) <
                                         math.distancesq(Terrain.Anchors.Value.Anchors[found].Position.xz, near))
                            found = ledges[k];
                    ledges.Dispose();
                    if (found >= 0)
                    {
                        var ledge = Terrain.Anchors.Value.Anchors[found];
                        var along = new float2(-ledge.Facing.y, ledge.Facing.x);
                        var spot = ledge.Position.xz + along * place.NextFloat(-1.4f, 1.4f);
                        var cup = new float3(spot.x, ledge.Position.y + place.NextFloat(-0.4f, 0.6f), spot.y);
                        // Onto the rock itself (the wall curves away from the anchor, or out into the way): in to the
                        // wall from there, or out of it, the cup just proud of the surface; failing that, at the anchor.
                        var into = new float3(-ledge.Facing.x, 0f, -ledge.Facing.y);
                        var inRock = Terrain.CaveDistance(cup) >= 0f;
                        var onRock = false;
                        for (int k = 1; k <= 25 && !onRock; k++)
                        {
                            var probe = cup + into * ((inRock ? -0.1f : 0.1f) * k);
                            if (Terrain.CaveDistance(probe) >= 0f != inRock)
                            {
                                // The surface lies between this probe and the one before: halved down to a few
                                // millimetres (air on one side, rock on the other).
                                var air = inRock ? probe : probe - into * 0.1f;
                                var rock = inRock ? probe - into * -0.1f : probe;
                                for (int h = 0; h < 5; h++)
                                {
                                    var mid = (air + rock) * 0.5f;
                                    if (Terrain.CaveDistance(mid) >= 0f)
                                        rock = mid;
                                    else
                                        air = mid;
                                }
                                cup = air - into * size * 0.6f;
                                onRock = true;
                            }
                        }
                        if (!onRock)
                            cup = ledge.Position + into * (0.3f - size * 0.6f);
                        s.Anchor = cup;
                        s.AnchorSize = size;
                        return true;
                    }
                }
                else if (b.Nests == NestSite.Ground)
                {
                    // A town bird's (a hen's): against a house wall beside its door, not out on the square.
                    if (s.Leash > 0f && Terrain.Anchors.IsCreated)
                    {
                        var doors = new NativeList<int>(8, Allocator.Temp);
                        Anchors.Near(ref Terrain.Anchors.Value, new float3(near.x, s.Position.y, near.y), math.max(s.Leash, 20f), AnchorKind.Door, ref doors);
                        var placed = false;
                        for (int attempt = 0; attempt < 6 && doors.Length > 0 && !placed; attempt++)
                        {
                            var door = Terrain.Anchors.Value.Anchors[doors[place.NextInt(doors.Length)]];
                            if (door.InCave)
                                continue;
                            var across = new float2(-door.Facing.y, door.Facing.x) * (place.NextBool() ? 1f : -1f);
                            var spot = door.Position.xz + across * place.NextFloat(1.2f, 2.2f) - door.Facing * 0.3f;
                            if (!Walkable(spot, Habitat.Land))
                                continue;
                            s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                            s.AnchorSize = size;
                            placed = true;
                        }
                        doors.Dispose();
                        if (placed)
                            return true;
                    }
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        var spot = near + place.NextFloat2Direction() * place.NextFloat(1f, 6f);
                        if (!Walkable(spot, Habitat.Land) || SlopeAhead(spot, spot + new float2(0.5f, 0f)) > 0.35f ||
                            SlopeAhead(spot, spot + new float2(0f, 0.5f)) > 0.35f)
                            continue;
                        s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                        s.AnchorSize = size;
                        return true;
                    }
                }
                else if (HasFlora && FloraPlacement.FindTree(ref Flora.Value, Terrain, near, 30f, b.Nests == NestSite.Treetop ? 6f : 3f, ref place, out var tree))
                {
                    if (b.Nests == NestSite.Treetop)
                    {
                        // Big and untidy, on the very top of the crown (in plain view, not inside the leaves).
                        s.AnchorSize = size * 1.4f;
                        // (A broadleaf's leaf clumps bulge about a third of the crown's radius above its height; a
                        // conifer ends in a spike at its height, the nest settled on it.)
                        var top = tree.Kind == TreeKind.Broadleaf ? tree.Height + tree.CrownRadius * 0.3f : tree.Height;
                        s.Anchor = tree.Base + new float3(0f, top - s.AnchorSize * 0.15f, 0f);
                    }
                    else
                    {
                        var dir = place.NextFloat2Direction();
                        var at = tree.Base.xz + dir * (tree.TrunkRadius + size * 0.8f);
                        s.Anchor = new float3(at.x, tree.Base.y + tree.CrownBase + (tree.Height - tree.CrownBase) * 0.25f, at.y);
                        s.AnchorSize = size;
                    }
                    return true;
                }
                s.AnchorSize = -1f;   // nowhere for one here: don't keep trying
                return false;
            }

            /// <summary>
            /// A moth's life: round and round a light. Its light is a Glow anchor (a glowing plant) near where it first
            /// looks, kept in Anchor; it flits to a point near it, and on getting there, to another.
            /// </summary>
            void Flit(ref CreatureState s, ref Random rng)
            {
                if (s.AnchorSize == 0f)
                {
                    if (Terrain.Anchors.IsCreated && Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 25f, AnchorKind.Glow, ref rng, out var glow))
                        s.Anchor = glow.Position + new float3(0f, 0.5f, 0f);
                    else
                        s.Anchor = s.Position + new float3(0f, 1f, 0f);   // no light: it flits about where it is
                    s.AnchorSize = -1f;   // (not a lair to draw)
                }
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = s.Anchor.xz + rng.NextFloat2Direction() * rng.NextFloat(0.15f, 1.1f);
                s.TargetHeight = s.Anchor.y + rng.NextFloat(-0.3f, 1.2f);
            }

            /// <summary>
            /// A moth's flight: straight at its point in three dimensions at its own pace, fluttering (no glide, no
            /// cruising height, no landing), turning to face the way it goes. True once it's there.
            /// </summary>
            bool FlitTo(ref CreatureState s, ref CreatureBlob b, float dt, ref float vertical)
            {
                var to = new float3(s.Target.x, s.TargetHeight, s.Target.y) - s.Position;
                var distance = math.length(to);
                s.Position += to * math.min(1f, b.FlySpeed * dt / math.max(distance, 1e-4f));
                var wanted = distance < 0.02f ? 0f : WrapAngle(math.atan2(to.x, to.z) - s.Yaw);
                var turn = math.clamp(wanted, -b.TurnRate * 1.5f * dt, b.TurnRate * 1.5f * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                s.Speed = b.FlySpeed;
                vertical = math.clamp(to.y / math.max(distance, 1e-3f), -0.5f, 0.5f) * 0.5f;
                return distance < 0.05f;
            }

            /// <summary>Picks a burrow mouth on flat, dry ground a few metres off (near home, for residents).</summary>
            bool DigBurrow(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                // Lairs come from the place (a 16 m cell of the world, round its centre, or a resident's home), so an
                // animal that despawns and comes back finds the same burrow, with nothing saved. Residents sharing a
                // home each get their own.
                var near = s.Leash > 0f ? s.Home : (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                var place = LairRandom(near, 0x5B0Bu ^ (s.Leash > 0f ? s.Individual : 0u));
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    var spot = near + place.NextFloat2Direction() * place.NextFloat(1.5f, 6f);
                    if (!Walkable(spot, Habitat.Land) || SlopeAhead(spot, spot + new float2(0.5f, 0f)) > 0.35f ||
                        SlopeAhead(spot, spot + new float2(0f, 0.5f)) > 0.35f)
                        continue;
                    s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                    s.AnchorSize = math.max(b.BodyLength * s.Scale * 0.8f, 0.18f);
                    return true;
                }
                // No room here: don't keep trying in this place (Anchor remembers which), but a wild one tries again once
                // it has moved on to another cell.
                s.AnchorSize = -1f;
                s.Anchor = new float3(near.x, 0f, near.y);
                return false;
            }

            /// <summary>
            /// To the burrow's mouth (running, if startled), down it, hidden a while (longer while the player is close),
            /// then up again to sit up and look about.
            /// </summary>
            void GoToGround(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float playerDistance,
                            ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                var mouth = s.Anchor;
                var down = mouth.y - (b.StandHeight + b.BodyLength) * s.Scale - 0.1f;
                switch (s.Stage)
                {
                    case 0:
                        s.Target = mouth.xz;
                        targetSpeed = s.ModeTime < 6f && s.Timer < 30f ? run : walk;
                        moving = true;
                        if (math.distance(s.Position.xz, mouth.xz) < 0.25f)
                            NextStage(ref s);
                        else if (s.ModeTime > 30f)
                            Pause(ref s, ref b, ref rng);
                        break;
                    case 1:
                        custom = true;
                        vertical = -0.7f;
                        s.Speed = 0f;
                        s.Position.xz = mouth.xz;
                        s.Position.y = math.max(s.Position.y - dt * 1.2f, down);
                        if (s.Position.y <= down + 0.01f)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Speed = 0f;
                        s.Position.y = down;
                        // Waits out danger: while the player is near, it stays down.
                        if (playerDistance > b.FleeDistance * s.Scale * 1.5f)
                            s.Timer -= dt;
                        if (s.Timer <= 0f)
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        vertical = 0.6f;
                        s.Speed = 0f;
                        s.Position.y = math.min(s.Position.y + dt * 0.8f, mouth.y);
                        if (s.Position.y >= mouth.y - 0.01f)
                        {
                            // Out, and straight up on its hind legs to look about (if it does that).
                            Pause(ref s, ref b, ref rng);
                            if ((b.Idles & IdleActions.SitUp) != 0)
                            {
                                s.Action = IdleAction.SitUp;
                                s.ActionTime = 0f;
                                s.ActionLength = rng.NextFloat(3f, 6f);
                            }
                        }
                        break;
                }
            }

            // ------------------------------------------------------------------------------- taking their ease

            /// <summary>
            /// Somewhere to sit or lie a while near home. Sitting: on a seat (a bench, a log, a stump, a fountain's rim,
            /// a hall's step: Sit anchors), or on the grass of a green, or on the ground by a well or fountain; mostly a
            /// seat, if there's one free. Lying back: on a green, or failing that the ground by a well. Each takes a slot
            /// of its own (by individual, the next along if that's taken), so two never sit in one another's lap:
            /// <list type="bullet">
            /// <item>a seat: one sitter to each place along it;</item>
            /// <item>a green: round its shade tree, a few metres out, facing out from it;</item>
            /// <item>a well: sitting in a row before it, facing it, an arm's length clear of the wall; lying in a second
            /// row further out, feet to it.</item>
            /// </list>
            /// False if there's nowhere in range, or no free slot.
            /// </summary>
            bool StartLounge(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                var lying = rng.NextFloat() < 0.4f;
                var roll = rng.NextFloat();
                // The order it looks in: lying, a green then a well; sitting, mostly a seat first.
                var order = lying ? new int3(1, 2, -1) : roll < 0.55f ? new int3(0, 1, 2) : roll < 0.8f ? new int3(1, 0, 2) : new int3(2, 0, 1);
                for (int o = 0; o < 3; o++)
                {
                    var kind = order[o] == 0 ? AnchorKind.Sit : order[o] == 1 ? AnchorKind.Green : order[o] == 2 ? AnchorKind.Well : (AnchorKind)255;
                    if ((byte)kind != 255 && LoungeAt(self, ref s, ref rng, kind, lying))
                        return true;
                }
                return false;
            }

            /// <summary>A free slot at one of the anchors of a kind near home (a few tried, at random), taken.</summary>
            bool LoungeAt(Entity self, ref CreatureState s, ref Random rng, AnchorKind kind, bool lying)
            {
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f), kind, ref near);
                var found = false;
                for (int tries = 0; tries < 3 && near.Length > 0 && !found; tries++)
                {
                    var pick = rng.NextInt(near.Length);
                    var a = set.Anchors[near[pick]];
                    near.RemoveAtSwapBack(pick);
                    if (a.InCave)
                        continue;
                    var across = new float2(-a.Facing.y, a.Facing.x);
                    var slots = kind == AnchorKind.Sit ? math.max((int)a.Capacity, 1) : kind == AnchorKind.Green ? math.max((int)a.Capacity, 4) : lying ? 4 : 5;
                    var room = kind == AnchorKind.Sit ? math.max(a.Size * 0.8f, 0.3f) : lying ? 1.6f : 0.9f;
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        float2 spot, face;
                        if (kind == AnchorKind.Sit)
                        {
                            spot = a.Position.xz + across * ((k - (slots - 1) * 0.5f) * a.Size);
                            face = spot + a.Facing * 3f;
                        }
                        else if (kind == AnchorKind.Green)
                        {
                            math.sincos(k * 2f * math.PI / slots + (a.Owner & 7) * 0.4f, out var sin, out var cos);
                            var outward = new float2(sin, cos);
                            spot = a.Position.xz + outward * math.max(a.Size * 0.6f, 2.5f);
                            face = spot + outward * 3f;
                        }
                        else
                        {
                            spot = lying ? a.Position.xz + across * ((k - 1.5f) * 2f) - a.Facing * 4f
                                         : a.Position.xz + across * ((k - 2) * 0.95f) - a.Facing * 1f;
                            face = spot + a.Facing * 3f;
                        }
                        if ((kind != AnchorKind.Sit && !Walkable(spot, Habitat.Land)) || Taken(self, spot, room))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = face;
                        // (On a seat: its height goes in Anchor, which every machine has, for the posture.)
                        s.TargetHeight = kind == AnchorKind.Sit ? 4f : lying ? 3f : 1f;
                        if (kind == AnchorKind.Sit)
                            s.Anchor = new float3(spot.x, a.Position.y, spot.y);
                        s.Timer = lying ? rng.NextFloat(80f, 200f) : rng.NextFloat(40f, 120f);
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>
            /// Whether this one would be at the town's fire now: townsfolk (not the watch or the guards, nor a traveller
            /// still on the road), from sunset until, by individual, a quarter to ten to ten past.
            /// </summary>
            bool Gathering(in CreatureState s, ref CreatureBlob b) =>
                b.Upright && b.Hands && s.Leash > 0f && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Watch &&
                s.Task != CreatureTask.Travel && Hour >= 17.8f && Hour < 21.95f + 0.2f * CreatureBlob.Trait(s.Individual, 41);

            static bool AtHearth(float stage) => stage >= CreatureAnimator.HearthStand && stage <= CreatureAnimator.HearthSeat;

            /// <summary>
            /// A place at the town's fire (its Hearth anchor) taken: round the ring, a free one of its places (Capacity of
            /// them, by individual and the next along if that's taken), sitting on the ground (legs out or cross-legged)
            /// or standing, facing the fire; or now and then a seat close by that faces it. It stays till it's time to go
            /// in (Gathering). False if there's no fire in reach, or no room at it.
            /// </summary>
            bool StartHearth(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(4, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f) + 20f, AnchorKind.Hearth, ref near);
                var found = false;
                var stand = rng.NextFloat() < 0.4f;
                var seat = rng.NextFloat() < 0.3f;
                for (int h = 0; h < near.Length && !found; h++)
                {
                    var a = set.Anchors[near[h]];
                    if (a.InCave)
                        continue;
                    var fire = a.Position.xz;
                    if (seat && HearthSeat(self, ref s, ref rng, fire, a.Size))
                    {
                        found = true;
                        break;
                    }
                    var slots = math.max((int)a.Capacity, 1);
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        math.sincos(k * 2f * math.PI / slots + (a.Owner & 7) * 0.4f, out var sin, out var cos);
                        var spot = fire + new float2(sin, cos) * (a.Size + (stand ? 0.1f : 0.25f));
                        if (!Walkable(spot, Habitat.Land) || Taken(self, spot, 0.9f))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = fire;
                        s.TargetHeight = stand ? CreatureAnimator.HearthStand : CreatureAnimator.HearthGround;
                        s.Timer = 9999f;   // (till it's time to go in)
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>A free place on a seat (bench, log, stump) near the fire and facing it, taken.</summary>
            bool HearthSeat(Entity self, ref CreatureState s, ref Random rng, float2 fire, float ring)
            {
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(fire.x, s.Position.y, fire.y), ring + 6f, AnchorKind.Sit, ref near);
                var found = false;
                for (int n = 0; n < near.Length && !found; n++)
                {
                    var a = set.Anchors[near[n]];
                    if (a.InCave || math.dot(a.Facing, math.normalizesafe(fire - a.Position.xz)) < 0.3f)
                        continue;
                    var across = new float2(-a.Facing.y, a.Facing.x);
                    var slots = math.max((int)a.Capacity, 1);
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        var spot = a.Position.xz + across * ((k - (slots - 1) * 0.5f) * a.Size);
                        if (Taken(self, spot, math.max(a.Size * 0.8f, 0.3f)))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = spot + a.Facing * 3f;
                        s.TargetHeight = CreatureAnimator.HearthSeat;
                        s.Anchor = new float3(spot.x, a.Position.y, spot.y);
                        s.Timer = 9999f;
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>Another place of the same kind, for one beaten to its spot: at the fire, in the play ring, or to lounge.</summary>
            bool Resettle(Entity self, ref CreatureState s, ref Random rng) =>
                AtHearth(s.TargetHeight) ? StartHearth(self, ref s, ref rng) :
                s.TargetHeight == CreatureAnimator.PlayRing ? StartPlayRing(self, ref s, ref rng) : StartLounge(self, ref s, ref rng);

            /// <summary>
            /// A place in the children's play ring: on the green nearest its home (the same green for every child of the
            /// town, so they find each other there), to one side of its shade tree, six places round, sitting facing the
            /// middle. A while there, poking at the ground with its stick or tossing its stone into the middle (see the
            /// child's play above). False if there's no green in reach or the ring is full.
            /// </summary>
            bool StartPlayRing(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f), AnchorKind.Green, ref near);
                var best = -1;
                for (int i = 0; i < near.Length; i++)
                    if (!set.Anchors[near[i]].InCave && (best < 0 || math.distancesq(set.Anchors[near[i]].Position.xz, s.Home) <
                                                                     math.distancesq(set.Anchors[best].Position.xz, s.Home)))
                        best = near[i];
                near.Dispose();
                if (best < 0)
                    return false;
                var a = set.Anchors[best];
                var centre = a.Position.xz + new float2(-a.Facing.y, a.Facing.x) * math.max(a.Size * 0.5f, 3f);
                const int places = 6;
                var first = (int)(s.Individual % places);
                for (int i = 0; i < places; i++)
                {
                    var k = (first + i) % places;
                    math.sincos(k * 2f * math.PI / places + (a.Owner & 7) * 0.5f, out var sin, out var cos);
                    var spot = centre + new float2(sin, cos) * 0.85f;
                    if (!Walkable(spot, Habitat.Land) || Taken(self, spot, 0.55f))
                        continue;
                    SetMode(ref s, CreatureMode.Sit);
                    s.Target = spot;
                    s.Focus = centre;
                    s.TargetHeight = CreatureAnimator.PlayRing;
                    s.Timer = rng.NextFloat(40f, 100f);
                    return true;
                }
                return false;
            }

            /// <summary>Another on its way to the same spot to sit or lie, that took it first (the lower entity index).</summary>
            bool Contested(Entity self, in CreatureState s)
            {
                var c = Cell(s.Target);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j].Index < self.Index && States[j].Mode == CreatureMode.Sit && math.distance(States[j].Target, s.Target) < 0.9f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Someone else there already, or on their way to sit or lie there (within room of it).</summary>
            bool Taken(Entity self, float2 spot, float room)
            {
                var c = Cell(spot);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self)
                            continue;
                        var other = States[j];
                        if (math.distance(other.Position.xz, spot) < room ||
                            other.Mode == CreatureMode.Sit && math.distance(other.Target, spot) < room)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }
        }
    }
}
