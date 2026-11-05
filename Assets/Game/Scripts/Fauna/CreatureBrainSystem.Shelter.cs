using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        /// <summary>
        /// A-71 row 2 counters (Interlocked from the brain's workers): searches this frame (capped, reset each frame),
        /// and running totals of shelters set off for, arrived at and searches that found none.
        /// </summary>
        public static readonly SharedStatic<ShelterCounts> Shelters = SharedStatic<ShelterCounts>.GetOrCreate<ShelterCountsKey>();
        struct ShelterCountsKey { }
        public struct ShelterCounts { public int ThisFrame; public long Started, Arrived, NotFound, Indoors; }
        /// <summary>At most this many shelter searches a frame, over every creature (each looks up trees, anchors, caves).</summary>
        const int ShelterSearchesPerFrame = 8;

        // BrainJob, part: shelter seeking out of the weather (A-71 row 2). Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            /// <summary>
            /// Something uncomfortable enough to look for shelter: the nearest shelter of a kind that helps with its
            /// complaint and that it likes (a tree's foot, a door's eaves or indoors, a well or the shore, a fire, a cave
            /// mouth). Fliers just go to roost or perch. False if there's none in reach (or it's no kind it seeks).
            /// </summary>
            bool StartShelter(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                var wanted = Comfort.Helps(M.DiscomfortKind) & b.Comfort.Seeks;
                if (wanted == ShelterNeed.None)
                    return false;
                if (b.Habitat == Habitat.Flies)
                {
                    if ((wanted & (ShelterNeed.Tree | ShelterNeed.Cave | ShelterNeed.Roof)) == 0)
                        return false;
                    if (b.Roosts)
                        StartRoost(ref s, ref b, ref rng);
                    else if (!StartFlight(ref s, ref b, ref rng, true))
                        return false;
                    return true;
                }

                var at = s.Position.xz;
                var best = float.MaxValue;
                var kind = ShelterNeed.None;
                float2 target = default, focus = default;
                var indoors = false;

                // A door: in, or under its eaves facing out (eaves shade too).
                if ((wanted & (ShelterNeed.Roof | ShelterNeed.Shade)) != 0 && b.Comfort.Race != ComfortRace.None)
                {
                    var anchor = s.Anchor;
                    var anchorYaw = s.AnchorYaw;
                    if (FindDoor(ref s))
                    {
                        var outward = new float2(math.sin(s.AnchorYaw), math.cos(s.AnchorYaw));
                        var spot = s.Anchor.xz + outward * 0.6f + new float2(-outward.y, outward.x) * rng.NextFloat(-0.8f, 0.8f);
                        var d = math.distance(at, spot);
                        if (d < best)
                        {
                            best = d;
                            target = spot;
                            focus = spot + outward * 5f;
                            kind = (wanted & ShelterNeed.Roof) != 0 ? ShelterNeed.Roof : ShelterNeed.Shade;
                            indoors = (wanted & ShelterNeed.Roof) != 0 &&
                                      (M.DiscomfortKind == ComfortKind.Storm || M.DiscomfortKind == ComfortKind.Cold || rng.NextFloat() < 0.5f);
                        }
                    }
                    if (!indoors)
                    {
                        s.Anchor = anchor;
                        s.AnchorYaw = anchorYaw;
                    }
                }
                // A tree: at its foot, on the side it came from, looking out.
                if ((wanted & (ShelterNeed.Tree | ShelterNeed.Shade)) != 0 && HasFlora && !b.Burrows &&
                    FloraPlacement.FindTree(ref Flora.Value, Terrain, at, 30f, 3f, ref rng, out var tree))
                {
                    var side = math.normalizesafe(at - tree.Base.xz, new float2(0f, 1f));
                    var spot = tree.Base.xz + side * (math.max(tree.TrunkRadius, 0.05f) + 0.6f);
                    var d = math.distance(at, spot);
                    if (d < best)
                    {
                        best = d;
                        target = spot;
                        focus = spot + side * 5f;
                        kind = ShelterNeed.Tree;
                        indoors = false;
                    }
                }
                // Water: a well or pool, else the shore.
                if ((wanted & ShelterNeed.Water) != 0)
                {
                    var water = NearestAnchor(AnchorKind.Well, at, 40f);
                    if (water < 0)
                        water = NearestAnchor(AnchorKind.Pool, at, 40f);
                    float2 spot, toward;
                    var found = false;
                    if (water >= 0)
                    {
                        ref var set = ref Terrain.Anchors.Value;
                        toward = set.Anchors[water].Position.xz;
                        spot = toward + math.normalizesafe(at - toward, new float2(0f, 1f)) * 1.4f;
                        found = true;
                    }
                    else
                        found = FindWater(ref s, ref b, ref rng, out spot, out toward);
                    if (found)
                    {
                        var d = math.distance(at, spot);
                        if (d < best)
                        {
                            best = d;
                            target = spot;
                            focus = toward;
                            kind = ShelterNeed.Water;
                            indoors = false;
                        }
                    }
                }
                // A fire: a hearth or a camp's fire, a step back from it.
                if ((wanted & ShelterNeed.Fire) != 0)
                {
                    var fire = NearestAnchor(AnchorKind.Hearth, at, 40f);
                    if (fire < 0)
                        fire = NearestAnchor(AnchorKind.Camp, at, 40f);
                    if (fire >= 0)
                    {
                        ref var set = ref Terrain.Anchors.Value;
                        var flame = set.Anchors[fire].Position.xz;
                        var spot = flame + math.normalizesafe(at - flame, new float2(0f, 1f)) * rng.NextFloat(1.4f, 2f);
                        // (Cold, a fire is worth twice the walk of a door or a cave.)
                        var d = math.distance(at, spot) * (M.DiscomfortKind == ComfortKind.Cold ? 0.5f : 1f);
                        if (d < best)
                        {
                            best = d;
                            target = spot;
                            focus = flame;
                            kind = ShelterNeed.Fire;
                            indoors = false;
                        }
                    }
                }
                // A cave: just inside its mouth, looking out.
                if ((wanted & ShelterNeed.Cave) != 0 && Terrain.Caves.IsCreated)
                {
                    ref var set = ref Terrain.Caves.Value;
                    for (int c = 0; c < set.Caves.Length; c++)
                    {
                        ref var cave = ref set.Caves[c];
                        var mouth = cave.Mouth.xz;
                        if (cave.NodeCount <= 0 || math.distancesq(mouth, at) > 60f * 60f)
                            continue;
                        var inward = math.normalizesafe(set.Nodes[cave.FirstNode].Position.xz - mouth, new float2(0f, 1f));
                        var spot = mouth + inward * 1.5f;
                        var d = math.distance(at, spot);
                        if (d < best)
                        {
                            best = d;
                            target = spot;
                            focus = mouth - inward * 5f;
                            kind = ShelterNeed.Cave;
                            indoors = false;
                        }
                    }
                }

                if (kind == ShelterNeed.None)
                    return false;
                System.Threading.Interlocked.Increment(ref Shelters.Data.Started);
                if (indoors)
                {
                    System.Threading.Interlocked.Increment(ref Shelters.Data.Indoors);
                    SetMode(ref s, CreatureMode.Home);
                    s.Timer = rng.NextFloat(30f, 90f);
                    return true;
                }
                SetMode(ref s, CreatureMode.Shelter);
                s.Target = target;
                s.Focus = focus;
                s.TargetHeight = (float)kind;
                s.Timer = rng.NextFloat(90f, 240f);
                return true;
            }

            /// <summary>
            /// CreatureMode.Shelter: to its shelter (walking, or hurrying when the weather is bad), then waiting there,
            /// turned to look out (animals lie down), till the weather lets up (ComfortCheck sends it on) or its time is up.
            /// A shelter it can't reach in a minute, it gives up on.
            /// </summary>
            void Sheltering(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float idleLook,
                            ref float targetSpeed, ref bool moving, ref float lie, ref float desiredLook)
            {
                if (s.Stage == 0)
                {
                    targetSpeed = M.ComfortPressure > 0.6f ? math.lerp(walk, run, 0.6f) : walk;
                    moving = true;
                    if (s.StageTime > 60f)
                    {
                        Pause(ref s, ref b, ref rng);
                        M.ShelterRetry = rng.NextFloat(20f, 40f);
                    }
                    else if (math.distance(s.Position.xz, s.Target) < 0.6f)
                    {
                        NextStage(ref s);
                        System.Threading.Interlocked.Increment(ref Shelters.Data.Arrived);
                    }
                    return;
                }
                s.Timer -= dt;
                var short_ = s.Target - s.Position.xz;
                s.Position.xz += math.normalizesafe(short_) * math.min(math.length(short_), 0.5f * dt);
                var toFace = s.Focus - s.Position.xz;
                var wantedYaw = WrapAngle(math.atan2(toFace.x, toFace.y) - s.Yaw);
                s.Yaw = WrapAngle(s.Yaw + math.clamp(wantedYaw, -b.TurnRate * dt, b.TurnRate * dt));
                // (Animals settle down to wait it out; folk stand under it, looking about.)
                if (b.Comfort.Race == ComfortRace.None && s.StageTime > 3f)
                    lie = 1f;
                desiredLook = idleLook;
                if (s.Timer <= 0f)
                {
                    Pause(ref s, ref b, ref rng);
                    M.ShelterRetry = rng.NextFloat(30f, 60f);
                }
            }

            /// <summary>The shelter it is in right now, as comfort counts it: its chosen one once there, indoors, down a
            /// burrow, roosting, up a tree or perched.</summary>
            ShelterNeed CoverNow(in CreatureState s, ref CreatureBlob b)
            {
                switch (s.Mode)
                {
                    case CreatureMode.Shelter:
                        return s.Stage >= 1 ? (ShelterNeed)(byte)s.TargetHeight : ShelterNeed.None;
                    case CreatureMode.Home:
                        return s.Stage == 2 ? ShelterNeed.Roof : ShelterNeed.None;
                    case CreatureMode.Burrow:
                        return s.Stage >= 2 ? ShelterNeed.Cave : ShelterNeed.None;
                    case CreatureMode.Roost:
                        return underRock ? ShelterNeed.Cave : ShelterNeed.Tree;
                    case CreatureMode.Climb:
                        return s.Stage >= 1 ? ShelterNeed.Tree : ShelterNeed.None;
                }
                if (b.Habitat == Habitat.Flies && !Flying(s) && s.Position.y > Terrain.SampleHeight(s.Position.xz) + 0.3f)
                    return ShelterNeed.Tree;
                return ShelterNeed.None;
            }

            /// <summary>Whether it may break off what it's doing to look for shelter: about its own business (or at
            /// work, once fed up with the weather), not holding, a cave or rock dweller (the sky can't reach those),
            /// rooted, disguised or haunting.</summary>
            bool MaySeekShelter(in CreatureState s, ref CreatureBlob b) =>
                (Free(s.Mode) || s.Mode == CreatureMode.Sit && s.Stage == 0 || s.Mode == CreatureMode.Work && M.Discomfort > 0.55f) &&
                !CaveWalker(in s, ref b) &&
                !b.Rooted && !b.Disguised && !b.Haunts;
        }
    }
}
